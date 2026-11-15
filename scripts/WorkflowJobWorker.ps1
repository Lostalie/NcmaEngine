# Fixed private transport worker. Parent owns the Windows Job before this process
# starts. All SDK descendants inherit it. No kill, shell, deployment or retry.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$PipeName)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
if($PipeName -cnotmatch '^ncma-workflow-job-[a-f0-9]{32}$'){throw 'workflow_job_pipe_name'}
$pipe=[IO.Pipes.NamedPipeClientStream]::new('.', $PipeName, [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::None)
$source=@'
using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
public static class NcmaWorkflowJobTransport {
 public static int Run(Stream pipe, string tool, string arguments, string working, string cli, string temp) {
  object gate=new object(); bool broken=false; int overflow=0; long output=0,error=0;
  Action<string> send=delegate(string text) {
   lock(gate) { if(broken)return; try {
    byte[] bytes=Encoding.UTF8.GetBytes(text); byte[] header=BitConverter.GetBytes(bytes.Length);
    pipe.Write(header,0,4);pipe.Write(bytes,0,bytes.Length);pipe.Flush();
   } catch { broken=true; } }
  };
  ProcessStartInfo start=new ProcessStartInfo(tool,arguments);
  start.UseShellExecute=false;start.CreateNoWindow=true;start.WorkingDirectory=working;
  start.RedirectStandardOutput=true;start.RedirectStandardError=true;start.RedirectStandardInput=true;
  start.EnvironmentVariables.Clear();
  string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
  start.EnvironmentVariables["SystemRoot"]=windows;start.EnvironmentVariables["WINDIR"]=windows;
  start.EnvironmentVariables["SystemDrive"]=Path.GetPathRoot(windows).Substring(0,2);
  start.EnvironmentVariables["PATH"]=Path.GetDirectoryName(tool)+";"+Path.Combine(windows,"System32");
  start.EnvironmentVariables["TEMP"]=temp;start.EnvironmentVariables["TMP"]=temp;
  start.EnvironmentVariables["DOTNET_CLI_HOME"]=cli;start.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"]="1";
  start.EnvironmentVariables["DOTNET_NOLOGO"]="1";start.EnvironmentVariables["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"]="true";
  start.EnvironmentVariables["MSBUILDDISABLENODEREUSE"]="1";
  start.EnvironmentVariables["DOTNET_CLI_UI_LANGUAGE"]="en-US";
  start.EnvironmentVariables["NUGET_CERT_REVOCATION_MODE"]="offline";
  using(Process process=new Process()) {
   process.StartInfo=start;
   if(!process.Start())throw new IOException("workflow_sdk_launch_failed");
   process.StandardInput.Close();send("{\"kind\":\"started\"}");
   Func<Stream,bool,Task> drain=delegate(Stream input,bool stderr) { return Task.Run(delegate {
    byte[] bytes=new byte[8192];int count;
    while((count=input.Read(bytes,0,bytes.Length))!=0) {
     long total=stderr?Interlocked.Add(ref error,count):Interlocked.Add(ref output,count);
     if(total>4194304) { if(Interlocked.Exchange(ref overflow,1)==0)send("{\"kind\":\"log-overflow\"}");continue; }
     send("{\"kind\":\""+(stderr?"stderr":"stdout")+"\",\"data\":\""+Convert.ToBase64String(bytes,0,count)+"\"}");
    }
   }); };
   Task stdout=drain(process.StandardOutput.BaseStream,false),stderrTask=drain(process.StandardError.BaseStream,true);
   Task completion=Task.Run(delegate {
    process.WaitForExit();try {Task.WaitAll(stdout,stderrTask);}catch {send("{\"kind\":\"transport-failed\"}");}
    send("{\"kind\":\"exit\",\"exitCode\":"+process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");
   });
   try {
    // Single-byte control channel: 1 requests ordinary close, 2 acknowledges exit.
    // SDK CLI may not support ordinary close. Report that fact, never force kill.
    int control;
    while((control=pipe.ReadByte())>=0) {
     if(control==1) { if(!process.HasExited&&!process.CloseMainWindow())send("{\"kind\":\"stop-unsupported\"}"); }
     else if(control==2) { if(!process.HasExited||!stdout.IsCompleted||!stderrTask.IsCompleted)throw new IOException("workflow_close_before_sdk_exit");break; }
     else throw new IOException("workflow_job_control");
    }
   } finally {
    // Disconnect is NOT child exit. Keep this worker/Job owner until all borrowers
    // exit naturally and both redirected streams drain, including failure paths.
    lock(gate)broken=true;
    process.WaitForExit();Task.WaitAll(stdout,stderrTask,completion);
   }
   return process.ExitCode;
  }
 }
}
'@
try {
 Add-Type -TypeDefinition $source -Language CSharp
 $pipe.Connect(30000)
 $reader=[IO.BinaryReader]::new($pipe,[Text.Encoding]::UTF8,$true)
 $length=$reader.ReadInt32();if($length -lt 2 -or $length -gt 32768){throw 'workflow_job_request_budget'}
 $bytes=$reader.ReadBytes($length);if($bytes.Length -ne $length){throw 'workflow_job_request_eof'}
 $r=[Text.UTF8Encoding]::new($false,$true).GetString($bytes)|ConvertFrom-Json
 if($r.kind -cne 'run' -or $r.nonce -cne $PipeName.Substring(18) -or [IO.Path]::GetFileName($r.tool) -cne 'dotnet.exe') {throw 'workflow_job_request_identity'}
 if($r.arguments.Length -gt 30000 -or -not [IO.Path]::IsPathRooted($r.working)){throw 'workflow_job_request_shape'}
 exit [NcmaWorkflowJobTransport]::Run($pipe,$r.tool,$r.arguments,$r.working,$r.cli,$r.temp)
}finally{$pipe.Dispose()}
