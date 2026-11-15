$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'M8_6-TestInventory.ps1')
function Check([bool]$value){if(-not $value){throw 'm86_inventory_assertion'}}
function Reject([scriptblock]$action,[string]$code){try{& $action|Out-Null}catch{if($_.Exception.Message.StartsWith($code,[StringComparison]::Ordinal)){return};throw};throw "Expected $code"}
function Inventory {
    $expected=Get-M86ExpectedTests
    [pscustomobject]@{tests=@(foreach($name in $expected.Keys){
        $entry=$expected[$name];$command=@('C:\fixture\test.exe');$properties=@()
        if($null -ne $entry.Spec){
            $s=$entry.Spec;$properties=@([pscustomobject]@{name='TIMEOUT';value=$s.Timeout},[pscustomobject]@{name='RUN_SERIAL';value=$true},[pscustomobject]@{name='LABELS';value=@('workflow-new',$s.Role)},[pscustomobject]@{name='WORKING_DIRECTORY';value='C:\fixture'})
            if($s.ContainsKey('Switch')){$command=@('C:\fixture\dotnet.exe','C:\fixture\Ncma.Editor.Services.Tests.dll','C:\fixture','C:\fixture\plugins',$s.Switch);if($s.Name -ceq 'NcmaWorkflowTests.CaptureHost'){$command+=@([Guid]::NewGuid().ToString('N'))};if($s.Name -ceq 'NcmaWorkflowTests.SourceCreationHost'){$command+=@('C:\fixture\git.exe')};if($s.Name -ceq 'NcmaWorkflowTests.SdkChain'){$command+=@('C:\fixture\sdk-inputs.json')}}
            else{$command=@('C:\Windows\powershell.exe','-NoLogo','-NoProfile','-File',('C:\fixture\scripts\'+$s.Script))}
        }
        [pscustomobject]@{name=$name;command=$command;properties=$properties}
    })}
}
$cases=[ordered]@{
    'exact old159 plus new21; inventory cannot prove tests or environment acceptance'={
        $result=Get-M86TestInventory (Inventory)
        Check ($result.Registered -eq 180 -and $result.WorkflowCount -eq 21 -and $result.HistoricalCount -eq 159)
        Check (-not $result.TestsExecuted -and -not $result.StageAccepted -and -not $result.InstallationAuthorized)
        Check (@($result.Tests|Where-Object Group -ceq 'historical-environment-blocked').Count -eq 10)
        Check (@($result.Tests|Where-Object Status -cne 'not-executed').Count -eq 0)
    }
    'missing unknown duplicate and disabled entries reject'={
        $i=Inventory;$i.tests=$i.tests[1..($i.tests.Count-1)];Reject {Get-M86TestInventory $i} 'm86_inventory_missing'
        $i=Inventory;$i.tests[0].name='Unreviewed';Reject {Get-M86TestInventory $i} 'm86_inventory_unknown'
        $i=Inventory;$i.tests+=@($i.tests[0]);Reject {Get-M86TestInventory $i} 'm86_inventory_duplicate'
        $i=Inventory;$i.tests[0].properties+=@([pscustomobject]@{name='DISABLED';value=$true});Reject {Get-M86TestInventory $i} 'm86_inventory_disabled'
    }
    'timeout serial labels and command are exact not prefix filters'={
        foreach($field in @('TIMEOUT','RUN_SERIAL','LABELS')){
            $i=Inventory;$test=$i.tests|Where-Object name -ceq 'NcmaWorkflowTests.Contracts'
            $p=$test.properties|Where-Object name -ceq $field;$p.value=switch($field){TIMEOUT{300}RUN_SERIAL{$false}LABELS{@('historical')}}
            Reject {Get-M86TestInventory $i} ('m86_inventory_'+@{TIMEOUT='timeout';RUN_SERIAL='serial';LABELS='label'}[$field])
        }
        $i=Inventory;($i.tests|Where-Object name -ceq 'NcmaWorkflowTests.Contracts').command[4]='--other';Reject {Get-M86TestInventory $i} 'm86_inventory_command'
    }
    'unconfigured real capture stays registered and explicitly unready'={
        $i=Inventory;($i.tests|Where-Object name -ceq 'NcmaWorkflowTests.CaptureHost').command[5]=''
        $r=Get-M86TestInventory $i;Check ($r.Registered -eq 180 -and -not $r.InputsConfigured -and -not $r.StageAccepted)
        $i=Inventory;($i.tests|Where-Object name -ceq 'NcmaWorkflowTests.SourceCreationHost').command[5]=''
        $r=Get-M86TestInventory $i;Check ($r.Registered -eq 180 -and -not $r.InputsConfigured -and -not $r.StageAccepted)
    }
    'new script entries cannot acquire execution policy bypass'={
        $i=Inventory;$test=$i.tests|Where-Object name -ceq 'NcmaWorkflowTests.CaptureInputs';$test.command+=@('-ExecutionPolicy','Bypass')
        Reject {Get-M86TestInventory $i} 'm86_inventory_execution_policy'
    }
    'historical PS5 and PS7 entries cannot override execution policy'={
        foreach($hostName in @('powershell.exe','pwsh.exe')){
            foreach($option in @('-ExecutionPolicy','-executionpolicy','-ep','-Ex','/ExecutionPolicy:Bypass')){
                $i=Inventory;$test=$i.tests|Where-Object name -ceq 'NcmaAiPackageTests.Input.CrossShell'
                $test.command=@(('C:\fixture\'+$hostName),'-NoLogo','-NoProfile',$option,'Bypass','-File','C:\fixture\scripts\Test-AiCandidateInputs.ps1')
                Reject {Get-M86TestInventory $i} 'm86_inventory_execution_policy'
            }
        }
    }
    'historical script entry keeps its host and normal switches without policy override'={
        $i=Inventory;$test=$i.tests|Where-Object name -ceq 'NcmaAiPackageTests.Input.CrossShell'
        $test.command=@('C:\fixture\powershell.exe','-NoLogo','-NoProfile','-File','C:\fixture\scripts\Test-AiCandidateInputs.ps1')
        $r=Get-M86TestInventory $i;Check ($r.Registered -eq 180 -and -not $r.TestsExecuted -and -not $r.StageAccepted)
    }
    'missing historical native executable is inventory only never runtime acceptance'={
        $i=Inventory;$historical=(Get-M86ExpectedTests).GetEnumerator()|Where-Object {$_.Value.Group -cne 'workflow-new'}|Select-Object -First 1
        $test=$i.tests|Where-Object name -ceq $historical.Key;$test.PSObject.Properties.Remove('command')
        $r=Get-M86TestInventory $i;Check ($r.Registered -eq 180 -and -not $r.TestsExecuted -and -not $r.StageAccepted)
        Check (@($r.Tests|Where-Object RuntimeDependenciesVerified).Count -eq 0)
    }
    'host executable and absolute command paths are mandatory'={
        foreach($name in @('NcmaWorkflowTests.Contracts','NcmaWorkflowTests.CaptureInputs')){
            $i=Inventory;($i.tests|Where-Object name -ceq $name).command[0]='C:\fixture\other.exe'
            Reject {Get-M86TestInventory $i} 'm86_inventory_host'
            $i=Inventory;($i.tests|Where-Object name -ceq $name).command[0]='dotnet.exe'
            Reject {Get-M86TestInventory $i} 'm86_inventory_path'
        }
        foreach($index in @(1,2,3)){
            $i=Inventory;($i.tests|Where-Object name -ceq 'NcmaWorkflowTests.Contracts').command[$index]=if($index -eq 1){'Ncma.Editor.Services.Tests.dll'}else{'relative'}
            Reject {Get-M86TestInventory $i} 'm86_inventory_path'
        }
    }
    'working directory and script provenance cannot be substituted'={
        $i=Inventory;$test=$i.tests|Where-Object name -ceq 'NcmaWorkflowTests.Contracts'
        ($test.properties|Where-Object name -ceq 'WORKING_DIRECTORY').value='C:\other'
        Reject {Get-M86TestInventory $i} 'm86_inventory_working_directory'
        $i=Inventory;$test=$i.tests|Where-Object name -ceq 'NcmaWorkflowTests.Contracts'
        $test.properties=@($test.properties|Where-Object name -cne 'WORKING_DIRECTORY')
        Reject {Get-M86TestInventory $i} 'm86_inventory_working_directory'
        $i=Inventory;($i.tests|Where-Object name -ceq 'NcmaWorkflowTests.CaptureInputs').command[4]='C:\other\Test-WorkflowBuildInputs.ps1'
        Reject {Get-M86TestInventory $i} 'm86_inventory_script_location'
        $i=Inventory;$test=$i.tests|Where-Object name -ceq 'NcmaWorkflowTests.Contracts'
        ($test.properties|Where-Object name -ceq 'WORKING_DIRECTORY').value='c:/fixture/'
        Check ((Get-M86TestInventory $i).Registered -eq 180)
    }
}
$failed=0
foreach($case in $cases.GetEnumerator()){
    Write-Output "BEGIN M8.6 inventory / $($case.Key)"
    try{& $case.Value;Write-Output "PASS M8.6 inventory / $($case.Key)"}catch{$failed++;Write-Output "FAIL M8.6 inventory / $($case.Key): $($_.Exception.GetType().Name)"}
}
Write-Output "SUMMARY M8.6 inventory: total=$($cases.Count), failed=$failed"
if($failed){exit 1};exit 0
