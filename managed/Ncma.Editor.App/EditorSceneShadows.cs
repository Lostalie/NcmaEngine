using Ncma.Assets;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;
namespace Ncma.Editor.App;

internal sealed unsafe partial class CandidatePresentation
{
    private RuntimeShadowLibrary? _shadowLibrary;
    private PreparedShadowLibrary? _highShadows;
    private PreparedShadowLibrary? PrepareShadowRuntime(World world,ISceneContent snapshot)
    {
        using var read=SceneEnvironmentState.BeginRead(world,world.Identity);
        var value=SceneShadowState.Read(snapshot);
        if(value!=SceneShadowState.Read(world,world.Identity))throw new ArgumentException("Stale editor shadow selection.");
        if(value?.Quality!=SceneShadowQuality.High||!ScenePostProcessPreflight.HasGeometry(snapshot)||_renderer is null)return null;
        if(_highShadows is not null)return _highShadows;
        var files=_shaderFiles??throw new ArgumentException("High shadows require checked scene shader files.");
        var library=_shadowLibrary??RuntimeShadowLibrary.ForHost(AppContext.BaseDirectory,project?.Root,project?.Configuration.ShadowLibrary);
        try{var prepared=library.Prepare(_renderer,files,()=>!_closed);_shadowLibrary=library;_highShadows=prepared;return prepared;}
        catch{if(!ReferenceEquals(library,_shadowLibrary))library.Dispose();throw;}
    }
}
