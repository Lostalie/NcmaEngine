using Ncma.Scene;
using Ncma.Scene.Rendering;
using Ncma.Scripting;

namespace Ncma.Editor.Services;

// Local cold-start receipt only; no AI authority, no retained authoring document/World.
// UI/shader/physics host dependencies remain validated by their existing composition services.
public sealed record EditorPlayDependencies(SceneBakeStamp Source, string ImageHash,
    SceneAssetDependencyClosure Assets, ScriptBindingClosure Scripts);
