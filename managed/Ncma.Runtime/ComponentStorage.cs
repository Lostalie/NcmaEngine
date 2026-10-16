using System.Text.Json;

namespace Ncma.Runtime;

// Internal bounded storage slots, not public identities. World validates handles before access.
internal interface IComponentPool
{
    bool Has(int slot);
    IComponentPool Clone();
    void Remove(int slot);
    void SetDecoded(int slot, object value);
    JsonElement Encode(int slot);
}

internal sealed class ComponentPool<T> : IComponentPool where T : struct, IComponent
{
    private readonly int[] _sparse;
    private int[] _slots = [];
    private T[] _values = [];
    private int _count;

    internal ComponentPool() => _sparse = new int[World.MaxObjects];
    private ComponentPool(ComponentPool<T> source)
    {
        _sparse = (int[])source._sparse.Clone();
        _slots = (int[])source._slots.Clone();
        _values = (T[])source._values.Clone();
        _count = source._count;
    }

    public bool Has(int slot) => (uint)slot < (uint)_sparse.Length && _sparse[slot] != 0;
    internal T Get(int slot) => Has(slot) ? _values[_sparse[slot] - 1] :
        throw new ArgumentException("Component is not attached.");

    internal void Set(int slot, T value)
    {
        if ((uint)slot >= (uint)_sparse.Length) throw new ArgumentOutOfRangeException(nameof(slot));
        int encoded = _sparse[slot];
        if (encoded != 0) { _values[encoded - 1] = value; return; }
        if (_count == _values.Length)
        {
            int size = Math.Min(World.MaxObjects, Math.Max(4, _count * 2));
            var slots = new int[size];
            var values = new T[size];
            _slots.CopyTo(slots, 0);
            _values.CopyTo(values, 0);
            _slots = slots;
            _values = values;
        }
        _slots[_count] = slot;
        _values[_count] = value;
        _sparse[slot] = ++_count;
    }

    public void Remove(int slot)
    {
        if (!Has(slot)) return;
        int index = _sparse[slot] - 1;
        int last = --_count;
        if (index != last)
        {
            _slots[index] = _slots[last];
            _values[index] = _values[last];
            _sparse[_slots[index]] = index + 1;
        }
        // Clear references (e.g. strings) and the removed membership, including last-element removal.
        _slots[last] = 0;
        _values[last] = default;
        _sparse[slot] = 0;
    }

    public IComponentPool Clone() => new ComponentPool<T>(this);
    // Boxing is confined to heterogeneous document decoding, never Get<T>/Set<T> storage.
    public void SetDecoded(int slot, object value) => Set(slot, (T)value);
    public JsonElement Encode(int slot) => JsonSerializer.SerializeToElement(Get(slot), SceneJson.Options);
}

internal sealed class ComponentStorage
{
    private readonly Dictionary<Type, IComponentPool> _pools;
    private readonly HashSet<Type> _owned = [];

    internal ComponentStorage() => _pools = [];
    private ComponentStorage(Dictionary<Type, IComponentPool> pools) => _pools = pools;

    // Whole-pool copy on first write is intentional for E1. E3 measures finer-grained candidates.
    internal ComponentStorage Fork()
    {
        var result = new ComponentStorage(new(_pools));
        _owned.Clear();
        return result;
    }

    internal bool Has(int slot, Type type) => _pools.TryGetValue(type, out var pool) && pool.Has(slot);
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
        // Prepare every clone before touching any value; a failed allocation cannot prefix-delete.
        Type[] types = _pools.Where(pair => pair.Value.Has(slot)).Select(pair => pair.Key).ToArray();
        var pools = types.Select(Writable).ToArray();
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
