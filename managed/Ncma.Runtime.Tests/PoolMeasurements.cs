using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Runtime;

// Local algorithm comparison, NOT E5 full-step/whole-engine acceptance.
internal static class PoolMeasurements
{
    private sealed record Sample(double Microseconds, long AllocatedBytes);
    internal static void Run()
    {
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "ecs_e3_pool_environment", runtime = Environment.Version.ToString(),
            os = Environment.OSVersion.ToString(), cpuCount = Environment.ProcessorCount, timerFrequency = Stopwatch.Frequency,
            testAssembly = Hash(typeof(PoolMeasurements).Assembly.Location), runtimeAssembly = Hash(typeof(World).Assembly.Location),
            tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            referenceCommit = "cb50b4f25e9acfaa85ddf9568717ec30e1e254ea", defaultPageSize = PagedValues<int>.DefaultPageSize }));
        foreach (int count in new[] { 100, 1000, 4096 })
        foreach (int pageSize in new[] { 0, 16, 32, 64, 128, 256 })
        {
            var flat = new WholePoolBaseline<TransformData>(); var paged = new ComponentPool<TransformData>(pageSize == 0 ? 64 : pageSize);
            for (int i = 0; i < count; ++i) { var value = TransformData.Identity with { Position = new(i, 0, 0) }; flat.Set(i, value); paged.Set(i, value); }
            foreach (string workload in new[] { "read", "fork-empty", "fork-sparse", "fork-dense", "fork-churn" })
            {
                int writes = workload == "fork-dense" ? count : Math.Max(1, count / 100);
                double expected = workload == "read" ? count * (count - 1d) / 2 : workload == "fork-empty" ? count :
                    Enumerable.Range(0, writes).Sum(i => (double)(i * count / writes + 1));
                Func<double> run = pageSize == 0 ? () => Flat(flat, count, writes, workload) : () => Paged(paged, count, writes, workload);
                const int repeats = 16, samples = 100;
                for (int i = 0; i < 24 * repeats; ++i) if (run() != expected) throw new Exception("Invalid pool measurement fixture.");
                var measured = new Sample[samples];
                for (int i = 0; i < samples; ++i)
                {
                    double sum = 0; long bytes = GC.GetAllocatedBytesForCurrentThread(); long begin = Stopwatch.GetTimestamp();
                    for (int j = 0; j < repeats; ++j) sum += run();
                    long end = Stopwatch.GetTimestamp(); bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
                    if (sum != expected * repeats || flat.Count != count || paged.Count != count || flat.Get(0).Position.X != 0 || paged.Get(0).Position.X != 0)
                        throw new Exception("Pool measurement changed its committed source.");
                    measured[i] = new((end - begin) * 1_000_000d / Stopwatch.Frequency / repeats, bytes / repeats);
                }
                var times = measured.Select(s => s.Microseconds).Order().ToArray(); var allocations = measured.Select(s => s.AllocatedBytes).Order().ToArray();
                Console.WriteLine(JsonSerializer.Serialize(new { kind = "ecs_e3_pool", count, pageSize, workload, repeats, samples,
                    medianUs = times[49], p95Us = times[94], p99Us = times[98], medianAllocatedBytes = allocations[49], raw = measured }));
            }
        }
    }
    private static double Flat(WholePoolBaseline<TransformData> source, int count, int writes, string workload)
    {
        if (workload == "read") { double read = 0; for (int i = 0; i < count; ++i) read += source.Get(i).Position.X; return read; }
        var candidate = (WholePoolBaseline<TransformData>)source.Clone();
        if (workload == "fork-empty") return candidate.Count;
        double sum = 0;
        for (int i = 0; i < writes; ++i) { int slot = i * count / writes; if (workload == "fork-churn") candidate.Remove(slot);
            candidate.Set(slot, TransformData.Identity with { Position = new Vector3(slot + 1, 0, 0) }); sum += candidate.Get(slot).Position.X; }
        return sum;
    }
    private static double Paged(ComponentPool<TransformData> source, int count, int writes, string workload)
    {
        if (workload == "read") { double read = 0; for (int i = 0; i < count; ++i) read += source.Get(i).Position.X; return read; }
        var candidate = (ComponentPool<TransformData>)source.Clone();
        if (workload == "fork-empty") return candidate.Count;
        double sum = 0;
        for (int i = 0; i < writes; ++i) { int slot = i * count / writes; if (workload == "fork-churn") candidate.Remove(slot);
            candidate.Set(slot, TransformData.Identity with { Position = new Vector3(slot + 1, 0, 0) }); sum += candidate.Get(slot).Position.X; }
        return sum;
    }
}
