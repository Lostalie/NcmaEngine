using System.Text;
using System.Text.Json;

namespace Ncma.Runtime;

public sealed record ComponentSnapshot(string TypeId, int Version, JsonElement Data);
public sealed record ObjectSnapshot(Guid PersistentId, string Name, ComponentSnapshot[] Components);
public sealed record WorldSnapshot(int Version, string Name, ObjectSnapshot[] Objects);

// Sole scene authority; native hosts receive copied render/inspection data only.
public sealed partial class World
{
    public const int MaxObjects = 4096;
    public const int MaxComponentsPerObject = 64;
    public const int MaxSnapshotBytes = 4 * 1024 * 1024;
    private sealed record Entry(Guid PersistentId, string Name, int Slot, ulong Generation, ulong CreationOrdinal);
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private Dictionary<int, Entry> _objects = new();
    private Dictionary<Guid, int> _uuidIndex = new();
    private Dictionary<int, Entry>? _stepObjects;
    private Dictionary<Guid, int>? _stepUuids;
    private ComponentStorage _storage = new();
    private ComponentStorage? _stepStorage;
    private bool[] _slots = new bool[MaxObjects];
    private bool[]? _stepSlots;
    private EntityGenerations _generations = new();
    private Exception? _stepError;
    private Guid _identity = Guid.NewGuid();
    private ulong _nextOrdinal = 1, _revision, _tick;
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

    public Entity CreateEntity(string name, Guid? persistentId = null)
    {
        VerifyStructuralAccess();
        ValidateName(name);
        Guid uuid = persistentId ?? Guid.NewGuid();
        if (uuid == Guid.Empty || _uuidIndex.ContainsKey(uuid)) throw new ArgumentException("Object UUID must be valid and unique.");
        if (_objects.Count >= MaxObjects || _nextOrdinal == ulong.MaxValue) throw new InvalidOperationException("Object capacity exceeded.");
        ulong revision = checked(_revision + 1);
        _objects.EnsureCapacity(_objects.Count + 1);
        _uuidIndex.EnsureCapacity(_uuidIndex.Count + 1);
        var (slot, generation) = _generations.Reserve(_slots);
        var entry = new Entry(uuid, name, slot, generation, _nextOrdinal++);
        var result = new Entity(_identity, slot, generation);
        _objects.Add(slot, entry);
        _uuidIndex.Add(uuid, slot);
        _slots[slot] = true;
        _revision = revision;
        return result;
    }

    public Entity FindEntity(Guid uuid)
    {
        VerifyAccess();
        if (!_uuidIndex.TryGetValue(uuid, out int slot)) throw new ArgumentException("Unknown object UUID.");
        return IdentityOf(_objects[slot]);
    }

    public IReadOnlyList<Entity> GetEntities()
    {
        VerifyAccess();
        return _objects.Values.OrderBy(e => e.CreationOrdinal).Select(IdentityOf).ToArray();
    }

    public WorldSnapshot CaptureSnapshot()
    {
        VerifyAccess();
        return Capture(_name, _objects, _storage);
    }

    private WorldSnapshot Capture(string name, Dictionary<int, Entry> objects, ComponentStorage storage) =>
        new(1, name, objects.OrderBy(pair => pair.Value.CreationOrdinal).Select(pair => new ObjectSnapshot(
            pair.Value.PersistentId, pair.Value.Name, storage.Capture(pair.Value.Slot, Components))).ToArray());

    private Entity IdentityOf(Entry entry) => new(_identity, entry.Slot, entry.Generation);

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
            ulong nextOrdinal = 1;
            var objects = new Dictionary<int, Entry>();
            var uuids = new Dictionary<Guid, int>();
            var storage = new ComponentStorage();
            var slots = new bool[MaxObjects];
            var generations = new EntityGenerations();
            foreach (var item in snapshot.Objects)
            {
                if (item is null || item.PersistentId == Guid.Empty || uuids.ContainsKey(item.PersistentId) ||
                    item.Components is null || item.Components.Length > MaxComponentsPerObject)
                    throw new ArgumentException("Invalid object record, duplicate UUID or capacity exceeded.");
                ValidateName(item.Name);
                var (slot, generation) = generations.Reserve(slots);
                foreach (var component in item.Components)
                {
                    if (component is null) throw new ArgumentException("Missing component record.");
                    object value = Components.Decode(component);
                    if (storage.Has(slot, value.GetType())) throw new ArgumentException("Duplicate component type.");
                    storage.SetDecoded(slot, value, Components);
                }
                slots[slot] = true;
                objects.Add(slot, new Entry(item.PersistentId, item.Name, slot, generation, nextOrdinal++));
                uuids.Add(item.PersistentId, slot);
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
                _generations = generations;
                _nextOrdinal = nextOrdinal;
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
    internal Entity CandidateEntity(Guid uuid)
    {
        VerifyAccess(); var target = StepEntry(uuid); return IdentityOf(target.Entry);
    }
    private Entry Require(Entity entity)
    {
        VerifyAccess();
        if (entity.WorldIdentity != _identity || entity.Generation == 0 ||
            (uint)entity.Index >= MaxObjects || !_objects.TryGetValue(entity.Index, out var entry) ||
            entry.Generation != entity.Generation)
            throw new ArgumentException("Stale, invalid or foreign Entity.");
        return entry;
    }
    public Guid GetUuid(Entity entity) => Require(entity).PersistentId;
    public string GetName(Entity entity) => Require(entity).Name;
    public void Rename(Entity entity, string name)
    {
        VerifyStructuralAccess();
        ValidateName(name);
        Entry entry = Require(entity);
        ulong revision = checked(_revision + 1);
        _objects[entity.Index] = entry with { Name = name };
        _revision = revision;
    }
    public void Destroy(Entity entity)
    {
        VerifyObjectStructure(Require(entity).PersistentId);
        VerifyStructuralAccess();
        Entry entry = Require(entity);
        ulong revision = checked(_revision + 1);
        _storage.RemoveAll(entry.Slot);
        _objects.Remove(entity.Index);
        _uuidIndex.Remove(entry.PersistentId);
        _slots[entry.Slot] = false;
        _revision = revision;
    }
    public bool Has<T>(Entity entity) where T : struct, IComponent => _storage.Has(Require(entity).Slot, typeof(T));
    public T Get<T>(Entity entity) where T : struct, IComponent => _storage.Get<T>(Require(entity).Slot);
    public void Set<T>(Entity entity, T value) where T : struct, IComponent
    {
        VerifyAccess();
        try { SetCore(entity, value); }
        catch (Exception error) { if (_updating) _stepError ??= error; throw; }
    }
    private void SetCore<T>(Entity entity, T value, ComponentAuthority? authority = null) where T : struct, IComponent
    {
        VerifyWriteAccess();
        if (_preparing) throw new InvalidOperationException("Mutation during restore preparation.");
        Entry entry = Require(entity);
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
    public bool Remove<T>(Entity entity) where T : struct, IComponent
    {
        VerifyObjectStructure(Require(entity).PersistentId);
        VerifyComponentTopology(typeof(T));
        VerifyStructuralAccess();
        Entry entry = Require(entity);
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
    private (int Id, Entry Entry) StepEntry(Guid uuid)
    {
        if (_stepUuids is null || !_stepUuids.TryGetValue(uuid, out int id)) throw new ArgumentException("Unknown or destroyed command object.");
        return (id, _stepObjects![id]);
    }
    internal void StageCreate(Guid uuid, string name)
    {
        VerifyCommandAccess(); ValidateName(name);
        if (uuid == Guid.Empty || _stepUuids!.ContainsKey(uuid) || _uuidIndex.ContainsKey(uuid)) throw new ArgumentException("Duplicate or invalid object UUID.");
        if (_stepObjects!.Count >= MaxObjects || _nextOrdinal == ulong.MaxValue) throw new InvalidOperationException("Object capacity exceeded.");
        _stepObjects.EnsureCapacity(_stepObjects.Count + 1);
        _stepUuids.EnsureCapacity(_stepUuids.Count + 1);
        var (slot, generation) = _generations.Reserve(_stepSlots!);
        var entry = new Entry(uuid, name, slot, generation, _nextOrdinal++);
        _stepObjects.Add(slot, entry); _stepUuids.Add(uuid, slot);
        _stepSlots![slot] = true;
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
        var objects = new Dictionary<int, Entry>(_objects);
        var uuids = new Dictionary<Guid, int>(_uuidIndex);
        var slots = (bool[])_slots.Clone();
        var storage = _storage.Fork();
        _authorityWrites.EnsureCapacity(_authorities.Count);
        _authorityWrites.Clear();
        _stepObjects = objects; _stepUuids = uuids; _stepError = null;
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
            var objects = _stepObjects!; var uuids = _stepUuids!;
            var storage = _stepStorage!; var slots = _stepSlots!;
            var candidate = Capture(_name, objects, storage);
            _ = SceneJson.EncodeBounded(candidate, MaxSnapshotBytes);
            inspect?.Invoke(candidate);
            if (_stepError is not null) throw new InvalidOperationException("Candidate validation rejected; step must abort.", _stepError);
            return () => { _objects = objects; _uuidIndex = uuids; _storage = storage; _slots = slots; _revision++; if (!_initializing) _tick++; AbortStep(); };
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
    public Entity Resolve(Entity entity) { _ = Require(entity); return entity; }
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
