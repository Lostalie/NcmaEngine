[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Debug', 'Release')][string]$Configuration,
    [Parameter(Mandatory = $true)][string]$NativePluginRoot,
    [switch]$IncludeAI,
    [string]$CMakeExecutable
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'AiCandidatePackage.ps1')
$packageWorkspace = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$packageAllowedRoot = Join-Path $packageWorkspace 'out'
$packageBase = Join-Path $packageWorkspace "out\package\m2-7\$Configuration\$([Guid]::NewGuid().ToString('N'))"
if (-not $packageBase.StartsWith($packageAllowedRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe package target.' }
$NativePluginRoot = [IO.Path]::GetFullPath($NativePluginRoot)
$expectedPlugins = Join-Path $packageWorkspace "out\build\windows-ninja-$($Configuration.ToLowerInvariant())\m2\plugins"
if ($NativePluginRoot -ne $expectedPlugins) { throw 'Plugin/configuration root mismatch.' }
function Assert-PlainPath([string]$path) {
    for ($part = [IO.DirectoryInfo]::new([IO.Path]::GetFullPath($path)); $null -ne $part; $part = $part.Parent) {
        if ($part.Exists -and ($part.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse package path.' }
    }
}
Assert-PlainPath $packageBase
Assert-PlainPath $NativePluginRoot
for ($packageParent = Get-Item -LiteralPath $packageWorkspace; $null -ne $packageParent; $packageParent = $packageParent.Parent) {
    if (($packageParent.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse workspace.' }
}
New-Item -ItemType Directory -Path $packageBase | Out-Null
$packageInputs = @(); $packageInputHash = $null
if ($IncludeAI) {
    if ([string]::IsNullOrWhiteSpace($CMakeExecutable) -or -not [IO.Path]::IsPathRooted($CMakeExecutable) -or
        !(Test-Path -LiteralPath $CMakeExecutable -PathType Leaf) -or [IO.Path]::GetFileName($CMakeExecutable) -ine 'cmake.exe') { throw 'Explicit CMake executable required for fresh native candidates.' }
    $packageInputs = @(Get-AiCandidateSourceInputs $packageWorkspace)
    $packageInputHash = Get-AiCandidateInputHash $packageInputs
    [IO.File]::WriteAllText((Join-Path $packageBase 'build-inputs.json'), (ConvertTo-Json -InputObject $packageInputs -Depth 4), [Text.UTF8Encoding]::new($false))
    # Normal native build refreshes dependency-tracked outputs; no test/deploy targets.
    $nativeBuild = Split-Path -Parent (Split-Path -Parent $NativePluginRoot)
    & $CMakeExecutable --build $nativeBuild --config $Configuration --parallel 4 *> (Join-Path $packageBase 'native-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'AI candidate native build failed; evidence retained.' }
    & dotnet build (Join-Path $packageWorkspace 'managed/Ncma.Gameplay.Sample/Ncma.Gameplay.Sample.csproj') --configuration $Configuration --nologo `
        "-p:ArtifactsPath=$packageBase/build/default" -p:NcmaEnableAI=false
    if ($LASTEXITCODE -ne 0) { throw 'AI candidate sample build failed.' }
}
function Publish-Candidate([string]$project, [string]$destination, [bool]$ai = $false) {
    $variantArgs = @()
    if ($IncludeAI) {
        $variant = if ($ai) { 'ai' } else { 'default' }
        $variantArgs = @("-p:ArtifactsPath=$packageBase/build/$variant", "-p:NcmaEnableAI=$($ai.ToString().ToLowerInvariant())")
    }
    & dotnet publish (Join-Path $packageWorkspace $project) --configuration $Configuration --runtime win-x64 --self-contained false `
        --output $destination --nologo -p:UseAppHost=true -p:PublishTrimmed=false -p:PublishAot=false -p:PublishSingleFile=false @variantArgs
    if ($LASTEXITCODE -ne 0) { throw "Candidate publish failed ($project): $LASTEXITCODE" }
}
function Copy-Exact([string]$source, [string]$destination) {
    Assert-PlainPath $source
    Assert-PlainPath $destination
    $item = Get-Item -LiteralPath $source
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Expected a regular deploy file.' }
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    # Fresh generation only; never overwrite an unexpected file or follow a link.
    [IO.File]::Copy($source, $destination, $false)
    if ((Get-DeploymentHash $source) -cne (Get-DeploymentHash $destination)) { throw 'Candidate copy hash mismatch.' }
}
function Add-Sample([string]$destination) {
    foreach ($file in @('sample.ncmaproject', 'start.ncmascene')) {
        Copy-Exact (Join-Path $packageWorkspace "engine\assets\examples\player\$file") (Join-Path $destination "sample\$file")
    }
    foreach ($file in @('Ncma.Gameplay.Sample.dll', 'Ncma.Gameplay.Sample.deps.json', 'Ncma.Gameplay.Sample.pdb')) {
        $sampleDirectory = if ($IncludeAI) { "$packageBase/build/default/bin/Ncma.Gameplay.Sample/$($Configuration.ToLowerInvariant())" } else { "$packageWorkspace/managed/Ncma.Gameplay.Sample/bin/$Configuration/net8.0" }
        Copy-Exact (Join-Path $sampleDirectory $file) (Join-Path $destination "sample\$file")
    }
}
function Manifest([string]$destination, [string]$product, [object[]]$modules) {
    # Existing bounded walker checks directories BEFORE descending into them.
    $files = @(Get-DeploymentFiles $packageWorkspace $destination | Sort-Object path)
    $requirements = @('compatible .NET runtime', 'Windows x64')
    if ($modules.Count -gt 0) { $requirements += 'native CRT dependencies as built' }
    if (@($modules | Where-Object { $_.id -eq 'ncma.renderer' }).Count -gt 0) { $requirements += 'DX11 GPU/debug layer for graphics validation' }
    if (@($modules | Where-Object { $_.id -eq 'ncma.gui' }).Count -gt 0) { $requirements += 'Windows msyh.ttc font for current Editor candidate' }
    $manifest = [ordered]@{ schemaVersion = 1; product = $product; configuration = $Configuration; rid = 'win-x64'; tfm = 'net8.0';
        publishMode = 'framework-dependent'; production = $false; modules = $modules; files = $files;
        sourceRevision = $packageRevision; sourceDirty = $packageDirty; environment = $requirements;
        selfContainedVerified = $false; manualAcceptance = $false;
        # Keep zero/one/many kernels a JSON array; $() unwraps the single Player entry.
        resourceKernels = @(if ($product -in @('NcmaEngine-editor-candidate','NcmaEngine-ai-editor-candidate')) {
            @{ id = 'ncma.character'; abiVersion = 2; path = 'plugins/NcmaNative.dll'; lazy = $true }
            @{ id = 'ncma.animation'; abiVersion = 2; path = 'plugins/NcmaNative.dll'; lazy = $true }
            @{ id = 'ncma.pose'; abiVersion = 1; path = 'plugins/NcmaAnimationKernel.dll'; lazy = $true }
            @{ id = 'ncma.text'; abiVersion = 1; path = 'plugins/NcmaText.dll'; lazy = $true }
        } elseif ($product -in @('NcmaPlayer-dx11-candidate', 'NcmaPlayer-dx11-physics-candidate')) {
            @{ id = 'ncma.pose'; abiVersion = 1; path = 'plugins/NcmaAnimationKernel.dll'; lazy = $true }
            @{ id = 'ncma.text'; abiVersion = 1; path = 'plugins/NcmaText.dll'; lazy = $true }
        }) }
    if ($IncludeAI) {
        $manifest.sourceInputsSha256 = $packageInputHash
        $manifest.aiEnabled = $product -eq 'NcmaEngine-ai-editor-candidate'
        Assert-AiCandidateDependencies $packageWorkspace $destination $manifest.aiEnabled
    }
    [IO.File]::WriteAllText((Join-Path $destination 'deployment-manifest.json'), ($manifest | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
}
$editor = Join-Path $packageBase 'editor'
$playerNull = Join-Path $packageBase 'player-null'
$playerDx11 = Join-Path $packageBase 'player-dx11'
$playerNullPhysics = Join-Path $packageBase 'player-null-physics'
$playerDx11Physics = Join-Path $packageBase 'player-dx11-physics'
$cookArgs = @()
if ($IncludeAI) { $cookArgs = @("-p:ArtifactsPath=$packageBase/build/default", '-p:NcmaEnableAI=false') }
& dotnet build (Join-Path $packageWorkspace 'managed\Ncma.Shader.Cook\Ncma.Shader.Cook.csproj') --configuration $Configuration --nologo @cookArgs
if ($LASTEXITCODE -ne 0) { throw 'Shader cooker build failed.' }
$shaderCooker = Join-Path $packageWorkspace "managed\Ncma.Shader.Cook\bin\$Configuration\net8.0\Ncma.Shader.Cook.dll"
if ($IncludeAI) { $shaderCooker = "$packageBase/build/default/bin/Ncma.Shader.Cook/$($Configuration.ToLowerInvariant())/Ncma.Shader.Cook.dll" }
function Add-Shaders([string]$destination,[string]$profile) {
    $cooked = Join-Path $packageBase ("shaders-" + $profile)
    & dotnet $shaderCooker $NativePluginRoot $cooked $profile
    if ($LASTEXITCODE -ne 0) { throw 'Shader package cook failed.' }
    foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $cooked 'assets\shaders') -File)) {
        Copy-Exact $file.FullName (Join-Path $destination ('assets\shaders\' + $file.Name))
    }
    foreach ($name in @('defaults.json','neutral-v1.nce')) {
        Copy-Exact (Join-Path $cooked ('assets\environments\' + $name)) (Join-Path $destination ('assets\environments\' + $name))
    }
    # Exact bounded C1 closed library; no recursive copy of unrelated author files.
    $postDirectory = Join-Path $cooked 'assets\postprocess'
    $postFiles = @(Get-ChildItem -LiteralPath $postDirectory -File)
    if ($postFiles.Count -ne 26) { throw 'Expected 24 Tone packages, Bloom NPH1 and one post library index.' }
    foreach ($file in $postFiles) { Copy-Exact $file.FullName (Join-Path $destination ('assets\postprocess\' + $file.Name)) }
    $shadowDirectory = Join-Path $cooked 'assets\shadows'
    $shadowFiles = @(Get-ChildItem -LiteralPath $shadowDirectory -File)
    if ($shadowFiles.Count -ne 5) { throw 'Expected four High shadow packages and one exact shadow library index.' }
    foreach ($file in $shadowFiles) { Copy-Exact $file.FullName (Join-Path $destination ('assets\shadows\' + $file.Name)) }
}
Publish-Candidate 'managed\Ncma.Editor.App\Ncma.Editor.App.csproj' $editor
Publish-Candidate 'managed\Ncma.Asset.ImportWorker\Ncma.Asset.ImportWorker.csproj' (Join-Path $editor 'tools\import-worker')
Copy-Exact (Join-Path (Split-Path -Parent (Split-Path -Parent $NativePluginRoot)) 'NcmaImportKernel.dll') (Join-Path $editor 'tools\import-worker\NcmaImportKernel.dll')
Publish-Candidate 'managed\Ncma.Player.App\Ncma.Player.App.csproj' $playerNull
New-Item -ItemType Directory -Path $playerDx11 | Out-Null
# Publish outputs are a fresh immutable generation; never merge old bin directories.
Get-ChildItem -LiteralPath $playerNull -File | ForEach-Object { Copy-Exact $_.FullName (Join-Path $playerDx11 $_.Name) }
foreach ($target in @($editor, $playerNull, $playerDx11)) { Add-Sample $target }
Add-Shaders $editor 'editor'
Add-Shaders $playerDx11 'player'
foreach ($file in @('NcmaPlatform.dll', 'NcmaRenderer.dll', 'glfw3.dll')) {
    Copy-Exact (Join-Path $NativePluginRoot $file) (Join-Path $editor "plugins\$file")
    Copy-Exact (Join-Path $NativePluginRoot $file) (Join-Path $playerDx11 "plugins\$file")
}
foreach ($file in @('NcmaGui.dll', 'NcmaPhysics.dll', 'NcmaText.dll')) { Copy-Exact (Join-Path $NativePluginRoot $file) (Join-Path $editor "plugins\$file") }
Copy-Exact (Join-Path $NativePluginRoot 'NcmaText.dll') (Join-Path $playerDx11 'plugins\NcmaText.dll')
# FBX numerical resource kernel is only in Editor, never a Player/Gameplay dependency.
$characterKernel = Join-Path (Split-Path -Parent (Split-Path -Parent $NativePluginRoot)) 'NcmaNative.dll'
Copy-Exact $characterKernel (Join-Path $editor 'plugins\NcmaNative.dll')
# Runtime pose numerics are independent of the Editor-only FBX import/preview kernel.
foreach ($target in @($editor, $playerDx11)) {
    Copy-Exact (Join-Path (Split-Path -Parent (Split-Path -Parent $NativePluginRoot)) 'NcmaAnimationKernel.dll') (Join-Path $target 'plugins\NcmaAnimationKernel.dll')
}
# Physics stays optional; original lightweight Players remain unchanged. Python/legacy bridges are absent.
foreach ($target in @($editor, $playerDx11)) {
    foreach ($name in @('glfw', 'spdlog', 'eigen')) {
        $licenseCandidates = @(Get-ChildItem -LiteralPath (Join-Path $packageWorkspace "engine\sdk\$name") -File | Where-Object { $_.Name -match '^(LICENSE|COPYING)' })
        if ($licenseCandidates.Count -eq 0) { throw "Missing license: $name" }
        foreach ($license in $licenseCandidates) { Copy-Exact $license.FullName (Join-Path $target "licenses\$name-$($license.Name).txt") }
    }
}
foreach ($name in @('imgui', 'box2d', 'JoltPhysics', 'ufbx')) {
    $licenses = @(Get-ChildItem -LiteralPath (Join-Path $packageWorkspace "engine\sdk\$name") -File | Where-Object { $_.Name -match '^(LICENSE|COPYING)' })
    if ($licenses.Count -eq 0) { throw "Missing license: $name" }
    foreach ($license in $licenses) { Copy-Exact $license.FullName (Join-Path $editor "licenses\$name-$($license.Name).txt") }
}
# Explicit solver variants use the exact freshly published base files. Never borrow a
# source-tree plugin at runtime or put physics into the default Null/flat application.
foreach ($pair in @(@($playerNull, $playerNullPhysics), @($playerDx11, $playerDx11Physics))) {
    $variantSource = [IO.Path]::GetFullPath($pair[0]); $variantTarget = [IO.Path]::GetFullPath($pair[1])
    if (-not $variantSource.StartsWith($packageBase + '\', [StringComparison]::OrdinalIgnoreCase) -or
        -not $variantTarget.StartsWith($packageBase + '\', [StringComparison]::OrdinalIgnoreCase) -or
        (Test-Path -LiteralPath $variantTarget)) { throw 'Invalid fresh physics variant paths.' }
    Assert-PlainPath $variantSource; Assert-PlainPath $variantTarget
    $variantFiles = @(Get-DeploymentFiles $packageWorkspace $variantSource)
    if ($variantFiles.Count -lt 1 -or $variantFiles.Count -gt 8192) { throw 'Physics variant file budget.' }
    foreach ($file in $variantFiles) {
        Copy-Exact (Join-Path $variantSource $file.path) (Join-Path $variantTarget $file.path)
    }
    Copy-Exact (Join-Path $NativePluginRoot 'NcmaPhysics.dll') (Join-Path $variantTarget 'plugins\NcmaPhysics.dll')
    foreach ($name in @('box2d', 'JoltPhysics') + $(if ($variantTarget -eq $playerNullPhysics) { @('eigen', 'spdlog') } else { @() })) {
        $licenses = @(Get-ChildItem -LiteralPath (Join-Path $packageWorkspace "engine\sdk\$name") -File | Where-Object { $_.Name -match '^(LICENSE|COPYING)' })
        if ($licenses.Count -eq 0) { throw "Missing physics variant license: $name" }
        foreach ($license in $licenses) { Copy-Exact $license.FullName (Join-Path $variantTarget "licenses\$name-$($license.Name).txt") }
    }
}
$packageRevision = & git -C $packageWorkspace rev-parse --verify HEAD
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($packageRevision)) { throw 'Cannot identify package source revision.' }
$packageRevision = $packageRevision.Trim()
$packageChanges = @(& git -C $packageWorkspace status --porcelain --untracked-files=normal)
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify package source state.' }
$packageDirty = $packageChanges.Count -gt 0
$platformModule = @{ id = 'ncma.platform'; abiMajor = 1; abiMinor = 0; capabilities = 0 }
$rendererModule = @{ id = 'ncma.renderer'; abiMajor = 1; abiMinor = 1; capabilities = 15 }
Manifest $editor 'NcmaEngine-editor-candidate' @($platformModule, $rendererModule, @{ id = 'ncma.gui'; abiMajor = 1; abiMinor = 8; capabilities = 0 }, @{ id = 'ncma.physics'; abiMajor = 1; abiMinor = 1; capabilities = 63 })
Manifest $playerNull 'NcmaPlayer-null-candidate' @()
Manifest $playerDx11 'NcmaPlayer-dx11-candidate' @($platformModule, $rendererModule)
$physicsModule = @{ id = 'ncma.physics'; abiMajor = 1; abiMinor = 2; capabilities = 255 }
Manifest $playerNullPhysics 'NcmaPlayer-null-physics-candidate' @($physicsModule)
Manifest $playerDx11Physics 'NcmaPlayer-dx11-physics-candidate' @($platformModule, $rendererModule, $physicsModule)
if ($IncludeAI) {
    $editorAi = Join-Path $packageBase 'editor-ai'
    Publish-Candidate 'managed\Ncma.Editor.App\Ncma.Editor.App.csproj' $editorAi $true
    # Only shared non-managed payload from this very generation; no old package/bin merge.
    foreach ($directory in @('plugins','assets','sample','tools','licenses')) {
        $sourceDirectory = Join-Path $editor $directory
        foreach ($file in @(Get-DeploymentFiles $packageWorkspace $sourceDirectory)) {
            Copy-Exact (Join-Path $sourceDirectory $file.path) (Join-Path $editorAi "$directory/$($file.path)")
        }
    }
    Manifest $editorAi 'NcmaEngine-ai-editor-candidate' @($platformModule, $rendererModule,
        @{ id = 'ncma.gui'; abiMajor = 1; abiMinor = 8; capabilities = 0 },
        @{ id = 'ncma.physics'; abiMajor = 1; abiMinor = 1; capabilities = 63 })
    $afterInputs = @(Get-AiCandidateSourceInputs $packageWorkspace)
    if ((Get-AiCandidateInputHash $afterInputs) -cne $packageInputHash) { throw 'Build inputs changed during candidate preparation; retain generation without publishing index.' }
    foreach ($pair in @(@($editor,$false),@($editorAi,$true))) {
        Assert-AiCandidateManifest $packageWorkspace $pair[0] $Configuration $pair[1] $packageInputHash (Get-DeploymentHash (Join-Path $pair[0] 'deployment-manifest.json'))
    }
    $aiIndex = Join-Path $packageWorkspace "out/verification/m8-4/$Configuration/packages.json"
    $null = Assert-DeploymentPath $packageWorkspace $aiIndex
    $aiPending = Join-Path $packageBase 'ai-packages.pending.json'
    $aiProducts = @{}
    foreach ($entry in @(@('editor',$editor),@('editorAi',$editorAi),@('playerNull',$playerNull))) {
        $aiProducts[$entry[0]] = @{path=$entry[1];manifestSha256=(Get-DeploymentHash (Join-Path $entry[1] 'deployment-manifest.json'))}
    }
    [IO.File]::WriteAllText($aiPending, (@{schemaVersion=1;configuration=$Configuration;sourceInputsSha256=$packageInputHash;
        sourceInputsFile=(Join-Path $packageBase 'build-inputs.json');products=$aiProducts;tested=$false;production=$false} | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    New-Item -ItemType Directory -Path (Split-Path $aiIndex) -Force | Out-Null
    if (Test-Path -LiteralPath $aiIndex) { [IO.File]::Replace($aiPending,$aiIndex,(Join-Path $packageBase 'previous-ai-packages.json')) }
    else { [IO.File]::Move($aiPending,$aiIndex) }
}
$packageIndex = Join-Path $packageWorkspace "out\verification\m2-7\$Configuration\packages.json"
Assert-PlainPath $packageIndex
New-Item -ItemType Directory -Path (Split-Path -Parent $packageIndex) -Force | Out-Null
if ($IncludeAI -and (Test-Path -LiteralPath $packageIndex)) { Copy-Exact $packageIndex (Join-Path $packageBase 'previous-default-packages.json') }
$indexJson = [ordered]@{ schemaVersion = 1; editor = $editor; playerNull = $playerNull; playerDx11 = $playerDx11; playerNullPhysics = $playerNullPhysics; playerDx11Physics = $playerDx11Physics } | ConvertTo-Json
[IO.File]::WriteAllText($packageIndex, $indexJson, [Text.UTF8Encoding]::new($false))
Write-Host "[Ncma] Framework-dependent candidates staged at $packageBase; production entry untouched."
