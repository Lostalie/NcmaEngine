using System.Text.Json;
using System.Text.Json.Serialization;
using Ncma.Application;
using Ncma.Editor.Services;

namespace Ncma.Editor.Modules;

public sealed record AiSceneCandidateRecord(int Version, string RequestSha256, string SourceSceneSha256,
    string PolicySha256, MeasurementInputsReceipt Inputs, MeasurementEntryBinding Entry,
    MeasurementRunStatus? Run, AiMeasurementBudgetStatus? Budget, string? InitialSceneSha256,
    string? FinalSceneSha256, bool ApplicationRetained, bool InputsRetained, byte[]? JointEvidence, string? FailureType)
{
    public bool PerformanceAccepted => false;
    public bool RepresentativeGameplay => false;
    public bool GrantsExecutionAuthority => false;
    public bool ProvenanceVerified => false;
}

// Numeric/hash closure around the original joint consumer; no paths, scene content, endpoint or key.
public static class AiSceneCandidateEvidence
{
    public const int MaximumBytes = 56 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 8, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static void Require(bool value) { if (!value) throw new ArgumentException("ai_scene_candidate_evidence"); }
    private static bool Hash(string? text) => text is { Length: 64 } && text.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static bool Type(string? text) => text is null || text.Length is > 0 and <= 128 && text.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    private static AiSceneCandidateRecord Validate(AiSceneCandidateRecord record)
    {
        ArgumentNullException.ThrowIfNull(record); ArgumentNullException.ThrowIfNull(record.Inputs); ArgumentNullException.ThrowIfNull(record.Entry);
        ArgumentNullException.ThrowIfNull(record.Inputs.Identity);
        Require(record.JointEvidence is null || record.JointEvidence.Length is > 0 and <= AiWorkspaceProcessEvidence.MaximumBytes);
        var r = record with { JointEvidence = record.JointEvidence?.ToArray() };
        Require(r.Version == 1 && Hash(r.RequestSha256) && Hash(r.SourceSceneSha256) && Hash(r.PolicySha256) &&
            Hash(r.Inputs.PlanFingerprint) && r.Inputs.Identity.Variant == MeasurementVariant.Ai && r.Inputs.Identity.Host == MeasurementHost.Editor &&
            r.Inputs.Scenario == MeasurementScenario.ControlledFixture && r.Inputs.WorkloadFiles == 1 && r.Inputs.CheckSequence > 0 &&
            r.Inputs.ProductFiles is >= 4 and <= MeasurementInputLease.MaximumProductFiles && r.Inputs.PayloadBytes is > 0 and <= MeasurementInputLease.MaximumPayloadBytes &&
            Hash(r.Inputs.ProductFilesSha256) && Hash(r.Inputs.WorkloadFilesSha256) &&
            r.Entry.ProcessId > 0 && Hash(r.Entry.ExecutableSha256) && Hash(r.Entry.EntryAssemblySha256) && Type(r.FailureType));
        // The same identity validator used by the original input plan; actual plan parameters live in the joint evidence.
        _ = new MeasurementInputsPlan(r.Inputs.Identity, 1000, 1000, 1, 1);
        Require(r.InitialSceneSha256 is null || Hash(r.InitialSceneSha256));
        Require(r.FinalSceneSha256 is null || r.InitialSceneSha256 is not null && Hash(r.FinalSceneSha256));
        if (r.Budget is {} budget) Require(budget.PolicySha256 == r.PolicySha256 && budget.MaximumAttempts is >= 1 and <= 32 &&
            budget.ChargedAttempts >= 0 && budget.ChargedAttempts <= budget.MaximumAttempts);
        if (r.Run is {} run) {
            Require(run.State is MeasurementRunState.Closed or MeasurementRunState.Retained or MeasurementRunState.Rejected &&
                Enum.IsDefined(run.End) && Type(run.FailureType) && run.CloseAttempts is >= 0 and <= 1 && run.CompletedFrames <= 1000000);
            Require(run.State != MeasurementRunState.Closed || !r.ApplicationRetained && !r.InputsRetained && !run.ApplicationRetained && !run.InputsRetained);
            Require(run.State != MeasurementRunState.Retained || r.ApplicationRetained || r.InputsRetained);
            Require(!run.ApplicationRetained || r.ApplicationRetained);
            Require(!run.InputsRetained || r.InputsRetained);
        }
        if (r.JointEvidence is {} jointBytes) {
            var joint = AiWorkspaceProcessEvidence.Decode(jointBytes);
            var process = ProcessMeasurementEvidence.DecodeExpected(joint.ProcessEvidence, r.Inputs.Identity);
            Require(joint.Inputs == r.Inputs && joint.ProcessIdentity == r.Inputs.Identity && r.Run is not null &&
                r.InitialSceneSha256 is not null && r.FinalSceneSha256 is not null && r.Budget is { Closed: true } &&
                process.Series.ProcessId == r.Entry.ProcessId && process.CompletedIterations == (long)r.Run.CompletedFrames);
        } else Require(r.FailureType is not null);
        if (r.FailureType is null) Require(r.Run is { State: MeasurementRunState.Closed, FailureType: null, InputsCheckedBeforeRun: true, InputsCheckedAfterRun: true } &&
            r.Run.End is not (MeasurementRunEnd.None or MeasurementRunEnd.Fault) && !r.ApplicationRetained && !r.InputsRetained && r.JointEvidence is not null);
        return r;
    }
    public static byte[] Encode(AiSceneCandidateRecord record)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(Validate(record), Options);
        Require(bytes.Length <= MaximumBytes); return bytes;
    }
    public static AiSceneCandidateRecord Decode(ReadOnlySpan<byte> bytes)
    {
        Require(bytes.Length is > 0 and <= MaximumBytes); byte[] owned = bytes.ToArray();
        try {
            var reader = new Utf8JsonReader(owned, new JsonReaderOptions { MaxDepth = 8 }); int tokens = 0; string? field = null;
            while (reader.Read()) {
                Require(++tokens <= 512 && reader.TokenType != JsonTokenType.StartArray);
                if (reader.TokenType == JsonTokenType.PropertyName) { Require(reader.ValueSpan.Length <= 128); field = reader.GetString(); }
                else if (reader.TokenType == JsonTokenType.String) {
                    Require(reader.ValueSpan.Length <= (field == "JointEvidence" && reader.CurrentDepth == 1 ? (AiWorkspaceProcessEvidence.MaximumBytes + 2) / 3 * 4 : 1024)); field = null;
                } else if (reader.TokenType == JsonTokenType.Number) Require(reader.ValueSpan.Length <= 32);
            }
            var record = JsonSerializer.Deserialize<AiSceneCandidateRecord>(owned, Options) ?? throw new ArgumentException("ai_scene_candidate_null");
            Require(owned.AsSpan().SequenceEqual(Encode(record))); return Validate(record);
        } catch (JsonException) { throw new ArgumentException("ai_scene_candidate_json"); }
    }
    public static AiSceneCandidateRecord DecodeExpected(ReadOnlySpan<byte> bytes, PreparedAiSceneMeasurement expected)
    {
        ArgumentNullException.ThrowIfNull(expected); var r = Decode(bytes);
        Require(r.RequestSha256 == expected.RequestSha256 && r.SourceSceneSha256 == expected.Request.SceneSha256 &&
            r.PolicySha256 == expected.PolicySha256 && r.Inputs == expected.Inputs);
        if (r.JointEvidence is {} data) Require(AiWorkspaceProcessEvidence.Decode(data).Inputs.PlanFingerprint == expected.Request.Inputs.Plan().Fingerprint);
        return r;
    }
    public static DiagnosticEvidenceFileReceipt ExportNew(string destination, AiSceneCandidateRecord record)
        => DiagnosticEvidenceFiles.WriteNew(destination, Encode(record));
}
