// Measurement-only telemetry. A signed runtime counter is retained even when invalid;
// last-completed-GC information is never substituted for current heap/live residency.
internal static class EcsMemoryV2
{
    internal const string Scope = "last-completed-gc-not-current-heap";
    internal static string Status(long raw) => raw < 0 ? "invalid-runtime-reading" : "valid-estimate";
    internal readonly record struct LastCollection(long index, int generation, long heapBytes,
        long fragmentedBytes, long totalCommittedBytes, bool compacted, bool concurrent, string scope);
    internal readonly record struct Snapshot(string status, LastCollection lastCollection);

    internal static Snapshot Capture(long raw)
    {
        var info = GC.GetGCMemoryInfo();
        return new(Status(raw), new(info.Index, info.Generation, info.HeapSizeBytes,
            info.FragmentedBytes, info.TotalCommittedBytes, info.Compacted, info.Concurrent, Scope));
    }
}
