using System.Reflection;
using System.Text;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Runtime;
using Ncma.Scene;

internal static class ReadQueryTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly record struct Diagnostic(int Value) : IComponent;
    private readonly record struct TextA(string Value) : IComponent;
    private readonly record struct TextB(string Value) : IComponent;
    private readonly record struct TextC(string Value) : IComponent;
    private readonly record struct TextD(string Value) : IComponent;
    private readonly record struct TextE(string Value) : IComponent;
    private static void Check(bool value) { if (!value) throw new Exception("E4-D capability read assertion failed."); }
    private static CapabilityRequest Request(EditSession edit, string capability, object input) =>
        new(EditSession.ContractVersion, Guid.NewGuid(), edit.SessionId, edit.Revision, capability, JsonSerializer.SerializeToElement(input));
    private static CapabilityResult Page(EditSession edit, int offset = 0, int limit = 32) => edit.Invoke(Request(edit, "ncma.scene.inspect", new { offset, limit }));
    private static CapabilityResult Object(EditSession edit, Guid id, string section = "summary", int offset = 0, int limit = 16) =>
        edit.Invoke(Request(edit, "ncma.scene.object.inspect", new { objectId = id, section, offset, limit }));
    private static long Captures(SceneDocument doc) => (long)typeof(SceneDocument).GetField("_capturedDocumentCount", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(doc)!;
    private static void ExactPage(EditSession edit, SceneDocumentSnapshot snapshot, int offset, int limit)
    {
        var expected = JsonSerializer.SerializeToElement(new { worldId = edit.Document.World.Identity, tick = edit.Document.World.Tick,
            totalCount = snapshot.Objects.Length, offset, limit, scene = new { snapshot.Version, snapshot.Name,
                objects = snapshot.Objects.Skip(offset).Take(limit).Select(item => new { item.Id, item.Name,
                    components = item.Components.Select(c => new { c.TypeId, c.Version }),
                    behaviours = item.Behaviours.Select(b => new { b.Id, b.TypeName, b.Enabled, exportCount = b.Exports.Length }) }) } }, Json);
        var actual = Page(edit, offset, limit); Check(actual.Status == "ok" && !actual.Changed && actual.Data.GetRawText() == expected.GetRawText());
    }
    internal static int Run()
    {
        var cases = new (string, Action)[] {
            ("existing scene capability matches complete-snapshot oracle on cold and warm pages", () => {
                var doc = new SceneDocument("查询"); for (int i = 0; i < 140; i++) { var e = doc.World.CreateEntity("Object" + i); if (i % 2 == 0) doc.World.Set(e, TransformData.Identity); }
                var edit = new EditSession(doc); var oracle = doc.CaptureSnapshot(); long before = Captures(doc);
                ExactPage(edit, oracle, 0, 128); ExactPage(edit, oracle, 128, 128); ExactPage(edit, oracle, int.MaxValue, 128);
                Check(Captures(doc) == before + 1 && Page(edit, -1).Code == "invalid_input" && Page(edit, 0, 129).Code == "invalid_input");
            }),
            ("read cache preserves missing-object precedence and detail pagination constraints", () => {
                var doc = new SceneDocument(); var id = doc.World.GetUuid(doc.World.CreateEntity("A")); var edit = new EditSession(doc);
                Check(Object(edit, id).Status == "ok"); long before = Captures(doc);
                Check(Object(edit, id, "summary", int.MaxValue, 64).Data.GetProperty("returnedCount").GetInt32() == 0);
                Check(Object(edit, Guid.NewGuid(), "unknown").Code == "object_not_found");
                Check(Object(edit, id, "unknown").Code == "invalid_input" && Object(edit, id, limit: 65).Code == "invalid_input");
                Check(Object(edit, id, offset: -1).Code == "invalid_input" && Captures(doc) == before);
            }),
            ("session revision permissions and history remain authoritative after warming reads", () => {
                var doc = new SceneDocument(); var id = doc.World.GetUuid(doc.World.CreateEntity("A")); var edit = new EditSession(doc);
                var original = doc.CaptureSnapshot(); ExactPage(edit, original, 0, 1);
                var stale = Request(edit, "ncma.scene.inspect", new { }); long before = Captures(doc);
                Check(edit.Invoke(stale with { SessionId = Guid.NewGuid() }).Code == "session_mismatch" && Captures(doc) == before);
                var rename = Request(edit, "ncma.scene.transaction", new { operations = new[] { new { op = "rename", objectId = id, name = "B" } } });
                Check(edit.Invoke(rename).Code == "permission_denied");
                var permissions = new CapabilityPermissions(["ncma.scene.transaction", "ncma.history.undo", "ncma.history.redo"]);
                Check(edit.Invoke(rename, permissions).Changed); Check(edit.Invoke(stale).Code == "revision_conflict");
                Check(Object(edit, id).Data.GetProperty("items")[0].GetProperty("name").GetString() == "B");
                Check(edit.Invoke(Request(edit, "ncma.history.undo", new { }), permissions).Changed); ExactPage(edit, original, 0, 1);
                Check(edit.Invoke(Request(edit, "ncma.history.redo", new { }), permissions).Changed); Check(Object(edit, id).Data.GetProperty("items")[0].GetProperty("name").GetString() == "B");
                edit.SetFrozen(true); Check(Page(edit).Status == "ok"); Check(edit.Invoke(Request(edit, "ncma.scene.transaction", new { operations = new[] { new { op = "rename", objectId = id, name = "C" } } }), permissions).Code == "play_frozen");
                edit.SetFrozen(false);
            }),
            ("readable schema diagnostics remain distinct from scene.validate", () => {
                var registry = ComponentRegistry.CreateDefault(); registry.Register<Diagnostic>("test.diagnostic", 1,
                    """{"type":"object","additionalProperties":false,"required":["value"],"properties":{"value":{"type":"string"}}}""", v => v);
                var doc = new SceneDocument("Diagnostic", registry); var e = doc.World.CreateEntity("A"); doc.World.Set(e, new Diagnostic(7));
                var edit = new EditSession(doc); Check(Page(edit).Status == "ok" && Object(edit, doc.World.GetUuid(e), "components").Status == "ok");
                Check(edit.Invoke(Request(edit, "ncma.scene.validate", new { })).Code == "invalid_input");
                Check(Page(edit).Status == "ok" && Object(edit, doc.World.GetUuid(e), "components").Data.GetProperty("items")[0].GetProperty("data").GetProperty("value").GetInt32() == 7);
            }),
            ("scene output budget rejects oversized summaries on both read paths", () => {
                var doc = new SceneDocument(); var objects = Enumerable.Range(0, 3).Select(i => new SceneObjectData(Guid.NewGuid(), "A" + i, [],
                    Enumerable.Range(0, 512).Select(_ => new BehaviourBindingData(Guid.NewGuid(), new string('L', 256), true, [])).ToArray())).ToArray();
                doc.RestoreSnapshot(new(1, "Large summaries", objects)); var edit = new EditSession(doc);
                Check(Page(edit, 0, 3).Code == "item_too_large" && Page(edit, 0, 3).Code == "item_too_large");
                Check(Page(edit, 0, 1).Status == "ok");
            }),
            ("large detail pages retain bounded output shrinking with exact continuation", () => {
                var registry = ComponentRegistry.CreateDefault();
                void Register<T>(string id) where T : struct, IComponent => registry.Register<T>(id, 1,
                    """{"type":"object","additionalProperties":false,"required":["value"],"properties":{"value":{"type":"string"}}}""", v => v);
                Register<TextA>("test.a"); Register<TextB>("test.b"); Register<TextC>("test.c"); Register<TextD>("test.d"); Register<TextE>("test.e");
                var doc = new SceneDocument("Detail", registry); var e = doc.World.CreateEntity("A"); string text = new('x', 60000);
                doc.World.Set(e, new TextA(text)); doc.World.Set(e, new TextB(text)); doc.World.Set(e, new TextC(text)); doc.World.Set(e, new TextD(text)); doc.World.Set(e, new TextE(text));
                var edit = new EditSession(doc); Guid id = doc.World.GetUuid(e);
                for (int attempt = 0; attempt < 2; attempt++) {
                    var result = Object(edit, id, "components", 0, 64); Check(result.Status == "ok");
                    int count = result.Data.GetProperty("returnedCount").GetInt32(); Check(count > 0 && count < 5 && result.Data.GetProperty("nextOffset").GetInt32() == count);
                    Check(Encoding.UTF8.GetByteCount(EditSession.EncodeResult(result)) <= EditSession.MaxReadOutputBytes);
                    var tail = Object(edit, id, "components", count, 64); Check(tail.Data.GetProperty("returnedCount").GetInt32() == 5 - count && tail.Data.GetProperty("complete").GetBoolean());
                }
            })
        };
        int failed = 0;
        foreach (var (name, run) in cases) try { run(); Console.WriteLine("PASS E4-D " + name); } catch (Exception e) { failed++; Console.Error.WriteLine("FAIL E4-D " + name + ": " + e); }
        Console.WriteLine($"E4-D capability reads: {cases.Length - failed}/{cases.Length} passed."); return failed;
    }
}
