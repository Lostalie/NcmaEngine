using System.Runtime;
using System.Runtime.CompilerServices;

// User-approved Find allocation test v2 only. Not an engine GC setting or latency benchmark.
// Entry/exit are outside the byte counter, but remain inside the original callback timeout.
internal sealed class ScheduleFindAllocationScope : IDisposable
{
    private static int _regionClaim;
    private readonly Action _end;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private bool _active;
    private ScheduleFindAllocationScope(Action end) => _end = end;

    internal static ScheduleFindAllocationScope Enter() => Enter(
        static () => GC.TryStartNoGCRegion(4 * 1024 * 1024, disallowFullBlockingGC: false),
        static () => GC.EndNoGCRegion());

    // Test-only injection makes refused/throwing entry and invalidated exit deterministic.
    internal static ScheduleFindAllocationScope Enter(Func<bool> begin, Action end)
    {
        ArgumentNullException.ThrowIfNull(begin);
        ArgumentNullException.ThrowIfNull(end);
        var scope = new ScheduleFindAllocationScope(end);
        // NoGCRegion is process-wide. Reject nesting/concurrent entry BEFORE touching CLR state.
        // A rejected contender must not release the current owner's claim.
        if (Interlocked.CompareExchange(ref _regionClaim, 1, 0) != 0)
            throw new InvalidOperationException("Find allocation v2: an allocation scope is already active.");
        try {
            if (GCSettings.LatencyMode == GCLatencyMode.NoGCRegion)
                throw new InvalidOperationException("Find allocation v2: an external NoGCRegion is already active.");
            if (!begin()) throw new InvalidOperationException("Find allocation v2: NoGCRegion entry failed.");
            scope._active = true;
            return scope;
        } catch { Volatile.Write(ref _regionClaim, 0); throw; }
    }

    public void Dispose()
    {
        if (!_active) return;
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Find allocation scope must end on its owner thread.");
        _active = false;
        // Collection/budget invalidation makes EndNoGCRegion throw. Never suppress or retry it.
        try { _end(); }
        finally { Volatile.Write(ref _regionClaim, 0); }
    }
}

internal static class ScheduleFindAllocationScopeTests
{
    private static void Check(bool value)
    { if (!value) throw new Exception("Find allocation v2 boundary assertion failed."); }
    private static void ThrowsSame(Action action, Exception expected)
    {
        try { action(); }
        catch (Exception actual) when (ReferenceEquals(actual, expected)) { return; }
        throw new Exception("Expected original Find allocation boundary exception.");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object AllocateControl() => new byte[53];

    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("Find allocation v2 rejects refused and throwing entry without cleanup", () => {
            int ends = 0; bool rejected = false;
            try { using var scope = ScheduleFindAllocationScope.Enter(() => false, () => ends++); }
            catch (InvalidOperationException e) { rejected = e.Message.Contains("entry failed", StringComparison.Ordinal); }
            Check(rejected && ends == 0);
            var failure = new InvalidOperationException("entry exception");
            ThrowsSame(() => ScheduleFindAllocationScope.Enter(() => throw failure, () => ends++), failure);
            Check(ends == 0);
        });
        yield return ("Find allocation v2 body failure cleans up exactly once including real region", () => {
            int ends = 0; var failure = new InvalidOperationException("body exception");
            ThrowsSame(() => {
                using var scope = ScheduleFindAllocationScope.Enter(() => true, () => ends++);
                throw failure;
            }, failure);
            Check(ends == 1);
            var lease = ScheduleFindAllocationScope.Enter(() => true, () => ends++);
            lease.Dispose(); lease.Dispose(); Check(ends == 2);
            Check(GCSettings.LatencyMode != GCLatencyMode.NoGCRegion);
            ThrowsSame(() => { using var scope = ScheduleFindAllocationScope.Enter(); throw failure; }, failure);
            Check(GCSettings.LatencyMode != GCLatencyMode.NoGCRegion);
        });
        yield return ("Find allocation v2 propagates invalidated exit without retry", () => {
            int ends = 0; var failure = new InvalidOperationException("exit invalidated");
            var scope = ScheduleFindAllocationScope.Enter(() => true, () => { ends++; throw failure; });
            ThrowsSame(scope.Dispose, failure);
            scope.Dispose(); Check(ends == 1);
            using (ScheduleFindAllocationScope.Enter(() => true, () => ends++)) { }
            Check(ends == 2);
        });
        yield return ("Find allocation v2 foreign-thread disposal leaves owner cleanup intact", () => {
            int ends = 0; bool rejected = false;
            using var scope = ScheduleFindAllocationScope.Enter(() => true, () => ends++);
            var thread = new Thread(() => {
                try { scope.Dispose(); } catch (InvalidOperationException) { rejected = true; }
            });
            thread.Start(); Check(thread.Join(5000)); Check(rejected && ends == 0);
            scope.Dispose(); Check(ends == 1);
        });
        yield return ("Find allocation v2 real zero and positive controls on owner and worker", () => {
            void Measure()
            {
                Check(GCSettings.LatencyMode != GCLatencyMode.NoGCRegion);
                GC.KeepAlive(AllocateControl());
                _ = GC.GetAllocatedBytesForCurrentThread();
                long empty, positive; object allocated;
                using (ScheduleFindAllocationScope.Enter()) {
                    Check(GCSettings.LatencyMode == GCLatencyMode.NoGCRegion);
                    long start = GC.GetAllocatedBytesForCurrentThread();
                    empty = GC.GetAllocatedBytesForCurrentThread() - start;
                    start = GC.GetAllocatedBytesForCurrentThread();
                    allocated = AllocateControl();
                    positive = GC.GetAllocatedBytesForCurrentThread() - start;
                }
                GC.KeepAlive(allocated);
                Check(empty == 0 && positive == 80 && GCSettings.LatencyMode != GCLatencyMode.NoGCRegion);
                Console.WriteLine($"Find v2 controls: thread={Environment.CurrentManagedThreadId}, empty={empty}B, positive={positive}B; checked exit");
            }
            Measure();
            Exception? failure = null;
            var worker = new Thread(() => { try { Measure(); } catch (Exception e) { failure = e; } });
            worker.Start(); Check(worker.Join(5000));
            if (failure is not null) throw new InvalidOperationException("Worker allocation control failed.", failure);
            Check(GCSettings.LatencyMode != GCLatencyMode.NoGCRegion);
        });
        yield return ("Find allocation v2 nested entry rejects without ending existing region", () => {
            Check(GCSettings.LatencyMode != GCLatencyMode.NoGCRegion);
            bool rejected = false;
            using (ScheduleFindAllocationScope.Enter()) {
                try { using var nested = ScheduleFindAllocationScope.Enter(); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected && GCSettings.LatencyMode == GCLatencyMode.NoGCRegion);
                bool foreignRejected = false;
                var contender = new Thread(() => {
                    try { using var nested = ScheduleFindAllocationScope.Enter(); }
                    catch (InvalidOperationException) { foreignRejected = true; }
                });
                contender.Start(); Check(contender.Join(5000));
                Check(foreignRejected && GCSettings.LatencyMode == GCLatencyMode.NoGCRegion);
            }
            Check(GCSettings.LatencyMode != GCLatencyMode.NoGCRegion);
            // A region owned outside this helper must not be adopted or ended by a failed entry.
            if (!GC.TryStartNoGCRegion(4 * 1024 * 1024, disallowFullBlockingGC: false))
                throw new InvalidOperationException("External-region control entry failed.");
            try {
                rejected = false;
                try { using var nested = ScheduleFindAllocationScope.Enter(); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected && GCSettings.LatencyMode == GCLatencyMode.NoGCRegion);
            } finally { GC.EndNoGCRegion(); }
            using (ScheduleFindAllocationScope.Enter()) { }
            Check(GCSettings.LatencyMode != GCLatencyMode.NoGCRegion);
        });
    }
}
