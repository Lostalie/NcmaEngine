using System.Reflection;
using Ncma.Runtime;
using Ncma.Scene;

internal static class AuthoringStoreTests
{
    private static void Check(bool condition, string message = "Authoring store assertion")
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected authoring rejection");
    }
    private static long Captures(SceneDocument doc) => (long)typeof(SceneDocument)
        .GetField("_capturedDocumentCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(doc)!;
    private struct Dynamic : IComponent
    {
        internal static int Current;
        public int Value { get => Current; set { } }
    }
    private readonly record struct TextValue(string Text) : IComponent;
    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("E6 authoring is UUID/serialized values only, no World/Entity/public legacy entry", () => {
            var document = new SceneDocument("Authoring"); Guid a = document.CreateObject("A"), b = document.CreateObject("B");
            document.SetComponent(a, TransformData.Identity); document.RenameObject(b, "Logic");
            Check(document.Name == "Authoring" && document.ObjectCount == 2 && document.GetObjectIds().SequenceEqual([a, b]));
            Check(document.HasComponent<TransformData>(a) && !document.HasComponent<TransformData>(b));
            Check(typeof(SceneDocument).GetProperty("World") is null);
            Check(typeof(SceneDocument).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .All(f => f.FieldType != typeof(World) && f.FieldType != typeof(Entity)));
            var exposed = typeof(SceneDocument).GetMethods(BindingFlags.Instance | BindingFlags.Public);
            Check(exposed.All(m => m.ReturnType != typeof(World) && m.ReturnType != typeof(Entity) &&
                m.GetParameters().All(p => p.ParameterType != typeof(Entity))));
            document.DeleteObject(a); Reject(() => document.GetComponent<TransformData>(a));
            Check(document.GetObjectIds().SequenceEqual([b]));
        });
        yield return ("E6 authoring stores frozen serialized values, custom getters cannot mutate past writes", () => {
            var registry = global::Ncma.Runtime.CompositionRegistry.Build();
            registry.Register<Dynamic>("dynamic", 1, """{"type":"object","required":["value"],"properties":{"value":{"type":"integer"}}}""", v => v);
            var document = new SceneDocument("Frozen authoring", registry); var id = document.CreateObject("A");
            Dynamic.Current = 7; document.SetComponent(id, new Dynamic());
            byte[] original = document.CaptureBytes(); ulong revision = document.Revision; Dynamic.Current = 99;
            try
            {
                Check(document.GetComponents(id).Single().Data.GetProperty("value").GetInt32() == 7);
                Check(original.SequenceEqual(document.CaptureBytes()) && document.Revision == revision);
                document.SetComponent(id, new Dynamic());
                Check(document.GetComponents(id).Single().Data.GetProperty("value").GetInt32() == 99 && document.Revision == revision + 1);
            }
            finally { Dynamic.Current = 0; }
        });
        yield return ("E6 authoring candidate atomicity, normalized schema failure and detached callbacks", () => {
            SceneDocument? source = null;
            source = new("Atomic", validateComposition: page => {
                Reject(() => source!.Rename("Injected"));
                if (page.Objects.Length > 0) page.Objects[0] = page.Objects[0] with { Name = "Callback corruption" };
            });
            var id = source.CreateObject("Original"); source.SetComponent(id, TransformData.Identity);
            byte[] before = source.CaptureBytes(); ulong revision = source.Revision;
            var invalid = source.CaptureSnapshot(); invalid.Objects[0].Components[0] = invalid.Objects[0].Components[0] with { Version = 900 };
            Reject(() => source.RestoreSnapshot(invalid)); Reject(() => source.CreateObject("Duplicate", id));
            Reject(() => source.SetComponent(id, TransformData.Identity with { Rotation = default }));
            Check(before.SequenceEqual(source.CaptureBytes()) && source.Revision == revision);
            var candidate = source.CaptureSnapshot(); candidate.Objects[0] = candidate.Objects[0] with { Name = "Prepared" };
            var commit = source.PrepareRestore(candidate, revision, page => page.Objects[0] = page.Objects[0] with { Name = "Second corruption" });
            candidate.Objects[0] = candidate.Objects[0] with { Name = "Caller corruption" };
            commit(); Check(source.GetObjectName(id) == "Prepared" && source.Revision == revision + 1);
            Reject(commit); Check(source.GetObjectName(id) == "Prepared");
        });
        yield return ("E6 authoring rejects stale prepared installs and foreign thread/read-only scope writes", () => {
            var document = new SceneDocument(); var id = document.CreateObject("A");
            var commit = document.PrepareRestore(document.CaptureSnapshot(), document.Revision);
            document.RenameObject(id, "B"); Reject(commit);
            using (document.ReadOnly())
            {
                Reject(() => document.DeleteObject(id)); Reject(() => document.SetBindings(id, []));
                Reject(() => document.RestoreBytes(document.CaptureBytes())); Check(document.GetObjectName(id) == "B");
                using (document.ReadOnly()) Reject(() => document.Rename("Blocked"));
            }
            Task.Run(() => { Reject(() => document.CaptureSnapshot()); Reject(() => document.CreateObject("Foreign")); }).GetAwaiter().GetResult();
            document.Rename("Writable again"); Check(document.Name == "Writable again");
        });
        yield return ("E6 authoring order and binding ownership survive restore, copies and bounded reads", () => {
            var document = new SceneDocument(); Guid a = document.CreateObject("A"), b = document.CreateObject("B");
            document.SetBindings(a, [new(Guid.NewGuid(), "Missing.Disabled", false, [new("Speed", ExportKind.Float, 2)])]);
            var state = document.CaptureSnapshot(); document.RestoreSnapshot(state with { Objects = state.Objects.Reverse().ToArray() });
            long captures = Captures(document);
            Check(document.ReadPage(0, 1, out int total).Objects.Single().Id == b && total == 2);
            Check(document.ReadPage(int.MaxValue, 128, out total).Objects.Length == 0 && total == 2);
            var bindings = document.GetBindings(a); bindings[0].Exports[0] = new("Bad", ExportKind.Float, 99);
            var page = document.ReadObject(a)!; page.Behaviours[0] = page.Behaviours[0] with { Enabled = true };
            Check(Captures(document) == captures && document.GetBindings(a)[0].Exports.Single().Value == 2 && !document.GetBindings(a)[0].Enabled);
            Reject(() => document.ReadPage(0, 129, out _)); Reject(() => document.ReadPage(-1, 1, out _));
            using var guard = document.ReadOnly(); var copy = document.CreateIsolatedCopy();
            copy.RenameObject(a, "Independent"); Check(document.GetObjectName(a) == "A");
        });
        yield return ("E6 authoring normalized budget failure preserves bytes and revision", () => {
            var registry = global::Ncma.Runtime.CompositionRegistry.Build();
            registry.Register<TextValue>("text", 1, """{"type":"object","required":["text"],"properties":{"text":{"type":"string"}}}""",
                v => v.Text == "expand" ? new(new string('x', ComponentRegistry.MaxPayloadBytes + 1)) : v);
            var document = new SceneDocument("Budget", registry); var id = document.CreateObject("A");
            document.SetComponent(id, new TextValue("safe")); byte[] original = document.CaptureBytes(); ulong revision = document.Revision;
            Reject(() => document.SetComponent(id, new TextValue("expand")));
            Check(original.SequenceEqual(document.CaptureBytes()) && revision == document.Revision);
        });
        yield return ("E6 authoring bakes isolated runtime Worlds without retaining editable storage", () => {
            var document = new SceneDocument("Source"); var id = document.CreateObject("Actor"); document.SetComponent(id, TransformData.Identity);
            byte[] source = document.CaptureBytes(); ulong revision = document.Revision;
            var baked = document.BakeRuntime(revision); var a = baked.Image.Instantiate(document.Components); var b = baked.Image.Instantiate(document.Components);
            Check(a.World.Identity != b.World.Identity && a.World.GetUuid(a.World.FindEntity(id)) == id);
            a.World.Set(a.World.FindEntity(id), TransformData.Identity with { Position = new(3, 4, 5) });
            Check(document.GetComponent<TransformData>(id) == TransformData.Identity && b.World.Get<TransformData>(b.World.FindEntity(id)) == TransformData.Identity);
            document.RenameObject(id, "Edited after bake"); Check(a.World.GetName(a.World.FindEntity(id)) == "Actor");
            Reject(() => document.BakeRuntime(revision));
            document.RestoreBytes(source); Check(document.BakeRuntime(document.Revision).Image.ContentHash == baked.Image.ContentHash);
        });
    }
}
