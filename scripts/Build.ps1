[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$SkipTests,
    [switch]$SkipManaged,
    [switch]$SkipPython,

    [switch]$CleanNative,

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
if ($CleanNative) {
    $expectedBuildDirectory = [IO.Path]::GetFullPath((Join-Path $projectRoot "out\build\windows-ninja-$configurationLower"))
    if ([IO.Path]::GetFullPath($buildDirectory) -ne $expectedBuildDirectory) { throw 'Unsafe native build path.' }
}
$cleanBuildOption = if ($CleanNative) { ' --clean-first' } else { '' }
$nativeCommand = @(
    'set "VSCMD_SKIP_SENDTELEMETRY=1"',
    ('call "{0}" >nul' -f $vcvars),
    'set "VSLANG=1033"',
    ('"{0}" -S "{1}" -B "{2}" -G Ninja -DCMAKE_BUILD_TYPE={3} -DCMAKE_MAKE_PROGRAM="{4}" -DNCMA_BUILD_TESTS=ON -DNCMA_BUILD_MANAGED=ON' -f $cmake, $projectRoot, $buildDirectory, $Configuration, $ninja),
    ('"{0}" --build "{1}" --target NcmaCore NcmaNative NcmaArchitectureTests{2}' -f $cmake, $buildDirectory, $cleanBuildOption),
    ('"{0}" --build "{1}"' -f $cmake, $buildDirectory)
) -join ' && '

Write-Host "[Ncma] Building native targets ($Configuration)..."
& $env:ComSpec /d /s /c $nativeCommand
if ($LASTEXITCODE -ne 0) {
    throw "Native build failed with exit code $LASTEXITCODE."
}

# Native DLLs contain kernels only; the C# apphost is deployed after regression.
# Stage the resources used by independent tooling and managed/native smoke tests.
$engineManagedOutput = Join-Path $projectRoot 'out\managed'
New-Item -ItemType Directory -Path $engineManagedOutput -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $buildDirectory 'NcmaPhysics.dll') -Destination $engineManagedOutput -Force
Copy-Item -LiteralPath (Join-Path $buildDirectory 'NcmaNative.dll') `
    -Destination (Join-Path $engineManagedOutput 'NcmaNative.dll') -Force
Copy-Item -LiteralPath (Join-Path $buildDirectory 'NcmaAnimationKernel.dll') -Destination (Join-Path $buildDirectory 'm2\plugins\NcmaAnimationKernel.dll') -Force

if (-not $SkipTests) {
    Write-Host '[Ncma] Running native architecture tests...'
    & $ctest --test-dir $buildDirectory --output-on-failure -E 'NcmaPoseManagedTests|NcmaAssetImportTests|NcmaAssetTests|NcmaPhysicsTests|NcmaRenderingTests|NcmaKernelReferenceCapture|NcmaCandidateGraphicsSmoke|NcmaPresentationTests|NcmaCandidatePresentationSmoke|NcmaInteropTests|NcmaApplicationServiceTests|NcmaEditorServiceTests|NcmaPlayerTests|NcmaEditorDeploymentTests|NcmaManagedHeadlessTests|NcmaSceneDocumentTests|NcmaSceneRenderingTests|NcmaEditorCoreTests|NcmaGameplayTests|NcmaEditorTransportTests'
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
    & dotnet build (Join-Path $projectRoot 'managed\Ncma.Animation.Tests\Ncma.Animation.Tests.csproj') --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Animation pose/clock tests build failed with exit code $LASTEXITCODE." }
    & dotnet build (Join-Path $projectRoot 'managed\Ncma.Asset.Import.Tests\Ncma.Asset.Import.Tests.csproj') --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Asset import tests build failed with exit code $LASTEXITCODE." }
    & dotnet build (Join-Path $projectRoot 'managed\Ncma.Assets.Tests\Ncma.Assets.Tests.csproj') --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Asset tests build failed with exit code $LASTEXITCODE." }
    & dotnet build (Join-Path $projectRoot 'managed\Ncma.Physics.Tests\Ncma.Physics.Tests.csproj') --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Physics tests build failed with exit code $LASTEXITCODE." }
    & dotnet build (Join-Path $projectRoot 'managed\Ncma.Player.Tests\Ncma.Player.Tests.csproj') --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Player tests build failed with exit code $LASTEXITCODE." }
    Write-Host '[Ncma] Building the independent C# headless runtime and command tests...'
    & dotnet build (Join-Path $projectRoot 'managed\Ncma.Scene.Rendering.Tests\Ncma.Scene.Rendering.Tests.csproj') --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Scene rendering foundation tests build failed with exit code $LASTEXITCODE." }
    & dotnet build (Join-Path $projectRoot 'managed\Ncma.Rendering.Tests\Ncma.Rendering.Tests.csproj') --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Rendering test build failed with exit code $LASTEXITCODE." }
    & dotnet build (Join-Path $projectRoot 'managed\Ncma.Presentation.Tests\Ncma.Presentation.Tests.csproj') --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Presentation test build failed with exit code $LASTEXITCODE." }
    & dotnet build (Join-Path $projectRoot 'managed\Ncma.Editor.App\Ncma.Editor.App.csproj') --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Managed candidate build failed with exit code $LASTEXITCODE." }
    & dotnet build (Join-Path $projectRoot 'managed\Ncma.Editor.Services.Tests\Ncma.Editor.Services.Tests.csproj') --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Managed editor service tests build failed with exit code $LASTEXITCODE." }
    $candidateOutput = Join-Path $projectRoot "out\verification\m2\candidate\$Configuration"
    $candidatePlugins = Join-Path $candidateOutput 'plugins'
    New-Item -ItemType Directory -Path $candidateOutput, $candidatePlugins -Force | Out-Null
    Copy-Item -Path (Join-Path $projectRoot "managed\Ncma.Editor.App\bin\$Configuration\net8.0\*") -Destination $candidateOutput -Force
    Copy-Item -Path (Join-Path $buildDirectory 'm2\plugins\*.dll') -Destination $candidatePlugins -Force
    Copy-Item -LiteralPath (Join-Path $buildDirectory 'NcmaNative.dll') -Destination (Join-Path $candidatePlugins 'NcmaNative.dll') -Force
    Copy-Item -LiteralPath (Join-Path $buildDirectory 'NcmaAnimationKernel.dll') -Destination (Join-Path $candidatePlugins 'NcmaAnimationKernel.dll') -Force
    & dotnet build (Join-Path $projectRoot 'managed\Ncma.Interop.Tests\Ncma.Interop.Tests.csproj') --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Interop test build failed with exit code $LASTEXITCODE." }
    & dotnet build (Join-Path $projectRoot 'managed\Ncma.Application.Tests\Ncma.Application.Tests.csproj') --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Managed application service tests build failed with exit code $LASTEXITCODE." }
    & dotnet build (Join-Path $projectRoot 'managed\Ncma.Runtime.Tests\Ncma.Runtime.Tests.csproj') --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Managed headless runtime build failed with exit code $LASTEXITCODE."
    }
    & dotnet build (Join-Path $projectRoot 'managed\Ncma.Scene.Tests\Ncma.Scene.Tests.csproj') --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Managed scene document build failed with exit code $LASTEXITCODE." }
    & dotnet build (Join-Path $projectRoot 'managed\Ncma.Editor.Core.Tests\Ncma.Editor.Core.Tests.csproj') --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Managed editor core build failed with exit code $LASTEXITCODE." }
    & dotnet build (Join-Path $projectRoot 'managed\Ncma.Gameplay.Tests\Ncma.Gameplay.Tests.csproj') --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Managed gameplay tests build failed with exit code $LASTEXITCODE." }
    $managedProject = Join-Path $projectRoot 'managed\Ncma.Managed.SmokeTest\Ncma.Managed.SmokeTest.csproj'
    $engineManagedOutput = Join-Path $projectRoot 'out\managed'
    Write-Host '[Ncma] Building the C# gameplay API...'
    & dotnet build $managedProject --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Managed build failed with exit code $LASTEXITCODE."
    }
    & dotnet build (Join-Path $projectRoot 'managed\Ncma.Editor.Transport.Tests\Ncma.Editor.Transport.Tests.csproj') --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Editor transport tests build failed with exit code $LASTEXITCODE." }
    $mcpOutput = Join-Path $projectRoot 'out\managed\editor-mcp'
    $transportTestsOutput = Join-Path $projectRoot 'out\managed\editor-transport-tests'
    New-Item -ItemType Directory -Path $mcpOutput, $transportTestsOutput -Force | Out-Null
    Copy-Item -Path (Join-Path $projectRoot "managed\Ncma.Editor.Mcp\bin\$Configuration\net8.0\*") -Destination $mcpOutput -Force
    Copy-Item -Path (Join-Path $projectRoot "managed\Ncma.Editor.Transport.Tests\bin\$Configuration\net8.0\*") -Destination $transportTestsOutput -Force
    Build-GameplayAssembly
    & (Join-Path $PSScriptRoot 'Package-M2_7.ps1') -Configuration $Configuration -NativePluginRoot (Join-Path $buildDirectory 'm2\plugins')
    New-Item -ItemType Directory -Path $engineManagedOutput -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $buildDirectory 'NcmaNative.dll') `
        -Destination (Join-Path $engineManagedOutput 'NcmaNative.dll') -Force

    if (-not $SkipTests) {
        $managedOutput = Join-Path $projectRoot "managed\Ncma.Managed.SmokeTest\bin\$Configuration\net8.0"
        Copy-Item -LiteralPath (Join-Path $buildDirectory 'NcmaPhysics.dll') -Destination $managedOutput -Force
        Copy-Item -LiteralPath (Join-Path $buildDirectory 'NcmaNative.dll') `
            -Destination (Join-Path $managedOutput 'NcmaNative.dll') -Force
        Write-Host '[Ncma] Running the managed/native ABI smoke test...'
        & dotnet (Join-Path $managedOutput 'Ncma.Managed.SmokeTest.dll')
        if ($LASTEXITCODE -ne 0) {
            throw "Managed/native smoke test failed with exit code $LASTEXITCODE."
        }
        Write-Host '[Ncma] Running managed headless commands and managed application tests...'
        & $ctest --test-dir $buildDirectory --output-on-failure -R 'NcmaPoseManagedTests|NcmaAssetImportTests|NcmaAssetTests|NcmaPhysicsTests|NcmaRenderingTests|NcmaKernelReferenceCapture|NcmaCandidateGraphicsSmoke|NcmaPresentationTests|NcmaCandidatePresentationSmoke|NcmaInteropTests|NcmaApplicationServiceTests|NcmaEditorServiceTests|NcmaPlayerTests|NcmaEditorDeploymentTests|NcmaManagedHeadlessTests|NcmaSceneDocumentTests|NcmaSceneRenderingTests|NcmaEditorCoreTests|NcmaGameplayTests|NcmaEditorTransportTests'
        if ($LASTEXITCODE -ne 0) {
            throw "Managed application regression failed with exit code $LASTEXITCODE."
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
        $env:NCMA_TEST_NATIVE_BUILD = $buildDirectory
        & python -m unittest discover -s (Join-Path $projectRoot 'python\tests') -v
        if ($LASTEXITCODE -ne 0) {
            throw "Python tooling tests failed with exit code $LASTEXITCODE."
        }
    }
}

if (-not $SkipTests -and -not $SkipManaged -and -not $SkipPython) {
    $profileDirectory = Join-Path $projectRoot "out\verification\m2-8\profiles-$Configuration"
    New-Item -ItemType Directory -Path $profileDirectory -Force | Out-Null
    $profileAssembly = Join-Path $projectRoot "managed\Ncma.Gameplay.Tests\bin\$Configuration\net8.0\Ncma.Gameplay.Tests.dll"
    for ($profileRound = 1; $profileRound -le 3; $profileRound++) {
        Write-Host "[Ncma] M2.8 retained runtime fixture measurement round $profileRound/3..."
        $profileLog = Join-Path $profileDirectory "runtime-round-$profileRound.log"
        & dotnet $profileAssembly --benchmark --pressure-benchmark 2>&1 | Out-File -LiteralPath $profileLog -Encoding utf8
        if ($LASTEXITCODE -ne 0) { throw "M2.8 runtime profile failed with exit code $LASTEXITCODE." }
    }
    Write-Host '[Ncma] Recording M2.8 read-only candidate/consumer preflight (not H8 acceptance)...'
    & python -m ncma_tools.m2_audit --root $projectRoot --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "M2.8 preflight failed with exit code $LASTEXITCODE." }
    . (Join-Path $PSScriptRoot 'EditorDeployment.ps1')
    $packageIndex = Get-Content -Raw -LiteralPath (Join-Path $projectRoot "out\verification\m2-7\$Configuration\packages.json") | ConvertFrom-Json
    Invoke-EditorDeployment -Workspace $projectRoot -Package $packageIndex.editor -Destination (Join-Path $projectRoot 'out\bin')
    & python -m ncma_tools.m2_audit --root $projectRoot --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Post-deployment audit failed.' }
}
Write-Host "[Ncma] Build succeeded: $buildDirectory" -ForegroundColor Green
