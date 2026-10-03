using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Editor.Core;

static void Check(bool valid, string message = "Assertion failed") { if (!valid) throw new Exception(message); }
static void Reject(Action action)
{
    bool failed = false;
    try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or JsonException) { failed = true; }
    Check(failed, "Invalid operation was accepted");
}
static JsonElement Json(string text) { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }
static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value);
static CapabilityRequest Request(EditSession session, string capability, JsonElement input) => new(EditSession.ContractVersion, Guid.NewGuid(), session.SessionId, session.Revision, capability, input);
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

static string TestFolder()
{
    var root = new DirectoryInfo(AppContext.BaseDirectory);
    while (root is not null && !File.Exists(Path.Combine(root.FullName, "Build.bat"))) root = root.Parent;
    if (root is null) throw new Exception("Repository root not found.");
    string folder = Path.Combine(root.FullName, "out", "tests", "editor-core", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder); return folder;
}

var cases = new (string Name, Action Run)[]
{
    ("Scoped multi-target transactions selection history and cached retries cannot escape", () => {
        var doc = new SceneDocument(); var a = doc.World.CreateObject("A"); var b = doc.World.CreateObject("B"); Guid aId = a.PersistentId, bId = b.PersistentId;
        var session = new EditSession(doc); var scope = new CapabilityPermissions(["ncma.scene.transaction","ncma.history.undo","ncma.history.redo"], objectScope: [aId], createScope: [], allowDocumentHistory: false);
        var multiple = Element(new { operations = new object[] { new { op = "rename", objectId = aId, name = "Good" }, new { op = "rename", objectId = bId, name = "Bad" } } });
        var before = doc.CaptureBytes(); Check(session.Invoke(Request(session, "ncma.scene.transaction", multiple), scope).Code == "scope_denied" && doc.CaptureBytes().SequenceEqual(before));
        var request = Request(session, "ncma.scene.transaction", Rename(aId, "Allowed")); Check(session.Invoke(request, scope).Changed);
        var other = new CapabilityPermissions(["ncma.scene.transaction"], objectScope: [bId], allowDocumentHistory: false);
        Check(session.Invoke(request, other).Status == "denied");
        Check(session.Invoke(Request(session, "ncma.scene.transaction", Rename(bId, "User")), EditPermissions()).Changed);
        Check(session.Invoke(Request(session, "ncma.history.undo", Json("{}")), scope).Code == "history_scope_denied");
        Check(doc.World.FindObject(bId).Name == "User");
        var newId = Guid.NewGuid(); Check(session.Invoke(Request(session, "ncma.scene.transaction", Create(newId)), scope).Code == "scope_denied");
        var select = Element(new { operations = new[] { new { op = "rename", objectId = aId, name = "Selection" } }, selection = bId });
        Check(session.Invoke(Request(session, "ncma.scene.transaction", select), scope).Code == "scope_denied");
        var noChange = Request(session, "ncma.scene.transaction", Rename(aId, "Allowed")); Check(session.Invoke(noChange, scope).Code == "no_change");
        Check(session.Invoke(noChange, other).Status == "denied");
    }),
    ("Document and file replacement history is never an Agent privilege", () => {
        var doc = new SceneDocument(); var obj = doc.World.CreateObject("A"); var session = new EditSession(doc);
        ulong generation = session.DocumentGeneration; Check(session.NewDocument(session.Revision, EditPermissions()).Changed);
        Check(session.DocumentGeneration > generation);
        var agent = new CapabilityPermissions(["ncma.scene.transaction", "ncma.history.undo"], allowDocumentHistory: false);
        var before = doc.CaptureBytes(); Check(session.Invoke(Request(session, "ncma.history.undo", Json("{}")), agent).Code == "history_scope_denied");
        Check(doc.CaptureBytes().SequenceEqual(before)); Check(session.Invoke(Request(session, "ncma.history.undo", Json("{}")), EditPermissions()).Changed);
    }),
    ("Final installation rechecks revocation after trusted component validation", () => {
        bool active = true, revoke = false;
        var registry = ComponentRegistry.CreateDefault(); registry.Register<Health>("game.health", 1,
            """{"type":"object","additionalProperties":false,"required":["points"],"properties":{"points":{"type":"integer"}}}""", h => { if (revoke) active = false; return h; });
        var doc = new SceneDocument(components: registry); var obj = doc.World.CreateObject("A"); var id = obj.PersistentId; obj.Set(new Health(1));
        var session = new EditSession(doc); var permission = new CapabilityPermissions(["ncma.scene.transaction"], objectScope: [id], isCurrent: () => active);
        var bytes = doc.CaptureBytes(); var request = Request(session, "ncma.scene.transaction", Element(new { operations = new[] { new { op = "set_component", objectId = id, typeId = "game.health", version = 1, data = new { points = 8 } } } }));
        revoke = true; var result = session.Invoke(request, permission); Check(!result.Changed && result.Code == "scope_denied" && doc.CaptureBytes().SequenceEqual(bytes) && session.State.UndoCount == 0);
    }),
    ("Binding deltas preserve unmentioned configuration and enforce trusted catalog and ranges", () => {
        var doc = new SceneDocument(); var obj = doc.World.CreateObject("A"); var id = obj.PersistentId; var first = Guid.NewGuid(); var second = Guid.NewGuid();
        doc.SetBindings(id, [new(first, "Test.Probe", true, [new("Speed", ExportKind.Float, 4), new("Health", ExportKind.Integer, 9)]),
            new(second, "Unresolved", false, [])]); var session = new EditSession(doc);
        session.SetBehaviourCatalog(new(Guid.NewGuid(), [new("Test.Probe", [new("Speed", 1, "Speed", "", 0), new("Health", 3, "Health", "", 0)])]));
        var scope = new CapabilityPermissions(["ncma.scene.transaction","ncma.history.undo","ncma.history.redo"], objectScope: [id], bindingScope: [first], requireTrustedBindings: true, allowDocumentHistory: false);
        var input = Element(new { operations = new[] { new { op = "set_export", objectId = id, bindingId = first, name = "Speed", kind = 1, value = 12 } } });
        Check(session.Invoke(Request(session, "ncma.scene.transaction", input), scope).Changed);
        Check(doc.GetBindings(id).Single(b => b.Id == first).Exports.Single(e => e.Name == "Health").Value == 9 && doc.GetBindings(id).Any(b => b.Id == second));
        Check(session.Invoke(Request(session, "ncma.history.undo", Json("{}")), scope).Changed && doc.GetBindings(id).Single(b => b.Id == first).Exports.Single(e => e.Name == "Speed").Value == 4);
        Check(session.Invoke(Request(session, "ncma.history.redo", Json("{}")), scope).Changed);
        var outside = Element(new { operations = new[] { new { op = "remove_binding", objectId = id, bindingId = second } } });
        Check(session.Invoke(Request(session, "ncma.scene.transaction", outside), scope).Code == "scope_denied");
        Check(session.Invoke(Request(session, "ncma.scene.transaction", outside), EditPermissions()).Changed && doc.GetBindings(id).Length == 1);
        Check(session.Invoke(Request(session, "ncma.scene.transaction", Element(new { operations = new[] { new { op = "set_export", objectId = id, bindingId = first, name = "Private", kind = 1, value = 1 } } })), scope).Code == "invalid_input");
    }),
    ("Detail queries return complete component binding and export data without runtime callbacks", () => {
        var doc = new SceneDocument(); var obj = doc.World.CreateObject("Detail"); obj.Set(TransformData.Identity);
        var binding = new BehaviourBindingData(Guid.NewGuid(), "Test.Probe", false, [new("Speed", ExportKind.Float, 7), new("Health", ExportKind.Integer, 8)]);
        doc.SetBindings(obj.PersistentId, [binding]); var session = new EditSession(doc); var bytes = doc.CaptureBytes(); var revision = session.Revision;
        var components = session.Invoke(Request(session, "ncma.scene.object.inspect", Element(new { objectId = obj.PersistentId, section = "components" })));
        Check(components.Status == "ok" && components.Data.GetProperty("items")[0].GetProperty("data").GetProperty("rotation").GetProperty("w").GetSingle() == 1);
        var bindings = session.Invoke(Request(session, "ncma.scene.object.inspect", Element(new { objectId = obj.PersistentId, section = "bindings" })));
        Check(bindings.Data.GetProperty("items")[0].GetProperty("exportCount").GetInt32() == 2);
        var exports = session.Invoke(Request(session, "ncma.scene.object.inspect", Element(new { objectId = obj.PersistentId, section = "exports", bindingId = binding.Id, limit = 1 })));
        Check(exports.Data.GetProperty("returnedCount").GetInt32() == 1 && !exports.Data.GetProperty("complete").GetBoolean() && exports.Data.GetProperty("nextOffset").GetInt32() == 1);
        Check(session.Invoke(Request(session, "ncma.scene.object.inspect", Element(new { objectId = Guid.NewGuid(), section = "summary" }))).Code == "object_not_found");
        Check(session.Invoke(Request(session, "ncma.scene.object.inspect", Element(new { objectId = obj.PersistentId, section = "summary", bindingId = binding.Id }))).Code == "invalid_input");
        Check(session.Invoke(Request(session, "ncma.engine.behaviour_types", Json("{}"))).Code == "catalog_unavailable");
        session.SetBehaviourCatalog(new(Guid.NewGuid(), [new("Test.Probe", [new("Speed", 1, "Speed", "Test", 7)])]));
        var catalog = session.Invoke(Request(session, "ncma.engine.behaviour_types", Json("{}"))); Check(catalog.Status == "ok" && catalog.Data.GetProperty("items")[0].GetProperty("exports")[0].GetProperty("kind").GetUInt32() == 1);
        Check(doc.CaptureBytes().SequenceEqual(bytes) && session.Revision == revision);
        foreach (var capability in session.Describe()) Check(capability.OutputSchema.GetProperty("properties").GetProperty("data").TryGetProperty("anyOf", out _));
    }),
    ("Detail pagination honors document revision and refuses oversized single catalog item", () => {
        var doc = new SceneDocument(); var obj = doc.World.CreateObject("Detail"); var session = new EditSession(doc);
        var request = Request(session, "ncma.scene.object.inspect", Element(new { objectId = obj.PersistentId, section = "summary", limit = 64 }));
        Check(session.Invoke(request).Status == "ok");
        Check(session.Invoke(Request(session, "ncma.scene.transaction", Rename(obj.PersistentId, "Changed")), EditPermissions()).Changed);
        Check(session.Invoke(request).Code == "revision_conflict");
        session.SetBehaviourCatalog(new(Guid.NewGuid(), [new("Test.Huge", [new("Value", 1, new string('x', 300000), "Test", 0)])]));
        Check(session.Invoke(Request(session, "ncma.engine.behaviour_types", Json("{}"))).Code == "item_too_large");
    }),
    ("Registered extensions and frozen metadata", () => {
        var registry = Extensions(); var document = new SceneDocument(components: registry); var world = document.World; var a = world.CreateObject("A");
        a.Set(new Health(100)); Check(a.Get<Health>().Points == 100);
        Reject(() => a.Set(new Health(-1))); Check(a.Get<Health>().Points == 100);
        Reject(() => registry.Register<Health>("new.health", 1, "{}", v => v));
        var session = new EditSession(document); var result = session.Invoke(Request(session, "ncma.engine.component_types", Json("{}")));
        Check(result.Status == "ok" && result.Data.GetProperty("components").GetArrayLength() == 2);
        Guid id = a.PersistentId;
        var input = Element(new { operations = new[] { new { op = "set_component", objectId = id, typeId = "game.health", version = 1, data = Json("{\"points\":42}") } } });
        Check(session.Invoke(Request(session, "ncma.scene.transaction", input), EditPermissions()).Status == "ok");
        Check(world.FindObject(id).Get<Health>().Points == 42);
    }),
    ("Capability schemas and read-only default", () => {
        var document = new SceneDocument(); var world = document.World; var session = new EditSession(document); Check(session.Describe().Count == 10);
        foreach (var descriptor in session.Describe()) { Check(descriptor.InputSchema.ValueKind == JsonValueKind.Object); Check(descriptor.OutputSchema.GetProperty("required").GetArrayLength() == 10); }
        var inspect = session.Invoke(Request(session, "ncma.scene.inspect", Json("{}")));
        Check(inspect.Status == "ok" && !inspect.Changed && inspect.Data.GetProperty("scene").GetProperty("objects").GetArrayLength() == 0);
        Check(session.Invoke(Request(session, "ncma.scene.transaction", Create(Guid.NewGuid()))).Status == "denied" && world.Count == 0);
        Check(session.Invoke(Request(session, "ncma.scene.validate", Json("{}"))).Data.GetProperty("valid").GetBoolean());
    }),
    ("Atomic create/set/rename and one revision", () => {
        var document = new SceneDocument(); var world = document.World; var session = new EditSession(document); Guid id = Guid.NewGuid();
        var input = Element(new { operations = new object[] { new { op = "create", objectId = id, name = "A" },
            new { op = "set_component", objectId = id, typeId = "ncma.transform", version = 1, data = world.Components.Encode(TransformData.Identity) },
            new { op = "rename", objectId = id, name = "Hero" } } });
        var result = session.Invoke(Request(session, "ncma.scene.transaction", input), EditPermissions());
        Check(result.Status == "ok" && result.Changed && result.Revision == 1 && world.FindObject(id).Name == "Hero");
    }),
    ("Late transaction failure is atomic", () => {
        var document = new SceneDocument(); var world = document.World; var session = new EditSession(document); Guid id = Guid.NewGuid();
        var input = Element(new { operations = new object[] { new { op = "create", objectId = id, name = "A" }, new { op = "rename", objectId = Guid.NewGuid(), name = "Missing" } } });
        Check(session.Invoke(Request(session, "ncma.scene.transaction", input), EditPermissions()).Status == "error");
        Check(world.Count == 0 && session.Revision == 0);
        Check(session.Invoke(Request(session, "ncma.history.undo", Json("{}")), EditPermissions()).Code == "history_empty");
    }),
    ("Request identity/revision guards and idempotency", () => {
        var document = new SceneDocument(); var world = document.World; var session = new EditSession(document); Guid id = Guid.NewGuid();
        var request = Request(session, "ncma.scene.transaction", Create(id));
        Check(session.Invoke(request with { SessionId = Guid.NewGuid() }, EditPermissions()).Code == "session_mismatch");
        Check(session.Invoke(request with { ExpectedRevision = null }, EditPermissions()).Code == "revision_conflict");
        var first = session.Invoke(request, EditPermissions()); var second = session.Invoke(request, EditPermissions());
        Check(second.Replayed && first.ExecutionRevision == second.ExecutionRevision && world.Count == 1 && world.Revision == 1);
        Check(session.Invoke(request with { Input = Create(Guid.NewGuid()) }, EditPermissions()).Code == "request_id_reused");
        Check(session.Invoke(Request(session, "ncma.scene.transaction", Rename(id, "B")) with { ExpectedRevision = 0 }, EditPermissions()).Code == "revision_conflict");
        Check(session.Invoke(request).Status == "denied");
    }),
    ("Undo/redo and redo branch invalidation", () => {
        var document = new SceneDocument(); var world = document.World; var session = new EditSession(document); Guid id = Guid.NewGuid(); var permissions = EditPermissions();
        Check(session.Invoke(Request(session, "ncma.scene.transaction", Create(id)), permissions).Status == "ok");
        Check(session.Invoke(Request(session, "ncma.history.undo", Json("{}")), permissions).Status == "ok" && world.Count == 0);
        Check(session.Invoke(Request(session, "ncma.history.redo", Json("{}")), permissions).Status == "ok" && world.Count == 1);
        Check(session.Invoke(Request(session, "ncma.scene.transaction", Rename(id, "Old")), permissions).Status == "ok");
        Check(session.Invoke(Request(session, "ncma.history.undo", Json("{}")), permissions).Status == "ok");
        Check(session.Invoke(Request(session, "ncma.scene.transaction", Rename(id, "New")), permissions).Status == "ok");
        Check(session.Invoke(Request(session, "ncma.history.redo", Json("{}")), permissions).Code == "history_empty" && world.FindObject(id).Name == "New");
    }),
    ("Destructive UUID authorization and redo permission", () => {
        var document = new SceneDocument(); var world = document.World; Guid id = world.CreateObject("A").PersistentId; var session = new EditSession(document);
        var limited = new CapabilityPermissions(["ncma.scene.delete_object", "ncma.history.undo", "ncma.history.redo"]);
        var approved = new CapabilityPermissions(["ncma.scene.delete_object", "ncma.history.undo", "ncma.history.redo"], [id]);
        var request = Request(session, "ncma.scene.delete_object", Element(new { objectId = id }));
        Check(session.Invoke(request, limited).Code == "target_not_authorized" && world.Count == 1);
        Check(session.Invoke(request, approved).Status == "ok" && world.Count == 0);
        Check(session.Invoke(Request(session, "ncma.history.undo", Json("{}")), approved).Status == "ok" && world.Count == 1);
        Check(session.Invoke(Request(session, "ncma.history.redo", Json("{}")), limited).Status == "denied" && world.Count == 1);
    }),
    ("No deletion/eval bypass inside transaction", () => {
        var document = new SceneDocument(); var world = document.World; Guid id = world.CreateObject("A").PersistentId; var session = new EditSession(document); ulong revision = world.Revision;
        var input = Element(new { operations = new[] { new { op = "delete", objectId = id } } });
        Check(session.Invoke(Request(session, "ncma.scene.transaction", input), EditPermissions()).Status == "error");
        Check(session.Invoke(Request(session, "ncma.python.exec", Json("{}")), EditPermissions()).Code == "unknown_capability");
        Check(world.Count == 1 && world.Revision == revision);
    }),
    ("Closed inputs/schema/size rejection", () => {
        var document = new SceneDocument(); var world = document.World; Guid id = world.CreateObject("A").PersistentId; var session = new EditSession(document); var permissions = EditPermissions();
        Check(session.Invoke(Request(session, "ncma.scene.inspect", Json("{\"permission\":true}"))).Status == "error");
        Check(session.Invoke(Request(session, "ncma.scene.transaction", Json("{\"operations\":[],\"operations\":[]}")), permissions).Status == "error");
        Check(session.Invoke(Request(session, "ncma.scene.transaction", Element(new { padding = new string('x', EditSession.MaxInputBytes) })), permissions).Status == "error");
        var bad = Element(new { operations = new[] { new { op = "set_component", objectId = id, typeId = "ncma.transform", version = 1, data = Json("{\"position\":{},\"rotation\":{},\"scale\":{}}") } } });
        Check(session.Invoke(Request(session, "ncma.scene.transaction", bad), permissions).Status == "error" && !world.FindObject(id).Has<TransformData>());
    }),
    ("History cannot overwrite outside edits", () => {
        var document = new SceneDocument(); var world = document.World; var session = new EditSession(document); var permissions = EditPermissions();
        Check(session.Invoke(Request(session, "ncma.scene.transaction", Create(Guid.NewGuid())), permissions).Status == "ok");
        world.CreateObject("Outside");
        Check(session.Invoke(Request(session, "ncma.history.undo", Json("{}")), permissions).Code == "history_invalidated" && world.Count == 2);
    }),
    ("Bounded history", () => {
        var document = new SceneDocument(); var world = document.World; var session = new EditSession(document); var permissions = EditPermissions();
        for (int i = 0; i < 70; ++i) Check(session.Invoke(Request(session, "ncma.scene.transaction", Create(Guid.NewGuid())), permissions).Status == "ok");
        for (int i = 0; i < EditSession.MaxHistoryEntries; ++i) Check(session.Invoke(Request(session, "ncma.history.undo", Json("{}")), permissions).Status == "ok");
        Check(world.Count == 6 && session.Invoke(Request(session, "ncma.history.undo", Json("{}")), permissions).Code == "history_empty");
    }),
    ("Malformed numeric/deep input produces structured errors", () => {
        var document = new SceneDocument(); var world = document.World; var session = new EditSession(document); var permissions = EditPermissions();
        var input = Element(new { operations = new[] { new { op = "set_component", objectId = Guid.NewGuid(), typeId = "ncma.transform", version = 1e30, data = Json("{}") } } });
        Check(session.Invoke(Request(session, "ncma.scene.transaction", input), permissions).Status == "error");
        string deep = string.Concat(Enumerable.Repeat("{\"nested\":", 40)) + "{}" + new string('}', 40);
        Check(session.Invoke(Request(session, "ncma.scene.inspect", Json(deep))).Code == "invalid_input");
        Check(world.Count == 0 && world.Revision == 0);
    }),
    ("Cyclic trusted schema fails safely", () => {
        var registry = new ComponentRegistry();
        registry.Register<Health>("cyclic", 1, """{"$ref":"#/$defs/loop","$defs":{"loop":{"$ref":"#/$defs/loop"}}}""", v => v);
        var document = new SceneDocument(components: registry); var world = document.World; var session = new EditSession(document); var permissions = EditPermissions();
        Guid id = Guid.NewGuid();
        var input = Element(new { operations = new object[] { new { op = "create", objectId = id, name = "A" },
            new { op = "set_component", objectId = id, typeId = "cyclic", version = 1, data = Json("{}") } } });
        Check(session.Invoke(Request(session, "ncma.scene.transaction", input), permissions).Code == "invalid_input" && world.Count == 0);
    }),
    ("Extension validator failure does not partially commit", () => {
        var registry = new ComponentRegistry();
        registry.Register<Health>("fault", 1, """{"type":"object","required":["points"],"properties":{"points":{"type":"integer"}}}""", _ => throw new ApplicationException("Validator failed"));
        var document = new SceneDocument(components: registry); var world = document.World; var session = new EditSession(document); Guid id = Guid.NewGuid();
        var input = Element(new { operations = new object[] { new { op = "create", objectId = id, name = "A" },
            new { op = "set_component", objectId = id, typeId = "fault", version = 1, data = Json("{\"points\":1}") } } });
        Check(session.Invoke(Request(session, "ncma.scene.transaction", input), EditPermissions()).Code == "operation_failed" && world.Count == 0);
    }),
    ("Complete components and all Behaviour metadata survive mixed history", () => {
        var document = new SceneDocument("Complete", Extensions()); var obj = document.World.CreateObject("Logic");
        obj.Set(new Health(100)); Guid id = obj.PersistentId;
        var binding = new BehaviourBindingData(Guid.NewGuid(), "Missing.Type", false, [
            new("Float", ExportKind.Float, .5), new("Double", ExportKind.Double, 1e100),
            new("Integer", ExportKind.Integer, 42), new("Boolean", ExportKind.Boolean, 1)]);
        document.SetBindings(id, [binding, binding with { Id = Guid.NewGuid() }]);
        var session = new EditSession(document); byte[] before = document.CaptureBytes();
        Check(session.Invoke(Request(session, "ncma.scene.transaction", Rename(id, "Renamed")), EditPermissions()).Changed);
        byte[] after = document.CaptureBytes();
        Check(session.Invoke(Request(session, "ncma.history.undo", Json("{}")), EditPermissions()).Status == "ok");
        Check(before.SequenceEqual(document.CaptureBytes()) && document.GetBindings(id).Length == 2);
        Check(session.Invoke(Request(session, "ncma.history.redo", Json("{}")), EditPermissions()).Status == "ok");
        Check(after.SequenceEqual(document.CaptureBytes()) && document.World.FindObject(id).Get<Health>().Points == 100);
    }),
    ("No change keeps references, revision, history and redo", () => {
        var document = new SceneDocument(); Guid id = document.World.CreateObject("A").PersistentId;
        var session = new EditSession(document); var p = EditPermissions(); var live = document.World.FindObject(id);
        ulong revision = session.Revision;
        var result = session.Invoke(Request(session, "ncma.scene.transaction", Rename(id, "A")), p);
        Check(result.Code == "no_change" && !result.Changed && session.Revision == revision && session.State.UndoCount == 0 && live.Name == "A");
        session.Invoke(Request(session, "ncma.scene.transaction", Rename(id, "B")), p);
        session.Invoke(Request(session, "ncma.history.undo", Json("{}")), p);
        session.Invoke(Request(session, "ncma.scene.transaction", Rename(id, "A")), p);
        Check(session.State.RedoCount == 1);
    }),
    ("Interaction preview, one commit, cancel and write exclusion", () => {
        var document = new SceneDocument(); Guid id = document.World.CreateObject("A").PersistentId;
        var session = new EditSession(document); var p = EditPermissions(); ulong revision = session.Revision;
        Guid token = session.BeginInteraction(id, "Drag", revision, p);
        for (int i = 0; i < 10; i++) session.UpdateInteraction(token, Rename(id, "Preview" + i), p);
        Check(document.World.FindObject(id).Name == "A" && session.Revision == revision && session.State.UndoCount == 0);
        Check(session.Invoke(Request(session, "ncma.scene.transaction", Rename(id, "Blocked")), p).Code == "edit_busy");
        Check(session.Invoke(Request(session, "ncma.scene.inspect", Json("{}"))).Status == "ok");
        Check(session.CommitInteraction(token, p).Changed && document.World.FindObject(id).Name == "Preview9" && session.State.UndoCount == 1);
        revision = session.Revision; token = session.BeginInteraction(id, "Cancel", revision, p);
        session.UpdateInteraction(token, Rename(id, "Cancelled"), p); session.CancelInteraction(token);
        Check(session.Revision == revision && document.World.FindObject(id).Name == "Preview9" && session.State.UndoCount == 1);
    }),
    ("Draft conflicts and revoked permissions cannot commit", () => {
        var document = new SceneDocument(); Guid id = document.World.CreateObject("A").PersistentId;
        var session = new EditSession(document); var p = EditPermissions();
        Guid token = session.BeginInteraction(id, "Rename", session.Revision, p); session.UpdateInteraction(token, Rename(id, "B"), p);
        Check(session.CommitInteraction(token).Code == "permission_denied" && document.World.FindObject(id).Name == "A");
        token = session.BeginInteraction(id, "Rename", session.Revision, p); session.UpdateInteraction(token, Rename(id, "B"), p);
        document.World.FindObject(id).Name = "External";
        Check(session.CommitInteraction(token, p).Code == "history_invalidated" && document.World.FindObject(id).Name == "External");
        session.Resynchronize(); Check(!session.State.HistoryInvalidated);
    }),
    ("Session owner thread and complete-document validator reentrancy", () => {
        SceneDocument? document = null; EditSession? session = null; bool armed = false; int blocked = 0;
        var registry = ComponentRegistry.CreateDefault();
        registry.Register<Health>("game.health", 1, """{"type":"object","required":["points"],"properties":{"points":{"type":"integer"}}}""", v => {
            if (armed) {
                try { document!.World.CreateObject("Reentrant"); } catch (InvalidOperationException) { blocked++; }
                try { document!.SetBindings(document.World.GetObjects()[0].PersistentId, []); } catch (InvalidOperationException) { blocked++; }
                Check(session!.Invoke(Request(session, "ncma.scene.transaction", Rename(document!.World.GetObjects()[0].PersistentId, "Reentrant")), EditPermissions()).Code == "edit_busy");
            }
            return v;
        });
        document = new SceneDocument(components: registry); var obj = document.World.CreateObject("A"); obj.Set(new Health(1));
        session = new EditSession(document); armed = true;
        Check(session.Invoke(Request(session, "ncma.scene.transaction", Rename(obj.PersistentId, "B")), EditPermissions()).Changed && blocked == 2);
        Check(Task.Run(() => { try { _ = session.State; return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult());
    }),
    ("Bounded cache retries report execution and current revisions", () => {
        var document = new SceneDocument(); Guid id = document.World.CreateObject("A").PersistentId;
        var session = new EditSession(document); var p = EditPermissions();
        var firstRequest = Request(session, "ncma.scene.transaction", Rename(id, "First")); var first = session.Invoke(firstRequest, p);
        session.Invoke(Request(session, "ncma.scene.transaction", Rename(id, "Second")), p);
        var replay = session.Invoke(firstRequest, p);
        Check(replay.Replayed && replay.ExecutionRevision == first.Revision && replay.Revision == session.Revision && document.World.FindObject(id).Name == "Second");
        Check(replay.Data.GetProperty("history").GetProperty("revision").GetUInt64() == session.Revision);
        for (int i = 0; i < EditSession.MaxCachedRequests; i++) session.Invoke(Request(session, "ncma.scene.transaction", Rename(id, "Next" + i)), p);
        Check(session.Invoke(firstRequest, p).Code == "revision_conflict");
    }),
    ("Paged inspection and protocol version reject old payload semantics", () => {
        var document = new SceneDocument(); for (int i = 0; i < 40; i++) document.World.CreateObject("A" + i);
        var session = new EditSession(document);
        var result = session.Invoke(Request(session, "ncma.scene.inspect", Element(new { offset = 32, limit = 8 })));
        Check(result.Data.GetProperty("totalCount").GetInt32() == 40 && result.Data.GetProperty("scene").GetProperty("objects").GetArrayLength() == 8);
        Check(session.Invoke(Request(session, "ncma.scene.inspect", Json("{}")) with { ContractVersion = 1 }).Code == "invalid_envelope");
        Check(typeof(World).Assembly.GetType("Ncma.Runtime.EditSession") is null);
        Check(!typeof(EditSession).Assembly.GetReferencedAssemblies().Any(a => a.Name!.Contains("Managed") || a.Name.Contains("Python")));
    }),
    ("Bindings and optional component commands preserve complete history", () => {
        var d = new SceneDocument(components: Extensions()); var obj = d.World.CreateObject("A");
        obj.Set(new Health(5)); obj.Set(TransformData.Identity); Guid id = obj.PersistentId;
        var s = new EditSession(d); var p = EditPermissions(); s.Select(id); var initial = d.CaptureBytes();
        var b = new BehaviourBindingData(Guid.NewGuid(), "Missing.Behaviour", false, [
            new("Float", ExportKind.Float, .25), new("Double", ExportKind.Double, 1e100),
            new("Integer", ExportKind.Integer, 23), new("Boolean", ExportKind.Boolean, 1)]);
        JsonElement Bind(BehaviourBindingData[] values) => JsonSerializer.SerializeToElement(new { operations = new[] {
            new { op = "set_bindings", objectId = id, bindings = values } } }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Check(s.Invoke(Request(s, "ncma.scene.transaction", Bind([b, b with { Id = Guid.NewGuid() }])), p).Changed);
        var bindings = d.GetBindings(id); bindings[0] = bindings[0] with { Enabled = true, Exports = [new("Speed", ExportKind.Double, 10)] };
        Check(s.Invoke(Request(s, "ncma.scene.transaction", Bind(bindings)), p).Changed);
        Check(s.Invoke(Request(s, "ncma.scene.transaction", Element(new { operations = new[] { new { op = "remove_component", objectId = id, typeId = "ncma.transform" } } })), p).Changed);
        Check(!d.World.FindObject(id).Has<TransformData>() && d.World.FindObject(id).Get<Health>().Points == 5);
        Check(s.Invoke(Request(s, "ncma.scene.transaction", Bind([])), p).Changed); var final = d.CaptureBytes();
        for (int i = 0; i < 4; i++) Check(s.Invoke(Request(s, "ncma.history.undo", Json("{}")), p).Changed);
        Check(initial.SequenceEqual(d.CaptureBytes()) && s.State.Selection == id);
        for (int i = 0; i < 4; i++) Check(s.Invoke(Request(s, "ncma.history.redo", Json("{}")), p).Changed);
        Check(final.SequenceEqual(d.CaptureBytes()));
    }),
    ("Invalid bindings, unregistered removal and late mixed failure are atomic", () => {
        var d = new SceneDocument(); Guid id = d.World.CreateObject("A").PersistentId; var s = new EditSession(d); var p = EditPermissions();
        var before = d.CaptureBytes(); ulong revision = s.Revision;
        var b = new BehaviourBindingData(Guid.NewGuid(), "Missing.Type", true, [new("Flag", ExportKind.Boolean, 2)]);
        var input = JsonSerializer.SerializeToElement(new { operations = new object[] {
            new { op = "rename", objectId = id, name = "B" }, new { op = "set_bindings", objectId = id, bindings = new[] { b } }
        } }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Check(s.Invoke(Request(s, "ncma.scene.transaction", input), p).Status == "error");
        Check(s.Invoke(Request(s, "ncma.scene.transaction", Element(new { operations = new[] { new { op = "remove_component", objectId = id, typeId = "unknown" } } })), p).Status == "error");
        Check(before.SequenceEqual(d.CaptureBytes()) && revision == s.Revision && s.State.UndoCount == 0);
    }),
    ("Export previews are isolated and commit exactly one command", () => {
        var d = new SceneDocument(); Guid id = d.World.CreateObject("A").PersistentId;
        d.SetBindings(id, [new(Guid.NewGuid(), "Missing.Type", true, [new("Speed", ExportKind.Double, 1)])]);
        var s = new EditSession(d); var p = EditPermissions(); byte[] before = d.CaptureBytes();
        Guid token = s.BeginInteraction(id, "Drag Export", s.Revision, p);
        for (int i = 2; i < 12; i++) {
            var bindings = d.GetBindings(id); bindings[0] = bindings[0] with { Exports = [new("Speed", ExportKind.Double, i)] };
            var input = JsonSerializer.SerializeToElement(new { operations = new[] { new { op = "set_bindings", objectId = id, bindings } } }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            _ = s.UpdateInteraction(token, input, p);
        }
        Check(before.SequenceEqual(d.CaptureBytes()) && s.CommitInteraction(token, p).Changed && s.State.UndoCount == 1);
        Check(d.GetBindings(id)[0].Exports[0].Value == 11);
    }),
    ("New/open Undo restores selection, file association and saved fingerprint", () => {
        string folder = TestFolder(); string a = Path.Combine(folder, "a.ncmascene"), b = Path.Combine(folder, "b.ncmascene");
        var d = new SceneDocument("A"); Guid id = d.World.CreateObject("A").PersistentId; var s = new EditSession(d); var p = EditPermissions(); s.Select(id);
        s.SaveDocument(a, p); Check(!s.State.Dirty && s.State.UndoCount == 0);
        var other = new SceneDocument("B"); Guid otherId = other.World.CreateObject("B").PersistentId; SceneDocumentFiles.Save(other, b);
        Check(s.OpenDocument(b, s.Revision, p).Changed && !s.State.Dirty && s.State.FilePath == b && s.State.Selection == otherId);
        Check(s.Invoke(Request(s, "ncma.history.undo", Json("{}")), p).Changed);
        Check(!s.State.Dirty && s.State.FilePath == a && s.State.Selection == id && d.World.Name == "A");
        Check(s.Invoke(Request(s, "ncma.history.redo", Json("{}")), p).Changed && s.State.FilePath == b);
        Check(s.NewDocument(s.Revision, p).Changed && s.State.Dirty && s.State.FilePath is null && s.State.Selection is null);
        Check(s.Invoke(Request(s, "ncma.history.undo", Json("{}")), p).Changed && !s.State.Dirty && s.State.FilePath == b && s.State.Selection == otherId);
    }),
    ("Failed open/save and frozen file operations preserve document and history", () => {
        string folder = TestFolder(); string file = Path.Combine(folder, "saved.ncmascene"), corrupt = Path.Combine(folder, "corrupt.ncmascene");
        var d = new SceneDocument(); Guid id = d.World.CreateObject("A").PersistentId; var s = new EditSession(d); var p = EditPermissions(); s.Select(id);
        s.SaveDocument(file, p); s.Invoke(Request(s, "ncma.scene.transaction", Rename(id, "B")), p);
        var bytes = d.CaptureBytes(); var state = s.State;
        File.WriteAllText(corrupt, "{bad}");
        Reject(() => s.OpenDocument(corrupt, s.Revision, p));
        Reject(() => s.OpenDocument(Path.Combine(folder, "old.ncscene"), s.Revision, p));
        Reject(() => s.SaveDocument(corrupt, p));
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None)) {
            try { s.SaveDocument(file, p); throw new Exception("Locked save accepted"); } catch (IOException) { }
        }
        Check(state == s.State && bytes.SequenceEqual(d.CaptureBytes()));
        s.SetFrozen(true); var frozen = s.State;
        Reject(() => s.SaveDocument(file, p)); Reject(() => s.NewDocument(s.Revision, p)); Reject(() => s.OpenDocument(file, s.Revision, p));
        Check(s.Invoke(Request(s, "ncma.history.undo", Json("{}")), p).Code == "play_frozen");
        Check(frozen == s.State && bytes.SequenceEqual(d.CaptureBytes()));
        s.SetFrozen(false); Check(s.Invoke(Request(s, "ncma.history.undo", Json("{}")), p).Changed && !s.State.Dirty);
    }),
    ("Dirty uses content even after history eviction and branch changes", () => {
        var d = new SceneDocument(); Guid id = d.World.CreateObject("A").PersistentId; var s = new EditSession(d); var p = EditPermissions();
        string file = Path.Combine(TestFolder(), "saved.ncmascene"); s.SaveDocument(file, p);
        for (int i = 0; i < 70; i++) Check(s.Invoke(Request(s, "ncma.scene.transaction", Rename(id, "B" + i)), p).Changed);
        Check(s.State.UndoCount == 64 && s.State.Dirty);
        Check(s.Invoke(Request(s, "ncma.scene.transaction", Rename(id, "A")), p).Changed && !s.State.Dirty);
        Check(s.Invoke(Request(s, "ncma.history.undo", Json("{}")), p).Changed && s.State.Dirty);
        s.SaveDocument(permissions: p); Check(!s.State.Dirty);
        Check(s.Invoke(Request(s, "ncma.scene.transaction", Rename(id, "Branch")), p).Changed && s.State.Dirty && s.State.RedoCount == 0);
        Check(s.Invoke(Request(s, "ncma.history.undo", Json("{}")), p).Changed && !s.State.Dirty);
    }),
    ("Delete clears only its UUID and Undo restores selection and bindings", () => {
        var d = new SceneDocument(); Guid id = d.World.CreateObject("A").PersistentId; Guid other = d.World.CreateObject("B").PersistentId;
        d.SetBindings(id, [new(Guid.NewGuid(), "Missing.Type", false, [])]); var s = new EditSession(d); s.Select(id); byte[] before = d.CaptureBytes();
        var p = new CapabilityPermissions(["ncma.scene.delete_object", "ncma.history.undo", "ncma.history.redo"], [id]);
        Check(s.Invoke(Request(s, "ncma.scene.delete_object", Element(new { objectId = other })), p).Code == "target_not_authorized");
        Check(s.Invoke(Request(s, "ncma.scene.delete_object", Element(new { objectId = id })), p).Changed && s.State.Selection is null && d.World.FindObject(other).Name == "B");
        Check(s.HistoryTarget(false) == id && s.Invoke(Request(s, "ncma.history.undo", Json("{}")), p).Changed);
        Check(s.State.Selection == id && before.SequenceEqual(d.CaptureBytes()));
    }),
    ("Identical new/open document commands keep revision and history", () => {
        var d = new SceneDocument(); var s = new EditSession(d); var p = EditPermissions();
        Check(s.NewDocument(s.Revision, p).Code == "no_change" && s.Revision == 0 && s.State.UndoCount == 0);
        string file = Path.Combine(TestFolder(), "empty.ncmascene"); s.SaveDocument(file, p);
        var state = s.State; Check(s.OpenDocument(file, s.Revision, p).Code == "no_change" && s.State == state);
    }),
    ("JSON gateway cannot self-grant writes or file permissions", () => {
        var d = new SceneDocument(); var s = new EditSession(d); Guid id = Guid.NewGuid();
        byte[] request = JsonSerializer.SerializeToUtf8Bytes(Request(s, "ncma.scene.transaction", Create(id)), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Check(s.InvokeJson(request).Code == "permission_denied" && d.World.Count == 0);
        Check(s.InvokeJson(System.Text.Encoding.UTF8.GetBytes("{}")).Status == "error");
        Check(s.Invoke(Request(s, "ncma.scene.open", Element(new { path = "a.ncmascene" })), EditPermissions()).Code == "unknown_capability");
        Reject(() => s.NewDocument(s.Revision)); Check(d.World.Count == 0);
    }),
};
int failures = 0;
foreach (var test in cases)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { ++failures; Console.Error.WriteLine("FAIL " + test.Name + ": " + error); }
}
Console.WriteLine($"Ncma Editor.Core: {cases.Length - failures}/{cases.Length} passed (no native DLL/Python).");
return failures == 0 ? 0 : 1;

readonly record struct Health(int Points) : IComponent;
