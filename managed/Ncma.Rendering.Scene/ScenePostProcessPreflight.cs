using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;
namespace Ncma.Rendering.Scene;

public static class ScenePostProcessPreflight
{
    private static readonly ComponentRegistry RenderValues=RenderConfiguration.CreateRegistry();
    public static bool HasSettings(SceneDocumentSnapshot scene)=>scene.Objects.Any(o=>o.Components.Any(c=>c.TypeId==PostProcessData.TypeId));
    public static bool HasGeometry(SceneDocumentSnapshot scene)=>scene.Objects.Any(o=>o.Components.Any(c=>c.TypeId is StaticMeshData.TypeId or SkinnedMeshData.TypeId));
    public static void ValidateValues(SceneDocumentSnapshot scene,PostProcessModules modules)
    {
        var value=modules.Validate(ScenePostProcessState.Read(scene));if(!value.Enabled)return;
        if(!HasGeometry(scene))throw new ArgumentException("Post effects require a 3D scene.");
        var settings=scene.Objects.SelectMany(o=>o.Components).Where(c=>c.TypeId==RenderConfiguration.ComponentType).Take(2).ToArray();
        if(settings.Length>1)throw new ArgumentException("Ambiguous render configuration.");
        float exposure=settings.Length==0?1:RenderValues.Decode<RenderConfiguration>(settings[0]).Exposure;
        float effective=exposure*SceneTonePresets.ExposureFromStops(value.ExposureEv);
        if(!float.IsFinite(effective)||effective is <.01f or >16)throw new ArgumentException("Combined Tone exposure must remain within [.01,16].");
    }
    public static void ValidateFiles(SceneDocumentSnapshot scene,RuntimePostProcessLibrary library)
    {
        var value=ScenePostProcessState.Read(scene);bool skin=scene.Objects.Any(o=>o.Components.Any(c=>c.TypeId==SkinnedMeshData.TypeId));
        var profile=SceneEnvironmentState.Read(scene).Enabled?ShaderProfile.SceneEnvironment:ShaderProfile.Scene3D;
        _=library.Tone(value.ToneModule,profile,false,skin);_=library.Tone(value.ToneModule,profile,true,skin);
        foreach(var feature in PostProcessData.DecodeStack(value.StackJson))_=library.Program(feature.ModuleId);
    }
    // Before gameplay: exact modules/parameters/files/native reflection and an actual bounded
    // binding. Viewport-dependent allocation is revalidated by the shared off-frame view path.
    public static void Prepare(RendererSession renderer,World world,SceneDocumentSnapshot scene,PostProcessRuntime? runtime)
    {
        using var read=SceneEnvironmentState.BeginRead(world,world.Identity);
        var value=ScenePostProcessState.Read(scene);
        if(value!=ScenePostProcessState.Read(world,world.Identity))throw new ArgumentException("Stale post preflight World.");
        if(!value.Enabled)return;
        if(runtime is null)throw new ArgumentException("Enabled post configuration requires a checked runtime.");
        bool skin=scene.Objects.Any(o=>o.Components.Any(c=>c.TypeId==SkinnedMeshData.TypeId));
        if(!skin&&!scene.Objects.Any(o=>o.Components.Any(c=>c.TypeId==StaticMeshData.TypeId)))throw new ArgumentException("Post effects require an explicit 3D scene; pure UI must not load them.");
        bool environment=SceneEnvironmentState.Read(scene).Enabled;
        var prepared=runtime.Prepare(value,16,16,environment,false,skin);
        using var binding=runtime.Install(()=>{
            var candidate=new ScenePipelineSession(renderer,new Scene3DPipeline(SceneTonePresets.ExposureFromStops(value.ExposureEv),shadows:false),16,16,shaders:prepared.Scene,environmentShaders:prepared.Environment);
            try{if(prepared.Plan is not null)candidate.ConfigurePostProcess(prepared.Plan,prepared.Programs,runtime.BindingAllowed);return candidate;}
            catch{candidate.Dispose();throw;}
        });
    }
}
