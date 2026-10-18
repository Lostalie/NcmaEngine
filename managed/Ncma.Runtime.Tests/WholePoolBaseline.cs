using System.Text.Json;
using Ncma.Runtime;

// Test-only frozen E2-B pool algorithm (cb50b4f), not a runtime fallback or compatibility API.
// Only the type name and the no-allocation PrepareRemove interface member differ.
internal sealed class WholePoolBaseline<T> : IComponentPool where T : struct, IComponent
{
    private readonly int[] _sparse;
    private int[] _slots = [];
    private T[] _values = [];
    private int _count;

    internal WholePoolBaseline() => _sparse = new int[World.MaxObjects];
    private WholePoolBaseline(WholePoolBaseline<T> source)
    {
        _sparse = (int[])source._sparse.Clone();
        _slots = (int[])source._slots.Clone();
        _values = (T[])source._values.Clone();
        _count = source._count;
    }

    public bool Has(int slot) => (uint)slot < (uint)_sparse.Length && _sparse[slot] != 0;
    public int Count => _count;
    public int SlotAt(int index) => (uint)index < (uint)_count ? _slots[index] : throw new ArgumentOutOfRangeException(nameof(index));
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

    public void PrepareRemove(int slot) { }
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

    public IComponentPool Clone() => new WholePoolBaseline<T>(this);
    // Boxing is confined to heterogeneous document decoding, never Get<T>/Set<T> storage.
    public void SetDecoded(int slot, object value) => Set(slot, (T)value);
    public JsonElement Encode(int slot) => JsonSerializer.SerializeToElement(Get(slot), SceneJson.Options);
}
