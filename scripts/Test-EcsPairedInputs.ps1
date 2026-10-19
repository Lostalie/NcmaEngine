[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$VerifiedClosure)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
. (Join-Path $PSScriptRoot 'EditorDeployment.ps1')
$source = Assert-DeploymentPath $repository $VerifiedClosure
$evidenceRoot = [IO.Path]::GetFullPath((Join-Path $repository 'out\verification\ecs-e3d')) + '\'
if (-not $source.StartsWith($evidenceRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Use an archived paired closure.' }
$manifestPath = Join-Path $source 'paired-inputs.json'
$originalJson = Get-Content -Raw -LiteralPath $manifestPath
$original = $originalJson | ConvertFrom-Json
if ($original.schema -ne 1 -or $original.files.Count -ne 9) { throw 'Wrong original fixture.' }
$originalHashes = @{}
foreach ($file in $original.files) {
    if ([IO.Path]::GetFileName($file.name) -cne $file.name -or $originalHashes.ContainsKey($file.name)) { throw 'Invalid source name.' }
    $hash = Get-DeploymentHash (Join-Path $source $file.name)
    if ($hash -cne $file.sha256) { throw 'Original fixture already corrupted.' }
    $originalHashes[$file.name] = $hash
}
$destination = Join-Path $evidenceRoot ('negative-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $destination | Out-Null
$cases = @('missing-file','altered-bytes','hash','size','duplicate','unknown','schema','variant','extra','json','arguments')
$results = @()
foreach ($case in $cases) {
    $directory = Join-Path $destination $case
    New-Item -ItemType Directory -Path $directory | Out-Null
    $manifest = $originalJson | ConvertFrom-Json
    foreach ($file in $manifest.files) {
        if ($case -eq 'missing-file' -and $file.name -ceq 'Ncma.Scene.dll') { continue }
        Copy-Item -LiteralPath (Join-Path $source $file.name) -Destination (Join-Path $directory $file.name)
    }
    switch ($case) {
        'altered-bytes' { [IO.File]::WriteAllBytes((Join-Path $directory 'Ncma.Scene.dll'), [byte[]]@(1,2,3)) }
        'hash' { $manifest.files[0].sha256 = '0' * 64 }
        'size' { $manifest.files[0].size = $manifest.files[0].size + 1 }
        'duplicate' { $manifest.files += $manifest.files[0] }
        'unknown' { $manifest.files[0].name = '..\outside.dll' }
        'schema' { $manifest.schema = 2 }
        'variant' { $manifest.variant = 'unreviewed' }
        'extra' { [IO.File]::WriteAllText((Join-Path $directory 'extra.txt'), 'negative fixture') }
    }
    $json = if ($case -eq 'json') { '{' } else { $manifest | ConvertTo-Json -Depth 6 }
    [IO.File]::WriteAllText((Join-Path $directory 'paired-inputs.json'), $json, [Text.UTF8Encoding]::new($false))
    [string[]]$modeArguments = if ($case -eq 'arguments') { @('--ecs-paired-smoke','--ecs-paired-measure') } else { @('--ecs-paired-smoke') }
    $log = Join-Path $destination "$case.log"
    $ErrorActionPreference = 'Continue'
    & dotnet (Join-Path $directory 'Ncma.Gameplay.Tests.dll') @modeArguments *> $log
    $result = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    $text = Get-Content -Raw -LiteralPath $log
    if ($result -eq 0 -or $text.Contains('ecs_e3_semantic') -or $text.Contains('E3 paired closure verified before and after workloads.')) {
        throw "Invalid paired fixture was accepted or executed workloads: $case"
    }
    $results += [ordered]@{ case=$case; exitCode=$result; log=$log; sha256=(Get-DeploymentHash $log) }
}
foreach ($name in $originalHashes.Keys) {
    if ((Get-DeploymentHash (Join-Path $source $name)) -cne $originalHashes[$name]) { throw 'Original paired input changed.' }
}
if ((Get-Content -Raw -LiteralPath $manifestPath) -cne $originalJson) { throw 'Original manifest changed.' }
[ordered]@{ schema=1; casesPassed=$results.Count; source=$source; sourceManifestSha256=(Get-DeploymentHash $manifestPath);
    originalInputsUnchanged=$true; results=$results } | ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath (Join-Path $destination 'complete.json') -Encoding UTF8
Write-Output "Paired input rejection: $($results.Count)/$($cases.Count) passed; $destination"
