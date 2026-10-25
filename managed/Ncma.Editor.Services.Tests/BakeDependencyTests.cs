using Ncma.Application.Runtime;
using Ncma.Editor.Services;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;
using Ncma.Scripting;

// Prepared E6 regression cases; not invoked during implementation/compilation checks.
internal static class BakeDependencyTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Bake dependency assertion"); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; }
        throw new Exception("Expected dependency rejection");
    }
    internal static IEnumerable<(string, Action)> Cases(string repository)
    {
        string assembly = Path.Combine(repository, "out/managed/Ncma.Gameplay.Sample.dll");
        yield return ("E6 binding closure is deterministic copied and generation guarded", () => {
            using var catalog = new ScriptCatalogService();
            using (var candidate = catalog.LoadCandidate(assembly)) catalog.CommitCandidate(candidate);
            var d = new SceneDocument(); var id = d.CreateObject("Script");
            d.SetBindings(id, [new(Guid.NewGuid(), catalog.Snapshot.Types.Single().TypeName, false,
                [new("Multiplier", ExportKind.Integer, 2)])]);
            var image = d.BakeRuntime(d.Revision).Image; byte[] before = d.CaptureBytes();
            var a = catalog.ResolveBindings(image); var b = catalog.ResolveBindings(image);
            Check(a.ContentHash == b.ContentHash && a.BindingCount == 1 && before.SequenceEqual(d.CaptureBytes()));
            var types = a.CopyTypes(); types[0] = types[0] with { TypeName = "Changed" };
            Check(a.CopyTypes()[0].TypeName != "Changed");
            using var foreign = new ScriptCatalogService(); Reject(() => foreign.RequireCurrent(a));
            using (var candidate = catalog.LoadCandidate(assembly)) catalog.CommitCandidate(candidate);
            Reject(() => catalog.RequireCurrent(a));
            Check(catalog.ResolveBindings(image).ContentHash == a.ContentHash);
        });
        yield return ("E6 missing disabled scripts fail before runtime factory or composition", () => {
            var d = new SceneDocument(); var id = d.CreateObject("Missing");
            d.SetBindings(id, [new(Guid.NewGuid(), "Missing.Script", false, [])]);
            var scene = d.BakeRuntime(d.Revision).Image.Instantiate(d.Components); int called = 0;
            using var owner = new RuntimeSessionOwner();
            Reject(() => owner.StartPlay(scene, factory: s => { called++; return new(s); }, compose: _ => { called++; return null; }));
            Check(called == 0 && owner.Play is null);
        });
        yield return ("E6 binding closure rejects renamed and mismatched Exports", () => {
            using var catalog = new ScriptCatalogService();
            using (var candidate = catalog.LoadCandidate(assembly)) catalog.CommitCandidate(candidate);
            var d = new SceneDocument(); var id = d.CreateObject("Exports");
            foreach (var export in new[] { new ExportData("Absent", ExportKind.Integer, 1), new ExportData("Multiplier", ExportKind.Float, 1) }) {
                d.SetBindings(id, [new(Guid.NewGuid(), catalog.Snapshot.Types.Single().TypeName, false, [export])]);
                Reject(() => catalog.ResolveBindings(d.BakeRuntime(d.Revision).Image));
            }
        });
        yield return ("E6 runtime composition cannot replace the preflight script catalog", () => {
            using var catalog = new ScriptCatalogService(); using var owner = new RuntimeSessionOwner(catalog);
            var d = new SceneDocument(); var scene = d.BakeRuntime(d.Revision).Image.Instantiate(d.Components);
            Reject(() => owner.StartPlay(scene, compose: _ => {
                using var candidate = catalog.LoadCandidate(assembly); catalog.CommitCandidate(candidate); return null;
            }));
            Check(owner.Play is null);
        });
        yield return ("E6 empty scene closure has no assets or 3D acquisition", () => {
            var d = new SceneDocument(); d.CreateObject("Logic"); var image = d.BakeRuntime(d.Revision).Image;
            var a = SceneAssetPreparation.ResolveDependencies(image.CopySnapshot(), null);
            var b = SceneAssetPreparation.ResolveDependencies(image.CopySnapshot(), null);
            Check(a.CopyRoots().Length == 0 && a.CopyMembers().Length == 0 && a.LeaseIdentity == Guid.Empty && a.ContentHash == b.ContentHash);
            using var owner = new EditorSessionOwner("Empty"); owner.StartPlay();
            Check(owner.PlayDependencies is { } receipt && receipt.Assets.CopyMembers().Length == 0 && receipt.Scripts.BindingCount == 0);
            owner.StopPlay(); Check(owner.PlayDependencies is null);
        });
        yield return ("E6 Editor rejects missing scene assets without publishing Play or freezing authoring", () => {
            using var owner = new EditorSessionOwner("Missing assets", components: global::Ncma.Runtime.CompositionRegistry.Build(global::Ncma.Scene.Rendering.Generated.ComponentModule.Module), validateComposition: SceneRenderValidation.RequireComposition);
            var d = owner.Document; var id = d.CreateObject("Mesh"); d.SetComponent(id, TransformData.Identity);
            d.SetComponent(id, new StaticMeshData(Guid.NewGuid(), Guid.NewGuid(), true, true, uint.MaxValue));
            owner.Edit!.Resynchronize(); byte[] before = d.CaptureBytes(); var state = owner.Edit.State;
            Reject(() => owner.StartPlay());
            Check(owner.Play is null && owner.PlayDependencies is null && owner.Edit.State == state && before.SequenceEqual(d.CaptureBytes()));
        });
    }
}
