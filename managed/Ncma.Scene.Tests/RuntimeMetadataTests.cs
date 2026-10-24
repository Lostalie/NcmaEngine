using Ncma.Runtime;
using Ncma.Scene;

// Migrates all eleven former runtime-document cases to the actual runtime model. No facade
// emulates SceneDocument or SetBindings; each change uses the real prepared ECS publication.
internal static class RuntimeMetadataTests
{
    private readonly record struct TextData(string Text) : IComponent;
    private static void Check(bool value)
    { if (!value) throw new Exception("Runtime metadata validation assertion failed."); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return; }
        throw new Exception("Invalid runtime metadata accepted.");
    }
    private static BehaviourBindingData Binding(double value = 1) => new(Guid.NewGuid(), "Logic.\"测试\\Example", true,
        [new("Value😀", ExportKind.Double, value)]);
    private static RuntimeSceneInstance Load(SceneDocument source) => source.BakeRuntime(source.Revision).Image.Instantiate(source.Components);
    private static RuntimeSceneInstance Make(int count = 1)
    {
        var source = new SceneDocument();
        for (int i = 0; i < count; ++i) source.CreateObject("Object" + i);
        return Load(source);
    }
    private static byte[] Bytes(RuntimeSceneInstance scene) => RuntimeSceneCodec.Encode(scene.CaptureSnapshot());
    private static Dictionary<Guid, BehaviourBindingData[]> Bindings(RuntimeSceneInstance scene) => scene.CopyRuntimeBindings();
    private static Guid First(RuntimeSceneInstance scene) => scene.World.GetUuid(scene.World.GetEntities()[0]);
    private static void Commit(RuntimeSceneInstance scene, Dictionary<Guid, BehaviourBindingData[]> bindings, Action? stage = null)
    {
        scene.World.BeginStep();
        try
        {
            stage?.Invoke(); Action? metadata = null;
            Action commit = scene.World.PrepareStepView(v => metadata = scene.PrepareRuntimeStep(v, bindings));
            int bytes = scene.RuntimeValidationWork.Bytes;
            commit(); metadata!(); Check(Bytes(scene).Length == bytes);
        }
        finally { scene.World.AbortStep(); }
    }
    internal static int Run()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("metadata-only normalization matches full runtime JSON and rejects normalized duplicates", () => {
                var scene = Make(); var id = First(scene);
                var binding = new BehaviourBindingData(Guid.NewGuid(), "Type.\ud800", true, [new("A\udc00", ExportKind.Double, 3)]);
                var snapshot = scene.CaptureSnapshot();
                snapshot.Objects[0] = snapshot.Objects[0] with { Behaviours = [binding] };
                var oracle = RuntimeSceneCodec.Copy(snapshot).Objects[0].Behaviours[0];
                Commit(scene, new() { [id] = [binding] });
                var copy = scene.CopyRuntimeBindings()[id][0];
                Check(copy.TypeName == oracle.TypeName && copy.Exports[0] == oracle.Exports[0]);
                byte[] before = Bytes(scene);
                var malformed = new BehaviourBindingData(Guid.NewGuid(), "Type", true,
                    [new("\ud800", ExportKind.Double, 1), new("\udc00", ExportKind.Double, 2)]);
                scene.World.BeginStep();
                Reject(() => scene.World.PrepareStepView(v => scene.PrepareRuntimeStep(v, new() { [id] = [malformed] })));
                Reject(scene.World.CommitStep); scene.World.AbortStep(); Check(before.SequenceEqual(Bytes(scene)));
            }),
            ("metadata-only copy preserves runtime Export order and exact checkpoint bytes", () => {
                var scene = Make(); var id = First(scene); var binding = Binding() with {
                    Exports = [new("Z", ExportKind.Double, -0d), new("A", ExportKind.Double, 2)] };
                // Bake has its own canonical ordering; runtime commands preserve the submitted
                // order. Exercise that boundary, not the removed authoring Restore overload.
                Commit(scene, new() { [id] = [binding] });
                var checkpoint = scene.CaptureCheckpoint(); scene.RestoreCheckpoint(checkpoint, scene.Revision);
                byte[] before = Bytes(scene); var copy = scene.CopyRuntimeBindings();
                Check(copy[id][0].Exports.Select(e => e.Name).SequenceEqual(new[] { "Z", "A" }));
                Commit(scene, copy); Check(before.SequenceEqual(Bytes(scene)));
            }),
            ("metadata-only command copies own arrays and enforce safe-boundary access", () => {
                var scene = Make(); var id = First(scene); Commit(scene, new() { [id] = [Binding(3)] });
                var copy = scene.CopyRuntimeBindings(); copy[id][0].Exports[0] = new("mutated", ExportKind.Double, 9);
                copy[id] = []; copy.Clear(); Check(scene.GetBindings(id)[0].Exports[0].Value == 3);
                Reject(() => Task.Run(scene.CopyRuntimeBindings).GetAwaiter().GetResult());
                scene.World.BeginStep(); Reject(() => scene.CopyRuntimeBindings()); scene.World.AbortStep();
                scene.World.Destroy(scene.World.FindEntity(id)); Check(scene.CopyRuntimeBindings().Count == 0);
            }),
            ("unchanged metadata reuses private roots but binding writes and removal invalidate them", () => {
                var scene = Make(); var id = First(scene); Commit(scene, new() { [id] = [Binding(3)] });
                var field = typeof(RuntimeSceneInstance).GetField("_bindings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                var root = field.GetValue(scene); Commit(scene, Bindings(scene)); Check(ReferenceEquals(root, field.GetValue(scene)));
                Commit(scene, new() { [id] = [Binding(4)] }); Check(!ReferenceEquals(root, field.GetValue(scene)));
                Commit(scene, Bindings(scene), () => scene.World.StageDestroy(id));
                Check(scene.CopyRuntimeBindings().Count == 0);
            }),
            ("exact combined budget with escaped metadata and mixed components", () => {
                var source = new SceneDocument("Scene \"引擎😀\"");
                for (int i = 0; i < 12; ++i) {
                    var entity = source.CreateObject("Object\\" + i); if (i % 2 == 0) source.SetComponent(entity, TransformData.Identity);
                    source.SetBindings(entity, i % 3 == 0 ? [Binding(i + 0.25)] : []);
                }
                var scene = Load(source); Commit(scene, Bindings(scene));
                long before = scene.RuntimeValidationWork.BindingEncodes;
                for (int i = 0; i < 10; ++i) Commit(scene, Bindings(scene));
                Check(scene.RuntimeValidationWork.BindingEncodes == before);
            }),
            ("metadata copies survive caller changes and preserve negative zero encoding", () => {
                var scene = Make(); var id = First(scene); var binding = Binding(0);
                var values = new Dictionary<Guid, BehaviourBindingData[]> { [id] = [binding] };
                Commit(scene, values); long before = scene.RuntimeValidationWork.BindingEncodes; int size = scene.RuntimeValidationWork.Bytes;
                values[id] = [binding with { Exports = [new("Value😀", ExportKind.Double, -0d)] }]; Commit(scene, values);
                Check(scene.RuntimeValidationWork.BindingEncodes == before + 1 && scene.RuntimeValidationWork.Bytes == size + 1);
                scene.World.BeginStep();
                try {
                    Action? metadata = null;
                    Action commit = scene.World.PrepareStepView(v => metadata = scene.PrepareRuntimeStep(v, values));
                    values[id][0].Exports[0] = new("changed", ExportKind.Double, 999); values[id][0] = Binding(500);
                    commit(); metadata!(); Check(BitConverter.DoubleToInt64Bits(scene.GetBindings(id)[0].Exports[0].Value) == long.MinValue);
                } finally { scene.World.AbortStep(); }
            }),
            ("duplicate IDs invalid exports and late failure leave no cache prefix", () => {
                var scene = Make(2); var ids = scene.World.GetEntities().Select(scene.World.GetUuid).ToArray();
                var first = Binding(); var values = new Dictionary<Guid, BehaviourBindingData[]> { [ids[0]] = [first], [ids[1]] = [] };
                Commit(scene, values); byte[] before = Bytes(scene);
                foreach (bool duplicate in new[] { true, false }) {
                    values[ids[1]] = duplicate ? [first] : [Binding(double.NaN)]; scene.World.BeginStep();
                    Reject(() => scene.World.PrepareStepView(v => scene.PrepareRuntimeStep(v, values)));
                    Reject(scene.World.CommitStep); scene.World.AbortStep(); Check(before.SequenceEqual(Bytes(scene)));
                }
                values[ids[1]] = []; Commit(scene, values);
            }),
            ("aborted metadata and stale installation do not mutate runtime", () => {
                var scene = Make(); var id = First(scene); var values = Bindings(scene); Commit(scene, values);
                byte[] before = Bytes(scene); values[id] = [Binding()];
                scene.World.BeginStep(); Action? metadata = null;
                _ = scene.World.PrepareStepView(v => metadata = scene.PrepareRuntimeStep(v, values));
                Reject(metadata!); scene.World.AbortStep(); Reject(metadata!); Check(before.SequenceEqual(Bytes(scene)));
                Commit(scene, Bindings(scene)); Reject(metadata!);
            }),
            ("foreign candidate views are rejected", () => {
                var a = Make(0); var b = Make(0); a.World.BeginStep(); b.World.BeginStep();
                Reject(() => b.World.PrepareStepView(v => a.PrepareRuntimeStep(v, [])));
                a.World.AbortStep(); b.World.AbortStep(); Check(a.World.Tick == 0 && b.World.Tick == 0);
            }),
            ("exact runtime boundary includes behaviours and normalized components", () => {
                var registry = new ComponentRegistry(); registry.Register<TextData>("text", 1,
                    """{"type":"object","required":["text"],"properties":{"text":{"type":"string"}}}""", v => v);
                var source = new SceneDocument("Boundary", registry);
                for (int i = 0; i < 64; ++i) source.SetComponent(source.CreateObject("Full"), new TextData(new string('x', 65000)));
                var authoredEdge = source.CreateObject("Edge"); source.SetComponent(authoredEdge, new TextData(""));
                var edgeId = authoredEdge; source.SetBindings(edgeId, [Binding()]);
                var scene = Load(source); var edge = scene.World.FindEntity(edgeId);
                int remaining = World.MaxSnapshotBytes - Bytes(scene).Length;
                Check(remaining > 0 && remaining < 65000); scene.World.Set(edge, new TextData(new string('x', remaining)));
                var values = Bindings(scene); Commit(scene, values); Check(scene.RuntimeValidationWork.Bytes == World.MaxSnapshotBytes);
                byte[] before = Bytes(scene); scene.World.BeginStep(); scene.World.Set(edge, new TextData(new string('x', remaining + 1)));
                Reject(() => scene.World.PrepareStepView(v => scene.PrepareRuntimeStep(v, values)));
                scene.World.AbortStep(); Check(before.SequenceEqual(Bytes(scene))); Commit(scene, Bindings(scene));
            }),
            ("rename delete create and binding changes retain the exact full runtime codec oracle", () => {
                var scene = Make(); var id = First(scene);
                var values = new Dictionary<Guid, BehaviourBindingData[]> { [id] = [Binding()] }; Commit(scene, values);
                for (int i = 0; i < 20; ++i) {
                    values[id] = [values[id][0] with { Enabled = i % 2 == 0, Exports = [new("n" + i, ExportKind.Integer, i)] }];
                    Commit(scene, values, () => scene.World.StageRename(id, "Rename😀" + i));
                }
                var replacement = Guid.NewGuid(); values.Remove(id); values.Add(replacement, [Binding()]);
                Commit(scene, values, () => { scene.World.StageDestroy(id); scene.World.StageCreate(replacement, "Replacement"); });
                Check(scene.World.GetEntities().Count == 1 && scene.GetBindings(replacement).Length == 1);
            }),
        };
        int failures = 0;
        foreach (var test in cases) {
            try { test.Run(); Console.WriteLine("PASS runtime metadata " + test.Name); }
            catch (Exception error) { failures++; Console.Error.WriteLine("FAIL runtime metadata " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Runtime metadata tests: {cases.Length - failures}/{cases.Length} passed."); return failures;
    }
}
