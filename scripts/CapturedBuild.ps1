# Trusted local build-input capture. No build/deployment occurs on import.
. (Join-Path $PSScriptRoot 'AiCandidatePackage.ps1')
if(-not ('NcmaCapturedBuildPins' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public sealed class NcmaCapturedBuildPins : IDisposable {
 readonly Dictionary<string,SafeFileHandle> dirs=new Dictionary<string,SafeFileHandle>(StringComparer.OrdinalIgnoreCase);
 readonly List<FileStream> files=new List<FileStream>();
 [StructLayout(LayoutKind.Sequential)] struct Info { public uint Attributes,CreationLow,CreationHigh,AccessLow,AccessHigh,WriteLow,WriteHigh,Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow; }
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFileW(string p,uint access,uint share,IntPtr sec,uint create,uint flags,IntPtr template);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetFileInformationByHandle(SafeFileHandle h,out Info info);
 public void Directory(string path) {
  path=Path.GetFullPath(path); if(dirs.ContainsKey(path))return;
  string parent=Path.GetDirectoryName(path);if(!String.IsNullOrEmpty(parent))Directory(parent);
  if(dirs.Count>=16384)throw new IOException("capture_directory_budget");
  var h=CreateFileW(@"\\?\"+path,0,3,IntPtr.Zero,3,0x02200000,IntPtr.Zero);
  Info info;if(h.IsInvalid||!GetFileInformationByHandle(h,out info)||(info.Attributes&0x410)!=0x10){h.Dispose();throw new IOException("capture_directory_identity");}
  dirs.Add(path,h); // deny delete/rename/reparse ancestor replacement throughout lease
 }
 public byte[] Read(string path,int maximum) {
  Directory(Path.GetDirectoryName(path));
  var h=CreateFileW(@"\\?\"+Path.GetFullPath(path),0x80000000,1,IntPtr.Zero,3,0x00200000,IntPtr.Zero);
  Info info;if(h.IsInvalid||!GetFileInformationByHandle(h,out info)||(info.Attributes&0x410)!=0||info.Links!=1||info.SizeHigh!=0||info.SizeLow>maximum){h.Dispose();throw new IOException("capture_file_identity_or_budget");}
  var stream=new FileStream(h,FileAccess.Read);files.Add(stream);
  byte[] bytes=new byte[info.SizeLow];int at=0;
  while(at<bytes.Length){int n=stream.Read(bytes,at,bytes.Length-at);if(n==0)throw new IOException("capture_short_read");at+=n;}
  return bytes;
 }
 public void Dispose(){foreach(var f in files)f.Dispose();foreach(var d in dirs.Values)d.Dispose();}
}
'@
}

function Get-CaptureBytesHash([byte[]]$Bytes) {
 $sha=[Security.Cryptography.SHA256]::Create()
 try{[BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace('-','')}finally{$sha.Dispose()}
}
function Assert-CaptureRelativePath([string]$Path) {
 if($Path -cnotmatch '^[A-Za-z0-9_. /\-]+$' -or $Path.Length -gt 1024){throw 'capture_path'}
 foreach($part in $Path.Split('/')){
  if(-not $part -or $part -in @('.','..') -or $part.EndsWith('.') -or $part -imatch '^(con|prn|aux|nul|com[0-9]|lpt[0-9])(\.|$)' -or $part -iin @('out','obj','bin','.git')){throw 'capture_path'}
 }
}

function Get-CapturedOriginInputs([string]$Workspace) {
 # The older AI diagnostic selector omits native shader tools. A build capture
 # needs the actual closure, but uses the SAME canonical content hash encoding.
 $map=@{};foreach($entry in @(Get-AiCandidateSourceInputs $Workspace)){$map.Add($entry.path,$entry)}
 $extra=@(& git -C $Workspace -c core.quotepath=false ls-files --cached --others --exclude-standard -- tools Directory.Build.props Directory.Build.targets Directory.Packages.props .editorconfig)
 if($LASTEXITCODE -ne 0){throw 'capture_origin_inventory'}
 foreach($path in $extra){
  if($path -match '(?i)(^|/)(bin|obj|\.git)/|\.user$' -or $map.ContainsKey($path)){continue}
  Assert-CaptureRelativePath $path;$full=Join-Path $Workspace $path
  for($parent=$full;$parent;$parent=[IO.Path]::GetDirectoryName($parent)){
   if((Test-Path -LiteralPath $parent) -and ((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'capture_origin_link'}
  }
  if(-not(Test-Path -LiteralPath $full -PathType Leaf)){continue}
  $map.Add($path,[pscustomobject]@{path=$path;size=(Get-Item -LiteralPath $full).Length;sha256=(Get-DeploymentHash $full)})
 }
 foreach($path in @(Get-AiCandidateOrderedPaths @($map.Keys))){$map[$path]}
}

function New-CapturedBuild([string]$Workspace,[string]$ReviewBundle,[string]$ExpectedBundleSha256) {
 $Workspace=[IO.Path]::GetFullPath($Workspace)
 $root=Assert-DeploymentPath $Workspace (Join-Path $Workspace ('out/captured-build/'+[Guid]::NewGuid().ToString('N')))
 New-Item -ItemType Directory -Path $root|Out-Null
 $source=Join-Path $root 'source';New-Item -ItemType Directory -Path $source|Out-Null
 $utf8=[Text.UTF8Encoding]::new($false,$true);$pins=[NcmaCapturedBuildPins]::new()
 try{
  $bundleHash=$null;$bundle=$null
  if($ReviewBundle){
   if($ExpectedBundleSha256 -cnotmatch '^[0-9A-F]{64}$'){throw 'capture_bundle_confirmation_required'}
   [byte[]]$bundleBytes=$pins.Read([IO.Path]::GetFullPath($ReviewBundle),524288)
   $bundleHash=Get-CaptureBytesHash $bundleBytes
   if($bundleHash -cne $ExpectedBundleSha256){throw 'capture_bundle_changed'}
   $bundle=$utf8.GetString($bundleBytes)|ConvertFrom-Json
   if($bundle.schemaVersion -ne 1 -or $bundle.kind -cne 'ncma.extension.captured-sources.v1' -or @($bundle.files).Count -lt 1 -or @($bundle.files).Count -gt 16){throw 'capture_bundle_shape'}
   [IO.File]::WriteAllBytes((Join-Path $root 'review.nccapture'),$bundleBytes)
  }elseif($ExpectedBundleSha256){throw 'capture_bundle_missing'}
  # The origin is read once into retained copies. Its Git list is discovery only;
  # it will never be used by the compiler or as the later package fingerprint.
  $inventory=@(Get-CapturedOriginInputs $Workspace);$records=@{};$payload=@{};$total=0L
  foreach($entry in $inventory){
   Assert-CaptureRelativePath $entry.path
   if($null -ne $entry.PSObject.Properties['deleted']){continue}
   [byte[]]$bytes=$pins.Read((Join-Path $Workspace $entry.path),67108864)
   $total+=$bytes.Length;if($total -gt 536870912){throw 'capture_total_budget'}
   if((Get-CaptureBytesHash $bytes) -cne $entry.sha256){throw 'capture_origin_changed'}
   $payload.Add($entry.path,$bytes)
  }
  # Apply the retained before/after images, not current CandidateRoot files.
  # Missing/renamed baseline inputs fail rather than silently transplanting a patch.
  if($null -ne $bundle){
   $projectDirs=@($payload.Keys|Where-Object{$_ -like 'managed/*.csproj'}|ForEach-Object{$_.Substring(0,$_.LastIndexOf('/')+1)})
   $linked=@{}
   foreach($definition in @($payload.Keys|Where-Object{$_ -like 'managed/*.csproj' -or $_ -like 'managed/*.targets'})){
    $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null
    $input=[IO.StringReader]::new($utf8.GetString($payload[$definition]));$reader=[Xml.XmlReader]::Create($input,$settings)
    try{$xml=[Xml.XmlDocument]::new();$xml.XmlResolver=$null;$xml.Load($reader)}finally{$reader.Dispose();$input.Dispose()}
    $directory=Split-Path (Join-Path $Workspace $definition)
    foreach($item in @($xml.SelectNodes('//Compile[@Include]'))){
     $include=$item.GetAttribute('Include').Replace('$(MSBuildThisFileDirectory)',$directory+'/')
     if($include.Contains('$') -or $include.Contains('*') -or $include.Contains(';')){continue}
     $full=[IO.Path]::GetFullPath((Join-Path $directory $include))
     if($full.StartsWith($Workspace+'\',[StringComparison]::OrdinalIgnoreCase)){$linked[$full.Substring($Workspace.Length+1).Replace('\','/')]=$true}
    }
   }
   $seen=@{};$reviewBytes=0L
   foreach($file in @($bundle.files)){
    [string]$path=$file.path;Assert-CaptureRelativePath $path
    if(-not $path.EndsWith('.cs',[StringComparison]::Ordinal) -or $seen.ContainsKey($path)){throw 'capture_bundle_path'};$seen.Add($path,$true)
    if(-not $linked.ContainsKey($path) -and @($projectDirs|Where-Object{$path.StartsWith($_,[StringComparison]::Ordinal)}).Count -eq 0){throw 'capture_review_not_compiled'}
    [byte[]]$before=@();[byte[]]$after=@()
    if($null -ne $file.before){$before=[Convert]::FromBase64String($file.before)}
    if($null -ne $file.after){$after=[Convert]::FromBase64String($file.after)}
    $reviewBytes+=$before.Length+$after.Length
    if($before.Length -gt 65536 -or $after.Length -gt 65536 -or $reviewBytes -gt 262144){throw 'capture_bundle_budget'}
    $null=$utf8.GetString($before);$null=$utf8.GetString($after)
    if(($null -ne $file.before) -ne $payload.ContainsKey($path)){throw 'capture_baseline_presence'}
    if($payload.ContainsKey($path) -and (Get-CaptureBytesHash $payload[$path]) -cne (Get-CaptureBytesHash $before)){throw 'capture_baseline_changed'}
    if($null -eq $file.before -and $null -eq $file.after){throw 'capture_empty_change'}
    if($null -eq $file.after){$null=$payload.Remove($path)}else{$payload[$path]=$after}
   }
  }
  foreach($path in @(Get-AiCandidateOrderedPaths @($payload.Keys))){
   $dest=Join-Path $source $path;New-Item -ItemType Directory -Force -Path (Split-Path $dest)|Out-Null
   $pins.Directory((Split-Path $dest));$stream=[IO.File]::Open($dest,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
   try{$stream.Write($payload[$path],0,$payload[$path].Length);$stream.Flush($true)}finally{$stream.Dispose()}
   $records.Add($path,[pscustomobject]@{path=$path;size=$payload[$path].Length;sha256=(Get-CaptureBytesHash $payload[$path])})
  }
  $entries=@(foreach($path in @(Get-AiCandidateOrderedPaths @($records.Keys))){$records[$path]})
  $hash=Get-AiCandidateInputHash $entries
  # Explicit source lists stop SDK globs from discovering later files. Captured
  # project links remain supported. No parent Directory.Build.props inheritance.
  $recipe=[Text.StringBuilder]::new('<Project><PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems><EnableDefaultNoneItems>false</EnableDefaultNoneItems></PropertyGroup>')
  $output=[Security.SecurityElement]::Escape($Workspace.Replace('\','/')+'/managed/$(MSBuildProjectName)/bin/')
  $obj=[Security.SecurityElement]::Escape($root.Replace('\','/')+'/obj/$(MSBuildProjectName)/')
  $null=$recipe.Append('<PropertyGroup Condition="&apos;$(ArtifactsPath)&apos; == &apos;&apos;"><BaseOutputPath>'+$output+'</BaseOutputPath><BaseIntermediateOutputPath>'+$obj+'</BaseIntermediateOutputPath></PropertyGroup>')
  foreach($project in @($entries|Where-Object path -like 'managed/*.csproj')){
   $dir=$project.path.Substring(0,$project.path.LastIndexOf('/')+1)
   $projectPath=[Security.SecurityElement]::Escape([IO.Path]::GetFullPath((Join-Path $source $project.path)))
   $null=$recipe.Append('<ItemGroup Condition="&apos;$(MSBuildProjectFullPath)&apos; == &apos;'+$projectPath+'&apos;">')
   $members=@($entries|Where-Object{$_.path.StartsWith($dir,[StringComparison]::Ordinal) -and $_.path.EndsWith('.cs',[StringComparison]::Ordinal)})
   foreach($member in $members){$null=$recipe.Append('<Compile Include="$(MSBuildThisFileDirectory)source/'+$member.path+'" />')}
   $null=$recipe.Append('</ItemGroup>')
  }
  $null=$recipe.Append('</Project>')
  $recipeText=$recipe.ToString().Replace('$(MSBuildThisFileDirectory)source/',($source.Replace('\','/')+'/'))
  [IO.File]::WriteAllText((Join-Path $root 'capture.props'),$recipeText,$utf8)
  $manifest=@{kind='ncma.captured-build.v1';schemaVersion=1;sourceInputsSha256=$hash;reviewBundleSha256=$bundleHash;
   recipeSha256=(Get-DeploymentHash (Join-Path $root 'capture.props'));files=$entries;originRole='capture-only';compilerSourceRole='retained-copy';accepted=$false}
  [IO.File]::WriteAllText((Join-Path $root 'capture.json'),($manifest|ConvertTo-Json -Depth 6),$utf8)
  return $root
 }finally{$pins.Dispose()}
}

function Open-CapturedBuild([string]$Workspace,[string]$Root) {
 $Root=Assert-DeploymentPath $Workspace $Root
 if(-not $Root.StartsWith((Join-Path $Workspace 'out/captured-build')+'\',[StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $Root) -cnotmatch '^[a-f0-9]{32}$'){throw 'capture_root'}
 $pins=[NcmaCapturedBuildPins]::new()
 try{
  [byte[]]$bytes=$pins.Read((Join-Path $Root 'capture.json'),16777216)
  $manifest=[Text.Encoding]::UTF8.GetString($bytes)|ConvertFrom-Json
  if($manifest.kind -cne 'ncma.captured-build.v1' -or $manifest.schemaVersion -ne 1 -or @($manifest.files).Count -gt 65536){throw 'capture_manifest'}
  $entries=@($manifest.files);$source=Join-Path $Root 'source';$total=0L
  foreach($entry in $entries){
   Assert-CaptureRelativePath $entry.path
   [byte[]]$content=$pins.Read((Join-Path $source $entry.path),67108864);$total+=$content.Length
   if($total -gt 536870912 -or $entry.size -ne $content.Length -or $entry.sha256 -cne (Get-CaptureBytesHash $content)){throw 'capture_content_changed'}
  }
  if((Get-AiCandidateInputHash $entries) -cne $manifest.sourceInputsSha256){throw 'capture_manifest_hash'}
  [byte[]]$props=$pins.Read((Join-Path $Root 'capture.props'),4194304)
  if((Get-CaptureBytesHash $props) -cne $manifest.recipeSha256){throw 'capture_recipe_changed'}
  if($manifest.reviewBundleSha256){if((Get-CaptureBytesHash $pins.Read((Join-Path $Root 'review.nccapture'),524288)) -cne $manifest.reviewBundleSha256){throw 'capture_review_changed'}}
  return [pscustomobject]@{Root=$Root;Source=$source;Manifest=$manifest;ManifestSha256=(Get-CaptureBytesHash $bytes);Pins=$pins;
   Arguments=@("-p:DirectoryBuildPropsPath=$Root/capture.props","-p:DirectoryBuildTargetsPath=$source/managed/Directory.Build.targets")}
 }catch{$pins.Dispose();throw}
}
