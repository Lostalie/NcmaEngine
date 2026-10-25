using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Ncma.Runtime;

// Kernels are trusted application code, not an OS sandbox. Contexts expose copied values only.
public sealed record ScheduleKernel(string TaskId, Action<ScheduleContext> Execute);
public enum ScheduleStructureKind { Create, Destroy, Rename, Add, Remove }
public sealed record ScheduleStructure(ScheduleStructureKind Kind, Guid ObjectId, string? Name = null, ComponentSnapshot? Component = null, string? TypeId = null);

public abstract class ScheduleComponent
{
    private protected ScheduleComponent() { }
    internal abstract Type Type { get; }
    internal abstract IScheduleCapture Bind(World world, ScheduleBufferBudget budget);
    public static ScheduleComponent Of<T>() where T : unmanaged, IComponent => new Channel<T>();
    private sealed class Channel<T> : ScheduleComponent where T : unmanaged, IComponent
    {
        internal override Type Type => typeof(T);
        internal override IScheduleCapture Bind(World world, ScheduleBufferBudget budget) => new ScheduleCapture<T>(world, budget);
    }
}

internal static class ScheduleCallbackScope
{
    [ThreadStatic] internal static ScheduleContext? Current;
    internal static void RejectWorldAccess()
    { if (Current is { } context) throw context.Reject("Scheduled callbacks cannot access a live World; use declared copied inputs."); }
}

internal sealed class ScheduleBudget(long reserved)
{
    private long _bytes = reserved >= 0 && reserved <= ScheduleExecutor.MaxFrameBytes
        ? reserved : throw new ArgumentOutOfRangeException(nameof(reserved));
    internal bool Charge(int bytes)
    {
        if (bytes < 0) return false; // A charge cannot refund another task's reservation.
        while (true) {
            long before = Interlocked.Read(ref _bytes);
            if (bytes > ScheduleExecutor.MaxFrameBytes - before) return false;
            // Publish only accepted reservations: no transient overcharge or failed debt.
            if (Interlocked.CompareExchange(ref _bytes, before + bytes, before) == before) return true;
        }
    }
}

public sealed class ScheduleContext
{
    private readonly ScheduleTask _task;
    // Expand once per context; the row-access path must not scan declarations or create delegates.
    // Include Kind in the key so a resource ID cannot grant access to a component with that ID.
    private readonly Dictionary<(ScheduleAccessKind Kind, string Id), ScheduleAccessMode> _permissions;
    private readonly Dictionary<Type, IScheduleColumn> _columns;
    private readonly Dictionary<string, byte[]> _resources;
    private readonly ScheduleCompletionBuffer.ReadView _prior;
    private readonly int _priorSequence;
    private readonly HashSet<string> _predecessors;
    private readonly CancellationToken _cancellation;
    private readonly ScheduleBudget _budget;
    private volatile bool _active;
    private volatile bool _completed;
    private int _thread;
    private Exception? _error;
    private int _bytes, _reads, _structureBytes = 2;
    internal readonly Dictionary<Type, IScheduleWrites> Writes;
    internal readonly Dictionary<string, byte[]> ResourceWrites = new(StringComparer.Ordinal);
    internal readonly List<ScheduleStructure> Structures = [];
    internal Exception? Error => Volatile.Read(ref _error);
    internal string TaskId => _task.Id;
    internal bool CompletedSuccessfully => _completed && Error is null;
    // Read only after ScheduleWork.Done publication, never while the callback is running.
    // Accepted logical output reservations + copied resource reads; not memory bandwidth.
    internal long ChargedBytes => (long)_bytes + _reads;
    internal ScheduleCompletionBuffer.ReadView CompletionView => _prior;
    public double FixedDeltaSeconds { get; }
    public ulong Tick { get; }
    internal ScheduleContext(ScheduleTask task, Dictionary<Type, IScheduleColumn> columns, Dictionary<string, byte[]> resources,
        ScheduleCompletionBuffer.ReadView prior, int priorSequence, HashSet<string> predecessors, double delta, ulong tick, CancellationToken cancellation, ScheduleBudget budget,
        Dictionary<Type, IScheduleWrites> writes)
    {
        if (priorSequence < 0 || priorSequence > prior.Sequence) throw new ArgumentException("Invalid completed-output visibility cutoff.");
        _task = task; _columns = columns; _resources = resources; _prior = prior; _priorSequence = priorSequence; _predecessors = predecessors;
        FixedDeltaSeconds = delta; Tick = tick; _cancellation = cancellation; _budget = budget;
        Writes = writes;
        _permissions = new(task.Access.Length);
        foreach (var access in task.Access) _permissions.Add((access.Kind, access.Id), access.Mode);
    }
    internal void Invoke(Action<ScheduleContext> kernel)
    {
        if (_completed || _active) throw Reject("Schedule context is single-use.");
        if (ScheduleCallbackScope.Current is not null) throw Reject("Nested scheduled callback.");
        _thread = Environment.CurrentManagedThreadId; _active = true; ScheduleCallbackScope.Current = this;
        try { Check(); kernel(this); Check(); }
        catch (Exception error) { Interlocked.CompareExchange(ref _error, error, null); }
        finally { _active = false; ScheduleCallbackScope.Current = null; _completed = true; }
    }
    internal void Invalidate() => _active = false;
    internal InvalidOperationException Reject(string reason)
    { var error = new InvalidOperationException(reason); Interlocked.CompareExchange(ref _error, error, null); return error; }
    private void Check()
    {
        if (!_active || _thread != Environment.CurrentManagedThreadId || ScheduleCallbackScope.Current != this)
            throw Reject("Expired, foreign-thread or escaped schedule context.");
        if (_cancellation.IsCancellationRequested) throw Reject("Scheduled execution cancelled.");
        if (_error is not null) throw new InvalidOperationException("Scheduled callback has a rejected access.", _error);
    }
    public void CheckCancellation() => Check();
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Allowed(ScheduleAccessKind kind, string id, bool write) =>
        _permissions.TryGetValue((kind, id), out var mode) && (!write || mode == ScheduleAccessMode.Write);
    private void Require(ScheduleAccessKind kind, string id, bool write)
    { Check(); if (!Allowed(kind, id, write)) throw Reject("Undeclared scheduled " + (write ? "write: " : "read: ") + id); }
    private ScheduleColumn<T> Column<T>() where T : unmanaged, IComponent
    {
        Check();
        if (!_columns.TryGetValue(typeof(T), out var column) || column is not ScheduleColumn<T> typed) throw Reject("Component input was not declared/captured.");
        Require(ScheduleAccessKind.Component, typed.Descriptor.TypeId, false); return typed;
    }
    private void Index(int index, int length) { if ((uint)index >= length) throw Reject("Scheduled row index is outside its captured membership."); }
    public int Count<T>() where T : unmanaged, IComponent => Column<T>().Count;
    public Guid ObjectAt<T>(int index) where T : unmanaged, IComponent { var c = Column<T>(); Index(index, c.Count); return c.Ids[index]; }
    public T Read<T>(int index) where T : unmanaged, IComponent { var c = Column<T>(); Index(index, c.Count); return c.Values[index]; }
    // Average O(1) lookup in this column's captured membership, not a live World lookup.
    public int Find<T>(Guid objectId) where T : unmanaged, IComponent => Column<T>().Find(objectId);
    public void Write<T>(int index, T value) where T : unmanaged, IComponent
    {
        var c = Column<T>(); Index(index, c.Count); Require(ScheduleAccessKind.Component, c.Descriptor.TypeId, true);
        var output = (ScheduleColumn<T>.Writes)Writes[typeof(T)];
        if (!output.Contains(index)) Charge(Unsafe.SizeOf<T>() + sizeof(int));
        try { output.Set(index, value); } catch (InvalidOperationException error) { throw Reject(error.Message); }
    }
    public byte[] ReadResource(string id)
    {
        var value = Resource(id);
        ReadBudget(value.Length); return (byte[])value.Clone();
    }
    // Caller-owned destination avoids managed allocation without exposing backing arrays/leases.
    public int ReadResource(string id, Span<byte> destination) => CopyRead(Resource(id), destination);
    private byte[] Resource(string id)
    {
        Require(ScheduleAccessKind.Resource, id, false);
        if (!_resources.TryGetValue(id, out var value)) throw Reject("Resource input was not bound.");
        return value;
    }
    public byte[] ReadOutput(string producer, string resource)
    {
        var value = Output(producer, resource);
        ReadBudget(value.Length); return (byte[])value.Clone();
    }
    public int ReadOutput(string producer, string resource, Span<byte> destination) => CopyRead(Output(producer, resource), destination);
    private byte[] Output(string producer, string resource)
    {
        Require(ScheduleAccessKind.Resource, resource, false);
        if (!_predecessors.Contains(producer) || !_prior.TryReadOutput(producer, resource, _priorSequence, out var value))
            throw Reject("Output requires a declared predecessor completed before this context's visibility cutoff.");
        return value;
    }
    private int CopyRead(byte[] value, Span<byte> destination)
    {
        if (destination.Length < value.Length) throw Reject("Resource destination is too small; no partial copy.");
        ReadBudget(value.Length); value.AsSpan().CopyTo(destination); return value.Length;
    }
    private void ReadBudget(int bytes)
    { if (bytes > ScheduleExecutor.MaxFrameBytes - _reads || !_budget.Charge(bytes)) throw Reject("Task copied-read budget exceeded."); _reads += bytes; }
    public void WriteResource(string id, ReadOnlySpan<byte> value)
    {
        Require(ScheduleAccessKind.Resource, id, true);
        if (value.Length > ScheduleExecutor.MaxResourceBytes) throw Reject("Resource output budget exceeded.");
        Charge(value.Length); ResourceWrites[id] = value.ToArray();
    }
    private void Charge(int bytes)
    { if (bytes > ScheduleExecutor.MaxTaskOutputBytes - _bytes || !_budget.Charge(bytes)) throw Reject("Task output byte budget exceeded."); _bytes += bytes; }
    private void Structure(ScheduleStructure command)
    {
        Require(ScheduleAccessKind.Structure, "*", true);
        if (_task.Affinity != ScheduleAffinity.OwnerThread || command.ObjectId == Guid.Empty || Structures.Count >= ScheduleExecutor.MaxStructuralCommands)
            throw Reject("Invalid structural target, affinity or operation budget.");
        if (command.Name is { } name) { try { World.ValidateName(name); } catch (ArgumentException e) { throw Reject(e.Message); } }
        int bytes;
        try { bytes = SceneJson.EncodeBounded(command,64*1024-_structureBytes).Length + (Structures.Count == 0 ? 0 : 1); }
        catch (ArgumentException) { throw Reject("Structural output byte budget exceeded."); }
        if (bytes > 64*1024-_structureBytes) throw Reject("Structural output byte budget exceeded.");
        Charge(bytes); _structureBytes += bytes; Structures.Add(command);
    }
    public void Create(Guid id, string name) => Structure(new(ScheduleStructureKind.Create, id, name));
    public void Destroy(Guid id) => Structure(new(ScheduleStructureKind.Destroy, id));
    public void Rename(Guid id, string name) => Structure(new(ScheduleStructureKind.Rename, id, name));
    public void Add<T>(Guid id, T value) where T : unmanaged, IComponent
    {
        var c = Column<T>(); Require(ScheduleAccessKind.Component, c.Descriptor.TypeId, true);
        try {
            using var data = JsonDocument.Parse(SceneJson.EncodeBounded(value, ComponentRegistry.MaxPayloadBytes));
            Structure(new(ScheduleStructureKind.Add, id, Component: new(c.Descriptor.TypeId, c.Descriptor.Version, data.RootElement.Clone())));
        } catch (ArgumentException) { throw Reject("Structural component encoding budget exceeded."); }
    }
    public void Remove<T>(Guid id) where T : unmanaged, IComponent
    { var c = Column<T>(); Require(ScheduleAccessKind.Component, c.Descriptor.TypeId, true); Structure(new(ScheduleStructureKind.Remove, id, TypeId: c.Descriptor.TypeId)); }
}
