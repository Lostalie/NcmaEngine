[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CandidatePackage,
    [ValidateRange(1,3)][int]$Runs = 3,
    [switch]$Measure
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
. (Join-Path $PSScriptRoot 'EditorDeployment.ps1')
$package = Assert-DeploymentPath $repository $CandidatePackage
if (-not $package.StartsWith((Join-Path $repository 'out\package\m2-7\Release\'), [StringComparison]::OrdinalIgnoreCase) -or
    -not $package.EndsWith('\editor', [StringComparison]::OrdinalIgnoreCase)) { throw 'Use a checked Release editor candidate.' }
[void](Assert-DeploymentContents $repository $package)
$manifest = Read-DeploymentManifest $package
if ($manifest.product -cne 'NcmaEngine-editor-candidate' -or $manifest.configuration -cne 'Release') { throw 'Unexpected product.' }
$destination = Join-Path $repository ('out\verification\ecs-e5c\read-diagnostic-' + [Guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $destination) { throw 'Preserve existing diagnostics.' }
New-Item -ItemType Directory -Path $destination | Out-Null
$closure = Join-Path $destination 'candidate'
New-Item -ItemType Directory -Path $closure | Out-Null
$products = @('Ncma.Runtime','Ncma.Scene','Ncma.Managed','Ncma.Gameplay','Ncma.Movement','Ncma.Editor.Core')
$drivers = @('Ncma.Gameplay.Tests.dll','Ncma.Gameplay.Tests.deps.json','Ncma.Gameplay.Tests.runtimeconfig.json')
$driverRoot = Join-Path $repository 'managed\Ncma.Gameplay.Tests\bin\Release\net8.0'
$files = @()
foreach ($name in (@($products | ForEach-Object { $_ + '.dll' }) + $drivers)) {
    $source = if ($name -in $drivers) { Join-Path $driverRoot $name } else { Join-Path $package $name }
    if ($name -notin $drivers) {
        $record = @($manifest.files | Where-Object { $_.path -ceq $name })
        if ($record.Count -ne 1 -or (Get-DeploymentHash $source) -cne $record[0].sha256) { throw 'Manifest product mismatch.' }
        # Current diagnostics may only inspect the products just built from current source.
        if ((Get-DeploymentHash (Join-Path $driverRoot $name)) -cne $record[0].sha256) { throw 'Current build differs from supplied candidate.' }
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $closure $name)
    $files += [ordered]@{ name=$name; size=(Get-Item -LiteralPath $source).Length; sha256=(Get-DeploymentHash $source) }
}
$paths = @(& git -C $repository ls-files --cached --others --exclude-standard -- engine/source engine/include engine/build engine/config managed scripts python tests CMakeLists.txt Build.bat NcmaEngine.sln)
if ($LASTEXITCODE -ne 0) { throw 'Source inventory failed.' }
$frozen = @($paths | Sort-Object -Unique | ForEach-Object {
    [ordered]@{ path=$_; sha=(Get-DeploymentHash (Join-Path $repository $_)) }
})
$frozen | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $destination 'source-freeze.json') -Encoding UTF8
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $destination 'diagnose.ps1')
Copy-Item -LiteralPath (Join-Path $package 'deployment-manifest.json') -Destination (Join-Path $destination 'package.json')
$contract = [ordered]@{ id='ecs-e5c-read-diagnostic-v1'; measured=[bool]$Measure; runs=$Runs; counts=@(0,100,1000,4096); warmup=12; samples=100;
    tieredCompilation='0'; scope='Read lifecycle diagnostics; invalidation/setup excluded, retained counts are identity array payload, not heap residency or formal acceptance';
    sourceFreezeSha256=(Get-DeploymentHash (Join-Path $destination 'source-freeze.json'));
    scriptSha256=(Get-DeploymentHash (Join-Path $destination 'diagnose.ps1')); packageSha256=(Get-DeploymentHash (Join-Path $destination 'package.json')) }
if (-not $Measure) { $contract.counts=@(0,8); $contract.warmup=1; $contract.samples=2 }
$contract | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $destination 'contract.json') -Encoding UTF8
[ordered]@{ schema=2; variant='candidate'; cohort='read-diagnostics'; contractSha256=(Get-DeploymentHash (Join-Path $destination 'contract.json')); files=$files } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $closure 'paired-inputs.json') -Encoding UTF8
$previousTiered = $env:DOTNET_TieredCompilation
$env:DOTNET_TieredCompilation='0'
$logs = @()
try {
    for ($run=1; $run -le $Runs; $run++) {
        $before=@{}; Get-Process | ForEach-Object { $before[$_.Id]=$_.CPU }
        Start-Sleep -Milliseconds 1000
        $processes=@(Get-Process | ForEach-Object {
            $delta=if ($before.ContainsKey($_.Id) -and $null -ne $_.CPU -and $null -ne $before[$_.Id]) { $_.CPU-$before[$_.Id] } else { $null }
            [ordered]@{ id=$_.Id; name=$_.ProcessName; cpuDeltaSeconds=$delta }
        })
        if ($Measure -and @($processes | Where-Object { $_.name -in @('ctest','ninja','cmake','MSBuild','cl','link') -or
            ($_.name -eq 'dotnet' -and $null -ne $_.cpuDeltaSeconds -and $_.cpuDeltaSeconds -gt 0.1) }).Count) { throw 'Concurrent build/test detected; do not terminate automatically.' }
        $cpu=try { @(Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors,CurrentClockSpeed,LoadPercentage) }
            catch { @([ordered]@{ unavailable=$_.Exception.Message }) }
        $power=& powercfg /getactivescheme 2>&1
        $environment=Join-Path $destination "environment-$run.json"
        [ordered]@{ utc=[DateTime]::UtcNow.ToString('o'); cpu=$cpu; powerScheme=($power -join "`n"); processes=$processes;
            scope='Preflight only; not continuous clock/load evidence or proof of idle hardware.' } | ConvertTo-Json -Depth 6 |
            Set-Content -LiteralPath $environment -Encoding UTF8
        $mode=if ($Measure) { '--ecs-e5-read-diagnose' } else { '--ecs-e5-read-diagnose-smoke' }
        $log=Join-Path $destination "diagnostic-$run.log"
        $ErrorActionPreference='Continue'
        & dotnet (Join-Path $closure 'Ncma.Gameplay.Tests.dll') $mode *> $log
        $runExit=$LASTEXITCODE
        $ErrorActionPreference='Stop'
        if ($runExit -ne 0) { throw "Diagnostic failed; preserve $log" }
        if (-not (Select-String -LiteralPath $log -SimpleMatch 'E5 diagnostic closure verified before and after workloads.' -Quiet)) { throw 'Missing terminal closure verification.' }
        $fixtureMarker = if ($Measure) { 'E5 read lifecycle diagnostic fixture checks: 40/40 passed; measured=True.' } else { 'E5 read lifecycle diagnostic fixture checks: 20/20 passed; measured=False.' }
        if (-not (Select-String -LiteralPath $log -SimpleMatch $fixtureMarker -Quiet)) { throw 'Incomplete read diagnostic fixtures.' }
        $logs += [ordered]@{ run=$run; path=[IO.Path]::GetFileName($log); sha256=(Get-DeploymentHash $log); exitCode=$runExit;
            environment=[IO.Path]::GetFileName($environment); environmentSha256=(Get-DeploymentHash $environment) }
        Write-Output "Diagnostic run $run completed: $log"
    }
    $afterPaths=@(& git -C $repository ls-files --cached --others --exclude-standard -- engine/source engine/include engine/build engine/config managed scripts python tests CMakeLists.txt Build.bat NcmaEngine.sln)
    if ($LASTEXITCODE -ne 0 -or ((@($afterPaths | Sort-Object -Unique) -join '|') -cne (@($frozen | ForEach-Object { $_.path }) -join '|'))) { throw 'Source inventory changed.' }
    foreach ($file in $frozen) { if ((Get-DeploymentHash (Join-Path $repository $file.path)) -cne $file.sha) { throw 'Source changed during diagnostics.' } }
    foreach ($file in $files) { if ((Get-DeploymentHash (Join-Path $closure $file.name)) -cne $file.sha256) { throw 'Diagnostic closure changed.' } }
    [ordered]@{ schema=1; measured=[bool]$Measure; logs=$logs; functionalGate=$false; formalPerformanceAccepted=$false; deployed=$false } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'execution.json') -Encoding UTF8
    Write-Output "Diagnostic archive: $destination"
} finally { $env:DOTNET_TieredCompilation=$previousTiered }
