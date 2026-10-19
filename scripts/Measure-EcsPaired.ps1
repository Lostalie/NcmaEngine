[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CandidatePackage,
    [ValidateRange(1,5)][int]$Runs = 3,
    [switch]$Measure
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
. (Join-Path $PSScriptRoot 'EditorDeployment.ps1')
$oldPackage = Join-Path $repository 'out\package\m2-7\Release\47f4cceb6906453e9ba274442320fde8\editor'
$candidate = Assert-DeploymentPath $repository $CandidatePackage
if (-not $candidate.StartsWith((Join-Path $repository 'out\package\m2-7\Release\'), [StringComparison]::OrdinalIgnoreCase) -or
    -not $candidate.EndsWith('\editor', [StringComparison]::OrdinalIgnoreCase) -or $candidate -eq $oldPackage) {
    throw 'Select a distinct verified Release editor candidate package.'
}
if ((Get-DeploymentHash (Join-Path $oldPackage 'deployment-manifest.json')) -cne 'C8A6B120D70F3A5DEF4F0088088E5239D1DC4EBD831A7AD1E295682C8C80F8F8') {
    throw 'The pre-E3 reference manifest differs from the delivered E2-B package.'
}
$products = @('Ncma.Runtime.dll','Ncma.Scene.dll','Ncma.Managed.dll','Ncma.Gameplay.dll','Ncma.Movement.dll','Ncma.Editor.Core.dll')
$driverFiles = @('Ncma.Gameplay.Tests.dll','Ncma.Gameplay.Tests.deps.json','Ncma.Gameplay.Tests.runtimeconfig.json')
$driver = Join-Path $repository 'managed\Ncma.Gameplay.Tests\bin\Release\net8.0'
$deps = Get-Content -Raw -LiteralPath (Join-Path $driver 'Ncma.Gameplay.Tests.deps.json') | ConvertFrom-Json
$target = @($deps.targets.PSObject.Properties | Where-Object { $_.Name -ceq $deps.runtimeTarget.name })
if ($target.Count -ne 1) { throw 'Unexpected driver runtime target.' }
$runtimeFiles = @($target[0].Value.PSObject.Properties | ForEach-Object { $_.Value.runtime.PSObject.Properties.Name } | Sort-Object)
if (($runtimeFiles -join '|') -cne ((@($products) + 'Ncma.Gameplay.Tests.dll' | Sort-Object) -join '|')) {
    throw 'Driver dependency closure changed; review explicitly before measuring.'
}
$generation = [Guid]::NewGuid().ToString('N')
$destination = Join-Path $repository "out\verification\ecs-e3d\$generation"
if (Test-Path -LiteralPath $destination) { throw 'Do not overwrite existing evidence.' }
New-Item -ItemType Directory -Path $destination | Out-Null
$records = @(); $closures = @{}
foreach ($variant in @('e2b','candidate')) {
    $package = if ($variant -eq 'e2b') { $oldPackage } else { $candidate }
    [void](Assert-DeploymentContents $repository $package)
    $manifest = Read-DeploymentManifest $package
    if ($manifest.product -cne 'NcmaEngine-editor-candidate' -or $manifest.configuration -cne 'Release' -or $manifest.tfm -cne 'net8.0') {
        throw 'Wrong source product identity.'
    }
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
        $copy = Join-Path $directory $name
        Copy-Item -LiteralPath $source -Destination $copy
        if ((Get-DeploymentHash $copy) -cne $sha -or (Get-Item -LiteralPath $copy).Length -ne $size) { throw 'Copied input differs.' }
        $files += [ordered]@{ name=$name; sha256=$sha; size=$size }
        $records += [ordered]@{ variant=$variant; name=$name; source=$source; archive=$copy; sha256=$sha; size=$size }
    }
    [ordered]@{ schema=1; variant=$variant; files=$files } | ConvertTo-Json -Depth 6 |
        Set-Content -LiteralPath (Join-Path $directory 'paired-inputs.json') -Encoding UTF8
    $closures[$variant] = $directory
}
$head = & git -C $repository rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source.' }
[ordered]@{ schema=1; generation=$generation; head="$head"; measured=[bool]$Measure; requestedRuns=$Runs;
    oldCommit='cb50b4f25e9acfaa85ddf9568717ec30e1e254ea'; oldPackage=$oldPackage; candidatePackage=$candidate;
    scriptSha256=(Get-DeploymentHash $PSCommandPath); oldManifestSha256=(Get-DeploymentHash (Join-Path $oldPackage 'deployment-manifest.json'));
    candidateManifestSha256=(Get-DeploymentHash (Join-Path $candidate 'deployment-manifest.json')); files=$records } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destination 'inputs.json') -Encoding UTF8
$priorTiered = $env:DOTNET_TieredCompilation
$env:DOTNET_TieredCompilation = '0'
$effectiveRuns = if ($Measure) { $Runs } else { 1 }
$mode = if ($Measure) { '--ecs-paired-measure' } else { '--ecs-paired-smoke' }
$expectedRows = if ($Measure) { 33 } else { 11 }
$logs = @()
try {
    for ($run = 1; $run -le $effectiveRuns; ++$run) {
        $order = if ($run % 2) { @('e2b','candidate') } else { @('candidate','e2b') }
        $semantics = @{}
        foreach ($variant in $order) {
            $log = Join-Path $destination "$variant-$run.log"
            $ErrorActionPreference = 'Continue'
            & dotnet (Join-Path $closures[$variant] 'Ncma.Gameplay.Tests.dll') $mode *> $log
            $runExit = $LASTEXITCODE
            $ErrorActionPreference = 'Stop'
            if ($runExit -ne 0) { throw "Paired $variant run $run failed; preserve $log" }
            $text = Get-Content -Raw -LiteralPath $log
            if (-not $text.Contains('E3 paired closure verified before and after workloads.')) { throw 'Missing executed closure proof.' }
            $rows = @($text -split '\r?\n' | Where-Object { $_.StartsWith('{') } | ForEach-Object { $_ | ConvertFrom-Json })
            $environment = @($rows | Where-Object { $_.kind -ceq 'ecs_e3_paired_environment' })
            if ($environment.Count -ne 1 -or $environment[0].variant -cne $variant -or $environment[0].tieredCompilation -cne '0') { throw 'Wrong paired environment.' }
            $outcomes = @($rows | Where-Object { $_.kind -ceq 'ecs_e3_semantic' } | Sort-Object workload,objects)
            if ($outcomes.Count -ne $expectedRows -or @($outcomes | Group-Object workload,objects).Count -ne $expectedRows) { throw 'Missing/duplicate semantic output.' }
            $samples = @($rows | Where-Object { $_.kind -ceq 'ecs_e0_baseline' })
            if ($Measure -and ($samples.Count -ne 33 -or @($samples | Where-Object { $_.samples -ne 100 -or $_.rawMs.Count -ne 100 -or $_.rawAllocatedThreadBytes.Count -ne 100 }).Count -ne 0)) {
                throw 'Incomplete raw paired samples.'
            }
            $semantics[$variant] = $outcomes | ConvertTo-Json -Depth 6 -Compress
            $logs += [ordered]@{ variant=$variant; run=$run; path=$log; sha256=(Get-DeploymentHash $log); semanticRows=$outcomes.Count; sampleRows=$samples.Count }
            Write-Output "Paired $variant run $run passed: $log"
        }
        if ($semantics.e2b -cne $semantics.candidate) { throw 'Old/candidate final semantic states differ.' }
    }
    foreach ($file in $records) {
        if ((Get-DeploymentHash $file.source) -cne $file.sha256 -or (Get-DeploymentHash $file.archive) -cne $file.sha256) { throw 'Paired inputs changed during execution.' }
    }
    [ordered]@{ schema=1; generation=$generation; measured=[bool]$Measure; runsPassed=$effectiveRuns; pairedSemanticsEqual=$true;
        inputsSha256=(Get-DeploymentHash (Join-Path $destination 'inputs.json')); logs=$logs;
        e3Complete=$false; fullPerformanceAccepted=$false; deployed=$false } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destination 'complete.json') -Encoding UTF8
    Write-Output "E3 paired evidence: $destination"
}
finally { $env:DOTNET_TieredCompilation = $priorTiered }
