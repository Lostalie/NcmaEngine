using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Ncma.Runtime;

static void Check(bool valid, string message = "Assertion failed") { if (!valid) throw new Exception(message); }
static void Reject(Action action)
{
    bool failed = false;
    try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or JsonException) { failed = true; }
    Check(failed, "Invalid operation was accepted");
}
static JsonElement Json(string text) { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }
static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value);
static CapabilityRequest Request(EditSession session, string capability, JsonElement input) => new(1, Guid.NewGuid(), session.SessionId, session.Revision, capability, input);
static CapabilityPermissions EditPermissions() => new(["ncma.scene.transaction", "ncma.history.undo", "ncma.history.redo"]);
static JsonElement Create(Guid id, string name = "Hero") => Element(new { operations = new[] { new { op = "create", objectId = id, name } } });
static JsonElement Rename(Guid id, string name) => Element(new { operations = new[] { new { op = "rename", objectId = id, name } } });
static ComponentRegistry Extensions()
{
    var registry = ComponentRegistry.CreateDefault();
    registry.Register<Health>("game.health", 1, """
        {"type":"object","additionalProperties":false,"required":["points"],"properties":{"points":{"type":"integer"}}}
        """, value => value.Points >= 0 ? value : throw new ArgumentException("Health must be nonnegative."));
    return registry;
}

var cases = new (string Name, Action Run)[]
{
    ("No native/Python dependency", () => {
        var assembly = typeof(World).Assembly;
        Check(!assembly.GetReferencedAssemblies().Any(a => a.Name!.StartsWith("Ncma.Managed") || a.Name.Contains("Python")));
        Check(!assembly.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            .Any(m => m.GetCustomAttribute<DllImportAttribute>() is not null));
    }),
    ("Empty flat objects and optional Transform", () => {
        var world = new World(); var a = world.CreateObject("Logic"); var b = world.CreateObject("Other");
        Check(!a.Has<TransformData>() && world.Count == 2);
        a.Set(TransformData.Identity); Check(a.Get<TransformData>() == TransformData.Identity);
        Check(a.Remove<TransformData>() && !a.Has<TransformData>());
        a.Destroy(); Check(world.Count == 1 && b.Name == "Other"); Reject(() => _ = a.Name);
        Check(typeof(GameObject).GetProperty("Parent") is null && typeof(GameObject).GetProperty("LogicLanguage") is null);
    }),
    ("UUIDs and duplicate rejection", () => {
        var world = new World(); Guid id = Guid.NewGuid(); var a = world.CreateObject("A", id);
        Check(world.FindObject(id).Reference == a.Reference);
        Reject(() => world.CreateObject("Duplicate", id)); Reject(() => world.CreateObject("Empty", Guid.Empty));
        Check(world.Count == 1);
    }),
    ("Cross-world and owner-thread safety", () => {
        var world = new World(); var other = new World(); var a = world.CreateObject("A");
        Reject(() => other.FindObject(a.PersistentId));
        Check(Task.Run(() => { try { _ = world.Count; return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult());
    }),
    ("Transform validation is atomic", () => {
        var world = new World(); var a = world.CreateObject("A"); a.Set(TransformData.Identity); ulong revision = world.Revision;
        Reject(() => a.Set(TransformData.Identity with { Position = new(float.NaN, 0, 0) }));
        Reject(() => a.Set(TransformData.Identity with { Rotation = new(0, 0, 0, 0) }));
        Check(world.Revision == revision && a.Get<TransformData>() == TransformData.Identity);
        a.Set(TransformData.Identity with { Rotation = new(0, 0, 0, 2) }); Check(a.Get<TransformData>().Rotation == Quaternion.Identity);
    }),
    ("Registered extensions and frozen metadata", () => {
        var registry = Extensions(); var world = new World(components: registry); var a = world.CreateObject("A");
        a.Set(new Health(100)); Check(a.Get<Health>().Points == 100);
        Reject(() => a.Set(new Health(-1))); Check(a.Get<Health>().Points == 100);
        Reject(() => registry.Register<Health>("new.health", 1, "{}", v => v));
        var session = new EditSession(world); var result = session.Invoke(Request(session, "ncma.engine.component_types", Json("{}")));
        Check(result.Status == "ok" && result.Data.GetProperty("components").GetArrayLength() == 2);
        Guid id = a.PersistentId;
        var input = Element(new { operations = new[] { new { op = "set_component", objectId = id, typeId = "game.health", version = 1, data = Json("{\"points\":42}") } } });
        Check(session.Invoke(Request(session, "ncma.scene.transaction", input), EditPermissions()).Status == "ok");
        Check(world.FindObject(id).Get<Health>().Points == 42);
    }),
    ("Mutable reference component rejected", () => {
        var registry = ComponentRegistry.CreateDefault();
        Reject(() => registry.Register<BadComponent>("bad", 1, "{}", value => value));
        Reject(() => registry.Register<BadHandle>("bad.handle", 1, "{}", value => value));
    }),
    ("Managed snapshot round-trip and stale handles", () => {
        var world = new World(components: Extensions()); var a = world.CreateObject("A"); Guid id = a.PersistentId;
        a.Set(new Health(25)); a.Set(TransformData.Identity); string text = world.SerializeSnapshot();
        Check(!text.Contains("worldId") && !text.Contains("reference"));
        world.LoadSnapshot(text); Reject(() => _ = a.Name);
        Check(world.FindObject(id).Get<Health>().Points == 25 && world.SerializeSnapshot() == text);
    }),
    ("Unknown/malformed snapshots preserve World", () => {
        var world = new World(); world.CreateObject("A"); string before = world.SerializeSnapshot(); ulong revision = world.Revision;
        Reject(() => world.LoadSnapshot(before.Replace("\"version\":1", "\"version\":99")));
        Reject(() => world.LoadSnapshot(before.Replace("\"objects\":", "\"python\":true,\"objects\":")));
        Reject(() => world.LoadSnapshot(before.Replace("\"version\":1", "\"version\":99,\"version\":1")));
        var snapshot = world.CaptureSnapshot();
        var malformed = snapshot with { Objects = [snapshot.Objects[0] with { Components = [new("python.behaviour", 1, Json("{}"))] }] };
        Reject(() => world.RestoreSnapshot(malformed));
        Check(world.Revision == revision && world.SerializeSnapshot() == before);
    }),
    ("Fixed-step scheduling and committed reads", () => {
        var world = new World(); Guid id = world.CreateObject("A").PersistentId; world.FindObject(id).Set(TransformData.Identity);
        var runner = new WorldRunner(world, 0.1); int observed = 0;
        runner.AddSystem(new TestSystem((w, dt) => { Check(dt == 0.1); var a = w.FindObject(id); a.Set(a.Get<TransformData>() with { Position = new(a.Get<TransformData>().Position.X + 1, 0, 0) }); }));
        runner.AddSystem(new TestSystem((w, _) => { Check(w.FindObject(id).Get<TransformData>().Position.X == observed++); }));
        Check(runner.Advance(0.05) == 0 && world.Tick == 0);
        Check(runner.Advance(0.15) == 2 && world.Tick == 2 && world.FindObject(id).Get<TransformData>().Position.X == 2);
        Reject(() => runner.Advance(double.NaN)); Reject(() => runner.Advance(2));
        Check(world.Tick == 2 && !runner.IsFaulted);
    }),
    ("Fixed-step failure aborts writes and requires recovery", () => {
        var world = new World(); var a = world.CreateObject("A"); a.Set(TransformData.Identity); ulong revision = world.Revision;
        var runner = new WorldRunner(world, 0.1);
        runner.AddSystem(new TestSystem((_, _) => { a.Set(TransformData.Identity with { Position = Vector3.One }); a.Name = "Forbidden"; }));
        Reject(() => runner.Advance(0.1));
        Check(runner.IsFaulted && world.Tick == 0 && world.Revision == revision && a.Get<TransformData>() == TransformData.Identity && a.Name == "A");
        Reject(() => runner.Advance(0)); runner.ResetFault(); Check(!runner.IsFaulted);
    }),
    ("Capability schemas and read-only default", () => {
        var world = new World(); var session = new EditSession(world); Check(session.Describe().Count == 8);
        foreach (var descriptor in session.Describe()) { Check(descriptor.InputSchema.ValueKind == JsonValueKind.Object); Check(descriptor.OutputSchema.GetProperty("required").GetArrayLength() == 8); }
        var inspect = session.Invoke(Request(session, "ncma.scene.inspect", Json("{}")));
        Check(inspect.Status == "ok" && !inspect.Changed && inspect.Data.GetProperty("scene").GetProperty("objects").GetArrayLength() == 0);
        Check(session.Invoke(Request(session, "ncma.scene.transaction", Create(Guid.NewGuid()))).Status == "denied" && world.Count == 0);
        Check(session.Invoke(Request(session, "ncma.scene.validate", Json("{}"))).Data.GetProperty("valid").GetBoolean());
    }),
    ("Atomic create/set/rename and one revision", () => {
        var world = new World(); var session = new EditSession(world); Guid id = Guid.NewGuid();
        var input = Element(new { operations = new object[] { new { op = "create", objectId = id, name = "A" },
            new { op = "set_component", objectId = id, typeId = "ncma.transform", version = 1, data = world.Components.Encode(TransformData.Identity) },
            new { op = "rename", objectId = id, name = "Hero" } } });
        var result = session.Invoke(Request(session, "ncma.scene.transaction", input), EditPermissions());
        Check(result.Status == "ok" && result.Changed && result.Revision == 1 && world.FindObject(id).Name == "Hero");
    }),
    ("Late transaction failure is atomic", () => {
        var world = new World(); var session = new EditSession(world); Guid id = Guid.NewGuid();
        var input = Element(new { operations = new object[] { new { op = "create", objectId = id, name = "A" }, new { op = "rename", objectId = Guid.NewGuid(), name = "Missing" } } });
        Check(session.Invoke(Request(session, "ncma.scene.transaction", input), EditPermissions()).Status == "error");
        Check(world.Count == 0 && session.Revision == 0);
        Check(session.Invoke(Request(session, "ncma.history.undo", Json("{}")), EditPermissions()).Code == "history_empty");
    }),
    ("Request identity/revision guards and idempotency", () => {
        var world = new World(); var session = new EditSession(world); Guid id = Guid.NewGuid();
        var request = Request(session, "ncma.scene.transaction", Create(id));
        Check(session.Invoke(request with { SessionId = Guid.NewGuid() }, EditPermissions()).Code == "invalid_envelope");
        Check(session.Invoke(request with { ExpectedRevision = null }, EditPermissions()).Code == "revision_conflict");
        var first = session.Invoke(request, EditPermissions()); var second = session.Invoke(request, EditPermissions());
        Check(first == second && world.Count == 1 && world.Revision == 1);
        Check(session.Invoke(request with { Input = Create(Guid.NewGuid()) }, EditPermissions()).Code == "request_id_reused");
        Check(session.Invoke(Request(session, "ncma.scene.transaction", Rename(id, "B")) with { ExpectedRevision = 0 }, EditPermissions()).Code == "revision_conflict");
        Check(session.Invoke(request).Status == "denied");
    }),
    ("Undo/redo and redo branch invalidation", () => {
        var world = new World(); var session = new EditSession(world); Guid id = Guid.NewGuid(); var permissions = EditPermissions();
        Check(session.Invoke(Request(session, "ncma.scene.transaction", Create(id)), permissions).Status == "ok");
        Check(session.Invoke(Request(session, "ncma.history.undo", Json("{}")), permissions).Status == "ok" && world.Count == 0);
        Check(session.Invoke(Request(session, "ncma.history.redo", Json("{}")), permissions).Status == "ok" && world.Count == 1);
        Check(session.Invoke(Request(session, "ncma.scene.transaction", Rename(id, "Old")), permissions).Status == "ok");
        Check(session.Invoke(Request(session, "ncma.history.undo", Json("{}")), permissions).Status == "ok");
        Check(session.Invoke(Request(session, "ncma.scene.transaction", Rename(id, "New")), permissions).Status == "ok");
        Check(session.Invoke(Request(session, "ncma.history.redo", Json("{}")), permissions).Code == "history_empty" && world.FindObject(id).Name == "New");
    }),
    ("Destructive UUID authorization and redo permission", () => {
        var world = new World(); Guid id = world.CreateObject("A").PersistentId; var session = new EditSession(world);
        var limited = new CapabilityPermissions(["ncma.scene.delete_object", "ncma.history.undo", "ncma.history.redo"]);
        var approved = new CapabilityPermissions(["ncma.scene.delete_object", "ncma.history.undo", "ncma.history.redo"], [id]);
        var request = Request(session, "ncma.scene.delete_object", Element(new { objectId = id }));
        Check(session.Invoke(request, limited).Code == "target_not_authorized" && world.Count == 1);
        Check(session.Invoke(request, approved).Status == "ok" && world.Count == 0);
        Check(session.Invoke(Request(session, "ncma.history.undo", Json("{}")), approved).Status == "ok" && world.Count == 1);
        Check(session.Invoke(Request(session, "ncma.history.redo", Json("{}")), limited).Status == "denied" && world.Count == 1);
    }),
    ("No deletion/eval bypass inside transaction", () => {
        var world = new World(); Guid id = world.CreateObject("A").PersistentId; var session = new EditSession(world); ulong revision = world.Revision;
        var input = Element(new { operations = new[] { new { op = "delete", objectId = id } } });
        Check(session.Invoke(Request(session, "ncma.scene.transaction", input), EditPermissions()).Status == "error");
        Check(session.Invoke(Request(session, "ncma.python.exec", Json("{}")), EditPermissions()).Code == "unknown_capability");
        Check(world.Count == 1 && world.Revision == revision);
    }),
    ("Closed inputs/schema/size rejection", () => {
        var world = new World(); Guid id = world.CreateObject("A").PersistentId; var session = new EditSession(world); var permissions = EditPermissions();
        Check(session.Invoke(Request(session, "ncma.scene.inspect", Json("{\"permission\":true}"))).Status == "error");
        Check(session.Invoke(Request(session, "ncma.scene.transaction", Json("{\"operations\":[],\"operations\":[]}")), permissions).Status == "error");
        Check(session.Invoke(Request(session, "ncma.scene.transaction", Element(new { padding = new string('x', EditSession.MaxInputBytes) })), permissions).Status == "error");
        var bad = Element(new { operations = new[] { new { op = "set_component", objectId = id, typeId = "ncma.transform", version = 1, data = Json("{\"position\":{},\"rotation\":{},\"scale\":{}}") } } });
        Check(session.Invoke(Request(session, "ncma.scene.transaction", bad), permissions).Status == "error" && !world.FindObject(id).Has<TransformData>());
    }),
    ("History cannot overwrite outside edits", () => {
        var world = new World(); var session = new EditSession(world); var permissions = EditPermissions();
        Check(session.Invoke(Request(session, "ncma.scene.transaction", Create(Guid.NewGuid())), permissions).Status == "ok");
        world.CreateObject("Outside");
        Check(session.Invoke(Request(session, "ncma.history.undo", Json("{}")), permissions).Code == "revision_conflict" && world.Count == 2);
    }),
    ("Bounded history", () => {
        var world = new World(); var session = new EditSession(world); var permissions = EditPermissions();
        for (int i = 0; i < 70; ++i) Check(session.Invoke(Request(session, "ncma.scene.transaction", Create(Guid.NewGuid())), permissions).Status == "ok");
        for (int i = 0; i < EditSession.MaxHistoryEntries; ++i) Check(session.Invoke(Request(session, "ncma.history.undo", Json("{}")), permissions).Status == "ok");
        Check(world.Count == 6 && session.Invoke(Request(session, "ncma.history.undo", Json("{}")), permissions).Code == "history_empty");
    }),
    ("Malformed numeric/deep input produces structured errors", () => {
        var world = new World(); var session = new EditSession(world); var permissions = EditPermissions();
        var input = Element(new { operations = new[] { new { op = "set_component", objectId = Guid.NewGuid(), typeId = "ncma.transform", version = 1e30, data = Json("{}") } } });
        Check(session.Invoke(Request(session, "ncma.scene.transaction", input), permissions).Status == "error");
        string deep = string.Concat(Enumerable.Repeat("{\"nested\":", 40)) + "{}" + new string('}', 40);
        Check(session.Invoke(Request(session, "ncma.scene.inspect", Json(deep))).Code == "invalid_input");
        Check(world.Count == 0 && world.Revision == 0);
    }),
    ("Cyclic trusted schema fails safely", () => {
        var registry = new ComponentRegistry();
        registry.Register<Health>("cyclic", 1, """{"$ref":"#/$defs/loop","$defs":{"loop":{"$ref":"#/$defs/loop"}}}""", v => v);
        var world = new World(components: registry); var session = new EditSession(world); var permissions = EditPermissions();
        Guid id = Guid.NewGuid();
        var input = Element(new { operations = new object[] { new { op = "create", objectId = id, name = "A" },
            new { op = "set_component", objectId = id, typeId = "cyclic", version = 1, data = Json("{}") } } });
        Check(session.Invoke(Request(session, "ncma.scene.transaction", input), permissions).Code == "invalid_input" && world.Count == 0);
    }),
    ("Extension validator failure does not partially commit", () => {
        var registry = new ComponentRegistry();
        registry.Register<Health>("fault", 1, """{"type":"object","required":["points"],"properties":{"points":{"type":"integer"}}}""", _ => throw new ApplicationException("Validator failed"));
        var world = new World(components: registry); var session = new EditSession(world); Guid id = Guid.NewGuid();
        var input = Element(new { operations = new object[] { new { op = "create", objectId = id, name = "A" },
            new { op = "set_component", objectId = id, typeId = "fault", version = 1, data = Json("{\"points\":1}") } } });
        Check(session.Invoke(Request(session, "ncma.scene.transaction", input), EditPermissions()).Code == "operation_failed" && world.Count == 0);
    }),
    ("Fixed-step budget/reentrancy/structure cannot bypass guards", () => {
        var world = new World(); var runner = new WorldRunner(world, 0.1, 2); Guid id = world.CreateObject("A").PersistentId;
        world.FindObject(id).Set(TransformData.Identity);
        runner.AddSystem(new TestSystem((w, _) => {
            Reject(() => w.CreateObject("Forbidden")); Reject(() => w.FindObject(id).Destroy());
            Reject(() => w.FindObject(id).Remove<TransformData>());
            Reject(() => runner.Advance(0)); Reject(() => runner.AddSystem(new TestSystem((_, _) => { })));
            w.FindObject(id).Set(TransformData.Identity with { Position = new(1, 0, 0) });
            w.FindObject(id).Set(TransformData.Identity with { Position = new(2, 0, 0) });
        }));
        Reject(() => runner.Advance(0.3)); Check(world.Tick == 0 && !runner.IsFaulted);
        Check(runner.Advance(0.1) == 1 && world.FindObject(id).Get<TransformData>().Position.X == 2);
    }),
    ("Impure trusted validator cannot overwrite outside mutations", () => {
        World? live = null; bool mutate = false;
        var registry = new ComponentRegistry();
        registry.Register<Health>("health", 1, """{"type":"object","required":["points"],"properties":{"points":{"type":"integer"}}}""", value => {
            if (mutate) { mutate = false; live!.CreateObject("Outside"); }
            return value;
        });
        live = new World(components: registry); var a = live.CreateObject("A"); a.Set(new Health(1));
        var snapshot = live.CaptureSnapshot(); mutate = true;
        Reject(() => live.RestoreSnapshot(snapshot));
        Check(live.Count == 2 && a.Get<Health>().Points == 1);
        var session = new EditSession(live); Guid id = Guid.NewGuid(); mutate = true;
        var input = Element(new { operations = new object[] { new { op = "create", objectId = id, name = "Candidate" },
            new { op = "set_component", objectId = id, typeId = "health", version = 1, data = Json("{\"points\":2}") } } });
        Check(session.Invoke(Request(session, "ncma.scene.transaction", input), EditPermissions()).Code == "revision_conflict");
        Check(live.Count == 3); Reject(() => live.FindObject(id));
    }),
};

int failures = 0;
foreach (var test in cases)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { ++failures; Console.Error.WriteLine("FAIL " + test.Name + ": " + error); }
}
Console.WriteLine($"Ncma managed headless: {cases.Length - failures}/{cases.Length} passed (no native DLL/Python).");
return failures == 0 ? 0 : 1;

readonly record struct Health(int Points) : IComponent;
readonly record struct BadComponent(List<int> Values) : IComponent;
readonly record struct BadHandle(nint Address) : IComponent;
sealed class TestSystem(Action<World, double> update) : IWorldSystem
{
    public void FixedUpdate(World world, double fixedDeltaSeconds) => update(world, fixedDeltaSeconds);
}
