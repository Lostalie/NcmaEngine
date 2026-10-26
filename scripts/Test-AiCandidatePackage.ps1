[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'AiCandidatePackage.ps1')
$workspace = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$output = Join-Path $workspace ("out/verification/m8-4/package-contracts/" + [Guid]::NewGuid().ToString('N'))
$null = Assert-DeploymentPath $workspace $output
if (Test-Path -LiteralPath $output) { throw 'Fresh fixture root required' }
New-Item -ItemType Directory -Path $output | Out-Null
$sourceHash = 'A' * 64
$utf8 = [Text.UTF8Encoding]::new($false)

function Save-Manifest($Fixture) {
    $files = @(Get-DeploymentFiles $workspace $Fixture.path | Where-Object path -ne 'deployment-manifest.json')
    $manifest = [ordered]@{schemaVersion=1;product=$(if($Fixture.ai){'NcmaEngine-ai-editor-candidate'}else{'NcmaEngine-editor-candidate'});
        rid='win-x64';tfm='net8.0';configuration='Debug';publishMode='framework-dependent';production=$false;
        sourceInputsSha256=$sourceHash;aiEnabled=$Fixture.ai;files=$files}
    [IO.File]::WriteAllText((Join-Path $Fixture.path 'deployment-manifest.json'), ($manifest | ConvertTo-Json -Depth 6), $utf8)
    $Fixture.hash = Get-DeploymentHash (Join-Path $Fixture.path 'deployment-manifest.json')
}
function Validate($Fixture) { Assert-AiCandidateManifest $workspace $Fixture.path 'Debug' $Fixture.ai $sourceHash $Fixture.hash }
function New-Fixture([bool]$AI) {
    # ASCII source also parses under Windows PowerShell's BOM-less ANSI default.
    # The actual fixture directory still contains spaces and Chinese characters.
    $path = Join-Path $output ([Guid]::NewGuid().ToString('N') + ' ' + [char]0x5019 + [char]0x9009 + ' ' + [char]0x5305)
    New-Item -ItemType Directory -Path $path | Out-Null
    $libraries = [ordered]@{'Ncma.Application/1.0.0'=@{type='project'}}
    # Deliberately synthetic bytes, never launched or advertised as a product package.
    foreach ($file in @('NcmaEngine.exe','NcmaEngine.dll','NcmaEngine.runtimeconfig.json')) { [IO.File]::WriteAllText((Join-Path $path $file),'synthetic-contract-fixture',$utf8) }
    if ($AI) {
        foreach ($name in @('Ncma.AI','Ncma.Modules','Ncma.Editor.Modules')) {
            $libraries[$name + '/1.0.0'] = @{type='project'}
            [IO.File]::WriteAllText((Join-Path $path ($name + '.dll')),'synthetic-contract-fixture',$utf8)
        }
    }
    [IO.File]::WriteAllText((Join-Path $path 'NcmaEngine.deps.json'), (@{libraries=$libraries}|ConvertTo-Json -Depth 4),$utf8)
    $fixture = [pscustomobject]@{path=$path;ai=$AI;hash=''}
    Save-Manifest $fixture; Validate $fixture
    return $fixture
}
function Expect-Rejection([scriptblock]$Action,[string]$Code) {
    try { & $Action }
    catch [IO.InvalidDataException] { if ($_.Exception.Message -ceq $Code) { return }; throw }
    throw "Expected exact rejection: $Code"
}
function Change-Manifest($Fixture,[scriptblock]$Change) {
    $path = Join-Path $Fixture.path 'deployment-manifest.json'
    $manifest = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    & $Change $manifest
    [IO.File]::WriteAllText($path,($manifest|ConvertTo-Json -Depth 8),$utf8)
    $Fixture.hash = Get-DeploymentHash $path
}
$cases = [ordered]@{
    'default exact closure' = { $f=New-Fixture $false; Validate $f }
    'optional exact closure' = { $f=New-Fixture $true; Validate $f }
    'manifest bytes changed' = { $f=New-Fixture $true; [IO.File]::AppendAllText((Join-Path $f.path 'deployment-manifest.json'),' '); Expect-Rejection { Validate $f } 'ai_package_manifest_hash' }
    'wrong source fingerprint' = { $f=New-Fixture $true; Expect-Rejection { Assert-AiCandidateManifest $workspace $f.path 'Debug' $true ('B'*64) $f.hash } 'ai_package_identity' }
    'wrong configuration' = { $f=New-Fixture $true; Expect-Rejection { Assert-AiCandidateManifest $workspace $f.path 'Release' $true $sourceHash $f.hash } 'ai_package_identity' }
    'wrong product variant' = { $f=New-Fixture $true; Expect-Rejection { Assert-AiCandidateManifest $workspace $f.path 'Debug' $false $sourceHash $f.hash } 'ai_package_identity' }
    'production identity rejected' = { $f=New-Fixture $true; Change-Manifest $f {param($m) $m.production=$true}; Expect-Rejection {Validate $f} 'ai_package_identity' }
    'file size/hash changed' = { $f=New-Fixture $true; [IO.File]::AppendAllText((Join-Path $f.path 'Ncma.AI.dll'),'changed'); Expect-Rejection {Validate $f} 'ai_package_file_integrity' }
    'extra file' = { $f=New-Fixture $true; [IO.File]::WriteAllText((Join-Path $f.path 'extra.dll'),'extra'); Expect-Rejection {Validate $f} 'ai_package_file_set' }
    'missing file without deletion' = {
        $f=New-Fixture $true; $copy=Join-Path $output ([Guid]::NewGuid().ToString('N')); New-Item -ItemType Directory -Path $copy|Out-Null
        foreach($file in @(Get-DeploymentFiles $workspace $f.path)){if($file.path -ne 'Ncma.AI.dll'){Copy-Item -LiteralPath (Join-Path $f.path $file.path) -Destination (Join-Path $copy $file.path)}}
        $f.path=$copy; Expect-Rejection {Validate $f} 'ai_package_file_set'
    }
    'default AI runtime leak' = { $f=New-Fixture $false; [IO.File]::WriteAllText((Join-Path $f.path 'Ncma.AI.dll'),'leak'); Save-Manifest $f; Expect-Rejection {Validate $f} 'ai_package_default_dependency' }
    'default AI deps-only leak' = { $f=New-Fixture $false; [IO.File]::WriteAllText((Join-Path $f.path 'NcmaEngine.deps.json'),'{"libraries":{"Ncma.AI/1.0.0":{}}}'); Save-Manifest $f; Expect-Rejection {Validate $f} 'ai_package_default_dependency' }
    'optional grpc leak' = { $f=New-Fixture $true; [IO.File]::WriteAllText((Join-Path $f.path 'Grpc.Net.Client.dll'),'leak'); Save-Manifest $f; Expect-Rejection {Validate $f} 'ai_package_forbidden_dependency' }
    'optional Python script leak' = { $f=New-Fixture $true; [IO.File]::WriteAllText((Join-Path $f.path 'worker.py'),'leak'); Save-Manifest $f; Expect-Rejection {Validate $f} 'ai_package_forbidden_dependency' }
    'optional incomplete deps' = { $f=New-Fixture $true; [IO.File]::WriteAllText((Join-Path $f.path 'NcmaEngine.deps.json'),'{"libraries":{"Ncma.AI/1.0.0":{}}}'); Save-Manifest $f; Expect-Rejection {Validate $f} 'ai_package_optional_closure' }
    'duplicate manifest member path' = { $f=New-Fixture $true; Change-Manifest $f {param($m) $m.files[1].path=$m.files[0].path}; Expect-Rejection {Validate $f} 'ai_package_file_path' }
    'path escape rejected' = { $f=New-Fixture $true; Change-Manifest $f {param($m) $m.files[0].path='../escape.dll'}; Expect-Rejection {Validate $f} 'ai_package_file_path' }
}
$failed = [Collections.Generic.List[string]]::new()
foreach($case in $cases.GetEnumerator()) {
    Write-Host "BEGIN AI candidate contract / $($case.Key)"
    try { & $case.Value; Write-Host "PASS AI candidate contract / $($case.Key)" }
    catch { $failed.Add($case.Key); Write-Host "FAIL AI candidate contract / $($case.Key): $($_.Exception)" }
}
Write-Host "SUMMARY AI candidate contracts: total=$($cases.Count), passed=$($cases.Count-$failed.Count), failed=$($failed.Count); fixtures=$output"
foreach($name in $failed){Write-Host "FAILED CASE $name"}
if($failed.Count -gt 0){exit 1}
exit 0
