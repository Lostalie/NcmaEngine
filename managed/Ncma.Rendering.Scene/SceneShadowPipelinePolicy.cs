using Ncma.Assets;
using Ncma.Scene.Rendering;

namespace Ncma.Rendering.Scene;

// One shared public policy for default/stage/full pipelines. Custom graphs are still built
// through their trusted callback; explicit scene presets cannot silently change allocation.
public static class SceneShadowPipelinePolicy
{
    public static bool EffectiveShadows(SceneShadowData? selection,bool lightCastsShadow)
    {
        if(selection is {} value)SceneShadowData.Validate(value);
        return lightCastsShadow&&selection?.Quality!=SceneShadowQuality.Off;
    }
    public static SceneShadowSettings Settings(SceneShadowData? selection,SceneShadowSettings? custom = null)
    {
        var preset=SceneShadowPresets.Get(selection is {} selected?SceneShadowData.Validate(selected).Quality:SceneShadowQuality.Medium);
        if(selection is not null && custom is {} supplied && supplied!=preset.Settings)throw new ArgumentException("Explicit shadow preset cannot be overridden by per-submit settings.");
        return custom??preset.Settings;
    }
    public static RenderPipeline Create(Func<float,float,bool,RenderPipeline>? factory,float exposure,float ambient,
        bool shadows,SceneShadowData? selection,ShaderProfile profile)
    {
        if(profile is not (ShaderProfile.Scene3D or ShaderProfile.SceneEnvironment))throw new ArgumentException("Scene shadow pipeline profile.",nameof(profile));
        var preset=SceneShadowPresets.Get(selection is {} selected?SceneShadowData.Validate(selected).Quality:SceneShadowQuality.Medium);
        if(shadows&&!preset.Enabled)throw new ArgumentException("Off scene cannot contain a shadow pass.");
        if(factory is null)return preset.CreatePipeline(shadows,exposure,ambient,profile:profile);
        var source=factory(exposure,ambient,shadows)??throw new ArgumentException("Null custom scene pipeline.");
        return selection is null?source:new SelectedPipeline(source,preset,shadows,profile);
    }
    private sealed class SelectedPipeline(RenderPipeline source,SceneShadowPreset preset,bool shadows,ShaderProfile profile):RenderPipeline
    {
        public override RenderGraph Build(uint width,uint height)
        {
            var graph=source.Build(width,height)??throw new ArgumentException("Null custom scene graph.");
            if(shadows&&preset.RequiresHighShader)graph.RequireSceneGeometryPixel(SceneShadowShaders.CreateHighPixel(profile));
            var plan=graph.Compile(RenderCapabilities.SceneDx11);var shadow=plan.Resources.SingleOrDefault(r=>r.Role==RenderRole.SceneShadow);
            if(!plan.RequiresSceneService || (shadow is not null)!=shadows || shadow is not null&&(shadow.Width!=preset.Budget.Resolution||shadow.Height!=preset.Budget.Resolution))
                throw new ArgumentException("Custom scene graph must match the explicit shadow preset resolution/presence.");
            return graph;
        }
    }
}
