[CmdletBinding()]
param([ValidateSet('Fingerprint','Index','CrossShell')][string]$Group='Fingerprint', [string]$VectorOutput)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'AiCandidatePackage.ps1')
$workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$vectorScript=$PSCommandPath
function Vector {
    @([pscustomobject]@{path='engine/A.cs';size=[long]1234;sha256=('A'*64)},
      [pscustomobject]@{path='managed/z.cs';size=0;sha256=$null;deleted=$true},
      [pscustomobject]@{path=('managed/'+[char]0x89D2+[char]0x8272+'.cs');size=[long]5678;sha256=('B'*64)})
}
function Require([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Reject([scriptblock]$Action,[string]$Code){
    try{& $Action|Out-Null}catch{if($_.Exception.Message -ceq $Code){return};throw}
    throw "Expected exact failure: $Code"
}
if($VectorOutput){
    $null=Assert-DeploymentPath $workspace $VectorOutput
    if(Test-Path -LiteralPath $VectorOutput){throw 'Fresh vector evidence required'}
    $saved=[Threading.Thread]::CurrentThread.CurrentCulture
    try{
        $hashes=foreach($culture in 'en-US','tr-TR','zh-CN'){
            [Threading.Thread]::CurrentThread.CurrentCulture=[Globalization.CultureInfo]::GetCultureInfo($culture)
            Require ((@(Get-AiCandidateOrderedPaths @('scripts/z.cs','scripts/a.cs','scripts/A.cs','scripts/a.cs')) -join '|') -ceq 'scripts/A.cs|scripts/a.cs|scripts/z.cs') 'Cross-shell ordinal source order'
            [pscustomobject]@{culture=$culture;hash=(Get-AiCandidateInputHash @(Vector))}
        }
        [IO.File]::WriteAllText($VectorOutput,($hashes|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
    }finally{[Threading.Thread]::CurrentThread.CurrentCulture=$saved}
    exit 0
}
$output=Join-Path $workspace ('out/verification/m8-4/input-contracts/'+[Guid]::NewGuid().ToString('N'))
$null=Assert-DeploymentPath $workspace $output
New-Item -ItemType Directory -Path $output|Out-Null
function IndependentHash {
    # Independent single-buffer wire oracle; does not call the production streaming encoder.
    $unicode='managed/'+[char]0x89D2+[char]0x8272+'.cs'
    $text="ncma-source-inputs-v1`n11:engine/A.cs`n1234:"+('A'*64)+"`n12:managed/z.cs`n0:-`n17:"+$unicode+"`n5678:"+('B'*64)+"`n"
    $sha=[Security.Cryptography.SHA256]::Create()
    try{return [BitConverter]::ToString($sha.ComputeHash([Text.UTF8Encoding]::new($false,$true).GetBytes($text))).Replace('-','')}finally{$sha.Dispose()}
}
function New-Index {
    $generation='0123456789abcdef0123456789abcdef'
    $base=Join-Path $workspace "out/package/m2-7/Debug/$generation"
    [pscustomobject]@{schemaVersion=1;configuration='Debug';production=$false;sourceInputsSha256=('A'*64);
        products=[pscustomobject]@{editor=[pscustomobject]@{path=(Join-Path $base 'editor');manifestSha256=('B'*64)};
            editorAi=[pscustomobject]@{path=(Join-Path $base 'editor-ai');manifestSha256=('C'*64)}}}
}
function Read-Index($Value,[bool]$AI=$true,[string]$Source=('A'*64)){
    $path=Join-Path $output ([Guid]::NewGuid().ToString('N')+'.json')
    [IO.File]::WriteAllText($path,($Value|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
    Get-AiCandidateIndexedProduct $workspace $path 'Debug' $AI $Source
}
$cases=[ordered]@{}
if($Group -eq 'Fingerprint'){
    $cases['typed ordinal path ordering']={Require ((@(Get-AiCandidateOrderedPaths @('scripts/z.cs','scripts/a.cs','scripts/A.cs','scripts/a.cs')) -join '|') -ceq 'scripts/A.cs|scripts/a.cs|scripts/z.cs') 'Ordinal order, not culture order'}
    $cases['versioned wire oracle Unicode/tombstone']={Require ((Get-AiCandidateInputHash @(Vector)) -ceq (IndependentHash)) 'Independent wire oracle'}
    $cases['changed content']={$v=@(Vector);$v[0].sha256='C'*64;Require ((Get-AiCandidateInputHash $v) -cne (IndependentHash)) 'Content drift'}
    $cases['changed length']={$v=@(Vector);$v[0].size++;Require ((Get-AiCandidateInputHash $v) -cne (IndependentHash)) 'Length drift'}
    $cases['changed path']={$v=@(Vector);$v[0].path='engine/B.cs';Require ((Get-AiCandidateInputHash $v) -cne (IndependentHash)) 'Path drift'}
    $cases['deleted versus empty']={$v=@(Vector);$v[1]=[pscustomobject]@{path='managed/z.cs';size=0;sha256=('D'*64)};Require ((Get-AiCandidateInputHash $v) -cne (IndependentHash)) 'Tombstone drift'}
    $cases['empty rejected']={Reject {Get-AiCandidateInputHash @()} 'ai_package_source_empty'}
    $cases['unsorted rejected']={$v=@(Vector);Reject {Get-AiCandidateInputHash @($v[1],$v[0])} 'ai_package_source_order'}
    $cases['duplicate rejected']={$v=@(Vector);Reject {Get-AiCandidateInputHash @($v[0],$v[0])} 'ai_package_source_order'}
    $cases['lowercase hash rejected']={$v=@(Vector);$v[0].sha256='a'*64;Reject {Get-AiCandidateInputHash $v} 'ai_package_source_hash'}
    $cases['negative length rejected']={$v=@(Vector);$v[0].size=-1;Reject {Get-AiCandidateInputHash $v} 'ai_package_source_size'}
    $cases['string length rejected']={$v=@(Vector);$v[0].size='1234';Reject {Get-AiCandidateInputHash $v} 'ai_package_source_size'}
    $cases['tombstone content rejected']={$v=@(Vector);$v[1].sha256='A'*64;Reject {Get-AiCandidateInputHash $v} 'ai_package_source_size'}
    $cases['path escape rejected']={$v=@(Vector);$v[0].path='../escape';Reject {Get-AiCandidateInputHash $v} 'ai_package_source_path'}
    $cases['newline path rejected']={$v=@(Vector);$v[0].path="engine/evil`n.cs";Reject {Get-AiCandidateInputHash $v} 'ai_package_source_path'}
}
if($Group -eq 'Index'){
    $cases['default exact indexed product']={$i=New-Index;$r=Read-Index $i $false;Require ($r.path -ceq [IO.Path]::GetFullPath($i.products.editor.path)) 'Default selection'}
    $cases['AI exact indexed product']={$i=New-Index;$r=Read-Index $i;Require ($r.path -ceq [IO.Path]::GetFullPath($i.products.editorAi.path)) 'AI selection'}
    $cases['stale source rejected']={Reject {Read-Index (New-Index) $true ('D'*64)} 'ai_index_stale_source'}
    $cases['wrong configuration rejected']={$i=New-Index;$i.configuration='Release';Reject {Read-Index $i} 'ai_index_identity'}
    $cases['production rejected']={$i=New-Index;$i.production=$true;Reject {Read-Index $i} 'ai_index_identity'}
    $cases['string production rejected']={$i=New-Index;$i.production='false';Reject {Read-Index $i} 'ai_index_identity'}
    $cases['wrong schema rejected']={$i=New-Index;$i.schemaVersion=2;Reject {Read-Index $i} 'ai_index_identity'}
    $cases['invalid manifest pin rejected']={$i=New-Index;$i.products.editorAi.manifestSha256='bad';Reject {Read-Index $i} 'ai_index_entry'}
    $cases['relative path rejected']={$i=New-Index;$i.products.editorAi.path='editor-ai';Reject {Read-Index $i} 'ai_index_entry'}
    $cases['outside package rejected']={$i=New-Index;$i.products.editorAi.path=Join-Path $workspace 'out/bin';Reject {Read-Index $i} 'ai_index_path'}
    $cases['wrong variant leaf rejected']={$i=New-Index;$i.products.editorAi.path=$i.products.editor.path;Reject {Read-Index $i} 'ai_index_path'}
    $cases['mixed generations rejected']={$i=New-Index;$i.products.editorAi.path=$i.products.editorAi.path.Replace('0123456789abcdef0123456789abcdef',('a'*32));Reject {Read-Index $i} 'ai_index_generation'}
}
if($Group -eq 'CrossShell'){
    foreach($shellName in 'powershell.exe','pwsh.exe'){
        $name=$shellName
        $cases[$name+' three cultures']=$([scriptblock]::Create(@'
            $name='SHELL'
            $shell=(Get-Command $name -CommandType Application -ErrorAction Stop).Source
            $result=Join-Path $output ($name+'.json')
            $info=[Diagnostics.ProcessStartInfo]::new();$info.FileName=$shell
            $info.Arguments='-NoLogo -NoProfile -ExecutionPolicy Bypass -File "'+$vectorScript+'" -VectorOutput "'+$result+'"'
            $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
            # Child emits no stdout; only a bounded vector file. Missing shell fails, never skips.
            $child=[Diagnostics.Process]::Start($info)
            try{
                [ordered]@{pid=$child.Id;startedUtc=$child.StartTime.ToUniversalTime().ToString('o');executable=$shell;arguments=$info.Arguments}|ConvertTo-Json|Set-Content (Join-Path $output ($name+'-process.json'))
                if(-not $child.WaitForExit(15000)){throw 'Vector child timeout; identity retained, no automatic kill'}
                Require ($child.ExitCode -eq 0) 'Vector child failure'
                Require ((Get-Item -LiteralPath $result).Length -le 4096) 'Vector output budget'
                # PS5 emits the JSON array as one pipeline object; expand the returned
                # value explicitly instead of counting pipeline emissions.
                $decoded=Get-Content -Raw -LiteralPath $result|ConvertFrom-Json
                $rows=@($decoded)
                Require ($rows.Count -eq 3) 'Culture count'
                Require (($rows.culture -join '|') -ceq 'en-US|tr-TR|zh-CN') 'Exact culture coverage'
                foreach($row in $rows){Require ($row.hash -ceq (IndependentHash)) ('Cross-shell wire mismatch '+$row.culture)}
            }finally{$child.Dispose()}
'@.Replace('SHELL',$name)))
    }
}
$failed=[Collections.Generic.List[string]]::new()
foreach($case in $cases.GetEnumerator()){
    Write-Host "BEGIN AI input $Group / $($case.Key)"
    try{& $case.Value;Write-Host "PASS AI input $Group / $($case.Key)"}catch{$failed.Add($case.Key);Write-Host "FAIL AI input $Group / $($case.Key): $($_.Exception)"}
}
Write-Host "SUMMARY AI input ${Group}: total=$($cases.Count), failed=$($failed.Count); evidence=$output"
foreach($name in $failed){Write-Host "FAILED CASE $name"}
if($failed.Count){exit 1};exit 0
