using System.Text.Json;
using Ncma.Application;

internal static class ProcessMemoryTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition) { checks++; if (!condition) throw new Exception("Process memory check " + checks); }
        void Reject(Action action)
        {
            bool rejected = false;
            try { action(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected);
        }
        var capture = new ProcessMemoryCapture();
        Check(capture.Capture().Samples.Length == 0);
        Reject(() => capture.Observe((MemoryCheckpoint)99));
        foreach (MemoryCheckpoint checkpoint in Enum.GetValues<MemoryCheckpoint>())
        {
            capture.Observe(checkpoint);
            Reject(() => capture.Observe(checkpoint));
        }
        var report = capture.Capture();
        Check(report.Version == 1 && report.TimestampFrequency > 0 && report.Samples.Length == 4);
        Check(!report.MemoryAccepted && !report.CompleteLifetimeCoverage && !report.NativeOnlyBytesAvailable && !report.GpuPhysicalResidencyAvailable);
        Check(report.Samples.Select(s => s.Checkpoint).SequenceEqual(Enum.GetValues<MemoryCheckpoint>()));
        if (OperatingSystem.IsWindows()) Check(report.Samples.All(s => s.ProcessCountersAvailable && s.WorkingSetBytes > 0));
        long previous = 0;
        foreach (var sample in report.Samples)
        {
            Check(sample.ProcessId == Environment.ProcessId && sample.ObserverThreadId == Environment.CurrentManagedThreadId);
            Check(sample.StartedTimestamp >= previous && sample.EndedTimestamp >= sample.StartedTimestamp);
            previous = sample.EndedTimestamp;
            Check(sample.ObserverAllocatedBytes >= 0);
            Check(sample.ProcessCounterFailure is null ? sample.ProcessCountersAvailable :
                sample.WorkingSetBytes is null && sample.PrivateBytes is null && sample.PeakWorkingSetBytes is null);
            Check(sample.RawManagedHeapValid == (sample.RawManagedHeapBytes >= 0));
            Check(sample.LastGcRecorded == (sample.LastGcIndex > 0));
            Check(sample.CollectionObservedDuringRead == (sample.Gen0Before != sample.Gen0After || sample.Gen1Before != sample.Gen1After || sample.Gen2Before != sample.Gen2After));
            Check(sample.LastGcHeapSizeBytes >= 0 && sample.LastGcCommittedBytes >= 0 && sample.LastGcFragmentedBytes >= 0);
        }
        // Do not force GC or assert resident memory returns to baseline: runtime caches and GC timing are real costs.
        var first = report.Samples[0];
        report.Samples[0] = default;
        Check(capture.Capture().Samples[0] == first);
        var invalidRaw = first with { RawManagedHeapBytes = -1, LastGcIndex = 0 };
        Check(!invalidRaw.RawManagedHeapValid && !invalidRaw.LastGcRecorded);
        var unavailable = first with { WorkingSetBytes = null, PrivateBytes = null, PeakWorkingSetBytes = null, ProcessCounterFailure = "Win32Exception" };
        Check(!unavailable.ProcessCountersAvailable);
        Check((first with { Gen0After = first.Gen0Before + 1 }).CollectionObservedDuringRead);
        var partial = new ProcessMemoryCapture();
        partial.Observe(MemoryCheckpoint.BeforeStartup); partial.Observe(MemoryCheckpoint.AfterShutdown);
        Reject(() => partial.Observe(MemoryCheckpoint.RuntimeReady));
        Check(partial.Capture().Samples.Length == 2);
        Check(Task.Run(() => { try { capture.Capture(); return false; } catch (InvalidOperationException) { return true; } }).Result);
        Check(Task.Run(() => { try { new ProcessMemoryCapture(); capture.Observe(MemoryCheckpoint.BeforeStartup); return false; } catch (InvalidOperationException) { return true; } }).Result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(capture.Capture()));
        Check(json.RootElement.GetProperty("Samples").GetArrayLength() == 4 && !json.RootElement.GetProperty("MemoryAccepted").GetBoolean());
        Console.WriteLine("process_memory_test_observations=" + JsonSerializer.Serialize(capture.Capture()));
        Console.WriteLine($"PASS E5.D process memory {checks} checks");
    }
}
