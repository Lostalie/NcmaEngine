$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'M8_5-TestAttribution.ps1')
$policy=Import-PowerShellDataFile (Join-Path $PSScriptRoot 'M8_5-TestGroups.psd1')
function Inventory { [pscustomobject]@{tests=@(foreach($g in $policy.Groups.Values){foreach($n in $g){[pscustomobject]@{name=$n}}})} }
function Check($value){if(-not $value){throw 'attribution assertion'}}
function Reject($work,[string]$code){try{& $work}catch{if($_.Exception.Message.StartsWith($code,[StringComparison]::Ordinal)){return};throw};throw "Expected $code"}
$cases=[ordered]@{
 '158 exact partition and extension-owned CodeGen regression'={
  $map=Get-M85TestGroups (Inventory);Check ($map.Count -eq 158);Check ($map['NcmaCodeGenTests'] -eq 'extension-new')
  Check (@($map.Values|Where-Object{$_ -eq 'extension-new'}).Count -eq 7)
  Check (@($map.Values|Where-Object{$_ -eq 'environment-blocked'}).Count -eq 10)
 }
 'later captured-build entry explicitly increases inventory to159'={
  $i=Inventory;$i.tests+=@([pscustomobject]@{name='NcmaExtensionTests.CapturedBuild'})
  $map=Get-M85TestGroups $i;Check ($map.Count -eq 159 -and $map['NcmaExtensionTests.CapturedBuild'] -eq 'extension-new')
 }
 'unknown entry cannot silently become legacy'={
  $i=Inventory;$i.tests+=@([pscustomobject]@{name='UnreviewedNewTest'});Reject {Get-M85TestGroups $i} 'm85_unclassified_test'
 }
 'missing entry cannot disappear from denominator'={
  $i=Inventory;$i.tests=@($i.tests|Where-Object name -ne 'NcmaCodeGenTests');Reject {Get-M85TestGroups $i} 'm85_missing_test'
 }
 'duplicate entry rejected'={
  $i=Inventory;$i.tests+=@($i.tests[0]);Reject {Get-M85TestGroups $i} 'm85_inventory_duplicate'
 }
}
$failed=0
foreach($case in $cases.GetEnumerator()){try{& $case.Value;Write-Output "PASS $($case.Key)"}catch{$failed++;Write-Output "FAIL $($case.Key): $_"}}
Write-Output "SUMMARY attribution: total=$($cases.Count), failed=$failed"
if($failed){exit 1}
