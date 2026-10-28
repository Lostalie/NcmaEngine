[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateSet('Debug','Release')][string]$Configuration,[string]$CapturedBuildRoot)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'AiVerificationInputs.ps1')
. (Join-Path $PSScriptRoot 'CapturedBuild.ps1')
$workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$evidence=Assert-DeploymentPath $workspace (Join-Path $workspace ("out/verification/m8-5/managed-inputs/"+[Guid]::NewGuid().ToString('N')))
New-Item -ItemType Directory -Path $evidence|Out-Null
if(-not $CapturedBuildRoot){$CapturedBuildRoot=New-CapturedBuild $workspace $null $null}
$capture=Open-CapturedBuild $workspace $CapturedBuildRoot
try {
$inputs=@($capture.Manifest.files);$sourceHash=$capture.Manifest.sourceInputsSha256
$exeHash=Get-DeploymentHash (Join-Path $workspace 'out/bin/NcmaEngine.exe')
$products=@{}
foreach($variant in 'extensions','ai-extensions'){
 $artifacts=Join-Path $workspace ("out/build/windows-ninja-"+$Configuration.ToLowerInvariant()+"/m8-5-"+$variant)
 foreach($project in 'Ncma.Editor.Services.Tests','Ncma.Player.Tests'){
  $log=Join-Path $evidence "$variant-$project.log"
  & dotnet build (Join-Path $capture.Source "managed/$project/$project.csproj") @($capture.Arguments) -c $Configuration --nologo --disable-build-servers -m:1 -p:UseSharedCompilation=false -warnaserror `
   "-p:ArtifactsPath=$artifacts" -p:NcmaEnableExtensions=true "-p:NcmaEnableAI=$($variant -eq 'ai-extensions')" "-bl:$evidence/$variant-$project.binlog" *> $log
  $exit=$LASTEXITCODE;Get-Content -LiteralPath $log -Tail 8
  if($exit -ne 0){throw "Extension verification compile failed: $variant/$project; evidence preserved"}
  $path=Join-Path $artifacts "bin/$project/$($Configuration.ToLowerInvariant())"
  $products["$variant/$project"]=@{root=$path;files=@(Get-AiVerificationFiles $path);log=$log;logSha256=(Get-DeploymentHash $log)}
 }
}
if((Get-DeploymentHash (Join-Path $workspace 'out/bin/NcmaEngine.exe')) -cne $exeHash){throw 'Formal EXE changed'}
$receipt=@{kind='compile-only';schemaVersion=2;configuration=$Configuration;sourceInputsSha256=$sourceHash;capturedBuildRoot=$capture.Root;captureManifestSha256=$capture.ManifestSha256;reviewBundleSha256=$capture.Manifest.reviewBundleSha256;products=$products;testsExecuted=$false;formalExeSha256=$exeHash}
[IO.File]::WriteAllText((Join-Path $evidence 'receipt.json'),($receipt|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
Write-Output "Compiled only: $evidence/receipt.json; no tests, package or deployment."
} finally { $capture.Pins.Dispose() }
