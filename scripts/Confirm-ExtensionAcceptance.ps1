[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$DraftReceipt,[Parameter(Mandatory=$true)][string]$PackageIndex,
 [Parameter(Mandatory=$true)][string]$ConfirmDraftSha256)
# Trusted human-only local step, not an AI/MCP action. The draft explicitly lists
# both full compile reports, CTest inventory+JUnit and hashed evidence. A hash
# confirmation records manual package/normal-close/restart checks, not a signature.
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'ExtensionPackage.ps1')
$workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$draft=Assert-DeploymentPath $workspace ([IO.Path]::GetFullPath($DraftReceipt));$index=Assert-DeploymentPath $workspace ([IO.Path]::GetFullPath($PackageIndex))
if($ConfirmDraftSha256 -cnotmatch '^[0-9A-F]{64}$' -or (Get-DeploymentHash $draft) -cne $ConfirmDraftSha256){throw 'extension_manual_review_stale'}
$capture=Open-ExtensionPackageCapture $workspace $index
try {
$source=$capture.Manifest.sourceInputsSha256;$null=Read-ExtensionPackageSet $workspace $index $source
if((Get-Item -LiteralPath $draft).Length -gt 4194304){throw 'extension_receipt_budget'}
$receipt=Get-Content -Raw -LiteralPath $draft|ConvertFrom-Json
if($receipt.accepted -isnot [bool] -or $receipt.accepted -or $receipt.manualAcceptance -isnot [bool] -or $receipt.manualAcceptance){throw 'extension_expected_unaccepted_draft'}
$receipt.accepted=$true;$receipt.manualAcceptance=$true
$directory=Assert-DeploymentPath $workspace (Join-Path $workspace ('out/verification/m8-5/manual/'+[Guid]::NewGuid().ToString('N')))
New-Item -ItemType Directory -Path $directory|Out-Null
$pending=Join-Path $directory 'pending.json';$final=Join-Path $directory 'accepted.json'
[IO.File]::WriteAllText($pending,($receipt|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
# Failed records remain pending and cannot be mistaken for an accepted filename.
$null=Assert-ExtensionTestReceipt $workspace $pending $source $index
if((Get-DeploymentHash $draft) -cne $ConfirmDraftSha256){throw 'extension_manual_review_stale'}
[IO.File]::Move($pending,$final)
Write-Output "Accepted trusted local receipt: $final. No deployment or restart performed."
} finally { $capture.Pins.Dispose() }
