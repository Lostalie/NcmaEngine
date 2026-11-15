[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][ValidateSet('Debug','Release')][string]$Configuration,
 [Parameter(Mandatory=$true)][string]$CapturedBuildRoot,[Parameter(Mandatory=$true)][string]$DependenciesRoot,
 [Parameter(Mandatory=$true)][string]$ExpectedDependenciesSha256,[Parameter(Mandatory=$true)][string]$DotnetExecutable,
 [Parameter(Mandatory=$true)][string]$GitExecutable,[Parameter(Mandatory=$true)][string]$OfflineCache,
 [Parameter(Mandatory=$true)][string]$VisualStudioRoot,[Parameter(Mandatory=$true)][string]$ToolsetVersion,
 [Parameter(Mandatory=$true)][string]$WindowsSdkRoot,[Parameter(Mandatory=$true)][string]$WindowsSdkVersion)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'OfflinePackageDependencies.ps1')
$workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$git=Get-OfflinePackagePath $GitExecutable;$cache=Get-OfflinePackagePath $OfflineCache
$vs=Get-OfflinePackagePath $VisualStudioRoot;$sdk=Get-OfflinePackagePath $WindowsSdkRoot
if([IO.Path]::GetFileName($git) -cne 'git.exe' -or -not(Test-Path -LiteralPath $git -PathType Leaf)){throw 'sdk_prepare_git_required'}
foreach($directory in @($cache,$vs,$sdk)){if(-not(Test-Path -LiteralPath $directory -PathType Container)){throw 'sdk_prepare_local_prerequisite'}}
foreach($version in @($ToolsetVersion,$WindowsSdkVersion)){if($version.Length -gt 32 -or $version -cnotmatch '^[0-9]+(\.[0-9]+){1,3}$'){throw 'sdk_prepare_version'}}
$capture=Open-CapturedBuild $workspace $CapturedBuildRoot;$dependencies=$null;$productPins=$null;$root=$null
try{
 if($null -ne $capture.Manifest.reviewBundleSha256){throw 'sdk_prepare_unreviewed_baseline_required'}
 $dependencies=Open-OfflinePackageDependencies $workspace $capture $DependenciesRoot $ExpectedDependenciesSha256 $Configuration $false $true $DotnetExecutable 'sdk'
 $root=Assert-DeploymentPath $workspace (Join-Path $workspace ('out/verification/m8-6/sdk-inputs/'+[Guid]::NewGuid().ToString('N')))
 if(Test-Path -LiteralPath $root){throw 'sdk_prepare_output_collision'};$null=New-Item -ItemType Directory -Path $root
 $job=@($dependencies.Jobs);if($job.Count -ne 1){throw 'sdk_prepare_fixed_graph'}
 $arguments=@(Get-OfflinePackageArguments $capture $dependencies.Root $Configuration $job[0])
 $log=Join-Path $root 'compile.log'
 & $DotnetExecutable build (Join-Path $capture.Source $job[0].project) @arguments --no-restore --no-incremental "-bl:$root/compile.binlog" *> $log
 if($LASTEXITCODE -ne 0){throw 'sdk_prepare_compile_failed'}
 $text=Get-Content -Raw -LiteralPath $log
 if($text -notmatch '0 Warning\(s\)' -or $text -notmatch '0 Error\(s\)'){throw 'sdk_prepare_compile_diagnostics'}
 $low=$Configuration.ToLowerInvariant();$wrong=if($Configuration -ceq 'Debug'){'release'}else{'debug'}
 if($text -match ("[\\/]"+$wrong+"(?:_[^\\/]+)?[\\/]")){throw 'sdk_prepare_wrong_configuration'}
 $products=@{};$productPins=[NcmaCapturedBuildPins]::new()
 foreach($name in @('Ncma.Editor.Services.Tests','Ncma.Editor.Mcp','Ncma.Gameplay.Sample')){
  $path=[IO.Path]::GetFullPath((Join-Path $dependencies.Root "artifacts/workflow-sdk/bin/$name/$low"))
  $files=@(Get-DeploymentFiles $workspace $path)
  foreach($file in $files){if((Get-CaptureBytesHash $productPins.Read((Join-Path $path $file.path),67108864)) -cne $file.sha256){throw 'sdk_prepare_product_changed'}}
  $products[$name]=@{root=$path;files=$files}
 }
 $harness=$products['Ncma.Editor.Services.Tests'];$mcp=$products['Ncma.Editor.Mcp']
 $request=[ordered]@{Version=1;Workspace=$workspace;Configuration=$Configuration;BaselineCaptureId=([Guid]::ParseExact((Split-Path $capture.Root -Leaf),'N').ToString('D'));
  BaselineManifestSha256=$capture.ManifestSha256;BaselineSourceSha256=$capture.Manifest.sourceInputsSha256;McpRoot=$mcp.root;DotnetExecutable=$dependencies.Dotnet;GitExecutable=$git;
  OfflineCache=$cache;VisualStudioRoot=$vs;ToolsetVersion=$ToolsetVersion;WindowsSdkRoot=$sdk;WindowsSdkVersion=$WindowsSdkVersion}
 $requestPath=Join-Path $root 'request.json';Write-OfflinePackageJson $requestPath $request
 [byte[]]$requestBytes=$productPins.Read($requestPath,16384)
 # Explicit preparation branch precedes every test dispatch; no test/worker/tool is launched by it.
 & $DotnetExecutable (Join-Path $harness.root 'Ncma.Editor.Services.Tests.dll') --workflow-sdk-prepare-inputs $requestPath *> "$root/observe.log"
 if($LASTEXITCODE -ne 0){throw 'sdk_prepare_original_observation_failed'}
 $inputs=Get-Content -Raw "$root/inputs.json"|ConvertFrom-Json
 $observed=Get-Content -Raw "$root/observation.json"|ConvertFrom-Json
 $harnessHash=Get-DeploymentHash (Join-Path $harness.root 'Ncma.Editor.Services.Tests.dll')
 if(@($inputs.PSObject.Properties).Count -ne 16 -or $inputs.BaselineManifestSha256 -cne $capture.ManifestSha256 -or $inputs.BaselineSourceSha256 -cne $capture.Manifest.sourceInputsSha256 -or
  $inputs.HarnessSha256 -cne $harnessHash -or $inputs.McpRoot -cne $mcp.root -or $inputs.McpFingerprint -cne $observed.mcpFingerprint -or
  $observed.requestSha256 -cne (Get-CaptureBytesHash $requestBytes) -or $observed.testsExecuted -ne $false -or $observed.executionAuthorized -ne $false){throw 'sdk_prepare_observation_binding'}
 foreach($entry in $products.Values){
  $current=@(Get-DeploymentFiles $workspace $entry.root)
  if(@(Compare-Object @($entry.files|ForEach-Object{$_.path+'|'+$_.size+'|'+$_.sha256}) @($current|ForEach-Object{$_.path+'|'+$_.size+'|'+$_.sha256})).Count){throw 'sdk_prepare_product_set_changed'}
 }
 $receipt=[ordered]@{kind='ncma.workflow.sdk-preparation.v1';configuration=$Configuration;root=$root;capturedBuildRoot=$capture.Root;captureManifestSha256=$capture.ManifestSha256;sourceInputsSha256=$capture.Manifest.sourceInputsSha256;
  offlineDependenciesRoot=$dependencies.Root;offlineDependenciesSha256=$dependencies.ReceiptSha256;products=$products;inputsSha256=(Get-DeploymentHash "$root/inputs.json");observationSha256=(Get-DeploymentHash "$root/observation.json");
  logSha256=(Get-DeploymentHash $log);testsExecuted=$false;sdkChainExecuted=$false;installationAuthorized=$false;stageAccepted=$false}
 Write-OfflinePackageJson "$root/receipt.json" $receipt
 [pscustomobject]@{Root=$root;Inputs=Join-Path $root 'inputs.json';InputsSha256=$receipt.inputsSha256;Harness=Join-Path $harness.root 'Ncma.Editor.Services.Tests.dll';ReceiptSha256=(Get-DeploymentHash "$root/receipt.json");TestsExecuted=$false}
}catch{
 if($root -and (Test-Path -LiteralPath $root)){Write-OfflinePackageJson "$root/failure.json" @{exceptionType=$_.Exception.GetType().Name;diagnostic=$_.Exception.Message;testsExecuted=$false;accepted=$false}}
 throw
}finally{
 if($null -ne $productPins){$productPins.Dispose()};if($null -ne $dependencies){$dependencies.Pins.Dispose()};$capture.Pins.Dispose()
}
