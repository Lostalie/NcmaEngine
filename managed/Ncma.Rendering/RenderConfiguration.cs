using System.Text.Json.Serialization;
using Ncma.Runtime;
namespace Ncma.Rendering;
// Serializable values only. Explicitly registered type; no GPU handle/COM pointer/assembly path.
public readonly record struct RenderConfiguration(
    [property: JsonRequired] int Version,
    [property: JsonRequired] Guid ConfigurationId,
    [property: JsonRequired] string PipelineType,
    [property: JsonRequired] float Exposure,
    [property: JsonRequired] float Metallic,
    [property: JsonRequired] float Roughness,
    [property: JsonRequired] float ToneExposureOverride,
    [property: JsonRequired] float FeatureExposure,
    [property: JsonRequired] bool ReplaceToneStage,
    [property: JsonRequired] float ClearRed,
    [property: JsonRequired] float ClearGreen,
    [property: JsonRequired] float ClearBlue) : IComponent
{
    public float BaseRed { get; init; } = .32f;
    public float BaseGreen { get; init; } = .16f;
    public float BaseBlue { get; init; } = .82f;
    public float LightIntensity { get; init; } = 4;
    public float Ambient { get; init; } = .035f;
    public bool ShadowEnabled { get; init; } = true;
    public int ShadowFilter { get; init; } = 2;
    public float ConstantBias { get; init; } = .0005f;
    public float SlopeBias { get; init; } = 1.5f;
    public float ShadowDistance { get; init; } = 40;
    public float CascadeLambda { get; init; } = .75f;
    public float LightRadius { get; init; } = .05f;
    public bool ContactEnabled { get; init; } = true;
    public int ContactSteps { get; init; } = 16;
    public float ContactDistance { get; init; } = .8f;
    public float ContactThickness { get; init; } = .08f;
    public float ContactStrength { get; init; } = .65f;
    public const string ComponentType = "ncma.render.configuration";
    public static RenderConfiguration Default(Guid id) => new(1,id,"ncma.reference_preview.v1",1,.35f,.28f,0,0,false,.1f,.2f,.3f);
    public static RenderConfiguration Validate(RenderConfiguration config)
    {
        if(config.Version!=1 || config.ConfigurationId==Guid.Empty || config.PipelineType is not ("ncma.reference_preview.v1" or "ncma.clear.v1"))
            throw new ArgumentException("Unsupported registered pipeline identity/version.");
        float[] values=[config.Exposure,config.Metallic,config.Roughness,config.ToneExposureOverride,config.FeatureExposure,config.ClearRed,config.ClearGreen,config.ClearBlue];
        if(values.Any(v=>!float.IsFinite(v)) || config.Exposure is <.01f or >16 || config.Metallic is <0 or >1 || config.Roughness is <.04f or >1 ||
            (config.ToneExposureOverride!=0 && config.ToneExposureOverride is <.01f or >16) ||
            (config.FeatureExposure!=0 && config.FeatureExposure is <.01f or >16) ||
            config.ClearRed is <0 or >1 || config.ClearGreen is <0 or >1 || config.ClearBlue is <0 or >1)
            throw new ArgumentException("Rendering configuration outside finite supported ranges.");
        _=config.CreatePipeline().Build(1,1).Compile(RenderCapabilities.ReferenceDx11);
        static bool Range(float value,float min,float max)=>float.IsFinite(value)&&value>=min&&value<=max;
        if(!Range(config.BaseRed,0,1)||!Range(config.BaseGreen,0,1)||!Range(config.BaseBlue,0,1)||!Range(config.LightIntensity,0,20)||!Range(config.Ambient,0,.25f)||
            config.ShadowFilter is <0 or >2||config.ContactSteps is <4 or >32||!Range(config.ConstantBias,0,.01f)||!Range(config.SlopeBias,0,8)||
            !Range(config.ShadowDistance,5,100)||!Range(config.CascadeLambda,0,1)||!Range(config.LightRadius,.001f,.2f)||
            !Range(config.ContactDistance,.05f,3)||!Range(config.ContactThickness,.005f,.3f)||!Range(config.ContactStrength,0,1))
            throw new ArgumentException("Reference lighting/shadow configuration outside finite supported ranges.");
        return config;
    }
    public RenderPipeline CreatePipeline() => PipelineType switch {
        "ncma.reference_preview.v1" => new ReferencePreviewPipeline(ToneExposureOverride,
            FeatureExposure==0?null:[new ExposureFeature(FeatureExposure)],
            ReplaceToneStage?new ExposureToneStage(ToneExposureOverride==0?Exposure:ToneExposureOverride):null),
        "ncma.clear.v1" => new ClearPipeline(ClearRed,ClearGreen,ClearBlue),
        _ => throw new ArgumentException("Pipeline not registered.") };
    public static ComponentRegistry CreateRegistry()
    {
        var registry=ComponentRegistry.CreateDefault();
        registry.Register<RenderConfiguration>(ComponentType,1,Schema,Validate);return registry;
    }
    public const string Schema="""
        {"type":"object","additionalProperties":false,"required":["version","configurationId","pipelineType","exposure","metallic","roughness","toneExposureOverride","featureExposure","replaceToneStage","clearRed","clearGreen","clearBlue"],
        "properties":{"version":{"type":"integer"},"configurationId":{"type":"string"},"pipelineType":{"type":"string"},
        "exposure":{"type":"number"},"metallic":{"type":"number"},"roughness":{"type":"number"},"toneExposureOverride":{"type":"number"},
        "featureExposure":{"type":"number"},"replaceToneStage":{"type":"boolean"},"clearRed":{"type":"number"},"clearGreen":{"type":"number"},"clearBlue":{"type":"number"},
        "baseRed":{"type":"number"},"baseGreen":{"type":"number"},"baseBlue":{"type":"number"},"lightIntensity":{"type":"number"},"ambient":{"type":"number"},
        "shadowEnabled":{"type":"boolean"},"shadowFilter":{"type":"integer"},"constantBias":{"type":"number"},"slopeBias":{"type":"number"},"shadowDistance":{"type":"number"},
        "cascadeLambda":{"type":"number"},"lightRadius":{"type":"number"},"contactEnabled":{"type":"boolean"},"contactSteps":{"type":"integer"},"contactDistance":{"type":"number"},"contactThickness":{"type":"number"},"contactStrength":{"type":"number"}}}
        """;
}
// Owns GPU plan lifetime, not World/history. Caller presents copied configuration at owner boundary.
public sealed class RenderPipelineService : IDisposable
{
    private readonly RendererSession _renderer;
    private RenderResources? _resources;
    private CompiledRenderGraph? _plan;
    private RenderConfiguration _configuration;
    private uint _width,_height;
    private ulong _generation;
    private ulong _appliedSettingsGeneration=ulong.MaxValue;
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private bool _disposed;
    public ulong Generation { get {Verify();return _generation;} }
    public ulong PlanBuilds { get; private set; }
    public CompiledRenderGraph Plan { get {Verify();return _plan??throw new InvalidOperationException("No active pipeline.");} }
    public RenderPipelineService(RendererSession renderer) { _renderer=renderer; }
    private void Verify() {if(Environment.CurrentManagedThreadId!=_owner)throw new InvalidOperationException("Render owner thread required.");ObjectDisposedException.ThrowIf(_disposed,this);}
    public void Configure(RenderConfiguration configuration,uint width,uint height)
    {
        Verify();
        if(_plan is not null && _configuration==configuration && _width==width && _height==height)return;
        configuration=RenderConfiguration.Validate(configuration);
        if(_plan is not null && _width==width && _height==height && _configuration.PipelineType==configuration.PipelineType &&
           _configuration.ToneExposureOverride==configuration.ToneExposureOverride && _configuration.FeatureExposure==configuration.FeatureExposure &&
           _configuration.ReplaceToneStage==configuration.ReplaceToneStage &&
           _configuration.ClearRed==configuration.ClearRed && _configuration.ClearGreen==configuration.ClearGreen && _configuration.ClearBlue==configuration.ClearBlue &&
           !configuration.ReplaceToneStage) {
            // Model/material/exposure uniforms do not change graph topology or GPU resources.
            _configuration=configuration;_generation=checked(_generation+1);return;
        }
        // Candidate graph/shaders/resources validated before replacing any currently active plan.
        var candidate=configuration.CreatePipeline().Build(width,height).Compile(_renderer.Capabilities);
        RenderResources? resources=null;
        try {
            if(candidate.RequiresReferenceResources)resources=_renderer.CreateReferenceResources();
            _renderer.WaitIdle(); // Safe synchronous retirement; no async aliasing/recovery claimed.
            _resources?.Dispose();
        } catch {resources?.Dispose();throw;}
        _resources=resources;_plan=candidate;_configuration=configuration;_width=width;_height=height;
        _generation=checked(_generation+1);PlanBuilds++;
    }
    public unsafe void Submit(ulong frame,float x=0,float y=0,ReadOnlySpan<float> model=default)
    {
        Verify();var data=RenderFrame.Reference(frame,_width,_height,_configuration.Exposure);
        if(_resources is not null && _appliedSettingsGeneration!=_generation) {
            _renderer.ConfigureReference(_resources,ReferenceSettings.From(_configuration));
            _appliedSettingsGeneration=_generation;
        }
        data.Viewport[0]=x;data.Viewport[1]=y;data.Metallic=_configuration.Metallic;data.Roughness=_configuration.Roughness;
        if(!model.IsEmpty){if(model.Length!=16)throw new ArgumentException("Column-major model requires 16 values.");for(int i=0;i<16;i++)data.Model[i]=model[i];}
        _renderer.Submit(Plan,_resources,data);
    }
    public void Dispose() {if(_disposed)return;Verify();_resources?.Dispose();_resources=null;_plan=null;_disposed=true;}
}
