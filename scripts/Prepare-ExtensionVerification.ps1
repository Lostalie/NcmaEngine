[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateSet('Debug','Release')][string]$Configuration,[string]$CapturedBuildRoot,
 [string]$DotnetExecutable,[string]$DependenciesRoot,[string]$ExpectedDependenciesSha256)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'AiVerificationInputs.ps1')
. (Join-Path $PSScriptRoot 'CapturedBuild.ps1')
. (Join-Path $PSScriptRoot 'VerificationInputStaging.ps1')
if(-not $CapturedBuildRoot -or -not $DotnetExecutable -or -not $DependenciesRoot -or -not $ExpectedDependenciesSha256){throw 'Extension verification requires explicit capture/tool and separate -Scope verification dependency preparation with its exact receipt SHA; no implicit restore'}
$workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$evidence=Assert-DeploymentPath $workspace (Join-Path $workspace ("out/verification/m8-5/managed-inputs/"+[Guid]::NewGuid().ToString('N')))
New-Item -ItemType Directory -Path $evidence|Out-Null
$capture=Open-CapturedBuild $workspace $CapturedBuildRoot
$dependencies=$null
try {
$dependencies=Open-OfflinePackageDependencies $workspace $capture $DependenciesRoot $ExpectedDependenciesSha256 $Configuration $true $true $DotnetExecutable 'verification'
$inputs=@($capture.Manifest.files);$sourceHash=$capture.Manifest.sourceInputsSha256
$exeHash=Get-DeploymentHash (Join-Path $workspace 'out/bin/NcmaEngine.exe')
$products=@{}
foreach($variant in 'extensions','ai-extensions'){
 $artifacts=Join-Path $workspace ("out/build/windows-ninja-"+$Configuration.ToLowerInvariant()+"/m8-5-"+$variant)
 foreach($project in 'Ncma.Editor.Services.Tests','Ncma.Player.Tests'){
  $log=Join-Path $evidence "$variant-$project.log"
  $id="verify-$variant/$project";$job=@($dependencies.Jobs|Where-Object id -ceq $id)
  if($job.Count -ne 1){throw 'Prepared extension verification job missing'}
  $preparedArguments=@(Get-OfflinePackageArguments $capture $dependencies.Root $Configuration $job[0])
  & $DotnetExecutable build (Join-Path $capture.Source $job[0].project) @preparedArguments --no-restore "-bl:$evidence/$variant-$project.binlog" *> $log
  $exit=$LASTEXITCODE;Get-Content -LiteralPath $log -Tail 8
  if($exit -ne 0){throw "Extension verification compile failed: $variant/$project; evidence preserved"}
  $path=Copy-VerificationInputTree $workspace $Configuration $dependencies $id
  if($path -cne [IO.Path]::GetFullPath((Join-Path $artifacts "bin/$project/$($Configuration.ToLowerInvariant())"))){throw 'Original extension input root changed'}
  $products["$variant/$project"]=@{root=$path;files=@(Get-AiVerificationFiles $path);log=$log;logSha256=(Get-DeploymentHash $log)}
 }
}
if((Get-DeploymentHash (Join-Path $workspace 'out/bin/NcmaEngine.exe')) -cne $exeHash){throw 'Formal EXE changed'}
$receipt=@{kind='compile-only';schemaVersion=2;configuration=$Configuration;sourceInputsSha256=$sourceHash;capturedBuildRoot=$capture.Root;captureManifestSha256=$capture.ManifestSha256;reviewBundleSha256=$capture.Manifest.reviewBundleSha256;offlineDependenciesRoot=$dependencies.Root;offlineDependenciesSha256=$dependencies.ReceiptSha256;products=$products;testsExecuted=$false;formalExeSha256=$exeHash}
[IO.File]::WriteAllText((Join-Path $evidence 'receipt.json'),($receipt|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
Write-Output "Compiled only: $evidence/receipt.json; no tests, package or deployment."
} finally {
 if($null -ne $dependencies){$dependencies.Pins.Dispose()}
 $capture.Pins.Dispose()
}
