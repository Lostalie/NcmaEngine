# Shared candidate/installed Editor dependency policy; no process or network access.
function Assert-AiCandidateDependencies([string]$Workspace, [string]$Directory, [bool]$EnableAI) {
    $files = @(Get-DeploymentFiles $Workspace $Directory)
    $optional = @('Ncma.AI','Ncma.Modules','Ncma.Editor.Modules')
    foreach ($file in $files) {
        $name = [IO.Path]::GetFileName($file.path)
        if ($name -match '^(?i:grpc|python|Ncma\.Modules\.Grpc|Microsoft\.CodeAnalysis|Ncma\.CodeGen)' -or
            $name -match '(?i)\.(py|pyc|key|pem)$' -or $name -eq '.env') { throw [IO.InvalidDataException]::new('ai_package_forbidden_dependency') }
        if (-not $EnableAI -and $name -match '^Ncma\.(AI|Modules|Editor\.Modules)(\.|$)') { throw [IO.InvalidDataException]::new('ai_package_default_dependency') }
        if ($name.EndsWith('.deps.json', [StringComparison]::OrdinalIgnoreCase)) {
            if ($file.size -gt 4194304) { throw [IO.InvalidDataException]::new('ai_package_deps_budget') }
            $deps = Get-Content -Raw -LiteralPath (Join-Path $Directory $file.path) | ConvertFrom-Json
            foreach ($library in $deps.libraries.PSObject.Properties.Name) {
                if ($library -match '^(?i:Grpc|Python|Ncma\.Modules\.Grpc|Microsoft\.CodeAnalysis|Ncma\.CodeGen)(/|\.)') { throw [IO.InvalidDataException]::new('ai_package_forbidden_dependency') }
                if (-not $EnableAI -and $library -match '^Ncma\.(AI|Modules|Editor\.Modules)(/|\.)') { throw [IO.InvalidDataException]::new('ai_package_default_dependency') }
            }
        }
    }
    if ($EnableAI) {
        $depsPath = Join-Path $Directory 'NcmaEngine.deps.json'
        if (!(Test-Path -LiteralPath $depsPath -PathType Leaf)) { throw [IO.InvalidDataException]::new('ai_package_missing_deps') }
        $deps = Get-Content -Raw -LiteralPath $depsPath | ConvertFrom-Json
        $actual = @($deps.libraries.PSObject.Properties.Name | Where-Object { $_ -match '^Ncma\.(AI|Modules|Editor\.Modules)(/|\.)' })
        if ($actual.Count -ne 3) { throw [IO.InvalidDataException]::new('ai_package_optional_closure') }
        foreach ($name in $optional) {
            if (@($actual | Where-Object { $_.Split('/')[0] -ceq $name }).Count -ne 1 -or
                !(Test-Path -LiteralPath (Join-Path $Directory ($name + '.dll')) -PathType Leaf)) { throw [IO.InvalidDataException]::new('ai_package_optional_closure') }
        }
    }
}
