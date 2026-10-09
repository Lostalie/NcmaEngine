param([Parameter(Mandatory=$true)][ValidateSet('Debug','Release')][string]$Configuration,
      [Parameter(Mandatory=$true)][string]$NativePluginRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'EditorDeployment.ps1')
$uiWorkspace = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot))
$uiManaged = Join-Path $uiWorkspace "managed\Ncma.Ui.Sample\bin\$Configuration\net8.0"
$uiNative = Assert-DeploymentPath $uiWorkspace $NativePluginRoot
# Retain the original source-layout smoke in addition to the isolated actual apphost.
& dotnet (Join-Path $uiManaged 'NcmaUiSample.dll') --smoke --plugins $uiNative
if ($LASTEXITCODE -ne 0) { throw 'Original UI smoke failed.' }
$uiRoot = Assert-DeploymentPath $uiWorkspace (Join-Path $uiWorkspace ('out\verification\m7-1-c3\' + $Configuration + '\' + [Guid]::NewGuid().ToString('N')))
$uiPackage = Assert-DeploymentPath $uiWorkspace (Join-Path $uiRoot 'standalone')
$uiPlugins = Assert-DeploymentPath $uiWorkspace (Join-Path $uiPackage 'plugins')
New-Item -ItemType Directory -Path $uiPlugins | Out-Null
$uiManagedNames = @('NcmaUiSample.exe','NcmaUiSample.dll','NcmaUiSample.deps.json','NcmaUiSample.runtimeconfig.json',
    'Ncma.Assets.dll','Ncma.Interop.dll','Ncma.Platform.dll','Ncma.Rendering.dll','Ncma.Runtime.dll','Ncma.Text.dll','Ncma.Ui.dll','Ncma.Ui.Rendering.dll')
$uiNativeNames = @('NcmaPlatform.dll','NcmaRenderer.dll','NcmaText.dll','glfw3.dll')
function Copy-UiFile([string]$Source,[string]$Destination) {
    $item = Get-Item -LiteralPath $Source
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Invalid UI package input.' }
    if (Test-Path -LiteralPath $Destination) { throw 'UI package destination already exists.' }
    Copy-Item -LiteralPath $Source -Destination $Destination
    if ((Get-DeploymentHash $Source) -ne (Get-DeploymentHash $Destination)) { throw 'UI package copy hash mismatch.' }
}
foreach ($name in $uiManagedNames) { Copy-UiFile (Join-Path $uiManaged $name) (Assert-DeploymentPath $uiWorkspace (Join-Path $uiPackage $name)) }
foreach ($name in $uiNativeNames) { Copy-UiFile (Join-Path $uiNative $name) (Assert-DeploymentPath $uiWorkspace (Join-Path $uiPlugins $name)) }
$uiFiles = @(Get-DeploymentFiles $uiWorkspace $uiPackage)
if ($uiFiles.Count -ne 16) { throw 'Unexpected UI package closure.' }
# No Editor/Gui/Scene/Physics/Animation/Character/import source, assets, or native plugins shipped.
# Shared Runtime/Assets/Rendering libraries contain contracts, but instantiate no World/3D kernel.
Push-Location $uiPackage
try {
    $uiResult = & (Join-Path $uiPackage 'NcmaUiSample.exe') --smoke --plugins $uiPlugins
    if ($LASTEXITCODE -ne 0) { throw 'Source-free UI apphost failed.' }
} finally { Pop-Location }
$uiSummary = ($uiResult | Select-Object -Last 1) | ConvertFrom-Json
if (-not $uiSummary.registeredUi -or $uiSummary.frames -ne 4 -or $uiSummary.shaderGeneration -ne 1 -or $uiSummary.validationErrors -ne 0 -or $uiSummary.validationWarnings -ne 0) { throw 'UI apphost validation evidence mismatch.' }
$uiAfter = @(Get-DeploymentFiles $uiWorkspace $uiPackage)
if (($uiAfter | ConvertTo-Json -Depth 8 -Compress) -cne ($uiFiles | ConvertTo-Json -Depth 8 -Compress)) { throw 'UI apphost modified its package.' }
[IO.File]::WriteAllText((Join-Path $uiRoot 'result.json'), ([ordered]@{ schema=1; configuration=$Configuration; files=$uiFiles; apphost=$uiSummary; frameworkDependent=$true; runtimeShaderPackage=$false; targetEnvironmentAcceptance=$false } | ConvertTo-Json -Depth 8))
Write-Host "PASS registered pure UI source-free apphost; 16 exact package files; API0/0; $uiRoot"
