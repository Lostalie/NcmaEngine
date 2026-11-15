# Read-only classification and exact registration checks. No run/build/deploy.
# Historical environmental attribution is NOT evidence about the current host.
function Get-M86AbsolutePath([string]$Path) {
    # Local Windows test inputs only; avoid IsPathFullyQualified (not in PS5 BCL).
    if([string]::IsNullOrWhiteSpace($Path) -or $Path -notmatch '^[A-Za-z]:[\\/]'){throw 'm86_inventory_path'}
    return [IO.Path]::GetFullPath($Path).TrimEnd([char]'\',[char]'/')
}
function Get-M86ExpectedTests {
    $policy=Import-PowerShellDataFile (Join-Path $PSScriptRoot 'M8_6-TestGroups.psd1')
    $old=Import-PowerShellDataFile (Join-Path $PSScriptRoot $policy.HistoricalPolicy)
    $map=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach($group in @('extension-new','m84-legacy','environment-blocked')) {
        foreach($name in $old.Groups[$group]){$map.Add($name,[pscustomobject]@{Group='historical-'+$group;Spec=$null})}
    }
    foreach($name in $old.Additions.Keys){$map.Add($name,[pscustomobject]@{Group='historical-'+$old.Additions[$name];Spec=$null})}
    if($map.Count -ne $policy.HistoricalCount){throw 'm86_historical_count'}
    foreach($spec in $policy.Workflow){$map.Add($spec.Name,[pscustomobject]@{Group='workflow-new';Spec=$spec})}
    if($map.Count -ne $policy.ExpectedCount){throw 'm86_policy_count'}
    return ,$map
}
function Get-M86TestInventory($Inventory) {
    $expected=Get-M86ExpectedTests
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $rows=@(foreach($test in $Inventory.tests) {
        if(-not $seen.Add($test.name)){throw 'm86_inventory_duplicate'}
        if(-not $expected.ContainsKey($test.name)){throw "m86_inventory_unknown: $($test.name)"}
        # CTest may omit command for an unbuilt historical native executable.
        # Preserve inventory-only reporting; it never proves runtime readiness.
        $commandProperty=$test.PSObject.Properties['command']
        $args=@();if($null -ne $commandProperty){$args=@($commandProperty.Value)}
        # Historical and new script entries obey the selected host's policy.
        # Do not let an abbreviated switch evade the registration guard.
        if($args.Count -gt 0 -and [IO.Path]::GetFileName([string]$args[0]) -iin @('powershell.exe','pwsh.exe')) {
            for($argumentIndex=1;$argumentIndex -lt $args.Count;$argumentIndex++) {
                $text=[string]$args[$argumentIndex]
                if($text.Length -lt 3 -or ($text[0] -ne '-' -and $text[0] -ne '/')){continue}
                $option=$text.Substring(1).Split([char]':')[0]
                if($option -ieq 'ep' -or ($option.Length -ge 2 -and 'ExecutionPolicy'.StartsWith($option,[StringComparison]::OrdinalIgnoreCase))){
                    throw "m86_inventory_execution_policy: $($test.name)"
                }
            }
        }
        $entry=$expected[$test.name]
        $properties=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
        foreach($property in $test.properties){$properties.Add($property.name,$property.value)}
        if($properties.ContainsKey('DISABLED') -and $properties['DISABLED']){throw "m86_inventory_disabled: $($test.name)"}
        $configured=$true
        if($entry.Group -eq 'workflow-new') {
            $spec=$entry.Spec
            if(-not $properties.ContainsKey('TIMEOUT') -or [double]$properties['TIMEOUT'] -ne $spec.Timeout){throw "m86_inventory_timeout: $($test.name)"}
            if(-not $properties.ContainsKey('RUN_SERIAL') -or -not $properties['RUN_SERIAL']){throw "m86_inventory_serial: $($test.name)"}
            if(-not $properties.ContainsKey('LABELS') -or @($properties['LABELS']) -cnotcontains 'workflow-new' -or @($properties['LABELS']) -cnotcontains $spec.Role){throw "m86_inventory_label: $($test.name)"}
            if($args.Count -lt 5){throw "m86_inventory_command: $($test.name)"}
            $hostPath=Get-M86AbsolutePath ([string]$args[0])
            $hostName=if($spec.ContainsKey('Switch')){'dotnet.exe'}else{'powershell.exe'}
            if([IO.Path]::GetFileName($hostPath) -ine $hostName){throw "m86_inventory_host: $($test.name)"}
            if(-not $properties.ContainsKey('WORKING_DIRECTORY')){throw "m86_inventory_working_directory: $($test.name)"}
            $working=Get-M86AbsolutePath ([string]$properties['WORKING_DIRECTORY'])
            if($spec.ContainsKey('Switch')) {
                $count=if($spec.Name -cin @('NcmaWorkflowTests.CaptureHost','NcmaWorkflowTests.SourceCreationHost','NcmaWorkflowTests.SdkChain')){6}else{5}
                $emptySdkHarness=$spec.Name -ceq 'NcmaWorkflowTests.SdkChain' -and [string]::IsNullOrWhiteSpace($args[1])
                if($args.Count -ne $count -or $args[4] -cne $spec.Switch -or (-not $emptySdkHarness -and [IO.Path]::GetFileName($args[1]) -cne 'Ncma.Editor.Services.Tests.dll')){throw "m86_inventory_command: $($test.name)"}
                if(-not $emptySdkHarness){$null=Get-M86AbsolutePath ([string]$args[1])}
                if((Get-M86AbsolutePath ([string]$args[2])) -ine $working){throw "m86_inventory_working_directory: $($test.name)"}
                $null=Get-M86AbsolutePath ([string]$args[3])
                if($spec.Name -ceq 'NcmaWorkflowTests.CaptureHost'){$id=[Guid]::Empty;$configured=[Guid]::TryParseExact($args[5],'N',[ref]$id) -and $id -ne [Guid]::Empty}
                elseif($spec.Name -ceq 'NcmaWorkflowTests.SourceCreationHost'){$configured= -not [string]::IsNullOrWhiteSpace($args[5]) -and [IO.Path]::IsPathRooted($args[5]) -and [IO.Path]::GetFileName($args[5]) -ceq 'git.exe'}
                elseif($spec.Name -ceq 'NcmaWorkflowTests.SdkChain'){$configured= -not $emptySdkHarness -and -not [string]::IsNullOrWhiteSpace($args[5]) -and [IO.Path]::IsPathRooted($args[5]) -and [IO.Path]::IsPathRooted($args[1])}
            } else {
                if($args.Count -ne 5 -or $args[1] -cne '-NoLogo' -or $args[2] -cne '-NoProfile' -or $args[3] -cne '-File' -or
                    [IO.Path]::GetFileName($args[4]) -cne $spec.Script){throw "m86_inventory_command: $($test.name)"}
                $scriptPath=Get-M86AbsolutePath ([string]$args[4])
                if($scriptPath -ine (Join-Path (Join-Path $working 'scripts') $spec.Script)){throw "m86_inventory_script_location: $($test.name)"}
            }
        }
        [pscustomobject]@{Name=$test.name;Group=$entry.Group;Role=$(if($null -ne $entry.Spec){$entry.Spec.Role}else{'historical'});
            ExplicitInputsConfigured=$configured;Status='not-executed';RuntimeDependenciesVerified=$false}
    })
    foreach($name in $expected.Keys){if(-not $seen.Contains($name)){throw "m86_inventory_missing: $name"}}
    return [pscustomobject]@{Kind='ncma.m8.6.inventory.v1';Expected=$expected.Count;Registered=$rows.Count;
        WorkflowCount=@($rows|Where-Object Group -ceq 'workflow-new').Count;HistoricalCount=@($rows|Where-Object Group -cne 'workflow-new').Count;
        InputsConfigured=(@($rows|Where-Object {-not $_.ExplicitInputsConfigured}).Count -eq 0);
        TestsExecuted=$false;StageAccepted=$false;InstallationAuthorized=$false;Tests=$rows}
}
