using Ncma.Editor.Services;
using Ncma.Scene;

namespace Ncma.Editor.App;

// Authoring provenance is not an ECS World identity or a permission grant.
internal readonly record struct EditorSceneSourceStamp(Guid EditSession, Guid DocumentSession,
    ulong DocumentGeneration, ulong Revision)
{
    internal static EditorSceneSourceStamp Read(EditorSessionOwner owner) =>
        new(owner.Edit!.SessionId, owner.Document.SessionId, owner.Edit.DocumentGeneration, owner.Document.Revision);
    internal bool Matches(EditorSessionOwner owner) => this == Read(owner);
}

// Replaceable CPU derivation used ONLY by edit-mode presentation. No behaviours, solver,
// live mirroring or write-back. A changed source is explicitly baked into a new instance.
// GPU borrowers own their lifetimes separately; the host retains failed candidates until close.
internal sealed class EditorSceneProjection
{
    internal EditorSceneSourceStamp Source { get; }
    internal RuntimeSceneInstance Scene { get; }
    private readonly Guid _world;
    private readonly ulong _revision;
    private EditorSceneProjection(EditorSceneSourceStamp source, RuntimeSceneInstance scene)
    { Source = source; Scene = scene; _world = scene.World.Identity; _revision = scene.World.Revision; }
    internal static EditorSceneProjection Bake(EditorSessionOwner owner)
    {
        var source = EditorSceneSourceStamp.Read(owner);
        var baked = owner.Document.BakeRuntime(source.Revision);
        var result = new EditorSceneProjection(source, baked.Image.Instantiate(owner.Document.Components));
        result.RequireCurrent(owner);
        return result;
    }
    internal bool IsCurrent(EditorSessionOwner owner)
    {
        if (!Source.Matches(owner) || Scene.World.Identity != _world ||
            Scene.World.Revision != _revision || Scene.World.Tick != 0) return false;
        // Reuse the checked public boundary; do not expose the runtime's internal guard.
        try { Ncma.Scene.Rendering.SceneEnvironmentState.VerifyBoundary(Scene.World, _world); return true; }
        catch (InvalidOperationException) { return false; }
    }
    internal void RequireCurrent(EditorSessionOwner owner)
    { if (!IsCurrent(owner)) throw new InvalidOperationException("Stale or mutated editor scene projection."); }
}

internal sealed unsafe partial class CandidatePresentation
{
    private EditorSceneProjection? _editProjection, _pendingEditProjection;
    private EditorSceneProjection? CurrentEditProjection =>
        _editor is not null && _editProjection?.IsCurrent(_editor) == true ? _editProjection : null;
    private EditorSceneProjection CreateOrReuseEditProjection() => CurrentEditProjection ??
        EditorSceneProjection.Bake(_editor ?? throw new InvalidOperationException("Editor not started."));
    private Ncma.Runtime.World CurrentEditWorld => CurrentEditProjection?.Scene.World ??
        throw new InvalidOperationException("Current authoring projection not prepared.");
    private Ncma.Rendering.Scene.SceneRenderSession? CurrentSceneForPresentation()
    {
        RequireGraphicsReady();
        if (!_sceneSynchronizationOk || _editor is null) return null;
        if (_editor.Play is {} play)
            return play.State != Ncma.Gameplay.PlayState.Faulted && _playScene?.WorldId == play.Scene.World.Identity ? _playScene : null;
        return CurrentEditProjection is {} projection && _editScene?.WorldId == projection.Scene.World.Identity ? _editScene : null;
    }
}
