using Ncma.Application;
using Ncma.Rendering;
using Ncma.Scene;

namespace Ncma.Player.App;

public sealed partial class PlayerRunner
{
    // Cold input boundary only. The authoring object never enters Player ownership or its loop.
    // M9 packaging can reuse the same explicit conversion rather than a second gameplay model.
    private static (RuntimeSceneImage Image, Ncma.Runtime.ComponentRegistry Components) LoadRuntimeScene(ProjectContext project)
    {
        var source = new SceneDocument(project.Configuration.Name,
            global::Ncma.Runtime.CompositionRegistry.Build(global::NcmaPlayer.Generated.ComponentModule.Module),
            Ncma.Characters.CharacterComponents.RequireComposition);
        SceneDocumentFiles.Load(source, project.StartupScenePath);
        var baked = source.BakeRuntime(source.Revision);
        return (baked.Image, source.Components);
    }
}
