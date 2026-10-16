[CmdletBinding()]
param([Parameter(Mandatory = $true)][ValidateSet('Debug', 'Release')][string]$Configuration)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$moduleWorkspace = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$moduleGeneration = [Guid]::NewGuid().ToString('N')
$moduleRoot = Join-Path $moduleWorkspace "out\package\modules\$Configuration\$moduleGeneration\client"
$moduleIndexRoot = Join-Path $moduleWorkspace "out\verification\m8-2\$Configuration"
function Assert-ModulePath([string]$path) {
    $resolved = [IO.Path]::GetFullPath($path)
    if (-not $resolved.StartsWith($moduleWorkspace + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Module path outside workspace.' }
    for ($part = [IO.DirectoryInfo]::new($resolved); $null -ne $part; $part = $part.Parent) {
        if ($part.Exists -and ($part.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Module package link rejected.' }
    }
}
function Get-ModuleSha256([string]$path) {
    $stream = [IO.File]::OpenRead($path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}
Assert-ModulePath $moduleRoot
Assert-ModulePath $moduleIndexRoot
if (Test-Path -LiteralPath $moduleRoot) { throw 'New module generation already exists.' }
New-Item -ItemType Directory -Path $moduleRoot | Out-Null
# Publish the actual optional library, never a test executable. No interpreter/model is installed.
& dotnet publish (Join-Path $moduleWorkspace 'managed\Ncma.Modules.Grpc\Ncma.Modules.Grpc.csproj') `
    --configuration $Configuration --no-restore --output $moduleRoot --nologo `
    -p:PublishTrimmed=false -p:PublishAot=false -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw 'Optional client library publish failed.' }
$moduleExpected = @('Ncma.Modules.dll', 'Ncma.Modules.Grpc.dll', 'Grpc.Core.Api.dll', 'Grpc.Net.Client.dll', 'Grpc.Net.Common.dll',
    'Microsoft.Extensions.DependencyInjection.Abstractions.dll', 'Microsoft.Extensions.Logging.Abstractions.dll') | Sort-Object
$moduleActual = @(Get-ChildItem -LiteralPath $moduleRoot -File -Filter '*.dll' | Select-Object -ExpandProperty Name | Sort-Object)
if (@(Compare-Object $moduleExpected $moduleActual).Count) { throw 'Unexpected optional client runtime closure.' }
$moduleLockPath = Join-Path $moduleWorkspace 'managed\Ncma.Modules.Grpc\packages.lock.json'
Copy-Item -LiteralPath $moduleLockPath -Destination (Join-Path $moduleRoot 'packages.lock.json')
$moduleLock = Get-Content -Raw -LiteralPath $moduleLockPath | ConvertFrom-Json
$moduleArchiveLockPath = Join-Path $moduleWorkspace 'managed\Ncma.Modules.Grpc\package-archives.lock.json'
$moduleArchives = Get-Content -Raw -LiteralPath $moduleArchiveLockPath | ConvertFrom-Json
Copy-Item -LiteralPath $moduleArchiveLockPath -Destination (Join-Path $moduleRoot 'package-archives.lock.json')
if ($moduleArchives.Count -ne @($moduleLock.dependencies.'net8.0'.PSObject.Properties | Where-Object { $_.Value.type -ne 'Project' }).Count) { throw 'Archive lock closure mismatch.' }
$moduleDeps = Get-Content -Raw -LiteralPath (Join-Path $moduleRoot 'Ncma.Modules.Grpc.deps.json') | ConvertFrom-Json
$moduleTarget = $moduleDeps.targets.'.NETCoreApp,Version=v8.0'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
# Preserve the resolved packages' own license declarations/notices. This is provenance, NOT a legal approval.
foreach ($dependency in $moduleLock.dependencies.'net8.0'.PSObject.Properties) {
    if ($dependency.Value.type -eq 'Project') { continue }
    $name = $dependency.Name.ToLowerInvariant()
    $version = $dependency.Value.resolved
    $source = Join-Path $moduleWorkspace "out\modules\nuget\$name\$version"
    Assert-ModulePath $source
    $archiveIdentity = @($moduleArchives | Where-Object { $_.id -ceq $dependency.Name -and $_.version -ceq $version })
    if ($archiveIdentity.Count -ne 1 -or $archiveIdentity[0].contentHash -cne $dependency.Value.contentHash -or
        $archiveIdentity[0].source -cne "https://api.nuget.org/v3-flatcontainer/$name/$version/$name.$version.nupkg") { throw 'Archive/content lock identity mismatch.' }
    # Recheck the original NuGet archive, not just a mutable cache metadata file. Then compare
    # each actual published runtime asset to that locked archive's selected .deps.json entry.
    $archivePath = Join-Path $source "$name.$version.nupkg"
    Assert-ModulePath $archivePath
    $archiveStream = [IO.File]::Open($archivePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $sha512 = [Security.Cryptography.SHA512]::Create()
    try {
        if ([Convert]::ToBase64String($sha512.ComputeHash($archiveStream)) -cne $archiveIdentity[0].archiveSha512) { throw 'Locked NuGet archive hash mismatch.' }
        $archiveStream.Position = 0
        $archive = [IO.Compression.ZipArchive]::new($archiveStream, [IO.Compression.ZipArchiveMode]::Read, $true)
        try {
            $targetName = $dependency.Name + '/' + $version
            $target = $moduleTarget.PSObject.Properties[$targetName]
            if ($null -eq $target) { throw 'Resolved NuGet version missing from published deps.' }
            foreach ($asset in $target.Value.runtime.PSObject.Properties) {
                $entry = $archive.GetEntry($asset.Name)
                if ($null -eq $entry) { throw 'Runtime asset missing from locked NuGet archive.' }
                $entryStream = $entry.Open()
                $sha256 = [Security.Cryptography.SHA256]::Create()
                try { $assetHash = [BitConverter]::ToString($sha256.ComputeHash($entryStream)).Replace('-', '') }
                finally { $entryStream.Dispose(); $sha256.Dispose() }
                $published = Join-Path $moduleRoot ([IO.Path]::GetFileName($asset.Name))
                if ((Get-ModuleSha256 $published) -cne $assetHash) { throw 'Published runtime differs from locked NuGet archive.' }
            }
        } finally { $archive.Dispose() }
    } finally { $archiveStream.Dispose(); $sha512.Dispose() }
    $noticeRoot = Join-Path $moduleRoot "notices\$name"
    New-Item -ItemType Directory -Path $noticeRoot | Out-Null
    foreach ($notice in Get-ChildItem -LiteralPath $source -File) {
        if ($notice.Name -like '*.nuspec' -or $notice.Name -like '*LICENSE*' -or $notice.Name -like '*NOTICE*') {
            Assert-ModulePath $notice.FullName
            Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $noticeRoot $notice.Name)
        }
    }
}
$moduleFiles = @(Get-ChildItem -LiteralPath $moduleRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
    Assert-ModulePath $_.FullName
    [pscustomobject][ordered]@{ path = $_.FullName.Substring($moduleRoot.Length + 1).Replace('\', '/'); size = $_.Length;
        sha256 = (Get-ModuleSha256 $_.FullName) }
})
if ($moduleFiles.Count -gt 64 -or ($moduleFiles | Measure-Object size -Sum).Sum -gt 33554432) { throw 'Module client package budget.' }
$moduleManifest = [ordered]@{ schemaVersion = 1; product = 'Ncma.Modules.Grpc'; configuration = $Configuration;
    generation = $moduleGeneration; tfm = 'net8.0'; executionPlatform = 'Windows x64'; mode = 'optional-library';
    requirements = @('compatible .NET 8 runtime', 'separately approved isolated CPython 3.12 worker installation');
    editorIntegrated = $false; inferenceImplemented = $false; selfContainedVerified = $false; files = $moduleFiles }
$moduleManifestPath = Join-Path $moduleRoot 'module-client-manifest.json'
[IO.File]::WriteAllText($moduleManifestPath, ($moduleManifest | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
New-Item -ItemType Directory -Path $moduleIndexRoot -Force | Out-Null
$moduleIndex = [ordered]@{ schemaVersion = 1; generation = $moduleGeneration; client = $moduleRoot;
    manifestSha256 = (Get-ModuleSha256 $moduleManifestPath) }
$modulePending = Join-Path $moduleIndexRoot "client-$moduleGeneration.pending.json"
[IO.File]::WriteAllText($modulePending, ($moduleIndex | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$moduleIndexPath = Join-Path $moduleIndexRoot 'client-package.json'
Assert-ModulePath $moduleIndexPath
if (Test-Path -LiteralPath $moduleIndexPath) {
    [IO.File]::Replace($modulePending, $moduleIndexPath, (Join-Path $moduleIndexRoot "client-$moduleGeneration.previous.json"))
} else { [IO.File]::Move($modulePending, $moduleIndexPath) }
Write-Host "[Ncma] Optional client library staged (no default deployment): $moduleRoot"
