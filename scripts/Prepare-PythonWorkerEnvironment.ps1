[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$InputsRoot,[Parameter(Mandatory=$true)][string]$ExpectedInputsReceiptSha256)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'PythonWorkerEnvironment.ps1')
$workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
# Explicit independent local dependency preparation, never normal product installation.
New-PythonWorkerEnvironment $workspace $InputsRoot $ExpectedInputsReceiptSha256
