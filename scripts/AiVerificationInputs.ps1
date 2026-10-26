# Compile provenance only. Does not execute tests, publish, deploy or access services.
. (Join-Path $PSScriptRoot 'AiCandidatePackage.ps1')
function Get-AiVerificationRoots([string]$Workspace,[string]$Configuration) {
    if($Configuration -cnotin @('Debug','Release')){throw [IO.InvalidDataException]::new('ai_verification_configuration')}
    $root=[IO.Path]::GetFullPath($Workspace).TrimEnd('\')
    [ordered]@{
        optional=[IO.Path]::GetFullPath((Join-Path $root ("out/build/windows-ninja-"+$Configuration.ToLowerInvariant()+"/m8-4-ai/bin/Ncma.Editor.Services.Tests/"+$Configuration.ToLowerInvariant())))
        contracts=[IO.Path]::GetFullPath((Join-Path $root "managed/Ncma.AI.Tests/bin/$Configuration/net8.0"))
    }
}
function Get-AiVerificationFiles([string]$Root) {
    $Root=[IO.Path]::GetFullPath($Root).TrimEnd('\')
    if(-not (Test-Path -LiteralPath $Root -PathType Container)){throw [IO.InvalidDataException]::new('ai_verification_missing_directory')}
    for($part=[IO.DirectoryInfo]::new($Root);$null -ne $part;$part=$part.Parent){
        if($part.Exists -and ($part.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw [IO.InvalidDataException]::new('ai_verification_reparse')}
    }
    $pending=[Collections.Generic.Queue[string]]::new();$pending.Enqueue($Root)
    $files=[Collections.Generic.List[object]]::new();$directories=0
    while($pending.Count){
        if(++$directories -gt 2048){throw [IO.InvalidDataException]::new('ai_verification_directory_budget')}
        foreach($item in Get-ChildItem -LiteralPath $pending.Dequeue() -Force){
            if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw [IO.InvalidDataException]::new('ai_verification_reparse')}
            if($item.PSIsContainer){$pending.Enqueue($item.FullName)}else{
                if($files.Count -ge 2048){throw [IO.InvalidDataException]::new('ai_verification_file_budget')}
                $files.Add([pscustomobject]@{path=$item.FullName.Substring($Root.Length+1).Replace('\','/');size=$item.Length;sha256=(Get-DeploymentHash $item.FullName)})
            }
        }
    }
    return $files.ToArray()
}
function Assert-AiVerificationReceipt([string]$Workspace,[string]$Configuration,[string]$CurrentSourceHash) {
    $roots=Get-AiVerificationRoots $Workspace $Configuration
    $path=Assert-DeploymentPath $Workspace (Join-Path $Workspace "out/verification/m8-4/$Configuration/managed-inputs.json")
    if(-not (Test-Path -LiteralPath $path -PathType Leaf)){throw [IO.InvalidDataException]::new('ai_verification_missing_receipt')}
    if((Get-Item -LiteralPath $path).Length -gt 1048576){throw [IO.InvalidDataException]::new('ai_verification_receipt_budget')}
    $value=Get-Content -Raw -LiteralPath $path|ConvertFrom-Json
    if($value.schemaVersion -ne 1 -or $value.configuration -cne $Configuration -or $value.kind -cne 'compile-only' -or
        $value.testsExecuted -isnot [bool] -or $value.testsExecuted -ne $false -or $value.generation -cnotmatch '^[a-f0-9]{32}$'){
        throw [IO.InvalidDataException]::new('ai_verification_identity')
    }
    if($CurrentSourceHash -cnotmatch '^[0-9A-F]{64}$' -or $value.sourceInputsSha256 -cne $CurrentSourceHash){throw [IO.InvalidDataException]::new('ai_verification_stale_source')}
    foreach($name in 'optional','contracts'){
        $entry=$value.products.$name
        if($entry.root -cne $roots[$name]){throw [IO.InvalidDataException]::new('ai_verification_root')}
        $actual=@(Get-AiVerificationFiles $roots[$name]);$listed=@($entry.files)
        if($listed.Count -eq 0 -or $listed.Count -ne $actual.Count){throw [IO.InvalidDataException]::new('ai_verification_file_set')}
        $map=@{};foreach($file in $actual){$map.Add($file.path,$file)}
        $seen=@{}
        foreach($file in $listed){
            $relative=[string]$file.path
            if($seen.ContainsKey($relative) -or -not $map.ContainsKey($relative) -or $map[$relative].path -cne $relative -or
                $map[$relative].size -ne $file.size -or $map[$relative].sha256 -cne $file.sha256){throw [IO.InvalidDataException]::new('ai_verification_file_integrity')}
            $seen.Add($relative,$true)
        }
        $assembly=if($name -eq 'optional'){'Ncma.Editor.Services.Tests'}else{'Ncma.AI.Tests'}
        foreach($suffix in '.dll','.deps.json','.runtimeconfig.json'){
            if(-not $seen.ContainsKey($assembly+$suffix)){throw [IO.InvalidDataException]::new('ai_verification_entry_missing')}
        }
    }
    return $value
}
