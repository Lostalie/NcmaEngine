using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Ncma.Runtime;
using Ncma.Scene;

// Separate diagnostic cohort. Original E0/E5 timed workloads and thresholds remain unchanged.
internal static class EcsReadDiagnostics
{
    private readonly record struct Marker(int Value) : IComponent;
    private readonly record struct Flag(int Value) : IComponent;
    private readonly record struct Unattached(int Value) : IComponent;
    private static readonly FieldInfo? Cache = typeof(World).GetField("_committedEntityOrder", BindingFlags.Instance | BindingFlags.NonPublic);
    private static Guid Id(int index) => new(index + 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Read lifecycle diagnostic mismatch."); }
    private static RuntimeSceneInstance Fixture(int count)
    {
        var registry = ComponentRegistry.CreateDefault();
        const string schema = """{"type":"object","required":["value"],"properties":{"value":{"type":"integer"}},"additionalProperties":false}""";
        registry.Register<Marker>("e5c.marker", 1, schema, value => value);
        registry.Register<Flag>("e5c.flag", 1, schema, value => value);
        registry.Register<Unattached>("e5c.unattached", 1, schema, value => value);
        var document = new SceneDocument("E5-C read lifecycle", registry);
        document.RestoreSnapshot(new(1, document.Name, Enumerable.Range(0, count).Select(i => {
            var components = new List<ComponentSnapshot> {
                new("ncma.transform", 1, registry.Encode(TransformData.Identity with { Position = new(i, 0, 0) })) };
            if (i % 2 == 0) components.Add(new("e5c.marker", 1, registry.Encode(new Marker(i))));
            if (i % 4 == 0) components.Add(new("e5c.flag", 1, registry.Encode(new Flag(i))));
            return new SceneObjectData(Id(i), "Object " + i, components.ToArray(), []);
        }).ToArray()));
        return document.BakeRuntime(document.Revision).Image.Instantiate(registry);
    }
    private static object Distribution(double[] values)
    {
        var sorted = values.Order().ToArray();
        double P(double p) => sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * p) - 1)];
        return new { p50 = P(.5), p95 = P(.95), p99 = P(.99), max = sorted[^1] };
    }
    internal static void Run(bool measured, Action<string, int, EcsDiagnosticState>? verified = null)
    {
        int warmup = measured ? 12 : 1, samples = measured ? 100 : 2, cases = 0;
        string[] names = ["copy_cold_first", "copy_warm", "copy_value_change", "copy_component_membership",
            "copy_recreate", "copy_restore", "query_value_change", "query_three_warm", "query_none_warm", "query_empty_pool"];
        if (measured) Console.WriteLine(JsonSerializer.Serialize(new {
            kind = "ecs_e6_read_diagnostic_environment", schema = 2, historicalPairedContractCompatible = false, runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription, cpuCount = Environment.ProcessorCount,
            stopwatchFrequency = Stopwatch.Frequency, serverGc = System.Runtime.GCSettings.IsServerGC,
#if DEBUG
            configuration = "Debug",
#else
            configuration = "Release",
#endif
            scope = "Diagnostic only: preparation/invalidation/oracle excluded from API timing; no formal threshold or whole-frame claim."
        }));
        foreach (int count in measured ? new[] { 0, 100, 1000, World.MaxObjects } : new[] { 0, 8 })
        foreach (string name in names)
        {
            RuntimeSceneInstance document = Fixture(count); World world = document.World;
            var snapshot = document.CaptureCheckpoint();
            Entity[] expected = Enumerable.Range(0, count).Select(i => world.FindEntity(Id(i))).ToArray();
            bool copies = name.StartsWith("copy_", StringComparison.Ordinal);
            EntityQuery? query = copies ? null : name switch {
                "query_three_warm" => world.Query<TransformData, Marker, Flag>(),
                "query_none_warm" => world.CreateQuery([typeof(TransformData)], [typeof(Flag)]),
                "query_empty_pool" => world.Query<Unattached>(),
                _ => world.Query<TransformData>()
            };
            if (copies && name != "copy_cold_first") _ = world.GetEntities();
            if (query is not null) _ = query.Count;
            int expectedMatches = 0; double expectedSum = 0;
            for (int i = 0; i < count; i++) {
                bool matches = name switch { "query_three_warm" => i % 4 == 0,
                    "query_none_warm" => i % 4 != 0, "query_empty_pool" => false, _ => true };
                if (matches) { expectedMatches++; expectedSum += i; }
            }
            int sequence = 0; IReadOnlyList<Entity>? result = null;
            void Prepare()
            {
                sequence++;
                if (name == "copy_cold_first") { document = Fixture(count); world = document.World; }
                if (name == "copy_restore") document.RestoreCheckpoint(snapshot, document.Revision);
                if (count != 0) {
                    Entity first = world.FindEntity(Id(0));
                    if (name is "copy_value_change" or "query_value_change")
                        world.Set(first, world.Get<TransformData>(first) with { Position = new(0, sequence, 0) });
                    if (name == "copy_component_membership") {
                        if (world.Has<Flag>(first)) Check(world.Remove<Flag>(first)); else world.Set(first, new Flag(0));
                    }
                    if (name == "copy_recreate") {
                        world.Destroy(world.FindEntity(Id(count - 1)));
                        var fresh = world.CreateEntity("Object " + (count - 1), Id(count - 1));
                        world.Set(fresh, TransformData.Identity with { Position = new(count - 1, 0, 0) });
                        if ((count - 1) % 2 == 0) world.Set(fresh, new Marker(count - 1));
                        if ((count - 1) % 4 == 0) world.Set(fresh, new Flag(count - 1));
                    }
                }
                // Independent UUID oracle, outside timing; never primes GetEntities' private cache.
                if (copies) for (int i = 0; i < count; i++) expected[i] = world.FindEntity(Id(i));
            }
            void Operation()
            {
                if (copies) { result = world.GetEntities(); Check(result.Count == count); return; }
                int matches = 0, last = -1; double sum = 0;
                foreach (var entity in query!) {
                    int index = (int)world.Get<TransformData>(entity).Position.X;
                    Check(index > last && world.GetUuid(entity) == Id(index));
                    last = index; sum += index; matches++;
                }
                Check(matches == expectedMatches && sum == expectedSum);
            }
            void Verify()
            {
                if (copies) {
                    Check(result is not null && result.SequenceEqual(expected));
                    if (count != 0) Check(!ReferenceEquals(result, Cache?.GetValue(world)));
                }
                if (count != 0 && name is "copy_value_change" or "query_value_change")
                    Check(world.Get<TransformData>(world.FindEntity(Id(0))).Position.Y == sequence);
            }
            for (int i = 0; i < warmup; i++) { Prepare(); Operation(); Verify(); }
            var times = new double[samples]; var allocations = new double[samples]; var pauses = new double[samples];
            int[] retained = new int[samples]; int[] gcStart = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
            long managedBefore = GC.GetTotalMemory(false);
            for (int i = 0; i < samples; i++) {
                Prepare();
                long bytes = GC.GetAllocatedBytesForCurrentThread(), pause = GC.GetTotalPauseDuration().Ticks;
                long start = Stopwatch.GetTimestamp(); Operation(); long ticks = Stopwatch.GetTimestamp() - start;
                allocations[i] = GC.GetAllocatedBytesForCurrentThread() - bytes;
                times[i] = ticks * 1000d / Stopwatch.Frequency;
                pauses[i] = (GC.GetTotalPauseDuration().Ticks - pause) / (double)TimeSpan.TicksPerMillisecond;
                Verify();
                retained[i] = (Cache?.GetValue(world) as Entity[])?.Length ?? 0;
                Check(retained[i] <= World.MaxObjects);
            }
            long managedAfter = GC.GetTotalMemory(false);
            int[] gc = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - gcStart[i]).ToArray();
            if (measured) Console.WriteLine(JsonSerializer.Serialize(new {
                kind = "ecs_e6_read_diagnostic_sample", schema = 2, historicalPairedContractCompatible = false, workload = name, objects = count, warmup, samples,
                ms = Distribution(times), allocatedThreadBytes = Distribution(allocations), runtimeGcPauseMs = Distribution(pauses),
                rawMs = times, rawAllocatedThreadBytes = allocations, rawRuntimeGcPauseMs = pauses,
                observedCacheField = Cache is not null, retainedIdentityCounts = retained, entitySizeBytes = Unsafe.SizeOf<Entity>(),
                managedBeforeBytes = managedBefore, managedAfterBytes = managedAfter, gcCollectionsIncludingSetup = gc,
                note = "Retained counts describe one private identity array payload, not heap residency. Output copies are additional. GC/heap include untimed fixture and invalidation; pause attribution can lag."
            }));
            verified?.Invoke(name, count, EcsDiagnosticState.FromRuntime(document)); cases++;
        }
        Console.WriteLine($"E5 read lifecycle diagnostic fixture checks: {cases}/{cases} passed; measured={measured}.");
    }
}
