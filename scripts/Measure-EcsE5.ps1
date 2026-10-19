[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CandidatePackage,
    [Parameter(Mandatory=$true)][string]$SourceFreeze,
    [ValidateSet('e2b','e4e')][string]$Reference = 'e2b',
    [ValidateSet('common','reads')][string]$Cohort = 'common',
    [switch]$Measure
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
. (Join-Path $PSScriptRoot 'EditorDeployment.ps1')
$contractPath = Join-Path $PSScriptRoot 'ecs-e5-contract.json'
$contractHash = Get-DeploymentHash $contractPath
$contract = Get-Content -Raw -LiteralPath $contractPath | ConvertFrom-Json
if ($contract.id -cne 'ecs-e5-a-v1' -or $contract.runs -ne 3 -or ($Cohort -eq 'reads' -and $Reference -ne 'e4e')) { throw 'Unsupported contract/cohort.' }
$freeze = Assert-DeploymentPath $repository $SourceFreeze
$freezeHash = Get-DeploymentHash $freeze
$frozen = Get-Content -Raw -LiteralPath $freeze | ConvertFrom-Json
function Assert-Frozen {
    if ((Get-DeploymentHash $freeze) -cne $freezeHash) { throw 'Source freeze changed.' }
    $paths = @(& git -C $repository ls-files --cached --others --exclude-standard -- engine/source engine/include engine/build engine/config managed scripts python tests CMakeLists.txt Build.bat NcmaEngine.sln)
    if ($LASTEXITCODE -ne 0) { throw 'Source inventory failed.' }
    if ((@($paths | Sort-Object -Unique) -join '|') -cne (@($frozen | ForEach-Object { $_.path }) -join '|')) { throw 'Source inventory changed.' }
    foreach ($file in $frozen) {
        $path = [IO.Path]::GetFullPath((Join-Path $repository $file.path))
        if (-not $path.StartsWith($repository + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Escaping frozen source path.' }
        if ((Get-DeploymentHash $path) -cne $file.sha) { throw "Frozen source mismatch: $($file.path)" }
    }
}
Assert-Frozen
$referenceRecord = $contract.references.$Reference
$referencePackage = Join-Path $repository ('out\package\m2-7\Release\' + $referenceRecord.generation + '\editor')
$candidate = Assert-DeploymentPath $repository $CandidatePackage
if (-not $candidate.StartsWith((Join-Path $repository 'out\package\m2-7\Release\'), [StringComparison]::OrdinalIgnoreCase) -or
    -not $candidate.EndsWith('\editor', [StringComparison]::OrdinalIgnoreCase)) { throw 'Use a verified Release editor package.' }
if ((Get-DeploymentHash (Join-Path $referencePackage 'deployment-manifest.json')) -cne $referenceRecord.manifestSha256) { throw 'Reference manifest changed.' }
$products = @('Ncma.Runtime.dll','Ncma.Scene.dll','Ncma.Managed.dll','Ncma.Gameplay.dll','Ncma.Movement.dll','Ncma.Editor.Core.dll')
$driverFiles = @('Ncma.Gameplay.Tests.dll','Ncma.Gameplay.Tests.deps.json','Ncma.Gameplay.Tests.runtimeconfig.json')
$driver = Join-Path $repository 'managed\Ncma.Gameplay.Tests\bin\Release\net8.0'
$deps = Get-Content -Raw -LiteralPath (Join-Path $driver 'Ncma.Gameplay.Tests.deps.json') | ConvertFrom-Json
$target = @($deps.targets.PSObject.Properties | Where-Object { $_.Name -ceq $deps.runtimeTarget.name })
if ($target.Count -ne 1) { throw 'Unexpected driver runtime target.' }
$runtimeFiles = @($target[0].Value.PSObject.Properties | ForEach-Object { $_.Value.runtime.PSObject.Properties.Name } | Sort-Object)
if (($runtimeFiles -join '|') -cne ((@($products) + 'Ncma.Gameplay.Tests.dll' | Sort-Object) -join '|')) { throw 'Driver dependency closure changed.' }
$generation = [Guid]::NewGuid().ToString('N')
$destination = Join-Path $repository "out\verification\ecs-e5a\$generation"
if (Test-Path -LiteralPath $destination) { throw 'Do not overwrite evidence.' }
New-Item -ItemType Directory -Path $destination | Out-Null
Copy-Item -LiteralPath $contractPath -Destination (Join-Path $destination 'contract.json')
Copy-Item -LiteralPath $freeze -Destination (Join-Path $destination 'source-freeze.json')
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $destination 'measure.ps1')
$verifier = Join-Path $repository 'python\src\ncma_tools\ecs_e5_evidence.py'
Copy-Item -LiteralPath $verifier -Destination (Join-Path $destination 'verifier.py')
$records = @(); $closures = @{}; $packages = @()
foreach ($variant in @($Reference,'candidate')) {
    $package = if ($variant -eq $Reference) { $referencePackage } else { $candidate }
    [void](Assert-DeploymentContents $repository $package)
    $manifest = Read-DeploymentManifest $package
    if ($manifest.product -cne 'NcmaEngine-editor-candidate' -or $manifest.configuration -cne 'Release' -or $manifest.tfm -cne 'net8.0') { throw 'Wrong source product.' }
    $manifestName = "$variant-package.json"
    Copy-Item -LiteralPath (Join-Path $package 'deployment-manifest.json') -Destination (Join-Path $destination $manifestName)
    $packages += [ordered]@{ variant=$variant; source=$package; archive=$manifestName; sha256=(Get-DeploymentHash (Join-Path $destination $manifestName)) }
    $directory = Join-Path $destination $variant
    New-Item -ItemType Directory -Path $directory | Out-Null
    $files = @()
    foreach ($name in (@($driverFiles) + $products)) {
        $source = if ($name -in $driverFiles) { Join-Path $driver $name } else { Join-Path $package $name }
        $sha = Get-DeploymentHash $source; $size = (Get-Item -LiteralPath $source).Length
        if ($name -in $products) {
            $expected = @($manifest.files | Where-Object { $_.path -ceq $name })
            if ($expected.Count -ne 1 -or $sha -cne $expected[0].sha256 -or $size -ne $expected[0].size) { throw 'Product input mismatch.' }
        }
        Copy-Item -LiteralPath $source -Destination (Join-Path $directory $name)
        $files += [ordered]@{ name=$name; sha256=$sha; size=$size }
        $records += [ordered]@{ variant=$variant; name=$name; source=$source; archive="$variant/$name"; sha256=$sha; size=$size }
    }
    [ordered]@{ schema=2; variant=$variant; cohort=$Cohort; contractSha256=$contractHash; files=$files } | ConvertTo-Json -Depth 6 |
        Set-Content -LiteralPath (Join-Path $directory 'paired-inputs.json') -Encoding UTF8
    $closures[$variant] = $directory
}
$head = & git -C $repository rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source.' }
function Environment-Snapshot {
    $before = @{}; Get-Process | ForEach-Object { $before[$_.Id] = $_.CPU }
    Start-Sleep -Milliseconds 1000
    $processes = @(Get-Process | ForEach-Object {
        $delta = if ($before.ContainsKey($_.Id) -and $null -ne $_.CPU -and $null -ne $before[$_.Id]) { $_.CPU - $before[$_.Id] } else { $null }
        [ordered]@{ id=$_.Id; name=$_.ProcessName; cpuDeltaSeconds=$delta }
    })
    if ($Measure -and @($processes | Where-Object { $_.name -in @('ctest','ninja','cmake','MSBuild','cl','link') -or
        ($_.name -eq 'dotnet' -and $null -ne $_.cpuDeltaSeconds -and $_.cpuDeltaSeconds -gt 0.1) }).Count) {
        throw 'Concurrent build/test/runtime load detected; do not terminate it automatically.'
    }
    $cpu = try { @(Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed,CurrentClockSpeed,LoadPercentage) }
        catch { @([ordered]@{ unavailable=$_.Exception.Message }) }
    $power = & powercfg /getactivescheme 2>&1
    [ordered]@{ utc=[DateTime]::UtcNow.ToString('o'); cpu=$cpu; powerScheme=($power -join "`n"); processes=$processes;
        note='One-second CPU deltas and reported clock only; not continuous turbo/GC pause tracing or proof of idle hardware.' }
}
[ordered]@{ schema=1; generation=$generation; head="$head"; measured=[bool]$Measure; reference=$Reference; cohort=$Cohort;
    contractSha256=$contractHash; sourceFreezeSha256=$freezeHash; scriptSha256=(Get-DeploymentHash $PSCommandPath);
    verifierSha256=(Get-DeploymentHash $verifier); packages=$packages; files=$records } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destination 'inputs.json') -Encoding UTF8
$priorTiered = $env:DOTNET_TieredCompilation; $priorPython = $env:PYTHONPATH
$env:DOTNET_TieredCompilation = '0'
$env:PYTHONPATH = Join-Path $repository 'python\src'
$runs = if ($Measure) { 3 } else { 1 }
$mode = if ($Measure) { '--ecs-e5-measure' } else { '--ecs-e5-smoke' }
$logs = @()
try {
    for ($run = 1; $run -le $runs; ++$run) {
        $order = if ($run % 2) { @($Reference,'candidate') } else { @('candidate',$Reference) }
        foreach ($variant in $order) {
            $envName = "$variant-$run-environment.json"
            Environment-Snapshot | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destination $envName) -Encoding UTF8
            $logName = "$variant-$run.log"; $log = Join-Path $destination $logName
            $started = [DateTime]::UtcNow.ToString('o')
            $ErrorActionPreference = 'Continue'
            & dotnet (Join-Path $closures[$variant] 'Ncma.Gameplay.Tests.dll') $mode *> $log
            $runExit = $LASTEXITCODE
            $ErrorActionPreference = 'Stop'
            if ($runExit -ne 0) { throw "E5 $variant run $run failed; preserve $log" }
            $logs += [ordered]@{ variant=$variant; run=$run; path=$logName; sha256=(Get-DeploymentHash $log);
                environment=$envName; environmentSha256=(Get-DeploymentHash (Join-Path $destination $envName));
                startedUtc=$started; finishedUtc=[DateTime]::UtcNow.ToString('o'); exitCode=$runExit }
            Write-Output "E5 $Cohort $variant run $run completed: $log"
        }
    }
    Assert-Frozen
    foreach ($file in $records) {
        if ((Get-DeploymentHash $file.source) -cne $file.sha256 -or (Get-DeploymentHash (Join-Path $destination $file.archive)) -cne $file.sha256) { throw 'Measurement input changed.' }
    }
    [ordered]@{ schema=1; generation=$generation; measured=[bool]$Measure; runsCompleted=$runs;
        inputsSha256=(Get-DeploymentHash (Join-Path $destination 'inputs.json')); logs=$logs;
        fullPerformanceAccepted=$false; deployed=$false } | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath (Join-Path $destination 'execution.json') -Encoding UTF8
    & python -m ncma_tools.ecs_e5_evidence $destination > (Join-Path $destination 'verification.json')
    if ($LASTEXITCODE -ne 0) { throw 'E5 evidence verification failed; preserve every artifact.' }
    Write-Output "E5 verified evidence: $destination"
}
finally { $env:DOTNET_TieredCompilation = $priorTiered; $env:PYTHONPATH = $priorPython }
