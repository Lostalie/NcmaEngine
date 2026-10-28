[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$PackageIndex,[Parameter(Mandatory=$true)][string]$AcceptanceReceipt,
    [switch]$EnableAI,[string]$ConfirmFingerprint)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'ExtensionPackage.ps1')
$workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$review=Get-ExtensionInstallReview $workspace ([IO.Path]::GetFullPath($PackageIndex)) ([IO.Path]::GetFullPath($AcceptanceReceipt)) ([bool]$EnableAI)
if([string]::IsNullOrEmpty($ConfirmFingerprint)){$review|ConvertTo-Json;return}
if($ConfirmFingerprint -cnotmatch '^[0-9A-F]{64}$' -or $ConfirmFingerprint -cne $review.fingerprint){throw 'extension_install_confirmation_stale'}
# Explicit trusted local human invocation only. No MCP binding. Existing deployer
# still requires normal close, validates apphost, retains backup, journals atomically
# and rolls back failures. Never force a process closed or restart unsaved work.
Invoke-EditorDeployment -Workspace $workspace -Package $review.package -Destination (Join-Path $workspace 'out/bin') -EnableAI:$EnableAI -EnableExtensions `
    -ExpectedCandidateManifestSha256 $review.candidateManifestSha256 -ExpectedInstalledManifestSha256 $review.oldManifestSha256
Write-Output 'Installation completed by original deployer. Start NcmaEngine.exe manually; no automatic restart.'
