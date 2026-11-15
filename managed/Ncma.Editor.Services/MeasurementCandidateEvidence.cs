using System.Text.Json;
using Ncma.Application;

namespace Ncma.Editor.Services;

public sealed record MeasurementCandidateRecord(int Version,string Mode,string RequestSha256,ProcessMeasurementIdentity Identity,
    int DurationMilliseconds,int IntervalMilliseconds,int Capacity,ulong MaximumFrames,string PlanFingerprint,
    MeasurementInputsReceipt Inputs,MeasurementEntryBinding Entry,MeasurementRunStatus? Run,byte[]? ProcessEvidence,string? FailureType)
{
    public bool PerformanceAccepted => false;
    public bool RepresentativeGameplay => false;
    public bool GrantsExecutionAuthority => false;
}

// CPU-idle evidence only. No request paths, scene data, credentials or executable approval token.
public static class MeasurementCandidateEvidence
{
    public const int MaximumBytes=24*1024*1024;
    private static void Require(bool value) { if(!value)throw new ArgumentException("measurement_candidate_evidence"); }
    private static MeasurementCandidateRecord Validate(MeasurementCandidateRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);ArgumentNullException.ThrowIfNull(record.Inputs);ArgumentNullException.ThrowIfNull(record.Entry);
        var copy=record with{ProcessEvidence=record.ProcessEvidence?.ToArray()};
        var plan=new MeasurementInputsPlan(copy.Identity,copy.DurationMilliseconds,copy.IntervalMilliseconds,copy.Capacity,copy.MaximumFrames);
        Require(copy.Version==1&&copy.Mode=="cpu-idle"&&copy.PlanFingerprint==plan.Fingerprint);
        WorkflowEvidenceRules.Hash(copy.RequestSha256);WorkflowEvidenceRules.Hash(copy.Entry.ExecutableSha256);WorkflowEvidenceRules.Hash(copy.Entry.EntryAssemblySha256);
        Require(copy.Entry.ProcessId>0&&copy.Inputs.Identity==copy.Identity&&copy.Inputs.PlanFingerprint==plan.Fingerprint&&copy.Inputs.CheckSequence>0&&
            copy.Inputs.Scenario==MeasurementScenario.EditorIdle&&copy.Inputs.WorkloadFiles==0&&copy.Inputs.ProductFiles is >=4 and <=1024&&copy.Inputs.PayloadBytes is >0 and <=MeasurementInputLease.MaximumPayloadBytes);
        WorkflowEvidenceRules.Hash(copy.Inputs.ProductFilesSha256);WorkflowEvidenceRules.Hash(copy.Inputs.WorkloadFilesSha256);
        static bool TypeName(string? name)=>name is null || name.Length is >0 and <=128 && name.All(c=>char.IsAsciiLetterOrDigit(c)||c=='_');
        Require(TypeName(copy.FailureType));
        if(copy.Run is {} run) {
            Require(Enum.IsDefined(run.State)&&Enum.IsDefined(run.End)&&run.State is MeasurementRunState.Closed or MeasurementRunState.Retained or MeasurementRunState.Rejected&&
                run.CompletedFrames<=plan.MaximumFrames&&run.CloseAttempts>=0&&TypeName(run.FailureType));
            Require(run.State!=MeasurementRunState.Closed||!run.ApplicationRetained&&!run.InputsRetained);
            Require(run.State!=MeasurementRunState.Retained||run.ApplicationRetained||run.InputsRetained);
            Require(run.End!=MeasurementRunEnd.Frames||run.CompletedFrames==plan.MaximumFrames);
            // Closed describes cleanup only; unchecked inputs must remain explicit failure evidence.
            Require(copy.FailureType is not null||run.FailureType is not null||run.InputsCheckedBeforeRun&&run.InputsCheckedAfterRun);
            if(copy.ProcessEvidence is {} data) {
                var process=ProcessMeasurementEvidence.DecodeExpected(data,copy.Identity);
                Require(process.Series.ProcessId==copy.Entry.ProcessId&&process.CompletedIterations==(long)run.CompletedFrames&&
                    process.Series.Capacity==plan.Capacity&&process.Series.DurationTicks==(Int128)process.Series.TimestampFrequency*plan.DurationMilliseconds/1000&&
                    process.Series.IntervalTicks==((Int128)process.Series.TimestampFrequency*plan.IntervalMilliseconds+999)/1000);
            } else Require(copy.FailureType is not null || run.FailureType is not null);
        } else Require(copy.ProcessEvidence is null&&copy.FailureType is not null);
        return copy;
    }
    public static byte[] Encode(MeasurementCandidateRecord record)
    {
        if(record?.ProcessEvidence?.Length>ProcessSeriesEvidence.MaximumBytes)throw new ArgumentException("measurement_candidate_budget");
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(Validate(record!),MeasurementCandidateRequest.JsonOptions);
        if(bytes.Length>MaximumBytes)throw new ArgumentException("measurement_candidate_budget");return bytes;
    }
    public static MeasurementCandidateRecord Decode(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length is <1 or >MaximumBytes)throw new ArgumentException("measurement_candidate_budget");
        byte[] owned=bytes.ToArray();
        try {
            var reader=new Utf8JsonReader(owned,new JsonReaderOptions{MaxDepth=8});int tokens=0;string? field=null;
            while(reader.Read()) {
                if(++tokens>512||reader.TokenType==JsonTokenType.StartArray)throw new ArgumentException("measurement_candidate_shape");
                if(reader.TokenType==JsonTokenType.PropertyName){if(reader.ValueSpan.Length>128)throw new ArgumentException("measurement_candidate_shape");field=reader.GetString();}
                else if(reader.TokenType==JsonTokenType.String) {
                    int bound=field=="ProcessEvidence"&&reader.CurrentDepth==1 ? (ProcessSeriesEvidence.MaximumBytes+2)/3*4 : 1024;
                    if(reader.ValueSpan.Length>bound)throw new ArgumentException("measurement_candidate_scalar");field=null;
                }
            }
            var record=JsonSerializer.Deserialize<MeasurementCandidateRecord>(owned,MeasurementCandidateRequest.JsonOptions)??throw new ArgumentException("measurement_candidate_null");
            if(!owned.AsSpan().SequenceEqual(Encode(record)))throw new ArgumentException("measurement_candidate_noncanonical");
            return Validate(record);
        } catch(JsonException){throw new ArgumentException("measurement_candidate_json");}
    }
    public static DiagnosticEvidenceFileReceipt ExportNew(string destination,MeasurementCandidateRecord record)
        => DiagnosticEvidenceFiles.WriteNew(destination,Encode(record));
}
