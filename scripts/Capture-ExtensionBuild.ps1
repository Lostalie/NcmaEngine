[CmdletBinding()]
param([string]$ReviewBundle,[string]$ExpectedBundleSha256)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'CapturedBuild.ps1')
$workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$root=New-CapturedBuild $workspace $ReviewBundle $ExpectedBundleSha256
Write-Output "Captured input: $root. Build from this copy; no compilation, approval or installation performed."
