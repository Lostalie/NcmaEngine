# Independent Python preparation inputs. No interpreter, pip, worker, or network execution.
. (Join-Path $PSScriptRoot 'OfflinePackageDependencies.ps1')

function Get-PythonWorkerWheels([byte[]]$LockBytes) {
 $text=[Text.UTF8Encoding]::new($false,$true).GetString($LockBytes)
 if($text.Length -gt 16384 -or $text.Contains([char]0)){throw 'python_lock_budget'}
 $found=@{}
 foreach($line in $text.Split([char]10)){
  $line=$line.Trim();if(-not $line -or $line.StartsWith('#')){continue}
  if($line -cnotmatch '^(grpcio|typing-extensions)==([0-9]+\.[0-9]+\.[0-9]+) --hash=sha256:([0-9a-f]{64})$'){throw 'python_lock_entry'}
  $name=$Matches[1];$version=$Matches[2];$hash=$Matches[3].ToUpperInvariant()
  if($found.ContainsKey($name)){throw 'python_lock_duplicate'}
  $file=if($name -ceq 'grpcio'){"grpcio-$version-cp312-cp312-win_amd64.whl"}else{"typing_extensions-$version-py3-none-any.whl"}
  $found.Add($name,[pscustomobject][ordered]@{name=$name;version=$version;file=$file;sha256=$hash})
 }
 if($found.Count -ne 2){throw 'python_lock_closure'}
 return @($found['grpcio'],$found['typing-extensions'])
}

function Assert-PythonWorkerExecutable([byte[]]$Bytes) {
 if($Bytes.Length -lt 256 -or $Bytes[0] -ne 77 -or $Bytes[1] -ne 90){throw 'python_interpreter_pe'}
 $offset=[BitConverter]::ToInt32($Bytes,60)
 if($offset -lt 64 -or $offset -gt $Bytes.Length-26 -or [BitConverter]::ToUInt32($Bytes,$offset) -ne 17744 -or
  [BitConverter]::ToUInt16($Bytes,$offset+4) -ne 34404 -or [BitConverter]::ToUInt16($Bytes,$offset+24) -ne 523){throw 'python_interpreter_x64'}
}

function Get-PythonWorkerRuntimeFiles([string]$Root,$Pins) {
 $Root=Get-OfflinePackagePath $Root;$Pins.Directory($Root)
 foreach($name in @('python.exe','pythonw.exe','python3.dll','python312.dll','vcruntime140.dll','vcruntime140_1.dll','LICENSE.txt')){
  if(-not(Test-Path -LiteralPath (Join-Path $Root $name) -PathType Leaf)){throw 'python_runtime_required_file'}
  $name
 }
 $pending=[Collections.Generic.Queue[string]]::new();$pending.Enqueue('Lib');$pending.Enqueue('DLLs');$count=7;$directories=0
 while($pending.Count){
  if(++$directories -gt 2048){throw 'python_runtime_directory_budget'}
  $relative=$pending.Dequeue();$path=Join-Path $Root $relative;$Pins.Directory($path)
  foreach($item in @(Get-ChildItem -LiteralPath $path -Force|Sort-Object Name)){
   # Installed third-party/user packages and bytecode are not runtime preparation sources.
   if($item.PSIsContainer -and $item.Name -cin @('site-packages','__pycache__')){continue}
   if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'python_runtime_reparse'}
   $child=$relative+'/'+$item.Name
   if($item.PSIsContainer){$pending.Enqueue($child)}elseif(-not $item.Name.EndsWith('.pyc',[StringComparison]::OrdinalIgnoreCase)){
    if(++$count -gt 8192){throw 'python_runtime_file_budget'}
    Assert-CaptureRelativePath $child;$child
   }
  }
 }
}

function Get-PythonWorkerRecipeNames {
 @('PythonWorkerInputs.ps1','Prepare-PythonWorkerInputs.ps1','PythonWorkerEnvironment.ps1','Prepare-PythonWorkerEnvironment.ps1','PythonWorkerEnvironment.py','OfflinePackageDependencies.ps1','CapturedBuild.ps1','AiCandidatePackage.ps1','EditorDeployment.ps1','EditorPackageDependencies.ps1')
}

function Get-PythonWorkerOrderedEntries($Entries) {
 $map=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
 foreach($entry in $Entries){if($map.ContainsKey($entry.path)){throw 'python_input_duplicate_path'};$map.Add($entry.path,$entry)}
 foreach($path in @(Get-AiCandidateOrderedPaths @($map.Keys))){$map[$path]}
}

function Assert-PythonWorkerInputLayout($Files,$Wheels) {
 $required=@('worker/ncma_module_worker.py','worker/tests/retained_work_probe.py','worker/tests/malformed_reply_probe.py','worker/requirements-win-py312.lock')
 $required+=@('python.exe','pythonw.exe','python3.dll','python312.dll','vcruntime140.dll','vcruntime140_1.dll','LICENSE.txt')|ForEach-Object{'runtime/'+$_}
 $required+=@('runtime/Lib/os.py','runtime/Lib/encodings/__init__.py','runtime/Lib/venv/__init__.py','runtime/Lib/venv/scripts/nt/python.exe','runtime/Lib/venv/scripts/nt/pythonw.exe')
 $required+=@(Get-PythonWorkerRecipeNames)|ForEach-Object{'recipe/'+$_}
 $required+=@($Wheels|ForEach-Object{'wheels/'+$_.file})
 foreach($path in $required){if(@($Files|Where-Object path -ceq $path).Count -ne 1){throw 'python_input_required_file'}}
 foreach($file in $Files){
  Assert-CaptureRelativePath $file.path
  if($file.path -cin $required){continue}
  if($file.path -cnotmatch '^runtime/(Lib|DLLs)/' -or $file.path -imatch '(^|/)(site-packages|__pycache__)(/|$)|\.pyc$'){throw 'python_input_unexpected_file'}
 }
 if(@($Files|Where-Object {$_.path -cmatch '^runtime/Lib/ensurepip/_bundled/pip-[0-9.]+-py3-none-any\.whl$'}).Count -ne 1){throw 'python_bundled_pip_closure'}
}

function New-PythonWorkerInputs([string]$Workspace,[string]$PythonExecutable,[string]$WheelDirectory) {
 $Workspace=[IO.Path]::GetFullPath($Workspace).TrimEnd('\')
 $python=Get-OfflinePackagePath $PythonExecutable;$wheels=Get-OfflinePackagePath $WheelDirectory
 if([IO.Path]::GetFileName($python) -cne 'python.exe' -or -not(Test-Path -LiteralPath $python -PathType Leaf) -or -not(Test-Path -LiteralPath $wheels -PathType Container)){throw 'python_input_prerequisite'}
 $pins=[NcmaCapturedBuildPins]::new();$root=$null
 try{
  $payload=[Collections.Generic.List[object]]::new();$total=0L
  # Retain all original bytes before creating output; missing wheels cannot leave a usable receipt.
  $sources=@('ncma_module_worker.py','tests/retained_work_probe.py','tests/malformed_reply_probe.py','requirements-win-py312.lock')
  foreach($name in $sources){
   $path=Join-Path $Workspace "python/worker/$name";[byte[]]$bytes=$pins.Read($path,1048576)
   $payload.Add([pscustomobject]@{path="worker/$name";bytes=$bytes});$total+=$bytes.Length
  }
  $lock=@($payload|Where-Object path -ceq 'worker/requirements-win-py312.lock')[0]
  $expected=@(Get-PythonWorkerWheels $lock.bytes)
  foreach($wheel in $expected){
   $path=Join-Path $wheels $wheel.file
   if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "python_offline_wheel_missing:$($wheel.file)"}
   [byte[]]$bytes=$pins.Read($path,67108864)
   if((Get-CaptureBytesHash $bytes) -cne $wheel.sha256){throw 'python_offline_wheel_hash'}
   $payload.Add([pscustomobject]@{path=('wheels/'+$wheel.file);bytes=$bytes});$total+=$bytes.Length
  }
  $runtime=Split-Path -Parent $python
  foreach($name in @(Get-PythonWorkerRuntimeFiles $runtime $pins)){
   [byte[]]$bytes=$pins.Read((Join-Path $runtime $name),67108864)
   $total+=$bytes.Length;if($total -gt 536870912){throw 'python_input_total_budget'}
   if($name -ceq 'python.exe'){Assert-PythonWorkerExecutable $bytes}
   $payload.Add([pscustomobject]@{path=('runtime/'+$name);bytes=$bytes})
  }
  $version=(Get-Item -LiteralPath $python).VersionInfo.FileVersion
  if($version -cnotmatch '^3\.12\.[0-9]+'){throw 'python_interpreter_version'}
  if(@($payload|Where-Object {$_.path -cmatch '^runtime/Lib/ensurepip/_bundled/pip-[0-9.]+-py3-none-any\.whl$'}).Count -ne 1){throw 'python_bundled_pip_closure'}
  foreach($name in @(Get-PythonWorkerRecipeNames)){
   [byte[]]$bytes=$pins.Read((Join-Path $PSScriptRoot $name),1048576)
   $payload.Add([pscustomobject]@{path=('recipe/'+$name);bytes=$bytes});$total+=$bytes.Length
  }
  if($payload.Count -gt 8192 -or $total -gt 536870912){throw 'python_input_total_budget'}
  Assert-PythonWorkerInputLayout $payload.ToArray() $expected
  $root=Assert-DeploymentPath $Workspace (Join-Path $Workspace ('out/verification/m8-6/python-inputs/'+[Guid]::NewGuid().ToString('N')))
  if(Test-Path -LiteralPath $root){throw 'python_input_collision'}
  $null=New-Item -ItemType Directory -Path $root
  $files=@(foreach($entry in @(Get-PythonWorkerOrderedEntries $payload.ToArray())){
   $path=Join-Path $root ('inputs/'+$entry.path);$null=New-Item -ItemType Directory -Force -Path (Split-Path $path)
   $stream=[IO.FileStream]::new($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
   try{$stream.Write($entry.bytes,0,$entry.bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
   [pscustomobject][ordered]@{path=$entry.path;size=$entry.bytes.Length;sha256=(Get-CaptureBytesHash $entry.bytes)}
  })
  $receipt=[ordered]@{kind='ncma.python-worker.inputs.v1';root=$root;originInterpreter=$python;observedFileVersion=$version;originWheelDirectory=$wheels;
   sourceRole='copied-inputs';files=$files;inputsSha256=(Get-AiCandidateInputHash $files);wheels=$expected;networkAuthorized=$false;interpreterExecuted=$false;environmentCreated=$false;testsExecuted=$false;installed=$false;accepted=$false}
  Write-OfflinePackageJson (Join-Path $root 'receipt.json') $receipt
  return [pscustomobject]@{Root=$root;ReceiptSha256=(Get-DeploymentHash (Join-Path $root 'receipt.json'));InputsSha256=$receipt.inputsSha256;Files=$files.Count;Bytes=$total;EnvironmentCreated=$false}
 }catch{
  if($root -and (Test-Path -LiteralPath $root)){Write-OfflinePackageJson (Join-Path $root 'failure.json') @{failureType=$_.Exception.GetType().Name;accepted=$false;environmentCreated=$false}}
  throw
 }finally{$pins.Dispose()}
}

function Open-PythonWorkerInputs([string]$Workspace,[string]$Root,[string]$ExpectedReceiptSha256) {
 $rootPath=Get-OfflinePackagePath $Root
 $prefix=[IO.Path]::GetFullPath((Join-Path $Workspace 'out/verification/m8-6/python-inputs')).TrimEnd('\')+'\'
 if(-not $rootPath.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or $rootPath.Substring($prefix.Length) -cnotmatch '^[a-f0-9]{32}$' -or $ExpectedReceiptSha256 -cnotmatch '^[0-9A-F]{64}$'){throw 'python_input_identity'}
 $pins=[NcmaCapturedBuildPins]::new()
 try{
  [byte[]]$bytes=$pins.Read((Join-Path $rootPath 'receipt.json'),4194304)
  if((Get-CaptureBytesHash $bytes) -cne $ExpectedReceiptSha256){throw 'python_input_receipt_changed'}
  $value=[Text.UTF8Encoding]::new($false,$true).GetString($bytes)|ConvertFrom-Json
  if($value.kind -cne 'ncma.python-worker.inputs.v1' -or $value.root -cne $rootPath -or $value.sourceRole -cne 'copied-inputs' -or
   $value.networkAuthorized -isnot [bool] -or $value.networkAuthorized -or $value.environmentCreated -isnot [bool] -or $value.environmentCreated){throw 'python_input_receipt_binding'}
  $inputRoot=Join-Path $rootPath 'inputs';$actual=@(Get-PythonWorkerOrderedEntries @(Get-DeploymentFiles $Workspace $inputRoot));$listed=@($value.files)
  if($actual.Count -lt 24 -or $actual.Count -ne $listed.Count -or (Get-AiCandidateInputHash $actual) -cne $value.inputsSha256){throw 'python_input_set_changed'}
  $total=0L;$lockBytes=$null
  for($i=0;$i -lt $actual.Count;$i++){
   $a=$actual[$i];$b=$listed[$i];$total+=$a.size
   if($total -gt 536870912 -or $a.path -cne $b.path -or $a.size -ne $b.size -or $a.sha256 -cne $b.sha256){throw 'python_input_bytes_changed'}
   [byte[]]$held=$pins.Read((Join-Path $inputRoot $a.path),67108864)
   if((Get-CaptureBytesHash $held) -cne $a.sha256){throw 'python_input_bytes_changed'}
   if($a.path -ceq 'worker/requirements-win-py312.lock'){$lockBytes=$held}
   if($a.path -ceq 'runtime/python.exe'){Assert-PythonWorkerExecutable $held}
  }
  if($null -eq $lockBytes){throw 'python_lock_closure'}
  $wheels=@(Get-PythonWorkerWheels $lockBytes)
  Assert-PythonWorkerInputLayout $actual $wheels
  $version=(Get-Item -LiteralPath (Join-Path $inputRoot 'runtime/python.exe')).VersionInfo.FileVersion
  if($version -cnotmatch '^3\.12\.[0-9]+' -or $version -cne $value.observedFileVersion){throw 'python_interpreter_version'}
  if(($wheels|ConvertTo-Json -Compress) -cne ($value.wheels|ConvertTo-Json -Compress)){throw 'python_input_wheel_binding'}
  foreach($wheel in $wheels){$entry=@($actual|Where-Object path -ceq ('wheels/'+$wheel.file));if($entry.Count -ne 1 -or $entry[0].sha256 -cne $wheel.sha256){throw 'python_offline_wheel_hash'}}
  foreach($name in @(Get-PythonWorkerRecipeNames)){
   $entry=@($actual|Where-Object path -ceq ('recipe/'+$name))
   if($entry.Count -ne 1 -or (Get-DeploymentHash (Join-Path $PSScriptRoot $name)) -cne $entry[0].sha256){throw 'python_input_recipe_changed'}
  }
  return [pscustomobject]@{Root=$rootPath;Inputs=$inputRoot;Pins=$pins;ReceiptSha256=$ExpectedReceiptSha256;InputsSha256=$value.inputsSha256;Receipt=$value}
 }catch{$pins.Dispose();throw}
}
