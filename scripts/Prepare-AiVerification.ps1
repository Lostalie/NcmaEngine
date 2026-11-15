[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateSet('Debug','Release')][string]$Configuration,
    [Parameter(Mandatory=$true)][string]$DotnetExecutable,[string]$CapturedBuildRoot,
    [string]$DependenciesRoot,[string]$ExpectedDependenciesSha256)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'AiVerificationInputs.ps1')
. (Join-Path $PSScriptRoot 'CapturedBuild.ps1')
. (Join-Path $PSScriptRoot 'VerificationInputStaging.ps1')
$workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if(-not [IO.Path]::IsPathRooted($DotnetExecutable) -or -not (Test-Path -LiteralPath $DotnetExecutable -PathType Leaf) -or
    [IO.Path]::GetFileName($DotnetExecutable) -ine 'dotnet.exe'){throw 'Explicit dotnet.exe prerequisite required'}
if(($CapturedBuildRoot -or $DependenciesRoot -or $ExpectedDependenciesSha256) -and (-not $CapturedBuildRoot -or -not $DependenciesRoot -or -not $ExpectedDependenciesSha256)){throw 'Captured verification requires separate -Scope verification dependency preparation and its exact receipt SHA'}
$generation=[Guid]::NewGuid().ToString('N')
$evidence=Assert-DeploymentPath $workspace (Join-Path $workspace "out/verification/m8-4/$Configuration/managed-inputs/$generation")
New-Item -ItemType Directory -Path $evidence|Out-Null
$formal=Join-Path $workspace 'out/bin/NcmaEngine.exe';$formalHash=Get-DeploymentHash $formal
$capture=$null;$dependencies=$null;$compileWorkspace=$workspace;$captureArgs=@()
$artifacts=Join-Path $workspace ("out/build/windows-ninja-"+$Configuration.ToLowerInvariant()+"/m8-4-ai")
$roots=Get-AiVerificationRoots $workspace $Configuration
try{
    if($CapturedBuildRoot){
        $capture=Open-CapturedBuild $workspace $CapturedBuildRoot;$compileWorkspace=$capture.Source;$captureArgs=@($capture.Arguments)
        $dependencies=Open-OfflinePackageDependencies $workspace $capture $DependenciesRoot $ExpectedDependenciesSha256 $Configuration $true $true $DotnetExecutable 'verification'
    }
    $inputs=@(if($null -ne $capture){$capture.Manifest.files}else{Get-AiCandidateSourceInputs $workspace});$sourceHash=Get-AiCandidateInputHash $inputs
    [IO.File]::WriteAllText("$evidence/build-inputs.json",(ConvertTo-Json -InputObject $inputs -Depth 4),[Text.UTF8Encoding]::new($false))
    # Recompile both exact CTest inputs; no whole Build script/test/publish/cook target.
    foreach($name in 'optional','contracts'){
        $project=if($name -eq 'optional'){'Ncma.Editor.Services.Tests'}else{'Ncma.AI.Tests'}
        [string[]]$extra=@(if($name -eq 'optional'){'-p:NcmaEnableAI=true';"-p:ArtifactsPath=$artifacts"}else{'-p:NcmaEnableAI=false'})
        if($null -ne $dependencies){
            $id=if($name -eq 'optional'){'verify-ai/Ncma.Editor.Services.Tests'}else{'verify-contracts/Ncma.AI.Tests'}
            $job=@($dependencies.Jobs|Where-Object id -ceq $id);if($job.Count -ne 1){throw 'Prepared verification job missing'}
            $preparedArguments=@(Get-OfflinePackageArguments $capture $dependencies.Root $Configuration $job[0])
            & $DotnetExecutable build (Join-Path $compileWorkspace $job[0].project) @preparedArguments --no-restore --no-incremental "-bl:$evidence/$name.binlog" *> "$evidence/$name.log"
        }else{
        & $DotnetExecutable build (Join-Path $compileWorkspace "managed/$project/$project.csproj") @captureArgs --configuration $Configuration --nologo `
            --no-incremental --disable-build-servers -m:1 -p:UseSharedCompilation=false -warnaserror @extra "-bl:$evidence/$name.binlog" *> "$evidence/$name.log"
        }
        $code=$LASTEXITCODE;Get-Content -LiteralPath "$evidence/$name.log" -Tail 8
        if($code -ne 0){throw "$name compile failed; evidence=$evidence"}
    }
    if($null -ne $dependencies){
        $null=Copy-VerificationInputTree $workspace $Configuration $dependencies 'verify-ai/Ncma.Editor.Services.Tests'
        $null=Copy-VerificationInputTree $workspace $Configuration $dependencies 'verify-contracts/Ncma.AI.Tests'
    }
    if($null -eq $capture -and (Get-AiCandidateInputHash @(Get-AiCandidateSourceInputs $workspace)) -cne $sourceHash){throw 'Source inputs changed; no new receipt'}
    if((Get-DeploymentHash $formal) -cne $formalHash){throw 'Formal EXE changed'}
    $products=[ordered]@{}
    foreach($name in 'optional','contracts'){$products[$name]=@{root=$roots[$name];files=@(Get-AiVerificationFiles $roots[$name])}}
    $receipt=[ordered]@{schemaVersion=1;kind='compile-only';configuration=$Configuration;generation=$generation;sourceInputsSha256=$sourceHash;
        capturedBuildRoot=$(if($null -ne $capture){$capture.Root}else{$null});captureManifestSha256=$(if($null -ne $capture){$capture.ManifestSha256}else{$null});
        offlineDependenciesRoot=$(if($null -ne $dependencies){$dependencies.Root}else{$null});offlineDependenciesSha256=$(if($null -ne $dependencies){$dependencies.ReceiptSha256}else{$null});
        testsExecuted=$false;products=$products;formalExeSha256=$formalHash;dotnet=@{path=$DotnetExecutable;sha256=(Get-DeploymentHash $DotnetExecutable)};
        logs=@(@{path="$evidence/optional.log";sha256=(Get-DeploymentHash "$evidence/optional.log")},@{path="$evidence/contracts.log";sha256=(Get-DeploymentHash "$evidence/contracts.log")})}
    $json=$receipt|ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText("$evidence/receipt.json",$json,[Text.UTF8Encoding]::new($false))
    $index=Assert-DeploymentPath $workspace (Join-Path $workspace "out/verification/m8-4/$Configuration/managed-inputs.json")
    $pending="$evidence/receipt.pending.json"
    [IO.File]::WriteAllText($pending,$json,[Text.UTF8Encoding]::new($false))
    if(Test-Path -LiteralPath $index){[IO.File]::Replace($pending,$index,"$evidence/previous-receipt.json")}else{[IO.File]::Move($pending,$index)}
    $null=Assert-AiVerificationReceipt $workspace $Configuration $sourceHash
    Write-Host "COMPILED ONLY: $Configuration managed inputs; source=$sourceHash; evidence=$evidence; tests NOT RUN"
}finally{
    if($null -ne $dependencies){$dependencies.Pins.Dispose()}
    if($null -ne $capture){$capture.Pins.Dispose()}
    if((Get-DeploymentHash $formal) -cne $formalHash){throw 'Formal EXE changed'}
}
