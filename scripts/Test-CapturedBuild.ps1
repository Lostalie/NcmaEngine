[CmdletBinding()]param()
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'CapturedBuild.ps1')
$workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$evidence=Join-Path $workspace ('out/verification/m8-5/captured-build-tests/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence|Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
function Fixture {
 $origin=Join-Path $evidence ([Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path "$origin/managed/Probe"|Out-Null
 & git init --quiet $origin;if($LASTEXITCODE -ne 0){throw 'Fixture Git inventory prerequisite failed'}
 [IO.File]::WriteAllText("$origin/managed/Probe/Probe.csproj",'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>',$utf8)
 [IO.File]::WriteAllText("$origin/managed/Directory.Build.targets",'<Project />',$utf8)
 [IO.File]::WriteAllText("$origin/managed/Probe/Program.cs",'System.Console.WriteLine(7);',$utf8)
 return $origin
}
function Check([bool]$value,[string]$reason){if(-not $value){throw $reason}}
function Reject([scriptblock]$call,[string]$expected){try{& $call}catch{if($_.Exception.Message -eq $expected){return};throw};throw "Expected $expected"}
$cases=[ordered]@{
 'actual compiler uses retained after bytes after origin becomes invalid'={
  $origin=Fixture;$bundlePath=Join-Path $origin 'review.nccapture'
  $bundle=@{schemaVersion=1;kind='ncma.extension.captured-sources.v1';files=@(@{path='managed/Probe/Program.cs';before=[Convert]::ToBase64String($utf8.GetBytes('System.Console.WriteLine(7);'));after=[Convert]::ToBase64String($utf8.GetBytes('System.Console.WriteLine(29);'))})}
  [IO.File]::WriteAllText($bundlePath,($bundle|ConvertTo-Json -Depth 5),$utf8)
  $root=New-CapturedBuild $origin $bundlePath (Get-DeploymentHash $bundlePath)
  [IO.File]::WriteAllText("$origin/managed/Probe/Program.cs",'this is not C# and must never enter compilation',$utf8)
  $capture=Open-CapturedBuild $origin $root
  try{
   $out=Join-Path $root 'artifacts'
   & dotnet build "$($capture.Source)/managed/Probe/Probe.csproj" @($capture.Arguments) "-p:ArtifactsPath=$out" -p:UseSharedCompilation=false --disable-build-servers --nologo *> "$root/build.log"
   Check ($LASTEXITCODE -eq 0) "Captured real compile failed: $root/build.log"
   $result=& dotnet "$out/bin/Probe/debug/Probe.dll"
   Check ($LASTEXITCODE -eq 0 -and $result -ceq '29') 'Compiler did not use retained after image'
   $writeRejected=$false
   try{[IO.File]::WriteAllText("$($capture.Source)/managed/Probe/Program.cs",'changed')}catch [IO.IOException]{$writeRejected=$true}
   Check $writeRejected 'Compiled input was not pinned'
  }finally{$capture.Pins.Dispose()}
 }
 'changed capture rejected, not repaired by rereading origin'={
  $origin=Fixture;$root=New-CapturedBuild $origin $null $null
  [IO.File]::WriteAllText("$root/source/managed/Probe/Program.cs",'System.Console.WriteLine(11);',$utf8)
  Reject {$lease=Open-CapturedBuild $origin $root;$lease.Pins.Dispose()} 'capture_content_changed'
 }
 'source bundle exact digest required and changed baseline rejected'={
  $origin=Fixture;$path=Join-Path $origin 'bad.nccapture';[IO.File]::WriteAllText($path,'{}',$utf8)
  Reject {New-CapturedBuild $origin $path ('0'*64)} 'capture_bundle_changed'
  $bundle=@{schemaVersion=1;kind='ncma.extension.captured-sources.v1';files=@(@{path='managed/Probe/Program.cs';before=[Convert]::ToBase64String($utf8.GetBytes('other version'));after=[Convert]::ToBase64String($utf8.GetBytes('new version'))})}
  [IO.File]::WriteAllText($path,($bundle|ConvertTo-Json -Depth 5),$utf8)
  Reject {New-CapturedBuild $origin $path (Get-DeploymentHash $path)} 'capture_baseline_changed'
 }
 'explicit compile lists exclude files added after capture'={
  $origin=Fixture;$root=New-CapturedBuild $origin $null $null
  [IO.File]::WriteAllText("$root/source/managed/Probe/NotCaptured.cs",'invalid C# must not be globbed',$utf8)
  $lease=Open-CapturedBuild $origin $root
  try{
   & dotnet build "$($lease.Source)/managed/Probe/Probe.csproj" @($lease.Arguments) "-p:ArtifactsPath=$root/artifacts" --nologo -p:UseSharedCompilation=false --disable-build-servers *> "$root/build.log"
   Check ($LASTEXITCODE -eq 0) "Implicit wildcard entered captured build: $root/build.log"
  }finally{$lease.Pins.Dispose()}
 }
 'review outside any captured project or explicit source link rejected'={
  $origin=Fixture;$path=Join-Path $origin 'unbound.nccapture'
  $bundle=@{schemaVersion=1;kind='ncma.extension.captured-sources.v1';files=@(@{path='Ignored.cs';before=$null;after=[Convert]::ToBase64String($utf8.GetBytes('public class Ignored {}'))})}
  [IO.File]::WriteAllText($path,($bundle|ConvertTo-Json -Depth 5),$utf8)
  Reject {New-CapturedBuild $origin $path (Get-DeploymentHash $path)} 'capture_review_not_compiled'
 }
}
$failed=0
foreach($case in $cases.GetEnumerator()){
 Write-Host "BEGIN captured build / $($case.Key)"
 try{&$case.Value;Write-Host "PASS captured build / $($case.Key)"}catch{$failed++;Write-Host "FAIL captured build / $($case.Key): $($_.Exception)"}
}
Write-Host "SUMMARY captured build: $($cases.Count-$failed)/$($cases.Count); evidence=$evidence"
if($failed){exit 1};exit 0
