using Ncma.Runtime;
namespace Ncma.Scene;

// Owns metadata, not a second object/component store. World is the sole runtime authority.
public sealed class SceneDocument
{
    private Dictionary<Guid, BehaviourBindingData[]> _bindings = [];
    private ulong _revision, _knownWorldRevision;
    private bool _preparing;
    private readonly Action<SceneDocumentSnapshot>? _validateComposition;
    // Trusted authoring policy only, not a gameplay step validator or an Agent-supplied callback.
    public SceneDocument(string name = "Untitled", ComponentRegistry? components = null,
        Action<SceneDocumentSnapshot>? validateComposition = null)
    {
        World = new World(name, components);
        _validateComposition = validateComposition;
        _knownWorldRevision = World.Revision;
    }
    public World World { get; }
    public Guid SessionId { get; } = Guid.NewGuid();
    public ulong Revision { get { VerifyAccess(); ObserveWorld(); return _revision; } }
    public void VerifyAccess() => World.VerifyAccess();
    private void ObserveWorld()
    {
        if (World.Revision == _knownWorldRevision) return;
        _revision = checked(_revision + 1);
        _knownWorldRevision = World.Revision;
        // Runtime gameplay can destroy objects. Forget only metadata of no-longer-live UUIDs.
        var live = World.GetObjects().Select(o => o.PersistentId).ToHashSet();
        foreach (Guid id in _bindings.Keys.Where(id => !live.Contains(id)).ToArray()) _bindings.Remove(id);
    }
    private void VerifyMutation()
    {
        VerifyAccess(); World.VerifyWriteAccess();
        if (_preparing || World.IsUpdating) throw new InvalidOperationException("Document mutation requires a safe boundary.");
        ObserveWorld();
    }
    public SceneDocumentSnapshot CaptureSnapshot()
    {
        VerifyAccess();
        if (_preparing || World.IsUpdating) throw new InvalidOperationException("Snapshot requires a committed safe boundary.");
        ObserveWorld();
        WorldSnapshot world = World.CaptureSnapshot();
        var snapshot = new SceneDocumentSnapshot(1, world.Name, world.Objects.Select(o => new SceneObjectData(
            o.PersistentId, o.Name, o.Components, _bindings.GetValueOrDefault(o.PersistentId, []))).ToArray());
        return SceneDocumentCodec.Copy(snapshot);
    }
    private void ValidateComposition(SceneDocumentSnapshot snapshot)
    {
        // A validator cannot rewrite the install candidate through its mutable DTO arrays.
        if (_validateComposition is not null) _validateComposition(SceneDocumentCodec.Copy(snapshot));
    }
    public SceneDocument CreateIsolatedCopy(Action<SceneDocumentSnapshot>? validateCompositionOverride = null)
    {
        byte[] bytes = CaptureBytes();
        using var read = World.ReadOnly(); // Shared trusted validators must not write the source through a closure.
        var copy = new SceneDocument(World.Name, World.Components, validateCompositionOverride ?? _validateComposition);
        copy.RestoreBytes(bytes);
        return copy;
    }
    public void ValidateAuthoring()
    {
        // Prepare only: no World install, revision increment or handle invalidation.
        _ = PrepareRestore(CaptureSnapshot());
    }
    public byte[] CaptureBytes() => SceneDocumentCodec.Encode(CaptureSnapshot());
    public void RestoreBytes(ReadOnlySpan<byte> bytes, ulong? expectedRevision = null)
    {
        VerifyMutation(); // Reject foreign threads/unsafe boundaries before decoding a large input.
        RestoreSnapshot(SceneDocumentCodec.Decode(bytes), expectedRevision);
    }
    public void RestoreSnapshot(SceneDocumentSnapshot snapshot, ulong? expectedRevision = null)
    {
        PrepareRestore(snapshot, expectedRevision)();
    }
    internal Action PrepareRestore(SceneDocumentSnapshot snapshot, ulong? expectedRevision = null,
        Action<SceneDocumentSnapshot>? prepared = null)
    {
        VerifyMutation();
        ulong baseline = _revision;
        if (expectedRevision is not null && expectedRevision != baseline) throw new InvalidOperationException("Document revision conflict.");
        _preparing = true;
        try
        {
            var owned = SceneDocumentCodec.Copy(snapshot);
            var bindings = owned.Objects.ToDictionary(o => o.Id, o => o.Behaviours);
            var world = new WorldSnapshot(1, owned.Name, owned.Objects.Select(o => new ObjectSnapshot(o.Id, o.Name, o.Components)).ToArray());
            ulong revision = checked(_revision + 1);
            Action commit = World.PrepareRestore(world, normalized =>
            {
                // Validators may normalize/enlarge values. Check the COMPLETE resulting document before installation.
                var candidate = new SceneDocumentSnapshot(1, normalized.Name, normalized.Objects.Select(o => new SceneObjectData(
                    o.PersistentId, o.Name, o.Components, bindings[o.PersistentId])).ToArray());
                _ = SceneDocumentCodec.Encode(candidate);
                ValidateComposition(candidate);
                prepared?.Invoke(candidate);
            });
            return () =>
            {
                VerifyMutation();
                if (_revision != baseline) throw new InvalidOperationException("Document changed after preparation.");
                commit(); // All data prepared/validated; no callbacks after World installation.
                _bindings = bindings;
                _knownWorldRevision = World.Revision;
                _revision = revision;
            };
        }
        finally { _preparing = false; }
    }
    public BehaviourBindingData[] GetBindings(Guid objectId)
    {
        VerifyAccess(); ObserveWorld();
        _ = World.FindObject(objectId);
        return CopyBindings(_bindings.GetValueOrDefault(objectId, []));
    }
    public void SetBindings(Guid objectId, BehaviourBindingData[] values)
    {
        VerifyMutation(); _ = World.FindObject(objectId);
        var identities = _bindings.Where(p => p.Key != objectId).SelectMany(p => p.Value).Select(b => b.Id).ToHashSet();
        SceneDocumentValidator.ValidateBindings(values, identities, "bindings");
        var owned = CopyBindings(values);
        var candidate = CaptureSnapshot();
        candidate = candidate with { Objects = candidate.Objects.Select(o => o.Id == objectId ? o with { Behaviours = owned } : o).ToArray() };
        _ = SceneDocumentCodec.Encode(candidate); // Enforce total document budget BEFORE mutation.
        ulong revision = checked(_revision + 1);
        _bindings[objectId] = owned;
        _revision = revision;
    }
    internal Action PrepareRuntimeStep(WorldSnapshot world, Dictionary<Guid, BehaviourBindingData[]> bindings)
    {
        VerifyAccess();
        if (!World.IsUpdating || _preparing) throw new InvalidOperationException("No active runtime step.");
        _preparing = true;
        try
        {
            var candidate = new SceneDocumentSnapshot(1, world.Name, world.Objects.Select(o => new SceneObjectData(
                o.PersistentId, o.Name, o.Components, bindings.GetValueOrDefault(o.PersistentId, []))).ToArray());
            var owned = SceneDocumentCodec.Copy(candidate);
            var prepared = owned.Objects.ToDictionary(o => o.Id, o => o.Behaviours);
            ulong revision = checked(_revision + 1), worldRevision = checked(World.Revision + 1);
            return () => { _bindings = prepared; _revision = revision; _knownWorldRevision = worldRevision; };
        }
        finally { _preparing = false; }
    }
    private static BehaviourBindingData[] CopyBindings(BehaviourBindingData[] values) => values
        .Select(b => b with { Exports = b.Exports.OrderBy(p => p.Name, StringComparer.Ordinal).ToArray() }).ToArray();
}
