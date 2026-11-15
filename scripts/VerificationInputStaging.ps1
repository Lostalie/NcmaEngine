# Original fixed CTest roots are retained. Never overwrite or merge a different
# generation here; replacing a prior input set is a separate checked operation.
. (Join-Path $PSScriptRoot 'AiVerificationInputs.ps1')
. (Join-Path $PSScriptRoot 'OfflinePackageDependencies.ps1')

function Get-VerificationInputTarget([string]$Workspace,[string]$Configuration,[string]$Id) {
 if($Configuration -cnotin @('Debug','Release')){throw 'verification_input_configuration'}
 $root=Get-OfflinePackagePath $Workspace
 if($Id -ceq 'verify-ai/Ncma.Editor.Services.Tests'){return (Get-AiVerificationRoots $root $Configuration).optional}
 if($Id -ceq 'verify-contracts/Ncma.AI.Tests'){return (Get-AiVerificationRoots $root $Configuration).contracts}
 $jobs=@(Get-OfflineVerificationJobs|Where-Object id -ceq $Id)
 if($jobs.Count -ne 1 -or $jobs[0].variant -cnotin @('verify-extensions','verify-ai-extensions')){throw 'verification_input_job'}
 $variant=$jobs[0].variant.Substring(7);$name=[IO.Path]::GetFileNameWithoutExtension($jobs[0].project)
 return [IO.Path]::GetFullPath((Join-Path $root "out/build/windows-ninja-$($Configuration.ToLowerInvariant())/m8-5-$variant/bin/$name/$($Configuration.ToLowerInvariant())"))
}

function Copy-VerificationInputTree([string]$Workspace,[string]$Configuration,$Preparation,[string]$Id) {
 $jobs=@($Preparation.Jobs|Where-Object id -ceq $Id)
 if($jobs.Count -ne 1){throw 'verification_input_job'}
 $job=$jobs[0];$name=[IO.Path]::GetFileNameWithoutExtension($job.project)
 $source=Get-OfflinePackagePath (Join-Path $Preparation.Root "artifacts/$($job.variant)/bin/$name/$($Configuration.ToLowerInvariant())")
 $destination=Get-OfflinePackagePath (Get-VerificationInputTarget $Workspace $Configuration $Id)
 $files=@(Get-AiVerificationFiles $source)
 foreach($suffix in @('.dll','.deps.json','.runtimeconfig.json')){if(@($files|Where-Object path -ceq ($name+$suffix)).Count -ne 1){throw 'verification_input_entry_missing'}}
 if(Test-Path -LiteralPath $destination){
  $existing=@(Get-AiVerificationFiles $destination)
  if(@(Compare-Object @($files|ForEach-Object{$_.path+'|'+$_.size+'|'+$_.sha256}) @($existing|ForEach-Object{$_.path+'|'+$_.size+'|'+$_.sha256})).Count){throw 'verification_input_existing_generation_requires_preserved_activation'}
  return $destination
 }
 $pins=[NcmaCapturedBuildPins]::new()
 try{
  # Bound source closure before any destination write. Source bytes stay retained
  # during copy. This is staging, not an installation or runtime acceptance.
  $total=0L
  foreach($file in $files){
   $total+=$file.size;if($total -gt 268435456){throw 'verification_input_byte_budget'}
   if((Get-CaptureBytesHash $pins.Read((Join-Path $source $file.path),67108864)) -cne $file.sha256){throw 'verification_input_source_changed'}
  }
  $null=New-Item -ItemType Directory -Path $destination
  foreach($file in $files){
   $target=Join-Path $destination $file.path;$parent=Split-Path -Parent $target
   if(-not(Test-Path -LiteralPath $parent)){$null=New-Item -ItemType Directory -Path $parent}
   $pins.Directory($parent)
   [IO.File]::Copy((Join-Path $source $file.path),$target,$false)
  }
  $actual=@(Get-AiVerificationFiles $destination)
  if(@(Compare-Object @($files|ForEach-Object{$_.path+'|'+$_.size+'|'+$_.sha256}) @($actual|ForEach-Object{$_.path+'|'+$_.size+'|'+$_.sha256})).Count){throw 'verification_input_copy_mismatch'}
  return $destination
 }finally{$pins.Dispose()}
}
