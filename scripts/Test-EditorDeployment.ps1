param([ValidateSet('Debug','Release')][string]$Configuration)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'EditorDeployment.ps1')
$deploymentTestWorkspace = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot))
$deploymentTestIndex = Get-Content -Raw -LiteralPath (Join-Path $deploymentTestWorkspace "out\verification\m2-7\$Configuration\packages.json") | ConvertFrom-Json
$deploymentTestRoot = Assert-DeploymentPath $deploymentTestWorkspace (Join-Path $deploymentTestWorkspace ('out\verification\deployment-tests\' + [Guid]::NewGuid().ToString('N')))
New-Item -ItemType Directory -Path $deploymentTestRoot | Out-Null
function New-OldInstall([string]$Name) {
    $path = Assert-DeploymentPath $deploymentTestWorkspace (Join-Path $deploymentTestRoot "$Name\bin")
    New-Item -ItemType Directory -Path $path | Out-Null
    foreach ($name in @('NcmaEngine.exe','NcmaPhysics.dll','glfw3.dll')) { [IO.File]::WriteAllText((Join-Path $path $name), 'synthetic rollback identity; never executed') }
    return $path
}
function Expect-Rejection([scriptblock]$Operation) {
    $rejected = $false
    try { & $Operation } catch { Write-Host ('Expected rejection: ' + $_.Exception.Message); $rejected = $true }
    if (-not $rejected) { throw 'Expected deployment rejection.' }
}
$target = New-OldInstall 'rollback'
$original = (Get-DeploymentHash (Join-Path $target 'NcmaEngine.exe'))
Expect-Rejection { Invoke-EditorDeployment $deploymentTestWorkspace $deploymentTestIndex.editor $target { throw 'Injected failure after backup.' } }
if ((Get-DeploymentHash (Join-Path $target 'NcmaEngine.exe')) -ne $original) { throw 'Rollback did not restore exact original.' }
$journal = Get-Content -Raw -LiteralPath (Join-Path (Split-Path $target) 'deployment\editor-journal.json') | ConvertFrom-Json
if ($journal.phase -ne 'RolledBack') { throw 'Rollback journal mismatch.' }
Write-Host 'PASS deployment injected failure and exact recovery'
$target = New-OldInstall 'unknown-data'
[IO.File]::WriteAllText((Join-Path $target 'user-scene.ncmascene'), 'preserve user file')
Expect-Rejection { Invoke-EditorDeployment $deploymentTestWorkspace $deploymentTestIndex.editor $target }
if (-not (Test-Path -LiteralPath (Join-Path $target 'user-scene.ncmascene'))) { throw 'User data moved.' }
Write-Host 'PASS deployment rejects unknown user data'
$target = New-OldInstall 'locked'
$lock = [IO.File]::Open((Join-Path $target 'NcmaEngine.exe'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
try { Expect-Rejection { Invoke-EditorDeployment $deploymentTestWorkspace $deploymentTestIndex.editor $target } }
finally { $lock.Dispose() }
if ((Get-DeploymentHash (Join-Path $target 'NcmaEngine.exe')) -ne $original) { throw 'Locked install modified.' }
Write-Host 'PASS deployment rejects file locks without force'
Expect-Rejection { Invoke-EditorDeployment $deploymentTestWorkspace $deploymentTestIndex.editor $deploymentTestWorkspace }
Write-Host 'PASS deployment rejects broad target'
$target = New-OldInstall 'success'
Invoke-EditorDeployment $deploymentTestWorkspace $deploymentTestIndex.editor $target
$manifest = Read-DeploymentManifest $target
$journalPath = Join-Path (Split-Path $target) 'deployment\editor-journal.json'
$journal = Get-Content -Raw -LiteralPath $journalPath | ConvertFrom-Json
if (-not $manifest.production -or $manifest.manualAcceptance -or $manifest.selfContainedVerified -or $journal.phase -ne 'Complete') { throw 'Production/manual metadata conflated.' }
Restore-EditorDeployment $deploymentTestWorkspace $target $journalPath
if ((Get-DeploymentHash (Join-Path $target 'NcmaEngine.exe')) -ne $original) { throw 'Explicit recovery identity mismatch.' }
Write-Host 'PASS formal-path apphost graphics smoke and explicit recovery'
$fresh = Assert-DeploymentPath $deploymentTestWorkspace (Join-Path $deploymentTestRoot 'fresh\bin')
Invoke-EditorDeployment $deploymentTestWorkspace $deploymentTestIndex.editor $fresh
if (-not (Read-DeploymentManifest $fresh).production) { throw 'Fresh installation failed.' }
Write-Host 'PASS first installation without an old entry'
$freshJournalPath = Join-Path (Split-Path $fresh) 'deployment\editor-journal.json'
$freshJournal = Get-Content -Raw -LiteralPath $freshJournalPath | ConvertFrom-Json
$freshJournalHash = Get-DeploymentHash $freshJournalPath
Invoke-EditorDeployment $deploymentTestWorkspace $deploymentTestIndex.editor $fresh
$history = Join-Path (Split-Path $freshJournalPath) ($freshJournal.generation + '\journal-Complete.json')
if ((Get-DeploymentHash $history) -ne $freshJournalHash) { throw 'Previous deployment journal was not preserved exactly.' }
$nextJournal = Get-Content -Raw -LiteralPath $freshJournalPath | ConvertFrom-Json
if ($nextJournal.generation -eq $freshJournal.generation -or $nextJournal.phase -ne 'Complete') { throw 'Redeployment generation mismatch.' }
Restore-EditorDeployment $deploymentTestWorkspace $fresh $freshJournalPath
if (-not (Read-DeploymentManifest $fresh).production) { throw 'Redeployment recovery failed.' }
Write-Host 'PASS redeployment preserves previous journal and recoverable apphost'
Write-Host '[Ncma] Deployment regression: 7/7 passed.'
