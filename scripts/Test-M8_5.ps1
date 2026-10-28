[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateSet('Compile','Inventory','Tests')][string]$Mode,
 [Parameter(Mandatory=$true)][ValidateSet('Debug','Release')][string]$Configuration,[string]$CapturedBuildRoot)
# No root Build script, publish, deployment, model connection or credential access.
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'ExtensionPackage.ps1')
. (Join-Path $PSScriptRoot 'M8_5-TestAttribution.ps1')
$workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot));Set-Location -LiteralPath $workspace
$evidence=Assert-DeploymentPath $workspace (Join-Path $workspace ('out/verification/m8-5/final/'+$Configuration+'/'+[Guid]::NewGuid().ToString('N')))
New-Item -ItemType Directory -Path $evidence|Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
function Save([string]$name,$value){[IO.File]::WriteAllText((Join-Path $evidence $name),($value|ConvertTo-Json -Depth 12),$utf8)}
$capture=$null;$compileWorkspace=$workspace;$captureArgs=@()
if($Mode -eq 'Compile' -or $CapturedBuildRoot){
 if(-not $CapturedBuildRoot){$CapturedBuildRoot=New-CapturedBuild $workspace $null $null}
 $capture=Open-CapturedBuild $workspace $CapturedBuildRoot;$compileWorkspace=$capture.Source;$captureArgs=@($capture.Arguments)
}
$inputs=@(if($null -ne $capture){$capture.Manifest.files}else{Get-AiCandidateSourceInputs $workspace});$sourceHash=Get-AiCandidateInputHash $inputs;Save 'source-inputs.json' $inputs
$formal=@{};foreach($file in 'NcmaEngine.exe','NcmaEngine.dll','deployment-manifest.json'){$formal[$file]=Get-DeploymentHash (Join-Path $workspace "out/bin/$file")}
$projects=@($inputs|Where-Object{$_.path.EndsWith('.csproj',[StringComparison]::Ordinal) -and $null -eq $_.PSObject.Properties['deleted']}|ForEach-Object{$_.path})
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs=& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($vs)){throw 'C++ toolchain missing'}
$cmake=Join-Path $vs 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
$ctest=Join-Path (Split-Path $cmake) 'ctest.exe';$vcvars=Join-Path $vs 'VC/Auxiliary/Build/vcvars64.bat'
$build=Join-Path $workspace ('out/build/windows-ninja-'+$Configuration.ToLowerInvariant())
$exit=0;$failure=$null;$testsExecuted=$false;$attribution=$null
try{
 switch($Mode){
  Compile {
   # Tool-generated temporary solution enumerates every current project, including
   # those intentionally not included in the IDE solution. Does not modify it.
   & dotnet new sln --name AllManaged --format sln --output $evidence *> "$evidence/solution.log"
   if($LASTEXITCODE -ne 0){throw 'Temporary solution creation failed'}
   $projectPaths=@($projects|ForEach-Object{Join-Path $compileWorkspace $_})
   & dotnet sln "$evidence/AllManaged.sln" add @projectPaths *>> "$evidence/solution.log"
   if($LASTEXITCODE -ne 0){throw 'Full project inventory registration failed'}
   $env:DOTNET_CLI_UI_LANGUAGE='en'
   & dotnet build "$evidence/AllManaged.sln" @captureArgs -c $Configuration --nologo --disable-build-servers -m:1 -p:UseSharedCompilation=false -warnaserror -p:NcmaEnableAI=false -p:NcmaEnableExtensions=false "-bl:$evidence/build.binlog" *> "$evidence/build.log"
   $exit=$LASTEXITCODE;$text=Get-Content -Raw "$evidence/build.log"
   $zero=$text -match '0 Warning\(s\)' -and $text -match '0 Error\(s\)'
   Save 'build-report.json' @{sourceInputsSha256=$sourceHash;capturedBuildRoot=$capture.Root;captureManifestSha256=$capture.ManifestSha256;configuration=$Configuration;projects=$projects;exitCode=$exit;warnings=$(if($zero){0}else{-1});errors=$(if($zero){0}else{-1})}
   Get-Content "$evidence/build.log" -Tail 10
   if($exit -ne 0 -or -not $zero){throw 'Full managed compile did not pass 0/0 gate'}
   & (Join-Path $PSScriptRoot 'Prepare-ExtensionVerification.ps1') -Configuration $Configuration -CapturedBuildRoot $capture.Root
   & (Join-Path $PSScriptRoot 'Prepare-AiVerification.ps1') -Configuration $Configuration -DotnetExecutable (Get-Command dotnet.exe).Source -CapturedBuildRoot $capture.Root
  }
  Inventory {
   & $env:ComSpec /d /s /c ('call "{0}" >nul && "{1}" -S "{2}" -B "{3}" && "{1}" --build "{3}" --parallel 4' -f $vcvars,$cmake,$workspace,$build) *> "$evidence/native.log"
   $exit=$LASTEXITCODE;Get-Content "$evidence/native.log" -Tail 12
   if($exit -ne 0){throw 'Native configure/build failed'}
   & $ctest --test-dir $build -N *> "$evidence/ctest-N.log";if($LASTEXITCODE -ne 0){throw 'ctest -N failed'}
   & $ctest --test-dir $build --show-only=json-v1 > "$evidence/inventory.json";if($LASTEXITCODE -ne 0){throw 'CTest inventory failed'}
   $inventory=Get-Content -Raw "$evidence/inventory.json"|ConvertFrom-Json
   if((Get-Content -Raw "$evidence/ctest-N.log") -notmatch ('Total Tests:\s+'+$inventory.tests.Count+'\s*$')){throw 'CTest inventory count mismatch'}
   $attribution=New-M85AttributionReport "$evidence/inventory.json" '' $Configuration 'inventory-only'
   Save 'attribution.json' $attribution
   Write-Output "Registered $($inventory.tests.Count) tests; not executed."
  }
  Tests {
   & $ctest --test-dir $build -N *> "$evidence/ctest-N.log";if($LASTEXITCODE -ne 0){throw 'ctest -N failed'}
   & $ctest --test-dir $build --show-only=json-v1 > "$evidence/inventory.json";if($LASTEXITCODE -ne 0){throw 'CTest inventory failed'}
   $inventory=Get-Content -Raw "$evidence/inventory.json"|ConvertFrom-Json
   if((Get-Content -Raw "$evidence/ctest-N.log") -notmatch ('Total Tests:\s+'+$inventory.tests.Count+'\s*$')){throw 'CTest inventory count mismatch'}
   $groups=Get-M85TestGroups $inventory
   $names=@($groups.Keys|Where-Object{$groups[$_] -eq 'extension-new'}|Sort-Object)
   $filter='^('+ (($names|ForEach-Object{[regex]::Escape($_)}) -join '|') +')$'
   # D owns only extension-new. Legacy refers to F6/F7; environment blockers
   # remain explicit non-execution, never a whole-engine or install acceptance.
   # No filtering inside an entry and no timeout/requirement relaxation.
   $testsExecuted=$true
   & $ctest --test-dir $build -R $filter --no-tests=error --parallel 1 --output-on-failure --output-junit "$evidence/junit.xml" *> "$evidence/tests.log"
   $exit=$LASTEXITCODE;Get-Content "$evidence/tests.log" -Tail 35
   $attribution=New-M85AttributionReport "$evidence/inventory.json" "$evidence/junit.xml" $Configuration 'current-extension-only'
   Save 'attribution.json' $attribution
   if($attribution.conclusion -ne 'passed'){$exit=1}
  }
 }
 if($null -eq $capture -and (Get-AiCandidateInputHash @(Get-AiCandidateSourceInputs $workspace)) -cne $sourceHash){throw 'Selected source inputs changed during verification'}
}catch{$failure=$_.Exception.Message;$exit=1;Write-Output "FAILED: $failure"}finally{
 foreach($file in $formal.Keys){if((Get-DeploymentHash (Join-Path $workspace "out/bin/$file")) -cne $formal[$file]){$failure='Formal installed files changed';$exit=1}}
 Save 'result.json' @{kind='ncma.m8.5.diagnostic.v2';mode=$Mode;configuration=$Configuration;sourceInputsSha256=$sourceHash;capturedBuildRoot=$(if($null -ne $capture){$capture.Root}else{$null});captureManifestSha256=$(if($null -ne $capture){$capture.ManifestSha256}else{$null});exitCode=$exit;failure=$failure;formal=$formal;accepted=$false;manualAcceptance=$false;testsExecuted=$testsExecuted;conclusionScope='extension-new';attribution=$attribution;deployment=$false}
 Write-Output "Evidence: $evidence"
 if($null -ne $capture){$capture.Pins.Dispose()}
}
exit $exit
