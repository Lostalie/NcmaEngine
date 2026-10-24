using Ncma.Runtime;

namespace Ncma.Scene;

// Per-load state. The ECS owns components; only script metadata is held alongside it.
public sealed partial class RuntimeSceneInstance
{
    private Dictionary<Guid, BehaviourBindingData[]> _bindings;
    private Dictionary<Guid, Entity> _bindingEntities;
    private ulong _revision, _knownWorldRevision;
    private bool _preparing;
    private long _capturedSnapshots;
    internal RuntimeSceneInstance(World world, Dictionary<Guid, BehaviourBindingData[]> bindings, string imageHash)
    {
        World = world; _bindings = bindings; ImageHash = imageHash;
        _bindingEntities = bindings.Keys.ToDictionary(id => id, world.FindEntity);
        _knownWorldRevision = world.Revision;
    }

    public World World { get; }
    public Guid SessionId { get; } = Guid.NewGuid();
    public string ImageHash { get; }
    public ulong Revision { get { VerifyAccess(); ObserveWorld(); return _revision; } }
    internal long CapturedSnapshotCount => _capturedSnapshots;
    public void VerifyAccess() => World.VerifyAccess();
    public BehaviourBindingData[] GetBindings(Guid objectId)
    {
        VerifyReadBoundary(); ObserveWorld();
        _ = World.FindEntity(objectId);
        return RuntimeSceneImage.CopyBindings(_bindings.GetValueOrDefault(objectId, []));
    }

    private void VerifyReadBoundary()
    {
        VerifyAccess();
        if (_preparing || _inspecting || !World.IsCommittedBoundary) throw new InvalidOperationException("Runtime snapshot requires a committed boundary.");
    }
    private void ObserveWorld()
    {
        if (World.Revision == _knownWorldRevision) return;
        ulong revision = checked(_revision + 1);
        // Structural writes through trusted runtime services must not resurrect metadata when
        // the same UUID is recreated with a different Entity generation or World identity.
        foreach (var id in _bindingEntities.Keys.Where(id =>
            !World.TryFindEntity(id, out var entity) || entity != _bindingEntities[id]).ToArray())
        { _bindings.Remove(id); _bindingEntities.Remove(id); _runtimeEncoding.Remove(id); }
        _knownWorldRevision = World.Revision; _revision = revision;
    }
    public RuntimeSceneSnapshot CaptureSnapshot()
    {
        VerifyReadBoundary(); ObserveWorld();
        var world = World.CaptureSnapshot();
        var snapshot = new RuntimeSceneSnapshot(RuntimeSceneImage.Format, RuntimeSceneImage.FormatVersion,
            world.Name, world.Objects.Select(o => new RuntimeObjectSnapshot(o.PersistentId, o.Name, o.Components,
                _bindings.GetValueOrDefault(o.PersistentId, []))).ToArray());
        _capturedSnapshots++;
        return RuntimeSceneCodec.Copy(snapshot);
    }
    public RuntimeSceneCheckpoint CaptureCheckpoint()
    { return new(SessionId, RuntimeSceneCodec.Encode(CaptureSnapshot())); }

    public void RestoreCheckpoint(RuntimeSceneCheckpoint checkpoint, ulong expectedRevision)
    {
        VerifyReadBoundary(); World.VerifyWriteAccess(); ObserveWorld();
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.SessionId != SessionId || expectedRevision != _revision)
            throw new InvalidOperationException("Foreign checkpoint or stale runtime revision.");
        var snapshot = checkpoint.Read();
        var bindings = snapshot.Objects.ToDictionary(o => o.Id, o => o.Behaviours);
        // Allocate every dictionary entry BEFORE World publication. Entity values are filled
        // without growth after commit, from the UUIDs already validated by PrepareRestore.
        var entities = bindings.Keys.ToDictionary(id => id, _ => default(Entity));
        ulong revision = checked(_revision + 1);
        _preparing = true;
        try
        {
            var state = new WorldSnapshot(1, snapshot.Name, snapshot.Objects.Select(o =>
                new ObjectSnapshot(o.Id, o.Name, o.Components)).ToArray());
            var commit = World.PrepareRestore(state, normalized =>
            {
                _ = RuntimeSceneCodec.Encode(new(RuntimeSceneImage.Format, RuntimeSceneImage.FormatVersion,
                    normalized.Name, normalized.Objects.Select(o => new RuntimeObjectSnapshot(
                        o.PersistentId, o.Name, o.Components, bindings[o.PersistentId])).ToArray()));
            });
            commit();
            foreach (var id in bindings.Keys) entities[id] = World.FindEntity(id);
            _bindings = bindings; _bindingEntities = entities; _revision = revision;
            _knownWorldRevision = World.Revision;
            _runtimeEncoding = []; _runtimeHeaderName = null; _runtimeHeaderBytes = 0;
        }
        finally { _preparing = false; }
    }
}
