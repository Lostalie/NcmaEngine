# Trusted local packaging preparation, not an AI permission, sandbox, or SDK attestation.
. (Join-Path $PSScriptRoot 'CapturedBuild.ps1')

function Get-OfflinePackagePath([string]$Path) {
 if($Path -cnotmatch '^[A-Za-z]:[\\/]' -or $Path -match '[\x00-\x1f;\x25$@*?<>|=]'){throw 'offline_package_local_path'}
 $full=[IO.Path]::GetFullPath($Path).TrimEnd('\')
 if(([IO.DriveInfo]::new([IO.Path]::GetPathRoot($full))).DriveType -ne [IO.DriveType]::Fixed){throw 'offline_package_local_drive'}
 for($part=[IO.DirectoryInfo]::new($full);$null -ne $part;$part=$part.Parent){
  if($part.Exists -and ($part.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'offline_package_reparse'}
 }
 return $full
}

function Get-OfflinePackageJobs([bool]$IncludeAI,[bool]$IncludeExtensions) {
 $jobs=[Collections.Generic.List[object]]::new()
 foreach($project in @('Ncma.Gameplay.Sample','Ncma.Shader.Cook')){
  $jobs.Add([pscustomobject][ordered]@{id="tools/$project";project="managed/$project/$project.csproj";variant='tools';operation='build';ai=$false;extensions=$false;rid=''})
 }
 $variants=@('default')
 if($IncludeAI){$variants+='ai'}
 if($IncludeExtensions){$variants+='default-extensions'}
 if($IncludeAI -and $IncludeExtensions){$variants+='ai-extensions'}
 foreach($variant in $variants){
  $projects=if($variant -eq 'default'){@('Ncma.Editor.App','Ncma.Asset.ImportWorker','Ncma.Player.App')}elseif($variant -eq 'ai'){@('Ncma.Editor.App')}else{@('Ncma.Editor.App','Ncma.Player.App')}
  foreach($project in $projects){$jobs.Add([pscustomobject][ordered]@{id="$variant/$project";project="managed/$project/$project.csproj";variant=$variant;operation='publish';ai=$variant.StartsWith('ai');extensions=$variant.EndsWith('-extensions');rid='win-x64'})}
 }
 return $jobs.ToArray()
}

function Get-OfflinePackageArguments($Capture,[string]$Root,[string]$Configuration,$Job) {
 if($Configuration -cnotin @('Debug','Release')){throw 'offline_package_configuration'}
 $rootPath=Get-OfflinePackagePath $Root
 $allJobs=@(Get-OfflinePackageJobs $true $true)+@(Get-OfflineVerificationJobs)+@(Get-OfflineModuleJobs)+@(Get-OfflineSdkJobs)
 $allowed=@($allJobs|Where-Object id -ceq $Job.id)
 if($allowed.Count -ne 1 -or ($allowed[0]|ConvertTo-Json -Compress) -cne ($Job|ConvertTo-Json -Compress)){throw 'offline_package_job'}
 $buildArguments=@($Capture.Arguments)+@('--nologo','--disable-build-servers','-noAutoResponse','-m:1','-nodeReuse:false','-p:UseSharedCompilation=false','-warnaserror',"-p:Configuration=$Configuration","-p:ArtifactsPath=$rootPath/artifacts/$($Job.variant)","-p:RestorePackagesPath=$rootPath/packages","-p:NcmaEnableAI=$($Job.ai.ToString().ToLowerInvariant())","-p:NcmaEnableExtensions=$($Job.extensions.ToString().ToLowerInvariant())")
 if($Job.operation -ceq 'publish'){$buildArguments+=@('-p:RuntimeIdentifier=win-x64','-p:SelfContained=false','-p:UseAppHost=true','-p:PublishTrimmed=false','-p:PublishAot=false','-p:PublishSingleFile=false')}
 if($Job.operation -ceq 'publish-library'){$buildArguments+=@('-p:PublishTrimmed=false','-p:PublishAot=false','-p:PublishSingleFile=false')}
 return $buildArguments
}

function Get-OfflineVerificationJobs {
 foreach($spec in @(@('verify-ai','Ncma.Editor.Services.Tests',$true,$false),@('verify-contracts','Ncma.AI.Tests',$false,$false),
  @('verify-extensions','Ncma.Editor.Services.Tests',$false,$true),@('verify-extensions','Ncma.Player.Tests',$false,$true),
  @('verify-ai-extensions','Ncma.Editor.Services.Tests',$true,$true),@('verify-ai-extensions','Ncma.Player.Tests',$true,$true))){
  [pscustomobject][ordered]@{id=($spec[0]+'/'+$spec[1]);project=("managed/{0}/{0}.csproj" -f $spec[1]);variant=$spec[0];operation='build';ai=$spec[2];extensions=$spec[3];rid=''}
 }
}

function Get-OfflineModuleJobs {
 foreach($spec in @(@('Ncma.Modules.Grpc','publish-library'),@('Ncma.Modules.Grpc.Tests','build'))){
  [pscustomobject][ordered]@{id=('module-transport/'+$spec[0]);project=("managed/{0}/{0}.csproj" -f $spec[0]);variant='module-transport';operation=$spec[1];ai=$false;extensions=$false;rid=''}
 }
}

function Get-OfflineSdkJobs {
 [pscustomobject][ordered]@{id='workflow-sdk/Ncma.Editor.Services.Tests';project='managed/Ncma.Editor.Services.Tests/Ncma.Editor.Services.Tests.csproj';variant='workflow-sdk';operation='build';ai=$false;extensions=$true;rid=''}
}

function Assert-OfflinePackageScripts($Capture,[string]$ScriptRoot,[string]$Scope='package') {
 $names=@('OfflinePackageDependencies.ps1','Prepare-PackageDependencies.ps1','Package-M2_7.ps1')
 if($Scope -ceq 'verification'){$names+=@('Prepare-AiVerification.ps1','Prepare-ExtensionVerification.ps1','VerificationInputStaging.ps1')}
 elseif($Scope -ceq 'modules'){$names+=@('Package-ModuleClient.ps1')}
 elseif($Scope -ceq 'sdk'){$names+=@('Prepare-WorkflowSdkInputs.ps1')}
 elseif($Scope -cne 'package'){throw 'offline_package_scope'}
 foreach($name in $names){
  $entry=@($Capture.Manifest.files|Where-Object path -ceq "scripts/$name")
  if($entry.Count -ne 1 -or (Get-DeploymentHash (Join-Path $ScriptRoot $name)) -cne $entry[0].sha256){throw 'offline_package_recipe_changed'}
 }
}

function Get-OfflinePackageDependencyFiles([string]$Workspace,[string]$Root) {
 $rootPath=Get-OfflinePackagePath $Root
 $files=[Collections.Generic.List[object]]::new()
 foreach($entry in @(Get-DeploymentFiles $Workspace (Join-Path $rootPath 'packages'))){
  $files.Add([pscustomobject]@{path=('packages/'+$entry.path);size=$entry.size;sha256=$entry.sha256})
 }
 foreach($variant in @(Get-ChildItem -LiteralPath (Join-Path $rootPath 'artifacts') -Directory)){
  if($variant.Name -cnotin @('tools','default','ai','default-extensions','ai-extensions','verify-ai','verify-contracts','verify-extensions','verify-ai-extensions','module-transport','workflow-sdk') -or ($variant.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'offline_package_variant_directory'}
  $obj=Join-Path $variant.FullName 'obj'
  foreach($entry in @(Get-DeploymentFiles $Workspace $obj)){
   if($entry.path.EndsWith('/project.assets.json',[StringComparison]::Ordinal) -or $entry.path.EndsWith('.csproj.nuget.g.props',[StringComparison]::Ordinal) -or $entry.path.EndsWith('.csproj.nuget.g.targets',[StringComparison]::Ordinal)){
    $files.Add([pscustomobject]@{path=('artifacts/'+$variant.Name+'/obj/'+$entry.path);size=$entry.size;sha256=$entry.sha256})
   }
  }
 }
 if($files.Count -lt 4 -or $files.Count -gt 32768){throw 'offline_package_dependency_budget'}
 return @($files.ToArray()|Sort-Object path)
}

function Assert-OfflinePackageAssets($Capture,[string]$Root,$Jobs) {
 $prefix=$Capture.Source.TrimEnd('\')+'\';$packages=[IO.Path]::GetFullPath((Join-Path $Root 'packages')).TrimEnd('\')
 foreach($job in $Jobs){
  $name=[IO.Path]::GetFileNameWithoutExtension($job.project)
  $obj=[IO.Path]::GetFullPath((Join-Path $Root "artifacts/$($job.variant)/obj/$name"))
  $asset=Join-Path $obj 'project.assets.json'
  if((Get-Item -LiteralPath $asset).Length -gt 4194304){throw 'offline_package_assets_budget'}
  $value=Get-Content -Raw -LiteralPath $asset|ConvertFrom-Json
  if($value.version -ne 3 -or [IO.Path]::GetFullPath($value.project.restore.projectPath) -ine [IO.Path]::GetFullPath((Join-Path $Capture.Source $job.project)) -or [IO.Path]::GetFullPath($value.project.restore.outputPath).TrimEnd('\') -ine $obj){throw 'offline_package_assets_binding'}
  $folders=@($value.packageFolders.PSObject.Properties.Name)
  if($folders.Count -ne 1 -or [IO.Path]::GetFullPath($folders[0]).TrimEnd('\') -ine $packages){throw 'offline_package_external_packages'}
  if($job.operation -ceq 'publish' -and @($value.targets.PSObject.Properties.Name|Where-Object {$_ -eq 'net8.0/win-x64'}).Count -ne 1){throw 'offline_package_rid_missing'}
  if($job.variant -ceq 'module-transport' -and (@($value.targets.PSObject.Properties.Name).Count -ne 1 -or $value.targets.PSObject.Properties.Name -cne 'net8.0')){throw 'offline_module_target_mismatch'}
  foreach($framework in $value.project.restore.frameworks.PSObject.Properties){
   foreach($ref in $framework.Value.projectReferences.PSObject.Properties){
    if(-not [IO.Path]::GetFullPath($ref.Name).StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'offline_package_foreign_project'}
   }
  }
 }
}

function Write-OfflinePackageJson([string]$Path,$Value) {
 $stream=[IO.FileStream]::new($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
 try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($Value|ConvertTo-Json -Depth 12));$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
}

function Open-OfflinePackageDependencies([string]$Workspace,$Capture,[string]$Root,[string]$ExpectedSha256,[string]$Configuration,[bool]$IncludeAI,[bool]$IncludeExtensions,[string]$DotnetExecutable,[string]$Scope='package') {
 if($Scope -cnotin @('package','verification','modules','sdk') -or ($Scope -ceq 'verification' -and (-not $IncludeAI -or -not $IncludeExtensions)) -or ($Scope -ceq 'modules' -and ($IncludeAI -or $IncludeExtensions)) -or ($Scope -ceq 'sdk' -and ($IncludeAI -or -not $IncludeExtensions))){throw 'offline_package_scope'}
 $rootPath=Get-OfflinePackagePath $Root
 $prefix=[IO.Path]::GetFullPath((Join-Path $Workspace "out/verification/m8-6/$Scope-dependencies")).TrimEnd('\')+'\'
 if(-not $rootPath.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or $rootPath.Substring($prefix.Length) -cnotmatch '^[a-f0-9]{32}$' -or $ExpectedSha256 -cnotmatch '^[0-9A-F]{64}$'){throw 'offline_package_preparation_identity'}
 $tool=Get-OfflinePackagePath $DotnetExecutable
 if([IO.Path]::GetFileName($tool) -ine 'dotnet.exe'){throw 'offline_package_dotnet_required'}
 Assert-OfflinePackageScripts $Capture $PSScriptRoot $Scope
 $pins=[NcmaCapturedBuildPins]::new()
 try{
  [byte[]]$bytes=$pins.Read((Join-Path $rootPath 'receipt.json'),16777216)
  if((Get-CaptureBytesHash $bytes) -cne $ExpectedSha256){throw 'offline_package_receipt_changed'}
  $value=[Text.Encoding]::UTF8.GetString($bytes)|ConvertFrom-Json
  if($value.kind -cne "ncma.$Scope.offline-dependencies.v1" -or $value.configuration -cne $Configuration -or $value.includeAI -isnot [bool] -or $value.includeAI -ne $IncludeAI -or $value.includeExtensions -isnot [bool] -or $value.includeExtensions -ne $IncludeExtensions -or $value.root -cne $rootPath -or $value.captureRoot -cne $Capture.Root -or $value.captureManifestSha256 -cne $Capture.ManifestSha256 -or $value.sourceInputsSha256 -cne $Capture.Manifest.sourceInputsSha256 -or $value.captureRecipeSha256 -cne $Capture.Manifest.recipeSha256 -or $value.dotnet.path -cne $tool -or $value.networkAuthorized -isnot [bool] -or $value.networkAuthorized){throw 'offline_package_receipt_binding'}
  if((Get-CaptureBytesHash $pins.Read($tool,67108864)) -cne $value.dotnet.sha256){throw 'offline_package_tool_changed'}
  $jobs=@(switch($Scope){'package'{Get-OfflinePackageJobs $IncludeAI $IncludeExtensions};'verification'{Get-OfflineVerificationJobs};'modules'{Get-OfflineModuleJobs};'sdk'{Get-OfflineSdkJobs}})
  if(($jobs|ConvertTo-Json -Depth 6 -Compress) -cne ($value.jobs|ConvertTo-Json -Depth 6 -Compress)){throw 'offline_package_jobs_changed'}
  $actual=@(Get-OfflinePackageDependencyFiles $Workspace $rootPath);$listed=@($value.files)
  if($actual.Count -ne $listed.Count){throw 'offline_package_dependency_set'}
  $total=0L
  for($i=0;$i -lt $actual.Count;$i++){
   $a=$actual[$i];$b=$listed[$i]
   if($a.path -cne $b.path -or $a.size -ne $b.size -or $a.sha256 -cne $b.sha256){throw 'offline_package_dependency_changed'}
   $total+=$a.size;if($total -gt 1073741824){throw 'offline_package_dependency_budget'}
   if((Get-CaptureBytesHash $pins.Read((Join-Path $rootPath $a.path),67108864)) -cne $a.sha256){throw 'offline_package_dependency_changed'}
  }
  Assert-OfflinePackageAssets $Capture $rootPath $jobs
  return [pscustomobject]@{Root=$rootPath;Jobs=$jobs;Pins=$pins;ReceiptSha256=$ExpectedSha256;Dotnet=$tool}
 }catch{$pins.Dispose();throw}
}
