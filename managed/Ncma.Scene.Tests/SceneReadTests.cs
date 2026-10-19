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
    private static void Check(bool value) { if (!value) throw new Exception("E4-D scene read assertion failed."); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or JsonException) { return; }
        throw new Exception("Unsafe read accepted.");
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
            ("page/object copies match full codec and stable pages do not recapture document", () => {
                var doc = new SceneDocument("Pages", Registry());
                for (int i = 0; i < 137; i++) { var entity = doc.World.CreateEntity("Object" + i); if (i % 3 == 0) doc.World.Set(entity, new TextValue("值" + i)); }
                var oracle = doc.CaptureSnapshot(); long before = Captures(doc);
                for (int offset = 0; offset < 150; offset += 16) {
                    var page = doc.ReadPage(offset, 16, out int total);
                    Check(total == 137); Equal(page, oracle with { Objects = oracle.Objects.Skip(offset).Take(16).ToArray() });
                }
                foreach (var obj in oracle.Objects.Take(3)) Equal(new(1, oracle.Name, [doc.ReadObject(obj.Id)!]), oracle with { Objects = [obj] });
                Check(Captures(doc) == before + 1); // The registered but absent volatile type must not defeat the proof.
            }),
            ("empty and 4096-member pages preserve exact count without offset overflow", () => {
                var doc = new SceneDocument(); Check(doc.ReadPage(0, 128, out int empty).Objects.Length == 0 && empty == 0);
                for (int i = 0; i < World.MaxObjects; i++) doc.World.CreateEntity("Item" + i);
                var oracle = doc.CaptureSnapshot(); _ = doc.ReadPage(0, 128, out _); long before = Captures(doc);
                var tail = doc.ReadPage(4090, 128, out int total); Check(total == 4096 && tail.Objects.Length == 6);
                Equal(tail, oracle with { Objects = oracle.Objects.Skip(4090).ToArray() });
                Check(doc.ReadPage(int.MaxValue, 128, out total).Objects.Length == 0 && total == 4096 && Captures(doc) == before);
                Reject(() => doc.ReadPage(-1, 1, out _)); Reject(() => doc.ReadPage(0, 0, out _)); Reject(() => doc.ReadPage(0, 129, out _));
            }),
            ("value revisions invalidate read proof without changing membership", () => {
                var doc = new SceneDocument("Values", Registry()); var entity = doc.World.CreateEntity("A"); doc.World.Set(entity, new TextValue("old"));
                Guid id = doc.World.GetUuid(entity); _ = doc.ReadObject(id); ulong member = doc.World.MembershipVersion, revision = doc.Revision;
                long before = Captures(doc); doc.World.Set(entity, new TextValue("new"));
                Check(doc.Revision == revision + 1 && member == doc.World.MembershipVersion);
                Check(doc.ReadObject(id)!.Components[0].Data.GetProperty("text").GetString() == "new" && Captures(doc) == before + 1);
                doc.World.Rename(entity, "Renamed"); Check(doc.ReadObject(id)!.Name == "Renamed");
            }),
            ("delete/reuse and Restore rebuild query identity and creation order", () => {
                var doc = new SceneDocument(); Guid a = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), b = Guid.Parse("11111111-1111-1111-1111-111111111111");
                var first = doc.World.CreateEntity("first", a); doc.World.CreateEntity("second", b);
                _ = doc.ReadPage(0, 2, out _); _ = doc.ReadPage(0, 2, out _); var original = doc.CaptureSnapshot();
                doc.World.Destroy(first); doc.World.CreateEntity("new", a);
                Check(doc.ReadPage(0, 2, out _).Objects.Select(o => o.Id).SequenceEqual(new[] { b, a }));
                Guid prior = doc.World.Identity; doc.RestoreSnapshot(original); Check(doc.World.Identity != prior);
                Equal(doc.ReadPage(0, 2, out _), original); Equal(doc.ReadPage(0, 2, out _), original);
                Check(doc.ReadObject(Guid.NewGuid()) is null);
            }),
            ("owned page and object arrays never alias source or later reads", () => {
                var doc = new SceneDocument("Owned", Registry()); var entity = doc.World.CreateEntity("A"); doc.World.Set(entity, new TextValue("data")); Guid id = doc.World.GetUuid(entity);
                doc.SetBindings(id, [new(Guid.NewGuid(), "Logic", true, [new("Value", ExportKind.Double, 2)])]);
                _ = doc.ReadObject(id); var original = doc.CaptureSnapshot();
                var obj = doc.ReadObject(id)!; obj.Components[0] = obj.Components[0] with { Version = 77 }; obj.Behaviours[0].Exports[0] = new("Bad", ExportKind.Double, 99);
                var page = doc.ReadPage(0, 1, out _); page.Objects[0] = page.Objects[0] with { Name = "corrupt" };
                Equal(doc.ReadPage(0, 1, out _), original); Equal(doc.CaptureSnapshot(), original);
            }),
            ("normalized strings and restored Export order match original codec", () => {
                var doc = new SceneDocument("UTF", Registry()); var entity = doc.World.CreateEntity("Name\ud800"); doc.World.Set(entity, new TextValue("\udc00")); Guid id = doc.World.GetUuid(entity);
                doc.SetBindings(id, [new(Guid.NewGuid(), "Logic\ud800", true, [new("Name\udc00", ExportKind.Double, -0d)])]);
                var oracle = doc.CaptureSnapshot(); Equal(doc.ReadPage(0, 1, out _), oracle); Equal(doc.ReadPage(0, 1, out _), oracle);
                var binding = new BehaviourBindingData(Guid.NewGuid(), "Logic", true, [new("Z", ExportKind.Double, -0d), new("A", ExportKind.Double, 2)]);
                doc.RestoreSnapshot(new(1, "Restored", [new(id, "A", [], [binding])]));
                _ = doc.ReadObject(id); Check(doc.ReadObject(id)!.Behaviours[0].Exports.Select(e => e.Name).SequenceEqual(new[] { "Z", "A" }));
            }),
            ("page-external normalized metadata failure is never hidden by warm proof", () => {
                var doc = new SceneDocument(); var a = doc.World.CreateEntity("visible"); var b = doc.World.CreateEntity("off-page");
                Guid id = doc.World.GetUuid(a); _ = doc.ReadPage(0, 1, out _);
                doc.SetBindings(doc.World.GetUuid(b), [new(Guid.NewGuid(), "Logic", true, [new("\ud800", ExportKind.Double, 1), new("\udc00", ExportKind.Double, 2)])]);
                Reject(() => doc.CaptureSnapshot()); Reject(() => doc.ReadPage(0, 1, out _)); Reject(() => doc.ReadObject(id)); Reject(() => doc.ReadObject(Guid.NewGuid()));
            }),
            ("global byte budget is still checked outside the requested page", () => {
                var doc = new SceneDocument("Budget", Registry()); var first = doc.World.CreateEntity("first"); Guid id = doc.World.GetUuid(first);
                _ = doc.ReadPage(0, 1, out _);
                for (int i = 0; i < 70; i++) doc.World.Set(doc.World.CreateEntity("large" + i), new TextValue(new string('x', 62000)));
                Reject(() => doc.CaptureSnapshot()); Reject(() => doc.ReadPage(0, 1, out _)); Reject(() => doc.ReadObject(id));
            }),
            ("volatile off-page encodings remain cold and observable without a revision change", () => {
                VolatileValue.Hook = null; VolatileValue.Current = 1;
                var doc = new SceneDocument("Volatile", Registry()); var a = doc.World.CreateEntity("visible"); var b = doc.World.CreateEntity("off-page"); doc.World.Set(b, new VolatileValue());
                _ = doc.ReadPage(0, 1, out _); long before = Captures(doc); ulong revision = doc.Revision;
                VolatileValue.Current = 2; Check(doc.ReadObject(doc.World.GetUuid(b))!.Components[0].Data.GetProperty("value").GetInt32() == 2);
                Check(Captures(doc) == before + 1 && doc.Revision == revision);
                VolatileValue.Hook = () => throw new ArgumentException("off-page encoding failed");
                try { Reject(() => doc.ReadPage(0, 1, out _)); Reject(() => doc.ReadObject(doc.World.GetUuid(a))); }
                finally { VolatileValue.Hook = null; }
                doc.World.Remove<VolatileValue>(b); _ = doc.ReadPage(0, 1, out _); before = Captures(doc); _ = doc.ReadPage(0, 1, out _); Check(Captures(doc) == before);
            }),
            ("readable schema-invalid state remains available for diagnostics", () => {
                var registry = ComponentRegistry.CreateDefault(); registry.Register<NumberValue>("test.mismatch", 1,
                    """{"type":"object","additionalProperties":false,"required":["value"],"properties":{"value":{"type":"string"}}}""", v => v);
                var doc = new SceneDocument("Diagnostic", registry); var entity = doc.World.CreateEntity("A"); doc.World.Set(entity, new NumberValue(7));
                var oracle = doc.CaptureSnapshot(); Reject(doc.ValidateAuthoring);
                Equal(doc.ReadPage(0, 1, out _), oracle); Equal(doc.ReadPage(0, 1, out _), oracle); Reject(doc.ValidateAuthoring);
            }),
            ("owner-thread and committed boundary apply to cold and warm reads", () => {
                var doc = new SceneDocument(); var id = doc.World.GetUuid(doc.World.CreateEntity("A")); _ = doc.ReadObject(id);
                Reject(() => Task.Run(() => doc.ReadObject(id)).GetAwaiter().GetResult());
                Reject(() => Task.Run(() => doc.ReadPage(0, 1, out _)).GetAwaiter().GetResult());
                doc.World.BeginStep(); try { Reject(() => doc.ReadPage(0, 1, out _)); Reject(() => doc.ReadObject(id)); } finally { doc.World.AbortStep(); }
                Check(doc.ReadObject(id)!.Id == id);
            }),
            ("encoding callbacks cannot mutate source or recursively enter read cache", () => {
                VolatileValue.Hook = null; var doc = new SceneDocument("Callback", Registry()); var entity = doc.World.CreateEntity("A"); doc.World.Set(entity, new VolatileValue()); Guid id = doc.World.GetUuid(entity);
                ulong revision = doc.Revision; VolatileValue.Hook = () => doc.World.Rename(entity, "Bad");
                try { Reject(() => doc.ReadObject(id)); Check(doc.World.GetName(entity) == "A" && doc.Revision == revision); }
                finally { VolatileValue.Hook = null; }
                VolatileValue.Hook = () => doc.ReadPage(0, 1, out _);
                try { Reject(() => doc.ReadObject(id)); } finally { VolatileValue.Hook = null; }
                Check(doc.ReadObject(id)!.Name == "A");
            }),
            ("metadata observer keeps values, removes absent UUIDs, preserves unobserved same-UUID recreation", () => {
                var doc = new SceneDocument(); var entity = doc.World.CreateEntity("A"); Guid id = doc.World.GetUuid(entity); var binding = new BehaviourBindingData(Guid.NewGuid(), "Logic", true, []);
                doc.SetBindings(id, [binding]); ulong revision = doc.Revision; doc.World.Set(entity, TransformData.Identity);
                Check(doc.Revision == revision + 1 && doc.GetBindings(id).Length == 1);
                doc.World.Destroy(entity); entity = doc.World.CreateEntity("Recreated", id); Check(doc.GetBindings(id).Length == 1);
                doc.World.Destroy(entity); _ = doc.Revision; entity = doc.World.CreateEntity("Observed deletion", id); Check(doc.GetBindings(id).Length == 0);
            })
        };
        int failed = 0;
        foreach (var (name, run) in cases) try { run(); Console.WriteLine("PASS E4-D " + name); } catch (Exception e) { failed++; Console.Error.WriteLine("FAIL E4-D " + name + ": " + e); }
        Console.WriteLine($"E4-D scene reads: {cases.Length - failed}/{cases.Length} passed.");
        return failed;
    }
}
