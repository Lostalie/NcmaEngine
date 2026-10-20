// v2 measurement-only fork. Keep timed operations identical to v1; source parity is tested.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Ncma.Gameplay;
using Ncma.Runtime;
using Ncma.Scene;

// E0 measures the shipping APIs, not a second ECS or a proposed storage prototype.
// Keep workload semantics fixed when adapting calls to E1/E2 APIs.
internal static class EcsBaselineV2
{
    // Test-process-only event namespace; workload/timing boundaries remain identical to E0/E3.
    internal static bool E5Events { get; set; }
    private readonly record struct Marker(int Value) : IComponent;
    private sealed class System(Action<World> update) : IWorldSystem
    {
        public void FixedUpdate(World world, double dt) => update(world);
    }
    private sealed class MovingBehaviour : Ncma.Behaviour
    {
        protected override void OnFixedUpdate(double dt)
        {
            var value = GameObject.LocalTransform;
            value.Position.X += 1;
            GameObject.LocalTransform = value;
        }
    }
    private static Guid Id(int index) => new(index + 1, 0, 0, new byte[8]);
    private static void Require(bool value)
    {
        if (!value) throw new InvalidOperationException("E0 fixture result mismatch.");
    }
    internal static SceneDocument Fixture(int count)
    {
        var registry = ComponentRegistry.CreateDefault();
        registry.Register<Marker>("e0.marker", 1,
            """{"type":"object","additionalProperties":false,"required":["value"],"properties":{"value":{"type":"integer"}}}""", v => v);
        var document = new SceneDocument("E0 deterministic fixture", registry);
        for (int i = 0; i < count; i++)
        {
            global::Ncma.Runtime.Entity item = document.World.CreateEntity("Object " + i, Id(i));
            document.World.Set(item, TransformData.Identity with { Position = new(i, 0, 0) });
            if (i % 2 == 0) document.World.Set(item, new Marker(i));
        }
        return document;
    }
    private static object Distribution(double[] values)
    {
        var sorted = values.Order().ToArray();
        double Percentile(double p) => sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * p) - 1)];
        return new { p50 = Percentile(.50), p95 = Percentile(.95), p99 = Percentile(.99), max = sorted[^1] };
    }
    private static void Measure(string name, int count, int sceneBytes, bool measured, int operations,
        Action operation, Action verify, Func<long[]>? layers = null)
    {
        int warmup = measured ? 12 : 1, samples = measured ? 100 : 2;
        for (int i = 0; i < warmup; i++) for (int j = 0; j < operations; j++) operation();
        var elapsed = new double[samples]; var allocations = new double[samples];
        var layerSamples = new List<long[]>();
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        long managedBefore = GC.GetTotalMemory(false);
        var memoryBefore = EcsMemoryV2.Capture(managedBefore);
        for (int i = 0; i < samples; i++)
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            for (int j = 0; j < operations; j++) operation();
            long ticks = Stopwatch.GetTimestamp() - start;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
            elapsed[i] = ticks * 1000d / Stopwatch.Frequency / operations;
            allocations[i] = allocated / (double)operations;
            if (layers is not null) layerSamples.Add(layers());
        }
        int[] collections = [GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2];
        long managedAfter = GC.GetTotalMemory(false);
        var memoryAfter = EcsMemoryV2.Capture(managedAfter);
        verify(); // No output, sorting, capture or validation inside the timed loop except API result guards.
        if (!measured) return;
        using var process = Process.GetCurrentProcess(); process.Refresh();
        string[] names = ["setup", "callbacks", "input", "worldSignals", "metadata", "instances", "receipts", "renderPrepare", "install"];
        var layerMs = new Dictionary<string, object>();
        for (int i = 0; layers is not null && i < names.Length; i++)
            layerMs.Add(names[i], Distribution(layerSamples.Select(s => s[i] * 1000d / Stopwatch.Frequency).ToArray()));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            kind = E5Events ? "ecs_e5_sample" : "ecs_e0_baseline", schema = 2, workload = name, objects = count, sceneBytes,
            warmup, samples, operationsPerSample = operations, ms = Distribution(elapsed),
            allocatedThreadBytes = Distribution(allocations), gcCollections = collections,
            managedBeforeBytes = managedBefore, managedAfterBytes = managedAfter,
            memoryBefore, memoryAfter,
            processWorkingSetBytes = process.WorkingSet64, processLifetimePeakWorkingSetBytes = process.PeakWorkingSet64,
            layerMs, rawMs = elapsed, rawAllocatedThreadBytes = allocations,
            scope = "CPU shipping APIs; no GPU, physics, inference or whole-frame claim"
        }));
    }
    internal static void Run(bool measured, Action<string, int, SceneDocument>? verified = null)
    {
        if (measured)
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                kind = E5Events ? "ecs_e5_environment" : "ecs_e0_environment", schema = 2, utc = DateTimeOffset.UtcNow,
                runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(), cpuCount = Environment.ProcessorCount,
                stopwatchFrequency = Stopwatch.Frequency, serverGc = global::System.Runtime.GCSettings.IsServerGC,
#if DEBUG
                configuration = "Debug",
#else
                configuration = "Release",
#endif
                assembly = typeof(EcsBaselineV2).Assembly.ManifestModule.ModuleVersionId,
                worldAssembly = typeof(World).Assembly.ManifestModule.ModuleVersionId,
                fixture = "all Transform; even indices Marker; deterministic UUID/index; 1/60 fixed step",
                warning = "Nearest-rank percentiles; process GC/memory include harness; peak is cumulative, not per-case"
            }));
        int cases = 0;
        foreach (int count in measured ? new[] { 100, 1000, 4096 } : new[] { 8 })
        {
            foreach (bool joint in new[] { false, true })
            {
                var doc = Fixture(count); double sum = 0; int matches = 0;
                Measure(joint ? "read_transform_and_marker" : "read_transform", count, doc.CaptureBytes().Length, measured, 16,
                    () =>
                    {
                        sum = 0; matches = 0;
                        foreach (global::Ncma.Runtime.Entity item in doc.World.GetEntities())
                            if (!joint || doc.World.Has<Marker>(item)) { sum += doc.World.Get<TransformData>(item).Position.X; matches++; }
                    },
                    () => Require(matches == (joint ? (count + 1) / 2 : count) &&
                        sum == Enumerable.Range(0, count).Where(i => !joint || i % 2 == 0).Sum(i => (double)i)));
                verified?.Invoke(joint ? "read_transform_and_marker" : "read_transform", count, doc); cases++;
            }
            foreach (int stride in new[] { 0, 100, 1 })
            {
                var doc = Fixture(count); int steps = 0;
                var runner = new WorldRunner(doc.World);
                runner.AddSystem(new System(w => Move(w, stride)));
                Measure("world_step_stride_" + stride, count, doc.CaptureBytes().Length, measured, 1,
                    () => { Require(runner.Advance(1d / 60) == 1); steps++; },
                    () => VerifyMoves(doc.World, count, stride, steps));
                verified?.Invoke("world_step_stride_" + stride, count, doc); cases++;
            }
            foreach (int stride in new[] { 0, 100, 1, -1 })
            {
                var doc = Fixture(count); int steps = 0;
                using var play = new PlaySession(doc, FrameTimePolicy.Strict) { MeasureSteps = measured };
                bool markerPresent = true;
                var marker = new ComponentSnapshot("e0.marker", 1, doc.World.Components.Encode(new Marker(0)));
                play.AddSystem(new System(w =>
                {
                    if (stride != -1) { Move(w, stride); return; }
                    // Bounded 1% membership churn through the real command buffer, not direct World mutation.
                    for (int i = 0; i < count; i += 100)
                    {
                        if (markerPresent) play.Commands.RemoveComponent(Id(i), "e0.marker");
                        else play.Commands.AddComponent(Id(i), marker);
                    }
                    markerPresent = !markerPresent;
                }));
                play.Start(_ => throw new InvalidOperationException("Fixture has no Behaviour bindings."));
                Measure("play_step_stride_" + stride, count, doc.CaptureBytes().Length, measured, 1,
                    () => { var result = play.AdvanceFrame(1d / 60); Require(result.State == PlayState.Running && result.StepsExecuted == 1); steps++; },
                    () =>
                    {
                        VerifyMoves(doc.World, count, Math.Max(0, stride), steps);
                        Require(play.Tick == (ulong)steps && play.RenderView.Objects.Length == count);
                        if (stride == -1) for (int i = 0; i < count; i += 100) Require(doc.World.Has<Marker>(doc.World.FindEntity(Id(i))) == markerPresent);
                    },
                    () =>
                    {
                        var p = play.LastStepProfile;
                        return [p.Setup, p.Callbacks, p.InputPrepare, p.WorldSignalsPrepare, p.MetadataPrepare,
                            p.InstancesPrepare, p.ReceiptsPrepare, p.RenderPrepare, p.Install];
                    });
                verified?.Invoke("play_step_stride_" + stride, count, doc); cases++;
            }
            {
                var doc = Fixture(count); int steps = 0;
                var snapshot = doc.CaptureSnapshot();
                doc.RestoreSnapshot(snapshot with
                {
                    Objects = snapshot.Objects.Select((o, i) => o with
                    {
                        Behaviours = [new BehaviourBindingData(Id(count + i), "E0.Moving", true, [])]
                    }).ToArray()
                });
                using var play = new PlaySession(doc, FrameTimePolicy.Strict) { MeasureSteps = measured };
                play.Start(_ => new MovingBehaviour());
                Measure("play_behaviour_full_write", count, doc.CaptureBytes().Length, measured, 1,
                    () => { var result = play.AdvanceFrame(1d / 60); Require(result.State == PlayState.Running && result.StepsExecuted == 1); steps++; },
                    () => { VerifyMoves(doc.World, count, 1, steps); Require(play.BehaviourCount == count); },
                    () => Layers(play));
                verified?.Invoke("play_behaviour_full_write", count, doc); cases++;
            }
            {
                var doc = Fixture(count); int steps = 0, nextId = count;
                int[] targets = Enumerable.Range(0, count).Where(i => i % 100 == 0).ToArray();
                Guid[] live = targets.Select(Id).ToArray();
                var transforms = targets.Select(i => new ComponentSnapshot("ncma.transform", 1,
                    doc.World.Components.Encode(TransformData.Identity with { Position = new(i, 0, 0) }))).ToArray();
                using var play = new PlaySession(doc, FrameTimePolicy.Strict) { MeasureSteps = measured };
                play.AddSystem(new System(_ =>
                {
                    for (int i = 0; i < targets.Length; i++)
                    {
                        play.Commands.Destroy(live[i]);
                        live[i] = Id(nextId++);
                        var pending = play.Commands.SpawnEmpty("Object " + targets[i], live[i]);
                        play.Commands.AddComponent(pending.ObjectId, transforms[i]);
                        if (targets[i] % 2 == 0) play.Commands.AddComponent(pending.ObjectId,
                            new ComponentSnapshot("e0.marker", 1, doc.World.Components.Encode(new Marker(targets[i]))));
                    }
                }));
                play.Start(_ => throw new InvalidOperationException("Fixture has no Behaviour bindings."));
                Measure("play_replace_one_percent", count, doc.CaptureBytes().Length, measured, 1,
                    () => { var result = play.AdvanceFrame(1d / 60); Require(result.State == PlayState.Running && result.StepsExecuted == 1); steps++; },
                    () =>
                    {
                        Require(doc.World.Count == count && play.Tick == (ulong)steps && play.RenderView.Objects.Length == count);
                        for (int i = 0; i < targets.Length; i++)
                            Require(doc.World.Get<TransformData>(doc.World.FindEntity(live[i])).Position.X == targets[i]);
                        Require(doc.World.GetEntities().Sum(o => (double)doc.World.Get<TransformData>(o).Position.X) == count * (count - 1d) / 2);
                    }, () => Layers(play));
                verified?.Invoke("play_replace_one_percent", count, doc); cases++;
            }
        }
        Console.WriteLine($"E0 fixture checks: {cases}/{cases} passed; measured={measured}.");
    }
    private static long[] Layers(PlaySession play)
    {
        var p = play.LastStepProfile;
        return [p.Setup, p.Callbacks, p.InputPrepare, p.WorldSignalsPrepare, p.MetadataPrepare,
            p.InstancesPrepare, p.ReceiptsPrepare, p.RenderPrepare, p.Install];
    }
    internal static void Move(World world, int stride)
    {
        if (stride == 0) return;
        var objects = world.GetEntities();
        for (int i = 0; i < objects.Count; i += stride)
        {
            var old = world.Get<TransformData>(objects[i]);
            world.Set(objects[i], old with { Position = old.Position + new global::System.Numerics.Vector3(1, 0, 0) });
        }
    }
    internal static void VerifyMoves(World world, int count, int stride, int steps)
    {
        Require(world.Count == count && world.Tick == (ulong)steps);
        for (int i = 0; i < count; i++)
            Require(world.Get<TransformData>(world.FindEntity(Id(i))).Position.X == i + (stride > 0 && i % stride == 0 ? steps : 0));
    }
}
