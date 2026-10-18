using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Runtime;

// Public-API-only entry: this same harness can run against the retained E2-B runtime DLL.
internal static class WorldReadMeasurements
{
    internal static void Run()
    {
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "ecs_e3_read_environment", runtime = Environment.Version.ToString(),
            os = Environment.OSVersion.ToString(), cpuCount = Environment.ProcessorCount,
            testAssembly = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(WorldReadMeasurements).Assembly.Location))),
            runtimeAssembly = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(World).Assembly.Location))),
            tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") }));
        foreach (int count in new[] { 100, 1000, 4096 })
        {
            var world = new World();
            for (int i = 0; i < count; ++i) world.Set(world.CreateEntity("Read fixture"), TransformData.Identity with { Position = new(i, 0, 0) });
            var entities = world.GetEntities().ToArray(); var query = world.Query<TransformData>();
            foreach (bool queried in new[] { false, true })
            {
                double Read() {
                    double sum = 0;
                    if (queried) { foreach (var entity in query) sum += world.Get<TransformData>(entity).Position.X; }
                    else foreach (var entity in entities) sum += world.Get<TransformData>(entity).Position.X;
                    return sum;
                }
                double expected = count * (count - 1d) / 2;
                const int repeats = 16, samples = 300;
                for (int i = 0; i < 256; ++i) if (Read() != expected) throw new Exception("Read fixture mismatch.");
                var raw = new double[samples]; var allocations = new long[samples];
                for (int i = 0; i < samples; ++i) {
                    long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp(); double value = 0;
                    for (int j = 0; j < repeats; ++j) value += Read();
                    long end = Stopwatch.GetTimestamp(); allocations[i] = (GC.GetAllocatedBytesForCurrentThread() - bytes) / repeats;
                    if (value != repeats * expected) throw new Exception("Read result mismatch.");
                    raw[i] = (end - start) * 1_000_000d / Stopwatch.Frequency / repeats;
                }
                var times = raw.Order().ToArray();
                Console.WriteLine(JsonSerializer.Serialize(new { kind = "ecs_e3_read", count, queried, samples, repeats,
                    medianUs = times[149], p95Us = times[284], p99Us = times[296], maxAllocatedBytes = allocations.Max(), raw }));
            }
        }
    }
}
