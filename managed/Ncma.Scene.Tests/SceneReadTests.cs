using System.Reflection;
using System.Text.Json;
using Ncma.Runtime;
using Ncma.Scene;

internal static class SceneReadTests
{
    private readonly record struct TextValue(string Text) : IComponent;
    private readonly record struct NumberValue(int Value) : IComponent;
    private struct VolatileValue : IComponent
    {
        internal static Action? Hook;
        internal static int Current;
        public int Value { get { Hook?.Invoke(); return Current; } set { } }
    }
    private static void Check(bool value) { if (!value) throw new Exception("E6 authoring read assertion failed."); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or JsonException) { return; }
        throw new Exception("Unsafe authoring operation accepted.");
    }
    private static long Captures(SceneDocument doc) => (long)typeof(SceneDocument)
        .GetField("_capturedDocumentCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(doc)!;
    private static void Equal(SceneDocumentSnapshot a, SceneDocumentSnapshot b) =>
        Check(SceneDocumentCodec.Encode(a).SequenceEqual(SceneDocumentCodec.Encode(b)));
    private static ComponentRegistry Registry()
    {
        var result = ComponentRegistry.CreateDefault();
        result.Register<TextValue>("test.text", 1, """{"type":"object","additionalProperties":false,"required":["text"],"properties":{"text":{"type":"string"}}}""", v => v);
        result.Register<VolatileValue>("test.volatile", 1, """{"type":"object","additionalProperties":false,"required":["value"],"properties":{"value":{"type":"integer"}}}""", v => v);
        return result;
    }
    internal static int Run()
    {
        var cases = new (string, Action)[] {
            ("page/object copies match full codec without recapturing the authoring document", () => {
                var doc = new SceneDocument("Pages", Registry());
                for (int i = 0; i < 137; i++) { var id = doc.CreateObject("Object" + i); if (i % 3 == 0) doc.SetComponent(id, new TextValue("值" + i)); }
                var oracle = doc.CaptureSnapshot(); long before = Captures(doc);
                for (int offset = 0; offset < 150; offset += 16) {
                    var page = doc.ReadPage(offset, 16, out int total);
                    Check(total == 137); Equal(page, oracle with { Objects = oracle.Objects.Skip(offset).Take(16).ToArray() });
                }
                foreach (var obj in oracle.Objects.Take(3)) Equal(new(1, oracle.Name, [doc.ReadObject(obj.Id)!]), oracle with { Objects = [obj] });
                Check(Captures(doc) == before); // Neither cold nor warm reads capture a complete document.
            }),
            ("empty and 4096-member pages preserve exact count without offset overflow", () => {
                var doc = new SceneDocument(); Check(doc.ReadPage(0, 128, out int empty).Objects.Length == 0 && empty == 0);
                doc.RestoreSnapshot(new(1, "Full", Enumerable.Range(0, World.MaxObjects)
                    .Select(i => new SceneObjectData(Guid.NewGuid(), "Item" + i, [], [])).ToArray()));
                var oracle = doc.CaptureSnapshot(); _ = doc.ReadPage(0, 128, out _); long before = Captures(doc);
                var tail = doc.ReadPage(4090, 128, out int total); Check(total == 4096 && tail.Objects.Length == 6);
                Equal(tail, oracle with { Objects = oracle.Objects.Skip(4090).ToArray() });
                Check(doc.ReadPage(int.MaxValue, 128, out total).Objects.Length == 0 && total == 4096 && Captures(doc) == before);
                Reject(() => doc.ReadPage(-1, 1, out _)); Reject(() => doc.ReadPage(0, 0, out _)); Reject(() => doc.ReadPage(0, 129, out _));
            }),
            ("value revisions change authoring data without changing UUID membership or capturing pages", () => {
                var doc = new SceneDocument("Values", Registry()); Guid id = doc.CreateObject("A"); doc.SetComponent(id, new TextValue("old"));
                _ = doc.ReadObject(id); var members = doc.GetObjectIds(); ulong revision = doc.Revision;
                long before = Captures(doc); doc.SetComponent(id, new TextValue("new"));
                Check(doc.Revision == revision + 1 && members.SequenceEqual(doc.GetObjectIds()));
                Check(doc.ReadObject(id)!.Components[0].Data.GetProperty("text").GetString() == "new" && Captures(doc) == before);
                doc.RenameObject(id, "Renamed"); Check(doc.ReadObject(id)!.Name == "Renamed");
            }),
            ("delete/reuse and Restore rebuild UUID order and invalidate prior revision", () => {
                var doc = new SceneDocument(); Guid a = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), b = Guid.Parse("11111111-1111-1111-1111-111111111111");
                doc.CreateObject("first", a); doc.CreateObject("second", b);
                _ = doc.ReadPage(0, 2, out _); _ = doc.ReadPage(0, 2, out _); var original = doc.CaptureSnapshot();
                doc.DeleteObject(a); doc.CreateObject("new", a);
                Check(doc.ReadPage(0, 2, out _).Objects.Select(o => o.Id).SequenceEqual(new[] { b, a }));
                ulong prior = doc.Revision; Guid session = doc.SessionId; doc.RestoreSnapshot(original);
                Check(doc.Revision == prior + 1 && doc.SessionId == session); Reject(() => doc.BakeRuntime(prior));
                Equal(doc.ReadPage(0, 2, out _), original); Equal(doc.ReadPage(0, 2, out _), original);
                Check(doc.ReadObject(Guid.NewGuid()) is null);
            }),
            ("owned page and object arrays never alias source or later reads", () => {
                var doc = new SceneDocument("Owned", Registry()); Guid id = doc.CreateObject("A"); doc.SetComponent(id, new TextValue("data"));
                doc.SetBindings(id, [new(Guid.NewGuid(), "Logic", true, [new("Value", ExportKind.Double, 2)])]);
                _ = doc.ReadObject(id); var original = doc.CaptureSnapshot();
                var obj = doc.ReadObject(id)!; obj.Components[0] = obj.Components[0] with { Version = 77 }; obj.Behaviours[0].Exports[0] = new("Bad", ExportKind.Double, 99);
                var page = doc.ReadPage(0, 1, out _); page.Objects[0] = page.Objects[0] with { Name = "corrupt" };
                Equal(doc.ReadPage(0, 1, out _), original); Equal(doc.CaptureSnapshot(), original);
            }),
            ("normalized strings and restored Export order match original codec", () => {
                var doc = new SceneDocument("UTF", Registry()); Guid id = doc.CreateObject("Name\ud800"); doc.SetComponent(id, new TextValue("\udc00"));
                doc.SetBindings(id, [new(Guid.NewGuid(), "Logic\ud800", true, [new("Name\udc00", ExportKind.Double, -0d)])]);
                var oracle = doc.CaptureSnapshot(); Equal(doc.ReadPage(0, 1, out _), oracle); Equal(doc.ReadPage(0, 1, out _), oracle);
                var binding = new BehaviourBindingData(Guid.NewGuid(), "Logic", true, [new("Z", ExportKind.Double, -0d), new("A", ExportKind.Double, 2)]);
                doc.RestoreSnapshot(new(1, "Restored", [new(id, "A", [], [binding])]));
                _ = doc.ReadObject(id); Check(doc.ReadObject(id)!.Behaviours[0].Exports.Select(e => e.Name).SequenceEqual(new[] { "Z", "A" }));
            }),
            ("page-external normalized metadata failure is rejected at write without poisoning valid reads", () => {
                var doc = new SceneDocument(); Guid a = doc.CreateObject("visible"), b = doc.CreateObject("off-page");
                _ = doc.ReadPage(0, 1, out _); var original = doc.CaptureSnapshot(); ulong revision = doc.Revision;
                Reject(() => doc.SetBindings(b, [new(Guid.NewGuid(), "Logic", true, [new("\ud800", ExportKind.Double, 1), new("\udc00", ExportKind.Double, 2)])]));
                Check(doc.Revision == revision && doc.GetBindings(b).Length == 0);
                Equal(doc.CaptureSnapshot(), original); Equal(doc.ReadPage(0, 1, out _), original with { Objects = [original.Objects[0]] });
                Check(doc.ReadObject(a)!.Name == "visible" && doc.ReadObject(Guid.NewGuid()) is null);
            }),
            ("global byte budget rejects the entire candidate including objects outside a requested page", () => {
                var doc = new SceneDocument("Budget", Registry()); Guid id = doc.CreateObject("first");
                _ = doc.ReadPage(0, 1, out _); var original = doc.CaptureSnapshot(); ulong revision = doc.Revision;
                var payload = new ComponentSnapshot("test.text", 1, doc.Components.Encode(new TextValue(new string('x', 62000))));
                var large = original with { Objects = [.. original.Objects, .. Enumerable.Range(0, 70)
                    .Select(i => new SceneObjectData(Guid.NewGuid(), "large" + i, [payload], []))] };
                Reject(() => doc.RestoreSnapshot(large));
                Check(doc.Revision == revision && doc.ObjectCount == 1 && doc.ReadObject(id)!.Name == "first");
                Equal(doc.CaptureSnapshot(), original); Equal(doc.ReadPage(0, 1, out _), original);
            }),
            ("volatile getters cannot silently change committed JSON or run during page reads", () => {
                VolatileValue.Hook = null; VolatileValue.Current = 1;
                var doc = new SceneDocument("Volatile", Registry()); Guid a = doc.CreateObject("visible"), b = doc.CreateObject("off-page"); doc.SetComponent(b, new VolatileValue());
                _ = doc.ReadPage(0, 1, out _); long before = Captures(doc); ulong revision = doc.Revision;
                VolatileValue.Current = 2; Check(doc.ReadObject(b)!.Components[0].Data.GetProperty("value").GetInt32() == 1);
                Check(Captures(doc) == before && doc.Revision == revision);
                VolatileValue.Hook = () => throw new ArgumentException("off-page encoding failed");
                try {
                    Check(doc.ReadPage(0, 1, out _).Objects.Single().Id == a && doc.ReadObject(b)!.Components[0].Data.GetProperty("value").GetInt32() == 1);
                    Reject(() => doc.SetComponent(b, new VolatileValue())); Check(doc.Revision == revision);
                } finally { VolatileValue.Hook = null; }
                doc.SetComponent(b, new VolatileValue());
                Check(doc.Revision == revision + 1 && doc.ReadObject(b)!.Components[0].Data.GetProperty("value").GetInt32() == 2);
                doc.RemoveComponent<VolatileValue>(b); _ = doc.ReadPage(0, 1, out _); before = Captures(doc); _ = doc.ReadPage(0, 1, out _); Check(Captures(doc) == before);
            }),
            ("schema-invalid payload is rejected before publication and prior diagnostic data stays readable", () => {
                var registry = ComponentRegistry.CreateDefault(); registry.Register<NumberValue>("test.mismatch", 1,
                    """{"type":"object","additionalProperties":false,"required":["value"],"properties":{"value":{"type":"string"}}}""", v => v);
                var doc = new SceneDocument("Diagnostic", registry); Guid id = doc.CreateObject("A"); var original = doc.CaptureSnapshot(); ulong revision = doc.Revision;
                Reject(() => doc.SetComponent(id, new NumberValue(7)));
                Check(doc.Revision == revision && doc.GetComponents(id).Length == 0);
                Equal(doc.ReadPage(0, 1, out _), original); Equal(doc.CaptureSnapshot(), original); doc.ValidateAuthoring();
            }),
            ("owner-thread and authoring committed boundary apply to cold and warm reads", () => {
                var doc = new SceneDocument(); Guid id = doc.CreateObject("A"); _ = doc.ReadObject(id);
                Reject(() => Task.Run(() => doc.ReadObject(id)).GetAwaiter().GetResult());
                Reject(() => Task.Run(() => doc.ReadPage(0, 1, out _)).GetAwaiter().GetResult());
                var commit = doc.PrepareRestore(doc.CaptureSnapshot(), doc.Revision, snapshot => {
                    Reject(() => doc.ReadPage(0, 1, out _)); Reject(() => doc.ReadObject(id));
                    snapshot.Objects[0] = snapshot.Objects[0] with { Name = "Detached callback" };
                });
                using (doc.ReadOnly()) { Reject(commit); Check(doc.ReadObject(id)!.Name == "A"); }
                commit(); Check(doc.ReadObject(id)!.Name == "A");
            }),
            ("encoding callbacks cannot mutate or reenter authoring during a candidate write", () => {
                VolatileValue.Hook = null; var doc = new SceneDocument("Callback", Registry()); Guid id = doc.CreateObject("A"); doc.SetComponent(id, new VolatileValue());
                ulong revision = doc.Revision; var original = doc.CaptureSnapshot(); VolatileValue.Hook = () => doc.RenameObject(id, "Bad");
                try { Reject(() => doc.SetComponent(id, new VolatileValue())); Check(doc.GetObjectName(id) == "A" && doc.Revision == revision); }
                finally { VolatileValue.Hook = null; }
                VolatileValue.Hook = () => doc.ReadPage(0, 1, out _);
                try { Reject(() => doc.SetComponent(id, new VolatileValue())); } finally { VolatileValue.Hook = null; }
                Check(doc.Revision == revision); Equal(doc.CaptureSnapshot(), original);
            }),
            ("delete immediately removes bindings; same-UUID recreation never resurrects scripts", () => {
                var doc = new SceneDocument(); Guid id = doc.CreateObject("A"); var binding = new BehaviourBindingData(Guid.NewGuid(), "Logic", true, []);
                doc.SetBindings(id, [binding]); ulong revision = doc.Revision; doc.SetComponent(id, TransformData.Identity);
                Check(doc.Revision == revision + 1 && doc.GetBindings(id).Length == 1);
                doc.DeleteObject(id); Reject(() => doc.GetBindings(id)); doc.CreateObject("Recreated", id); Check(doc.GetBindings(id).Length == 0);
                doc.SetBindings(id, [binding]); doc.DeleteObject(id); _ = doc.Revision; doc.CreateObject("Observed deletion", id); Check(doc.GetBindings(id).Length == 0);
            })
        };
        int failed = 0;
        foreach (var (name, run) in cases) try { run(); Console.WriteLine("PASS E6 authoring read " + name); } catch (Exception e) { failed++; Console.Error.WriteLine("FAIL E6 authoring read " + name + ": " + e); }
        Console.WriteLine($"E6 authoring scene reads: {cases.Length - failed}/{cases.Length} passed.");
        return failed;
    }
}
