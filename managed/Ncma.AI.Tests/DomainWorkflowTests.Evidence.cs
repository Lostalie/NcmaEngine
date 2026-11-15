using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.AI;
using Ncma.Application;
using Ncma.Editor.Modules;
using Ncma.Modules;

internal static partial class DomainWorkflowTests
{
    private static string EvidenceHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    // Explicit pre-reader v1 numeric projection: adding the reader must not silently change the wire shape.
    private static byte[] V1Projection(AiWorkflowObservationReport report) => JsonSerializer.SerializeToUtf8Bytes(new {
        Version=1, report.Identity, report.Intent, report.StartedTimestamp, report.EndedTimestamp, report.RequestCapacity,
        report.EventCapacity, report.DroppedRequests, report.DroppedEvents, report.ObservationFailures, report.Requests, report.Events,
        Transport=new { report.Transport.StartedTimestamp, report.Transport.EndedTimestamp, report.Transport.Capacity,
            report.Transport.Invoked, report.Transport.Completed, report.Transport.Pending, report.Transport.Dropped, report.Transport.BoundaryOverlaps,
            Samples=report.Transport.Samples.Select(s=>new { s.CompletionSequence,s.InvocationSequence,s.Request,s.StartedTimestamp,s.CompletedTimestamp,
                s.AdapterElapsedTicks,s.OwnedActiveSlot,s.Diagnostic.HttpStatus,PreparedRequestBytes=s.Diagnostic.RequestBytes,ReadResponseBytes=s.Diagnostic.ResponseBytes,s.Diagnostic.Usage }).ToArray() },
        Summary=AiWorkflowObservationEvidence.Inspect(report)
    });
    private static ProcessMeasurementIdentity ProcessLabels(ObservationIdentity identity) => new(identity.RunId,identity.SourceSha256,new('C',64),new('D',64),
        identity.WorkloadSha256,MeasurementConfiguration.Release,MeasurementVariant.Ai,MeasurementHost.Editor);
    private static byte[] EmptyProcessEvidence(ProcessMeasurementIdentity id)
    {
        // Synthetic unobserved window; deliberately different clock frequency, not a claim of real measurement.
        var series=new ProcessSeriesReport(1,id.RunId,1,1,1000,0,0,1000,1000,1,1,1,0,ProcessSeriesStop.ExplicitSeal,[]);
        return ProcessMeasurementEvidence.Encode(new(1,id,series,false,0,0,0,0,0));
    }
    private static IEnumerable<(string,Action)> EvidenceCases()
    {
        yield return ("M8.7-E2D workflow v1 projection parity strict roundtrip and omitted diagnostics",()=>{
            var report=ObservedSuccess();var sample=report.Transport.Samples[0];
            report=report with{Transport=report.Transport with{Samples=[sample with{Diagnostic=sample.Diagnostic with{Code="SECRET_BODY",ExceptionType="SECRET_KEY"}}]}};
            byte[] bytes=AiWorkflowObservationEvidence.Encode(report);Check(bytes.SequenceEqual(V1Projection(report)));
            var document=AiWorkflowObservationEvidence.DecodeExpected(bytes,report.Identity,report.Intent);
            Check(AiWorkflowObservationEvidence.EncodeDocument(document).SequenceEqual(bytes)&&document.Summary.ReceiptBackedSuccesses==1);
            string text=Encoding.UTF8.GetString(bytes);Check(!text.Contains("SECRET")&&!text.Contains("ExceptionType")&&!text.Contains("ElapsedMilliseconds"));
        });
        yield return ("M8.7-E2D expected workflow identity and intent cannot be substituted",()=>{
            var report=ObservedSuccess();byte[] bytes=AiWorkflowObservationEvidence.Encode(report);
            foreach(var id in new[]{report.Identity with{RunId=Guid.NewGuid()},report.Identity with{SourceSha256=new('E',64)},report.Identity with{WorkloadSha256=new('E',64)},
                report.Identity with{Configuration=ObservationConfiguration.Debug},report.Identity with{Variant=ObservationVariant.AiExtensions},report.Identity with{TimestampFrequency=report.Identity.TimestampFrequency+1}})
                Reject(()=>AiWorkflowObservationEvidence.DecodeExpected(bytes,id,report.Intent));
            Reject(()=>AiWorkflowObservationEvidence.DecodeExpected(bytes,report.Identity,AiAuthoringIntent.UiDocument));
        });
        yield return ("M8.7-E2D copied documents and nested module replay reject tampering",()=>{
            var report=ObservedSuccess();byte[] bytes=AiWorkflowObservationEvidence.Encode(report);
            var a=AiWorkflowObservationEvidence.Decode(bytes);var b=AiWorkflowObservationEvidence.Decode(bytes);
            a.Requests[0]=a.Requests[0] with{Module=a.Requests[0].Module! with{EvidenceSha256=new('0',64)}};
            Reject(()=>AiWorkflowObservationEvidence.EncodeDocument(a));Check(AiWorkflowObservationEvidence.EncodeDocument(b).SequenceEqual(bytes));
            Reject(()=>AiWorkflowObservationEvidence.EncodeDocument(b with{Summary=b.Summary with{ReceiptBackedSuccesses=0}}));
            Reject(()=>AiWorkflowObservationEvidence.EncodeDocument(b with{Events=[b.Events[0],b.Events[0]]}));
            Reject(()=>AiWorkflowObservationEvidence.EncodeDocument(b with{Requests=[b.Requests[0],b.Requests[0]]}));
        });
        yield return ("M8.7-E2D numeric transport retains original sequence usage and join validation",()=>{
            var d=AiWorkflowObservationEvidence.Decode(AiWorkflowObservationEvidence.Encode(ObservedSuccess()));var row=d.Transport.Samples[0];
            foreach(var invalid in new[]{row with{CompletionSequence=2},row with{InvocationSequence=0},row with{PreparedRequestBytes=-1},row with{HttpStatus=99},
                row with{Usage=new AiTokenUsage(1,2,4)},row with{Request=row.Request with{SessionId=Guid.NewGuid()}},row with{AdapterElapsedTicks=-1}})
                Reject(()=>AiWorkflowObservationEvidence.EncodeDocument(d with{Transport=d.Transport with{Samples=[invalid]}}));
            Reject(()=>AiWorkflowObservationEvidence.EncodeDocument(d with{Transport=d.Transport with{Pending=1}}));
        });
        yield return ("M8.7-E2D malformed duplicate unknown derived and noncanonical JSON rejected",()=>{
            byte[] bytes=AiWorkflowObservationEvidence.Encode(ObservedSuccess());string json=Encoding.UTF8.GetString(bytes);
            foreach(string invalid in new[]{"", "null", "{}",json+" ",json.Replace("\"Version\":1","\"Version\":2"),
                json.Replace("\"Version\":1","\"Version\":1,\"Version\":1"),json.Replace("\"Version\":1","\"Version\":1,\"SECRET_FIELD\":0"),
                json.Replace("\"PerformanceAccepted\":false","\"PerformanceAccepted\":true")}) Reject(()=>AiWorkflowObservationEvidence.Decode(Encoding.UTF8.GetBytes(invalid)));
            Reject(()=>AiWorkflowObservationEvidence.Decode([0xFF]));
            try { AiWorkflowObservationEvidence.Decode(Encoding.UTF8.GetBytes("{SECRET_BODY"));throw new Exception("Expected malformed JSON rejection"); }
            catch(ArgumentException error){Check(!error.Message.Contains("SECRET"));}
        });
        yield return ("M8.7-E2D preflight bounds every nested array and scalar before typed decode",()=>{
            string Many(int count)=>string.Join(',',Enumerable.Repeat("{}",count));
            foreach(string invalid in new[]{"{\"Requests\":["+Many(257)+"]}","{\"Events\":["+Many(4097)+"]}",
                "{\"Transport\":{\"Samples\":["+Many(4097)+"]}}",
                "{\"Requests\":[{\"Module\":{\"Events\":["+Many(33)+"]}}]}",
                "{\"Requests\":[{\"Module\":{\"Latencies\":["+Many(43)+"]}}]}",
                "{\"Requests\":[1]}","{\"Other\":[]}","{\"X\":\""+new string('x',513)+"\"}",new string('[',14)+new string(']',14)})
                Reject(()=>AiWorkflowObservationEvidence.Decode(Encoding.UTF8.GetBytes(invalid)));
            byte[] bytes=AiWorkflowObservationEvidence.Encode(ObservedSuccess());
            Reject(()=>AiWorkflowObservationEvidence.Decode(bytes,bytes.Length-1));Reject(()=>AiWorkflowObservationEvidence.Decode(bytes,AiWorkflowObservationEvidence.MaximumBytes+1));
            Reject(()=>AiWorkflowObservationEvidence.EncodeDocument(AiWorkflowObservationEvidence.Decode(bytes),1));
        });
        yield return ("M8.7-E2D incomplete pending and truncated scopes remain incomplete after import",()=>{
            using(var f=new Fixture()) {
                f.Transport.Hold=new(TaskCreationOptions.RunContinuationsAsynchronously);f.Workflow.BeginObservation(WorkflowIdentity());f.Start();f.Workflow.Pump();Until(()=>Volatile.Read(ref f.Transport.Calls)==1);
                var d=AiWorkflowObservationEvidence.Decode(AiWorkflowObservationEvidence.Encode(f.Workflow.SealObservation()));
                Check(d.Transport.Pending==1&&d.Summary.PendingModuleRequests==1&&!d.Summary.CompleteCapturedScope);
            }
            using(var f=new Fixture()) {
                f.Workflow.BeginObservation(WorkflowIdentity(),eventCapacity:1);f.Start();f.Complete();var review=f.Workflow.Review!;Check(f.Workflow.Confirm(review,review.Fingerprint,true).Status=="ok");Until(f.Workflow.TryUnregister);
                var d=AiWorkflowObservationEvidence.Decode(AiWorkflowObservationEvidence.Encode(f.Workflow.SealObservation()));
                Check(d.DroppedEvents==1&&!d.Summary.CompleteCapturedScope&&d.Summary.ReceiptBackedSuccesses==0);
            }
        });
        yield return ("M8.7-E2D byte-pinned process association is labels only not common clock or performance",()=>{
            var report=ObservedSuccess();var id=ProcessLabels(report.Identity);byte[] w=AiWorkflowObservationEvidence.Encode(report),p=EmptyProcessEvidence(id);
            var association=WorkflowProcessAssociation.Inspect(w,p,report.Identity,report.Intent,id,EvidenceHash(w),EvidenceHash(p));
            Check(association.Workflow.ReceiptBackedSuccesses==1&&!association.ProcessBoundToLoop&&!association.ProcessCadenceComplete&&!association.ProcessCountersAvailable);
            Check(!association.CommonClockVerified&&!association.ProvenanceVerified&&!association.PerformanceAccepted&&association.RunId==id.RunId);
        });
        yield return ("M8.7-E2D association rejects independently valid but mismatched run source workload mode",()=>{
            var report=ObservedSuccess();var id=ProcessLabels(report.Identity);byte[] w=AiWorkflowObservationEvidence.Encode(report);
            foreach(var invalid in new[]{id with{RunId=Guid.NewGuid()},id with{SourceSha256=new('E',64)},id with{WorkloadSha256=new('E',64)},
                id with{Configuration=MeasurementConfiguration.Debug},id with{Variant=MeasurementVariant.AiExtensions},id with{Host=MeasurementHost.Player}}) {
                byte[] p=EmptyProcessEvidence(invalid);Reject(()=>WorkflowProcessAssociation.Inspect(w,p,report.Identity,report.Intent,invalid,EvidenceHash(w),EvidenceHash(p)));
            }
        });
        yield return ("M8.7-E2D independent expected bytes product and capture labels required",()=>{
            var report=ObservedSuccess();var id=ProcessLabels(report.Identity);byte[] w=AiWorkflowObservationEvidence.Encode(report),p=EmptyProcessEvidence(id);
            Reject(()=>WorkflowProcessAssociation.Inspect(w,p,report.Identity,report.Intent,id,new('0',64),EvidenceHash(p)));
            Reject(()=>WorkflowProcessAssociation.Inspect(w,p,report.Identity,report.Intent,id,EvidenceHash(w),new('0',64)));
            foreach(var invalid in new[]{id with{ProductManifestSha256=new('E',64)},id with{CaptureManifestSha256=new('E',64)}})
                Reject(()=>WorkflowProcessAssociation.Inspect(w,p,report.Identity,report.Intent,invalid,EvidenceHash(w),EvidenceHash(p)));
        });
    }
}
