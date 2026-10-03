using System.Diagnostics;
using System.Text.Json;
using Ncma;
using Ncma.Gameplay;
using Ncma.Runtime;
using Ncma.Scene;

internal static class PressureProfiles
{
    private readonly record struct Extra<T>(double Value) : IComponent where T : struct;
    private readonly struct P0 { } private readonly struct P1 { } private readonly struct P2 { } private readonly struct P3 { }
    private readonly struct P4 { } private readonly struct P5 { } private readonly struct P6 { } private readonly struct P7 { }
    private sealed class Counter { internal long BehaviourTicks, SystemTicks; internal double Sum; }
    private sealed class Moving(Counter counter) : Behaviour
    {
        protected override void OnFixedUpdate(double dt)
        {
            long start = Stopwatch.GetTimestamp();
            var value = GameObject.LocalTransform; value.Position.X += (float)dt; GameObject.LocalTransform = value;
            counter.BehaviourTicks += Stopwatch.GetTimestamp() - start;
        }
    }
    private sealed class ReadSystem(Counter counter) : IWorldSystem
    {
        public void FixedUpdate(World world, double dt)
        {
            long start = Stopwatch.GetTimestamp(); double sum = 0;
            foreach (var item in world.GetObjects()) sum += item.Get<TransformData>().Position.X + dt;
            counter.Sum = sum; counter.SystemTicks += Stopwatch.GetTimestamp() - start;
        }
    }
    private static object Stats(IEnumerable<double> values)
    {
        var array = values.Order().ToArray();
        return new { median = array[array.Length / 2], p95 = array[(int)Math.Ceiling(array.Length * .95) - 1], max = array[^1] };
    }
    internal static void Run()
    {
        foreach (var setup in new (int Objects, int Components, int Bindings)[] { (128, 9, 4), (32, 9, 64), (1024, 1, 1), (4096, 1, 1) })
        {
            var registry = ComponentRegistry.CreateDefault();
            const string schema = """{"type":"object","additionalProperties":false,"required":["value"],"properties":{"value":{"type":"number"}}}""";
            void Register<T>(int id) where T : struct => registry.Register<Extra<T>>("profile.extra." + id, 1, schema, v => v);
            Register<P0>(0); Register<P1>(1); Register<P2>(2); Register<P3>(3);
            Register<P4>(4); Register<P5>(5); Register<P6>(6); Register<P7>(7);
            var doc = new SceneDocument("Pressure", registry); var extra = JsonSerializer.SerializeToElement(new { value = 1d });
            var components = new[] { new ComponentSnapshot("ncma.transform", 1, registry.Encode(TransformData.Identity)) }
                .Concat(Enumerable.Range(0, setup.Components - 1).Select(i => new ComponentSnapshot("profile.extra." + i, 1, extra))).ToArray();
            var scene = new SceneDocumentSnapshot(1, "Pressure", Enumerable.Range(0, setup.Objects).Select(i => new SceneObjectData(
                Guid.NewGuid(), "Object " + i, components, Enumerable.Range(0, setup.Bindings).Select(_ => new BehaviourBindingData(
                    Guid.NewGuid(), "Profile.Moving", true, [])).ToArray())).ToArray());
            doc.RestoreSnapshot(scene); int sceneBytes = doc.CaptureBytes().Length; var counter = new Counter();
            using var play = new PlaySession(doc) { MeasureSteps = true };
            play.AddSystem(new ReadSystem(counter)); play.Start(_ => new Moving(counter));
            var frames = new List<double>(); var allocations = new List<double>();
            var layers = new Dictionary<string, List<double>>();
            void Add(string name, long ticks)
            { if (!layers.TryGetValue(name, out var values)) layers[name] = values = []; values.Add(ticks * 1000d / Stopwatch.Frequency); }
            for (int n = 0; n < 40; n++)
            {
                counter.BehaviourTicks = counter.SystemTicks = 0;
                long before = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
                var status = play.AdvanceFrame(1d / 60); timer.Stop();
                if (status.State != PlayState.Running || status.StepsExecuted != 1) throw new Exception("Pressure step failed");
                if (n < 8) continue;
                frames.Add(timer.Elapsed.TotalMilliseconds); allocations.Add(GC.GetAllocatedBytesForCurrentThread() - before);
                var p = play.LastStepProfile;
                Add("behaviour", counter.BehaviourTicks); Add("system", counter.SystemTicks); Add("setup", p.Setup); Add("callbacks", p.Callbacks);
                Add("inputPrepare", p.InputPrepare); Add("worldAndSignalsPrepare", p.WorldSignalsPrepare); Add("metadataPrepare", p.MetadataPrepare);
                Add("instancesPrepare", p.InstancesPrepare); Add("receiptsPrepare", p.ReceiptsPrepare); Add("renderPrepare", p.RenderPrepare); Add("install", p.Install);
            }
            if (play.RenderView.Objects.Length != setup.Objects || play.Tick != 40 || !double.IsFinite(counter.Sum)) throw new Exception("Pressure output invalid");
            Console.WriteLine(JsonSerializer.Serialize(new { fixture = "runtime_pressure_one_fixed_step", objects = setup.Objects,
                componentsPerObject = setup.Components, bindingsPerObject = setup.Bindings, systems = 1, sceneBytes, renderObjects = play.RenderView.Objects.Length,
                samples = frames.Count, frameMs = Stats(frames), allocationBytes = Stats(allocations),
                layerMs = layers.ToDictionary(p => p.Key, p => Stats(p.Value)) }));
        }
    }
}
