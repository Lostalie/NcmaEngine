using Ncma.Runtime;

namespace Ncma.Scene.Rendering;

/// <summary>Caller-owned committed reads. Retains a query plan, never a component value or write authority.</summary>
public sealed class SceneComponentReader<T> where T : struct, IComponent
{
    private readonly World _world;
    private EntityQuery? _query;
    private Guid _identity;
    private int _capacity;

    public SceneComponentReader(World world)
    { ArgumentNullException.ThrowIfNull(world); _ = world.Identity; _world = world; }

    /// <summary>Returns the complete member count. Value is copied only for exactly one member.</summary>
    public int ReadSingle(Guid expectedWorld, out T value)
    {
        var query = GetQuery(expectedWorld);
        value = default;
        int count = query?.Count ?? 0;
        if (count == 1) foreach (var entity in query!) { value = _world.Get<T>(entity); break; }
        return count;
    }

    internal EntityQuery? GetQuery(Guid expectedWorld)
    {
        // Check the caller's resource/session stamp BEFORE rebuilding after a restore.
        SceneEnvironmentState.VerifyBoundary(_world, expectedWorld);
        if (!_world.Components.IsRegistered<T>()) return null;
        int needed = _world.Count;
        if (_query is null || _identity != expectedWorld || needed > _capacity)
        {
            int capacity = 16;
            while (capacity < needed) capacity *= 2;
            var query = _world.CreateQuery([typeof(T)], [], Math.Min(capacity, World.MaxObjects));
            _query = query; _identity = expectedWorld; _capacity = capacity;
        }
        return _query;
    }
}

// Internal membership copies only. Extraction holds a World read scope while consuming the span.
// Geometry includes malformed members so the existing diagnostics remain visible.
internal sealed class SceneGeometryQuery
{
    private readonly World _world;
    private readonly SceneComponentReader<StaticMeshData> _static;
    private readonly SceneComponentReader<SkinnedMeshData> _skin;
    private readonly IComparer<Entity> _order;
    private Entity[] _members = [];
    private Guid _identity;
    private ulong _version;
    private int _count;
    private bool _cached;

    internal SceneGeometryQuery(World world)
    { _world = world; _static = new(world); _skin = new(world); _order = new CreationOrder(world); }

    internal ReadOnlySpan<Entity> Read(Guid expectedWorld)
    {
        SceneEnvironmentState.VerifyBoundary(_world, expectedWorld);
        ulong version = _world.MembershipVersion;
        if (_cached && _identity == expectedWorld && _version == version) return _members.AsSpan(0, _count);
        _cached = false;
        var statics = _static.GetQuery(expectedWorld);
        var skins = _skin.GetQuery(expectedWorld);
        int maximum = Math.Min(_world.Count, (statics?.Count ?? 0) + (skins?.Count ?? 0));
        if (_members.Length < maximum) _members = new Entity[maximum];
        int count = 0;
        if (statics is not null) foreach (var entity in statics) _members[count++] = entity;
        if (skins is not null) foreach (var entity in skins)
            if (!_world.Has<StaticMeshData>(entity)) _members[count++] = entity;
        // Both plans produced current identities at this committed owner-thread boundary.
        Array.Sort(_members, 0, count, _order);
        _members.AsSpan(count).Clear();
        _count = count; _identity = expectedWorld; _version = version; _cached = true;
        return _members.AsSpan(0, count);
    }

    private sealed class CreationOrder(World world) : IComparer<Entity>
    { public int Compare(Entity a, Entity b) => world.CompareCreationOrder(a, b); }
}
