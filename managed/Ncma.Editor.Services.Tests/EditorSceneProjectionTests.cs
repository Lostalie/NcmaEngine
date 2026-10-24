using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static class EditorSceneProjectionTests
{
    private static void Check(bool condition)
    { if (!condition) throw new InvalidOperationException("E6 editor projection assertion"); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected stale projection rejection");
    }
    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("E6 edit preview bake is detached, read-only and never starts Play", () => {
            using var owner = new EditorSessionOwner("Projection");
            var workspace = new EditorWorkspace(owner); Guid id = workspace.CreateObject(workspace.Stamp);
            byte[] before = owner.Document.CaptureBytes(); var state = owner.Edit!.State;
            var first = EditorSceneProjection.Bake(owner); var second = EditorSceneProjection.Bake(owner);
            Check(first.IsCurrent(owner) && second.IsCurrent(owner) && first.Source == second.Source);
            Check(first.Scene.SessionId != second.Scene.SessionId && first.Scene.World.Identity != second.Scene.World.Identity);
            Check(first.Scene.World.GetUuid(first.Scene.World.FindEntity(id)) == id);
            Check(first.Scene.ImageHash == second.Scene.ImageHash && first.Scene.World.Tick == 0);
            Check(owner.Play is null && state == owner.Edit.State && before.SequenceEqual(owner.Document.CaptureBytes()));
        });
        yield return ("E6 document transaction and exact Undo do not revive stale preview", () => {
            using var owner = new EditorSessionOwner("Projection");
            var workspace = new EditorWorkspace(owner); Guid id = workspace.CreateObject(workspace.Stamp);
            byte[] before = owner.Document.CaptureBytes(); var old = EditorSceneProjection.Bake(owner);
            workspace.Transaction(workspace.Stamp, "Rename", [new { op = "rename", objectId = id, name = "Changed" }]);
            Check(!old.IsCurrent(owner)); Reject(() => old.RequireCurrent(owner));
            var changed = EditorSceneProjection.Bake(owner); Check(changed.IsCurrent(owner));
            Check(changed.Scene.World.GetName(changed.Scene.World.FindEntity(id)) == "Changed");
            workspace.History(workspace.Stamp, false);
            Check(before.SequenceEqual(owner.Document.CaptureBytes()) && !old.IsCurrent(owner) && !changed.IsCurrent(owner));
            var restored = EditorSceneProjection.Bake(owner); Check(restored.IsCurrent(owner));
            Check(restored.Scene.ImageHash == old.Scene.ImageHash && restored.Scene.World.Identity != old.Scene.World.Identity);
        });
        yield return ("E6 preview runtime mutation and clock advancement cannot write back", () => {
            using var owner = new EditorSessionOwner("Projection");
            var workspace = new EditorWorkspace(owner); Guid id = workspace.CreateObject(workspace.Stamp);
            byte[] bytes = owner.Document.CaptureBytes(); var state = owner.Edit!.State;
            var preview = EditorSceneProjection.Bake(owner);
            preview.Scene.World.Set(preview.Scene.World.FindEntity(id), TransformData.Identity);
            Check(preview.Source.Matches(owner) && !preview.IsCurrent(owner)); Reject(() => preview.RequireCurrent(owner));
            var ticking = EditorSceneProjection.Bake(owner); new WorldRunner(ticking.Scene.World).Advance(1.0 / 60);
            Check(ticking.Scene.World.Tick == 1 && !ticking.IsCurrent(owner));
            Check(bytes.SequenceEqual(owner.Document.CaptureBytes()) && state == owner.Edit.State && owner.Play is null);
        });
        yield return ("E6 preview source stamp rejects foreign session and document replacement", () => {
            using var owner = new EditorSessionOwner("Projection"); using var foreign = new EditorSessionOwner("Projection");
            var workspace = new EditorWorkspace(owner); workspace.CreateObject(workspace.Stamp);
            var preview = EditorSceneProjection.Bake(owner);
            Check(!preview.Source.Matches(foreign)); Reject(() => preview.RequireCurrent(foreign));
            workspace.New(workspace.Stamp, true); Check(!preview.Source.Matches(owner));
            var next = EditorSceneProjection.Bake(owner);
            Check(next.Source.DocumentGeneration != preview.Source.DocumentGeneration && next.Scene.World.GetEntities().Count == 0);
        });
        yield return ("E6 preparation UI requires authoring provenance, not a document-as-World identity", () => {
            using var owner = new EditorSessionOwner("Projection");
            var workspace = new EditorWorkspace(owner); Guid id = workspace.CreateObject(workspace.Stamp);
            var view = new EditorPresenter(workspace, null); var preview = EditorSceneProjection.Bake(owner);
            var world = preview.Scene.World;
            view.ShowEnvironmentPreparation(world.Identity, world.Revision, Guid.Empty, true, false, EnvironmentLightingData.Off);
            view.ShowPostPreparation(world.Identity, world.Revision, Guid.Empty, true, false, PostProcessData.Off);
            Check(view.EnvironmentPreparationStatus.Contains("等待") && view.PostPreparationStatus.Contains("等待"));
            view.ShowEnvironmentPreparation(world.Identity, world.Revision, Guid.Empty, true, false, EnvironmentLightingData.Off, preview.Source);
            view.ShowPostPreparation(world.Identity, world.Revision, Guid.Empty, true, false, PostProcessData.Off, authoring: preview.Source);
            Check(view.EnvironmentPreparationStatus.Contains("关闭") && view.PostPreparationStatus.Contains("关闭"));
            workspace.Transaction(workspace.Stamp, "Rename", [new { op = "rename", objectId = id, name = "New source" }]);
            Check(view.EnvironmentPreparationStatus.Contains("等待") && view.PostPreparationStatus.Contains("等待"));
            var failed = EditorSceneSourceStamp.Read(owner);
            view.ShowEnvironmentPreparation(Guid.Empty, 0, Guid.Empty, false, true, null, failed);
            view.ShowPostPreparation(Guid.Empty, 0, Guid.Empty, false, true, null, authoring: failed);
            Check(view.EnvironmentPreparationStatus.Contains("失败") && view.PostPreparationStatus.Contains("失败"));
        });
    }
}
