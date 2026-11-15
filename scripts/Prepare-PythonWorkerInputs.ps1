[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$PythonExecutable,[Parameter(Mandatory=$true)][string]$OfflineWheelDirectory)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'PythonWorkerInputs.ps1')
$workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
# Input preparation only. A receipt is not an environment, install permission or test acceptance.
New-PythonWorkerInputs $workspace $PythonExecutable $OfflineWheelDirectory
