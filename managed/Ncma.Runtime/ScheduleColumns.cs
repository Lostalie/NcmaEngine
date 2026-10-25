using System.Runtime.CompilerServices;

namespace Ncma.Runtime;

// Account retained capacities of BOTH input banks and all sparse writer buffers. This is
// conservative payload/index accounting, not a hard cap on CLR/object headers or transient GC.
internal sealed class ScheduleBufferBudget
{
    private long _bytes;
    internal long Bytes => Interlocked.Read(ref _bytes);
    internal void Reserve(long bytes)
    {
        if (bytes < 0) throw new InvalidOperationException("Invalid retained buffer reservation.");
        while (true) {
            long before = Interlocked.Read(ref _bytes);
            if (bytes > ScheduleExecutor.MaxFrameBytes - before) throw new InvalidOperationException("Retained schedule buffer budget exceeded.");
            if (Interlocked.CompareExchange(ref _bytes, before + bytes, before) == before) return;
        }
    }
}
internal interface IScheduleCapture
{
    IScheduleColumn Buffer(int bank);
    void Capture(int bank);
}
internal interface IScheduleColumn
{
    ComponentDescriptor Descriptor { get; }
    int Count { get; }
    long Bytes { get; }
    IScheduleWrites CreateWrites();
}
internal interface IScheduleWrites
{
    int Count { get; }
    void Reset();
    void Prepare();
    (Guid Object, string Type) TargetAt(int index);
    void Stage(World world);
}
internal sealed class ScheduleCapture<T> : IScheduleCapture where T : unmanaged, IComponent
{
    private readonly World _world;
    private readonly EntityQuery _query;
    private readonly ScheduleBufferBudget _budget;
    private readonly ScheduleColumn<T>[] _buffers;
    private int _queryCapacity;
    internal ScheduleCapture(World world, ScheduleBufferBudget budget)
    {
        _world = world; _budget = budget;
        _queryCapacity = world.Count; budget.Reserve((long)_queryCapacity * Unsafe.SizeOf<Entity>());
        _query = world.Query<T>(); // One persistent plan per bound column, never per frame.
        var descriptor = world.Components.Describe(typeof(T));
        _buffers = [new(descriptor, budget), new(descriptor, budget)];
    }
    public IScheduleColumn Buffer(int bank) => _buffers[bank];
    public void Capture(int bank)
    {
        // EntityQuery grows its own member cache to world.Count on membership changes.
        int required = _world.Count;
        if (required > _queryCapacity) { _budget.Reserve((long)(required - _queryCapacity) * Unsafe.SizeOf<Entity>()); _queryCapacity = required; }
        _buffers[bank].Capture(_world, _query);
    }
}
internal sealed class ScheduleColumn<T>(ComponentDescriptor descriptor, ScheduleBufferBudget budget) : IScheduleColumn where T : unmanaged, IComponent
{
    public ComponentDescriptor Descriptor => descriptor;
    public int Count { get; private set; }
    // Includes a conservative allowance for the UUID dictionary entries/buckets as well
    // as Entity/UUID arrays. Same original budget, not an unaccounted per-bank index.
    private static int RowBytes => Unsafe.SizeOf<T>() + 128;
    public long Bytes => (long)Count * RowBytes;
    private readonly Dictionary<Guid, int> _rowsById = [];
    internal Entity[] Entities = [];
    internal Guid[] Ids = [];
    internal T[] Values = [];
    internal void Capture(World world, EntityQuery query)
    {
        int count = query.Count;
        if (count > Values.Length) {
            int capacity = Math.Min(World.MaxObjects, Math.Max(count, Math.Max(4, Values.Length * 2)));
            budget.Reserve((long)(capacity - Values.Length) * RowBytes);
            Array.Resize(ref Entities, capacity); Array.Resize(ref Ids, capacity); Array.Resize(ref Values, capacity);
            _rowsById.EnsureCapacity(capacity);
        }
        query.CopyTo(Entities.AsSpan(0, count));
        // Owner rebuilds only before callbacks start. Each bank retains its own dictionary
        // until all borrowers exit; Clear removes deleted/shifted rows without shrinking.
        _rowsById.Clear();
        for (int i = 0; i < count; ++i) {
            Ids[i] = world.GetUuid(Entities[i]); Values[i] = world.Get<T>(Entities[i]);
            _rowsById.Add(Ids[i], i);
        }
        Count = count; // Capacity tails remain private and are never added to the index.
    }
    internal int Find(Guid objectId) => _rowsById.TryGetValue(objectId, out int row) ? row : -1;
    public IScheduleWrites CreateWrites() => new Writes(this, budget);
    internal sealed class Writes(ScheduleColumn<T> source, ScheduleBufferBudget retained) : IScheduleWrites
    {
        private readonly Dictionary<int, int> _positions = [];
        private int[] _rows = [];
        private T[] _values = [];
        private bool _prepared;
        public int Count { get; private set; }
        internal int Capacity => _rows.Length;
        internal bool Contains(int row) => _positions.ContainsKey(row);
        public void Reset() { _positions.Clear(); Count = 0; _prepared = false; }
        internal void Set(int row, T value)
        {
            if (_prepared || (uint)row >= (uint)source.Count) throw new InvalidOperationException("Sparse writer is closed or row is stale.");
            if (_positions.TryGetValue(row, out int slot)) { _values[slot] = value; return; }
            if (Count == _rows.Length) {
                int capacity = Math.Min(World.MaxObjects, Math.Max(4, _rows.Length * 2));
                retained.Reserve((long)(capacity - _rows.Length) * (Unsafe.SizeOf<T>() + 64));
                Array.Resize(ref _rows, capacity); Array.Resize(ref _values, capacity); _positions.EnsureCapacity(capacity);
            }
            _rows[Count] = row; _values[Count] = value; _positions.Add(row, Count); ++Count;
        }
        // Preserve original ascending row publication order, regardless of callback write order.
        public void Prepare() { if (_prepared) return; Array.Sort(_rows, _values, 0, Count); _prepared = true; }
        public (Guid Object, string Type) TargetAt(int index) => (source.Ids[_rows[index]], source.Descriptor.TypeId);
        public void Stage(World world)
        {
            if (!_prepared) throw new InvalidOperationException("Prepare sparse writes before staging.");
            for (int i = 0; i < Count; ++i) { var entity = source.Entities[_rows[i]]; world.VerifyScheduledWrite(entity, typeof(T)); world.Set(entity, _values[i]); }
        }
    }
}
