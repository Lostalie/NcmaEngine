namespace Ncma.Runtime;

/// <summary>
/// Caller-owned, reusable query over committed entities. Retain the plan, not an enumerator.
/// Values are read through World.Get and remain copies. There are no writable component leases.
/// </summary>
public sealed class EntityQuery
{
    public const int MaxTerms = 16;
    private readonly World _world;
    private readonly Guid _identity;
    private readonly Type[] _all, _none;
    private readonly Entity[] _members;
    private readonly IComparer<Entity> _order;
    private ulong _version;
    private bool _cached;
    private int _count;

    internal EntityQuery(World world, Type[] all, Type[] none, int capacity)
    {
        _world = world;
        _identity = world.Identity;
        _all = all;
        _none = none;
        _members = new Entity[capacity];
        _order = new CreationOrder(world);
    }

    public int Count { get { Refresh(); return _count; } }

    /// <summary>Copies all results or rejects without changing the destination. Never truncates.</summary>
    public int CopyTo(Span<Entity> destination)
    {
        Refresh();
        if (destination.Length < _count) throw new ArgumentException("Query output capacity exceeded.");
        _members.AsSpan(0, _count).CopyTo(destination);
        return _count;
    }

    public Enumerator GetEnumerator()
    {
        Refresh();
        return new(_world, _identity, _version, _members, _count);
    }

    private void Refresh()
    {
        _world.VerifyQueryIdentity(_identity);
        ulong version = _world.MembershipVersion;
        if (_cached && _version == version) return;
        _cached = false;
        int count = _world.FillQuery(_all, _none, _members);
        Array.Sort(_members, 0, count, _order);
        _members.AsSpan(count).Clear();
        _count = count;
        _version = version;
        _cached = true;
    }

    private sealed class CreationOrder(World world) : IComparer<Entity>
    {
        public int Compare(Entity x, Entity y) => world.CompareCreationOrder(x, y);
    }

    // Pattern enumeration deliberately avoids IEnumerable boxing and cannot survive an await.
    public ref struct Enumerator
    {
        private readonly World? _world;
        private readonly Guid _identity;
        private readonly ulong _version;
        private readonly Entity[] _members;
        private readonly int _count;
        private int _index;

        internal Enumerator(World world, Guid identity, ulong version, Entity[] members, int count)
        { _world = world; _identity = identity; _version = version; _members = members; _count = count; _index = -1; }

        public Entity Current
        {
            get
            {
                Verify();
                if ((uint)_index >= (uint)_count) throw new InvalidOperationException("Query enumerator is not positioned on a member.");
                return _members[_index];
            }
        }

        public bool MoveNext()
        {
            Verify();
            if (_index < _count) ++_index;
            return _index < _count;
        }

        private void Verify()
        {
            if (_world is null) throw new InvalidOperationException("Uninitialized query enumerator.");
            _world.VerifyQueryIdentity(_identity);
            if (_world.MembershipVersion != _version) throw new InvalidOperationException("Query iteration invalidated by a structural commit.");
        }
    }
}

public sealed partial class World
{
    /// <summary>
    /// Creates a bounded All/None plan; empty All includes empty entities. Types must be registered,
    /// unique and disjoint. The plan is invalid after World restore; create a new plan then.
    /// </summary>
    public EntityQuery CreateQuery(ReadOnlySpan<Type> all, ReadOnlySpan<Type> none, int capacity = MaxObjects)
    {
        VerifyAccess();
        if (capacity < 1 || capacity > MaxObjects || all.Length > EntityQuery.MaxTerms ||
            none.Length > EntityQuery.MaxTerms - all.Length) throw new ArgumentException("Query term or result budget exceeded.");
        var unique = new HashSet<Type>();
        foreach (Type type in all) Validate(type);
        foreach (Type type in none) Validate(type);
        return new EntityQuery(this, all.ToArray(), none.ToArray(), capacity);

        void Validate(Type type)
        {
            if (type is null || !unique.Add(type)) throw new ArgumentException("Query types must be non-null, unique and disjoint.");
            _ = Components.Describe(type);
        }
    }

    public EntityQuery Query<T>() where T : struct, IComponent => CreateQuery([typeof(T)], []);
    public EntityQuery Query<T1, T2>() where T1 : struct, IComponent where T2 : struct, IComponent => CreateQuery([typeof(T1), typeof(T2)], []);
    public EntityQuery Query<T1, T2, T3>() where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent =>
        CreateQuery([typeof(T1), typeof(T2), typeof(T3)], []);

    internal void VerifyQueryIdentity(Guid identity)
    {
        VerifyAccess();
        if (_identity != identity) throw new InvalidOperationException("Query belongs to a replaced World identity.");
    }

    internal int CompareCreationOrder(Entity a, Entity b) => _objects[a.Index].CreationOrdinal.CompareTo(_objects[b.Index].CreationOrdinal);

    internal int FillQuery(Type[] all, Type[] none, Entity[] output)
    {
        VerifyAccess();
        IComponentPool? smallest = null;
        foreach (Type type in all)
        {
            var pool = _storage.FindPool(type);
            if (pool is null || pool.Count == 0) return 0;
            if (smallest is null || pool.Count < smallest.Count) smallest = pool;
        }
        int count = 0;
        if (smallest is null)
        {
            foreach (var entry in _objects.Values) Add(entry);
        }
        else
        {
            for (int i = 0; i < smallest.Count; ++i) Add(_objects[smallest.SlotAt(i)]);
        }
        return count;

        void Add(Entry entry)
        {
            foreach (Type type in all) if (!_storage.Has(entry.Slot, type)) return;
            foreach (Type type in none) if (_storage.Has(entry.Slot, type)) return;
            if (count == output.Length) throw new InvalidOperationException("Query result budget exceeded; no truncated result is available.");
            output[count++] = IdentityOf(entry);
        }
    }
}
