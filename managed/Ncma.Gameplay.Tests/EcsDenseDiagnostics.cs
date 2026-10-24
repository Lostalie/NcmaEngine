using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Ncma.Runtime;
using Ncma.Scene;

// Opt-in test-only diagnostics. No probes, flags or branches are added to production World.
// Reflection binds delegates once outside measurements; no per-entity MethodInfo.Invoke.
internal static class EcsDenseDiagnostics
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private delegate TransformData ValidateTransform(TransformData value, out JsonElement data);
    private sealed class MovingSystem : IWorldSystem
    {
        public void FixedUpdate(World world, double dt) => EcsBaseline.Move(world, 1);
    }
    private readonly record struct Stamp(long Ticks, long Bytes, long PauseTicks)
    {
        internal static Stamp Now() => new(Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread(),
            GC.GetTotalPauseDuration().Ticks);
    }
    private sealed class Samples(int count)
    {
        internal readonly double[] Ms = new double[count], Allocations = new double[count], GcPauseMs = new double[count];
        internal void Record(int sample, Stamp start)
        {
            Stamp end = Stamp.Now();
            if (sample < 0) return;
            Ms[sample] = (end.Ticks - start.Ticks) * 1000d / Stopwatch.Frequency;
            Allocations[sample] = end.Bytes - start.Bytes;
            GcPauseMs[sample] = (end.PauseTicks - start.PauseTicks) / (double)TimeSpan.TicksPerMillisecond;
        }
        internal object Output() => new { ms = Distribution(Ms), allocatedThreadBytes = Distribution(Allocations),
            runtimeGcPauseMs = Distribution(GcPauseMs), rawMs = Ms, rawAllocatedThreadBytes = Allocations, rawRuntimeGcPauseMs = GcPauseMs };
    }
    private sealed class Step
    {
        private readonly Action<bool> _begin;
        private readonly Func<Action<WorldSnapshot>?, bool, Action> _prepare;
        private readonly Action _abort;
        private readonly World _world;
        internal readonly Samples[] Phases;
        internal Step(World world, int samples)
        {
            _world = world;
            _begin = typeof(World).GetMethod("BeginStep", Hidden)!.CreateDelegate<Action<bool>>(world);
            _prepare = typeof(World).GetMethod("PrepareStep", Hidden)!.CreateDelegate<Func<Action<WorldSnapshot>?, bool, Action>>(world);
            _abort = typeof(World).GetMethod("AbortStep", Hidden)!.CreateDelegate<Action>(world);
            Phases = Enumerable.Range(0, 5).Select(_ => new Samples(samples)).ToArray();
        }
        internal void Run(int sample)
        {
            Stamp start = Stamp.Now(); _begin(false); Phases[0].Record(sample, start);
            try
            {
                start = Stamp.Now(); EcsBaseline.Move(_world, 1); Phases[1].Record(sample, start);
                start = Stamp.Now(); Action install = _prepare(null, true); Phases[2].Record(sample, start);
                start = Stamp.Now(); install(); Phases[3].Record(sample, start);
            }
            finally { start = Stamp.Now(); _abort(); Phases[4].Record(sample, start); }
        }
    }
    private static object Distribution(double[] values)
    {
        var sorted = values.Order().ToArray();
        double P(double p) => sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * p) - 1)];
        return new { p50 = P(.50), p95 = P(.95), p99 = P(.99), max = sorted[^1] };
    }
    internal static void Run(bool measured, Action<string, int, EcsDiagnosticState>? verified = null)
    {
        int warmup = measured ? 12 : 1, samples = measured ? 100 : 2;
        if (measured) Console.WriteLine(JsonSerializer.Serialize(new {
            kind = "ecs_e6_diagnostic_environment", schema = 2, historicalPairedContractCompatible = false, runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription, cpuCount = Environment.ProcessorCount,
            serverGc = global::System.Runtime.GCSettings.IsServerGC, stopwatchFrequency = Stopwatch.Frequency,
            configuration =
#if DEBUG
                "Debug",
#else
                "Release",
#endif
            scope = "Diagnostic delegates call real fixed-step internals; omits WorldRunner envelope. Not formal paired samples.",
            gcScope = "Runtime cumulative stop-the-world pause deltas, not collection counts. Completion may lag phase; not an allocation-stack trace."
        }));
        int cases = 0;
        foreach (int count in measured ? new[] { 100, 1000, 4096 } : new[] { 8 })
        {
            var document = EcsBaseline.RuntimeFixture(count); _ = RuntimeSceneCodec.Encode(document.CaptureSnapshot());
            var step = new Step(document.World, samples);
            for (int i = 0; i < warmup; i++) step.Run(-1);
            long managedBefore = GC.GetTotalMemory(false), pauseBefore = GC.GetTotalPauseDuration().Ticks;
            int[] collectionsBefore = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
            var total = new Samples(samples);
            for (int i = 0; i < samples; i++) { Stamp start = Stamp.Now(); step.Run(i); total.Record(i, start); }
            long pauseAfter = GC.GetTotalPauseDuration().Ticks, managedAfter = GC.GetTotalMemory(false);
            int[] collections = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - collectionsBefore[i]).ToArray();
            EcsBaseline.VerifyMoves(document.World, count, 1, warmup + samples);
            // Independent PUBLIC scheduler oracle outside diagnostic timings, including full codec bytes.
            var oracle = EcsBaseline.RuntimeFixture(count); _ = RuntimeSceneCodec.Encode(oracle.CaptureSnapshot());
            var runner = new WorldRunner(oracle.World); runner.AddSystem(new MovingSystem());
            for (int i = 0; i < warmup + samples; i++)
                if (runner.Advance(1d / 60) != 1) throw new InvalidOperationException("Diagnostic oracle missed a step.");
            if (!RuntimeSceneCodec.Encode(document.CaptureSnapshot()).AsSpan().SequenceEqual(RuntimeSceneCodec.Encode(oracle.CaptureSnapshot())))
                throw new InvalidOperationException("Diagnostic step differs from public WorldRunner.");
            if (measured)
            {
                string[] names = ["begin", "callback_including_value_encoding", "prepare_including_schema_and_budget", "install_including_abort_clear", "finally_abort_clear"];
                Console.WriteLine(JsonSerializer.Serialize(new { kind = "ecs_e6_diagnostic_phases", schema = 2, historicalPairedContractCompatible = false, objects = count,
                    warmup, samples, total = total.Output(), phases = names.Select((n, i) => new { name = n, samples = step.Phases[i].Output() }),
                    managedBeforeBytes = managedBefore, managedAfterBytes = managedAfter, gcCollections = collections,
                    runtimeGcPauseMs = (pauseAfter - pauseBefore) / (double)TimeSpan.TicksPerMillisecond,
                    note = "No output/codec/oracle in timed loop. Install owns its existing clear; final clear measured separately. Phase percentiles do not sum." }));
            }
            verified?.Invoke("diagnostic_world_full_write", count, EcsDiagnosticState.FromRuntime(document)); cases++;
            ProbeComponents(document.World.Components, count, warmup, samples, measured); cases++;
        }
        Console.WriteLine($"E5 dense diagnostic fixture checks: {cases}/{cases} passed; measured={measured}.");
    }
    private static void ProbeComponents(ComponentRegistry registry, int count, int warmup, int samples, bool measured)
    {
        var validate = typeof(ComponentRegistry).GetMethods(Hidden).Single(m => m.Name == "Validate" && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(TransformData)).CreateDelegate<ValidateTransform>(registry);
        var check = typeof(ComponentRegistry).GetMethod("ValidateEncoding", Hidden)!.CreateDelegate<Action<ComponentSnapshot>>(registry);
        Type json = typeof(World).Assembly.GetType("Ncma.Runtime.SceneJson", throwOnError: true)!;
        var measure = json.GetMethod("MeasureBounded", BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(ComponentSnapshot)).CreateDelegate<Func<ComponentSnapshot, int, int>>();
        var depth = json.GetMethod("VerifySnapshotComponentDepth", BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<Action<JsonElement>>();
        TransformData value = TransformData.Identity with { Position = new(4095, 0, 0) };
        validate(value, out JsonElement data);
        var component = new ComponentSnapshot("ncma.transform", 1, data);
        int result = 0;
        (string Name, Action Operation)[] operations = [
            ("empty_probe", () => { }),
            ("normalize_encode_payload", () => { if (validate(value, out _) != value) throw new InvalidOperationException("Probe normalization mismatch."); }),
            ("schema_payload_depth", () => check(component)),
            ("component_envelope_measure", () => result = measure(component, World.MaxSnapshotBytes)),
            ("payload_raw_utf8_count", () => result = Encoding.UTF8.GetByteCount(data.GetRawText())),
            ("snapshot_depth_only", () => depth(data))
        ];
        foreach (var (name, operation) in operations)
        {
            for (int i = 0; i < warmup; i++) for (int j = 0; j < count; j++) operation();
            var values = new Samples(samples);
            for (int i = 0; i < samples; i++)
            {
                Stamp start = Stamp.Now();
                for (int j = 0; j < count; j++) operation();
                values.Record(i, start);
            }
            if (measured) Console.WriteLine(JsonSerializer.Serialize(new { kind = "ecs_e6_diagnostic_probe", schema = 2, historicalPairedContractCompatible = false,
                probe = name, operationsPerSample = count, warmup, samples, values = values.Output(),
                scope = "Repeated single immutable Transform payload, isolated cost only. Nested probes are not additive, not a World allocation stack." }));
        }
        if (result != Encoding.UTF8.GetByteCount(data.GetRawText())) throw new InvalidOperationException("Payload probe mismatch.");
    }
}
