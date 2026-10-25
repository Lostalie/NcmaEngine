using System.Collections.Concurrent;
using System.Diagnostics;

namespace Ncma.Runtime;

internal sealed class ScheduleWork(ScheduleContext context, Action<ScheduleContext> kernel)
{
    internal readonly ScheduleContext Context = context;
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Done => _completion.Task;
    internal long Elapsed;
    private long _bytes;
    private long? _allocatedBytes;
    internal ScheduleTaskReport SnapshotReport()
    {
        // Done is the acquire boundary for all metrics; unfinished workers are not 0B.
        if (!Done.IsCompleted) return new(Context.TaskId, 0, "unfinished", null, null);
        return new(Context.TaskId, Elapsed, Context.Error is null ? "computed" : "failed", _bytes, _allocatedBytes);
    }
    internal void Run()
    {
        long started = Stopwatch.GetTimestamp();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        try { Context.Invoke(kernel); }
        finally {
            long delta = GC.GetAllocatedBytesForCurrentThread() - allocated;
            _allocatedBytes = delta >= 0 ? delta : null; // Never clamp invalid telemetry to 0B.
            _bytes = Context.ChargedBytes;
            Elapsed = Stopwatch.GetTimestamp() - started;
            _completion.TrySetResult(true);
        }
    }
}

// Fixed bounded workers, no live World input. A hung trusted callback cannot be killed safely:
// Retirement disconnects publication without dropping ownership of unfinished workers/inputs.
internal sealed class ScheduleWorkers
{
    private readonly BlockingCollection<ScheduleWork> _queue = new(ScheduleGraph.MaxTasks);
    private readonly Thread[] _threads;
    private bool _closed;
    internal ScheduleWorkers(int count)
    {
        _threads = Enumerable.Range(0, count).Select(i => new Thread(Loop) { IsBackground = true, Name = "Ncma.Schedule." + i }).ToArray();
    }
    internal void Start() { foreach (var thread in _threads) thread.Start(); }
    private void Loop()
    { foreach (var work in _queue.GetConsumingEnumerable()) work.Run(); }
    internal void Enqueue(ScheduleWork work)
    { if (_closed || !_queue.TryAdd(work)) throw new InvalidOperationException("Bounded worker queue closed or full."); }
    internal int AliveCount { get { int count=0; foreach(var thread in _threads) if(thread.IsAlive) count++; return count; } }
    internal void RequestClose()
    { if (!_closed) { _closed = true; _queue.CompleteAdding(); } }
    internal bool Close(TimeSpan budget)
    {
        RequestClose();
        var clock = Stopwatch.StartNew();
        foreach (var thread in _threads)
        {
            TimeSpan left = budget - clock.Elapsed;
            if (thread.IsAlive && (left <= TimeSpan.Zero || !thread.Join(left))) return false;
        }
        _queue.Dispose(); return true;
    }
}
