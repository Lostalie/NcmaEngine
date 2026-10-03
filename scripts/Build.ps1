[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$SkipTests,
    [switch]$SkipManaged,
    [switch]$SkipPython,

    [switch]$GameplayOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $PSScriptRoot

function Build-GameplayAssembly {
    $gameplayProjectPath = Join-Path $projectRoot 'managed\Ncma.Gameplay.Sample\Ncma.Gameplay.Sample.csproj'
    & dotnet build $gameplayProjectPath --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Sample gameplay build failed with exit code $LASTEXITCODE." }
    $gameplayDestination = Join-Path $projectRoot 'out\managed'
    New-Item -ItemType Directory -Path $gameplayDestination -Force | Out-Null
    foreach ($file in @('Ncma.Gameplay.Sample.dll', 'Ncma.Gameplay.Sample.pdb', 'Ncma.Gameplay.Sample.deps.json')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot "managed\Ncma.Gameplay.Sample\bin\$Configuration\net8.0\$file") `
            -Destination (Join-Path $gameplayDestination $file) -Force
    }
}

if ($GameplayOnly) {
    $gameplayPath = $env:Path
    Remove-Item Env:PATH -ErrorAction SilentlyContinue
    $env:Path = $gameplayPath
    Build-GameplayAssembly
    Write-Host '[Ncma] Gameplay rebuilt. Use Gameplay > Reload C# Assembly in the editor.'
    exit 0
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'

if (-not (Test-Path -LiteralPath $vswhere)) {
    throw 'Visual Studio Installer (vswhere.exe) was not found. Install Visual Studio 2022 with Desktop development with C++.'
}

$vsRoot = & $vswhere -latest -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -property installationPath

if ([string]::IsNullOrWhiteSpace($vsRoot)) {
    throw 'Visual Studio 2022 C++ tools were not found. Install the Desktop development with C++ workload.'
}

$vcvars = Join-Path $vsRoot 'VC\Auxiliary\Build\vcvars64.bat'
$cmake = Join-Path $vsRoot 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
$ctest = Join-Path $vsRoot 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\ctest.exe'
$ninja = Join-Path $vsRoot 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe'

foreach ($requiredFile in @($vcvars, $cmake, $ctest, $ninja)) {
    if (-not (Test-Path -LiteralPath $requiredFile)) {
        throw "Required build tool was not found: $requiredFile"
    }
}

$configurationLower = $Configuration.ToLowerInvariant()
$buildDirectory = Join-Path $projectRoot "out\build\windows-ninja-$configurationLower"
$nativeCommand = @(
    'set VSCMD_SKIP_SENDTELEMETRY=1',
    ('call "{0}" >nul' -f $vcvars),
    'set VSLANG=1033',
    ('"{0}" -S "{1}" -B "{2}" -G Ninja -DCMAKE_BUILD_TYPE={3} -DCMAKE_MAKE_PROGRAM="{4}" -DNCMA_BUILD_TESTS=ON -DNCMA_BUILD_MANAGED=ON -DNCMA_BUILD_EDITOR=ON' -f $cmake, $projectRoot, $buildDirectory, $Configuration, $ninja),
    ('"{0}" --build "{1}"' -f $cmake, $buildDirectory)
) -join ' && '

Write-Host "[Ncma] Building native targets ($Configuration)..."
& $env:ComSpec /d /s /c $nativeCommand
if ($LASTEXITCODE -ne 0) {
    throw "Native build failed with exit code $LASTEXITCODE."
}

# Both gameplay frontends borrow the play world through this versioned native bridge.
# Deploy before editor/native Python smoke tests, independently of the managed build.
$engineManagedOutput = Join-Path $projectRoot 'out\managed'
New-Item -ItemType Directory -Path $engineManagedOutput -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $buildDirectory 'NcmaNative.dll') `
    -Destination (Join-Path $engineManagedOutput 'NcmaNative.dll') -Force

if (-not $SkipTests) {
    Write-Host '[Ncma] Running native architecture tests...'
    & $ctest --test-dir $buildDirectory --output-on-failure -E 'NcmaManagedHostSmokeTest|NcmaEditorGameplaySmokeTest'
    if ($LASTEXITCODE -ne 0) {
        throw "Native tests failed with exit code $LASTEXITCODE."
    }
}

# Some desktop hosts provide both Path and PATH. .NET/MSBuild treats those as duplicate
# keys, so normalize them only in this process before invoking dotnet.
$effectivePath = $env:Path
Remove-Item Env:PATH -ErrorAction SilentlyContinue
$env:Path = $effectivePath

if (-not $SkipManaged) {
    $managedProject = Join-Path $projectRoot 'managed\Ncma.Managed.SmokeTest\Ncma.Managed.SmokeTest.csproj'
    $managedHostProject = Join-Path $projectRoot 'managed\Ncma.Managed.Host\Ncma.Managed.Host.csproj'
    $engineManagedOutput = Join-Path $projectRoot 'out\managed'
    Write-Host '[Ncma] Building the C# gameplay API...'
    & dotnet build $managedProject --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Managed build failed with exit code $LASTEXITCODE."
    }
    & dotnet build $managedHostProject --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Managed host build failed with exit code $LASTEXITCODE."
    }
    Build-GameplayAssembly
    New-Item -ItemType Directory -Path $engineManagedOutput -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $buildDirectory 'NcmaNative.dll') `
        -Destination (Join-Path $engineManagedOutput 'NcmaNative.dll') -Force
    foreach ($managedFile in @(
        'Ncma.Managed.Host.dll',
        'Ncma.Managed.Host.pdb',
        'Ncma.Managed.Host.deps.json',
        'Ncma.Managed.Host.runtimeconfig.json',
        'Ncma.Managed.dll',
        'Ncma.Managed.pdb'
    )) {
        $source = Join-Path $projectRoot "managed\Ncma.Managed.Host\bin\$Configuration\net8.0\$managedFile"
        Copy-Item -LiteralPath $source -Destination (Join-Path $engineManagedOutput $managedFile) -Force
    }

    if (-not $SkipTests) {
        $managedOutput = Join-Path $projectRoot "managed\Ncma.Managed.SmokeTest\bin\$Configuration\net8.0"
        Copy-Item -LiteralPath (Join-Path $buildDirectory 'NcmaNative.dll') `
            -Destination (Join-Path $managedOutput 'NcmaNative.dll') -Force
        Write-Host '[Ncma] Running the managed/native ABI smoke test...'
        & dotnet (Join-Path $managedOutput 'Ncma.Managed.SmokeTest.dll')
        if ($LASTEXITCODE -ne 0) {
            throw "Managed/native smoke test failed with exit code $LASTEXITCODE."
        }
        Write-Host '[Ncma] Running the embedded .NET gameplay host smoke test...'
        & $ctest --test-dir $buildDirectory --output-on-failure -R 'NcmaManagedHostSmokeTest|NcmaEditorGameplaySmokeTest'
        if ($LASTEXITCODE -ne 0) {
            throw "Embedded .NET host smoke test failed with exit code $LASTEXITCODE."
        }
    }
}

if (-not $SkipPython) {
    Write-Host '[Ncma] Checking the Python tooling entry point...'
    $env:PYTHONPATH = Join-Path $projectRoot 'python\src'
    & python -m ncma_tools.cli inspect $projectRoot | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Python tooling check failed with exit code $LASTEXITCODE."
    }
    if (-not $SkipTests -and -not $SkipManaged) {
        Write-Host '[Ncma] Testing Python project manifest, FBX tooling and animation MCP (including stdio protocol)...'
        & python -m unittest discover -s (Join-Path $projectRoot 'python\tests') -v
        if ($LASTEXITCODE -ne 0) {
            throw "Python tooling tests failed with exit code $LASTEXITCODE."
        }
    }
}

Write-Host "[Ncma] Build succeeded: $buildDirectory" -ForegroundColor Green
