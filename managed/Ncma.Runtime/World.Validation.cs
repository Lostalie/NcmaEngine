namespace Ncma.Runtime;

public sealed partial class World
{
    // Derived encoded metadata only; all values and identities remain owned by the sole World.
    // Cached DTO arrays never leave this class. Snapshot callbacks receive fresh array shells.
    private sealed record EncodedComponent(ComponentSnapshot Snapshot, int Bytes, bool Stable);
    private sealed record EncodedObject(Entry Entry, ObjectSnapshot Snapshot, Dictionary<Type, EncodedComponent> Components, int Bytes, bool Stable);
    private sealed record EncodedWorld(Dictionary<int, EncodedObject> Objects, int HeaderBytes, int Bytes, bool Stable);
    private EncodedWorld? _committedEncoding, _stepEncodingBaseline, _stepPreparedEncoding;
    private ulong _encodingRevision, _stepMutation, _stepPreparedMutation;
    private Guid _stepAttempt;
    private HashSet<int>? _stepDirtySlots;
    private HashSet<(int Slot, Type Type)>? _stepDirtyComponents;
    private long _encodedObjectCount, _encodedComponentCount, _copiedValidationSnapshotCount;
    internal (long EncodedObjects, long CopiedSnapshots) ValidationWork
    { get { VerifyAccess(); return (_encodedObjectCount, _copiedValidationSnapshotCount); } }
    internal long EncodedComponentCount { get { VerifyAccess(); return _encodedComponentCount; } }
    internal int PreparedSnapshotBytes
    {
        get
        {
            VerifyAccess();
            if (!_updating || _stepError is not null || _stepPreparedEncoding is null || _stepPreparedMutation != _stepMutation)
                throw new InvalidOperationException("No current prepared snapshot budget.");
            return _stepPreparedEncoding.Bytes;
        }
    }

    private void DirtyStep(int slot, Type? type = null)
    {
        ulong next = checked(_stepMutation + 1);
        _stepDirtySlots!.Add(slot); // Allocation, if any, precedes the actual mutation.
        if (type is not null) _stepDirtyComponents!.Add((slot, type));
        _stepMutation = next;
    }
    private EncodedWorld PrepareEncoding()
    {
        var objects = _stepObjects!;
        if (_stepPreparedEncoding is { Stable: true } prepared && _stepPreparedMutation == _stepMutation) return prepared;
        var prior = _stepEncodingBaseline;
        if (_stepDirtySlots!.Count == 0 && prior is { Stable: true }) return prior;
        int headerBytes = prior?.HeaderBytes ?? SceneJson.MeasureBounded(new WorldSnapshot(1, _name, []), MaxSnapshotBytes);
        int bytes = headerBytes;
        var entries = new Dictionary<int, EncodedObject>(objects.Count);
        bool stable = true;
        foreach (var (slot, entry) in objects)
        {
            EncodedObject? cached = prior?.Objects.GetValueOrDefault(slot);
            EncodedObject current;
            if (cached is not null && !_stepDirtySlots.Contains(slot) &&
                cached.Stable && ReferenceEquals(cached.Entry, entry)) current = cached;
            else
            {
                current = EncodeObject(entry, cached);
                _encodedObjectCount++;
            }
            // Exact serializer-produced object sizes plus the unchanged enclosing empty array and commas.
            bytes = checked(bytes + current.Bytes + (entries.Count == 0 ? 0 : 1));
            if (bytes > MaxSnapshotBytes) throw new ArgumentException("Snapshot size limit exceeded.");
            entries.Add(slot, current); stable &= current.Stable;
        }
        return new(entries, headerBytes, bytes, stable);
    }
    private EncodedObject EncodeObject(Entry entry, EncodedObject? prior)
    {
        if (prior?.Entry.Generation != entry.Generation) prior = null;
        int bytes = SceneJson.MeasureBounded(new ObjectSnapshot(entry.PersistentId, entry.Name, []), MaxSnapshotBytes);
        var components = new Dictionary<Type, EncodedComponent>(); bool stable = true;
        foreach (Type type in _stepStorage!.TypesAt(entry.Slot))
        {
            EncodedComponent current;
            if (prior is not null && !_stepDirtyComponents!.Contains((entry.Slot, type)) &&
                prior.Components.TryGetValue(type, out var cached) && cached.Stable) current = cached;
            else
            {
                var descriptor = Components.Describe(type);
                var snapshot = new ComponentSnapshot(descriptor.TypeId, descriptor.Version, _stepStorage.FindPool(type)!.Encode(entry.Slot));
                Components.ValidateEncoding(snapshot);
                int size = SceneJson.MeasureBounded(snapshot, MaxSnapshotBytes);
                current = new(snapshot, size, Components.CanCacheEncoding(type)); _encodedComponentCount++;
            }
            bytes = checked(bytes + current.Bytes + (components.Count == 0 ? 0 : 1));
            if (bytes > MaxSnapshotBytes) throw new ArgumentException("Snapshot size limit exceeded.");
            components.Add(type, current); stable &= current.Stable;
        }
        var result = new ObjectSnapshot(entry.PersistentId, entry.Name,
            components.Values.Select(c => c.Snapshot).OrderBy(c => c.TypeId, StringComparer.Ordinal).ToArray());
        return new(entry, result, components, bytes, stable);
    }
    private WorldSnapshot CopyValidationSnapshot(EncodedWorld encoded)
    {
        _copiedValidationSnapshotCount++;
        return new(1, _name, encoded.Objects.Values.OrderBy(o => o.Entry.CreationOrdinal)
            .Select(o => o.Snapshot with { Components = (ComponentSnapshot[])o.Snapshot.Components.Clone() }).ToArray());
    }

    internal readonly record struct PreparedObject(Entity Entity, Guid Id, string Name, int ComponentArrayBytes);
    // A checked observation of one prepared attempt. It never exposes backing dictionaries, values,
    // mutable DTO arrays or a write lease, and expires on mutation/abort/commit/replacement.
    internal sealed class PreparedStepView
    {
        private readonly World _world;
        private readonly EncodedWorld _encoded;
        private readonly Guid _attempt;
        private readonly ulong _mutation;
        internal PreparedStepView(World world)
        {
            world.VerifyAccess();
            if (!world._preparing || world._stepPreparedEncoding is null || world._stepPreparedMutation != world._stepMutation)
                throw new InvalidOperationException("No prepared observation.");
            _world = world; _encoded = world._stepPreparedEncoding; _attempt = world._stepAttempt; _mutation = world._stepMutation;
        }
        internal void VerifyCurrent()
        {
            _world.VerifyAccess();
            if (!_world._updating || _world._stepError is not null || _world._stepAttempt != _attempt || _world._stepMutation != _mutation ||
                !ReferenceEquals(_world._stepPreparedEncoding, _encoded)) throw new InvalidOperationException("Prepared observation expired.");
        }
        internal World Owner { get { VerifyCurrent(); return _world; } }
        internal string Name { get { VerifyCurrent(); return _world._name; } }
        internal int Bytes { get { VerifyCurrent(); return _encoded.Bytes; } }
        internal bool Contains(Entity entity)
        {
            VerifyCurrent();
            return entity.WorldIdentity == _world._identity && _encoded.Objects.TryGetValue(entity.Index, out var value) &&
                value.Entry.Generation == entity.Generation;
        }
        internal int ComponentBytes(Guid id, string typeId)
        {
            VerifyCurrent();
            if (!_world._stepUuids!.TryGetValue(id, out int slot) || !_encoded.Objects.TryGetValue(slot, out var value))
                throw new ArgumentException("Unknown prepared object.");
            return value.Components.Values.FirstOrDefault(c => c.Snapshot.TypeId == typeId)?.Bytes
                ?? throw new ArgumentException("Component is not attached.");
        }
        internal IEnumerable<PreparedObject> Objects
        {
            get
            {
                VerifyCurrent();
                foreach (var value in _encoded.Objects.Values.OrderBy(o => o.Entry.CreationOrdinal))
                {
                    VerifyCurrent();
                    int bytes = checked(2 + value.Components.Values.Sum(c => c.Bytes) + Math.Max(0, value.Components.Count - 1));
                    yield return new(_world.IdentityOf(value.Entry), value.Entry.PersistentId, value.Entry.Name, bytes);
                    VerifyCurrent(); // Also reject stale resumption after the last yielded item.
                }
            }
        }
        internal WorldSnapshot CopySnapshot() { VerifyCurrent(); return _world.CopyValidationSnapshot(_encoded); }
    }
}
