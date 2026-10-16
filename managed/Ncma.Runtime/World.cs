using System.Text;
using System.Text.Json;

namespace Ncma.Runtime;

public readonly record struct ObjectReference(Guid WorldId, ulong Id);
public sealed record ComponentSnapshot(string TypeId, int Version, JsonElement Data);
public sealed record ObjectSnapshot(Guid PersistentId, string Name, ComponentSnapshot[] Components);
public sealed record WorldSnapshot(int Version, string Name, ObjectSnapshot[] Objects);

// Sole scene authority; native hosts receive copied render/inspection data only.
public sealed partial class World
{
    public const int MaxObjects = 4096;
    public const int MaxComponentsPerObject = 64;
    public const int MaxSnapshotBytes = 4 * 1024 * 1024;
    private sealed record Entry(Guid PersistentId, string Name, int Slot);
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private Dictionary<ulong, Entry> _objects = new();
    private Dictionary<Guid, ulong> _uuidIndex = new();
    private Dictionary<ulong, Entry>? _stepObjects;
    private Dictionary<Guid, ulong>? _stepUuids;
    private ComponentStorage _storage = new();
    private ComponentStorage? _stepStorage;
    private bool[] _slots = new bool[MaxObjects];
    private bool[]? _stepSlots;
    private ulong _stepNextId;
    private Exception? _stepError;
    private Guid _identity = Guid.NewGuid();
    private ulong _nextId = 1, _revision, _tick;
    private bool _updating, _preparing, _initializing;
    private int _readOnlyDepth;
    private string _name;

    public World(string name = "Untitled", ComponentRegistry? components = null)
    {
        ValidateName(name);
        _name = name;
        Components = components ?? ComponentRegistry.CreateDefault();
        Components.Freeze();
    }

    public ComponentRegistry Components { get; }
    public string Name { get { VerifyAccess(); return _name; } }
    public Guid Identity { get { VerifyAccess(); return _identity; } }
    public ulong Revision { get { VerifyAccess(); return _revision; } }
    public ulong Tick { get { VerifyAccess(); return _tick; } }
    internal bool IsUpdating { get { VerifyAccess(); return _updating; } }
    internal bool IsCommittedBoundary { get { VerifyAccess(); return !_updating && !_preparing; } }
    public int Count { get { VerifyAccess(); return _objects.Count; } }

    public GameObject CreateObject(string name, Guid? persistentId = null)
    {
        VerifyStructuralAccess();
        ValidateName(name);
        Guid uuid = persistentId ?? Guid.NewGuid();
        if (uuid == Guid.Empty || _uuidIndex.ContainsKey(uuid)) throw new ArgumentException("Object UUID must be valid and unique.");
        if (_objects.Count >= MaxObjects || _nextId == ulong.MaxValue) throw new InvalidOperationException("Object capacity exceeded.");
        ulong revision = checked(_revision + 1);
        ulong id = _nextId;
        int slot = FreeSlot(_slots);
        var entry = new Entry(uuid, name, slot);
        _objects.EnsureCapacity(_objects.Count + 1);
        _uuidIndex.EnsureCapacity(_uuidIndex.Count + 1);
        _objects.Add(id, entry);
        _uuidIndex.Add(uuid, id);
        _slots[slot] = true;
        _nextId++;
        _revision = revision;
        return new GameObject(this, new(_identity, id));
    }

    public GameObject FindObject(Guid uuid)
    {
        VerifyAccess();
        if (!_uuidIndex.TryGetValue(uuid, out ulong id)) throw new ArgumentException("Unknown object UUID.");
        return new GameObject(this, new(_identity, id));
    }

    public IReadOnlyList<GameObject> GetObjects()
    {
        VerifyAccess();
        return _objects.Keys.Order().Select(id => new GameObject(this, new(_identity, id))).ToArray();
    }

    public WorldSnapshot CaptureSnapshot()
    {
        VerifyAccess();
        return Capture(_name, _objects, _storage);
    }

    private WorldSnapshot Capture(string name, Dictionary<ulong, Entry> objects, ComponentStorage storage) =>
        new(1, name, objects.OrderBy(pair => pair.Key).Select(pair => new ObjectSnapshot(
            pair.Value.PersistentId, pair.Value.Name, storage.Capture(pair.Value.Slot, Components))).ToArray());

    private static int FreeSlot(bool[] slots)
    {
        int slot = Array.IndexOf(slots, false);
        return slot >= 0 ? slot : throw new InvalidOperationException("Object capacity exceeded.");
    }

    public string SerializeSnapshot()
    {
        return Encoding.UTF8.GetString(SceneJson.EncodeBounded(CaptureSnapshot(), MaxSnapshotBytes));
    }

    public void LoadSnapshot(string json)
    {
        VerifyStructuralAccess();
        if (Encoding.UTF8.GetByteCount(json) > MaxSnapshotBytes) throw new ArgumentException("Snapshot size limit exceeded.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        SceneJson.RejectDuplicateFields(document.RootElement);
        RestoreSnapshot(document.RootElement.Deserialize<WorldSnapshot>(SceneJson.Options) ?? throw new ArgumentException("Missing snapshot."));
    }

    // Full restore invalidates ALL old handles. Persistent UUIDs must be resolved again.
    public void RestoreSnapshot(WorldSnapshot snapshot)
    {
        PrepareRestore(snapshot)();
    }

    // Internal prepared commit: all validation/allocation precedes installation. No extension callbacks at commit.
    internal Action PrepareRestore(WorldSnapshot snapshot, Action<WorldSnapshot>? validateCandidate = null)
    {
        VerifyStructuralAccess();
        VerifyNoAuthorities();
        _preparing = true;
        try
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (snapshot.Version != 1 || snapshot.Objects is null || snapshot.Objects.Length > MaxObjects)
                throw new ArgumentException("Unsupported or oversized managed snapshot.");
            ValidateName(snapshot.Name);
            _ = SceneJson.EncodeBounded(snapshot, MaxSnapshotBytes);
            ulong revision = checked(_revision + 1);
            ulong nextId = _nextId;
            var objects = new Dictionary<ulong, Entry>();
            var uuids = new Dictionary<Guid, ulong>();
            var storage = new ComponentStorage();
            var slots = new bool[MaxObjects];
            foreach (var item in snapshot.Objects)
            {
                if (item is null || item.PersistentId == Guid.Empty || uuids.ContainsKey(item.PersistentId) ||
                    item.Components is null || item.Components.Length > MaxComponentsPerObject || nextId == ulong.MaxValue)
                    throw new ArgumentException("Invalid object record, duplicate UUID or capacity exceeded.");
                ValidateName(item.Name);
                int slot = objects.Count;
                foreach (var component in item.Components)
                {
                    if (component is null) throw new ArgumentException("Missing component record.");
                    object value = Components.Decode(component);
                    if (storage.Has(slot, value.GetType())) throw new ArgumentException("Duplicate component type.");
                    storage.SetDecoded(slot, value, Components);
                }
                slots[slot] = true;
                objects.Add(nextId, new Entry(item.PersistentId, item.Name, slot));
                uuids.Add(item.PersistentId, nextId++);
            }
            var normalized = Capture(snapshot.Name, objects, storage);
            _ = SceneJson.EncodeBounded(normalized, MaxSnapshotBytes);
            validateCandidate?.Invoke(normalized);
            if (_revision != revision - 1)
                throw new InvalidOperationException("A trusted validator mutated the World during snapshot validation.");
            string name = snapshot.Name;
            Guid identity = Guid.NewGuid();
            return () =>
            {
                VerifyStructuralAccess();
                VerifyNoAuthorities();
                if (_revision != revision - 1) throw new InvalidOperationException("World changed after preparing restore.");
                _objects = objects;
                _uuidIndex = uuids;
                _storage = storage;
                _slots = slots;
                _nextId = nextId;
                _name = name;
                _identity = identity;
                _revision = revision;
            };
        }
        finally { _preparing = false; }
    }

    internal void VerifyAccess()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread) throw new InvalidOperationException("World access requires its owner thread.");
    }
    private void VerifyStructuralAccess()
    {
        VerifyAccess();
        VerifyWriteAccess();
        if (_updating || _preparing) throw new InvalidOperationException("Structural/editor mutation is not allowed inside an update/restore preparation.");
    }
    internal void VerifyWriteAccess()
    {
        VerifyAccess();
        if (_readOnlyDepth != 0) {
            var error=new InvalidOperationException("World mutation is forbidden in a read-only gameplay callback.");
            RejectStep(error);throw error;
        }
    }
    internal IDisposable ReadOnly()
    {
        VerifyAccess();
        if (_updating || _preparing) throw new InvalidOperationException("Read-only callbacks require a committed boundary.");
        var scope = new ReadScope(this);
        _readOnlyDepth++;
        return scope;
    }
    private sealed class ReadScope(World world) : IDisposable
    {
        private bool _disposed;
        public void Dispose() { world.VerifyAccess(); if (_disposed) return; world._readOnlyDepth--; _disposed = true; }
    }
    internal IDisposable PreparationReadOnly()
    {
        VerifyAccess(); var scope = new ReadScope(this); _readOnlyDepth++; return scope;
    }
    internal ObjectReference CandidateReference(Guid uuid)
    {
        VerifyAccess(); var target = StepEntry(uuid); return new(_identity, target.Id);
    }
    private Entry Require(ObjectReference reference)
    {
        VerifyAccess();
        if (reference.WorldId != _identity || !_objects.TryGetValue(reference.Id, out var entry))
            throw new ArgumentException("Stale or foreign object reference.");
        return entry;
    }
    internal Guid GetUuid(ObjectReference reference) => Require(reference).PersistentId;
    internal string GetName(ObjectReference reference) => Require(reference).Name;
    internal void Rename(ObjectReference reference, string name)
    {
        VerifyStructuralAccess();
        ValidateName(name);
        Entry entry = Require(reference);
        ulong revision = checked(_revision + 1);
        _objects[reference.Id] = entry with { Name = name };
        _revision = revision;
    }
    internal void Destroy(ObjectReference reference)
    {
        VerifyObjectStructure(Require(reference).PersistentId);
        VerifyStructuralAccess();
        Entry entry = Require(reference);
        ulong revision = checked(_revision + 1);
        _storage.RemoveAll(entry.Slot);
        _objects.Remove(reference.Id);
        _uuidIndex.Remove(entry.PersistentId);
        _slots[entry.Slot] = false;
        _revision = revision;
    }
    internal bool Has<T>(ObjectReference reference) where T : struct, IComponent => _storage.Has(Require(reference).Slot, typeof(T));
    internal T Get<T>(ObjectReference reference) where T : struct, IComponent => _storage.Get<T>(Require(reference).Slot);
    internal void Set<T>(ObjectReference reference, T value) where T : struct, IComponent
    {
        VerifyAccess();
        try { SetCore(reference, value); }
        catch (Exception error) { if (_updating) _stepError ??= error; throw; }
    }
    private void SetCore<T>(ObjectReference reference, T value, ComponentAuthority? authority = null) where T : struct, IComponent
    {
        VerifyWriteAccess();
        if (_preparing) throw new InvalidOperationException("Mutation during restore preparation.");
        Entry entry = Require(reference);
        Type type = typeof(T);
        VerifyComponentAuthority(entry.PersistentId, type, authority);
        if (!_storage.Has(entry.Slot, type)) { VerifyObjectStructure(entry.PersistentId); VerifyComponentTopology(type); }
        T validated;
        _preparing = true;
        try { validated = Components.Validate(value); } finally { _preparing = false; }
        if (_updating)
        {
            var candidate = StepEntry(entry.PersistentId);
            if (!_stepStorage!.Has(candidate.Entry.Slot, type)) throw new InvalidOperationException("Component was removed or is not attached.");
            _stepStorage.Set(candidate.Entry.Slot, validated);
        }
        else
        {
            if (!_storage.Has(entry.Slot, type) && _storage.Count(entry.Slot) >= MaxComponentsPerObject)
                throw new InvalidOperationException("Component capacity exceeded.");
            ulong revision = checked(_revision + 1);
            _storage.Set(entry.Slot, validated);
            _revision = revision;
        }
    }
    internal bool Remove<T>(ObjectReference reference) where T : struct, IComponent
    {
        VerifyObjectStructure(Require(reference).PersistentId);
        VerifyComponentTopology(typeof(T));
        VerifyStructuralAccess();
        Entry entry = Require(reference);
        if (!_storage.Has(entry.Slot, typeof(T))) return false;
        ulong revision = checked(_revision + 1);
        _storage.Remove(entry.Slot, typeof(T));
        _revision = revision;
        return true;
    }
    internal void RejectStep(Exception error) { VerifyAccess(); if (_updating) _stepError ??= error; }
    private void VerifyCommandAccess()
    {
        VerifyWriteAccess();
        if (!_updating || _initializing || _preparing) throw new InvalidOperationException("Runtime commands require a fixed step.");
    }
    private (ulong Id, Entry Entry) StepEntry(Guid uuid)
    {
        if (_stepUuids is null || !_stepUuids.TryGetValue(uuid, out ulong id)) throw new ArgumentException("Unknown or destroyed command object.");
        return (id, _stepObjects![id]);
    }
    internal void StageCreate(Guid uuid, string name)
    {
        VerifyCommandAccess(); ValidateName(name);
        if (uuid == Guid.Empty || _stepUuids!.ContainsKey(uuid) || _uuidIndex.ContainsKey(uuid)) throw new ArgumentException("Duplicate or invalid object UUID.");
        if (_stepObjects!.Count >= MaxObjects || _stepNextId == ulong.MaxValue) throw new InvalidOperationException("Object capacity exceeded.");
        ulong id = _stepNextId;
        int slot = FreeSlot(_stepSlots!);
        var entry = new Entry(uuid, name, slot);
        _stepObjects.EnsureCapacity(_stepObjects.Count + 1);
        _stepUuids.EnsureCapacity(_stepUuids.Count + 1);
        _stepObjects.Add(id, entry); _stepUuids.Add(uuid, id);
        _stepSlots![slot] = true;
        _stepNextId++;
    }
    internal void StageDestroy(Guid uuid)
    {
        VerifyObjectStructure(uuid);
        VerifyCommandAccess(); var target = StepEntry(uuid);
        _stepStorage!.RemoveAll(target.Entry.Slot);
        _stepObjects!.Remove(target.Id); _stepUuids!.Remove(uuid);
        _stepSlots![target.Entry.Slot] = false;
    }
    internal void StageRename(Guid uuid, string name)
    {
        VerifyCommandAccess(); ValidateName(name); var target = StepEntry(uuid);
        _stepObjects![target.Id] = target.Entry with { Name = name };
    }
    internal void StageAdd(Guid uuid, ComponentSnapshot value)
    {
        VerifyObjectStructure(uuid);
        VerifyCommandAccess(); var target = StepEntry(uuid);
        object decoded;
        _preparing = true; try { decoded = Components.Decode(value); } finally { _preparing = false; }
        Type type = decoded.GetType();
        Components.VerifyRuntimeAttachment(type);
        VerifyComponentTopology(type);
        if (_stepStorage!.Has(target.Entry.Slot, type)) throw new ArgumentException("Component already attached.");
        if (_stepStorage.Count(target.Entry.Slot) >= MaxComponentsPerObject) throw new ArgumentException("Component capacity exceeded.");
        _stepStorage.SetDecoded(target.Entry.Slot, decoded, Components);
    }
    internal void StageRemove(Guid uuid, string typeId)
    {
        VerifyObjectStructure(uuid);
        VerifyCommandAccess(); var target = StepEntry(uuid);
        Type? type = _stepStorage!.FindType(target.Entry.Slot, typeId, Components);
        if (type is null) throw new ArgumentException("Component is not attached.");
        VerifyComponentTopology(type);
        _stepStorage.Remove(target.Entry.Slot, type);
    }
    internal void RequireCommandTarget(Guid uuid) { VerifyCommandAccess(); _ = StepEntry(uuid); }
    internal void BeginStep(bool initialization = false)
    {
        VerifyStructuralAccess();
        _ = checked(_revision + 1);
        if (!initialization) _ = checked(_tick + 1);
        var objects = new Dictionary<ulong, Entry>(_objects);
        var uuids = new Dictionary<Guid, ulong>(_uuidIndex);
        var slots = (bool[])_slots.Clone();
        var storage = _storage.Fork();
        _authorityWrites.EnsureCapacity(_authorities.Count);
        _authorityWrites.Clear();
        _stepObjects = objects; _stepUuids = uuids; _stepNextId = _nextId; _stepError = null;
        _stepSlots = slots; _stepStorage = storage;
        _initializing = initialization;
        _updating = true;
    }
    internal Action PrepareStep(Action<WorldSnapshot>? inspect = null, bool requireAuthorityWrites = true)
    {
        VerifyWriteAccess();
        if (!_updating || _preparing) throw new InvalidOperationException("No active gameplay step.");
        _preparing = true;
        try
        {
            if (_stepError is not null) throw new InvalidOperationException("Runtime command rejected; step must abort.", _stepError);
            if (requireAuthorityWrites) VerifyAuthorityWrites();
            var objects = _stepObjects!; var uuids = _stepUuids!; ulong nextId = _stepNextId;
            var storage = _stepStorage!; var slots = _stepSlots!;
            var candidate = Capture(_name, objects, storage);
            _ = SceneJson.EncodeBounded(candidate, MaxSnapshotBytes);
            inspect?.Invoke(candidate);
            if (_stepError is not null) throw new InvalidOperationException("Candidate validation rejected; step must abort.", _stepError);
            return () => { _objects = objects; _uuidIndex = uuids; _storage = storage; _slots = slots; _nextId = nextId; _revision++; if (!_initializing) _tick++; AbortStep(); };
        }
        finally { _preparing = false; }
    }
    internal void CommitStep()
    {
        VerifyAccess();
        if (!_updating) throw new InvalidOperationException("No active gameplay step.");
        PrepareStep()();
    }
    internal void AbortStep()
    {
        VerifyAccess();
        _stepObjects = null; _stepUuids = null; _stepStorage = null; _stepSlots = null; _stepError = null;
        _authorityWrites.Clear();
        _updating = false;
        _initializing = false;
    }
    internal GameObject Resolve(ObjectReference reference) { _ = Require(reference); return new(this, reference); }
    internal void FixedStep(Action update)
    {
        VerifyStructuralAccess();
        BeginStep();
        try
        {
            update();
            CommitStep();
        }
        finally { AbortStep(); }
    }
    internal static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 256 || name.Any(char.IsControl))
            throw new ArgumentException("Names must be nonempty, <=256 characters and contain no control characters.");
    }
}

public sealed class GameObject
{
    internal GameObject(World world, ObjectReference reference) { World = world; Reference = reference; }
    public World World { get; }
    public ObjectReference Reference { get; }
    public Guid PersistentId => World.GetUuid(Reference);
    public string Name { get => World.GetName(Reference); set => World.Rename(Reference, value); }
    public bool Has<T>() where T : struct, IComponent => World.Has<T>(Reference);
    public T Get<T>() where T : struct, IComponent => World.Get<T>(Reference);
    public void Set<T>(T value) where T : struct, IComponent => World.Set(Reference, value);
    public bool Remove<T>() where T : struct, IComponent => World.Remove<T>(Reference);
    public void Destroy() => World.Destroy(Reference);
}
