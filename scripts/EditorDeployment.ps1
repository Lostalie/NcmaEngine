$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-DeploymentHash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}

function Assert-DeploymentPath([string]$Workspace, [string]$Path) {
    $absolute = [IO.Path]::GetFullPath($Path)
    $allowed = [IO.Path]::GetFullPath((Join-Path $Workspace 'out')) + '\'
    if (-not $absolute.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Deployment path outside out.' }
    if ((Test-Path -LiteralPath $absolute) -and ((Get-Item -LiteralPath $absolute -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Reparse deployment item.' }
    for ($part = [IO.DirectoryInfo]::new($absolute); $null -ne $part; $part = $part.Parent) {
        if ($part.Exists -and ($part.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Reparse deployment path.' }
    }
    return $absolute
}

function Get-DeploymentFiles([string]$Workspace, [string]$Directory) {
    $Directory = Assert-DeploymentPath $Workspace $Directory
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { throw 'Missing deployment directory.' }
    $pending = [Collections.Generic.Queue[string]]::new()
    $pending.Enqueue($Directory)
    $result = [Collections.Generic.List[object]]::new()
    while ($pending.Count -gt 0) {
        foreach ($item in Get-ChildItem -LiteralPath $pending.Dequeue() -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse deployment item.' }
            if ($item.PSIsContainer) { $pending.Enqueue($item.FullName) }
            else {
                if ($result.Count -ge 8192) { throw 'Deployment file budget.' }
                $result.Add([pscustomobject]@{ path = $item.FullName.Substring($Directory.Length + 1).Replace('\','/'); size = $item.Length; sha256 = (Get-DeploymentHash $item.FullName) })
            }
        }
    }
    return $result.ToArray()
}

function Read-DeploymentManifest([string]$Directory) {
    $path = Join-Path $Directory 'deployment-manifest.json'
    $item = Get-Item -LiteralPath $path
    if ($item.PSIsContainer -or $item.Length -le 0 -or $item.Length -gt 4194304 -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Invalid manifest file.' }
    return Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
}

function Assert-DeploymentContents([string]$Workspace, [string]$Directory, [switch]$Legacy) {
    $actual = @(Get-DeploymentFiles $Workspace $Directory)
    if ($Legacy) {
        if ($actual.Count -ne 3 -or @($actual | Where-Object { $_.path -notin @('NcmaEngine.exe','NcmaPhysics.dll','glfw3.dll') }).Count) { throw 'Unknown old-install files; stop without moving user data.' }
        return $actual
    }
    $manifest = Read-DeploymentManifest $Directory
    if ($manifest.schemaVersion -ne 1 -or $manifest.rid -ne 'win-x64' -or $manifest.tfm -ne 'net8.0' -or $manifest.configuration -notin @('Debug','Release') -or $manifest.product -notin @('NcmaEngine-editor-candidate','NcmaEngine-editor')) { throw 'Unsupported editor package.' }
    $seen = @{}
    foreach ($file in $manifest.files) {
        $relative = [string]$file.path
        if ([IO.Path]::IsPathRooted($relative) -or $relative -match '[:\\]' -or $relative.Split('/') -contains '..' -or $relative.Split('/') -contains '.' -or $relative.Split('/') -contains '' -or $seen.ContainsKey($relative)) { throw 'Invalid/duplicate manifest path.' }
        $seen[$relative] = $true
        $found = @($actual | Where-Object { $_.path -ceq $relative })
        if ($found.Count -ne 1 -or $found[0].size -ne $file.size -or $found[0].sha256 -ne $file.sha256) { throw "Package integrity: $relative" }
    }
    foreach ($file in $actual) {
        if ($seen.ContainsKey($file.path) -or $file.path -eq 'deployment-manifest.json') { continue }
        if ($file.path -notin @('out/user/logs/editor-candidate.jsonl','out/user/logs/editor-candidate.jsonl.1','out/user/editor/preferences.json','out/user/editor/workspace.json','sample/out/user/logs/editor-candidate.jsonl','sample/out/user/logs/editor-candidate.jsonl.1','sample/out/user/editor/preferences.json','sample/out/user/editor/workspace.json') -or $file.size -gt 1048576 -or ($file.path.EndsWith('/workspace.json') -and $file.size -gt 4096)) { throw "Unknown install file: $($file.path)" }
    }
    return $actual
}

function Write-DeploymentJournal([string]$Path, [object]$Journal) {
    $temporary = $Path + '.tmp'
    foreach ($item in @($Path, $temporary)) {
        if ((Test-Path -LiteralPath $item) -and ((Get-Item -LiteralPath $item -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Reparse journal file.' }
    }
    [IO.File]::WriteAllText($temporary, ($Journal | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($temporary, $Path, [NullString]::Value) }
    else { [IO.File]::Move($temporary, $Path) }
}

function Restore-EditorDeployment([string]$Workspace, [string]$Destination, [string]$JournalPath) {
    $Workspace = [IO.Path]::GetFullPath($Workspace)
    $Destination = Assert-DeploymentPath $Workspace $Destination
    $JournalPath = Assert-DeploymentPath $Workspace $JournalPath
    $expectedJournal = if ($Destination -eq (Join-Path $Workspace 'out\bin')) { Join-Path $Workspace 'out\deployment\editor-journal.json' } else { Join-Path (Split-Path $Destination) 'deployment\editor-journal.json' }
    if ($JournalPath -ne $expectedJournal -or ((Split-Path $Destination -Leaf) -ne 'bin')) { throw 'Recovery target mismatch.' }
    $journalItem = Get-Item -LiteralPath $JournalPath
    if ($journalItem.Length -gt 4194304 -or ($journalItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Invalid journal.' }
    $journal = Get-Content -Raw -LiteralPath $JournalPath | ConvertFrom-Json
    if ($journal.schemaVersion -ne 1 -or $journal.generation -notmatch '^[a-f0-9]{32}$' -or $journal.destination -ne $Destination) { throw 'Journal identity mismatch.' }
    $generation = Assert-DeploymentPath $Workspace (Join-Path (Split-Path $JournalPath) $journal.generation)
    $backup = Assert-DeploymentPath $Workspace (Join-Path $generation 'backup')
    if (-not (Test-Path -LiteralPath $backup -PathType Container)) { throw 'No backup to recover; stop.' }
    $files = @(Get-DeploymentFiles $Workspace $backup)
    if ($files.Count -ne @($journal.oldFiles).Count) { throw 'Backup identity mismatch.' }
    foreach ($file in $journal.oldFiles) {
        $found = @($files | Where-Object { $_.path -ceq $file.path })
        if ($found.Count -ne 1 -or $found[0].sha256 -ne $file.sha256 -or $found[0].size -ne $file.size) { throw 'Backup was modified; stop recovery.' }
    }
    if (Test-Path -LiteralPath $Destination) {
        $null = Assert-DeploymentContents $Workspace $Destination
        $failed = Assert-DeploymentPath $Workspace (Join-Path $generation 'failed')
        if (Test-Path -LiteralPath $failed) { throw 'Recovery destination already exists.' }
        Move-Item -LiteralPath $Destination -Destination $failed
    }
    Move-Item -LiteralPath $backup -Destination $Destination
    $journal.phase = 'RolledBack'
    Write-DeploymentJournal $JournalPath $journal
}

function Invoke-EditorDeployment {
    param([string]$Workspace, [string]$Package, [string]$Destination, [scriptblock]$AfterBackup)
    $Workspace = [IO.Path]::GetFullPath($Workspace)
    $Destination = Assert-DeploymentPath $Workspace $Destination
    $testPrefix = Join-Path $Workspace 'out\verification\deployment-tests\'
    if ($Destination -ne (Join-Path $Workspace 'out\bin') -and (-not $Destination.StartsWith($testPrefix, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $Destination -Leaf) -ne 'bin')) { throw 'Unsupported install target.' }
    $Package = Assert-DeploymentPath $Workspace $Package
    if (-not $Package.StartsWith((Join-Path $Workspace 'out\package\') , [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsupported package root.' }
    $packageFiles = @(Assert-DeploymentContents $Workspace $Package)
    $deployRoot = Assert-DeploymentPath $Workspace $(if ($Destination -eq (Join-Path $Workspace 'out\bin')) { Join-Path $Workspace 'out\deployment' } else { Join-Path (Split-Path $Destination) 'deployment' })
    New-Item -ItemType Directory -Path $deployRoot -Force | Out-Null
    $journalPath = Join-Path $deployRoot 'editor-journal.json'
    if (Test-Path -LiteralPath $journalPath) {
        $previous = Get-Content -Raw -LiteralPath $journalPath | ConvertFrom-Json
        if ($previous.phase -notin @('Complete','RolledBack')) { throw 'Interrupted deployment; run explicit recovery first.' }
        if ($previous.generation -notmatch '^[a-f0-9]{32}$') { throw 'Previous journal identity mismatch.' }
        $history = Assert-DeploymentPath $Workspace (Join-Path $deployRoot ($previous.generation + '\journal-' + $previous.phase + '.json'))
        if (Test-Path -LiteralPath $history) {
            if ((Get-DeploymentHash $history) -ne (Get-DeploymentHash $journalPath)) { throw 'Journal history conflict; preserve evidence.' }
        }
        else { Copy-Item -LiteralPath $journalPath -Destination $history }
    }
    $oldFiles = @()
    if (Test-Path -LiteralPath $Destination) {
        $legacy = -not (Test-Path -LiteralPath (Join-Path $Destination 'deployment-manifest.json'))
        $oldFiles = @(Assert-DeploymentContents $Workspace $Destination -Legacy:$legacy)
        foreach ($file in $oldFiles) {
            $stream = [IO.File]::Open((Join-Path $Destination $file.path), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
            $stream.Dispose()
        }
        foreach ($process in Get-Process -Name NcmaEngine -ErrorAction SilentlyContinue) {
            if ($process.Path -and $process.Path.StartsWith($Destination + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Editor running; close normally before deployment.' }
        }
    }
    $id = [Guid]::NewGuid().ToString('N')
    $generation = Assert-DeploymentPath $Workspace (Join-Path $deployRoot $id)
    $stage = Assert-DeploymentPath $Workspace (Join-Path $generation 'staging')
    $backup = Assert-DeploymentPath $Workspace (Join-Path $generation 'backup')
    New-Item -ItemType Directory -Path $stage | Out-Null
    foreach ($file in $packageFiles) {
        if ($file.path.StartsWith('out/')) { continue }
        $target = Assert-DeploymentPath $Workspace (Join-Path $stage $file.path)
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $Package $file.path) -Destination $target
    }
    foreach ($file in $oldFiles | Where-Object { $_.path.StartsWith('out/user/') -or $_.path.StartsWith('sample/out/user/') }) {
        $target = Assert-DeploymentPath $Workspace (Join-Path $stage $file.path)
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $Destination $file.path) -Destination $target
    }
    $manifest = Read-DeploymentManifest $stage
    $manifest.production = $true
    $manifest.product = 'NcmaEngine-editor'
    [IO.File]::WriteAllText((Join-Path $stage 'deployment-manifest.json'), ($manifest | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    $null = Assert-DeploymentContents $Workspace $stage
    $validation = Start-Process -FilePath (Join-Path $stage 'NcmaEngine.exe') -ArgumentList '--validate-package' -WindowStyle Hidden -Wait -PassThru
    if ($validation.ExitCode -ne 0) { throw 'Staged apphost/manifest validation failed.' }
    $journal = [ordered]@{ schemaVersion = 1; generation = $id; destination = $Destination; phase = 'Prepared'; oldFiles = $oldFiles; configuration = $manifest.configuration; manualAcceptance = $false; selfContainedVerified = $false }
    Write-DeploymentJournal $journalPath $journal
    $moved = $false
    try {
        if (Test-Path -LiteralPath $Destination) { Move-Item -LiteralPath $Destination -Destination $backup; $moved = $true }
        $journal.phase = 'BackedUp'; Write-DeploymentJournal $journalPath $journal
        if ($AfterBackup) { & $AfterBackup }
        Move-Item -LiteralPath $stage -Destination $Destination
        $journal.phase = 'Installed'; Write-DeploymentJournal $journalPath $journal
        $null = Assert-DeploymentContents $Workspace $Destination
        $smokeArguments = '--smoke-test --project "' + (Join-Path $Destination 'sample\sample.ncmaproject') + '"'
        $smoke = Start-Process -FilePath (Join-Path $Destination 'NcmaEngine.exe') -ArgumentList $smokeArguments -WindowStyle Hidden -Wait -PassThru
        if ($smoke.ExitCode -ne 0) { throw 'Formal-path editor smoke failed.' }
        $journal.phase = 'Complete'; Write-DeploymentJournal $journalPath $journal
        Write-Host "[Ncma] C# editor deployed; recoverable backup: $backup"
    }
    catch {
        if ($moved) { Restore-EditorDeployment $Workspace $Destination $journalPath }
        else { $journal.phase = 'RecoveryRequired'; Write-DeploymentJournal $journalPath $journal }
        throw
    }
}
