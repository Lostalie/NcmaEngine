# Fixed preparation worker: no build, test, approval, source write or deployment.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$PipeName)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
if($PipeName -cnotmatch '^ncma-workflow-capture-[a-f0-9]{32}$'){throw 'workflow_pipe_name'}
. (Join-Path $PSScriptRoot 'WorkflowBuildInputs.ps1')
$pipe=[IO.Pipes.NamedPipeClientStream]::new('.', $PipeName, [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::None)
$reader=$null;$writer=$null;$prepared=$null;$nonce=$null
function Read-WorkflowFrame {
 $length=$reader.ReadInt32()
 if($length -lt 2 -or $length -gt 16384){throw 'workflow_request_budget'}
 [byte[]]$bytes=$reader.ReadBytes($length)
 if($bytes.Length -ne $length){throw 'workflow_request_eof'}
 return ([Text.UTF8Encoding]::new($false,$true).GetString($bytes)|ConvertFrom-Json)
}
function Write-WorkflowFrame($value) {
 [byte[]]$bytes=[Text.UTF8Encoding]::new($false,$true).GetBytes(($value|ConvertTo-Json -Depth 5 -Compress))
 if($bytes.Length -gt 2097152){throw 'workflow_response_budget'}
 $writer.Write([int]$bytes.Length);$writer.Write($bytes);$writer.Flush()
}
try {
 $pipe.Connect(30000)
 $reader=[IO.BinaryReader]::new($pipe,[Text.Encoding]::UTF8,$true)
 $writer=[IO.BinaryWriter]::new($pipe,[Text.Encoding]::UTF8,$true)
 $r=Read-WorkflowFrame
 if($r.version -ne 1 -or $r.kind -cne 'prepare' -or $r.nonce -cnotmatch '^[a-f0-9]{32}$'){throw 'workflow_request_shape'}
 $nonce=$r.nonce
 $prepared=Open-WorkflowBuildInputs -Workspace $r.workspace -CaptureId ([Guid]$r.captureId) -JobId ([Guid]$r.jobId) `
  -ExpectedManifestSha256 $r.manifestSha256 -ExpectedSourceInputsSha256 $r.sourceInputsSha256 `
  -ExpectedCaptureRecipeSha256 $r.recipeSha256 -ExpectedReviewBundleSha256 $r.reviewBundleSha256 `
  -RecipeId $r.recipeId -Configuration $r.configuration -Variant $r.variant
 Write-WorkflowFrame ([ordered]@{kind='prepared';nonce=$nonce;facts=$prepared.FactsJson;handles=@($prepared.Capture.Pins.BorrowHandles())})
 # Only close is accepted. This preparer cannot execute a build command.
 $close=Read-WorkflowFrame
 if($close.kind -cne 'close' -or $close.nonce -cne $nonce){throw 'workflow_close_shape'}
 Write-WorkflowFrame ([ordered]@{kind='closing';nonce=$nonce})
}catch{
 if($null -ne $writer){try{Write-WorkflowFrame ([ordered]@{kind='failed';nonce=$nonce;exceptionType=$_.Exception.GetType().Name})}catch{}}
 exit 1
}finally{
 if($null -ne $prepared){$prepared.Capture.Pins.Dispose()}
 if($null -ne $reader){$reader.Dispose()};if($null -ne $writer){$writer.Dispose()};$pipe.Dispose()
}
