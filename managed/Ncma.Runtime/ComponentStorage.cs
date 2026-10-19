using System.Text.Json;

namespace Ncma.Runtime;

// Internal bounded storage slots, not public identities. World validates handles before access.
internal interface IComponentPool
{
    int Count { get; }
    int SlotAt(int index);
    bool Has(int slot);
    IComponentPool Clone();
    void PrepareRemove(int slot);
    void Remove(int slot);
    void SetDecoded(int slot, object value);
    JsonElement Encode(int slot);
}

internal sealed class ComponentPool<T> : IComponentPool where T : struct, IComponent
{
    private int[] _sparse;
    private int[] _slots = [];
    private bool _metadataOwned = true;
    private readonly PagedValues<T> _values;
    private int _count;

    internal ComponentPool(int pageSize = PagedValues<T>.DefaultPageSize)
    { _values = new(pageSize); _sparse = new int[World.MaxObjects]; }
    private ComponentPool(ComponentPool<T> source)
    {
        _values = source._values.Fork();
        _sparse = source._sparse; _slots = source._slots;
        _metadataOwned = false; source._metadataOwned = false;
        _count = source._count;
    }

    public bool Has(int slot) => (uint)slot < World.MaxObjects && _sparse[slot] != 0;
    public int Count => _count;
    public int SlotAt(int index) => (uint)index < (uint)_count ? _slots[index] : throw new ArgumentOutOfRangeException(nameof(index));
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal T Get(int slot)
    {
        if ((uint)slot >= World.MaxObjects) throw new ArgumentException("Component is not attached.");
        int encoded = _sparse[slot];
        return encoded != 0 ? _values.ReadPresent(encoded - 1) : throw new ArgumentException("Component is not attached.");
    }

    internal void Set(int slot, T value)
    {
        if ((uint)slot >= World.MaxObjects) throw new ArgumentOutOfRangeException(nameof(slot));
        int encoded = _sparse[slot];
        if (encoded != 0) { _values.Write(encoded - 1, value); return; }
        PrepareMetadata(_count + 1); _values.PrepareWrite(_count);
        _slots[_count] = slot; _values.Write(_count, value); _sparse[slot] = _count + 1;
        _count++;
    }

    public void PrepareRemove(int slot)
    {
        if (!Has(slot)) return;
        int index = _sparse[slot] - 1;
        int last = _count - 1;
        PrepareMetadata(_count);
        _values.PrepareWrite(index); _values.PrepareWrite(last);
    }
    public void Remove(int slot)
    {
        if (!Has(slot)) return;
        PrepareRemove(slot);
        int index = _sparse[slot] - 1;
        int last = _count - 1;
        int moved = _slots[last];
        if (index != last)
        {
            _slots[index] = moved;
            _values.Write(index, _values.ReadPresent(last));
            _sparse[moved] = index + 1;
        }
        // Clear references (e.g. strings) and the removed membership, including last-element removal.
        _slots[last] = 0;
        _values.Write(last, default);
        _sparse[slot] = 0;
        _count = last;
    }

    public IComponentPool Clone() => new ComponentPool<T>(this);
    private void PrepareMetadata(int capacity)
    {
        if (_metadataOwned && capacity <= _slots.Length) return;
        var sparse = _metadataOwned ? _sparse : (int[])_sparse.Clone();
        int size = capacity <= _slots.Length ? _slots.Length : Math.Min(World.MaxObjects, Math.Max(4, _slots.Length * 2));
        var slots = new int[size]; _slots.CopyTo(slots, 0);
        _sparse = sparse; _slots = slots; _metadataOwned = true;
    }
    // Boxing is confined to heterogeneous document decoding, never Get<T>/Set<T> storage.
    public void SetDecoded(int slot, object value) => Set(slot, (T)value);
    public JsonElement Encode(int slot)
    {
        using var document = JsonDocument.Parse(SceneJson.EncodeBounded(Get(slot), ComponentRegistry.MaxPayloadBytes));
        return document.RootElement.Clone();
    }
}

internal sealed class ComponentStorage
{
    private readonly Dictionary<Type, IComponentPool> _pools;
    private readonly HashSet<Type> _owned = [];

    internal ComponentStorage() => _pools = [];
    private ComponentStorage(Dictionary<Type, IComponentPool> pools) => _pools = pools;

    // The pool directory is bounded by registered types; pool forks share immutable pages.
    internal ComponentStorage Fork()
    {
        var result = new ComponentStorage(new(_pools));
        _owned.Clear();
        return result;
    }

    internal bool Has(int slot, Type type) => _pools.TryGetValue(type, out var pool) && pool.Has(slot);
    internal IComponentPool? FindPool(Type type) => _pools.GetValueOrDefault(type);
    internal bool HasStableReadEncoding(ComponentRegistry registry) =>
        _pools.All(pair => pair.Value.Count == 0 || registry.CanCacheEncoding(pair.Key));
    internal MemberTypes TypesAt(int slot) => new(_pools, slot);
    // Internal owner-thread traversal; does not escape World as a query or writable lease.
    // Avoid a closure, LINQ iterator chain and boxed dictionary enumerator per prepared object.
    internal readonly struct MemberTypes(Dictionary<Type, IComponentPool> pools, int slot)
    {
        public Enumerator GetEnumerator() => new(pools, slot);
        internal struct Enumerator(Dictionary<Type, IComponentPool> pools, int slot)
        {
            private Dictionary<Type, IComponentPool>.Enumerator _iterator = pools.GetEnumerator();
            public readonly Type Current => _iterator.Current.Key;
            public bool MoveNext()
            {
                while (_iterator.MoveNext()) if (_iterator.Current.Value.Has(slot)) return true;
                return false;
            }
            public void Dispose() => _iterator.Dispose();
        }
    }
    internal T Get<T>(int slot) where T : struct, IComponent =>
        _pools.TryGetValue(typeof(T), out var pool) ? ((ComponentPool<T>)pool).Get(slot) :
            throw new ArgumentException("Component is not attached.");
    internal int Count(int slot)
    {
        int count = 0;
        foreach (var pool in _pools.Values) if (pool.Has(slot)) count++;
        return count;
    }

    internal void Set<T>(int slot, T value) where T : struct, IComponent
    {
        Type type = typeof(T);
        if (!_pools.ContainsKey(type))
        {
            _pools.EnsureCapacity(_pools.Count + 1);
            _owned.EnsureCapacity(_owned.Count + 1);
            var pool = new ComponentPool<T>();
            pool.Set(slot, value);
            _pools.Add(type, pool);
            _owned.Add(type);
        }
        else ((ComponentPool<T>)Writable(type)).Set(slot, value);
    }

    internal void SetDecoded(int slot, object value, ComponentRegistry registry)
    {
        Type type = value.GetType();
        if (!_pools.ContainsKey(type))
        {
            _pools.EnsureCapacity(_pools.Count + 1);
            _owned.EnsureCapacity(_owned.Count + 1);
            var pool = registry.CreatePool(type);
            pool.SetDecoded(slot, value);
            _pools.Add(type, pool);
            _owned.Add(type);
        }
        else Writable(type).SetDecoded(slot, value);
    }

    internal void Remove(int slot, Type type)
    {
        if (Has(slot, type)) Writable(type).Remove(slot);
    }

    internal void RemoveAll(int slot)
    {
        // Prepare every pool fork AND touched page before any value can be prefix-deleted.
        Type[] types = _pools.Where(pair => pair.Value.Has(slot)).Select(pair => pair.Key).ToArray();
        var pools = types.Select(Writable).ToArray();
        foreach (var pool in pools) pool.PrepareRemove(slot);
        foreach (var pool in pools) pool.Remove(slot);
    }

    internal Type? FindType(int slot, string typeId, ComponentRegistry registry)
    {
        foreach (var (type, pool) in _pools)
            if (pool.Has(slot) && registry.Describe(type).TypeId == typeId) return type;
        return null;
    }

    internal ComponentSnapshot[] Capture(int slot, ComponentRegistry registry) => _pools
        .Where(pair => pair.Value.Has(slot))
        .OrderBy(pair => registry.Describe(pair.Key).TypeId, StringComparer.Ordinal)
        .Select(pair => new ComponentSnapshot(registry.Describe(pair.Key).TypeId,
            registry.Describe(pair.Key).Version, pair.Value.Encode(slot))).ToArray();

    private IComponentPool Writable(Type type)
    {
        var pool = _pools[type];
        if (_owned.Contains(type)) return pool;
        var copy = pool.Clone();
        _owned.Add(type);
        _pools[type] = copy;
        return copy;
    }
}
