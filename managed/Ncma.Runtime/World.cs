using System.Text;
using System.Text.Json;

namespace Ncma.Runtime;

public readonly record struct ObjectReference(Guid WorldId, ulong Id);
public sealed record ComponentSnapshot(string TypeId, int Version, JsonElement Data);
public sealed record ObjectSnapshot(Guid PersistentId, string Name, ComponentSnapshot[] Components);
public sealed record WorldSnapshot(int Version, string Name, ObjectSnapshot[] Objects);

// Independent managed authority. Never mirrors a live legacy native SceneWorld.
public sealed class World
{
    public const int MaxObjects = 4096;
    public const int MaxComponentsPerObject = 64;
    public const int MaxSnapshotBytes = 4 * 1024 * 1024;
    private sealed record Entry(Guid PersistentId, string Name, Dictionary<Type, object> Components);
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private Dictionary<ulong, Entry> _objects = new();
    private Dictionary<Guid, ulong> _uuidIndex = new();
    private readonly Dictionary<(ulong, Type), object> _pending = new();
    private Guid _identity = Guid.NewGuid();
    private ulong _nextId = 1, _revision, _tick;
    private bool _updating;
    private string _name;

    public World(string name = "Untitled", ComponentRegistry? components = null)
    {
        ValidateName(name);
        _name = name;
        Components = components ?? ComponentRegistry.CreateDefault();
        Components.Freeze();
    }

    public ComponentRegistry Components { get; }
    public Guid Identity { get { VerifyAccess(); return _identity; } }
    public ulong Revision { get { VerifyAccess(); return _revision; } }
    public ulong Tick { get { VerifyAccess(); return _tick; } }
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
        _objects.Add(id, new Entry(uuid, name, new()));
        _uuidIndex.Add(uuid, id);
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
        return new(1, _name, _objects.OrderBy(pair => pair.Key).Select(pair => new ObjectSnapshot(
            pair.Value.PersistentId, pair.Value.Name, pair.Value.Components.Values
                .OrderBy(value => Components.Describe(value.GetType()).TypeId, StringComparer.Ordinal)
                .Select(value => new ComponentSnapshot(Components.Describe(value.GetType()).TypeId,
                    Components.Describe(value.GetType()).Version, Components.EncodeObject(value))).ToArray())).ToArray());
    }

    public string SerializeSnapshot()
    {
        string json = JsonSerializer.Serialize(CaptureSnapshot(), SceneJson.Options);
        if (Encoding.UTF8.GetByteCount(json) > MaxSnapshotBytes) throw new ArgumentException("Snapshot size limit exceeded.");
        return json;
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
        VerifyStructuralAccess();
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Version != 1 || snapshot.Objects is null || snapshot.Objects.Length > MaxObjects)
            throw new ArgumentException("Unsupported or oversized managed snapshot.");
        ValidateName(snapshot.Name);
        if (JsonSerializer.SerializeToUtf8Bytes(snapshot, SceneJson.Options).Length > MaxSnapshotBytes)
            throw new ArgumentException("Snapshot size limit exceeded.");
        ulong revision = checked(_revision + 1);
        ulong nextId = _nextId;
        var objects = new Dictionary<ulong, Entry>();
        var uuids = new Dictionary<Guid, ulong>();
        foreach (var item in snapshot.Objects)
        {
            if (item is null || item.PersistentId == Guid.Empty || uuids.ContainsKey(item.PersistentId) ||
                item.Components is null || item.Components.Length > MaxComponentsPerObject || nextId == ulong.MaxValue)
                throw new ArgumentException("Invalid object record, duplicate UUID or capacity exceeded.");
            ValidateName(item.Name);
            var values = new Dictionary<Type, object>();
            foreach (var component in item.Components)
            {
                if (component is null) throw new ArgumentException("Missing component record.");
                object value = Components.Decode(component);
                if (!values.TryAdd(value.GetType(), value)) throw new ArgumentException("Duplicate component type.");
            }
            objects.Add(nextId, new Entry(item.PersistentId, item.Name, values));
            uuids.Add(item.PersistentId, nextId++);
        }
        if (_revision != revision - 1)
            throw new InvalidOperationException("A trusted validator mutated the World during snapshot validation.");
        _objects = objects;
        _uuidIndex = uuids;
        _nextId = nextId;
        _name = snapshot.Name;
        _identity = Guid.NewGuid();
        _revision = revision;
    }

    internal void VerifyAccess()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread) throw new InvalidOperationException("World access requires its owner thread.");
    }
    private void VerifyStructuralAccess()
    {
        VerifyAccess();
        if (_updating) throw new InvalidOperationException("Structural/editor mutation is not allowed inside a fixed step.");
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
        VerifyStructuralAccess();
        Entry entry = Require(reference);
        ulong revision = checked(_revision + 1);
        _objects.Remove(reference.Id);
        _uuidIndex.Remove(entry.PersistentId);
        _revision = revision;
    }
    internal bool Has<T>(ObjectReference reference) where T : struct, IComponent => Require(reference).Components.ContainsKey(typeof(T));
    internal T Get<T>(ObjectReference reference) where T : struct, IComponent =>
        Require(reference).Components.TryGetValue(typeof(T), out var value) ? (T)value : throw new ArgumentException("Component is not attached.");
    internal void Set(ObjectReference reference, object value)
    {
        Entry entry = Require(reference);
        object validated = Components.Validate(value);
        Type type = validated.GetType();
        if (_updating)
        {
            if (!entry.Components.ContainsKey(type)) throw new InvalidOperationException("Attach components outside the fixed step.");
            _pending[(reference.Id, type)] = validated;
        }
        else
        {
            if (!entry.Components.ContainsKey(type) && entry.Components.Count >= MaxComponentsPerObject)
                throw new InvalidOperationException("Component capacity exceeded.");
            ulong revision = checked(_revision + 1);
            entry.Components[type] = validated;
            _revision = revision;
        }
    }
    internal bool Remove<T>(ObjectReference reference) where T : struct, IComponent
    {
        VerifyStructuralAccess();
        Entry entry = Require(reference);
        if (!entry.Components.ContainsKey(typeof(T))) return false;
        ulong revision = checked(_revision + 1);
        entry.Components.Remove(typeof(T));
        _revision = revision;
        return true;
    }
    internal void FixedStep(Action update)
    {
        VerifyStructuralAccess();
        ulong revision = checked(_revision + 1), tick = checked(_tick + 1);
        _updating = true;
        try
        {
            update();
            foreach (var write in _pending) _objects[write.Key.Item1].Components[write.Key.Item2] = write.Value;
            _revision = revision;
            _tick = tick;
        }
        finally { _pending.Clear(); _updating = false; }
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
