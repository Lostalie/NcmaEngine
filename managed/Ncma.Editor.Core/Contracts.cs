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
    private readonly HashSet<Guid>? _scope, _created, _bindings;
    private readonly HashSet<string>? _components;
    private readonly Func<bool>? _isCurrent;
    internal bool AllowsDocumentHistory { get; }
    internal bool RequireTrustedBindings { get; }
    public CapabilityPermissions(IEnumerable<string>? allowedMutations = null, IEnumerable<Guid>? approvedDestructiveTargets = null,
        IEnumerable<Guid>? objectScope = null, IEnumerable<Guid>? createScope = null,
        IEnumerable<string>? componentScope = null, IEnumerable<Guid>? bindingScope = null,
        bool allowDocumentHistory = true, bool requireTrustedBindings = false, Func<bool>? isCurrent = null)
    {
        _allowed = new(allowedMutations ?? [], StringComparer.Ordinal);
        _targets = new(approvedDestructiveTargets ?? []);
        _scope = objectScope is null ? null : new(objectScope); _created = createScope is null ? null : new(createScope);
        _components = componentScope is null ? null : new(componentScope, StringComparer.Ordinal);
        _bindings = bindingScope is null ? null : new(bindingScope); _isCurrent = isCurrent;
        AllowsDocumentHistory = allowDocumentHistory; RequireTrustedBindings = requireTrustedBindings;
    }
    public static CapabilityPermissions ReadOnly { get; } = new();
    internal bool Allows(string capability, Guid? target = null) =>
        (_isCurrent?.Invoke() ?? true) && _allowed.Contains(capability) && (target is null || _targets.Contains(target.Value));
    internal bool InScope(CommandImpact impact) =>
        (_isCurrent?.Invoke() ?? true) && (!impact.DocumentReplacement || AllowsDocumentHistory) &&
        (_scope is null || impact.Objects.All(_scope.Contains)) && (_created is null || impact.CreatedObjects.All(_created.Contains)) &&
        (_components is null || impact.ComponentTypes.All(_components.Contains)) && (_bindings is null || impact.Bindings.All(_bindings.Contains));
    internal bool ObjectAllowed(Guid id, bool creating = false) =>
        (_isCurrent?.Invoke() ?? true) && (_scope is null || _scope.Contains(id)) && (!creating || _created is null || _created.Contains(id));
}
public sealed record CommandImpact(Guid[] Objects, Guid[] CreatedObjects, string[] ComponentTypes, Guid[] Bindings, bool DocumentReplacement);
public sealed record EditState(Guid SessionId, ulong Revision, int UndoCount, int RedoCount,
    string UndoLabel, string RedoLabel, bool Dirty, bool HistoryInvalidated, bool EditBusy,
    bool Frozen, Guid? Selection, string? FilePath);
