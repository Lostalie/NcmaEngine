using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;
namespace Ncma.Rendering.Scene;

public static class ScenePostProcessPreflight
{
    private static readonly ComponentRegistry RenderValues=RenderConfiguration.CreateRegistry();
    public static bool HasSettings(ISceneContent scene)=>scene.Objects.Any(o=>o.Components.Any(c=>c.TypeId==PostProcessData.TypeId));
    public static bool HasGeometry(ISceneContent scene)=>scene.Objects.Any(o=>o.Components.Any(c=>c.TypeId is StaticMeshData.TypeId or SkinnedMeshData.TypeId));
    public static void ValidateValues(ISceneContent scene,PostProcessModules modules)
    {
        var value=modules.Validate(ScenePostProcessState.Read(scene));if(!value.Enabled)return;
        if(!HasGeometry(scene))throw new ArgumentException("Post effects require a 3D scene.");
        var settings=scene.Objects.SelectMany(o=>o.Components).Where(c=>c.TypeId==RenderConfiguration.ComponentType).Take(2).ToArray();
        if(settings.Length>1)throw new ArgumentException("Ambiguous render configuration.");
        float exposure=settings.Length==0?1:RenderValues.Decode<RenderConfiguration>(settings[0]).Exposure;
        float effective=exposure*SceneTonePresets.ExposureFromStops(value.ExposureEv);
        if(!float.IsFinite(effective)||effective is <.01f or >16)throw new ArgumentException("Combined Tone exposure must remain within [.01,16].");
    }
    public static void ValidateFiles(ISceneContent scene,RuntimePostProcessLibrary library)
    {
        var value=ScenePostProcessState.Read(scene);bool skin=scene.Objects.Any(o=>o.Components.Any(c=>c.TypeId==SkinnedMeshData.TypeId));
        var profile=SceneEnvironmentState.Read(scene).Enabled?ShaderProfile.SceneEnvironment:ShaderProfile.Scene3D;
        _=library.Tone(value.ToneModule,profile,false,skin);_=library.Tone(value.ToneModule,profile,true,skin);
        foreach(var feature in PostProcessData.DecodeStack(value.StackJson))_=library.Program(feature.ModuleId);
    }
}
