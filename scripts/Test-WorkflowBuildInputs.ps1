[CmdletBinding()]param()
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'WorkflowBuildInputs.ps1')
. (Join-Path $PSScriptRoot 'PackageVerificationContext.ps1')
. (Join-Path $PSScriptRoot 'OfflinePackageDependencies.ps1')
. (Join-Path $PSScriptRoot 'VerificationInputStaging.ps1')
. (Join-Path $PSScriptRoot 'PythonWorkerInputs.ps1')
. (Join-Path $PSScriptRoot 'PythonWorkerEnvironment.ps1')
$workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$evidence=Assert-DeploymentPath $workspace (Join-Path $workspace ('out/verification/m8-6/capture-tests/'+[Guid]::NewGuid().ToString('N')))
New-Item -ItemType Directory -Path $evidence|Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
function Fixture([switch]$MissingProject) {
 $id=[Guid]::NewGuid();$root=Assert-DeploymentPath $workspace (Join-Path $workspace ('out/captured-build/'+$id.ToString('N')))
 if(Test-Path -LiteralPath $root){throw 'Fixture collision'}
 $dir=Join-Path $root 'source/managed/Ncma.Editor.Services.Tests';New-Item -ItemType Directory -Path $dir|Out-Null
 $files=@('managed/Directory.Build.targets','managed/Ncma.Editor.Services.Tests/Probe.cs')
 if(-not $MissingProject){$files+= 'managed/Ncma.Editor.Services.Tests/Ncma.Editor.Services.Tests.csproj'}
 $rows=@(foreach($file in $files){$text=if($file.EndsWith('.cs')){'public class Probe {}'}else{'<Project />'};$bytes=$utf8.GetBytes($text);[IO.File]::WriteAllBytes((Join-Path "$root/source" $file),$bytes);[pscustomobject]@{path=$file;size=$bytes.Length;sha256=(Get-CaptureBytesHash $bytes)}})
 $props=$utf8.GetBytes('<Project />');[IO.File]::WriteAllBytes("$root/capture.props",$props)
 $manifest=@{kind='ncma.captured-build.v1';schemaVersion=1;files=$rows;sourceInputsSha256=(Get-AiCandidateInputHash $rows);recipeSha256=(Get-CaptureBytesHash $props);reviewBundleSha256=$null}
 [IO.File]::WriteAllText("$root/capture.json",($manifest|ConvertTo-Json -Depth 6),$utf8)
 [IO.File]::WriteAllText((Join-Path $evidence ($id.ToString('N')+'.json')),(@{captureRoot=$root;manifestSha256=(Get-DeploymentHash "$root/capture.json");missingProject=[bool]$MissingProject}|ConvertTo-Json),$utf8)
 return @{Workspace=$workspace;CaptureId=$id;JobId=[Guid]::NewGuid();ExpectedManifestSha256=(Get-DeploymentHash "$root/capture.json");ExpectedSourceInputsSha256=$manifest.sourceInputsSha256;ExpectedCaptureRecipeSha256=$manifest.recipeSha256}
}
function Check([bool]$value,[string]$reason){if(-not $value){throw $reason}}
function Reject([scriptblock]$action,[string]$code){try{$lease=&$action;if($null -ne $lease){$lease.Capture.Pins.Dispose()}}catch{if($_.Exception.Message -eq $code){return};throw};throw "Expected $code"}
function Root($argsMap){Join-Path $workspace ('out/captured-build/'+$argsMap.CaptureId.ToString('N'))}
function PackageFixture {
 $origin=Join-Path $evidence ('package-origin-'+[Guid]::NewGuid().ToString('N'))
 foreach($child in @('managed/Probe','scripts')){New-Item -ItemType Directory -Path (Join-Path $origin $child)|Out-Null}
 & git init --quiet $origin;if($LASTEXITCODE -ne 0){throw 'Fixture Git prerequisite missing'}
 [IO.File]::WriteAllText("$origin/managed/Probe/Probe.csproj",'<Project />',$utf8)
 [IO.File]::WriteAllText("$origin/managed/Probe/Code.cs",'public class Probe {}',$utf8)
 [IO.File]::WriteAllText("$origin/managed/Directory.Build.targets",'<Project />',$utf8)
 [IO.File]::WriteAllText("$origin/scripts/Probe.ps1",'# synthetic source, never executed',$utf8)
 $root=New-CapturedBuild $origin $null $null
 return @{origin=$origin;root=$root;scriptRoot="$root/source/scripts";hash=(Get-DeploymentHash "$root/capture.json")}
}
function RejectPackage([scriptblock]$action,[string]$code){
 $context=$null;$rejected=$false
 try{$context=& $action}catch{if($_.Exception.Message -cne $code){throw};$rejected=$true}
 finally{if($null -ne $context -and $null -ne $context.Capture){$context.Capture.Pins.Dispose()}}
 Check $rejected "Expected package rejection: $code"
}
function OfflineFixture([ValidateSet('package','modules')][string]$Scope='package') {
 $origin=Join-Path $evidence ('offline-origin-'+[Guid]::NewGuid().ToString('N'))
 $null=New-Item -ItemType Directory -Path "$origin/scripts"
 & git init --quiet $origin;if($LASTEXITCODE -ne 0){throw 'Fixture Git prerequisite missing'}
 $scripts=@('OfflinePackageDependencies.ps1','Prepare-PackageDependencies.ps1','Package-M2_7.ps1');if($Scope -ceq 'modules'){$scripts+='Package-ModuleClient.ps1'}
 foreach($name in $scripts){[IO.File]::Copy((Join-Path $PSScriptRoot $name),(Join-Path "$origin/scripts" $name),$false)}
 $jobs=@(if($Scope -ceq 'modules'){Get-OfflineModuleJobs}else{Get-OfflinePackageJobs $false $false})
 foreach($job in $jobs){$path=Join-Path $origin $job.project;$null=New-Item -ItemType Directory -Path (Split-Path $path);[IO.File]::WriteAllText($path,'<Project />',$utf8)}
 [IO.File]::WriteAllText("$origin/managed/Directory.Build.targets",'<Project />',$utf8)
 $captureRoot=New-CapturedBuild $origin $null $null;$capture=Open-CapturedBuild $origin $captureRoot
 try{
  $root=Join-Path $origin ("out/verification/m8-6/$Scope-dependencies/"+[Guid]::NewGuid().ToString('N'))
  $null=New-Item -ItemType Directory -Path "$root/packages/probe/1.0"
  [IO.File]::WriteAllText("$root/packages/probe/1.0/probe.dll",'synthetic dependency, never executed',$utf8)
  $tool=Join-Path $root 'dotnet.exe';[IO.File]::WriteAllText($tool,'synthetic tool, never executed',$utf8)
  foreach($job in $jobs){
   $name=[IO.Path]::GetFileNameWithoutExtension($job.project);$obj=Join-Path $root "artifacts/$($job.variant)/obj/$name"
   $null=New-Item -ItemType Directory -Path $obj
   $targets=@{'net8.0'=@{}};if($job.operation -eq 'publish'){$targets['net8.0/win-x64']=@{}}
   $asset=@{version=3;targets=$targets;packageFolders=@{(Join-Path $root 'packages')=@{}};project=@{restore=@{projectPath=(Join-Path $capture.Source $job.project);outputPath=$obj;frameworks=@{'net8.0'=@{projectReferences=@{}}}}}}
   Write-OfflinePackageJson "$obj/project.assets.json" $asset
   foreach($suffix in @('props','targets')){[IO.File]::WriteAllText("$obj/$name.csproj.nuget.g.$suffix",'<Project />',$utf8)}
  }
  $receipt=[ordered]@{kind="ncma.$Scope.offline-dependencies.v1";configuration='Release';root=$root;includeAI=$false;includeExtensions=$false;captureRoot=$capture.Root;captureManifestSha256=$capture.ManifestSha256;sourceInputsSha256=$capture.Manifest.sourceInputsSha256;captureRecipeSha256=$capture.Manifest.recipeSha256;dotnet=@{path=$tool;sha256=(Get-DeploymentHash $tool)};jobs=$jobs;files=@(Get-OfflinePackageDependencyFiles $origin $root);networkAuthorized=$false}
  Write-OfflinePackageJson "$root/receipt.json" $receipt
  return @{workspace=$origin;capture=$capture;root=$root;hash=(Get-DeploymentHash "$root/receipt.json");tool=$tool}
 }catch{$capture.Pins.Dispose();throw}
}
function RejectOffline([scriptblock]$action,[string]$code){
 $lease=$null;$rejected=$false
 try{$lease=&$action}catch{if($_.Exception.Message -cne $code){throw};$rejected=$true}
 finally{if($null -ne $lease){$lease.Pins.Dispose()}}
 Check $rejected "Expected offline preparation rejection: $code"
}
$cases=[ordered]@{
 'Python environment invocation uses only copied recipe with isolated no-bytecode flags'={
  $inputRoot=Join-Path $workspace ('out/verification/m8-6/python-inputs/'+('a'*32)+'/inputs')
  $output=Join-Path $workspace ('out/verification/m8-6/python-environments/'+('b'*32))
  foreach($step in @('create','install')){
   $plan=Get-PythonWorkerEnvironmentInvocation $workspace $inputRoot $output $step
   Check ($plan.Arguments.Count -eq 6 -and $plan.Arguments[0] -ceq '-I' -and $plan.Arguments[1] -ceq '-B' -and $plan.Arguments[3] -ceq $step) 'Python fixed arguments drift'
   Check ($plan.Arguments[2] -ceq (Join-Path $inputRoot 'recipe/PythonWorkerEnvironment.py') -and $plan.Arguments[4] -ceq $inputRoot -and $plan.Arguments[5] -ceq $output) 'Python copied paths drift'
   $expected=if($step -ceq 'create'){Join-Path $inputRoot 'runtime/python.exe'}else{Join-Path $output 'venv/Scripts/python.exe'}
   Check ($plan.Executable -ceq $expected) 'Python executable drift'
  }
  Check (-not(Test-Path -LiteralPath $output)) 'Pure invocation plan created output'
 }
 'Python environment refuses foreign generation source and arbitrary step'={
  $inputRoot=Join-Path $workspace ('out/verification/m8-6/python-inputs/'+('a'*32)+'/inputs')
  $output=Join-Path $workspace ('out/verification/m8-6/python-environments/'+('b'*32))
  RejectOffline {Get-PythonWorkerEnvironmentInvocation $workspace $inputRoot $output 'pip install'} 'python_environment_step'
  RejectOffline {Get-PythonWorkerEnvironmentInvocation $workspace "$workspace/python/worker" $output 'create'} 'python_environment_input_scope'
  RejectOffline {Get-PythonWorkerEnvironmentInvocation $workspace $inputRoot "$workspace/out/modules/python312" 'create'} 'python_environment_scope'
 }
 'Python environment observation cannot claim execution or substitute extra packages'={
  $inputRoot=Join-Path $workspace ('out/verification/m8-6/python-inputs/'+('a'*32)+'/inputs')
  $output=Join-Path $workspace ('out/verification/m8-6/python-environments/'+('b'*32))
  $wheels=@(Get-PythonWorkerWheels ([IO.File]::ReadAllBytes((Join-Path $workspace 'python/worker/requirements-win-py312.lock'))))
  $value=[pscustomobject]@{kind='ncma.python-worker.environment-observation.v1';inputs=$inputRoot;environment=(Join-Path $output 'venv');baseRuntime=(Join-Path $inputRoot 'runtime');installed=[pscustomobject]@{grpcio=$wheels[0].version;'typing-extensions'=$wheels[1].version};isolated=$true;bytecodeDisabled=$true;pipConfigurationDisabled=$true;pipInstalled=$false;workerExecuted=$false;testsExecuted=$false}
  Assert-PythonWorkerEnvironmentObservation $value $inputRoot $output $wheels
  $value.isolated='true';RejectOffline {Assert-PythonWorkerEnvironmentObservation $value $inputRoot $output $wheels} 'python_environment_isolation';$value.isolated=$true
  $value.testsExecuted=$true;RejectOffline {Assert-PythonWorkerEnvironmentObservation $value $inputRoot $output $wheels} 'python_environment_execution_claim';$value.testsExecuted=$false
  $value.installed.grpcio='0.0.0';RejectOffline {Assert-PythonWorkerEnvironmentObservation $value $inputRoot $output $wheels} 'python_environment_package_closure'
 }
 'SDK preparation fixes the extensions graph and cannot use product or module variants'={
  $jobs=@(Get-OfflineSdkJobs);Check ($jobs.Count -eq 1) 'SDK job count'
  $job=$jobs[0];Check ($job.id -ceq 'workflow-sdk/Ncma.Editor.Services.Tests' -and $job.extensions -and -not $job.ai -and $job.operation -ceq 'build' -and $job.rid -ceq '') 'SDK graph drift'
  $arguments=@(Get-OfflinePackageArguments ([pscustomobject]@{Arguments=@()}) $evidence 'Debug' $job)
  Check ($arguments -contains '-p:NcmaEnableExtensions=true' -and $arguments -contains '-p:NcmaEnableAI=false' -and $arguments -contains "-p:ArtifactsPath=$evidence/artifacts/workflow-sdk") 'SDK isolation binding'
  $job.extensions=$false
  RejectOffline {Get-OfflinePackageArguments ([pscustomobject]@{Arguments=@()}) $evidence 'Debug' $job} 'offline_package_job'
 }
 'Python lock yields exactly two named Windows CPython wheels without URLs'={
  $bytes=[IO.File]::ReadAllBytes((Join-Path $workspace 'python/worker/requirements-win-py312.lock'))
  $wheels=@(Get-PythonWorkerWheels $bytes)
  Check ($wheels.Count -eq 2 -and $wheels[0].file -ceq 'grpcio-1.84.0-cp312-cp312-win_amd64.whl' -and $wheels[1].file -ceq 'typing_extensions-4.16.0-py3-none-any.whl') 'Wheel selection drift'
  Check ($wheels[0].sha256 -ceq '49717E857899F4136D7657BF5ADED61AC479110A075438290923A4D86AF7CD02') 'Wheel digest drift'
 }
 'Python lock rejects duplicate missing injection and invalid UTF8 entries'={
  $first='grpcio==1.84.0 --hash=sha256:'+('a'*64);$second='typing-extensions==4.16.0 --hash=sha256:'+('b'*64)
  RejectOffline {Get-PythonWorkerWheels $utf8.GetBytes("$first`n$first`n$second")} 'python_lock_duplicate'
  RejectOffline {Get-PythonWorkerWheels $utf8.GetBytes($first)} 'python_lock_closure'
  RejectOffline {Get-PythonWorkerWheels $utf8.GetBytes("$first`n$second`n--index-url https://invalid.test")} 'python_lock_entry'
  $bad=$false;try{Get-PythonWorkerWheels ([byte[]]@(255))}catch [Text.DecoderFallbackException]{$bad=$true}
  Check $bad 'Invalid UTF8 lock accepted'
 }
 'Python input ordering is ordinal and rejects case aliases'={
  $entries=@([pscustomobject]@{path='runtime/Lib/a.py'},[pscustomobject]@{path='runtime/Lib/Z.py'},[pscustomobject]@{path='runtime/Lib/_x.py'})
  $ordered=@(Get-PythonWorkerOrderedEntries $entries)
  Check (($ordered.path -join '|') -ceq 'runtime/Lib/Z.py|runtime/Lib/_x.py|runtime/Lib/a.py') 'Python fingerprint order drift'
  RejectOffline {Get-PythonWorkerOrderedEntries @([pscustomobject]@{path='worker/A.py'},[pscustomobject]@{path='worker/a.py'})} 'python_input_duplicate_path'
 }
 'Python interpreter PE rejects x86 and out-of-bounds headers without execution'={
  [byte[]]$pe=[byte[]]::new(256);$pe[0]=77;$pe[1]=90;[BitConverter]::GetBytes([int]64).CopyTo($pe,60)
  [BitConverter]::GetBytes([uint32]17744).CopyTo($pe,64);[BitConverter]::GetBytes([uint16]34404).CopyTo($pe,68);[BitConverter]::GetBytes([uint16]523).CopyTo($pe,88)
  Assert-PythonWorkerExecutable $pe
  [BitConverter]::GetBytes([uint16]332).CopyTo($pe,68)
  RejectOffline {Assert-PythonWorkerExecutable $pe} 'python_interpreter_x64'
  [BitConverter]::GetBytes([int]65536).CopyTo($pe,60)
  RejectOffline {Assert-PythonWorkerExecutable $pe} 'python_interpreter_x64'
 }
 'Python missing wheel fails before output creation or interpreter execution'={
  $origin=Join-Path $evidence ('python-missing-'+[Guid]::NewGuid().ToString('N'))
  foreach($dir in @('python/worker/tests','runtime','wheels')){$null=New-Item -ItemType Directory -Path (Join-Path $origin $dir)}
  foreach($name in @('ncma_module_worker.py','tests/retained_work_probe.py','tests/malformed_reply_probe.py')){[IO.File]::WriteAllText((Join-Path $origin "python/worker/$name"),'# not executed',$utf8)}
  [IO.File]::Copy((Join-Path $workspace 'python/worker/requirements-win-py312.lock'),(Join-Path $origin 'python/worker/requirements-win-py312.lock'),$false)
  [IO.File]::WriteAllText((Join-Path $origin 'runtime/python.exe'),'never executable',$utf8)
  RejectOffline {New-PythonWorkerInputs $origin "$origin/runtime/python.exe" "$origin/wheels"} 'python_offline_wheel_missing:grpcio-1.84.0-cp312-cp312-win_amd64.whl'
  Check (-not(Test-Path -LiteralPath "$origin/out")) 'Missing dependency created output'
 }
 'module recipes separate library publish from test build without RID or apphost'={
  $jobs=@(Get-OfflineModuleJobs);Check ($jobs.Count -eq 2) 'Module job count'
  Check ($jobs[0].operation -ceq 'publish-library' -and $jobs[1].operation -ceq 'build') 'Module operation drift'
  foreach($job in $jobs){
   $arguments=@(Get-OfflinePackageArguments ([pscustomobject]@{Arguments=@()}) $evidence 'Release' $job)
   Check ($job.variant -ceq 'module-transport' -and $job.rid -ceq '' -and -not $job.ai -and -not $job.extensions) 'Unexpected module variant'
   Check (@($arguments|Where-Object {$_ -match 'RuntimeIdentifier|UseAppHost'}).Count -eq 0) 'Module acquired product RID/apphost'
   Check ($arguments -contains '-p:Configuration=Release' -and $arguments -contains "-p:RestorePackagesPath=$evidence/packages") 'Module argument binding'
  }
  $jobs[0].operation='publish'
  RejectOffline {Get-OfflinePackageArguments ([pscustomobject]@{Arguments=@()}) $evidence 'Release' $jobs[0]} 'offline_package_job'
 }
 'module preparation rejects product scope and feature substitution while retaining exact dependencies'={
  $p=OfflineFixture 'modules';$lease=$null
  try{
   RejectOffline {Open-OfflinePackageDependencies $p.workspace $p.capture $p.root $p.hash 'Release' $false $false $p.tool} 'offline_package_preparation_identity'
   RejectOffline {Open-OfflinePackageDependencies $p.workspace $p.capture $p.root $p.hash 'Release' $true $false $p.tool 'modules'} 'offline_package_scope'
   RejectOffline {Open-OfflinePackageDependencies $p.workspace $p.capture $p.root $p.hash 'Debug' $false $false $p.tool 'modules'} 'offline_package_receipt_binding'
   $lease=Open-OfflinePackageDependencies $p.workspace $p.capture $p.root $p.hash 'Release' $false $false $p.tool 'modules'
   Check ($lease.Jobs.Count -eq 2) 'Missing module preparation jobs'
   $blocked=$false;try{[IO.File]::WriteAllText("$($p.root)/packages/probe/1.0/probe.dll",'changed',$utf8)}catch [IO.IOException]{$blocked=$true}
   Check $blocked 'Module dependency not retained'
  }finally{if($null -ne $lease){$lease.Pins.Dispose()};$p.capture.Pins.Dispose()}
 }
 'module assets reject unexpected RID before library publication'={
  $p=OfflineFixture 'modules'
  try{
   $asset=Join-Path $p.root 'artifacts/module-transport/obj/Ncma.Modules.Grpc/project.assets.json'
   $value=Get-Content -Raw $asset|ConvertFrom-Json
   $value.targets|Add-Member -NotePropertyName 'net8.0/win-x64' -NotePropertyValue ([pscustomobject]@{})
   [IO.File]::WriteAllText($asset,($value|ConvertTo-Json -Depth 10),$utf8)
   RejectOffline {Assert-OfflinePackageAssets $p.capture $p.root @(Get-OfflineModuleJobs)} 'offline_module_target_mismatch'
  }finally{$p.capture.Pins.Dispose()}
 }
 'verification recipes contain exactly six isolated no-RID build jobs'={
  $jobs=@(Get-OfflineVerificationJobs);Check ($jobs.Count -eq 6 -and @($jobs.id|Sort-Object -Unique).Count -eq 6) 'Verification job drift'
  Check (@($jobs|Where-Object {$_.operation -cne 'build' -or $_.rid -cne ''}).Count -eq 0) 'Verification unexpectedly publishes'
  Check (@($jobs|Where-Object variant -ceq 'verify-contracts').Count -eq 1) 'Contracts root missing'
  foreach($job in $jobs){
   $arguments=@(Get-OfflinePackageArguments ([pscustomobject]@{Arguments=@()}) $evidence 'Debug' $job)
   Check ($arguments -contains "-p:ArtifactsPath=$evidence/artifacts/$($job.variant)" -and $arguments -contains '-p:Configuration=Debug') 'Verification argument binding'
  }
 }
 'verification staging retains original canonical CTest and receipt roots'={
  foreach($configuration in @('Debug','Release')){
   $roots=Get-AiVerificationRoots $workspace $configuration
   Check ((Get-VerificationInputTarget $workspace $configuration 'verify-ai/Ncma.Editor.Services.Tests') -ceq $roots.optional) 'Optional root drift'
   Check ((Get-VerificationInputTarget $workspace $configuration 'verify-contracts/Ncma.AI.Tests') -ceq $roots.contracts) 'Contracts root drift'
   foreach($variant in @('extensions','ai-extensions')){foreach($name in @('Ncma.Editor.Services.Tests','Ncma.Player.Tests')){
    $expected=[IO.Path]::GetFullPath((Join-Path $workspace "out/build/windows-ninja-$($configuration.ToLowerInvariant())/m8-5-$variant/bin/$name/$($configuration.ToLowerInvariant())"))
    Check ((Get-VerificationInputTarget $workspace $configuration "verify-$variant/$name") -ceq $expected) 'Extension root drift'
   }}
  }
  RejectOffline {Get-VerificationInputTarget $workspace 'Profile' 'verify-contracts/Ncma.AI.Tests'} 'verification_input_configuration'
  RejectOffline {Get-VerificationInputTarget $workspace 'Debug' '../foreign'} 'verification_input_job'
 }
 'verification staging copies complete trees and permits only byte-identical reuse'={
  $origin=Join-Path $evidence ('staging-'+[Guid]::NewGuid().ToString('N'));$prepared=Join-Path $origin 'prepared'
  $source=Join-Path $prepared 'artifacts/verify-contracts/bin/Ncma.AI.Tests/release';$null=New-Item -ItemType Directory -Path "$source/nested"
  foreach($name in @('Ncma.AI.Tests.dll','Ncma.AI.Tests.deps.json','Ncma.AI.Tests.runtimeconfig.json','nested/extra.dat')){[IO.File]::WriteAllText((Join-Path $source $name),('synthetic:'+ $name),$utf8)}
  $lease=[pscustomobject]@{Root=$prepared;Jobs=@(Get-OfflineVerificationJobs)}
  $target=Copy-VerificationInputTree $origin 'Release' $lease 'verify-contracts/Ncma.AI.Tests'
  $before=@(Get-AiVerificationFiles $target)
  Check ($before.Count -eq 4 -and (Get-DeploymentHash "$target/nested/extra.dat") -ceq (Get-DeploymentHash "$source/nested/extra.dat")) 'Incomplete staged tree'
  Check ((Copy-VerificationInputTree $origin 'Release' $lease 'verify-contracts/Ncma.AI.Tests') -ceq $target) 'Identical reuse failed'
  [IO.File]::WriteAllText("$source/nested/extra.dat",'different generation',$utf8)
  RejectOffline {Copy-VerificationInputTree $origin 'Release' $lease 'verify-contracts/Ncma.AI.Tests'} 'verification_input_existing_generation_requires_preserved_activation'
  Check ((Get-AiVerificationFiles $target|ConvertTo-Json -Compress) -ceq ($before|ConvertTo-Json -Compress)) 'Existing input mutated'
 }
 'verification staging rejects incomplete output before creating canonical target'={
  $origin=Join-Path $evidence ('missing-staging-'+[Guid]::NewGuid().ToString('N'));$prepared=Join-Path $origin 'prepared'
  $source=Join-Path $prepared 'artifacts/verify-contracts/bin/Ncma.AI.Tests/debug';$null=New-Item -ItemType Directory -Path $source
  [IO.File]::WriteAllText("$source/Ncma.AI.Tests.dll",'synthetic',$utf8)
  $lease=[pscustomobject]@{Root=$prepared;Jobs=@(Get-OfflineVerificationJobs)}
  RejectOffline {Copy-VerificationInputTree $origin 'Debug' $lease 'verify-contracts/Ncma.AI.Tests'} 'verification_input_entry_missing'
  Check (-not(Test-Path -LiteralPath (Get-VerificationInputTarget $origin 'Debug' 'verify-contracts/Ncma.AI.Tests'))) 'Rejected staging wrote target'
 }
 'offline package recipes isolate no-RID tools and all requested publish variants'={
  foreach($spec in @(@($false,$false,5),@($true,$false,6),@($false,$true,7),@($true,$true,10))){
   $jobs=@(Get-OfflinePackageJobs $spec[0] $spec[1]);Check ($jobs.Count -eq $spec[2]) 'Job count drift'
   Check (@($jobs.id|Sort-Object -Unique).Count -eq $jobs.Count) 'Duplicate job'
   Check (@($jobs|Where-Object {$_.operation -eq 'build' -and ($_.rid -ne '' -or $_.variant -ne 'tools')}).Count -eq 0) 'Tool RID conflated'
   Check (@($jobs|Where-Object {$_.operation -eq 'publish' -and $_.rid -ne 'win-x64'}).Count -eq 0) 'Publish RID absent'
  }
 }
 'offline package arguments preserve exact configuration feature and artifact binding'={
  $capture=[pscustomobject]@{Arguments=@('-p:DirectoryBuildPropsPath=CAPTURE','-p:DirectoryBuildTargetsPath=TARGETS')}
  foreach($job in @(Get-OfflinePackageJobs $true $true)){
   $arguments=@(Get-OfflinePackageArguments $capture $evidence 'Release' $job)
   Check ($arguments -contains '-p:Configuration=Release' -and $arguments -contains "-p:ArtifactsPath=$evidence/artifacts/$($job.variant)" -and $arguments -contains "-p:RestorePackagesPath=$evidence/packages") 'Output/config binding missing'
   Check (($arguments -contains '-p:RuntimeIdentifier=win-x64') -eq ($job.operation -eq 'publish')) 'RID binding mismatch'
   Check ($arguments -contains "-p:NcmaEnableAI=$($job.ai.ToString().ToLowerInvariant())" -and $arguments -contains "-p:NcmaEnableExtensions=$($job.extensions.ToString().ToLowerInvariant())") 'Feature binding mismatch'
  }
 }
 'offline package path rejects network relative and MSBuild property injection'={
  foreach($bad in @('relative','https://feed.invalid','\\server\share\feed',($evidence+';Property=x'),($evidence+'%3Bbad'))){RejectOffline {Get-OfflinePackagePath $bad} 'offline_package_local_path'}
 }
 'offline package exact receipt retains dependency bytes and rejects changed config or features'={
  $p=OfflineFixture;$lease=$null
  try{
   RejectOffline {Open-OfflinePackageDependencies $p.workspace $p.capture $p.root ('F'*64) 'Release' $false $false $p.tool} 'offline_package_receipt_changed'
   RejectOffline {Open-OfflinePackageDependencies $p.workspace $p.capture $p.root $p.hash 'Debug' $false $false $p.tool} 'offline_package_receipt_binding'
   RejectOffline {Open-OfflinePackageDependencies $p.workspace $p.capture $p.root $p.hash 'Release' $true $false $p.tool} 'offline_package_receipt_binding'
   $lease=Open-OfflinePackageDependencies $p.workspace $p.capture $p.root $p.hash 'Release' $false $false $p.tool
   Check ($lease.Jobs.Count -eq 5) 'Missing prepared jobs'
   $blocked=$false;try{[IO.File]::WriteAllText("$($p.root)/packages/probe/1.0/probe.dll",'changed',$utf8)}catch [IO.IOException]{$blocked=$true}
   Check $blocked 'Dependency not retained'
  }finally{if($null -ne $lease){$lease.Pins.Dispose()};$p.capture.Pins.Dispose()}
 }
 'offline package dependency edits and additions are rejected with no SDK invocation'={
  $p=OfflineFixture
  try{
   [IO.File]::WriteAllText("$($p.root)/packages/probe/1.0/probe.dll",'changed',$utf8)
   RejectOffline {Open-OfflinePackageDependencies $p.workspace $p.capture $p.root $p.hash 'Release' $false $false $p.tool} 'offline_package_dependency_changed'
   [IO.File]::WriteAllText("$($p.root)/packages/probe/1.0/extra.dll",'extra',$utf8)
   RejectOffline {Open-OfflinePackageDependencies $p.workspace $p.capture $p.root $p.hash 'Release' $false $false $p.tool} 'offline_package_dependency_set'
  }finally{$p.capture.Pins.Dispose()}
 }
 'package verification binds canonical IO to retained capture, not later author bytes'={
  $p=PackageFixture
  $context=Open-PackageVerificationContext $p.scriptRoot $p.origin $p.root $p.hash
  try{
   Check ($context.Workspace -ceq [IO.Path]::GetFullPath($p.origin) -and $context.Capture.Root -ceq [IO.Path]::GetFullPath($p.root)) 'Package roots conflated'
   $value=[pscustomobject]@{capturedBuildRoot=$context.Capture.Root;captureManifestSha256=$p.hash;sourceInputsSha256=$context.SourceInputsSha256}
   Assert-PackageVerificationCapture $context $value
   [IO.File]::WriteAllText("$($p.origin)/managed/Probe/Code.cs",'later author bytes',$utf8)
   Assert-PackageVerificationCapture $context $value
   $blocked=$false;try{[IO.File]::WriteAllText("$($p.root)/source/managed/Probe/Code.cs",'changed',$utf8)}catch [IO.IOException]{$blocked=$true}
   Check $blocked 'Package capture input not held'
  }finally{$context.Capture.Pins.Dispose()}
 }
 'package verification rejects incomplete binding, wrong manifest and foreign script root'={
  $p=PackageFixture
  RejectPackage {Open-PackageVerificationContext $p.scriptRoot $p.origin '' $p.hash} 'package_verification_binding_required'
  RejectPackage {Open-PackageVerificationContext $p.scriptRoot $p.origin $p.root ('F'*64)} 'package_verification_capture_changed'
  RejectPackage {Open-PackageVerificationContext "$($p.origin)/scripts" $p.origin $p.root $p.hash} 'package_verification_script_root'
  RejectPackage {Open-PackageVerificationContext $p.scriptRoot "$($p.origin)/managed" $p.root $p.hash} 'package_verification_workspace'
  $stream=[IO.File]::Open("$($p.root)/capture.json",[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None);$stream.Dispose()
 }
 'package index manifest and compile receipt cannot substitute a different capture'={
  $p=PackageFixture;$context=Open-PackageVerificationContext $p.scriptRoot $p.origin $p.root $p.hash
  try{
   foreach($field in @('capturedBuildRoot','captureManifestSha256','sourceInputsSha256')){
    $value=[pscustomobject]@{capturedBuildRoot=$context.Capture.Root;captureManifestSha256=$p.hash;sourceInputsSha256=$context.SourceInputsSha256}
    $value.$field='wrong'
    RejectPackage {Assert-PackageVerificationCapture $context $value} 'package_verification_input_capture_mismatch'
   }
   RejectPackage {Assert-PackageVerificationCapture $context ([pscustomobject]@{sourceInputsSha256=$context.SourceInputsSha256})} 'package_verification_uncaptured_input'
  }finally{$context.Capture.Pins.Dispose()}
 }
 'native test and shader tool source closure is copied and pinned, never reread from origin'={
  $origin=Join-Path $evidence ('origin-'+[Guid]::NewGuid().ToString('N'))
  foreach($child in @('managed/Probe','tests/plugins','tools')){New-Item -ItemType Directory -Path (Join-Path $origin $child)|Out-Null}
  & git init --quiet $origin;if($LASTEXITCODE -ne 0){throw 'Fixture Git prerequisite missing'}
  [IO.File]::WriteAllText("$origin/managed/Probe/Probe.csproj",'<Project />',$utf8)
  [IO.File]::WriteAllText("$origin/managed/Probe/Probe.cs",'public class Probe {}',$utf8)
  [IO.File]::WriteAllText("$origin/managed/Directory.Build.targets",'<Project />',$utf8)
  foreach($path in @('tests/plugins/Probe.cpp','tests/TestAssertions.h','tools/Shader.cpp')){[IO.File]::WriteAllText((Join-Path $origin $path),'// exact native input',$utf8)}
  $packageInputs=@(Get-AiCandidateSourceInputs $origin)
  $captureInputs=@(Get-CapturedOriginInputs $origin)
  Check (($packageInputs|ConvertTo-Json -Depth 5 -Compress) -ceq ($captureInputs|ConvertTo-Json -Depth 5 -Compress)) 'Capture and package selection differ'
  $captured=New-CapturedBuild $origin $null $null;$lease=Open-CapturedBuild $origin $captured
  try{
   Check ((Get-AiCandidateInputHash $packageInputs) -ceq $lease.Manifest.sourceInputsSha256) 'Unpatched capture and package source fingerprint differ'
   foreach($path in @('tests/plugins/Probe.cpp','tests/TestAssertions.h','tools/Shader.cpp')){
    Check (@($lease.Manifest.files|Where-Object path -ceq $path).Count -eq 1) 'Native source omitted or duplicated'
    [IO.File]::WriteAllText((Join-Path $origin $path),'// later author bytes',$utf8)
    Check ([IO.File]::ReadAllText((Join-Path $lease.Source $path)) -ceq '// exact native input') 'Origin reread into captured native input'
    $blocked=$false;try{[IO.File]::WriteAllText((Join-Path $lease.Source $path),'changed',$utf8)}catch [IO.IOException]{$blocked=$true}
    Check $blocked 'Captured native source not pinned'
   }
  }finally{$lease.Pins.Dispose()}
 }
 'shared source selection includes root build settings and excludes generated files'={
  $origin=Join-Path $evidence ('selection-'+[Guid]::NewGuid().ToString('N'))
  foreach($child in @('managed/Probe/bin','managed/Probe/obj','tests','tools','out','docs')){New-Item -ItemType Directory -Path (Join-Path $origin $child)|Out-Null}
  & git init --quiet $origin;if($LASTEXITCODE -ne 0){throw 'Fixture Git prerequisite missing'}
  $included=@('.editorconfig','CMakeLists.txt','CMakePresets.json','Directory.Build.props','Directory.Build.targets','Directory.Packages.props','managed/Probe/Code.cs','tests/Probe.cpp','tools/Shader.cpp')
  foreach($path in $included){[IO.File]::WriteAllText((Join-Path $origin $path),'exact input',$utf8)}
  foreach($path in @('managed/Probe/bin/Generated.cs','managed/Probe/obj/Generated.cs','managed/Probe/local.user','out/secret.txt','docs/note.md')){[IO.File]::WriteAllText((Join-Path $origin $path),'not a build input',$utf8)}
  $selected=@(Get-AiCandidateSourceInputs $origin);$captured=@(Get-CapturedOriginInputs $origin)
  Check ($selected.Count -eq $included.Count -and @(Compare-Object @($selected.path) $included).Count -eq 0) 'Complete selection or exclusions changed'
  Check (($selected|ConvertTo-Json -Depth 5 -Compress) -ceq ($captured|ConvertTo-Json -Depth 5 -Compress)) 'Shared source adapters drifted'
  $before=Get-AiCandidateInputHash $selected
  [IO.File]::WriteAllText("$origin/tools/Shader.cpp",'changed tool',$utf8)
  Check ((Get-AiCandidateInputHash @(Get-AiCandidateSourceInputs $origin)) -cne $before) 'Native tool change omitted from package fingerprint'
  Check ((Get-AiCandidateInputHash @(Get-CapturedOriginInputs $origin)) -ceq (Get-AiCandidateInputHash @(Get-AiCandidateSourceInputs $origin))) 'Adapters disagree after tool change'
 }
 'source discovery rejects a nested directory borrowing its ancestor Git identity'={
  $origin=Join-Path $evidence ('wrong-root-'+[Guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Path "$origin/managed/Probe"|Out-Null
  & git init --quiet $origin;if($LASTEXITCODE -ne 0){throw 'Fixture Git prerequisite missing'}
  [IO.File]::WriteAllText("$origin/managed/Probe/Code.cs",'exact input',$utf8)
  foreach($adapter in @('Get-AiCandidateSourceInputs','Get-CapturedOriginInputs')){
   $rejected=$false
   try{$null=& $adapter "$origin/managed"}catch{if($_.Exception.Message -cne 'ai_package_source_workspace'){throw};$rejected=$true}
   Check $rejected 'Nested source root accepted as a checkout'
  }
 }
 'physical capture pinned; preparation has no process or output authority'={
  $p=Fixture;$root=Root $p;$lease=Open-WorkflowBuildInputs @p
  try{
   $facts=$lease.FactsJson|ConvertFrom-Json
   Check (-not $facts.processStarted -and -not $facts.launchAuthorized -and -not $facts.outputReserved) 'Preparation claimed authority'
   Check ($facts.captureManifestSha256 -ceq $p.ExpectedManifestSha256 -and $facts.sourceFileCount -eq 3) 'Wrong captured facts'
   Check (-not(Test-Path -LiteralPath $facts.outputRoot)) 'Preparation created output'
   $blocked=$false
   try{$stream=[IO.File]::Open("$root/source/managed/Ncma.Editor.Services.Tests/Probe.cs",[IO.FileMode]::Open,[IO.FileAccess]::Write,[IO.FileShare]::Read);$stream.Dispose()}catch [IO.IOException]{$blocked=$true}
   Check $blocked 'Source not write-locked'
   Check ($facts.project -ceq 'managed/Ncma.Editor.Services.Tests/Ncma.Editor.Services.Tests.csproj') 'Wrong fixed project'
  }finally{$lease.Capture.Pins.Dispose()}
 }
 'each expected capture binding mismatch rejects and releases locks'={
  foreach($field in @('ExpectedManifestSha256','ExpectedSourceInputsSha256','ExpectedCaptureRecipeSha256','ExpectedReviewBundleSha256')){
   $p=Fixture;$root=Root $p;$p[$field]='F'*64
   Reject {Open-WorkflowBuildInputs @p} 'workflow_capture_binding_changed'
   $stream=[IO.File]::Open("$root/capture.json",[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None);$stream.Dispose()
  }
 }
 'changed actual captured source is rejected before any launch'={
  $p=Fixture;$root=Root $p;[IO.File]::WriteAllText("$root/source/managed/Ncma.Editor.Services.Tests/Probe.cs",'changed',$utf8)
  Reject {Open-WorkflowBuildInputs @p} 'capture_content_changed'
 }
 'fixed recipe configuration IDs and hash format reject unapproved alternatives'={
  $p=Fixture;$p.RecipeId='workflow-arbitrary-v1';Reject {Open-WorkflowBuildInputs @p} 'workflow_build_recipe_required';$null=$p.Remove('RecipeId')
  $p.Configuration='debug';Reject {Open-WorkflowBuildInputs @p} 'workflow_job_configuration';$null=$p.Remove('Configuration')
  $p.Variant='unknown';Reject {Open-WorkflowBuildInputs @p} 'workflow_job_configuration';$null=$p.Remove('Variant')
  $p.JobId=[Guid]::Empty;Reject {Open-WorkflowBuildInputs @p} 'workflow_job_identity';$p.JobId=[Guid]::NewGuid()
  $p.ExpectedManifestSha256='bad';Reject {Open-WorkflowBuildInputs @p} 'workflow_capture_hash'
 }
 'foreign workspace and reused output directory are rejected without overwriting'={
  $p=Fixture;$copy=$p.Clone();$copy.Workspace=Join-Path $evidence 'foreign';Reject {Open-WorkflowBuildInputs @copy} 'workflow_workspace_mismatch'
  $output=Assert-DeploymentPath $workspace (Join-Path $workspace ('out/verification/m8-6/jobs/'+$p.JobId.ToString('N')))
  New-Item -ItemType Directory -Path $output|Out-Null
  [IO.File]::WriteAllText("$output/retain.txt",'retain',$utf8)
  Reject {Open-WorkflowBuildInputs @p} 'workflow_job_output_exists'
  Check ((Get-Content -Raw -LiteralPath "$output/retain.txt") -ceq 'retain') 'Existing job overwritten'
 }
 'required fixed project missing from otherwise valid capture is rejected'={
  $p=Fixture -MissingProject;Reject {Open-WorkflowBuildInputs @p} 'workflow_capture_required_input'
 }
 'captured directory reparse alias is rejected; evidence junction retained'={
  $p=Fixture;$target=Root $p;$aliasId=[Guid]::NewGuid();$alias=Assert-DeploymentPath $workspace (Join-Path $workspace ('out/captured-build/'+$aliasId.ToString('N')))
  New-Item -ItemType Junction -Path $alias -Target $target|Out-Null
  [IO.File]::WriteAllText((Join-Path $evidence 'reparse-alias.json'),(@{alias=$alias;target=$target;retained=$true;recursiveCleanupAllowed=$false}|ConvertTo-Json),$utf8)
  $p.CaptureId=$aliasId;Reject {Open-WorkflowBuildInputs @p} 'Reparse deployment item.'
 }
}
$failed=0
foreach($case in $cases.GetEnumerator()){
 Write-Output "BEGIN workflow capture / $($case.Key)"
 try{&$case.Value;Write-Output "PASS workflow capture / $($case.Key)"}catch{$failed++;Write-Output "FAIL workflow capture / $($case.Key): $($_.Exception)"}
}
Write-Output "SUMMARY workflow capture: total=$($cases.Count), passed=$($cases.Count-$failed), failed=$failed; evidence=$evidence"
if($failed){exit 1};exit 0
