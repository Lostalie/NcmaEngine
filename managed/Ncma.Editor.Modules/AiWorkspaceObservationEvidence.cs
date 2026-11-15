using System.Text.Json;
using System.Text.Json.Serialization;
using Ncma.Editor.Services;
using Ncma.Modules;

namespace Ncma.Editor.Modules;

public sealed record AiWorkspaceWorkflowEvidence(long Ordinal, Guid ConfigurationId, AiAuthoringIntent Intent,
    bool ReleasedBeforeSeal, byte[] WorkflowEvidence);
public sealed record AiWorkspaceObservationRecord(int Version, ObservationIdentity Identity, long StartedTimestamp,
    long EndedTimestamp, int WorkflowCapacity, int WorkflowByteLimit, int TotalByteLimit, long StartedWorkflows,
    long DroppedWorkflows, long FailedWorkflows, AiWorkspaceWorkflowEvidence[] Workflows)
{
    public bool PerformanceAccepted => false;
    public bool GrantsExecutionAuthority => false;
    public bool ProvenanceVerified => false;
    // Only retained reports. Dropped/failed workflows may conceal further configuration changes.
    public bool MultipleCapturedConfigurations => Workflows.Select(w => w.ConfigurationId).Distinct().Take(2).Count() > 1;
}

// Bounded detached numeric evidence. Configuration UUID is opaque, not endpoint/model/credential data.
// This workspace observer excludes connection diagnostics and domain calls outside its workflow wrapper.
public static class AiWorkspaceObservationEvidence
{
    public const int MaximumWorkflows = 32, MaximumWorkflowBytes = 1024 * 1024,
        MaximumPayloadBytes = 8 * 1024 * 1024, MaximumBytes = 12 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 5, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value)
    { if (!value) throw new ArgumentException("ai_workspace_observation_evidence"); }
    internal static void ValidateLimits(int capacity, int bytes, int total)
    {
        if (capacity is < 1 or > MaximumWorkflows || bytes is < 1 or > MaximumWorkflowBytes || total < bytes || total > MaximumPayloadBytes)
            throw new ArgumentException("ai_workspace_observation_limits");
    }
    private static AiWorkspaceObservationRecord Validate(AiWorkspaceObservationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateLimits(record.WorkflowCapacity, record.WorkflowByteLimit, record.TotalByteLimit);
        _ = new ModuleObservationCapture(record.Identity, 0, 1);
        Require(record.Version == 1 && record.Identity.Variant is ObservationVariant.Ai or ObservationVariant.AiExtensions &&
            record.StartedTimestamp >= 0 && record.EndedTimestamp >= record.StartedTimestamp && record.StartedWorkflows >= 0 &&
            record.DroppedWorkflows >= 0 && record.FailedWorkflows >= 0 && record.Workflows is not null && record.Workflows.Length <= record.WorkflowCapacity);
        Require((Int128)record.Workflows.Length + record.DroppedWorkflows + record.FailedWorkflows == record.StartedWorkflows);
        var entries = (AiWorkspaceWorkflowEvidence[])record.Workflows.Clone();
        int bytes = 0; long ordinal = 0, end = record.StartedTimestamp;
        var requestIds = new HashSet<Guid>(); var sessionIds = new HashSet<Guid>();
        for (int i = 0; i < entries.Length; ++i) {
            var row = entries[i];
            Require(row is not null && row.ConfigurationId != Guid.Empty && row.Ordinal > ordinal &&
                row.Ordinal <= record.StartedWorkflows && row.Ordinal <= record.WorkflowCapacity && Enum.IsDefined(row.Intent) &&
                (row.ReleasedBeforeSeal || i == entries.Length - 1 && row.Ordinal == record.StartedWorkflows) &&
                row.WorkflowEvidence is not null && row.WorkflowEvidence.Length is > 0 && row.WorkflowEvidence.Length <= record.WorkflowByteLimit);
            bytes = checked(bytes + row.WorkflowEvidence.Length); Require(bytes <= record.TotalByteLimit);
            byte[] owned = row.WorkflowEvidence.ToArray();
            var workflow = AiWorkflowObservationEvidence.DecodeExpected(owned, record.Identity, row.Intent, record.WorkflowByteLimit);
            Require(workflow.StartedTimestamp >= end && workflow.EndedTimestamp <= record.EndedTimestamp);
            var localRequests = workflow.Requests.Select(r => r.RequestId).Concat(workflow.Transport.Samples.Select(s => s.Request.RequestId)).Distinct();
            var localSessions = workflow.Requests.Where(r => r.Module is not null).SelectMany(r => r.Module!.Events.Select(e => e.Request.SessionId))
                .Concat(workflow.Transport.Samples.Select(s => s.Request.SessionId)).Distinct();
            foreach (var id in localRequests) Require(requestIds.Add(id));
            foreach (var id in localSessions) Require(sessionIds.Add(id));
            entries[i] = row with { WorkflowEvidence = owned }; ordinal = row.Ordinal; end = workflow.EndedTimestamp;
        }
        return record with { Workflows = entries };
    }
    public static byte[] Encode(AiWorkspaceObservationRecord record)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(Validate(record), Options);
        Require(bytes.Length <= MaximumBytes); return bytes;
    }
    public static AiWorkspaceObservationRecord Decode(ReadOnlySpan<byte> bytes)
    {
        Require(bytes.Length is > 0 and <= MaximumBytes); byte[] owned = bytes.ToArray();
        try {
            var reader = new Utf8JsonReader(owned, new JsonReaderOptions { MaxDepth = 5 });
            int tokens = 0, entries = 0; string? field = null;
            while (reader.Read()) {
                Require(++tokens <= 1024);
                if (reader.TokenType == JsonTokenType.PropertyName) { Require(reader.ValueSpan.Length <= 128); field = reader.GetString(); }
                else if (reader.TokenType == JsonTokenType.StartArray) Require(reader.CurrentDepth == 1 && field == "Workflows");
                else if (reader.TokenType == JsonTokenType.StartObject && reader.CurrentDepth == 2) Require(++entries <= MaximumWorkflows);
                else if (reader.TokenType == JsonTokenType.String) {
                    Require(reader.ValueSpan.Length <= (reader.CurrentDepth == 3 && field == "WorkflowEvidence" ? (MaximumWorkflowBytes + 2) / 3 * 4 : 256));
                    field = null;
                } else if (reader.TokenType == JsonTokenType.Number) Require(reader.ValueSpan.Length <= 32);
            }
            var record = JsonSerializer.Deserialize<AiWorkspaceObservationRecord>(owned, Options);
            Require(record is not null); var valid = Validate(record);
            Require(owned.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(valid, Options))); return valid;
        } catch (JsonException) { throw new ArgumentException("ai_workspace_observation_json"); }
    }
    public static AiWorkspaceObservationRecord DecodeExpected(ReadOnlySpan<byte> bytes, ObservationIdentity expected)
    {
        ArgumentNullException.ThrowIfNull(expected); var record = Decode(bytes); Require(record.Identity == expected); return record;
    }
    public static DiagnosticEvidenceFileReceipt ExportNew(string destination, AiWorkspaceObservationRecord record)
        => DiagnosticEvidenceFiles.WriteNew(destination, Encode(record));
}
