namespace Ncma.Runtime;

public sealed partial class World
{
    // Derived encoded metadata only; all values and identities remain owned by the sole World.
    // Full object DTOs and sorted component arrays are materialized only for explicit snapshot
    // observers. Fixed-step validation/typed prepared views need metadata and exact budgets only.
    private readonly record struct EncodedComponent(Type Type, ComponentSnapshot Snapshot, int Bytes, bool Stable);
    private sealed record EncodedObject(Entry Entry, EncodedComponent[] Components, int HeaderBytes, int Bytes, bool Stable);
    private sealed record EncodedWorld(Dictionary<int, EncodedObject> Objects, int HeaderBytes, int Bytes, bool Stable);
    private EncodedWorld? _committedEncoding, _stepEncodingBaseline, _stepPreparedEncoding;
    private ulong _encodingRevision, _stepMutation, _stepPreparedMutation;
    private Guid _stepAttempt;
    private HashSet<int>? _stepDirtySlots;
    private HashSet<(int Slot, Type Type)>? _stepDirtyComponents;
    // Only proof-eligible immutable value encodings, produced by the normal EVERY-write validator.
    // Attempt-local, branch-forked and generation-checked; never a second component value store.
    private readonly record struct ValidatedEncoding(ulong Generation, System.Text.Json.JsonElement Data);
    private Dictionary<(int Slot, Type Type), ValidatedEncoding>? _stepValidatedEncodings;
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
        if (type is not null)
        {
            _stepDirtyComponents!.Add((slot, type));
            _stepValidatedEncodings!.Remove((slot, type));
        }
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
        int headerBytes = prior is not null && ReferenceEquals(prior.Entry, entry) ? prior.HeaderBytes :
            SceneJson.MeasureBounded(new ObjectSnapshot(entry.PersistentId, entry.Name, []), MaxSnapshotBytes);
        int bytes = headerBytes;
        // Immutable attempt-owned derived metadata, bounded by MaxComponentsPerObject. The common
        // unchanged pool order uses the same index; membership changes fall back to identity lookup.
        var components = new EncodedComponent[_stepStorage!.Count(entry.Slot)]; bool stable = true;
        int index = 0;
        foreach (Type type in _stepStorage!.TypesAt(entry.Slot))
        {
            EncodedComponent current;
            EncodedComponent? cached = null;
            if (prior is not null && !_stepDirtyComponents!.Contains((entry.Slot, type)))
            {
                if (index < prior.Components.Length && prior.Components[index].Type == type) cached = prior.Components[index];
                else foreach (var candidate in prior.Components)
                    if (candidate.Type == type) { cached = candidate; break; }
            }
            if (cached is { Stable: true } reusable) current = reusable;
            else
            {
                var descriptor = Components.Describe(type);
                var data = _stepValidatedEncodings!.TryGetValue((entry.Slot, type), out var validated) && validated.Generation == entry.Generation
                    ? validated.Data : _stepStorage.FindPool(type)!.Encode(entry.Slot);
                var snapshot = new ComponentSnapshot(descriptor.TypeId, descriptor.Version, data);
                Components.ValidateEncoding(snapshot);
                int size = SceneJson.MeasureBounded(snapshot, MaxSnapshotBytes);
                current = new(type, snapshot, size, Components.CanCacheEncoding(type)); _encodedComponentCount++;
            }
            bytes = checked(bytes + current.Bytes + (index == 0 ? 0 : 1));
            if (bytes > MaxSnapshotBytes) throw new ArgumentException("Snapshot size limit exceeded.");
            components[index++] = current; stable &= current.Stable;
        }
        return new(entry, components, headerBytes, bytes, stable);
    }
    private WorldSnapshot CopyValidationSnapshot(EncodedWorld encoded)
    {
        _copiedValidationSnapshotCount++;
        return new(1, _name, encoded.Objects.Values.OrderBy(o => o.Entry.CreationOrdinal)
            .Select(o => new ObjectSnapshot(o.Entry.PersistentId, o.Entry.Name,
                o.Components.Select(c => c.Snapshot).OrderBy(c => c.TypeId, StringComparer.Ordinal).ToArray())).ToArray());
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
        internal bool TryResolve(Guid id, out Entity entity)
        {
            VerifyCurrent();
            if (_world._stepUuids!.TryGetValue(id, out int slot) && _encoded.Objects.TryGetValue(slot, out var value))
            { entity = _world.IdentityOf(value.Entry); return true; }
            entity = default; return false;
        }
        internal bool TryGet<T>(Entity entity, out T value) where T : struct, IComponent
        {
            if (!Contains(entity)) throw new ArgumentException("Unknown prepared entity.");
            if (!_world._stepStorage!.Has(entity.Index, typeof(T))) { value = default; return false; }
            value = _world._stepStorage.Get<T>(entity.Index); return true;
        }
        internal int ComponentBytes(Guid id, string typeId)
        {
            VerifyCurrent();
            if (!_world._stepUuids!.TryGetValue(id, out int slot) || !_encoded.Objects.TryGetValue(slot, out var value))
                throw new ArgumentException("Unknown prepared object.");
            foreach (var component in value.Components)
                if (component.Snapshot.TypeId == typeId) return component.Bytes;
            throw new ArgumentException("Component is not attached.");
        }
        internal IEnumerable<PreparedObject> Objects
        {
            get
            {
                VerifyCurrent();
                foreach (var value in _encoded.Objects.Values.OrderBy(o => o.Entry.CreationOrdinal))
                {
                    VerifyCurrent();
                    int bytes = checked(2 + value.Bytes - value.HeaderBytes);
                    yield return new(_world.IdentityOf(value.Entry), value.Entry.PersistentId, value.Entry.Name, bytes);
                    VerifyCurrent(); // Also reject stale resumption after the last yielded item.
                }
            }
        }
        internal WorldSnapshot CopySnapshot() { VerifyCurrent(); return _world.CopyValidationSnapshot(_encoded); }
    }
}
