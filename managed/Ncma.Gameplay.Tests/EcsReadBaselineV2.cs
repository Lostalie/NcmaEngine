// E6 adaptation retaining v2 memory telemetry; historical paired-source parity is not claimed.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Runtime;
using Ncma.Scene;

// E6 diagnostic: query workloads own runtime state; editor workloads use authoring data.
// Frozen E5 workload shapes are retained, not historical paired-source parity or acceptance.
internal static class EcsReadBaselineV2
{
    private readonly record struct Marker(int Value) : IComponent;
    private static Guid Id(int index) => new(index + 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("E5 read fixture mismatch."); }
    private static object Distribution(double[] input)
    {
        var sorted = input.Order().ToArray();
        double P(double p) => sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * p) - 1)];
        return new { p50 = P(.5), p95 = P(.95), p99 = P(.99), max = sorted[^1] };
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static void Run(bool measured, Action<string, int, EcsDiagnosticState> verified)
    {
        if (measured) Console.WriteLine(JsonSerializer.Serialize(new {
            kind = "ecs_e6_environment", schema = 3, historicalPairedContractCompatible = false, utc = DateTimeOffset.UtcNow,
            runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(), cpuCount = Environment.ProcessorCount,
            stopwatchFrequency = Stopwatch.Frequency, serverGc = System.Runtime.GCSettings.IsServerGC,
#if DEBUG
            configuration = "Debug",
#else
            configuration = "Release",
#endif
            assembly = typeof(EcsReadBaselineV2).Assembly.ManifestModule.ModuleVersionId,
            worldAssembly = typeof(World).Assembly.ManifestModule.ModuleVersionId,
            fixture = "Transform all, Marker even; setup excluded; API guards included",
            warning = "GC/memory include untimed invalidation; peak is process lifetime, not case residency"
        }));
        string[] names = ["query_cold", "query_warm", "query_mixed_warm", "query_membership_rebuild",
            "query_restore_rebuild", "editor_page_cold", "editor_page_warm_first", "editor_page_warm_last", "editor_object_warm"];
        foreach (int count in measured ? new[] { 100, 1000, 4096 } : new[] { 8 })
        foreach (string name in names)
        {
            var registry = global::Ncma.Runtime.CompositionRegistry.Build();
            registry.Register<Marker>("e5.marker", 1,
                """{"type":"object","required":["value"],"properties":{"value":{"type":"integer"}},"additionalProperties":false}""", v => v);
            var doc = new SceneDocument("E5 read fixture", registry);
            doc.RestoreSnapshot(new(1, doc.Name, Enumerable.Range(0, count).Select(i => {
                var components = new List<ComponentSnapshot> {
                    new("ncma.transform", 1, registry.Encode(TransformData.Identity with { Position = new(i, 0, 0) })) };
                if (i % 2 == 0) components.Add(new("e5.marker", 1, registry.Encode(new Marker(i))));
                return new SceneObjectData(Id(i), "Object " + i, components.ToArray(), []);
            }).ToArray()));
            byte[] snapshot = doc.CaptureBytes();
            // Explicit cold runtime derivation for query workloads; never an authoring mirror.
            var runtime = doc.BakeRuntime(doc.Revision).Image.Instantiate(registry);
            var world = runtime.World; var checkpoint = runtime.CaptureCheckpoint();
            bool runtimeQuery = name.StartsWith("query_", StringComparison.Ordinal);
            bool mixed = name is "query_mixed_warm" or "query_membership_rebuild";
            EntityQuery query = mixed ? world.Query<TransformData, Marker>() : world.Query<TransformData>();
            _ = query.Count;
            var edit = new EditSession(doc); edit.SetFrozen(true);
            CapabilityRequest request = null!;
            bool markerPresent = true; int setup = 0;
            int offset = name == "editor_page_warm_last" ? Math.Max(0, count - 128) : 0;
            void Prepare()
            {
                ++setup;
                if (name == "query_membership_rebuild")
                {
                    var first = world.FindEntity(Id(0));
                    if (markerPresent) Check(world.Remove<Marker>(first)); else world.Set(first, new Marker(0));
                    markerPresent = !markerPresent;
                }
                if (name == "query_restore_rebuild") runtime.RestoreCheckpoint(checkpoint, runtime.Revision);
                if (name == "editor_page_cold") doc.RenameObject(Id(0), "Cold " + setup);
                if (name.StartsWith("editor_", StringComparison.Ordinal))
                {
                    bool detail = name == "editor_object_warm";
                    request = new(EditSession.ContractVersion, Guid.NewGuid(), edit.SessionId, edit.Revision,
                        detail ? "ncma.scene.object.inspect" : "ncma.scene.inspect", detail ?
                        JsonSerializer.SerializeToElement(new { objectId = Id(count - 1), section = "components", limit = 64 }) :
                        JsonSerializer.SerializeToElement(new { offset, limit = 128 }));
                }
            }
            void Operation()
            {
                if (name.StartsWith("query_", StringComparison.Ordinal))
                {
                    if (name is "query_cold" or "query_restore_rebuild") query = world.Query<TransformData>();
                    int matches = 0, last = -1; double sum = 0;
                    foreach (var entity in query)
                    {
                        int index = (int)world.Get<TransformData>(entity).Position.X;
                        Check(index > last && world.GetUuid(entity) == Id(index));
                        last = index; sum += index; ++matches;
                    }
                    int expected = mixed ? (count + 1) / 2 - (markerPresent ? 0 : 1) : count;
                    Check(matches == expected && sum == (mixed ? (count / 2d) * (count - 2) / 2 : count * (count - 1d) / 2));
                    return;
                }
                var result = edit.Invoke(request);
                Check(result.Status == "ok" && !result.Changed);
                if (name == "editor_object_warm")
                    Check(result.Data.GetProperty("items").GetArrayLength() == 1);
                else
                {
                    var objects = result.Data.GetProperty("scene").GetProperty("objects");
                    Check(result.Data.GetProperty("totalCount").GetInt32() == count &&
                        result.Data.GetProperty("model").GetString() == "ncma.authoring-scene" && result.Data.GetProperty("modelVersion").GetInt32() == 1 &&
                        result.Data.GetProperty("documentSessionId").GetGuid() == doc.SessionId && result.Data.GetProperty("documentRevision").GetUInt64() == doc.Revision &&
                        objects.GetArrayLength() == Math.Min(128, count - offset));
                    int index = offset;
                    foreach (var item in objects.EnumerateArray()) Check(item.GetProperty("id").GetGuid() == Id(index++));
                }
            }
            int warmup = measured ? 12 : 1, samples = measured ? 100 : 2;
            for (int i = 0; i < warmup; i++) { Prepare(); Operation(); }
            var times = new double[samples]; var allocations = new double[samples];
            int[] gcBefore = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
            long managedBefore = GC.GetTotalMemory(false);
            var memoryBefore = EcsMemoryV2.Capture(managedBefore);
            for (int i = 0; i < samples; i++)
            {
                Prepare(); // Invalidations and unique request construction are explicitly outside API timing/allocation.
                long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                Operation();
                times[i] = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
                allocations[i] = GC.GetAllocatedBytesForCurrentThread() - bytes;
            }
            int[] collections = [GC.CollectionCount(0) - gcBefore[0], GC.CollectionCount(1) - gcBefore[1], GC.CollectionCount(2) - gcBefore[2]];
            long managedAfter = GC.GetTotalMemory(false);
            var memoryAfter = EcsMemoryV2.Capture(managedAfter);
            if (measured)
            {
                using var process = Process.GetCurrentProcess(); process.Refresh();
                Console.WriteLine(JsonSerializer.Serialize(new {
                    kind = "ecs_e6_sample", schema = 3, historicalPairedContractCompatible = false, workload = name, objects = count, sceneBytes = runtimeQuery ? checkpoint.CopyBytes().Length : snapshot.Length,
                    stateFormat = runtimeQuery ? RuntimeSceneImage.Format : "ncma.authoring-scene", stateVersion = 1,
                    warmup, samples, operationsPerSample = 1, ms = Distribution(times),
                    allocatedThreadBytes = Distribution(allocations), rawMs = times, rawAllocatedThreadBytes = allocations,
                    gcCollections = collections, managedBeforeBytes = managedBefore, managedAfterBytes = managedAfter,
                    memoryBefore, memoryAfter,
                    processWorkingSetBytes = process.WorkingSet64, processLifetimePeakWorkingSetBytes = process.PeakWorkingSet64,
                    layerMs = new { }, scope = "Read API including result guards; invalidation/request setup excluded; no transport/GPU/frame claim"
                }));
            }
            Check(edit.State.UndoCount == 0 && edit.State.RedoCount == 0);
            if (runtimeQuery) Check(snapshot.SequenceEqual(doc.CaptureBytes()));
            else Check(checkpoint.CopyBytes().SequenceEqual(runtime.CaptureCheckpoint().CopyBytes()));
            verified(name, count, runtimeQuery ? EcsDiagnosticState.FromRuntime(runtime) : EcsDiagnosticState.FromAuthoring(doc));
        }
        Console.WriteLine($"E5 read fixture checks: {(measured ? 27 : 9)} passed; measured={measured}.");
    }
}
