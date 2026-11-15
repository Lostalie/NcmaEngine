using System.Diagnostics;

namespace Ncma.Application;

[Flags]
public enum ProcessSeriesFailure { None = 0, Cpu = 1, Memory = 2, Handles = 4, Threads = 8, Managed = 16, Reader = 32, All = 63 }
public readonly record struct ProcessGcCounts(int Gen0, int Gen1, int Gen2);
public sealed record ProcessSeriesCounters(long? CpuTime100ns, long? WorkingSetBytes, long? PrivateBytes,
    int? Handles, int? Threads, long? ApproximateAllocatedBytes, long? RawManagedHeapBytes,
    long? LastGcIndex, long? LastGcHeapBytes, long? LastGcCommittedBytes,
    ProcessGcCounts? GcBefore, ProcessGcCounts? GcAfter, ProcessSeriesFailure Failures)
{
    public bool CollectionObservedDuringRead => GcBefore is not null && GcAfter is not null && GcBefore != GcAfter;
    internal static ProcessSeriesCounters Unavailable => new(null, null, null, null, null, null, null, null, null, null, null, null, ProcessSeriesFailure.All);
    internal static void Validate(ProcessSeriesCounters value)
    {
        ArgumentNullException.ThrowIfNull(value);
        static void Require(bool valid) { if (!valid) throw new ArgumentException("process_series_counter_shape"); }
        Require((value.Failures & ~ProcessSeriesFailure.All) == 0 && (!value.Failures.HasFlag(ProcessSeriesFailure.Reader) || value.Failures == ProcessSeriesFailure.All));
        bool Failed(ProcessSeriesFailure flag) => (value.Failures & flag) != 0;
        Require(Failed(ProcessSeriesFailure.Cpu) ? value.CpuTime100ns is null : value.CpuTime100ns is >= 0);
        Require(Failed(ProcessSeriesFailure.Memory) ? value.WorkingSetBytes is null && value.PrivateBytes is null : value.WorkingSetBytes is >= 0 && value.PrivateBytes is >= 0);
        Require(Failed(ProcessSeriesFailure.Handles) ? value.Handles is null : value.Handles is >= 0);
        Require(Failed(ProcessSeriesFailure.Threads) ? value.Threads is null : value.Threads is >= 1);
        static bool Counts(ProcessGcCounts? c) => c is { Gen0: >= 0, Gen1: >= 0, Gen2: >= 0 };
        Require(Failed(ProcessSeriesFailure.Managed)
            ? value.ApproximateAllocatedBytes is null && value.RawManagedHeapBytes is null && value.LastGcIndex is null && value.LastGcHeapBytes is null && value.LastGcCommittedBytes is null && value.GcBefore is null && value.GcAfter is null
            : value.ApproximateAllocatedBytes is >= 0 && value.RawManagedHeapBytes is >= 0 && value.LastGcIndex is >= 0 && value.LastGcHeapBytes is >= 0 && value.LastGcCommittedBytes is >= 0 && Counts(value.GcBefore) && Counts(value.GcAfter));
    }
    // Current process ONLY. Sequential, non-atomic reads; no remote PID, forced GC, timers or processes.
    internal static ProcessSeriesCounters ReadCurrent()
    {
        long? cpu = null, working = null, privateBytes = null, allocated = null, managed = null, gcIndex = null, heap = null, committed = null;
        int? handles = null, threads = null; ProcessGcCounts? before = null, after = null; var failures = ProcessSeriesFailure.None;
        try {
            using var process = Process.GetCurrentProcess(); process.Refresh();
            try { cpu = process.TotalProcessorTime.Ticks; } catch (Exception e) when (e is not OutOfMemoryException) { failures |= ProcessSeriesFailure.Cpu; }
            try { working = process.WorkingSet64; privateBytes = process.PrivateMemorySize64; }
            catch (Exception e) when (e is not OutOfMemoryException) { working = privateBytes = null; failures |= ProcessSeriesFailure.Memory; }
            try { handles = process.HandleCount; } catch (Exception e) when (e is not OutOfMemoryException) { failures |= ProcessSeriesFailure.Handles; }
            try {
                var items = process.Threads;
                try { threads = items.Count; } finally { foreach (ProcessThread thread in items) thread.Dispose(); }
            } catch (Exception e) when (e is not OutOfMemoryException) { threads = null; failures |= ProcessSeriesFailure.Threads; }
        } catch (Exception e) when (e is not OutOfMemoryException) {
            cpu = working = privateBytes = null; handles = threads = null;
            failures |= ProcessSeriesFailure.Cpu | ProcessSeriesFailure.Memory | ProcessSeriesFailure.Handles | ProcessSeriesFailure.Threads;
        }
        try {
            before = new(GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
            allocated = GC.GetTotalAllocatedBytes(precise: false); managed = GC.GetTotalMemory(forceFullCollection: false);
            var gc = GC.GetGCMemoryInfo(); gcIndex = gc.Index; heap = gc.HeapSizeBytes; committed = gc.TotalCommittedBytes;
            after = new(GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        } catch (Exception e) when (e is not OutOfMemoryException) {
            allocated = managed = gcIndex = heap = committed = null; before = after = null; failures |= ProcessSeriesFailure.Managed;
        }
        return new(cpu, working, privateBytes, handles, threads, allocated, managed, gcIndex, heap, committed, before, after, failures);
    }
}
