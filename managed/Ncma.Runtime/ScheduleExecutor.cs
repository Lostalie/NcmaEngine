using System.Diagnostics;
using System.Collections.Frozen;

namespace Ncma.Runtime;

// Bytes: accepted per-task budget charges (outputs + copied reads), not heap usage.
// AllocatedBytes: same-thread managed allocation delta around synchronous Context.Invoke,
// including callback guards/errors; excludes capture/context creation/merge/report/native
// memory and allocations on child threads. Null means unavailable, never a measured zero.
public sealed record ScheduleTaskReport(string TaskId, long ElapsedTimestampTicks, string State,
    long? Bytes, long? AllocatedBytes);
public sealed record ScheduleReport(string GraphHash, Guid World, ulong AttemptTick, string State,
    bool ParallelEnabled, string? Failure, ScheduleTaskReport[] Tasks);

// Borrowed by one runner. Fixed-step resources are copied data channels, not arbitrary external
// objects/solver handles. OS side effects of trusted callbacks cannot be sandboxed or rolled back.
public sealed class ScheduleExecutor : IDisposable
{
    public const int MaxFrameBytes = 16 * 1024 * 1024, MaxTaskOutputBytes = 1024 * 1024,
        MaxResourceBytes = 64 * 1024, MaxStructuralCommands = 128, MaxWorkers = 4;
    private readonly World _world;
    private readonly Guid _identity;
    private readonly ScheduleGraph _graph;
    private readonly ScheduleTask[] _ordered;
    private readonly Dictionary<string, Action<ScheduleContext>> _kernels;
    private readonly Dictionary<Type, IScheduleCapture> _captures = [];
    private readonly Dictionary<Type, IScheduleColumn>[] _columnBanks = [new(), new()];
    private readonly Dictionary<string, Dictionary<Type, IScheduleWrites>>[] _writeBanks = [new(StringComparer.Ordinal), new(StringComparer.Ordinal)];
    private readonly ScheduleBufferBudget _retainedBuffers = new();
    private int _nextBufferBank, _bufferBankInUse = -1;
    private readonly Dictionary<string, HashSet<string>> _predecessors;
    private readonly Dictionary<string, string[]> _dependencies;
    private readonly string[] _anchors;
    private readonly FrozenDictionary<string, int> _completionSlots;
    private Dictionary<string, byte[]> _resources;
    private Dictionary<string, byte[]>? _pendingResources;
    private readonly int _workerCount;
    private readonly TimeSpan _timeout;
    private ScheduleWorkers? _workers;
    private CancellationTokenSource? _cancellation;
    private readonly List<ScheduleWork> _jobs = [];
    private readonly List<ScheduleContext> _contexts = [];
    private ScheduleReport _report;
    private bool _running, _disposed, _faulted, _attached, _retired;
    private ulong _attemptTick;
    private Guid _attempt;
    public ScheduleGraph Graph => _graph;
    public ScheduleReport Inspect() { Verify(); return _report with { Tasks = (ScheduleTaskReport[])_report.Tasks.Clone() }; }

    public ScheduleExecutor(World world, ScheduleGraph graph, IEnumerable<ScheduleKernel> kernels,
        IEnumerable<ScheduleComponent> components, IReadOnlyDictionary<string, byte[]>? resources = null,
        int workerCount = 0, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(world); ArgumentNullException.ThrowIfNull(graph);
        world.VerifyAccess(); _world = world; _identity = world.Identity;
        _timeout = timeout ?? TimeSpan.FromSeconds(2);
        if (workerCount is < 0 or > MaxWorkers || _timeout < TimeSpan.FromMilliseconds(1) || _timeout > TimeSpan.FromSeconds(5)) throw new ArgumentException("Invalid worker/time budget.");
        _workerCount = workerCount;
        // Revalidate existing graph against this exact runtime registry, not another type map.
        _graph = ScheduleGraph.Compile(world.Components, graph.CopyTasks(), graph.CopyResources());
        if (_graph.ContentHash != graph.ContentHash) throw new ArgumentException("Schedule registry contract changed.");
        var tasks = _graph.CopyTasks().ToDictionary(t => t.Id, StringComparer.Ordinal);
        _ordered = _graph.CopySerialOrder().Select(id => tasks[id]).ToArray();
        _completionSlots = _ordered.Select((task, index) => (task.Id, index)).ToFrozenDictionary(x => x.Id, x => x.index, StringComparer.Ordinal);
        ArgumentNullException.ThrowIfNull(kernels); ArgumentNullException.ThrowIfNull(components);
        var callbacks = kernels.Take(ScheduleGraph.MaxTasks + 1).ToArray();
        if (callbacks.Length != tasks.Count || callbacks.Any(k => k is null || k.Execute is null || !tasks.ContainsKey(k.TaskId))) throw new ArgumentException("Exact bounded kernel bindings required.");
        _kernels = callbacks.ToDictionary(k => k.TaskId, k => k.Execute, StringComparer.Ordinal);
        var channels = components.Take(ScheduleGraph.MaxTasks * ScheduleGraph.MaxAccessPerTask + 1).ToArray();
        if (channels.Length > ScheduleGraph.MaxTasks * ScheduleGraph.MaxAccessPerTask || channels.Any(c => c is null)) throw new ArgumentException("Component channel budget.");
        var boundChannels = channels.ToDictionary(c => c.Type);
        var declared = tasks.Values.SelectMany(t => t.Access).Where(a => a.Kind == ScheduleAccessKind.Component).Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var bound = channels.Select(c => world.Components.Describe(c.Type).TypeId).ToHashSet(StringComparer.Ordinal);
        if (!bound.SetEquals(declared)) throw new ArgumentException("Exactly the declared unmanaged component channels must be bound.");
        var resourceDeclarations = graph.CopyResources();
        if ((resources?.Count ?? 0) != resourceDeclarations.Length) throw new ArgumentException("Exact resource inputs required.");
        _resources = new(StringComparer.Ordinal);
        foreach (var r in resourceDeclarations)
        {
            if (resources is null || !resources.TryGetValue(r.Id, out var data) || data is null || data.Length > MaxResourceBytes) throw new ArgumentException("Invalid resource input.");
            _resources.Add(r.Id, (byte[])data.Clone());
        }
        var edges = _graph.CopyEdges();
        var executionOrder = _graph.CopyExecutionOrder();
        _anchors = _graph.CopyPhaseAnchors().Select(a => a.Id).ToArray();
        _dependencies = executionOrder.ToDictionary(id => id, id => edges.Where(e => e.To == id).Select(e => e.From).ToArray(), StringComparer.Ordinal);
        // Cold ancestry cache is for ReadOutput authorization only, never execution edges.
        // Readiness uses compact direct edges, including synthetic completion anchors.
        var ancestors = executionOrder.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var id in executionOrder)
            foreach (var from in _dependencies[id]) { if(tasks.ContainsKey(from)) ancestors[id].Add(from); ancestors[id].UnionWith(ancestors[from]); }
        _predecessors = tasks.Keys.ToDictionary(id => id, id => ancestors[id], StringComparer.Ordinal);
        foreach (var channel in boundChannels) {
            var capture = channel.Value.Bind(world, _retainedBuffers); _captures.Add(channel.Key, capture);
            for (int bank = 0; bank < 2; ++bank) _columnBanks[bank].Add(channel.Key, capture.Buffer(bank));
        }
        for (int bank = 0; bank < 2; ++bank)
            foreach (var task in _ordered) {
                var writers = new Dictionary<Type, IScheduleWrites>();
                foreach (var column in _columnBanks[bank])
                    if (task.Access.Any(a => a.Kind == ScheduleAccessKind.Component && a.Id == column.Value.Descriptor.TypeId && a.Mode == ScheduleAccessMode.Write))
                        writers.Add(column.Key, column.Value.CreateWrites());
                _writeBanks[bank].Add(task.Id, writers);
            }
        _report = new(graph.ContentHash, _identity, world.Tick, "idle", workerCount > 0, null, []);
    }
    private void Verify()
    { _world.VerifyAccess(); ObjectDisposedException.ThrowIf(_disposed, this); if (_world.Identity != _identity) throw new InvalidOperationException("Schedule belongs to a replaced World."); }
    internal void Attach(World world)
    { Verify(); if (!ReferenceEquals(world, _world) || _attached || _running || _retired || _world.IsUpdating) throw new InvalidOperationException("Schedule already attached, retired or foreign."); _attached = true; }
    // Permanent publication revocation, not a promise that trusted callbacks have exited.
    internal void Retire()
    {
        _world.VerifyAccess(); ObjectDisposedException.ThrowIf(_disposed,this);
        if(_running || _world.IsUpdating)throw new InvalidOperationException("Retire only at an idle boundary.");
        _retired=true; _attached=false; _faulted=true; _pendingResources=null; _attempt=Guid.Empty;
        _cancellation?.Cancel(); foreach(var context in _contexts)context.Invalidate();
        _workers?.RequestClose(); _report=_report with {State="retired"};
    }
    internal int PendingWorkerCount { get { _world.VerifyAccess(); return _workers?.AliveCount??0; } }
    internal bool TryReclaim(TimeSpan budget)
    {
        _world.VerifyAccess(); if(_disposed)return true;
        if(!_retired || _attached || _running || _world.IsUpdating)throw new InvalidOperationException("Only retired idle schedules can be reclaimed.");
        if(_workers is not null && !_workers.Close(budget))return false;
        _workers=null; ReleaseCompleted(); ReleaseTypedBuffers(); _report=_report with {State="reclaimed"}; return true;
    }
    public byte[] ReadResource(string id) { Verify(); if (_running) throw new InvalidOperationException("Resource read during scheduling."); return (byte[])_resources[id].Clone(); }
    public void UpdateResource(string id, ReadOnlySpan<byte> value)
    { Verify(); if (_running || _retired || _faulted || _jobs.Count != 0 || _world.IsUpdating || _pendingResources is not null || !_resources.ContainsKey(id) || value.Length > MaxResourceBytes) throw new InvalidOperationException("Resource update requires an idle declared boundary."); _resources[id] = value.ToArray(); }

    internal void Run(double delta, Action<IReadOnlyList<ScheduleStructure>>? submitStructure)
    {
        Verify();
        if (!_attached || _retired || _running || _faulted || !_world.IsUpdating || _pendingResources is not null || _jobs.Count != 0 || _bufferBankInUse >= 0) throw new InvalidOperationException("Schedule is not ready for its fixed step.");
        _attemptTick = checked(_world.Tick + 1); _attempt = _world.ScheduleAttempt;
        _cancellation = new(); _cancellation.CancelAfter(_timeout); _running = true;
        var deadline = Stopwatch.StartNew();
        var reports = new List<ScheduleTaskReport>(); var completed = new ScheduleCompletionBuffer(_completionSlots);
        try
        {
            _bufferBankInUse = _nextBufferBank; _nextBufferBank ^= 1;
            var columns = _columnBanks[_bufferBankInUse]; long budget = 0;
            foreach (var capture in _captures.Values) capture.Capture(_bufferBankInUse);
            foreach (var column in columns.Values) budget += column.Bytes;
            foreach (var taskWrites in _writeBanks[_bufferBankInUse].Values) foreach (var writer in taskWrites.Values) writer.Reset();
            budget += _resources.Values.Sum(v => (long)v.Length);
            if (budget > MaxFrameBytes) throw new InvalidOperationException("Schedule input/output buffer budget.");
            var allocationBudget = new ScheduleBudget(budget);
            var pending = new HashSet<string>(_ordered.Select(t => t.Id), StringComparer.Ordinal);
            var completedNodes = new HashSet<string>(StringComparer.Ordinal);
            while (pending.Count != 0)
            {
                // Anchors have no kernel/context/output and cannot complete until ALL incoming
                // tasks and the preceding anchor complete successfully. No worker slot needed.
                foreach (var anchor in _anchors)
                    if (!completedNodes.Contains(anchor) && _dependencies[anchor].All(completedNodes.Contains)) completedNodes.Add(anchor);
                var ready = _ordered.Where(t => pending.Contains(t.Id) && _dependencies[t.Id].All(completedNodes.Contains)).ToArray();
                if (ready.Length == 0) throw new InvalidOperationException("Schedule made no progress.");
                if (_workerCount == 0) ready = [ready[0]];
                var wave = new List<(ScheduleTask Task, ScheduleWork Work)>();
                int visibleThrough = completed.View.Sequence;
                // Owner tasks run on the owner even when independent worker tasks exist. None
                // of these callbacks sees staged writes of another task through its input.
                foreach (var task in ready)
                {
                    _cancellation.Token.ThrowIfCancellationRequested();
                    var context = new ScheduleContext(task, columns, _resources, completed.View, visibleThrough, _predecessors[task.Id], delta, _attemptTick, _cancellation.Token, allocationBudget, _writeBanks[_bufferBankInUse][task.Id]);
                    _contexts.Add(context); var job = new ScheduleWork(context, _kernels[task.Id]);
                    if (task.Affinity == ScheduleAffinity.Worker && _workerCount > 0) {
                        if (_workers is null) { _workers = new(_workerCount); _workers.Start(); }
                        _workers.Enqueue(job);
                    } else job.Run();
                    _jobs.Add(job); wave.Add((task, job));
                }
                foreach (var item in wave)
                {
                    var left = _timeout - deadline.Elapsed;
                    if (!item.Work.Done.IsCompleted && (left <= TimeSpan.Zero || !item.Work.Done.Wait(left))) throw new TimeoutException("Scheduled worker deadline exceeded: " + item.Task.Id);
                    if (item.Work.Context.Error is { } error) throw new InvalidOperationException("Scheduled task failed: " + item.Task.Id, error);
                    reports.Add(item.Work.SnapshotReport()); completed.Publish(item.Task.Id, item.Work.Context); completedNodes.Add(item.Task.Id); pending.Remove(item.Task.Id);
                }
            }
            // Collect and validate conflicts BEFORE staging any task output; duplicate writes
            // never become completion-order-dependent 'last writer wins'.
            // Gather the entire attempt's Create set first, so a writer ordered before its
            // creator receives the same diagnostic. Keep the original merged command bound.
            var created = new HashSet<Guid>(); int structuralCount = 0;
            foreach (var task in _ordered)
                foreach (var command in completed.GetCompleted(task.Id).Structures) {
                    if (++structuralCount > MaxStructuralCommands) throw new InvalidOperationException("Merged structural operation budget.");
                    if (command.Kind == ScheduleStructureKind.Create) created.Add(command.ObjectId);
                }
            var writes = new HashSet<(Guid, string)>(); var objects = new HashSet<Guid>(); var destroyed = new HashSet<Guid>();
            var structures = new List<ScheduleStructure>(); var nextResources = new Dictionary<string, byte[]>(_resources, StringComparer.Ordinal);
            var resourceWrites = new HashSet<string>(StringComparer.Ordinal); long outputBytes = 0;
            foreach (var task in _ordered)
            {
                var context = completed.GetCompleted(task.Id);
                foreach (var output in context.Writes.Values) {
                    output.Prepare();
                    for (int i = 0; i < output.Count; ++i) {
                        var target = output.TargetAt(i);
                        if (created.Contains(target.Object)) throw new InvalidOperationException(
                            $"scheduled_write_to_created_object: use Add<T> structural operation for an object created in this attempt. Task={task.Id}; Object={target.Object:D}; Component={target.Type}.");
                        if (!writes.Add(target) || destroyed.Contains(target.Object)) throw new InvalidOperationException("Conflicting scheduled component output.");
                        objects.Add(target.Object);
                    }
                }
                foreach (var command in context.Structures)
                {
                    if (command.Kind == ScheduleStructureKind.Destroy) { if (objects.Contains(command.ObjectId) || !destroyed.Add(command.ObjectId)) throw new InvalidOperationException("Exclusive scheduled destroy conflict."); }
                    else if (destroyed.Contains(command.ObjectId)) throw new InvalidOperationException("Scheduled target already destroyed.");
                    string field = command.Kind is ScheduleStructureKind.Add ? command.Component!.TypeId : command.Kind is ScheduleStructureKind.Remove ? command.TypeId! : "$" + command.Kind;
                    if (!writes.Add((command.ObjectId, field))) throw new InvalidOperationException("Conflicting scheduled structure output.");
                    if (structures.Count >= MaxStructuralCommands) throw new InvalidOperationException("Merged structural operation budget.");
                    objects.Add(command.ObjectId); structures.Add(command);
                }
                foreach (var resource in context.ResourceWrites)
                {
                    if (!resourceWrites.Add(resource.Key)) throw new InvalidOperationException("Multiple task producers for one resource output; use separate intermediate channels.");
                    outputBytes += resource.Value.Length; nextResources[resource.Key] = resource.Value;
                }
            }
            if (structures.Count > MaxStructuralCommands || outputBytes + budget > MaxFrameBytes) throw new InvalidOperationException("Schedule merged output budget.");
            _ = SceneJson.EncodeBounded(structures,64*1024);
            _cancellation.Token.ThrowIfCancellationRequested();
            foreach (var command in structures) _world.VerifyScheduledStructure(command.ObjectId);
            foreach (var task in _ordered) foreach (var output in completed.GetCompleted(task.Id).Writes.Values) output.Stage(_world);
            if (structures.Count != 0) { if (submitStructure is null) _world.StageScheduledStructure(structures); else submitStructure(structures.AsReadOnly()); }
            _pendingResources = nextResources;
            _report = new(_graph.ContentHash, _identity, _attemptTick, "staged", _workerCount > 0, null, reports.ToArray());
        }
        catch (Exception error)
        {
            _faulted = true; _cancellation.Cancel(); _pendingResources = null;
            // Include every dispatched job, even if waiting failed before its report row.
            // Each row is a frozen snapshot; late worker exit never rewrites old reports.
            reports.Clear(); foreach (var job in _jobs) reports.Add(job.SnapshotReport());
            string failure = error.GetType().Name + ": " + error.Message + " | " + error.GetBaseException().Message;
            _report = new(_graph.ContentHash, _identity, _attemptTick, "failed", _workerCount > 0, failure[..Math.Min(failure.Length, 512)], reports.ToArray());
            _world.RejectStep(error); throw;
        }
        finally { foreach (var context in _contexts) context.Invalidate(); _running = false; ReleaseCompleted(); }
    }
    internal void FinishAttempt()
    {
        Verify(); if (_pendingResources is null) return;
        if (!_world.WasSchedulePublished(_attempt, _attemptTick)) { Aborted(); return; }
        _resources = _pendingResources; _pendingResources = null; _report = _report with { State = "committed" };
    }
    internal void Aborted()
    { Verify(); _pendingResources = null; if (_report.State == "staged") _report = _report with { State = "aborted" }; }
    private void ReleaseCompleted()
    {
        if (_jobs.Any(j => !j.Done.IsCompleted)) return;
        _jobs.Clear(); _contexts.Clear(); _cancellation?.Dispose(); _cancellation = null; _bufferBankInUse = -1;
    }
    private void ReleaseTypedBuffers()
    {
        if (_jobs.Count != 0 || _bufferBankInUse >= 0) throw new InvalidOperationException("Unfinished workers still own typed buffers.");
        _captures.Clear();
        for (int bank = 0; bank < 2; ++bank) { _columnBanks[bank].Clear(); _writeBanks[bank].Clear(); }
    }
    public void Quiesce()
    {
        _world.VerifyAccess(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (_running || _world.IsUpdating) throw new InvalidOperationException("Cannot close a running schedule.");
        _cancellation?.Cancel(); foreach (var context in _contexts) context.Invalidate();
        if (_workers is not null && !_workers.Close(_timeout)) throw new ScheduleCleanupPendingException("Schedule workers still owned; execution recovery is separate from resource cleanup.");
        _workers = null; ReleaseCompleted(); _pendingResources = null; _faulted = _retired;
    }
    public void Dispose()
    { if (_disposed) return; _world.VerifyAccess(); if (_attached) throw new InvalidOperationException("Detach schedule from its runner before disposal."); Quiesce(); ReleaseTypedBuffers(); _disposed = true; }
}
