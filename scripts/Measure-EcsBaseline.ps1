[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [ValidateRange(1, 5)][int]$Runs = 3
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$assembly = Join-Path $repository "managed\Ncma.Gameplay.Tests\bin\$Configuration\net8.0\Ncma.Gameplay.Tests.dll"
if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw 'Build with Build.bat before measuring.' }
function Hash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '') }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}
$generation = [Guid]::NewGuid().ToString('N')
$destination = Join-Path $repository "out\verification\ecs-e0\$generation"
New-Item -ItemType Directory -Path $destination | Out-Null
Push-Location $repository
try {
    $head = & git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source commit.' }
    # Do not recurse through ignored build outputs just to discard them afterwards.
    $sourcePaths = @(& git ls-files --cached --others --exclude-standard -- managed)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate source inventory.' }
    $sourcePaths = @($sourcePaths | Where-Object { [IO.Path]::GetExtension($_) -in '.cs', '.csproj', '.props', '.targets' } | Sort-Object -Unique)
    $sources = @($sourcePaths | ForEach-Object { [ordered]@{ path = $_; sha256 = Hash (Join-Path $repository $_) } })
    $binaries = @(Get-ChildItem -LiteralPath (Split-Path -Parent $assembly) -File |
        Sort-Object Name | ForEach-Object { [ordered]@{ name = $_.Name; sha256 = Hash $_.FullName } })
    $manifest = [ordered]@{ schema = 1; generation = $generation; configuration = $Configuration; head = "$head";
        startedUtc = [DateTime]::UtcNow.ToString('o'); runs = $Runs; scriptSha256 = Hash $PSCommandPath;
        sources = $sources; binaries = $binaries; scope = 'CPU baseline, not deployment or full engine acceptance' }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destination 'inputs.json') -Encoding UTF8
    for ($run = 1; $run -le $Runs; $run++) {
        $log = Join-Path $destination "run-$run.log"
        & dotnet $assembly --ecs-baseline *> $log
        if ($LASTEXITCODE -ne 0) { throw "E0 run $run failed; retained $log" }
        $rows = @(Get-Content -LiteralPath $log | Where-Object { $_.StartsWith('{') } | ForEach-Object { ConvertFrom-Json $_ } |
            Where-Object { $_.kind -eq 'ecs_e0_baseline' })
        if ($rows.Count -ne 33 -or @($rows | Where-Object { $_.samples -ne 100 }).Count -ne 0) { throw "Incomplete E0 output: $log" }
        Write-Host "E0 $Configuration run $run passed: $log"
    }
    foreach ($source in $sources) {
        if ((Hash (Join-Path $repository $source.path)) -ne $source.sha256) { throw "Source changed during measurement: $($source.path)" }
    }
    $finalPaths = @(& git ls-files --cached --others --exclude-standard -- managed)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot recheck source inventory.' }
    $finalPaths = @($finalPaths | Where-Object { [IO.Path]::GetExtension($_) -in '.cs', '.csproj', '.props', '.targets' } | Sort-Object -Unique)
    if (($sourcePaths -join "`n") -cne ($finalPaths -join "`n")) { throw 'Source inventory changed during measurement.' }
    foreach ($binary in $binaries) {
        if ((Hash (Join-Path (Split-Path -Parent $assembly) $binary.name)) -ne $binary.sha256) { throw "Binary changed during measurement: $($binary.name)" }
    }
    [ordered]@{ schema = 1; generation = $generation; finishedUtc = [DateTime]::UtcNow.ToString('o'); runsPassed = $Runs;
        inputsSha256 = Hash (Join-Path $destination 'inputs.json'); logs = @(Get-ChildItem -LiteralPath $destination -Filter 'run-*.log' |
            ForEach-Object { [ordered]@{ name = $_.Name; sha256 = Hash $_.FullName } }) } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $destination 'complete.json') -Encoding UTF8
    Write-Host "E0 evidence: $destination"
}
finally { Pop-Location }
