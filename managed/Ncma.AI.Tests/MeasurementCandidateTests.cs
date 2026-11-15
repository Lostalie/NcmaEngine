using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.Application;
using Ncma.Editor.Services;

internal static class MeasurementCandidateTests
{
    private static void Check(bool value){if(!value)throw new Exception("Candidate measurement assertion");}
    private static void Reject(Action action){try{action();}catch(Exception e)when(e is ArgumentException or IOException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException){return;}throw new Exception("Candidate measurement must reject");}
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    private static (MeasurementCandidateRequest Request,MeasurementInputsTests.Fixture Fixture) Create()
    {
        var f=new MeasurementInputsTests.Fixture();string idle=Path.Combine(f.Root,"idle");Directory.CreateDirectory(idle);
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(new{schemaVersion=1,kind="ncma.measurement-workload.v1",scenario="editor-idle",files=Array.Empty<object>()});
        File.WriteAllBytes(Path.Combine(idle,MeasurementInputLease.WorkloadManifestName),bytes);
        return(new(1,f.Identity with{WorkloadSha256=Hash(bytes)},3000,1000,3,2,f.Capture,f.Product,idle),f);
    }
    private sealed class Clock:IFrameClock {public double Seconds=>0;}
    private sealed class Pipeline:IFramePipeline {
        public bool PumpPlatform()=>true;public void ApplyEditorIntents(FrameContext f){}public void PumpAgent(FrameContext f){}
        public void BeginPresentation(FrameContext f){}public void SubmitInput(FrameContext f){}public void AdvancePlay(FrameContext f){}public void Present(FrameContext f){}
    }
    private static MeasurementCandidateRecord Record()
    {
        var (request,f)=Create();using var inputs=request.OpenInputs();var receipt=inputs.Prepare();
        using var host=new ApplicationLifetime();host.Start([]);var driver=new MeasurementRunDriver(request.Plan(),inputs);
        var status=driver.RunAndClose(host,new Clock(),new Pipeline());
        // Synthetic record shape, NOT a successful BindCurrentEditor or independent apphost run.
        return new(1,"cpu-idle",Hash(MeasurementCandidateRequest.Encode(request)),request.Identity,3000,1000,3,2,
            request.Plan().Fingerprint,receipt,new(Environment.ProcessId,new('A',64),new('B',64)),status,
            ProcessMeasurementEvidence.Encode(driver.ProcessReport!),null);
    }
    internal static IEnumerable<(string,Action)> Cases()
    {
        yield return("M8.7-E2E3 request canonical roundtrip retains explicit plan",()=>{
            var (request,_)=Create();byte[] bytes=MeasurementCandidateRequest.Encode(request);
            Check(MeasurementCandidateRequest.Decode(bytes)==request);Check(request.Plan().MaximumFrames==2);
            foreach(var bad in new[]{request with{Version=2},request with{Capacity=1},request with{ProductDirectory="relative"},request with{Identity=request.Identity with{Host=MeasurementHost.Player}}})Reject(()=>MeasurementCandidateRequest.Encode(bad));
        });
        yield return("M8.7-E2E3 request rejects malformed duplicate unknown noncanonical bytes",()=>{
            var (r,_)=Create();string json=Encoding.UTF8.GetString(MeasurementCandidateRequest.Encode(r));
            foreach(string bad in new[]{"null","{}",json+" ",json.Replace("\"Version\":1","\"Version\":1,\"Version\":1"),json.Replace("\"Version\":1","\"Unknown\":0,\"Version\":1"),"{\"Version\":\"SECRET"})Reject(()=>MeasurementCandidateRequest.Decode(Encoding.UTF8.GetBytes(bad)));
            Reject(()=>MeasurementCandidateRequest.Decode(new byte[MeasurementCandidateRequest.MaximumBytes+1]));
        });
        yield return("M8.7-E2E3 request pinned read requires external exact hash",()=>{
            var (request,f)=Create();byte[] bytes=MeasurementCandidateRequest.Encode(request);string path=Path.Combine(f.Root,"request.json");File.WriteAllBytes(path,bytes);
            Check(MeasurementCandidateRequest.Read(path,Hash(bytes))==request);Reject(()=>MeasurementCandidateRequest.Read(path,new('0',64)));
            Check(File.ReadAllBytes(path).SequenceEqual(bytes));
        });
        yield return("M8.7-E2E3 output preflight is readonly and rejects input overlap or overwrite",()=>{
            var (request,f)=Create();string path=Path.Combine(Path.GetTempPath(),"ncma-measurement-output-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);path=Path.Combine(path,"run.ncobserve");
            request.ValidateOutput(path);Check(!File.Exists(path));File.WriteAllText(path,"old evidence");Reject(()=>request.ValidateOutput(path));Check(File.ReadAllText(path)=="old evidence");
            foreach(string root in new[]{request.ProductDirectory,request.WorkloadDirectory,Path.GetDirectoryName(request.CaptureManifestPath)!})Reject(()=>request.ValidateOutput(Path.Combine(root,"new.ncobserve")));
            Reject(()=>DiagnosticEvidenceFiles.ValidateNewDestination(Path.Combine(f.Root,"bad.txt")));
        });
        yield return("M8.7-E2E3 independent current editor binding rejects harness without launching fake exe",()=>{
            var (request,_)=Create();using var inputs=request.OpenInputs();inputs.Prepare();
            Reject(()=>inputs.BindCurrentEditor());Check(inputs.State==MeasurementInputsState.Ready); // No fallback to dotnet DLL or test executable.
        });
        yield return("M8.7-E2E3 evidence roundtrip numeric fields and decoded process bind",()=>{
            var record=Record();byte[] bytes=MeasurementCandidateEvidence.Encode(record);var read=MeasurementCandidateEvidence.Decode(bytes);
            Check(read.Mode=="cpu-idle"&&read.Run!.CompletedFrames==2&&read.Run.InputsCheckedBeforeRun&&read.Run.InputsCheckedAfterRun&&!read.PerformanceAccepted&&!read.RepresentativeGameplay&&!read.GrantsExecutionAuthority);
            Check(ProcessMeasurementEvidence.DecodeExpected(read.ProcessEvidence!,read.Identity).CompletedIterations==2);
            read.ProcessEvidence![0]=0;Check(MeasurementCandidateEvidence.Decode(bytes).ProcessEvidence![0]!=(byte)0);
            Check(!Encoding.UTF8.GetString(bytes).Contains("CaptureManifestPath"));
        });
        yield return("M8.7-E2E3 evidence rejects plan process binding and declared gameplay mismatch",()=>{
            var r=Record();foreach(var bad in new[]{r with{Version=2},r with{Mode="open-world-exploration"},r with{Capacity=4},r with{PlanFingerprint=new('0',64)},r with{Entry=r.Entry with{ProcessId=r.Entry.ProcessId+1}},r with{Inputs=r.Inputs with{WorkloadFiles=1}},r with{Run=r.Run! with{CompletedFrames=1}}})Reject(()=>MeasurementCandidateEvidence.Encode(bad));
        });
        yield return("M8.7-E2E3 evidence rejects closed success without both input checks",()=>{
            var r=Record();string json=Encoding.UTF8.GetString(MeasurementCandidateEvidence.Encode(r));
            foreach(var (before,after) in new[]{(false,true),(true,false),(false,false)}) {
                var bad=r with{Run=r.Run! with{InputsCheckedBeforeRun=before,InputsCheckedAfterRun=after}};
                Reject(()=>MeasurementCandidateEvidence.Encode(bad));
                string forged=json.Replace("\"InputsCheckedBeforeRun\":true","\"InputsCheckedBeforeRun\":"+(before?"true":"false"))
                    .Replace("\"InputsCheckedAfterRun\":true","\"InputsCheckedAfterRun\":"+(after?"true":"false"));
                Check(forged!=json);Reject(()=>MeasurementCandidateEvidence.Decode(Encoding.UTF8.GetBytes(forged)));
            }
        });
        yield return("M8.7-E2E3 evidence preserves explicit post input check failure after close",()=>{
            var r=Record();var failedRun=r.Run! with{InputsCheckedAfterRun=false,FailureType=nameof(IOException)};
            foreach(var failure in new[]{r with{Run=failedRun},r with{Run=failedRun,FailureType=nameof(IOException)},
                r with{Run=failedRun with{FailureType=null},FailureType=nameof(IOException)}}) {
                var read=MeasurementCandidateEvidence.Decode(MeasurementCandidateEvidence.Encode(failure));
                Check(read.Run==failure.Run&&read.Run is {State:MeasurementRunState.Closed,InputsCheckedBeforeRun:true,InputsCheckedAfterRun:false}&&
                    read.FailureType==failure.FailureType&&read.ProcessEvidence!.SequenceEqual(failure.ProcessEvidence!));
            }
        });
        yield return("M8.7-E2E3 evidence preserves startup failure with missing sampling not fabricated success",()=>{
            var r=Record() with{Run=null,ProcessEvidence=null,FailureType="IOException"};var copy=MeasurementCandidateEvidence.Decode(MeasurementCandidateEvidence.Encode(r));
            Check(copy.Run is null&&copy.ProcessEvidence is null&&copy.FailureType=="IOException");Reject(()=>MeasurementCandidateEvidence.Encode(r with{FailureType=null}));
            Reject(()=>MeasurementCandidateEvidence.Encode(r with{FailureType="SECRET/path"}));
        });
        yield return("M8.7-E2E3 evidence rejects contradictory retention and malformed bytes",()=>{
            var r=Record();Reject(()=>MeasurementCandidateEvidence.Encode(r with{Run=r.Run! with{State=MeasurementRunState.Closed,InputsRetained=true}}));
            Reject(()=>MeasurementCandidateEvidence.Encode(r with{Run=r.Run! with{State=MeasurementRunState.Retained}}));
            byte[] data=MeasurementCandidateEvidence.Encode(r);string json=Encoding.UTF8.GetString(data);
            foreach(string bad in new[]{"[]","null","{}",json+" ",json.Replace("\"PerformanceAccepted\":false","\"PerformanceAccepted\":true"),json.Replace("\"Version\":1","\"Version\":1,\"Version\":1")})Reject(()=>MeasurementCandidateEvidence.Decode(Encoding.UTF8.GetBytes(bad)));
            Reject(()=>MeasurementCandidateEvidence.Decode(new byte[MeasurementCandidateEvidence.MaximumBytes+1]));
        });
        yield return("M8.7-E2E3 evidence export uses original create new readback without cleanup",()=>{
            var r=Record();string root=Path.Combine(Path.GetTempPath(),"ncma-candidate-evidence-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            string path=Path.Combine(root,"run.ncobserve");var receipt=MeasurementCandidateEvidence.ExportNew(path,r);byte[] bytes=File.ReadAllBytes(path);
            Check(receipt.Sha256==Hash(bytes)&&receipt.Bytes==bytes.Length);_=MeasurementCandidateEvidence.Decode(bytes);
            Reject(()=>MeasurementCandidateEvidence.ExportNew(path,r));Check(File.ReadAllBytes(path).SequenceEqual(bytes));
        });
    }
}
