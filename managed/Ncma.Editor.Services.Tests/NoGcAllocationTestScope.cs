using System.Runtime;
using System.Runtime.CompilerServices;

// Test-only v2 allocation boundary, not an engine setting or a performance benchmark.
// The lease is allocated before entry; neither failed entry nor failed exit can become a skipped test.
internal sealed class NoGcAllocationTestScope : IDisposable
{
    private readonly Action _end;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private bool _active;

    private NoGcAllocationTestScope(Action end) => _end = end;

    internal static NoGcAllocationTestScope Enter() => Enter(
        static () => GC.TryStartNoGCRegion(4 * 1024 * 1024, disallowFullBlockingGC: false),
        static () => GC.EndNoGCRegion());

    // Dependency injection only for deterministic failure-path tests; no production API.
    internal static NoGcAllocationTestScope Enter(Func<bool> begin, Action end)
    {
        ArgumentNullException.ThrowIfNull(begin);
        ArgumentNullException.ThrowIfNull(end);
        var scope = new NoGcAllocationTestScope(end);
        if (!begin()) throw new InvalidOperationException("UTF8 allocation v2: NoGCRegion entry failed.");
        scope._active = true;
        return scope;
    }

    public void Dispose()
    {
        if (!_active) return;
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Allocation test scope must end on its owner thread.");
        _active = false;
        // EndNoGCRegion throws if collection/budget invalidated the interval. Never swallow/retry.
        _end();
    }
}

internal static class NoGcAllocationScopeTests
{
    private static void Check(bool condition)
    {
        if (!condition) throw new Exception("NoGC allocation boundary assertion failed.");
    }

    private static void ThrowsSame(Action action, Exception expected)
    {
        try { action(); }
        catch (Exception actual) when (ReferenceEquals(actual, expected)) { return; }
        throw new Exception("Expected original scope failure.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object AllocateControl() => new byte[53];

    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("E5-D allocation v2 rejects failed or throwing entry", () => {
            int ends = 0;
            bool rejected = false;
            try { using var scope = NoGcAllocationTestScope.Enter(() => false, () => ends++); }
            catch (InvalidOperationException e) { rejected = e.Message.Contains("entry failed", StringComparison.Ordinal); }
            Check(rejected && ends == 0);
            var failure = new InvalidOperationException("entry exception");
            ThrowsSame(() => NoGcAllocationTestScope.Enter(() => throw failure, () => ends++), failure);
            Check(ends == 0);
        });
        yield return ("E5-D allocation v2 disposes on body failure exactly once", () => {
            int ends = 0;
            var failure = new InvalidOperationException("body exception");
            ThrowsSame(() => {
                using var scope = NoGcAllocationTestScope.Enter(() => true, () => ends++);
                throw failure;
            }, failure);
            Check(ends == 1);
            var lease = NoGcAllocationTestScope.Enter(() => true, () => ends++);
            lease.Dispose(); lease.Dispose();
            Check(ends == 2);
        });
        yield return ("E5-D allocation v2 propagates invalidated exit without retry", () => {
            int ends = 0;
            var failure = new InvalidOperationException("exit invalidated");
            var scope = NoGcAllocationTestScope.Enter(() => true, () => { ends++; throw failure; });
            ThrowsSame(scope.Dispose, failure);
            scope.Dispose();
            Check(ends == 1);
        });
        yield return ("E5-D allocation v2 preserves owner-thread cleanup", () => {
            int ends = 0;
            using var scope = NoGcAllocationTestScope.Enter(() => true, () => ends++);
            bool rejected = false;
            var thread = new Thread(() => {
                try { scope.Dispose(); }
                catch (InvalidOperationException) { rejected = true; }
            });
            thread.Start(); Check(thread.Join(5000));
            Check(rejected && ends == 0);
            scope.Dispose(); Check(ends == 1);
        });
        yield return ("E5-D allocation v2 real zero and positive controls", () => {
            GC.KeepAlive(AllocateControl()); // Warm the measured method outside the region.
            _ = GC.GetAllocatedBytesForCurrentThread();
            long empty, positive;
            object allocated;
            using (NoGcAllocationTestScope.Enter()) {
                Check(GCSettings.LatencyMode == GCLatencyMode.NoGCRegion);
                long start = GC.GetAllocatedBytesForCurrentThread();
                empty = GC.GetAllocatedBytesForCurrentThread() - start;
                start = GC.GetAllocatedBytesForCurrentThread();
                allocated = AllocateControl();
                positive = GC.GetAllocatedBytesForCurrentThread() - start;
            }
            GC.KeepAlive(allocated);
            Check(empty == 0 && positive == 80 && GCSettings.LatencyMode != GCLatencyMode.NoGCRegion);
            Console.WriteLine($"Allocation v2 controls: empty={empty}B positive={positive}B; checked exit");
        });
    }
}
