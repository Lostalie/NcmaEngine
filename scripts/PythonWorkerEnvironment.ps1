# Trusted local OFFLINE preparation only. Not the AI SDK worker or a process sandbox.
. (Join-Path $PSScriptRoot 'PythonWorkerInputs.ps1')

function Get-PythonWorkerEnvironmentRoot([string]$Workspace,[string]$Root) {
 $path=Get-OfflinePackagePath $Root
 $prefix=[IO.Path]::GetFullPath((Join-Path $Workspace 'out/verification/m8-6/python-environments')).TrimEnd('\')+'\'
 if(-not $path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or $path.Substring($prefix.Length) -cnotmatch '^[a-f0-9]{32}$'){throw 'python_environment_scope'}
 return $path
}

function Get-PythonWorkerEnvironmentInvocation([string]$Workspace,[string]$Inputs,[string]$Root,[string]$Step) {
 $rootPath=Get-PythonWorkerEnvironmentRoot $Workspace $Root
 $inputPath=Get-OfflinePackagePath $Inputs
 $prefix=[IO.Path]::GetFullPath((Join-Path $Workspace 'out/verification/m8-6/python-inputs')).TrimEnd('\')+'\'
 if(-not $inputPath.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or $inputPath.Substring($prefix.Length) -cnotmatch '^[a-f0-9]{32}\\inputs$'){throw 'python_environment_input_scope'}
 if($Step -cnotin @('create','install')){throw 'python_environment_step'}
 $exe=if($Step -ceq 'create'){Join-Path $inputPath 'runtime/python.exe'}else{Join-Path $rootPath 'venv/Scripts/python.exe'}
 # Returns a fixed plan, NOT a validated input lease or execution permission.
 return [pscustomobject]@{Executable=$exe;Arguments=@('-I','-B',(Join-Path $inputPath 'recipe/PythonWorkerEnvironment.py'),$Step,$inputPath,$rootPath)}
}

function Assert-PythonWorkerEnvironmentObservation($Value,[string]$Inputs,[string]$Root,$Wheels) {
 if($Value.kind -cne 'ncma.python-worker.environment-observation.v1' -or $Value.inputs -cne $Inputs -or
  $Value.environment -cne (Join-Path $Root 'venv') -or $Value.baseRuntime -cne (Join-Path $Inputs 'runtime')){throw 'python_environment_observation_binding'}
 foreach($name in @('isolated','bytecodeDisabled','pipConfigurationDisabled')){
  if($Value.$name -isnot [bool] -or -not $Value.$name){throw 'python_environment_isolation'}
 }
 foreach($name in @('pipInstalled','workerExecuted','testsExecuted')){
  if($Value.$name -isnot [bool] -or $Value.$name){throw 'python_environment_execution_claim'}
 }
 if(@($Value.installed.PSObject.Properties).Count -ne 2){throw 'python_environment_package_closure'}
 foreach($wheel in $Wheels){
  $property=$Value.installed.PSObject.Properties[$wheel.name]
  if($null -eq $property -or $property.Value -cne $wheel.version){throw 'python_environment_package_closure'}
 }
}

function Get-PythonWorkerEnvironmentFiles([string]$Workspace,[string]$Root) {
 $files=@(Get-PythonWorkerOrderedEntries @(Get-DeploymentFiles $Workspace $Root|Where-Object path -cne 'receipt.json'))
 $bytes=0L
 foreach($file in $files){
  $bytes+=$file.size
  if($file.size -gt 67108864 -or $bytes -gt 268435456){throw 'python_environment_budget'}
  if($file.path -imatch '(^|/)__pycache__(/|$)|\.pyc$' -or
   ($file.path -cnotmatch '^venv/' -and $file.path -cnotin @('create.log','install.log','pip-report.json','observation.json'))){throw 'python_environment_unexpected_file'}
 }
 foreach($path in @('venv/pyvenv.cfg','venv/Scripts/python.exe','venv/Scripts/pythonw.exe','create.log','install.log','pip-report.json','observation.json')){
  if(@($files|Where-Object path -ceq $path).Count -ne 1){throw 'python_environment_required_file'}
 }
 return $files
}

function New-PythonWorkerEnvironment([string]$Workspace,[string]$InputsRoot,[string]$ExpectedInputsReceiptSha256) {
 # All dependencies/recipe/file identities are checked BEFORE output or Python execution.
 $input=Open-PythonWorkerInputs $Workspace $InputsRoot $ExpectedInputsReceiptSha256
 $root=$null;$created=$false;$check=$null;$savedTemp=$env:TEMP;$savedTmp=$env:TMP
 try{
  $root=Get-PythonWorkerEnvironmentRoot $Workspace (Join-Path $Workspace ('out/verification/m8-6/python-environments/'+[Guid]::NewGuid().ToString('N')))
  if(Test-Path -LiteralPath $root){throw 'python_environment_collision'}
  $null=New-Item -ItemType Directory -Path $root
  $created=$true
  $temp=Join-Path $root 'tmp';$null=New-Item -ItemType Directory -Path $temp
  $env:TEMP=$temp;$env:TMP=$temp
  foreach($step in @('create','install')){
   $plan=Get-PythonWorkerEnvironmentInvocation $Workspace $input.Inputs $root $step
   $exe=$plan.Executable;$arguments=$plan.Arguments
   # Synchronous trusted preparation; retain input handles until child exits.
   # No timeout kill, alternate host, policy bypass, worker/model or test invocation.
   & $exe @arguments *> (Join-Path $root ($step+'.log'))
   if($LASTEXITCODE -ne 0){throw ('python_environment_'+$step+'_failed')}
  }
  $observationPath=Join-Path $root 'observation.json'
  if((Get-Item -LiteralPath $observationPath).Length -gt 16384){throw 'python_environment_observation_budget'}
  $observed=Get-Content -Raw -LiteralPath $observationPath|ConvertFrom-Json
  Assert-PythonWorkerEnvironmentObservation $observed $input.Inputs $root $input.Receipt.wheels
  $files=@(Get-PythonWorkerEnvironmentFiles $Workspace $root)
  # Recheck complete input set after preparation; held handles alone do not deny file additions.
  $check=Open-PythonWorkerInputs $Workspace $input.Root $input.ReceiptSha256
  $receipt=[ordered]@{kind='ncma.python-worker.environment.v1';root=$root;inputsRoot=$input.Root;inputsReceiptSha256=$input.ReceiptSha256;
   inputsSha256=$input.InputsSha256;sourceRole='copied-inputs';environment=(Join-Path $root 'venv');worker=(Join-Path $input.Inputs 'worker');
   files=$files;filesSha256=(Get-AiCandidateInputHash $files);networkAuthorized=$false;workerExecuted=$false;testsExecuted=$false;
   canonicalEnvironmentReplaced=$false;installed=$false;accepted=$false}
  Write-OfflinePackageJson (Join-Path $root 'receipt.json') $receipt
  return [pscustomobject]@{Root=$root;ReceiptSha256=(Get-DeploymentHash (Join-Path $root 'receipt.json'));InputsRoot=$input.Root;
   InputsReceiptSha256=$input.ReceiptSha256;Environment=$receipt.environment;Worker=$receipt.worker;TestsExecuted=$false}
 }catch{
  if($created){Write-OfflinePackageJson (Join-Path $root 'failure.json') @{exceptionType=$_.Exception.GetType().Name;accepted=$false;testsExecuted=$false}}
  throw
 }finally{
  $env:TEMP=$savedTemp;$env:TMP=$savedTmp
  if($null -ne $check){$check.Pins.Dispose()};$input.Pins.Dispose()
 }
}

function Open-PythonWorkerEnvironment([string]$Workspace,[string]$Root,[string]$ExpectedReceiptSha256) {
 $rootPath=Get-PythonWorkerEnvironmentRoot $Workspace $Root
 if($ExpectedReceiptSha256 -cnotmatch '^[0-9A-F]{64}$'){throw 'python_environment_receipt_hash'}
 $pins=[NcmaCapturedBuildPins]::new();$input=$null
 try{
  [byte[]]$bytes=$pins.Read((Join-Path $rootPath 'receipt.json'),4194304)
  if((Get-CaptureBytesHash $bytes) -cne $ExpectedReceiptSha256){throw 'python_environment_receipt_changed'}
  $receipt=[Text.UTF8Encoding]::new($false,$true).GetString($bytes)|ConvertFrom-Json
  if($receipt.kind -cne 'ncma.python-worker.environment.v1' -or $receipt.root -cne $rootPath -or $receipt.sourceRole -cne 'copied-inputs' -or
   $receipt.environment -cne (Join-Path $rootPath 'venv')){throw 'python_environment_receipt_binding'}
  foreach($name in @('networkAuthorized','workerExecuted','testsExecuted','canonicalEnvironmentReplaced','installed','accepted')){
   if($receipt.$name -isnot [bool] -or $receipt.$name){throw 'python_environment_receipt_claim'}
  }
  $input=Open-PythonWorkerInputs $Workspace $receipt.inputsRoot $receipt.inputsReceiptSha256
  if($input.InputsSha256 -cne $receipt.inputsSha256 -or $receipt.worker -cne (Join-Path $input.Inputs 'worker')){throw 'python_environment_input_binding'}
  $actual=@(Get-PythonWorkerEnvironmentFiles $Workspace $rootPath);$listed=@($receipt.files)
  if($actual.Count -ne $listed.Count -or (Get-AiCandidateInputHash $actual) -cne $receipt.filesSha256){throw 'python_environment_set_changed'}
  for($i=0;$i -lt $actual.Count;$i++){
   $a=$actual[$i];$b=$listed[$i]
   if($a.path -cne $b.path -or $a.size -ne $b.size -or $a.sha256 -cne $b.sha256){throw 'python_environment_content_changed'}
   [byte[]]$held=$pins.Read((Join-Path $rootPath $a.path),67108864)
   if((Get-CaptureBytesHash $held) -cne $a.sha256){throw 'python_environment_content_changed'}
   if($a.path -ceq 'observation.json'){
    if($held.Length -gt 16384){throw 'python_environment_observation_budget'}
    $observed=[Text.UTF8Encoding]::new($false,$true).GetString($held)|ConvertFrom-Json
    Assert-PythonWorkerEnvironmentObservation $observed $input.Inputs $rootPath $input.Receipt.wheels
   }
  }
  # Caller must retain BOTH owners through consumption, then dispose both explicitly.
  return [pscustomobject]@{Root=$rootPath;Pins=$pins;InputPins=$input.Pins;Inputs=$input.Inputs;Environment=$receipt.environment;Worker=$receipt.worker;
   ReceiptSha256=$ExpectedReceiptSha256;InputsSha256=$input.InputsSha256;Receipt=$receipt}
 }catch{if($null -ne $input){$input.Pins.Dispose()};$pins.Dispose();throw}
}
