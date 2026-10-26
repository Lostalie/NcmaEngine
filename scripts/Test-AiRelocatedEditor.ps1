[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateSet('Debug','Release')][string]$Configuration,
    [Parameter(Mandatory=$true)][ValidateSet('DefaultCpu','DefaultDx11','AiCpu','AiDx11','AiConfiguredCpu','AiConfiguredDx11')][string]$Case,
    [switch]$HostProbe, [string]$ProbeDll, [string]$DotnetExecutable)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'AiVerificationInputs.ps1')
$workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$output=Join-Path $workspace ('out/verification/m8-4/relocated-editor/'+$Configuration+'/'+[Guid]::NewGuid().ToString('N'))
$null=Assert-DeploymentPath $workspace $output
if(Test-Path -LiteralPath $output){throw 'Fresh evidence directory required'}
New-Item -ItemType Directory -Path $output|Out-Null
$pins=[Collections.Generic.List[IDisposable]]::new()
$child=$null;$failed=$false
# Bounded asynchronous stdout/stderr drain works under the actual Windows PS5 CTest host.
Add-Type -TypeDefinition @'
using System; using System.IO; using System.Text; using System.Threading.Tasks;
public static class NcmaAiBoundedOutput {
 public static async Task<string> Read(Stream input) {
  using(var buffer = new MemoryStream()) {
   var bytes = new byte[4096]; int count;
   while((count = await input.ReadAsync(bytes,0,bytes.Length).ConfigureAwait(false)) != 0) {
    if(buffer.Length + count > 262144) throw new InvalidDataException("apphost_output_budget");
    buffer.Write(bytes,0,count);
   }
   return new UTF8Encoding(false,true).GetString(buffer.ToArray());
  }
 }
}
'@
Write-Host "BEGIN relocated Editor / $Case"
try {
    $ai=$Case.StartsWith('Ai',[StringComparison]::Ordinal);$cpu=$Case.EndsWith('Cpu',[StringComparison]::Ordinal)
    $indexPath=Join-Path $workspace "out/verification/m8-4/$Configuration/packages.json"
    $sourceHash=Get-AiCandidateInputHash @(Get-AiCandidateSourceInputs $workspace)
    $entry=Get-AiCandidateIndexedProduct $workspace $indexPath $Configuration $ai $sourceHash
    $source=$entry.path
    Assert-AiCandidateManifest $workspace $source $Configuration $ai $sourceHash $entry.manifestSha256
    $manifest=Read-DeploymentManifest $source
    $moved=Join-Path $output ('relocated '+[char]0x5019+[char]0x9009+' '+$Case)
    $null=Assert-DeploymentPath $workspace $moved
    New-Item -ItemType Directory -Path $moved|Out-Null
    $files=@(Get-DeploymentFiles $workspace $source)
    foreach($file in $files){
        $target=Join-Path $moved $file.path
        $null=Assert-DeploymentPath $workspace $target
        New-Item -ItemType Directory -Path (Split-Path $target) -Force|Out-Null
        [IO.File]::Copy((Join-Path $source $file.path),$target,$false)
    }
    Assert-AiCandidateManifest $workspace $moved $Configuration $ai $sourceHash $entry.manifestSha256
    # Entire source package is unavailable during the actual relocated apphost run.
    foreach($file in $files){$pins.Add([IO.File]::Open((Join-Path $source $file.path),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None))}
    # A known repository interpreter, if installed, is unavailable too. This does NOT
    # claim no other Python installation exists on the machine.
    $python=Join-Path $workspace 'out/modules/python312/Scripts/python.exe'
    $null=Assert-DeploymentPath $workspace $python
    if(Test-Path -LiteralPath $python -PathType Leaf){$pins.Add([IO.File]::Open($python,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None))}
    $run=Join-Path $output 'environment';New-Item -ItemType Directory -Path $run|Out-Null
    $start=[Diagnostics.ProcessStartInfo]::new()
    $start.FileName=Join-Path $moved 'NcmaEngine.exe'
    $start.Arguments='--editor --project "'+(Join-Path $moved 'sample/sample.ncmaproject')+'" --frames 8'
    if($cpu){$start.Arguments+=' --cpu-only'}
    if($HostProbe){
        $verification=Assert-AiVerificationReceipt $workspace $Configuration $sourceHash
        $expectedProbe=[IO.Path]::GetFullPath((Join-Path $workspace ("out/build/windows-ninja-"+$Configuration.ToLowerInvariant()+"/m8-4-ai/bin/Ncma.Editor.Services.Tests/"+$Configuration.ToLowerInvariant()+"/Ncma.Editor.Services.Tests.dll")))
        if(-not [IO.Path]::IsPathRooted($ProbeDll) -or [IO.Path]::GetFullPath($ProbeDll) -cne $expectedProbe -or -not (Test-Path -LiteralPath $ProbeDll -PathType Leaf)){throw 'Exact current-configuration probe required'}
        if(-not [IO.Path]::IsPathRooted($DotnetExecutable) -or -not (Test-Path -LiteralPath $DotnetExecutable -PathType Leaf)){throw 'Explicit dotnet prerequisite required'}
        $request=Join-Path $output 'request.json'
        $requestJson=[ordered]@{productRoot=$moved;output=$output;cpuOnly=$cpu;ai=$ai;configured=$Case.Contains('Configured');manifestSha256=$entry.manifestSha256;verificationGeneration=$verification.generation;probeSha256=(Get-DeploymentHash $ProbeDll)}|ConvertTo-Json
        [IO.File]::WriteAllText($request,$requestJson,[Text.UTF8Encoding]::new($false))
        $start.FileName=$DotnetExecutable
        $start.Arguments='"'+$ProbeDll+'" --ai-relocated-product-child "'+$request+'"'
    } elseif($Case.Contains('Configured')){throw 'Configured lane requires trusted host probe'}
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true;$start.WorkingDirectory=$run
    $start.EnvironmentVariables.Clear()
    $windows=[Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)
    $start.EnvironmentVariables['SystemRoot']=$windows;$start.EnvironmentVariables['WINDIR']=$windows
    $start.EnvironmentVariables['PATH']=Join-Path $windows 'System32'
    foreach($name in @('TEMP','TMP','USERPROFILE','LOCALAPPDATA')){$start.EnvironmentVariables[$name]=$run}
    $child=[Diagnostics.Process]::new();$child.StartInfo=$start
    if(-not $child.Start()){throw 'Actual apphost failed to start'}
    [ordered]@{pid=$child.Id;startedUtc=$child.StartTime.ToUniversalTime().ToString('o');executable=$start.FileName;arguments=$start.Arguments}|
        ConvertTo-Json|Set-Content -LiteralPath "$output/process.json" -Encoding utf8
    $stdout=[NcmaAiBoundedOutput]::Read($child.StandardOutput.BaseStream)
    $stderr=[NcmaAiBoundedOutput]::Read($child.StandardError.BaseStream)
    if(-not $child.WaitForExit(30000)){
        # Preserve exact identity for explicit cleanup; never broaden process targets.
        throw 'Owned apphost timeout; process.json retained. Do not claim pass; resolve retained process before further execution.'
    }
    $text=$stdout.GetAwaiter().GetResult();$errors=$stderr.GetAwaiter().GetResult()
    [IO.File]::WriteAllText("$output/stdout.log",$text,[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText("$output/stderr.log",$errors,[Text.UTF8Encoding]::new($false))
    $expected=if($cpu){'candidate_cpu_gui frames=8; resources released'}else{'candidate_dx11_scene frames=8; resources released'}
    if($HostProbe){$expected='checked_relocated_editor edit_play_undo; resources released'}
    if($child.ExitCode -ne 0 -or -not $text.Contains($expected)){throw 'Relocated apphost did not complete exact frame/close contract'}
    foreach($file in $manifest.files){
        $path=Join-Path $moved $file.path
        if((Get-DeploymentHash $path) -cne $file.sha256){throw 'Published payload changed during apphost execution'}
    }
    [ordered]@{case=$Case;configuration=$Configuration;sourceInputsSha256=$sourceHash;manifestSha256=$entry.manifestSha256;
        actualRelocatedApphost=(-not $HostProbe);actualRelocatedProductHost=[bool]$HostProbe;sourceFilesLocked=$files.Count;minimalEnvironment=$true;noInferenceRequested=$true;
        osNetworkIsolationProven=$false;humanUiAcceptance=$false;relocatedEditPlayProven=[bool]$HostProbe;exitCode=$child.ExitCode}|
        ConvertTo-Json|Set-Content -LiteralPath "$output/result.json" -Encoding utf8
    Write-Host "PASS relocated Editor / $Case"
} catch {
    $failed=$true;Write-Host "FAIL relocated Editor / ${Case}: $($_.Exception.Message)"
    [IO.File]::WriteAllText("$output/failure.txt",$_.Exception.ToString(),[Text.UTF8Encoding]::new($false))
} finally {
    if($null -ne $child){$child.Dispose()}
    foreach($pin in $pins){$pin.Dispose()}
}
Write-Host "SUMMARY relocated Editor: total=1, failed=$([int]$failed); evidence=$output"
if($failed){exit 1};exit 0
