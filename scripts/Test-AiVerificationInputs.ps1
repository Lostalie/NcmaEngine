[CmdletBinding()]param()
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'AiVerificationInputs.ps1')
$workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$output=Assert-DeploymentPath $workspace (Join-Path $workspace ('out/verification/m8-4/verification-contracts/'+[Guid]::NewGuid().ToString('N')))
New-Item -ItemType Directory -Path $output|Out-Null
function Save($Fixture){[IO.File]::WriteAllText($Fixture.path,($Fixture.receipt|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))}
function New-Fixture([bool]$OmitEntry=$false){
    $root=Join-Path $output ([Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $root|Out-Null
    $roots=Get-AiVerificationRoots $root 'Debug';$products=[ordered]@{}
    foreach($name in 'optional','contracts'){
        New-Item -ItemType Directory -Path $roots[$name] -Force|Out-Null
        $assembly=if($name -eq 'optional'){'Ncma.Editor.Services.Tests'}else{'Ncma.AI.Tests'}
        foreach($suffix in '.dll','.deps.json','.runtimeconfig.json'){
            if($OmitEntry -and $name -eq 'optional' -and $suffix -eq '.runtimeconfig.json'){continue}
            # Synthetic bytes only; never loaded/executed or labelled as product acceptance.
            [IO.File]::WriteAllText((Join-Path $roots[$name] ($assembly+$suffix)),'synthetic fixture',[Text.UTF8Encoding]::new($false))
        }
        $products[$name]=[pscustomobject]@{root=$roots[$name];files=@(Get-AiVerificationFiles $roots[$name])}
    }
    $path=Join-Path $root 'out/verification/m8-4/Debug/managed-inputs.json';New-Item -ItemType Directory -Path (Split-Path $path) -Force|Out-Null
    $fixture=[pscustomobject]@{root=$root;path=$path;receipt=[pscustomobject]@{schemaVersion=1;kind='compile-only';configuration='Debug';generation=('a'*32);sourceInputsSha256=('A'*64);testsExecuted=$false;products=[pscustomobject]$products}}
    Save $fixture;return $fixture
}
function Verify($Fixture){$null=Assert-AiVerificationReceipt $Fixture.root 'Debug' ('A'*64)}
function Reject([scriptblock]$Action,[string]$Code){try{& $Action}catch{if($_.Exception.Message -ceq $Code){return};throw};throw "Expected exact rejection: $Code"}
$cases=[ordered]@{
    'exact closure'={Verify (New-Fixture)}
    'stale source'={$f=New-Fixture;$f.receipt.sourceInputsSha256='B'*64;Save $f;Reject {Verify $f} 'ai_verification_stale_source'}
    'wrong configuration'={$f=New-Fixture;$f.receipt.configuration='Release';Save $f;Reject {Verify $f} 'ai_verification_identity'}
    'false acceptance flag'={$f=New-Fixture;$f.receipt.testsExecuted=$true;Save $f;Reject {Verify $f} 'ai_verification_identity'}
    'wrong root'={$f=New-Fixture;$f.receipt.products.optional.root=$output;Save $f;Reject {Verify $f} 'ai_verification_root'}
    'modified probe'={$f=New-Fixture;[IO.File]::AppendAllText((Join-Path $f.receipt.products.optional.root 'Ncma.Editor.Services.Tests.dll'),'changed');Reject {Verify $f} 'ai_verification_file_integrity'}
    'extra runtime file'={$f=New-Fixture;[IO.File]::WriteAllText((Join-Path $f.receipt.products.optional.root 'extra.dll'),'extra');Reject {Verify $f} 'ai_verification_file_set'}
    'missing entry prerequisite'={$f=New-Fixture $true;Reject {Verify $f} 'ai_verification_entry_missing'}
    'duplicate pin'={$f=New-Fixture;$f.receipt.products.optional.files[1]=$f.receipt.products.optional.files[0];Save $f;Reject {Verify $f} 'ai_verification_file_integrity'}
    'missing receipt'={$root=Join-Path $output ([Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $root|Out-Null;Reject {$null=Assert-AiVerificationReceipt $root 'Debug' ('A'*64)} 'ai_verification_missing_receipt'}
}
$failed=[Collections.Generic.List[string]]::new()
foreach($case in $cases.GetEnumerator()){
    Write-Host "BEGIN AI verification receipt / $($case.Key)"
    try{& $case.Value;Write-Host "PASS AI verification receipt / $($case.Key)"}catch{$failed.Add($case.Key);Write-Host "FAIL AI verification receipt / $($case.Key): $($_.Exception)"}
}
Write-Host "SUMMARY AI verification receipt: total=$($cases.Count), failed=$($failed.Count); evidence=$output"
foreach($name in $failed){Write-Host "FAILED CASE $name"}
if($failed.Count){exit 1};exit 0
