# Trusted local C1 adapter. Import/open does not build, test, deploy, or grant execution.
# Keep the returned Capture.Pins alive until the consuming job has fully drained.
# A plain preparation object/hash is NOT a human approval or an execution token.
. (Join-Path $PSScriptRoot 'CapturedBuild.ps1')

function Open-WorkflowBuildInputs {
 [CmdletBinding()]
 param(
  [Parameter(Mandatory=$true)][string]$Workspace,
  [Parameter(Mandatory=$true)][Guid]$CaptureId,
  [Parameter(Mandatory=$true)][Guid]$JobId,
  [Parameter(Mandatory=$true)][string]$ExpectedManifestSha256,
  [Parameter(Mandatory=$true)][string]$ExpectedSourceInputsSha256,
  [Parameter(Mandatory=$true)][string]$ExpectedCaptureRecipeSha256,
  [AllowNull()][string]$ExpectedReviewBundleSha256,
  [string]$RecipeId='editor-services-build-v1',
  [string]$Configuration='Debug',
  [string]$Variant='default')
 $ErrorActionPreference='Stop'
 # The workspace is the repository containing THIS trusted adapter. Never select
 # a second tree, arbitrary capture path, project, command line, or output path.
 $workspaceRoot=[IO.Path]::GetFullPath($Workspace).TrimEnd('\')
 $adapterRoot=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
 if(-not [string]::Equals($workspaceRoot,$adapterRoot,[StringComparison]::OrdinalIgnoreCase)) {throw 'workflow_workspace_mismatch'}
 if($CaptureId -eq [Guid]::Empty -or $JobId -eq [Guid]::Empty){throw 'workflow_job_identity'}
 if($RecipeId -cnotin @('editor-services-build-v1','editor-services-restore-offline-v1','workflow-contracts-v1','workflow-host-v1','workflow-native-prepare-v1')){throw 'workflow_build_recipe_required'}
 if($Configuration -cnotin @('Debug','Release') -or $Variant -cnotin @('default','ai','extensions','ai-extensions')){throw 'workflow_job_configuration'}
 foreach($hash in @($ExpectedManifestSha256,$ExpectedSourceInputsSha256,$ExpectedCaptureRecipeSha256)){
  if($hash -cnotmatch '^[0-9A-F]{64}$'){throw 'workflow_capture_hash'}
 }
 if($ExpectedReviewBundleSha256 -and $ExpectedReviewBundleSha256 -cnotmatch '^[0-9A-F]{64}$'){throw 'workflow_capture_hash'}
 $root=Assert-DeploymentPath $workspaceRoot (Join-Path $workspaceRoot ('out/captured-build/'+$CaptureId.ToString('N')))
 $outputKind=if($RecipeId -ceq 'editor-services-restore-offline-v1'){'restores'}else{'jobs'}
 $outputRoot=Assert-DeploymentPath $workspaceRoot (Join-Path $workspaceRoot ('out/verification/m8-6/'+$outputKind+'/'+$JobId.ToString('N')))
 if(Test-Path -LiteralPath $outputRoot){throw 'workflow_job_output_exists'}
 $capture=$null
 try {
  # Reuse the original physical identity/content/ancestor locks and complete
  # manifest/input/recipe verification. No alternative stamp algorithm.
  $capture=Open-CapturedBuild $workspaceRoot $root
  if($capture.ManifestSha256 -cne $ExpectedManifestSha256 -or
     $capture.Manifest.sourceInputsSha256 -cne $ExpectedSourceInputsSha256 -or
     $capture.Manifest.recipeSha256 -cne $ExpectedCaptureRecipeSha256 -or
     [string]$capture.Manifest.reviewBundleSha256 -cne [string]$ExpectedReviewBundleSha256){throw 'workflow_capture_binding_changed'}
  $project='managed/Ncma.Editor.Services.Tests/Ncma.Editor.Services.Tests.csproj'
  $targets='managed/Directory.Build.targets'
  foreach($required in @($project,$targets)) {
   if(@($capture.Manifest.files|Where-Object{$_.path -ceq $required}).Count -ne 1){throw 'workflow_capture_required_input'}
  }
  # Protect the nearest existing output ancestor; an absent leaf is NOT reserved.
  # C2 must still recheck and exclusively create its output after human approval.
  $parent=[IO.DirectoryInfo]::new((Split-Path -Parent $outputRoot))
  while($null -ne $parent -and -not $parent.Exists){$parent=$parent.Parent}
  if($null -eq $parent){throw 'workflow_output_parent_missing'}
  $capture.Pins.Directory($parent.FullName)
  $null=Assert-DeploymentPath $workspaceRoot $outputRoot
  if(Test-Path -LiteralPath $outputRoot){throw 'workflow_job_output_exists'}
  $facts=[ordered]@{
   kind='ncma.workflow.capture-preparation.v1';schemaVersion=1;workspace=$workspaceRoot;
   captureId=$CaptureId.ToString('D');jobId=$JobId.ToString('D');recipeId=$RecipeId;
   configuration=$Configuration;variant=$Variant;captureRoot=$capture.Root;sourceRoot=$capture.Source;
   project=$project;outputRoot=$outputRoot;sourceInputsSha256=$capture.Manifest.sourceInputsSha256;
   captureManifestSha256=$capture.ManifestSha256;captureRecipeSha256=$capture.Manifest.recipeSha256;
   reviewBundleSha256=$capture.Manifest.reviewBundleSha256;sourceFileCount=@($capture.Manifest.files).Count;
   processStarted=$false;launchAuthorized=$false;outputReserved=$false
  }
  # Facts are a detached JSON string. Capture remains an in-process trusted lease,
  # not something to deserialize from an Agent or accept after the pins are closed.
  return [pscustomobject]@{Capture=$capture;FactsJson=($facts|ConvertTo-Json -Depth 4 -Compress)}
 }catch{if($null -ne $capture){$capture.Pins.Dispose()};throw}
}
