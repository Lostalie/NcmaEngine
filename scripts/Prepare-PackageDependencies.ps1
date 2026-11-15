[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][ValidateSet('Debug','Release')][string]$Configuration,
 [Parameter(Mandatory=$true)][string]$CapturedBuildRoot,
 [Parameter(Mandatory=$true)][string]$DotnetExecutable,
 [Parameter(Mandatory=$true)][string]$OfflinePackageSource,
 [switch]$IncludeAI,[switch]$IncludeExtensions,
 [ValidateSet('package','verification','modules','sdk')][string]$Scope='package')
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'OfflinePackageDependencies.ps1')
if($Scope -ceq 'verification'){
 if($IncludeAI -or $IncludeExtensions){throw 'Verification preparation has six fixed jobs; do not supply product switches'}
 $IncludeAI=[switch]::new($true);$IncludeExtensions=[switch]::new($true)
}
if($Scope -ceq 'modules' -and ($IncludeAI -or $IncludeExtensions)){throw 'Module preparation has two fixed optional transport jobs; no product switches'}
if($Scope -ceq 'sdk'){
 if($IncludeAI -or $IncludeExtensions){throw 'SDK preparation has one fixed extensions harness graph; no product switches'}
 $IncludeExtensions=[switch]::new($true)
}
$workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$tool=Get-OfflinePackagePath $DotnetExecutable;$feed=Get-OfflinePackagePath $OfflinePackageSource
if([IO.Path]::GetFileName($tool) -ine 'dotnet.exe' -or -not(Test-Path -LiteralPath $tool -PathType Leaf) -or -not(Test-Path -LiteralPath $feed -PathType Container)){throw 'offline_package_prerequisite'}
$capture=Open-CapturedBuild $workspace $CapturedBuildRoot
$toolPins=[NcmaCapturedBuildPins]::new();$root=$null;$failure=$null
$environment=@{};$names=@('DOTNET_CLI_TELEMETRY_OPTOUT','DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE','DOTNET_CLI_UI_LANGUAGE','NUGET_CERT_REVOCATION_MODE')
foreach($name in $names){$environment[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try{
 Assert-OfflinePackageScripts $capture $PSScriptRoot $Scope
 $toolHash=Get-CaptureBytesHash $toolPins.Read($tool,67108864)
 $root=Assert-DeploymentPath $workspace (Join-Path $workspace ("out/verification/m8-6/$Scope-dependencies/"+[Guid]::NewGuid().ToString('N')))
 if(Test-Path -LiteralPath $root){throw 'offline_package_output_exists'}
 $null=New-Item -ItemType Directory -Path $root
 $config=Join-Path $root 'offline.nuget.config'
 $xml='<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear /></packageSources><fallbackPackageFolders><clear /></fallbackPackageFolders><packageSourceMapping><clear /></packageSourceMapping><disabledPackageSources><clear /></disabledPackageSources></configuration>'
 [IO.File]::WriteAllText($config,$xml,[Text.UTF8Encoding]::new($false))
 $jobs=@(switch($Scope){'package'{Get-OfflinePackageJobs ([bool]$IncludeAI) ([bool]$IncludeExtensions)};'verification'{Get-OfflineVerificationJobs};'modules'{Get-OfflineModuleJobs};'sdk'{Get-OfflineSdkJobs}})
 Write-OfflinePackageJson (Join-Path $root 'plan.json') @{configuration=$Configuration;captureRoot=$capture.Root;captureManifestSha256=$capture.ManifestSha256;sourceInputsSha256=$capture.Manifest.sourceInputsSha256;root=$root;jobs=$jobs;source=$feed;networkAuthorized=$false}
 $env:DOTNET_CLI_TELEMETRY_OPTOUT='1';$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true';$env:DOTNET_CLI_UI_LANGUAGE='en-US';$env:NUGET_CERT_REVOCATION_MODE='offline'
 foreach($job in $jobs){
  $arguments=@(Get-OfflinePackageArguments $capture $root $Configuration $job)
  $log=Join-Path $root ($job.id.Replace('/','-')+'.restore.log')
  & $tool restore (Join-Path $capture.Source $job.project) @arguments --disable-parallel --no-http-cache --configfile $config --source $feed --packages (Join-Path $root 'packages') -p:RestoreFallbackFolders= -p:RestoreAdditionalProjectSources= -p:RestoreAdditionalProjectFallbackFolders= -p:RestoreIgnoreFailedSources=false -p:NuGetAudit=false *> $log
  $exit=$LASTEXITCODE;Get-Content -LiteralPath $log -Tail 4
  if($exit -ne 0){throw "offline_package_restore_failed:$($job.id)"}
 }
 Assert-OfflinePackageAssets $capture $root $jobs
 $receipt=[ordered]@{kind="ncma.$Scope.offline-dependencies.v1";configuration=$Configuration;root=$root;includeAI=[bool]$IncludeAI;includeExtensions=[bool]$IncludeExtensions;captureRoot=$capture.Root;captureManifestSha256=$capture.ManifestSha256;sourceInputsSha256=$capture.Manifest.sourceInputsSha256;captureRecipeSha256=$capture.Manifest.recipeSha256;dotnet=@{path=$tool;sha256=$toolHash};jobs=$jobs;files=@(Get-OfflinePackageDependencyFiles $workspace $root);networkAuthorized=$false;compiled=$false;tested=$false;installed=$false}
 Write-OfflinePackageJson (Join-Path $root 'receipt.json') $receipt
 [pscustomobject]@{DependenciesRoot=$root;ExpectedDependenciesSha256=(Get-DeploymentHash (Join-Path $root 'receipt.json'));CapturedBuildRoot=$capture.Root;Configuration=$Configuration;Compiled=$false;TestsExecuted=$false}
}catch{
 $failure=$_.Exception.GetType().Name+': '+$_.Exception.Message
 if($root -and (Test-Path -LiteralPath $root)){Write-OfflinePackageJson (Join-Path $root 'failure.json') @{failure=$failure;accepted=$false;networkAuthorized=$false}}
 throw
}finally{
 foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$environment[$name],'Process')}
 $toolPins.Dispose();$capture.Pins.Dispose()
}
