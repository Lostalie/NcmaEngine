[CmdletBinding()]param()
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'ExtensionPackage.ps1')
$workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot));$sourceHash='A'*64
$utf8=[Text.UTF8Encoding]::new($false)
function Save-Index($f){[IO.File]::WriteAllText($f.path,($f.index|ConvertTo-Json -Depth 8),$utf8)}
function Save-Product($f,[string]$name){
 $entry=$f.index.products.$name;$directory=$entry.path
 $ai=$name -ceq 'editorAiExtensions'
 $product=switch($name){'editorExtensions'{'NcmaEngine-editor-candidate'};'editorAiExtensions'{'NcmaEngine-ai-editor-candidate'};'playerDx11Extensions'{'NcmaPlayer-dx11-candidate'};'playerNullPhysicsExtensions'{'NcmaPlayer-null-physics-candidate'};'playerDx11PhysicsExtensions'{'NcmaPlayer-dx11-physics-candidate'};default{'NcmaPlayer-null-candidate'}}
 $manifest=@{schemaVersion=1;product=$product;configuration='Debug';rid='win-x64';tfm='net8.0';publishMode='framework-dependent';production=$false;
  extensionsEnabled=$true;aiEnabled=$ai;sourceInputsSha256=$sourceHash;files=@(Get-DeploymentFiles $workspace $directory|Where-Object path -ne 'deployment-manifest.json');syntheticFixture=$true}
 [IO.File]::WriteAllText((Join-Path $directory 'deployment-manifest.json'),($manifest|ConvertTo-Json -Depth 7),$utf8)
 $entry.manifestSha256=Get-DeploymentHash (Join-Path $directory 'deployment-manifest.json');Save-Index $f
}
function New-Fixture([bool]$AI=$false){
 $root=Join-Path $workspace ('out/package/m2-7/Debug/'+[Guid]::NewGuid().ToString('N'));$null=Assert-DeploymentPath $workspace $root
 New-Item -ItemType Directory -Path $root|Out-Null
 $index=@{schemaVersion=1;configuration='Debug';tested=$false;production=$false;sourceInputsSha256=$sourceHash;products=@{}}
 $f=[pscustomobject]@{path=(Join-Path $root 'extensions-packages.json');index=$index}
 $names=@('editorExtensions','playerNullExtensions','playerDx11Extensions','playerNullPhysicsExtensions','playerDx11PhysicsExtensions')
 if($AI){$names+=@('editorAiExtensions','playerNullAiExtensions')}
 foreach($name in $names){
  $directory=Join-Path $root $name;New-Item -ItemType Directory -Path $directory|Out-Null
  $assembly=if($name.StartsWith('editor')){'NcmaEngine'}else{'NcmaPlayer'}
  $libraries=@{'Ncma.Extensions/1.0.0'=@{};'Ncma.Extensions.Hosting/1.0.0'=@{}}
  foreach($dll in 'Ncma.Extensions','Ncma.Extensions.Hosting',$assembly){[IO.File]::WriteAllText((Join-Path $directory ($dll+'.dll')),'synthetic-contract-fixture-never-executed',$utf8)}
  if($name -eq 'editorAiExtensions'){foreach($dll in 'Ncma.AI','Ncma.Modules','Ncma.Editor.Modules'){$libraries[$dll+'/1.0.0']=@{};[IO.File]::WriteAllText((Join-Path $directory ($dll+'.dll')),'synthetic',$utf8)}}
  [IO.File]::WriteAllText((Join-Path $directory ($assembly+'.deps.json')),(@{libraries=$libraries}|ConvertTo-Json -Depth 5),$utf8)
  $index.products[$name]=@{path=$directory;manifestSha256=''};Save-Product $f $name
 }
 Save-Index $f;return $f
}
function Validate($f){$null=Read-ExtensionPackageSet $workspace $f.path $sourceHash}
function Reject([scriptblock]$action,[string]$code){try{&$action}catch{if($_.Exception.Message -ceq $code){return};throw};throw "Expected $code"}
$cases=[ordered]@{
 'exact Editor Player shared module set'={Validate (New-Fixture)}
 'combined AI Editor has same-context Player peer'={Validate (New-Fixture $true)}
 'source fingerprint mismatch'={$f=New-Fixture;Reject {$null=Read-ExtensionPackageSet $workspace $f.path ('B'*64)} 'extension_index_identity'}
 'test flag cannot turn unaccepted index into evidence'={$f=New-Fixture;$f.index.tested=$true;Save-Index $f;Reject {Validate $f} 'extension_index_identity'}
 'extra product rejected'={$f=New-Fixture;$f.index.products['unexpected']=$f.index.products.editorExtensions;Save-Index $f;Reject {Validate $f} 'extension_index_products'}
 'matching manifest but different runtime component module rejected'={$f=New-Fixture;[IO.File]::WriteAllText((Join-Path $f.index.products.playerNullExtensions.path 'Ncma.Extensions.Hosting.dll'),'different');Save-Product $f 'playerNullExtensions';Reject {Validate $f} 'extension_editor_player_module_mismatch'}
 'default product rejects extension leakage'={$f=New-Fixture;Reject {Assert-AiCandidateDependencies $workspace $f.index.products.editorExtensions.path $false} 'extension_package_dependency'}
 'unmanifested synthetic configuration rejected without reading credentials'={$f=New-Fixture;[IO.File]::WriteAllText((Join-Path $f.index.products.editorExtensions.path 'synthetic.env'),'not-a-secret');Reject {Validate $f} 'extension_file_set'}
 'compile-only record never authorizes installation'={$f=New-Fixture;$path=Join-Path (Split-Path $f.path) 'unaccepted.json';[IO.File]::WriteAllText($path,(@{kind='compile-only';sourceInputsSha256=$sourceHash;accepted=$false;manualAcceptance=$false}|ConvertTo-Json),$utf8);Reject {$null=Assert-ExtensionTestReceipt $workspace $path $sourceHash $f.path} 'extension_receipt_unaccepted'}
}
$failed=[Collections.Generic.List[string]]::new()
foreach($case in $cases.GetEnumerator()){
 Write-Host "BEGIN extension package / $($case.Key)"
 try{&$case.Value;Write-Host "PASS extension package / $($case.Key)"}catch{$failed.Add($case.Key);Write-Host "FAIL extension package / $($case.Key): $($_.Exception)"}
}
Write-Host "SUMMARY extension package: total=$($cases.Count), failed=$($failed.Count); synthetic fixtures retained, never launched"
if($failed.Count){exit 1};exit 0
