# Fixed original capture creation only: no build/restore/test/install or source apply.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$PipeName)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
if($PipeName -cnotmatch '^ncma-workflow-source-[a-f0-9]{32}$'){throw 'workflow_pipe_name'}
. (Join-Path $PSScriptRoot 'CapturedBuild.ps1')
$pipe=[IO.Pipes.NamedPipeClientStream]::new('.', $PipeName, [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::None)
$reader=$null;$writer=$null;$capture=$null;$nonce=$null
function Read-SourceFrame {
 $length=$reader.ReadInt32();if($length -lt 2 -or $length -gt 16384){throw 'workflow_request_budget'}
 [byte[]]$bytes=$reader.ReadBytes($length);if($bytes.Length -ne $length){throw 'workflow_request_eof'}
 return ([Text.UTF8Encoding]::new($false,$true).GetString($bytes)|ConvertFrom-Json)
}
function Write-SourceFrame($value) {
 [byte[]]$bytes=[Text.UTF8Encoding]::new($false,$true).GetBytes(($value|ConvertTo-Json -Depth 5 -Compress))
 if($bytes.Length -gt 2097152){throw 'workflow_response_budget'}
 $writer.Write([int]$bytes.Length);$writer.Write($bytes);$writer.Flush()
}
try {
 $pipe.Connect(30000);$reader=[IO.BinaryReader]::new($pipe,[Text.Encoding]::UTF8,$true);$writer=[IO.BinaryWriter]::new($pipe,[Text.Encoding]::UTF8,$true)
 $r=Read-SourceFrame
 $keys=@($r.PSObject.Properties.Name|Sort-Object)
 if(($keys -join '|') -cne 'bundle|bundleSha256|git|kind|nonce|version|workspace' -or $r.kind -cne 'capture-reviewed' -or $r.version -ne 1 -or $r.nonce -cnotmatch '^[a-f0-9]{32}$'){throw 'workflow_source_request'}
 $nonce=$r.nonce
 $workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
 if(-not [string]::Equals($workspace,[IO.Path]::GetFullPath($r.workspace).TrimEnd('\'),[StringComparison]::OrdinalIgnoreCase)){throw 'workflow_workspace_mismatch'}
 if($r.bundleSha256 -cnotmatch '^[0-9A-F]{64}$' -or -not [IO.Path]::IsPathRooted($r.git) -or [IO.Path]::GetFileName($r.git) -cne 'git.exe'){throw 'workflow_source_tool'}
 # Explicit parent-pinned executable; no ambient PATH discovery or git writes.
 # Original capture helpers invoke this lexical function for read-only inventory.
 $script:workflowGit=[string]$r.git
 function git { & $script:workflowGit @args; $global:LASTEXITCODE=$LASTEXITCODE }
 $root=New-CapturedBuild $workspace ([string]$r.bundle) ([string]$r.bundleSha256)
 $capture=Open-CapturedBuild $workspace $root
 if($capture.Manifest.reviewBundleSha256 -cne $r.bundleSha256){throw 'workflow_capture_review_mismatch'}
 $facts=[ordered]@{kind='ncma.workflow.source-created.v1';captureId=(Split-Path -Leaf $root);workspace=$workspace;
  sourceInputsSha256=$capture.Manifest.sourceInputsSha256;manifestSha256=$capture.ManifestSha256;
  recipeSha256=$capture.Manifest.recipeSha256;reviewBundleSha256=$capture.Manifest.reviewBundleSha256;
  sourceFileCount=@($capture.Manifest.files).Count;buildAuthorized=$false;installationAuthorized=$false}
 Write-SourceFrame ([ordered]@{kind='captured';nonce=$nonce;facts=($facts|ConvertTo-Json -Compress);handles=@($capture.Pins.BorrowHandles())})
 $close=Read-SourceFrame
 if($close.kind -cne 'close' -or $close.nonce -cne $nonce){throw 'workflow_close_shape'}
 Write-SourceFrame ([ordered]@{kind='closing';nonce=$nonce})
}catch{
 if($null -ne $writer){try{Write-SourceFrame ([ordered]@{kind='failed';nonce=$nonce;exceptionType=$_.Exception.GetType().Name})}catch{}}
 exit 1
}finally{
 if($null -ne $capture){$capture.Pins.Dispose()}
 if($null -ne $reader){$reader.Dispose()};if($null -ne $writer){$writer.Dispose()};$pipe.Dispose()
}
