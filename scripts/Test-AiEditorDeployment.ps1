param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'AiCandidatePackage.ps1')
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot))
$sourceHash = Get-AiCandidateInputHash @(Get-AiCandidateSourceInputs $root)
$index = "$root/out/verification/m8-4/$Configuration/packages.json"
$plain = Get-AiCandidateIndexedProduct $root $index $Configuration $false $sourceHash
$ai = Get-AiCandidateIndexedProduct $root $index $Configuration $true $sourceHash
Assert-AiCandidateManifest $root $plain.path $Configuration $false $sourceHash $plain.manifestSha256
Assert-AiCandidateManifest $root $ai.path $Configuration $true $sourceHash $ai.manifestSha256
$evidence = Assert-DeploymentPath $root "$root/out/verification/deployment-tests/$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $evidence | Out-Null
$results = [Collections.Generic.List[object]]::new()
function Case([string]$Name, [scriptblock]$Action) {
    try { & $Action; $results.Add(@{name=$Name;passed=$true}); Write-Host "PASS $Name" }
    catch { $results.Add(@{name=$Name;passed=$false;error=$_.Exception.ToString()}); Write-Host "FAIL $Name : $_" }
}
function RejectVariant([string]$Package, [bool]$EnableAI, [string]$Target) {
    $rejected = $false
    try { Invoke-EditorDeployment -Workspace $root -Package $Package -Destination $Target -EnableAI:$EnableAI }
    catch { if ($_.Exception.Message -notlike 'Candidate variant mismatch*') { throw }; $rejected = $true }
    if (-not $rejected -or (Test-Path -LiteralPath (Split-Path $Target))) { throw 'Variant refusal must precede all destination mutations.' }
}
function SameFiles([object[]]$Expected, [string]$Directory) {
    $actual = @(Get-DeploymentFiles $root $Directory)
    if ($actual.Count -ne $Expected.Count) { throw 'File count changed.' }
    foreach ($file in $Expected) {
        $match = @($actual | Where-Object path -CEQ $file.path)
        if ($match.Count -ne 1 -or $match[0].sha256 -cne $file.sha256 -or $match[0].size -ne $file.size) { throw 'File bytes changed.' }
    }
}
Case 'AI candidate requires explicit opt-in' { RejectVariant $ai.path $false "$evidence/no-opt-in/bin" }
Case 'AI opt-in rejects default candidate' { RejectVariant $plain.path $true "$evidence/wrong-variant/bin" }
Case 'default to AI preserves exact default and restores it' {
    $target = "$evidence/lifecycle/bin"
    Invoke-EditorDeployment -Workspace $root -Package $plain.path -Destination $target
    $before = @(Assert-DeploymentContents $root $target)
    Invoke-EditorDeployment -Workspace $root -Package $ai.path -Destination $target -EnableAI
    $installed = Read-DeploymentManifest $target
    if ($installed.product -cne 'NcmaEngine-ai-editor' -or $installed.aiEnabled -ne $true -or $installed.production -ne $true) { throw 'Installed AI identity lost.' }
    $journalPath = "$evidence/lifecycle/deployment/editor-journal.json"
    $journal = Get-Content -Raw -LiteralPath $journalPath | ConvertFrom-Json
    if ($journal.phase -cne 'Complete') { throw 'Deployment incomplete.' }
    SameFiles $before "$evidence/lifecycle/deployment/$($journal.generation)/backup"
    Restore-EditorDeployment $root $target $journalPath
    SameFiles $before $target
    if ((Read-DeploymentManifest $target).aiEnabled -ne $false) { throw 'Default variant not restored.' }
}
[IO.File]::WriteAllText("$evidence/results.json", (ConvertTo-Json -InputObject $results.ToArray() -Depth 5))
Write-Host "Evidence: $evidence"
if (@($results | Where-Object { -not $_.passed }).Count) { throw 'AI deployment cases failed; all case results retained.' }
Write-Host 'AI deployment: 3/3 passed; no model configured or invoked.'
