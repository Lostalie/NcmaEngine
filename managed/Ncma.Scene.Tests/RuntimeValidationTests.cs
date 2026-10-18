using Ncma.Runtime;
using Ncma.Scene;

internal static class RuntimeValidationTests
{
    private readonly record struct TextData(string Text) : IComponent;
    private static void Check(bool value) { if (!value) throw new Exception("Runtime document validation assertion failed."); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return; }
        throw new Exception("Invalid runtime document accepted.");
    }
    private static BehaviourBindingData Binding(double value = 1) => new(Guid.NewGuid(), "Logic.\"测试\\Example", true,
        [new("Value😀", ExportKind.Double, value)]);
    private static Dictionary<Guid, BehaviourBindingData[]> Bindings(SceneDocument document) => document.World.GetEntities()
        .ToDictionary(e => document.World.GetUuid(e), e => document.GetBindings(document.World.GetUuid(e)));
    private static void Commit(SceneDocument document, Dictionary<Guid, BehaviourBindingData[]> bindings, Action? stage = null)
    {
        document.World.BeginStep();
        try
        {
            stage?.Invoke(); Action? metadata = null;
            Action commit = document.World.PrepareStepView(v => metadata = document.PrepareRuntimeStep(v, bindings));
            int bytes = document.RuntimeValidationWork.Bytes;
            commit(); metadata!(); Check(document.CaptureBytes().Length == bytes);
        }
        finally { document.World.AbortStep(); }
    }
    internal static int Run()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("exact combined budget with escaped metadata and mixed components", () => {
                var document = new SceneDocument("Scene \"引擎😀\"");
                for (int i = 0; i < 12; ++i) {
                    var entity = document.World.CreateEntity("Object\\" + i); if (i % 2 == 0) document.World.Set(entity, TransformData.Identity);
                    document.SetBindings(document.World.GetUuid(entity), i % 3 == 0 ? [Binding(i + 0.25)] : []);
                }
                Commit(document, Bindings(document));
                long before = document.RuntimeValidationWork.BindingEncodes;
                for (int i = 0; i < 10; ++i) Commit(document, Bindings(document));
                Check(document.RuntimeValidationWork.BindingEncodes == before);
            }),
            ("metadata copies survive caller changes and preserve negative zero encoding", () => {
                var document = new SceneDocument(); var entity = document.World.CreateEntity("A"); var id = document.World.GetUuid(entity);
                var binding = Binding(0); var values = new Dictionary<Guid, BehaviourBindingData[]> { [id] = [binding] };
                Commit(document, values); long before = document.RuntimeValidationWork.BindingEncodes; int size = document.RuntimeValidationWork.Bytes;
                values[id] = [binding with { Exports = [new("Value😀", ExportKind.Double, -0d)] }]; Commit(document, values);
                Check(document.RuntimeValidationWork.BindingEncodes == before + 1 && document.RuntimeValidationWork.Bytes == size + 1);
                document.World.BeginStep(); Action? metadata = null;
                Action commit = document.World.PrepareStepView(v => metadata = document.PrepareRuntimeStep(v, values));
                values[id][0].Exports[0] = new("changed", ExportKind.Double, 999); values[id][0] = Binding(500);
                commit(); metadata!(); Check(BitConverter.DoubleToInt64Bits(document.GetBindings(id)[0].Exports[0].Value) == long.MinValue);
            }),
            ("duplicate IDs invalid exports and late failure leave no cache prefix", () => {
                var document = new SceneDocument(); var a = document.World.CreateEntity("A"); var b = document.World.CreateEntity("B");
                var first = Binding(); var values = new Dictionary<Guid, BehaviourBindingData[]> { [document.World.GetUuid(a)] = [first], [document.World.GetUuid(b)] = [] };
                Commit(document, values); byte[] before = document.CaptureBytes();
                foreach (bool duplicate in new[] { true, false }) {
                    values[document.World.GetUuid(b)] = duplicate ? [first] : [Binding(double.NaN)]; document.World.BeginStep();
                    Reject(() => document.World.PrepareStepView(v => document.PrepareRuntimeStep(v, values)));
                    Reject(document.World.CommitStep); document.World.AbortStep(); Check(before.SequenceEqual(document.CaptureBytes()));
                }
                values[document.World.GetUuid(b)] = []; Commit(document, values);
            }),
            ("aborted metadata and stale installation do not mutate document", () => {
                var document = new SceneDocument(); var a = document.World.CreateEntity("A"); var values = Bindings(document); Commit(document, values);
                byte[] before = document.CaptureBytes(); values[document.World.GetUuid(a)] = [Binding()];
                document.World.BeginStep(); Action? metadata = null;
                _ = document.World.PrepareStepView(v => metadata = document.PrepareRuntimeStep(v, values));
                Reject(metadata!); document.World.AbortStep(); Reject(metadata!); Check(before.SequenceEqual(document.CaptureBytes()));
                Commit(document, Bindings(document)); Reject(metadata!);
            }),
            ("foreign candidate views are rejected", () => {
                var a = new SceneDocument(); var b = new SceneDocument(); a.World.BeginStep(); b.World.BeginStep();
                Reject(() => b.World.PrepareStepView(v => a.PrepareRuntimeStep(v, [])));
                a.World.AbortStep(); b.World.AbortStep(); Check(a.World.Tick == 0 && b.World.Tick == 0);
            }),
            ("exact document boundary includes behaviours and normalized components", () => {
                var registry = new ComponentRegistry(); registry.Register<TextData>("text", 1,
                    """{"type":"object","required":["text"],"properties":{"text":{"type":"string"}}}""", v => v);
                var document = new SceneDocument("Boundary", registry);
                for (int i = 0; i < 64; ++i) document.World.Set(document.World.CreateEntity("Full"), new TextData(new string('x', 65000)));
                var edge = document.World.CreateEntity("Edge"); document.World.Set(edge, new TextData(""));
                document.SetBindings(document.World.GetUuid(edge), [Binding()]);
                int remaining = SceneDocumentCodec.MaxBytes - document.CaptureBytes().Length;
                Check(remaining > 0 && remaining < 65000); document.World.Set(edge, new TextData(new string('x', remaining)));
                var values = Bindings(document); Commit(document, values); Check(document.RuntimeValidationWork.Bytes == SceneDocumentCodec.MaxBytes);
                byte[] before = document.CaptureBytes(); document.World.BeginStep(); document.World.Set(edge, new TextData(new string('x', remaining + 1)));
                Reject(() => document.World.PrepareStepView(v => document.PrepareRuntimeStep(v, values)));
                document.World.AbortStep(); Check(before.SequenceEqual(document.CaptureBytes())); Commit(document, Bindings(document));
            }),
            ("rename delete create and binding changes retain the exact full codec oracle", () => {
                var document = new SceneDocument(); var a = document.World.CreateEntity("A"); var id = document.World.GetUuid(a);
                var values = new Dictionary<Guid, BehaviourBindingData[]> { [id] = [Binding()] }; Commit(document, values);
                for (int i = 0; i < 20; ++i) {
                    values[id] = [values[id][0] with { Enabled = i % 2 == 0, Exports = [new("n" + i, ExportKind.Integer, i)] }];
                    Commit(document, values, () => document.World.StageRename(id, "Rename😀" + i));
                }
                var replacement = Guid.NewGuid(); values.Remove(id); values.Add(replacement, [Binding()]);
                Commit(document, values, () => { document.World.StageDestroy(id); document.World.StageCreate(replacement, "Replacement"); });
                Check(document.World.GetEntities().Count == 1 && document.GetBindings(replacement).Length == 1);
            }),
        };
        int failures = 0;
        foreach (var test in cases) { try { test.Run(); Console.WriteLine("PASS runtime document validation " + test.Name); }
            catch (Exception error) { failures++; Console.Error.WriteLine("FAIL runtime document validation " + test.Name + ": " + error); } }
        Console.WriteLine($"Runtime document validation tests: {cases.Length - failures}/{cases.Length} passed."); return failures;
    }
}
