using System.Text.Json;
using Ncma.Runtime;
using Ncma.Scene;

internal static class RuntimeInspectionTests
{
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or JsonException) { return; }
        throw new Exception("Unsafe runtime inspection accepted.");
    }
    private static RuntimeSceneInstance Make(int count = 3)
    {
        var source = new SceneDocument("Inspection");
        for (int i = 0; i < count; i++)
        {
            var entity = source.CreateObject("Object " + i);
            source.SetComponent(entity, TransformData.Identity);
            source.SetBindings(entity, [new(Guid.NewGuid(), "Game.Probe", true,
                [new("Value", ExportKind.Double, -0d)])]);
        }
        return source.BakeRuntime(source.Revision).Image.Instantiate(source.Components);
    }
    private static void CommitMetadata(RuntimeSceneInstance scene)
    {
        var bindings = scene.CopyRuntimeBindings(); var id = bindings.Keys.First();
        bindings[id][0] = bindings[id][0] with { Enabled = false };
        scene.World.BeginStep();
        try
        {
            Action? metadata = null;
            var commit = scene.World.PrepareStepView(view => metadata = scene.PrepareRuntimeStep(view, bindings));
            commit(); metadata!();
        }
        finally { scene.World.AbortStep(); }
    }
    public static IEnumerable<(string, Action)> Cases()
    {
        yield return ("E6-A runtime inspection pages preserve creation order without whole snapshots", () => {
            var scene = Make(260); var expected = scene.CaptureSnapshot(); long captures = scene.CapturedSnapshotCount;
            var stamp = scene.GetInspectionStamp(); var ids = new List<Guid>();
            for (int offset = 0; offset < expected.Objects.Length; offset += 128)
            {
                var page = scene.InspectObjects(stamp, offset);
                Check(page.Stamp == stamp && page.TotalCount == 260 && page.Offset == offset && page.ObjectId is null, "Incorrect page envelope");
                for (int i = 0; i < page.Items.Length; i++)
                    Check(page.Items[i] == new RuntimeObjectInfo(expected.Objects[offset + i].Id, expected.Objects[offset + i].Name), "Wrong object order");
                ids.AddRange(page.Items.Select(x => x.Id));
            }
            Check(ids.SequenceEqual(expected.Objects.Select(x => x.Id)) && scene.CapturedSnapshotCount == captures, "Partial/duplicated page or full snapshot capture");
            var removed = scene.World.GetEntities()[0]; scene.World.Destroy(removed);
            var replacement = scene.World.CreateEntity("Replacement");
            var tail = scene.InspectObjects(scene.GetInspectionStamp(), 259, 1);
            Check(tail.Items.Single().Id == scene.World.GetUuid(replacement), "Slot reuse broke creation order");
        });
        yield return ("E6-A runtime inspection copies components bindings and exports without write authority", () => {
            var scene = Make(); var stamp = scene.GetInspectionStamp(); var id = scene.InspectObjects(stamp).Items[0].Id;
            var components = scene.InspectComponents(stamp, id); var binding = scene.InspectBindings(stamp, id);
            var exports = scene.InspectExports(stamp, id, binding.Items[0].Id);
            Check(components.Items.Single().TypeId == "ncma.transform" && binding.Items[0].ExportCount == 1 &&
                BitConverter.DoubleToInt64Bits(exports.Items[0].Value) == long.MinValue, "Component/binding/negative zero lost");
            var retained = components.Items[0].Data;
            components.Items[0] = null!; binding.Items[0] = binding.Items[0] with { Enabled = false }; exports.Items[0] = new("Changed", ExportKind.Double, 12);
            Check(scene.GetBindings(id)[0].Enabled && scene.GetBindings(id)[0].Exports[0].Name == "Value" && retained.GetProperty("position").GetProperty("x").GetSingle() == 0, "Inspection aliases live state");
            Check(scene.GetInspectionStamp() == stamp, "Read changed runtime stamps");
        });
        yield return ("E6-A runtime inspection rejects every stale stamp dimension and foreign instance", () => {
            var scene = Make(); var stamp = scene.GetInspectionStamp(); var id = scene.InspectObjects(stamp).Items[0].Id;
            foreach (var bad in new[] { default, stamp with { Version = 99 }, stamp with { SceneSessionId = Guid.NewGuid() },
                stamp with { WorldId = Guid.NewGuid() }, stamp with { Revision = stamp.Revision + 1 }, stamp with { Tick = stamp.Tick + 1 } })
            {
                Reject(() => scene.InspectObjects(bad)); Reject(() => scene.InspectComponents(bad, id));
                Reject(() => scene.InspectBindings(bad, id)); Reject(() => scene.InspectExports(bad, id, scene.GetBindings(id)[0].Id));
            }
            Reject(() => Make().InspectObjects(stamp));
        });
        yield return ("E6-A runtime inspection invalidates metadata world edits ticks and checkpoint identities", () => {
            var scene = Make(); var stamp = scene.GetInspectionStamp(); CommitMetadata(scene);
            Reject(() => scene.InspectObjects(stamp)); stamp = scene.GetInspectionStamp();
            Check(!scene.InspectBindings(stamp, scene.World.GetUuid(scene.World.GetEntities()[0])).Items[0].Enabled, "Committed metadata invisible");
            scene.World.Rename(scene.World.GetEntities()[0], "Changed"); Reject(() => scene.InspectObjects(stamp));
            stamp = scene.GetInspectionStamp(); new WorldRunner(scene.World).Advance(1d / 60);
            Reject(() => scene.InspectObjects(stamp)); stamp = scene.GetInspectionStamp();
            var checkpoint = scene.CaptureCheckpoint(); scene.RestoreCheckpoint(checkpoint, scene.Revision);
            Reject(() => scene.InspectObjects(stamp));
            Check(scene.GetInspectionStamp().WorldId != stamp.WorldId && scene.InspectObjects(scene.GetInspectionStamp()).Items[0].Name == "Changed", "Restore did not refresh query identity");
        });
        yield return ("E6-A runtime inspection bounds offsets counts and exact object binding identities", () => {
            var scene = Make(); var stamp = scene.GetInspectionStamp(); var id = scene.World.GetUuid(scene.World.GetEntities()[0]); var binding = scene.GetBindings(id)[0].Id;
            foreach (int limit in new[] { -1, 0, 129, int.MaxValue })
            {
                Reject(() => scene.InspectObjects(stamp, 0, limit)); Reject(() => scene.InspectComponents(stamp, id, 0, limit));
                Reject(() => scene.InspectBindings(stamp, id, 0, limit)); Reject(() => scene.InspectExports(stamp, id, binding, 0, limit));
            }
            Reject(() => scene.InspectObjects(stamp, -1)); Reject(() => scene.InspectComponents(stamp, id, -1));
            Reject(() => scene.InspectBindings(stamp, id, -1)); Reject(() => scene.InspectExports(stamp, id, binding, -1));
            Reject(() => scene.InspectComponents(stamp, Guid.Empty)); Reject(() => scene.InspectBindings(stamp, Guid.NewGuid()));
            Reject(() => scene.InspectExports(stamp, id, Guid.NewGuid()));
            Check(scene.InspectObjects(stamp, int.MaxValue).Items.Length == 0 && scene.InspectComponents(stamp, id, int.MaxValue).Items.Length == 0 &&
                scene.InspectBindings(stamp, id, int.MaxValue).Items.Length == 0 && scene.InspectExports(stamp, id, binding, int.MaxValue).Items.Length == 0, "Large offset overflow");
        });
        yield return ("E6-A runtime inspection rejects oversized whole responses without truncation or state changes", () => {
            var registry = ComponentRegistry.CreateDefault();
            const string schema = """{"type":"object","required":["text"],"properties":{"text":{"type":"string"}}}""";
            registry.Register<Payload<A>>("text.a", 1, schema, v => v); registry.Register<Payload<B>>("text.b", 1, schema, v => v);
            registry.Register<Payload<C>>("text.c", 1, schema, v => v); registry.Register<Payload<D>>("text.d", 1, schema, v => v);
            registry.Register<Payload<E>>("text.e", 1, schema, v => v);
            var source = new SceneDocument("Large", registry); var entity = source.CreateObject("Payload"); string value = new('x', 60000);
            source.SetComponent(entity, new Payload<A>(value)); source.SetComponent(entity, new Payload<B>(value)); source.SetComponent(entity, new Payload<C>(value));
            source.SetComponent(entity, new Payload<D>(value)); source.SetComponent(entity, new Payload<E>(value));
            var scene = source.BakeRuntime(source.Revision).Image.Instantiate(registry); var stamp = scene.GetInspectionStamp(); var id = entity;
            Reject(() => scene.InspectComponents(stamp, id)); Check(scene.GetInspectionStamp() == stamp, "Budget rejection changed state");
            for (int i = 0; i < 5; i++)
            {
                var page = scene.InspectComponents(stamp, id, i, 1);
                Check(page.TotalCount == 5 && page.Items.Length == 1 && page.Items[0].Data.GetProperty("text").GetString() == value, "Bounded retry silently lost payload");
                Check(SceneJson.EncodeBounded(page, RuntimeSceneInstance.MaxInspectionBytes).Length <= RuntimeSceneInstance.MaxInspectionBytes, "Envelope exceeds declared bound");
            }
        });
        yield return ("E6-A runtime inspection rejects foreign threads and active candidate reads", () => {
            var scene = Make(); var stamp = scene.GetInspectionStamp();
            Task.Run(() => { Reject(() => scene.GetInspectionStamp()); Reject(() => scene.InspectObjects(stamp)); }).GetAwaiter().GetResult();
            scene.World.BeginStep();
            try { Reject(() => scene.GetInspectionStamp()); Reject(() => scene.InspectObjects(stamp)); }
            finally { scene.World.AbortStep(); }
            Check(scene.InspectObjects(stamp).Items.Length == 3, "Rejected read poisoned committed access");
        });
        yield return ("E6-A runtime inspection prevents encoder reentry writes and retains guard after failures", () => {
            var registry = ComponentRegistry.CreateDefault(); registry.Register<HookValue>("hook", 1,
                """{"type":"object","required":["value"],"properties":{"value":{"type":"integer"}}}""", v => v);
            var source = new SceneDocument("Hook", registry); var entity = source.CreateObject("Original"); source.SetComponent(entity, new HookValue());
            var scene = source.BakeRuntime(source.Revision).Image.Instantiate(registry); var stamp = scene.GetInspectionStamp(); var id = entity;
            bool entered = false;
            HookValue.Hook = () => {
                entered = true; Reject(() => scene.InspectObjects(stamp)); Reject(() => scene.CaptureSnapshot());
                Reject(() => scene.World.Rename(scene.World.FindEntity(id), "Illegal"));
            };
            try { scene.InspectComponents(stamp, id); Check(entered, "Encoder hook did not run"); }
            finally { HookValue.Hook = null; }
            Check(scene.GetInspectionStamp() == stamp && scene.InspectObjects(stamp).Items[0].Name == "Original", "Read guard leaked or mutation escaped");
        });
    }
    private readonly record struct Payload<T>(string Text) : IComponent where T : struct;
    private readonly struct A { } private readonly struct B { } private readonly struct C { }
    private readonly struct D { } private readonly struct E { }
    private struct HookValue : IComponent
    {
        internal static Action? Hook;
        public int Value { get { Hook?.Invoke(); return 1; } set { } }
    }
}
