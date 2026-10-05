[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Debug', 'Release')][string]$Configuration,
    [Parameter(Mandatory = $true)][string]$NativePluginRoot
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
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
function Publish-Candidate([string]$project, [string]$destination) {
    & dotnet publish (Join-Path $packageWorkspace $project) --configuration $Configuration --runtime win-x64 --self-contained false `
        --output $destination --nologo -p:UseAppHost=true -p:PublishTrimmed=false -p:PublishAot=false -p:PublishSingleFile=false
    if ($LASTEXITCODE -ne 0) { throw "Candidate publish failed ($project): $LASTEXITCODE" }
}
function Copy-Exact([string]$source, [string]$destination) {
    Assert-PlainPath $source
    Assert-PlainPath $destination
    $item = Get-Item -LiteralPath $source
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Expected a regular deploy file.' }
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
}
function Add-Sample([string]$destination) {
    foreach ($file in @('sample.ncmaproject', 'start.ncmascene')) {
        Copy-Exact (Join-Path $packageWorkspace "engine\assets\examples\player\$file") (Join-Path $destination "sample\$file")
    }
    foreach ($file in @('Ncma.Gameplay.Sample.dll', 'Ncma.Gameplay.Sample.deps.json', 'Ncma.Gameplay.Sample.pdb')) {
        Copy-Exact (Join-Path $packageWorkspace "managed\Ncma.Gameplay.Sample\bin\$Configuration\net8.0\$file") (Join-Path $destination "sample\$file")
    }
}
function Manifest([string]$destination, [string]$product, [object[]]$modules) {
    $files = @(Get-ChildItem -LiteralPath $destination -Recurse -File | Sort-Object FullName | ForEach-Object {
        if (($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse deploy file.' }
        $hashStream = [IO.File]::OpenRead($_.FullName)
        $hashAlgorithm = [Security.Cryptography.SHA256]::Create()
        try { $fileHash = [BitConverter]::ToString($hashAlgorithm.ComputeHash($hashStream)).Replace('-', '') }
        finally { $hashStream.Dispose(); $hashAlgorithm.Dispose() }
        [ordered]@{ path = $_.FullName.Substring($destination.Length + 1).Replace('\', '/'); size = $_.Length; sha256 = $fileHash }
    })
    $requirements = @('compatible .NET runtime', 'Windows x64')
    if ($modules.Count -gt 0) { $requirements += 'native CRT dependencies as built' }
    if (@($modules | Where-Object { $_.id -eq 'ncma.renderer' }).Count -gt 0) { $requirements += 'DX11 GPU/debug layer for graphics validation' }
    if (@($modules | Where-Object { $_.id -eq 'ncma.gui' }).Count -gt 0) { $requirements += 'Windows msyh.ttc font for current Editor candidate' }
    $manifest = [ordered]@{ schemaVersion = 1; product = $product; configuration = $Configuration; rid = 'win-x64'; tfm = 'net8.0';
        publishMode = 'framework-dependent'; production = $false; modules = $modules; files = $files;
        sourceRevision = $packageRevision; sourceDirty = $packageDirty; environment = $requirements;
        selfContainedVerified = $false; manualAcceptance = $false;
        resourceKernels = $(if ($product -eq 'NcmaEngine-editor-candidate') { @(@{ id = 'ncma.character'; abiVersion = 2; path = 'plugins/NcmaNative.dll'; lazy = $true }, @{ id = 'ncma.animation'; abiVersion = 2; path = 'plugins/NcmaNative.dll'; lazy = $true }) } else { @() }) }
    [IO.File]::WriteAllText((Join-Path $destination 'deployment-manifest.json'), ($manifest | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
}
$editor = Join-Path $packageBase 'editor'
$playerNull = Join-Path $packageBase 'player-null'
$playerDx11 = Join-Path $packageBase 'player-dx11'
Publish-Candidate 'managed\Ncma.Editor.App\Ncma.Editor.App.csproj' $editor
Publish-Candidate 'managed\Ncma.Asset.ImportWorker\Ncma.Asset.ImportWorker.csproj' (Join-Path $editor 'tools\import-worker')
Copy-Exact (Join-Path (Split-Path -Parent (Split-Path -Parent $NativePluginRoot)) 'NcmaImportKernel.dll') (Join-Path $editor 'tools\import-worker\NcmaImportKernel.dll')
Publish-Candidate 'managed\Ncma.Player.App\Ncma.Player.App.csproj' $playerNull
New-Item -ItemType Directory -Path $playerDx11 | Out-Null
# Publish outputs are a fresh immutable generation; never merge old bin directories.
Get-ChildItem -LiteralPath $playerNull -File | ForEach-Object { Copy-Exact $_.FullName (Join-Path $playerDx11 $_.Name) }
foreach ($target in @($editor, $playerNull, $playerDx11)) { Add-Sample $target }
foreach ($file in @('NcmaPlatform.dll', 'NcmaRenderer.dll', 'glfw3.dll')) {
    Copy-Exact (Join-Path $NativePluginRoot $file) (Join-Path $editor "plugins\$file")
    Copy-Exact (Join-Path $NativePluginRoot $file) (Join-Path $playerDx11 "plugins\$file")
}
foreach ($file in @('NcmaGui.dll', 'NcmaPhysics.dll')) { Copy-Exact (Join-Path $NativePluginRoot $file) (Join-Path $editor "plugins\$file") }
# FBX numerical resource kernel is only in Editor, never a Player/Gameplay dependency.
$characterKernel = Join-Path (Split-Path -Parent (Split-Path -Parent $NativePluginRoot)) 'NcmaNative.dll'
Copy-Exact $characterKernel (Join-Path $editor 'plugins\NcmaNative.dll')
# Physics stays optional; neither Player variant contains it by default. Python and legacy bridges are absent.
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
$packageRevision = & git -C $packageWorkspace rev-parse --verify HEAD
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($packageRevision)) { throw 'Cannot identify package source revision.' }
$packageRevision = $packageRevision.Trim()
$packageChanges = @(& git -C $packageWorkspace status --porcelain --untracked-files=normal)
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify package source state.' }
$packageDirty = $packageChanges.Count -gt 0
$platformModule = @{ id = 'ncma.platform'; abiMajor = 1; abiMinor = 0; capabilities = 0 }
$rendererModule = @{ id = 'ncma.renderer'; abiMajor = 1; abiMinor = 1; capabilities = 15 }
Manifest $editor 'NcmaEngine-editor-candidate' @($platformModule, $rendererModule, @{ id = 'ncma.gui'; abiMajor = 1; abiMinor = 2; capabilities = 0 }, @{ id = 'ncma.physics'; abiMajor = 1; abiMinor = 1; capabilities = 63 })
Manifest $playerNull 'NcmaPlayer-null-candidate' @()
Manifest $playerDx11 'NcmaPlayer-dx11-candidate' @($platformModule, $rendererModule)
$packageIndex = Join-Path $packageWorkspace "out\verification\m2-7\$Configuration\packages.json"
Assert-PlainPath $packageIndex
New-Item -ItemType Directory -Path (Split-Path -Parent $packageIndex) -Force | Out-Null
$indexJson = [ordered]@{ schemaVersion = 1; editor = $editor; playerNull = $playerNull; playerDx11 = $playerDx11 } | ConvertTo-Json
[IO.File]::WriteAllText($packageIndex, $indexJson, [Text.UTF8Encoding]::new($false))
Write-Host "[Ncma] Framework-dependent candidates staged at $packageBase; production entry untouched."
