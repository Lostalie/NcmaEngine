using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;
namespace Ncma.Editor.App;

internal sealed unsafe partial class CandidatePresentation
{
    private readonly PostProcessModules _postModules=postProcessModules??PostProcessModules.Official();
    private RuntimePostProcessLibrary? _postLibrary;
    private PostProcessRuntime? _postRuntime;
    private PostProcessRuntime? PreparePostRuntime(World world,SceneDocumentSnapshot snapshot)
    {
        using var read=SceneEnvironmentState.BeginRead(world,world.Identity);
        ScenePostProcessPreflight.ValidateValues(snapshot,_postModules);
        if(!ScenePostProcessPreflight.HasSettings(snapshot)||!ScenePostProcessPreflight.HasGeometry(snapshot))return null;
        if(_renderer is null){if(ScenePostProcessState.Read(snapshot).Enabled)throw new NotSupportedException("Post processing requires graphical scene rendering.");return null;}
        if(_postRuntime is not null){ScenePostProcessPreflight.ValidateFiles(snapshot,_postLibrary!);return _postRuntime;}
        var files=_shaderFiles??throw new ArgumentException("Post processing requires checked scene shader files.");
        var library=RuntimePostProcessLibrary.ForHost(AppContext.BaseDirectory,project?.Root,project?.Configuration.PostProcessLibrary);
        try{ScenePostProcessPreflight.ValidateFiles(snapshot,library);_postRuntime=new(_renderer,files,library,_postModules,()=>!_closed);_postLibrary=library;return _postRuntime;}
        catch{library.Dispose();throw;}
    }
    private void PreflightPost(World world,SceneDocumentSnapshot scene)
    {
        var runtime=PreparePostRuntime(world,scene);
        var high=PrepareShadowRuntime(world,scene);
        if(_renderer is not null){
            ScenePostProcessPreflight.Prepare(_renderer,world,scene,runtime);
            ScenePipelineStartup.Prepare(_renderer,world,scene,pipelineFactory,runtime,()=>!_closed,high);
        }
    }
}
