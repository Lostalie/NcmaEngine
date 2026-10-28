# Reporting only. Never rewrites JUnit or creates installation acceptance.
function Get-M85TestGroups($Inventory) {
    $policy=Import-PowerShellDataFile (Join-Path $PSScriptRoot 'M8_5-TestGroups.psd1')
    $map=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach($group in @('extension-new','m84-legacy','environment-blocked')) {
        foreach($name in $policy.Groups[$group]) { $map.Add($name,$group) }
    }
    if($map.Count -ne $policy.BaselineCount){throw 'm85_group_baseline_count'}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($test in $Inventory.tests) {
        if(-not $seen.Add($test.name)){throw 'm85_inventory_duplicate'}
        if(-not $map.ContainsKey($test.name)) {
            if(-not $policy.Additions.ContainsKey($test.name)){throw "m85_unclassified_test: $($test.name)"}
            $map.Add($test.name,$policy.Additions[$test.name])
        }
    }
    foreach($name in $map.Keys){if(-not $seen.Contains($name)){throw "m85_missing_test: $name"}}
    return ,$map
}
function New-M85AttributionReport([string]$InventoryPath,[string]$JUnitPath,[string]$Configuration,[string]$EvidenceRole) {
    $inventory=Get-Content -Raw -LiteralPath $InventoryPath|ConvertFrom-Json
    $groups=Get-M85TestGroups $inventory
    if($EvidenceRole -eq 'current-extension-only') {
        $policy=Import-PowerShellDataFile (Join-Path $PSScriptRoot 'M8_5-TestGroups.psd1')
        foreach($name in $policy.Additions.Keys){if(-not $groups.ContainsKey($name)){throw "m85_missing_current_test: $name"}}
    }
    $raw=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    if($JUnitPath) {
        $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null
        $reader=[Xml.XmlReader]::Create($JUnitPath,$settings)
        try{$xml=[Xml.XmlDocument]::new();$xml.XmlResolver=$null;$xml.Load($reader)}finally{$reader.Dispose()}
        foreach($case in $xml.SelectNodes('//testcase')) {
            $name=$case.GetAttribute('name');if(-not $groups.ContainsKey($name)){throw "m85_junit_unknown: $name"}
            $status=$case.GetAttribute('status')
            if($null -ne $case.SelectSingleNode('failure|error')){$status='fail'}
            elseif($null -ne $case.SelectSingleNode('skipped')){$status='notrun'}
            $raw.Add($name,$status)
        }
    }
    $rows=@(foreach($test in $inventory.tests) {
        $group=$groups[$test.name];$status=if($raw.ContainsKey($test.name)){$raw[$test.name]}else{'not-executed'}
        $decision=switch($group) {
            'extension-new' {if($status -eq 'run'){'passed'}elseif($status -eq 'fail'){'failed'}else{'not-executed'}}
            'm84-legacy' {'refer-to-M8.4-F6/F7'}
            # ASCII source also parses under Windows PowerShell 5.1 (ANSI default).
            'environment-blocked' {-join [char[]]@(0x672A,0x6267,0x884C,0xFF08,0x73AF,0x5883,0xFF09)}
        }
        [ordered]@{name=$test.name;group=$group;rawStatus=$status;dStatus=$decision}
    })
    $ext=@($rows|Where-Object group -eq 'extension-new')
    $pass=@($ext|Where-Object dStatus -eq 'passed').Count;$failed=@($ext|Where-Object dStatus -eq 'failed').Count
    [ordered]@{
        kind='ncma.m8.5.attribution.v1';configuration=$Configuration;evidenceRole=$EvidenceRole
        inventory=$InventoryPath;inventorySha256=(Get-FileHash $InventoryPath -Algorithm SHA256).Hash
        junit=$JUnitPath;junitSha256=$(if($JUnitPath){(Get-FileHash $JUnitPath -Algorithm SHA256).Hash}else{$null})
        policySha256=(Get-FileHash (Join-Path $PSScriptRoot 'M8_5-TestGroups.psd1') -Algorithm SHA256).Hash
        reporterSha256=(Get-FileHash (Join-Path $PSScriptRoot 'M8_5-TestAttribution.ps1') -Algorithm SHA256).Hash
        conclusionScope='extension-new';extensionExpected=$ext.Count;extensionPassed=$pass;extensionFailed=$failed
        extensionNotExecuted=($ext.Count-$pass-$failed)
        conclusion=$(if($failed){'failed'}elseif($pass -eq $ext.Count){'passed'}else{'incomplete'})
        counts=@{'extension-new'=$ext.Count;'m84-legacy'=@($rows|Where-Object group -eq 'm84-legacy').Count;'environment-blocked'=@($rows|Where-Object group -eq 'environment-blocked').Count}
        legacyReports=(Import-PowerShellDataFile (Join-Path $PSScriptRoot 'M8_5-TestGroups.psd1')).LegacyReports
        environmentStatusMeaning='Current D acceptance not executed (environment); raw historical attempts and partial cases are not relabelled.'
        wholeEngineAccepted=$false;accepted=$false;installationAuthorized=$false;tests=$rows
    }
}
