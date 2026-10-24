using System.Text.Json;
using Ncma.Runtime;
using Ncma.Scene;

// Registered now; execution deferred until the entire E6 implementation is ready.
internal static class RuntimeSceneBakeTests
{
    private static void Check(bool value, string reason)
    { if (!value) throw new InvalidOperationException(reason); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; }
        throw new Exception("Invalid bake operation accepted.");
    }
    private static SceneDocument Make()
    {
        var doc = new SceneDocument("Bake source");
        var first = doc.CreateObject("Logic", Guid.Parse("ffffffff-0000-0000-0000-000000000001"));
        var second = doc.CreateObject("Spatial", Guid.Parse("00000001-0000-0000-0000-000000000002"));
        doc.SetComponent(second, TransformData.Identity);
        doc.SetBindings(first, [new(
            Guid.Parse("00000002-0000-0000-0000-000000000003"), "Game.Logic", true,
            [new("Z", ExportKind.Double, -0.0), new("A", ExportKind.Integer, 5)])]);
        return doc;
    }
    public static IEnumerable<(string, Action)> Cases()
    {
        yield return ("E6-A bake deterministic bytes and read-only source", () => {
            var doc = Make(); byte[] before = doc.CaptureBytes(); ulong revision = doc.Revision;
            Guid identity = doc.SessionId; var ids = doc.GetObjectIds();
            var a = doc.BakeRuntime(revision); var b = doc.BakeRuntime(revision);
            Check(a.Image.CopyBytes().SequenceEqual(b.Image.CopyBytes()) && a.Image.ContentHash == b.Image.ContentHash, "Unstable bake");
            Check(before.SequenceEqual(doc.CaptureBytes()) && doc.Revision == revision && doc.SessionId == identity, "Bake changed Edit");
            Check(ids.SequenceEqual(doc.GetObjectIds()), "Bake changed authoring UUID order");
            Check(a.Source == new SceneBakeStamp(doc.SessionId, revision), "Wrong source stamp");
        });
        yield return ("E6-A runtime image excludes authoring session identity", () => {
            var a = Make(); var b = new SceneDocument(); b.RestoreBytes(a.CaptureBytes());
            var x = a.BakeRuntime(a.Revision); var y = b.BakeRuntime(b.Revision);
            Check(x.Source.DocumentSession != y.Source.DocumentSession, "Fixture must use independent documents");
            Check(x.Image.CopyBytes().SequenceEqual(y.Image.CopyBytes()), "Authoring identity entered runtime content");
            using var json = JsonDocument.Parse(x.Image.CopyBytes());
            Check(json.RootElement.GetProperty("format").GetString() == RuntimeSceneImage.Format &&
                json.RootElement.GetProperty("version").GetInt32() == RuntimeSceneImage.FormatVersion, "Missing runtime format discriminator");
            Check(!json.RootElement.TryGetProperty("revision", out _) && !json.RootElement.TryGetProperty("sessionId", out _), "Authoring revision serialized");
        });
        yield return ("E6-A image data and UUID order are owned", () => {
            var doc = Make(); var image = doc.BakeRuntime(doc.Revision).Image;
            var ids = image.CopyObjectIds(); var expected = doc.GetObjectIds();
            Check(ids.SequenceEqual(expected), "Authored order changed");
            ids[0] = Guid.Empty; byte[] bytes = image.CopyBytes(); bytes[0] = 0;
            Check(image.CopyObjectIds().SequenceEqual(expected) && image.CopyBytes()[0] != 0, "Mutable output aliases image");
        });
        yield return ("E6-A independent loads have independent ECS identities", () => {
            var doc = Make(); var image = doc.BakeRuntime(doc.Revision).Image;
            var a = image.Instantiate(ComponentRegistry.CreateDefault()); var b = image.Instantiate(ComponentRegistry.CreateDefault());
            Guid id = image.CopyObjectIds()[1]; var entity = a.World.FindEntity(id);
            Check(a.World.Identity != b.World.Identity && a.SessionId != b.SessionId, "Shared runtime identity");
            Reject(() => b.World.GetName(entity));
            a.World.Set(entity, TransformData.Identity with { Position = new(1, 2, 3) });
            Check(b.World.Get<TransformData>(b.World.FindEntity(id)) == TransformData.Identity &&
                doc.GetComponent<TransformData>(id) == TransformData.Identity, "Runtime mutation leaked");
        });
        yield return ("E6-A script copies are isolated and UUID reuse does not resurrect bindings", () => {
            var doc = Make(); var image = doc.BakeRuntime(doc.Revision).Image;
            var runtime = image.Instantiate(ComponentRegistry.CreateDefault()); Guid id = image.CopyObjectIds()[0];
            var bindings = runtime.GetBindings(id); bindings[0].Exports[0] = new("Corrupt", ExportKind.Boolean, 42);
            Check(runtime.GetBindings(id)[0].Exports[0].Name == "A", "Runtime binding aliases caller");
            runtime.World.Destroy(runtime.World.FindEntity(id)); runtime.World.CreateEntity("Replacement", id);
            Check(runtime.GetBindings(id).Length == 0, "Destroyed scripts resurrected");
            Check(image.Instantiate(ComponentRegistry.CreateDefault()).GetBindings(id).Length == 1, "Image was mutated");
        });
        yield return ("E6-A stale authoring revision rejected before conversion", () => {
            var doc = Make(); ulong old = doc.Revision; doc.CreateObject("New");
            byte[] before = doc.CaptureBytes(); int called = 0;
            Reject(() => doc.BakeRuntime(old, _ => called++));
            Check(called == 0 && before.SequenceEqual(doc.CaptureBytes()), "Stale bake had effects");
        });
        yield return ("E6-A failing composition leaves source and previous image intact", () => {
            var doc = Make(); var previous = doc.BakeRuntime(doc.Revision); byte[] before = doc.CaptureBytes();
            Reject(() => doc.BakeRuntime(doc.Revision, _ => throw new ArgumentException("Missing pinned asset")));
            Check(before.SequenceEqual(doc.CaptureBytes()) && previous.Image.ObjectCount == 2, "Failed bake published state");
            Check(doc.BakeRuntime(doc.Revision).Image.ContentHash == previous.Image.ContentHash, "Bake guard failed to release");
        });
        yield return ("E6-A composition receives copy and cannot mutate live source", () => {
            var doc = Make(); Guid id = doc.GetObjectIds()[0];
            var expected = doc.BakeRuntime(doc.Revision).Image;
            var actual = doc.BakeRuntime(doc.Revision, snapshot => {
                snapshot.Objects[0] = snapshot.Objects[0] with { Name = "Changed" };
                Reject(() => doc.CreateObject("Forbidden"));
                Reject(() => doc.SetBindings(id, []));
                Reject(() => doc.BakeRuntime(doc.Revision));
            });
            Check(expected.ContentHash == actual.Image.ContentHash && doc.ObjectCount == 2, "Composition altered output/source");
        });
        yield return ("E6-A incompatible registry load cannot replace active scene", () => {
            var doc = Make(); var image = doc.BakeRuntime(doc.Revision).Image;
            var active = image.Instantiate(ComponentRegistry.CreateDefault()); Guid identity = active.World.Identity;
            Reject(() => image.Instantiate(new ComponentRegistry()));
            Check(active.World.Identity == identity && active.World.Count == 2, "Failed load changed active World");
        });
        yield return ("E6-A pure logical scene does not inject transform or 3D data", () => {
            var doc = new SceneDocument("Logic only", new ComponentRegistry()); var entity = doc.CreateObject("Empty");
            var image = doc.BakeRuntime(doc.Revision).Image; var runtime = image.Instantiate(new ComponentRegistry());
            Check(runtime.World.FindEntity(entity) != default &&
                runtime.World.CaptureSnapshot().Objects[0].Components.Length == 0 &&
                runtime.World.Components.Describe().Count == 0, "Unrequested domain dependencies injected");
        });
    }
}
