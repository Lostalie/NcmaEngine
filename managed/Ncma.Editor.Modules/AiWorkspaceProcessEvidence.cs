using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ncma.Application;
using Ncma.Editor.Services;
using Ncma.Modules;

namespace Ncma.Editor.Modules;

public sealed record AiWorkspaceProcessRecord(int Version, ObservationIdentity WorkspaceIdentity,
    ProcessMeasurementIdentity ProcessIdentity, int DurationMilliseconds, int IntervalMilliseconds, int Capacity,
    ulong MaximumFrames, MeasurementInputsReceipt Inputs, string WorkspaceSha256, string ProcessSha256,
    byte[] WorkspaceEvidence, byte[] ProcessEvidence)
{
    public bool PerformanceAccepted => false;
    public bool WorkloadExecutionVerified => false;
    public bool CommonClockVerified => false;
    public bool GrantsExecutionAuthority => false;
    public bool ProvenanceVerified => false;
}
public sealed record WorkspaceProcessWorkflowSummary(long Ordinal, Guid ConfigurationId, AiAuthoringIntent Intent,
    bool ReleasedBeforeSeal, WorkflowObservationSummary Workflow);
public sealed record WorkspaceProcessSummary(Guid RunId, long StartedWorkflows, long DroppedWorkflows,
    long FailedWorkflows, bool MultipleCapturedConfigurations, bool ProcessBoundToLoop, bool ProcessCadenceComplete,
    bool ProcessCountersAvailable, WorkspaceProcessWorkflowSummary[] Workflows)
{
    public bool PerformanceAccepted => false;
    public bool CommonClockVerified => false;
    public bool ProvenanceVerified => false;
}

// Offline data association, not a measurement launcher/lease/approval or an executed-workload proof.
// Both original byte streams are preserved; no cross-clock arithmetic or cross-configuration averages.
public static class AiWorkspaceProcessEvidence
{
    public const int MaximumBytes = 40 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 8, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value)
    { if (!value) throw new ArgumentException("ai_workspace_process_evidence"); }
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static bool IsHash(string? text) => text is { Length: 64 } && text.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static void Sizes(int workspace, int process)
    { Require(workspace is > 0 and <= AiWorkspaceObservationEvidence.MaximumBytes && process is > 0 and <= ProcessSeriesEvidence.MaximumBytes); }
    public static AiWorkspaceProcessRecord Create(MeasurementInputsPlan plan, MeasurementInputsReceipt inputs,
        ObservationIdentity expectedWorkspace, ReadOnlySpan<byte> workspace, ReadOnlySpan<byte> process,
        string expectedWorkspaceSha256, string expectedProcessSha256)
    {
        ArgumentNullException.ThrowIfNull(plan); Sizes(workspace.Length, process.Length);
        var record = new AiWorkspaceProcessRecord(1, expectedWorkspace, plan.Identity, plan.DurationMilliseconds,
            plan.IntervalMilliseconds, plan.Capacity, plan.MaximumFrames, inputs, expectedWorkspaceSha256,
            expectedProcessSha256, workspace.ToArray(), process.ToArray());
        return Validate(record).Record;
    }
    private static (AiWorkspaceProcessRecord Record, AiWorkspaceObservationRecord Workspace, ProcessMeasurementReport Process) Validate(AiWorkspaceProcessRecord record)
    {
        ArgumentNullException.ThrowIfNull(record); ArgumentNullException.ThrowIfNull(record.Inputs);
        Require(record.WorkspaceEvidence is not null && record.ProcessEvidence is not null);
        Sizes(record.WorkspaceEvidence.Length, record.ProcessEvidence.Length);
        var copy = record with { WorkspaceEvidence = record.WorkspaceEvidence.ToArray(), ProcessEvidence = record.ProcessEvidence.ToArray() };
        var plan = new MeasurementInputsPlan(copy.ProcessIdentity, copy.DurationMilliseconds, copy.IntervalMilliseconds, copy.Capacity, copy.MaximumFrames);
        var inputs = copy.Inputs;
        Require(copy.Version == 1 && Hash(copy.WorkspaceEvidence) == copy.WorkspaceSha256 && Hash(copy.ProcessEvidence) == copy.ProcessSha256 &&
            inputs.Identity == plan.Identity && inputs.PlanFingerprint == plan.Fingerprint && inputs.CheckSequence > 0 &&
            inputs.Scenario is MeasurementScenario.ControlledFixture or MeasurementScenario.OpenWorldExploration &&
            inputs.ProductFiles is >= 4 and <= MeasurementInputLease.MaximumProductFiles &&
            inputs.WorkloadFiles is >= 1 and <= MeasurementInputLease.MaximumWorkloadFiles &&
            inputs.PayloadBytes is > 0 and <= MeasurementInputLease.MaximumPayloadBytes && IsHash(inputs.ProductFilesSha256) && IsHash(inputs.WorkloadFilesSha256));
        var workspace = AiWorkspaceObservationEvidence.DecodeExpected(copy.WorkspaceEvidence, copy.WorkspaceIdentity);
        var process = ProcessMeasurementEvidence.DecodeExpected(copy.ProcessEvidence, plan.Identity);
        WorkflowProcessAssociation.ValidateLabels(workspace.Identity, process.Identity);
        Require(process.Series.Capacity == plan.Capacity && process.CompletedIterations <= (long)plan.MaximumFrames &&
            process.Series.DurationTicks == (Int128)process.Series.TimestampFrequency * plan.DurationMilliseconds / 1000 &&
            process.Series.IntervalTicks == ((Int128)process.Series.TimestampFrequency * plan.IntervalMilliseconds + 999) / 1000);
        return (copy, workspace, process);
    }
    public static byte[] Encode(AiWorkspaceProcessRecord record)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(Validate(record).Record, Options);
        Require(bytes.Length <= MaximumBytes); return bytes;
    }
    public static AiWorkspaceProcessRecord Decode(ReadOnlySpan<byte> bytes)
    {
        Require(bytes.Length is > 0 and <= MaximumBytes); byte[] owned = bytes.ToArray();
        try {
            var reader = new Utf8JsonReader(owned, new JsonReaderOptions { MaxDepth = 8 });
            int tokens = 0; string? field = null;
            while (reader.Read()) {
                Require(++tokens <= 512 && reader.TokenType != JsonTokenType.StartArray);
                if (reader.TokenType == JsonTokenType.PropertyName) { Require(reader.ValueSpan.Length <= 128); field = reader.GetString(); }
                else if (reader.TokenType == JsonTokenType.String) {
                    int bound = (reader.CurrentDepth, field) switch {
                        (1, "WorkspaceEvidence") => (AiWorkspaceObservationEvidence.MaximumBytes + 2) / 3 * 4,
                        (1, "ProcessEvidence") => (ProcessSeriesEvidence.MaximumBytes + 2) / 3 * 4,
                        _ => 1024
                    };
                    Require(reader.ValueSpan.Length <= bound); field = null;
                } else if (reader.TokenType == JsonTokenType.Number) Require(reader.ValueSpan.Length <= 32);
            }
            var record = JsonSerializer.Deserialize<AiWorkspaceProcessRecord>(owned, Options); Require(record is not null);
            var validated = Validate(record).Record;
            Require(owned.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(validated, Options))); return validated;
        } catch (JsonException) { throw new ArgumentException("ai_workspace_process_json"); }
    }
    // External expected labels/hashes must be independently pinned, never taken from the untrusted envelope itself.
    public static AiWorkspaceProcessRecord DecodeExpected(ReadOnlySpan<byte> bytes, MeasurementInputsPlan expectedPlan,
        ObservationIdentity expectedWorkspace, string expectedWorkspaceSha256, string expectedProcessSha256)
    {
        ArgumentNullException.ThrowIfNull(expectedPlan); ArgumentNullException.ThrowIfNull(expectedWorkspace);
        var record = Decode(bytes);
        Require(record.WorkspaceIdentity == expectedWorkspace && record.ProcessIdentity == expectedPlan.Identity &&
            record.Inputs.PlanFingerprint == expectedPlan.Fingerprint && record.WorkspaceSha256 == expectedWorkspaceSha256 && record.ProcessSha256 == expectedProcessSha256);
        return record;
    }
    public static WorkspaceProcessSummary Inspect(AiWorkspaceProcessRecord record)
    {
        var (_, workspace, process) = Validate(record);
        var rows = workspace.Workflows.Select(w => new WorkspaceProcessWorkflowSummary(w.Ordinal, w.ConfigurationId, w.Intent,
            w.ReleasedBeforeSeal, AiWorkflowObservationEvidence.DecodeExpected(w.WorkflowEvidence, workspace.Identity, w.Intent, workspace.WorkflowByteLimit).Summary)).ToArray();
        // Decode the process once, not once per workflow. Summary preserves separate configuration rows and all gaps.
        return new(workspace.Identity.RunId, workspace.StartedWorkflows, workspace.DroppedWorkflows, workspace.FailedWorkflows,
            workspace.MultipleCapturedConfigurations, process.BoundToLoop, process.Series.CompleteCadenceCoverage, process.Series.AllCountersAvailable, rows);
    }
    // Caller owns destination approval/isolation. No automatic write, source scan, lease substitution or overwrite.
    public static DiagnosticEvidenceFileReceipt ExportNew(string destination, AiWorkspaceProcessRecord record)
        => DiagnosticEvidenceFiles.WriteNew(destination, Encode(record));
}
