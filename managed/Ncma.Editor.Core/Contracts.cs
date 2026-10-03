using System.Text.Json;
using System.Text.Json.Serialization;
namespace Ncma.Editor.Core;

public enum MutationRisk { ReadOnly, Reversible, Destructive }
public sealed record CapabilityDescriptor(string Name, string Description, MutationRisk Risk,
    JsonElement InputSchema, JsonElement OutputSchema);
public sealed record CapabilityRequest(
    [property: JsonRequired] int ContractVersion, [property: JsonRequired] Guid RequestId,
    [property: JsonRequired] Guid SessionId, [property: JsonRequired] ulong? ExpectedRevision,
    [property: JsonRequired] string Capability, [property: JsonRequired] JsonElement Input);
// Revision is current; ExecutionRevision identifies the original successful execution on a replay.
public sealed record CapabilityResult(int ContractVersion, Guid RequestId, Guid SessionId,
    ulong Revision, string Status, string Code, bool Changed, JsonElement Data,
    ulong ExecutionRevision, bool Replayed = false);

// Constructed by a trusted host, never from request JSON.
public sealed class CapabilityPermissions
{
    private readonly HashSet<string> _allowed;
    private readonly HashSet<Guid> _targets;
    public CapabilityPermissions(IEnumerable<string>? allowedMutations = null, IEnumerable<Guid>? approvedDestructiveTargets = null)
    {
        _allowed = new(allowedMutations ?? [], StringComparer.Ordinal);
        _targets = new(approvedDestructiveTargets ?? []);
    }
    public static CapabilityPermissions ReadOnly { get; } = new();
    internal bool Allows(string capability, Guid? target = null) =>
        _allowed.Contains(capability) && (target is null || _targets.Contains(target.Value));
}
public sealed record EditState(Guid SessionId, ulong Revision, int UndoCount, int RedoCount,
    string UndoLabel, string RedoLabel, bool Dirty, bool HistoryInvalidated, bool EditBusy,
    bool Frozen, Guid? Selection, string? FilePath);
