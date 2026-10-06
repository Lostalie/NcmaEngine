using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Ncma.Runtime;
using Ncma.Scene;

static void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
static void Reject(Action action)
{
    try { action(); }
    catch (Exception e) when (e is ArgumentException or InvalidOperationException or JsonException or KeyNotFoundException or IOException or UnauthorizedAccessException) { return; }
    throw new Exception("Invalid operation accepted");
}
static ComponentRegistry Registry()
{
    var registry = ComponentRegistry.CreateDefault();
    registry.Register<Health>("game.health", 1, """{"type":"object","additionalProperties":false,"required":["points"],"properties":{"points":{"type":"integer"}}}""",
        value => value.Points >= 0 ? value : throw new ArgumentException("Negative health."));
    return registry;
}
static BehaviourBindingData Binding() => new(Guid.NewGuid(), "Not.Installed.Behaviour", false, [
    new("Float", ExportKind.Float, 0.25), new("Double", ExportKind.Double, 1e100),
    new("Integer", ExportKind.Integer, 23), new("Boolean", ExportKind.Boolean, 1)]);
static SceneDocument Populated()
{
    var doc = new SceneDocument("Scene", Registry());
    var logic = doc.World.CreateObject("Logic");
    logic.Set(new Health(100));
    var hero = doc.World.CreateObject("Hero"); hero.Set(TransformData.Identity);
    doc.SetBindings(logic.PersistentId, [Binding(), Binding()]);
    _ = doc.Revision; return doc;
}
static void AtomicReject(SceneDocument doc, SceneDocumentSnapshot snapshot)
{
    byte[] before = doc.CaptureBytes(); ulong rev = doc.Revision; Guid identity = doc.World.Identity;
    Reject(() => doc.RestoreSnapshot(snapshot));
    Check(before.SequenceEqual(doc.CaptureBytes()) && rev == doc.Revision && identity == doc.World.Identity, "Failed restore modified scene.");
}
static string AssetPath(string name)
{
    string? root = AppContext.BaseDirectory;
    while (root is not null && !File.Exists(Path.Combine(root, "Build.bat"))) root = Path.GetDirectoryName(root);
    if (root is null) throw new InvalidOperationException("Cannot locate test workspace.");
    string directory = Path.Combine(root, "out", "tests", "scene-document-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    return Path.Combine(directory, name);
}
var cases = new (string, Action)[]
{
    ("Independent managed document", () => {
        var assembly = typeof(SceneDocument).Assembly;
        Check(!assembly.GetReferencedAssemblies().Any(a => a.Name!.Contains("Python") || a.Name.StartsWith("Ncma.Managed")));
        Check(!assembly.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .Any(m => m.GetCustomAttribute<DllImportAttribute>() is not null));
    }),
    ("All components/scripts/exports roundtrip and stable bytes", () => {
        var doc = Populated(); byte[] before = doc.CaptureBytes(); var snapshot = doc.CaptureSnapshot();
        doc.RestoreBytes(before);
        Check(before.SequenceEqual(doc.CaptureBytes()));
        var logic = doc.World.FindObject(snapshot.Objects[0].Id);
        Check(!logic.Has<TransformData>() && logic.Get<Health>().Points == 100 && doc.GetBindings(logic.PersistentId).Length == 2);
    }),
    ("Snapshot and binding copies never alias live data", () => {
        var doc = Populated(); byte[] before = doc.CaptureBytes(); var snapshot = doc.CaptureSnapshot();
        snapshot.Objects[0].Behaviours[0].Exports[0] = new("Broken", ExportKind.Boolean, 99);
        snapshot.Objects[0].Components[0] = snapshot.Objects[1].Components[0];
        var bindings = doc.GetBindings(snapshot.Objects[0].Id); bindings[0].Exports[0] = new("Broken", ExportKind.Boolean, 99);
        Check(before.SequenceEqual(doc.CaptureBytes()));
    }),
    ("Deleted UUID metadata removed; external World writes observed", () => {
        var doc = Populated(); var obj = doc.World.GetObjects()[0]; ulong rev = doc.Revision;
        obj.Destroy(); Check(doc.Revision == rev + 1);
        var clone = new SceneDocument("Clone", Registry()); clone.RestoreBytes(doc.CaptureBytes());
        Check(clone.World.Count == 1);
    }),
    ("Bindings update revision without changing World handles", () => {
        var doc = Populated(); var obj = doc.World.GetObjects()[0]; ulong rev = doc.Revision; ulong wr = doc.World.Revision;
        var bindings = doc.GetBindings(obj.PersistentId); bindings[0] = bindings[0] with { Enabled = true };
        doc.SetBindings(obj.PersistentId, bindings);
        Check(doc.Revision == rev + 1 && doc.World.Revision == wr && obj.Name == "Logic");
    }),
    ("Restore invalidates old references and increments once", () => {
        var doc = Populated(); var obj = doc.World.GetObjects()[0]; var id = obj.PersistentId; ulong rev = doc.Revision;
        doc.RestoreSnapshot(doc.CaptureSnapshot(), rev);
        Check(doc.Revision == rev + 1 && doc.World.FindObject(id).Name == "Logic"); Reject(() => _ = obj.Name);
    }),
    ("Expected revision rejects before mutation", () => {
        var doc = Populated(); byte[] before = doc.CaptureBytes();
        Reject(() => doc.RestoreSnapshot(doc.CaptureSnapshot(), doc.Revision + 1)); Check(before.SequenceEqual(doc.CaptureBytes()));
    }),
    ("Duplicate object/behaviour UUIDs rejected atomically", () => {
        var doc = Populated(); var s = doc.CaptureSnapshot();
        AtomicReject(doc, s with { Objects = [s.Objects[0], s.Objects[0]] });
        var item = s.Objects[0] with { Behaviours = [s.Objects[0].Behaviours[0], s.Objects[0].Behaviours[0]] };
        AtomicReject(doc, s with { Objects = [item] });
    }),
    ("Unknown and duplicate components rejected atomically", () => {
        var doc = Populated(); var s = doc.CaptureSnapshot(); var item = s.Objects[0];
        var c = item.Components[0];
        foreach (var invalid in new[] { c with { TypeId = "not.registered" }, c with { Version = 999 } })
            AtomicReject(doc, s with { Objects = [item with { Components = [invalid] }] });
        AtomicReject(doc, s with { Objects = [item with { Components = [c, c] }] });
    }),
    ("Invalid component data rejected atomically", () => {
        var doc = Populated(); var s = doc.CaptureSnapshot(); var c = s.Objects[0].Components[0];
        var bad = c with { Data = JsonSerializer.SerializeToElement(new { points = -1 }) };
        AtomicReject(doc, s with { Objects = [s.Objects[0] with { Components = [bad] }] });
    }),
    ("Invalid Export types/values/names rejected atomically", () => {
        var doc = Populated(); var s = doc.CaptureSnapshot(); var item = s.Objects[0]; var binding = item.Behaviours[0];
        foreach (var bad in new[] { new ExportData("X", ExportKind.Boolean, 2), new("X", ExportKind.Integer, 1.5),
            new("X", ExportKind.Float, double.MaxValue), new("X", (ExportKind)99, 0), new("", ExportKind.Double, 0) })
            AtomicReject(doc, s with { Objects = [item with { Behaviours = [binding with { Exports = [bad] }] }] });
        AtomicReject(doc, s with { Objects = [item with { Behaviours = [binding with { Exports = [binding.Exports[0], binding.Exports[0]] }] }] });
    }),
    ("Malformed/duplicate/unknown/missing JSON fields rejected", () => {
        var doc = Populated(); byte[] before = doc.CaptureBytes();
        foreach (var json in new[] { "{", "{\"version\":1,\"version\":1,\"name\":\"A\",\"objects\":[]}",
            "{\"version\":1,\"name\":\"A\",\"objects\":[],\"language\":0}", "{\"version\":1,\"objects\":[]}" })
            Reject(() => doc.RestoreBytes(Encoding.UTF8.GetBytes(json)));
        Check(before.SequenceEqual(doc.CaptureBytes()));
        Reject(() => doc.RestoreBytes(before.AsSpan(0, before.Length - 1)));
    }),
    ("Removed scene snapshots are rejected without compatibility conversion", () => {
        var doc = Populated(); byte[] before = doc.CaptureBytes();
        ulong revision = doc.Revision; Guid identity = doc.World.Identity;
        var live = doc.World.GetObjects()[0];
        string id = live.PersistentId.ToString();
        var payloads = new[] {
            """{"version":6,"name":"Legacy","objects":[]}""",
            """{"version":1,"name":"Legacy","objects":[{"id":"OBJECT_ID","name":"Logic","language":0,"hasTransform":false,"transform":{},"bindings":[]}]}""".Replace("OBJECT_ID", id),
            """{"version":1,"name":"Legacy","objects":[{"id":"OBJECT_ID","name":"Logic","logicLanguage":0,"localTransform":{},"behaviours":[]}]}""".Replace("OBJECT_ID", id)
        };
        foreach (string payload in payloads) {
            Reject(() => doc.RestoreBytes(Encoding.UTF8.GetBytes(payload)));
            Check(before.SequenceEqual(doc.CaptureBytes()) && revision == doc.Revision && identity == doc.World.Identity);
            Check(live.Name == "Logic"); // Failed legacy input does not invalidate live references.
        }
        var assembly = typeof(SceneDocument).Assembly;
        Check(assembly.GetType("Ncma.Scene.SceneSnapshotCodec") is null);
        Check(assembly.GetType("Ncma.Scene.SceneSnapshot") is null);
        Check(assembly.GetType("Ncma.Scene.LegacySceneAdapter") is null);
        Check(assembly.GetType("Ncma.Scene.SceneDocumentCodec") is not null);
    }),
    ("Snapshot bounds and version checks", () => {
        var doc = Populated(); AtomicReject(doc, doc.CaptureSnapshot() with { Version = 99 });
        Reject(() => doc.RestoreBytes(new byte[SceneDocumentCodec.MaxBytes + 1]));
        var s = doc.CaptureSnapshot();
        AtomicReject(doc, s with { Objects = Enumerable.Repeat(s.Objects[0], World.MaxObjects + 1).ToArray() });
    }),
    ("Bounded encoder rejects total size and oversized bindings before commit", () => {
        var doc = new SceneDocument(); var a = doc.World.CreateObject("A"); var b = doc.World.CreateObject("B");
        var properties = Enumerable.Range(0, 1024).Select(i => new ExportData(i.ToString() + new string('x', 120), ExportKind.Double, 1)).ToArray();
        var values = Enumerable.Range(0, 12).Select(_ => new BehaviourBindingData(Guid.NewGuid(), "Type", false, properties)).ToArray();
        doc.SetBindings(a.PersistentId, values);
        byte[] before = doc.CaptureBytes(); ulong rev = doc.Revision;
        var more = Enumerable.Range(0, 24).Select(_ => new BehaviourBindingData(Guid.NewGuid(), "Type", false, properties)).ToArray();
        Reject(() => doc.SetBindings(b.PersistentId, more));
        Check(before.SequenceEqual(doc.CaptureBytes()) && rev == doc.Revision);
    }),
    ("Validator-normalized payload and total document size stay bounded", () => {
        var registry = ComponentRegistry.CreateDefault();
        registry.Register<Label>("game.label", 1, """{"type":"object","additionalProperties":false,"required":["value"],"properties":{"value":{"type":"string"}}}""",
            _ => new Label(new string('x', 50000)));
        var doc = new SceneDocument("Bounded", registry);
        var properties = Enumerable.Range(0, 1024).Select(i => new ExportData(i.ToString() + new string('x', 120), ExportKind.Double, 1)).ToArray();
        var bindings = Enumerable.Range(0, 12).Select(_ => new BehaviourBindingData(Guid.NewGuid(), "Type", false, properties)).ToArray();
        var objects = Enumerable.Range(0, 70).Select(i => new SceneObjectData(Guid.NewGuid(), "A",
            [new ComponentSnapshot("game.label", 1, JsonSerializer.SerializeToElement(new { value = "small" }))],
            i == 0 ? bindings : [])).ToArray();
        AtomicReject(doc, new(1, "Bounded", objects)); // World alone fits, combined normalized document does not.
        var hugeRegistry = ComponentRegistry.CreateDefault();
        hugeRegistry.Register<Label>("game.label", 1, """{"type":"object","additionalProperties":false,"required":["value"],"properties":{"value":{"type":"string"}}}""",
            _ => new Label(new string('x', 70000)));
        var huge = new SceneDocument("Huge", hugeRegistry);
        AtomicReject(huge, new(1, "Huge", [objects[1]]));
    }),
    ("Bindings validation cannot partially mutate", () => {
        var doc = Populated(); byte[] before = doc.CaptureBytes(); ulong rev = doc.Revision; var s = doc.CaptureSnapshot();
        Reject(() => doc.SetBindings(s.Objects[0].Id, [s.Objects[0].Behaviours[0], s.Objects[0].Behaviours[0]]));
        Check(before.SequenceEqual(doc.CaptureBytes()) && rev == doc.Revision);
    }),
    ("Owner-thread access rejected", () => {
        var doc = Populated();
        Check(Task.Run(() => { try { _ = doc.CaptureBytes(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult());
    }),
    ("Restore validator cannot mutate World or document", () => {
        SceneDocument? doc = null; bool armed = false; int blocked = 0;
        var registry = ComponentRegistry.CreateDefault();
        registry.Register<Health>("game.health", 1, """{"type":"object","additionalProperties":false,"required":["points"],"properties":{"points":{"type":"integer"}}}""", value => {
            if (armed) {
                try { doc!.World.CreateObject("Reentrant"); } catch (InvalidOperationException) { blocked++; }
                try { doc!.SetBindings(doc.World.GetObjects()[0].PersistentId, []); } catch (InvalidOperationException) { blocked++; }
            }
            return value;
        });
        doc = new("Protected", registry); var obj = doc.World.CreateObject("Hero"); obj.Set(new Health(1));
        var snapshot = doc.CaptureSnapshot(); armed = true; doc.RestoreSnapshot(snapshot);
        Check(blocked == 2 && doc.World.Count == 1);
    }),
    ("Complete document assets preserve all registered components and bindings", () => {
        var doc = Populated(); byte[] before = doc.CaptureBytes();
        var path = AssetPath("Scene" + SceneDocumentFiles.Extension);
        SceneDocumentFiles.Save(doc, path);
        var clone = new SceneDocument("Clone", Registry()); SceneDocumentFiles.Load(clone, path);
        Check(before.SequenceEqual(File.ReadAllBytes(path)) && before.SequenceEqual(clone.CaptureBytes()));
        Check(clone.World.GetObjects()[0].Get<Health>().Points == 100 && !clone.World.GetObjects()[0].Has<TransformData>());
        clone.World.GetObjects()[0].Set(new Health(7)); SceneDocumentFiles.Save(clone, path);
        SceneDocumentFiles.Load(doc, path); Check(doc.World.GetObjects()[0].Get<Health>().Points == 7);
        Check(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp").Any());
    }),
    ("Unsupported text and document versions have no migration path", () => {
        var doc = Populated(); byte[] before = doc.CaptureBytes(); ulong revision = doc.Revision;
        var path = AssetPath("Unsupported.ncmascene");
        foreach (string data in new[] { "NCMA_SCENE 1\\n", "NCMA_SCENE 6\\n",
            """{"version":6,"name":"Old","objects":[]}""", "{", "" }) {
            File.WriteAllText(path, data, new UTF8Encoding(false));
            byte[] file = File.ReadAllBytes(path);
            Reject(() => SceneDocumentFiles.Load(doc, path));
            Reject(() => SceneDocumentFiles.Save(doc, path));
            Check(before.SequenceEqual(doc.CaptureBytes()) && revision == doc.Revision && file.SequenceEqual(File.ReadAllBytes(path)));
        }
    }),
    ("Only the new extension is accepted, even for valid JSON", () => {
        var doc = Populated();
        var path = AssetPath("Wrong.txt"); File.WriteAllBytes(path, doc.CaptureBytes());
        var oldPath = AssetPath("Removed.ncscene");
        try { SceneDocumentFiles.Load(doc, oldPath); throw new Exception("Old extension accepted."); }
        catch (ArgumentException) { }
        Reject(() => SceneDocumentFiles.Save(doc, oldPath)); Check(!File.Exists(oldPath));
        Reject(() => SceneDocumentFiles.Load(doc, path)); Reject(() => SceneDocumentFiles.Save(doc, path));
        Check(typeof(SceneDocument).Assembly.GetType("Ncma.Scene.LegacySceneAdapter") is null);
        var upper = AssetPath("Scene.NCMASCENE"); SceneDocumentFiles.Save(doc, upper); SceneDocumentFiles.Load(doc, upper);
    }),
    ("Oversized and unknown-component assets cannot partially load", () => {
        var doc = Populated(); byte[] before = doc.CaptureBytes(); ulong revision = doc.Revision;
        var path = AssetPath("Invalid.ncmascene");
        File.WriteAllBytes(path, new byte[SceneDocumentCodec.MaxBytes + 1]);
        Reject(() => SceneDocumentFiles.Load(doc, path));
        var snapshot = doc.CaptureSnapshot();
        snapshot.Objects[0].Components[0] = snapshot.Objects[0].Components[0] with { TypeId = "missing.component" };
        File.WriteAllBytes(path, SceneDocumentCodec.Encode(snapshot)); Reject(() => SceneDocumentFiles.Load(doc, path));
        Check(before.SequenceEqual(doc.CaptureBytes()) && revision == doc.Revision);
    }),
    ("Save failure preserves target and leaves no temporary files", () => {
        var doc = Populated(); var path = AssetPath("Locked.ncmascene"); SceneDocumentFiles.Save(doc, path);
        byte[] before = File.ReadAllBytes(path); doc.World.GetObjects()[0].Set(new Health(2));
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            Reject(() => SceneDocumentFiles.Save(doc, path));
        }
        Check(before.SequenceEqual(File.ReadAllBytes(path)));
        Check(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp").Any());
    }),
    ("Complete snapshot cloning keeps edit/play isolated", () => {
        var edit = Populated(); byte[] before = edit.CaptureBytes(); var play = new SceneDocument("Play", Registry());
        play.RestoreBytes(before); play.World.GetObjects()[0].Set(new Health(2));
        var id = play.World.GetObjects()[0].PersistentId; play.SetBindings(id, []);
        Check(before.SequenceEqual(edit.CaptureBytes()) && edit.World.GetObjects()[0].Get<Health>().Points == 100);
    })
};
int failures = 0;
var allCases=cases.Concat(PrefabTests.Cases()).ToArray();
foreach (var (name, run) in allCases) {
    try { run(); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + e); }
}
Console.WriteLine($"Scene document: {allCases.Length - failures}/{allCases.Length} passed.");
return failures == 0 ? 0 : 1;
readonly record struct Health(int Points) : IComponent;
readonly record struct Label(string Value) : IComponent;
