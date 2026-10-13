using System.Numerics;
using Ncma.Assets;

namespace Ncma.Rendering.Scene.Examples;

// Copyable trusted-startup examples. No platform API, runtime code loader or permissions.
// The numeric program is borrowed from an explicitly checked NPH package, not compiled here.
public static class ExampleHdrGain
{
    public static readonly Guid ModuleId = Guid.Parse("7a14ee04-00d1-44b2-8844-000000000001");

    public static PostFeatureRegistration Registration() => new(ModuleId, 1, 50,
        [new("gain", 0, 3, Default: .25f)], [],
        (width, height, parameters, programs) => Build(width, height, parameters["gain"], programs),
        "Example linear HDR gain");

    // Insert before Bloom (order100). Combine samples the same linear input twice:
    // output = half(clamp(input * (1 + gain), 0, 65504)); it is NOT another Tone pass.
    public static PostProcessPlan? Build(uint width, uint height, float gain, PostProcessPackage programs)
    {
        if (width is 0 or > 4096 || height is 0 or > 4096 || !float.IsFinite(gain) || gain is < 0 or > 3)
            throw new ArgumentException("Example gain dimensions/range.");
        if (gain == 0) return null;
        ArgumentNullException.ThrowIfNull(programs);
        var graph = new PostProcessGraph();
        var output = graph.AddTarget(new(width, height));
        graph.AddPass(new(BloomFeature.CombineProgram, PostProcessGraph.SceneHdr,
            PostProcessGraph.SceneHdr, output, new Vector4(gain, 0, 0, 0)));
        return graph.Compile(width, height, output, programs);
    }
}

// Replaces high-level geometry stage policy; shaders still come from the checked shader pair.
public sealed class ExampleAmbientStage(float ambient) : ISceneGeometryStage
{
    public void Add(RenderGraph graph, RenderResourceId? shadow, RenderResourceId hdr, RenderResourceId depth)
    {
        if (!float.IsFinite(ambient) || ambient is < 0 or > 1) throw new ArgumentException("Example ambient range.");
        graph.AddPass("Example geometry stage", RenderOperation.SceneGeometry,
            shadow is { } s ? [s] : [], [hdr, depth], 4, parameters: [ambient * .5f]);
    }
}

// Full graph replacement: owns the graph/resources/dependencies, never calls Scene3DPipeline.Build.
// It deliberately chooses a 512 shadow target. HDR Feature execution remains the shared public
// PostProcessGraph contract between geometry and the ONE final Tone; display UI comes afterwards.
public sealed class ExampleScenePipeline(float exposure, float ambient, bool shadows) : RenderPipeline
{
    public override RenderGraph Build(uint width, uint height)
    {
        if (width is 0 or > 4096 || height is 0 or > 4096 || !float.IsFinite(exposure) || exposure is < .01f or > 16 ||
            !float.IsFinite(ambient) || ambient is < 0 or > 1) throw new ArgumentException("Example scene dimensions/parameters.");
        var graph = new RenderGraph();
        RenderResourceId? shadow = shadows ? graph.AddResource(new("Example shadow", RenderRole.SceneShadow,
            RenderFormat.Depth32, RenderUsage.Sampled | RenderUsage.DepthTarget, 512, 512)) : null;
        var hdr = graph.AddResource(new("Example HDR", RenderRole.SceneHdr, RenderFormat.Rgba16Float,
            RenderUsage.Sampled | RenderUsage.ColorTarget, width, height));
        var depth = graph.AddResource(new("Example depth", RenderRole.SceneDepth, RenderFormat.Depth32,
            RenderUsage.Sampled | RenderUsage.DepthTarget, width, height));
        var output = graph.AddResource(new("Example output", RenderRole.SceneOutput, RenderFormat.Rgba8,
            RenderUsage.ColorTarget, width, height, Imported: true));
        if (shadow is { } s) graph.AddPass("Example shadow pass", RenderOperation.SceneShadow, [], [s], 4);
        new ExampleAmbientStage(ambient).Add(graph, shadow, hdr, depth);
        graph.AddPass("Example final Tone", RenderOperation.SceneTone, [hdr, depth], [output], 4, parameters: [exposure]);
        graph.SetOutput(output);
        return graph;
    }
}

public static class SceneCustomizationExamples
{
    // Pass either method group as SceneRenderSession.pipelineFactory at trusted startup.
    // There is no serialized delegate and no Agent request that can supply one.
    public static RenderPipeline ReplaceGeometryStage(float exposure, float ambient, bool shadows) =>
        new Scene3DPipeline(exposure, ambient, shadows: shadows, geometry: new ExampleAmbientStage(ambient));
    public static RenderPipeline ReplacePipeline(float exposure, float ambient, bool shadows) =>
        new ExampleScenePipeline(exposure, ambient, shadows);

    public static PostProcessModules Modules() => new(
        [new(PostProcessLibrarySelection.AcesModule, 1, "ACES fitted"),
         new(PostProcessLibrarySelection.ReinhardModule, 1, "Reinhard"),
         new(PostProcessLibrarySelection.LinearModule, 1, "Linear Clamp")],
        [ExampleHdrGain.Registration(),
         new(PostProcessLibrarySelection.BloomModule, 1, 100,
            [new("levels", 1, 6, true, 4), new("threshold", 0, 16, Default: 1),
             new("softKnee", 0, 1, Default: .5f), new("intensity", 0, 4, Default: .08f), new("scatter", 0, 1, Default: .7f)], [],
            (w, h, p, bytes) => BloomFeature.Build(w, h, new(Levels: (int)p["levels"], Threshold: p["threshold"],
                SoftKnee: p["softKnee"], Intensity: p["intensity"], Scatter: p["scatter"]), bytes), "Bloom")]);
}
