[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateSet('Debug','Release')][string]$Configuration,
    [Parameter(Mandatory=$true)][string]$DotnetExecutable)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'AiVerificationInputs.ps1')
$workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if(-not [IO.Path]::IsPathRooted($DotnetExecutable) -or -not (Test-Path -LiteralPath $DotnetExecutable -PathType Leaf) -or
    [IO.Path]::GetFileName($DotnetExecutable) -ine 'dotnet.exe'){throw 'Explicit dotnet.exe prerequisite required'}
$generation=[Guid]::NewGuid().ToString('N')
$evidence=Assert-DeploymentPath $workspace (Join-Path $workspace "out/verification/m8-4/$Configuration/managed-inputs/$generation")
New-Item -ItemType Directory -Path $evidence|Out-Null
$formal=Join-Path $workspace 'out/bin/NcmaEngine.exe';$formalHash=Get-DeploymentHash $formal
$inputs=@(Get-AiCandidateSourceInputs $workspace);$sourceHash=Get-AiCandidateInputHash $inputs
[IO.File]::WriteAllText("$evidence/build-inputs.json",(ConvertTo-Json -InputObject $inputs -Depth 4),[Text.UTF8Encoding]::new($false))
$artifacts=Join-Path $workspace ("out/build/windows-ninja-"+$Configuration.ToLowerInvariant()+"/m8-4-ai")
$roots=Get-AiVerificationRoots $workspace $Configuration
try{
    # Recompile both exact CTest inputs; no whole Build script/test/publish/cook target.
    foreach($name in 'optional','contracts'){
        $project=if($name -eq 'optional'){'Ncma.Editor.Services.Tests'}else{'Ncma.AI.Tests'}
        [string[]]$extra=@(if($name -eq 'optional'){'-p:NcmaEnableAI=true';"-p:ArtifactsPath=$artifacts"}else{'-p:NcmaEnableAI=false'})
        & $DotnetExecutable build (Join-Path $workspace "managed/$project/$project.csproj") --configuration $Configuration --nologo `
            --no-incremental --disable-build-servers -m:1 -p:UseSharedCompilation=false -warnaserror @extra "-bl:$evidence/$name.binlog" *> "$evidence/$name.log"
        $code=$LASTEXITCODE;Get-Content -LiteralPath "$evidence/$name.log" -Tail 8
        if($code -ne 0){throw "$name compile failed; evidence=$evidence"}
    }
    if((Get-AiCandidateInputHash @(Get-AiCandidateSourceInputs $workspace)) -cne $sourceHash){throw 'Source inputs changed; no new receipt'}
    if((Get-DeploymentHash $formal) -cne $formalHash){throw 'Formal EXE changed'}
    $products=[ordered]@{}
    foreach($name in 'optional','contracts'){$products[$name]=@{root=$roots[$name];files=@(Get-AiVerificationFiles $roots[$name])}}
    $receipt=[ordered]@{schemaVersion=1;kind='compile-only';configuration=$Configuration;generation=$generation;sourceInputsSha256=$sourceHash;
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
    if((Get-DeploymentHash $formal) -cne $formalHash){throw 'Formal EXE changed'}
}
