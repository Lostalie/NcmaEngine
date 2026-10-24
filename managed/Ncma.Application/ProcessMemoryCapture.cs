using System.ComponentModel;
using System.Diagnostics;

namespace Ncma.Application;

public enum MemoryCheckpoint { BeforeStartup, RuntimeReady, BeforeShutdown, AfterShutdown }

public readonly record struct ProcessMemorySample(MemoryCheckpoint Checkpoint, int ProcessId, int ObserverThreadId,
    long StartedTimestamp, long EndedTimestamp, long ObserverAllocatedBytes,
    long? WorkingSetBytes, long? PrivateBytes, long? PeakWorkingSetBytes, string? ProcessCounterFailure,
    long RawManagedHeapBytes, long LastGcIndex, long LastGcHeapSizeBytes, long LastGcFragmentedBytes,
    long LastGcCommittedBytes, int Gen0Before, int Gen1Before, int Gen2Before,
    int Gen0After, int Gen1After, int Gen2After)
{
    public bool ProcessCountersAvailable => ProcessCounterFailure is null &&
        WorkingSetBytes is >= 0 && PrivateBytes is >= 0 && PeakWorkingSetBytes is >= 0;
    public bool RawManagedHeapValid => RawManagedHeapBytes >= 0;
    public bool LastGcRecorded => LastGcIndex > 0;
    public bool CollectionObservedDuringRead => Gen0Before != Gen0After || Gen1Before != Gen1After || Gen2Before != Gen2After;
}

public sealed record ProcessMemoryReport(int Version, long TimestampFrequency, ProcessMemorySample[] Samples)
{
    public bool MemoryAccepted => false;
    public bool CompleteLifetimeCoverage => false;
    public bool NativeOnlyBytesAvailable => false;
    public bool GpuPhysicalResidencyAvailable => false;
    public string Scope => "opt-in owner-thread lifecycle checkpoints; sequential non-atomic process/GC reads; " +
        "working set/private include runtime and observer, not native-only; OS peak working set covers process lifetime, not this interval; " +
        "raw managed estimate uses GetTotalMemory(false), last-GC fields may be stale; no forced GC or peak/leak acceptance; " +
        "observer wall/allocation exclude construction, report copying/serialization and caller overhead";
}

// Four fixed optional checkpoints. No timer, worker, callbacks, file IO, native plugin or Agent authority.
// A failed/skipped checkpoint is never retried or silently filled in with a later sample.
public sealed class ProcessMemoryCapture
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly int _process = Environment.ProcessId;
    private readonly ProcessMemorySample[] _samples = new ProcessMemorySample[4];
    private int _count;
    private int _last = -1;

    private void VerifyOwner()
    {
        if (Environment.CurrentManagedThreadId != _owner)
            throw new InvalidOperationException("Memory observation requires owner thread.");
    }

    public void Observe(MemoryCheckpoint checkpoint)
    {
        VerifyOwner();
        if (!Enum.IsDefined(checkpoint) || (int)checkpoint <= _last)
            throw new InvalidOperationException("Invalid, repeated or out-of-order memory checkpoint.");
        _last = (int)checkpoint;
        long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        long? workingSet = null, privateBytes = null, peakWorkingSet = null;
        string? processFailure = null;
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            workingSet = process.WorkingSet64;
            privateBytes = process.PrivateMemorySize64;
            peakWorkingSet = process.PeakWorkingSet64;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            workingSet = privateBytes = peakWorkingSet = null;
            processFailure = error.GetType().Name;
        }
        long rawManaged = GC.GetTotalMemory(false);
        var gc = GC.GetGCMemoryInfo();
        int after0 = GC.CollectionCount(0), after1 = GC.CollectionCount(1), after2 = GC.CollectionCount(2);
        long end = Stopwatch.GetTimestamp(), cost = GC.GetAllocatedBytesForCurrentThread() - allocated;
        _samples[_count++] = new(checkpoint, _process, _owner, start, end, cost,
            workingSet, privateBytes, peakWorkingSet, processFailure, rawManaged,
            gc.Index, gc.HeapSizeBytes, gc.FragmentedBytes, gc.TotalCommittedBytes,
            gen0, gen1, gen2, after0, after1, after2);
    }

    public ProcessMemoryReport Capture()
    {
        VerifyOwner();
        return new(1, Stopwatch.Frequency, _samples.AsSpan(0, _count).ToArray());
    }
}
