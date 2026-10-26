# Trusted local candidate preparation/verification only. No deployment, process
# launch, service connection or credential lookup occurs when this file is loaded.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'EditorDeployment.ps1')

function Get-AiCandidateOrderedPaths([string[]]$Paths) {
    # Typed collection avoids PS5 object/PSObject array sorting semantics.
    $ordered=[Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    foreach($relative in $Paths){$null=$ordered.Add($relative)}
    foreach($relative in $ordered){$relative}
}

function Get-AiCandidateSourceInputs([string]$Workspace) {
    $Workspace = [IO.Path]::GetFullPath($Workspace).TrimEnd('\')
    # Includes tracked and non-ignored new implementation files, not just the Git commit.
    # This is an explicit build-input selection, NOT a freeze of the whole repository.
    $paths = @(& git -C $Workspace -c core.quotepath=false ls-files --cached --others --exclude-standard -- CMakeLists.txt CMakePresets.json managed engine scripts)
    if ($LASTEXITCODE -ne 0 -or $paths.Count -eq 0 -or $paths.Count -gt 65536) { throw 'ai_package_source_inventory' }
    foreach ($relative in @(Get-AiCandidateOrderedPaths $paths)) {
        if ($relative -match '(?i)(^|/)(bin|obj|\.vs|\.git)/|\.user$') { continue }
        $source = [IO.Path]::GetFullPath((Join-Path $Workspace $relative))
        if (-not $source.StartsWith($Workspace + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'ai_package_source_escape' }
        for ($part = $source; $part; $part = [IO.Path]::GetDirectoryName($part)) {
            if ((Test-Path -LiteralPath $part) -and ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'ai_package_source_link' }
        }
        if (!(Test-Path -LiteralPath $source)) {
            # A tracked deletion is part of the dirty input state, not an old file to restore.
            [pscustomobject][ordered]@{path=$relative;size=0;sha256=$null;deleted=$true}
            continue
        }
        $item = Get-Item -LiteralPath $source
        if ($item.PSIsContainer) { throw 'ai_package_source_not_file' }
        [pscustomobject][ordered]@{path=$relative;size=$item.Length;sha256=(Get-DeploymentHash $source)}
    }
}

function Get-AiCandidateInputHash([object[]]$Inputs) {
    if ($Inputs.Count -eq 0 -or $Inputs.Count -gt 65536) { throw 'ai_package_source_empty' }
    # Versioned, length-framed UTF-8, independent of PS5/PS7 JSON serializers/culture.
    $encoding = [Text.UTF8Encoding]::new($false, $true)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $prefix = $encoding.GetBytes("ncma-source-inputs-v1`n")
        $null = $algorithm.TransformBlock($prefix,0,$prefix.Length,$prefix,0)
        $last = $null
        foreach ($entry in $Inputs) {
            if ($null -eq $entry -or $entry.path -isnot [string]) { throw 'ai_package_source_shape' }
            $path = [string]$entry.path
            if ([IO.Path]::IsPathRooted($path) -or $path -match '[:\\\r\n]' -or @($path.Split('/') | Where-Object { $_ -in @('','.','..') }).Count) { throw 'ai_package_source_path' }
            if ($path.Length -eq 0 -or $path.Length -gt 1024 -or ($null -ne $last -and [StringComparer]::Ordinal.Compare($last,$path) -ge 0)) { throw 'ai_package_source_order' }
            $last = $path
            if ($null -ne $entry.PSObject.Properties['deleted'] -and $entry.deleted -isnot [bool]) { throw 'ai_package_source_shape' }
            $deleted = $null -ne $entry.PSObject.Properties['deleted'] -and $entry.deleted -eq $true
            if (($entry.size -isnot [int] -and $entry.size -isnot [long]) -or $entry.size -lt 0 -or
                ($deleted -and ($entry.size -ne 0 -or $null -ne $entry.sha256))) { throw 'ai_package_source_size' }
            $hash = if ($deleted) { '-' } else { [string]$entry.sha256 }
            if (-not $deleted -and $hash -cnotmatch '^[0-9A-F]{64}$') { throw 'ai_package_source_hash' }
            $length = $encoding.GetByteCount($path).ToString([Globalization.CultureInfo]::InvariantCulture)
            $size = ([long]$entry.size).ToString([Globalization.CultureInfo]::InvariantCulture)
            $bytes = $encoding.GetBytes($length + ':' + $path + "`n" + $size + ':' + $hash + "`n")
            $null = $algorithm.TransformBlock($bytes,0,$bytes.Length,$bytes,0)
        }
        $null = $algorithm.TransformFinalBlock([byte[]]@(),0,0)
        return [BitConverter]::ToString($algorithm.Hash).Replace('-','')
    }
    finally { $algorithm.Dispose() }
}

function Get-AiCandidateIndexedProduct([string]$Workspace, [string]$IndexPath, [string]$Configuration,
    [bool]$EnableAI, [string]$CurrentSourceHash) {
    if ($Configuration -notin @('Debug','Release') -or $CurrentSourceHash -cnotmatch '^[0-9A-F]{64}$') { throw [IO.InvalidDataException]::new('ai_index_expected_identity') }
    $IndexPath = Assert-DeploymentPath $Workspace $IndexPath
    $item = Get-Item -LiteralPath $IndexPath
    if ($item.PSIsContainer -or $item.Length -gt 65536) { throw [IO.InvalidDataException]::new('ai_index_budget') }
    $index = Get-Content -Raw -LiteralPath $IndexPath | ConvertFrom-Json
    if ($index.schemaVersion -ne 1 -or $index.configuration -cne $Configuration -or
        $index.production -isnot [bool] -or $index.production -ne $false) { throw [IO.InvalidDataException]::new('ai_index_identity') }
    if ($index.sourceInputsSha256 -cne $CurrentSourceHash) { throw [IO.InvalidDataException]::new('ai_index_stale_source') }
    $base = [IO.Path]::GetFullPath((Join-Path $Workspace "out/package/m2-7/$Configuration")).TrimEnd('\')
    $generation = $null; $selected = $null
    foreach ($pair in @(@('editor','editor'),@('editorAi','editor-ai'))) {
        $entry = $index.products.($pair[0])
        if ($entry.path -isnot [string] -or -not [IO.Path]::IsPathRooted($entry.path) -or $entry.manifestSha256 -cnotmatch '^[0-9A-F]{64}$') { throw [IO.InvalidDataException]::new('ai_index_entry') }
        $source = [IO.Path]::GetFullPath($entry.path).TrimEnd('\')
        if (-not $source.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)) { throw [IO.InvalidDataException]::new('ai_index_path') }
        $relative = $source.Substring($base.Length+1)
        if ($relative -cnotmatch ('^([a-f0-9]{32})\\'+$pair[1]+'$')) { throw [IO.InvalidDataException]::new('ai_index_path') }
        $current = $relative.Split('\')[0]
        if ($null -ne $generation -and $generation -cne $current) { throw [IO.InvalidDataException]::new('ai_index_generation') }
        $generation = $current
        $null = Assert-DeploymentPath $Workspace $source
        if (($EnableAI -and $pair[0] -eq 'editorAi') -or (-not $EnableAI -and $pair[0] -eq 'editor')) {
            $selected = [pscustomobject]@{path=$source;manifestSha256=$entry.manifestSha256;generation=$generation}
        }
    }
    return $selected
}

function Assert-AiCandidateDependencies([string]$Workspace, [string]$Directory, [bool]$EnableAI) {
    $files = @(Get-DeploymentFiles $Workspace $Directory)
    $optional = @('Ncma.AI','Ncma.Modules','Ncma.Editor.Modules')
    foreach ($file in $files) {
        $name = [IO.Path]::GetFileName($file.path)
        if ($name -match '^(?i:grpc|python|Ncma\.Modules\.Grpc|Microsoft\.CodeAnalysis|Ncma\.CodeGen)' -or
            $name -match '(?i)\.(py|pyc|key|pem)$' -or $name -eq '.env') { throw [IO.InvalidDataException]::new('ai_package_forbidden_dependency') }
        if (-not $EnableAI -and $name -match '^Ncma\.(AI|Modules|Editor\.Modules)(\.|$)') { throw [IO.InvalidDataException]::new('ai_package_default_dependency') }
        if ($name.EndsWith('.deps.json', [StringComparison]::OrdinalIgnoreCase)) {
            if ($file.size -gt 4194304) { throw [IO.InvalidDataException]::new('ai_package_deps_budget') }
            $deps = Get-Content -Raw -LiteralPath (Join-Path $Directory $file.path) | ConvertFrom-Json
            foreach ($library in $deps.libraries.PSObject.Properties.Name) {
                if ($library -match '^(?i:Grpc|Python|Ncma\.Modules\.Grpc|Microsoft\.CodeAnalysis|Ncma\.CodeGen)(/|\.)') { throw [IO.InvalidDataException]::new('ai_package_forbidden_dependency') }
                if (-not $EnableAI -and $library -match '^Ncma\.(AI|Modules|Editor\.Modules)(/|\.)') { throw [IO.InvalidDataException]::new('ai_package_default_dependency') }
            }
        }
    }
    if ($EnableAI) {
        $depsPath = Join-Path $Directory 'NcmaEngine.deps.json'
        if (!(Test-Path -LiteralPath $depsPath -PathType Leaf)) { throw [IO.InvalidDataException]::new('ai_package_missing_deps') }
        $deps = Get-Content -Raw -LiteralPath $depsPath | ConvertFrom-Json
        $actual = @($deps.libraries.PSObject.Properties.Name | Where-Object { $_ -match '^Ncma\.(AI|Modules|Editor\.Modules)(/|\.)' })
        if ($actual.Count -ne 3) { throw [IO.InvalidDataException]::new('ai_package_optional_closure') }
        foreach ($name in $optional) {
            if (@($actual | Where-Object { $_.Split('/')[0] -ceq $name }).Count -ne 1 -or
                !(Test-Path -LiteralPath (Join-Path $Directory ($name + '.dll')) -PathType Leaf)) { throw [IO.InvalidDataException]::new('ai_package_optional_closure') }
        }
    }
}

function Assert-AiCandidateManifest([string]$Workspace, [string]$Directory, [string]$Configuration,
    [bool]$EnableAI, [string]$SourceInputsSha256, [string]$ManifestSha256) {
    $Directory = Assert-DeploymentPath $Workspace $Directory
    if ($Configuration -notin @('Debug','Release') -or $SourceInputsSha256 -cnotmatch '^[0-9A-F]{64}$' -or
        $ManifestSha256 -cnotmatch '^[0-9A-F]{64}$') { throw [IO.InvalidDataException]::new('ai_package_expected_identity') }
    $manifestPath = Join-Path $Directory 'deployment-manifest.json'
    if (!(Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw [IO.InvalidDataException]::new('ai_package_missing_manifest') }
    $files = @(Get-DeploymentFiles $Workspace $Directory) # bounded, never traverses links
    if ((Get-DeploymentHash $manifestPath) -cne $ManifestSha256) { throw [IO.InvalidDataException]::new('ai_package_manifest_hash') }
    $manifest = Read-DeploymentManifest $Directory
    $product = if ($EnableAI) { 'NcmaEngine-ai-editor-candidate' } else { 'NcmaEngine-editor-candidate' }
    if ($manifest.schemaVersion -ne 1 -or $manifest.product -cne $product -or $manifest.configuration -cne $Configuration -or
        $manifest.rid -cne 'win-x64' -or $manifest.tfm -cne 'net8.0' -or $manifest.publishMode -cne 'framework-dependent' -or
        $manifest.sourceInputsSha256 -cne $SourceInputsSha256 -or $manifest.aiEnabled -isnot [bool] -or $manifest.aiEnabled -ne $EnableAI -or
        $manifest.production -isnot [bool] -or $manifest.production -ne $false) { throw [IO.InvalidDataException]::new('ai_package_identity') }
    $listed = @($manifest.files)
    if ($listed.Count -le 0 -or $listed.Count -gt 8191 -or $files.Count -ne $listed.Count + 1) { throw [IO.InvalidDataException]::new('ai_package_file_set') }
    $actual = @{}; foreach ($file in $files) { $actual.Add($file.path, $file) }
    $seen = @{}
    foreach ($file in $listed) {
        $path = [string]$file.path
        if ([string]::IsNullOrEmpty($path) -or $path.Length -gt 1024 -or [IO.Path]::IsPathRooted($path) -or
            $path -match '[:\\]' -or @($path.Split('/') | Where-Object { $_ -in @('','.','..') }).Count -or
            $path -eq 'deployment-manifest.json' -or $seen.ContainsKey($path)) { throw [IO.InvalidDataException]::new('ai_package_file_path') }
        $seen.Add($path, $true)
        if (-not $actual.ContainsKey($path) -or $actual[$path].path -cne $path -or $actual[$path].size -ne $file.size -or
            $actual[$path].sha256 -cne $file.sha256) { throw [IO.InvalidDataException]::new('ai_package_file_integrity') }
    }
    Assert-AiCandidateDependencies $Workspace $Directory $EnableAI
}
