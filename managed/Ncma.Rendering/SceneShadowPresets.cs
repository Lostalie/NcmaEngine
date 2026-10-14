using Ncma.Assets;
namespace Ncma.Rendering;

// Logical costs of the official single-map shader, not driver VRAM, texture transactions
// or a promise about arbitrary registered user shaders. PCSS here is an approximation.
public readonly record struct SceneShadowBudget(uint Resolution, ulong DepthTextureBytes,
    int MaximumDepthReadsPerShadedPixel, int BlockerSearchReads);

public sealed class SceneShadowPreset
{
    public SceneShadowQuality Quality { get; }
    public SceneShadowSettings Settings { get; }
    public SceneShadowBudget Budget { get; }
    public bool Enabled => Quality != SceneShadowQuality.Off;
    // Budget is only valid when the matching official geometry pixel program is bound.
    public bool RequiresHighShader => Quality == SceneShadowQuality.High;

    internal SceneShadowPreset(SceneShadowQuality quality, uint resolution, bool pcss, float radius)
    {
        Quality = quality;
        Settings = new SceneShadowSettings(.0005f, .002f, pcss, radius) { Distance = 40 };
        Budget = new(resolution, checked(4UL * resolution * resolution),
            resolution == 0 ? 0 : pcss ? SceneShadowShaders.HighMaximumDepthReads : 9, resolution != 0 && pcss ? 9 : 0);
    }

    public SceneShadowBudget EffectiveBudget(bool lightCastsShadow) =>
        lightCastsShadow ? Budget : default;

    // The caller must use the same effective shadow presence when selecting its shader
    // variant and caster batch. No allocation or renderer/plugin load occurs here.
    public RenderPipeline CreatePipeline(bool lightCastsShadow, float exposure = 1, float ambient = .03f,
        IReadOnlyList<ScenePipelineFeature>? features = null, ISceneGeometryStage? geometry = null,
        ISceneToneStage? tone = null, ShaderProfile profile = ShaderProfile.Scene3D)
    {
        if (profile is not (ShaderProfile.Scene3D or ShaderProfile.SceneEnvironment))
            throw new ArgumentException("Scene shadow preset profile.", nameof(profile));
        var pipeline = new Scene3DPipeline(exposure, ambient, Budget.Resolution, Enabled && lightCastsShadow, features, geometry, tone);
        return RequiresHighShader && lightCastsShadow ? new RequiredPixelPipeline(pipeline, SceneShadowShaders.CreateHighPixel(profile)) : pipeline;
    }

    private sealed class RequiredPixelPipeline(RenderPipeline source, ShaderDescriptor pixel) : RenderPipeline
    {
        public override RenderGraph Build(uint width, uint height)
        { var graph = source.Build(width, height); graph.RequireSceneGeometryPixel(pixel); return graph; }
    }
}

public static class SceneShadowPresets
{
    private static readonly SceneShadowPreset Off = new(SceneShadowQuality.Off, 0, false, 3);
    private static readonly SceneShadowPreset Low = new(SceneShadowQuality.Low, 512, false, 3);
    private static readonly SceneShadowPreset Medium = new(SceneShadowQuality.Medium, 1024, false, 3);
    private static readonly SceneShadowPreset High = new(SceneShadowQuality.High, 2048, true, 8);

    public static SceneShadowPreset Get(SceneShadowQuality quality) => quality switch
    {
        SceneShadowQuality.Off => Off,
        SceneShadowQuality.Low => Low,
        SceneShadowQuality.Medium => Medium,
        SceneShadowQuality.High => High,
        _ => throw new ArgumentOutOfRangeException(nameof(quality), "Unknown scene shadow quality.")
    };
}
