# Shared candidate/installed Editor dependency policy; no process or network access.
function Assert-AiCandidateDependencies([string]$Workspace, [string]$Directory, [bool]$EnableAI, [bool]$EnableExtensions = $false) {
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
    Assert-ExtensionCandidateDependencies $Workspace $Directory $EnableExtensions
}

# Same compiled component module in optional Editor AND Player. No dynamic loader,
# no editor assembly in Player, no generators/compiler packages shipped as runtime.
function Assert-ExtensionCandidateDependencies([string]$Workspace, [string]$Directory, [bool]$Enabled) {
    $files = @(Get-DeploymentFiles $Workspace $Directory)
    $expected = @('Ncma.Extensions','Ncma.Extensions.Hosting')
    foreach ($file in $files) {
        $name = [IO.Path]::GetFileName($file.path)
        if ($name -match '^Ncma\.Extensions.*\.dll$' -and (-not $Enabled -or $name -cnotin @('Ncma.Extensions.dll','Ncma.Extensions.Hosting.dll'))) {
            throw [IO.InvalidDataException]::new('extension_package_dependency')
        }
        if ($name.EndsWith('.deps.json',[StringComparison]::OrdinalIgnoreCase)) {
            if ($file.size -gt 4194304) { throw [IO.InvalidDataException]::new('extension_package_deps_budget') }
            $deps=Get-Content -Raw -LiteralPath (Join-Path $Directory $file.path)|ConvertFrom-Json
            foreach ($library in $deps.libraries.PSObject.Properties.Name) {
                if ($library -match '^Ncma\.Extensions(/|\.)' -and (-not $Enabled -or $library.Split('/')[0] -cnotin $expected)) {
                    throw [IO.InvalidDataException]::new('extension_package_dependency')
                }
            }
        }
    }
    if ($Enabled) {
        $rootDeps=@('NcmaEngine.deps.json','NcmaPlayer.deps.json')|Where-Object{Test-Path -LiteralPath (Join-Path $Directory $_) -PathType Leaf}
        if (@($rootDeps).Count -ne 1) { throw [IO.InvalidDataException]::new('extension_package_host') }
        $deps=Get-Content -Raw -LiteralPath (Join-Path $Directory $rootDeps)|ConvertFrom-Json
        foreach ($name in $expected) {
            if (@($deps.libraries.PSObject.Properties.Name|Where-Object{$_.Split('/')[0] -ceq $name}).Count -ne 1 -or
                !(Test-Path -LiteralPath (Join-Path $Directory ($name+'.dll')) -PathType Leaf)) { throw [IO.InvalidDataException]::new('extension_package_closure') }
        }
        if ($rootDeps -eq 'NcmaPlayer.deps.json' -and @($deps.libraries.PSObject.Properties.Name|Where-Object{$_ -match '^Ncma\.(Editor|Gui|AI|Modules)(/|\.)'}).Count) {
            throw [IO.InvalidDataException]::new('extension_player_editor_dependency')
        }
    }
}
