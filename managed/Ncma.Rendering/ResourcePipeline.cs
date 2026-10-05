using System.Numerics;
namespace Ncma.Rendering;
public readonly record struct ResourcePipelineSettings(float Exposure = 1, float Ambient = .03f, ResourceDrawMode Mode = ResourceDrawMode.Pbr);
public abstract class ResourcePipelineFeature { public abstract ResourcePipelineSettings Apply(ResourcePipelineSettings settings); }
public interface IResourceGeometryStage { void Add(RenderGraph graph, RenderResourceId output, ResourcePipelineSettings settings); }
public sealed class ResourceGeometryStage : IResourceGeometryStage
{
    public void Add(RenderGraph graph, RenderResourceId output, ResourcePipelineSettings settings) => graph.AddPass("Typed PBR geometry", RenderOperation.ResourceGeometry, [], [output], 3, parameters: [settings.Exposure, settings.Ambient, (float)settings.Mode, 0]);
}
public sealed class ResourceExposureFeature(float exposure) : ResourcePipelineFeature
{ public override ResourcePipelineSettings Apply(ResourcePipelineSettings settings) => settings with { Exposure = exposure }; }
// Defaults and trusted user extensions use the exact same public graph, stage and capability validator.
// No arbitrary shader/DLL input; the v3 GPU stage currently has no shadow/IBL or multi-pass composition.
public sealed class ResourceMeshPipeline(ResourcePipelineSettings? settings = null, IReadOnlyList<ResourcePipelineFeature>? features = null, IResourceGeometryStage? stage = null) : RenderPipeline
{
    public override RenderGraph Build(uint width, uint height)
    {
        var configuration = settings ?? new ResourcePipelineSettings(1, .03f);
        if (features is not null) { if (features.Count > 16) throw new ArgumentException("Feature budget."); foreach (var feature in features) configuration = feature.Apply(configuration); }
        var graph = new RenderGraph(); var output = graph.AddResource(new("Typed output", RenderRole.ResourceOutput, RenderFormat.Rgba8, RenderUsage.ColorTarget, width, height, Imported: true));
        (stage ?? new ResourceGeometryStage()).Add(graph, output, configuration); graph.SetOutput(output); return graph;
    }
}
public sealed class ResourcePipelineSession
{
    private readonly RendererSession _renderer;
    public CompiledRenderGraph Plan { get; }
    public ResourcePipelineSession(RendererSession renderer, RenderPipeline pipeline, uint width, uint height)
    { _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer)); _ = renderer.Handle; ArgumentNullException.ThrowIfNull(pipeline); Plan = pipeline.Build(width, height).Compile(RenderCapabilities.ResourceDx11); if (!Plan.RequiresResourceService || Plan.RequiresReferenceResources) throw new ArgumentException("Resource service graph required."); }
    public void Submit(ulong frame, ReadOnlySpan<ResourceDraw> draws, ResourceLighting lighting, GpuViewTarget? target = null, Vector4 clear = default)
    {
        if (target is not null && (target.Width != Plan.Width || target.Height != Plan.Height)) throw new ArgumentException("Target/compiled graph dimensions differ.");
        var parameters = Plan.ResourceParameters;
        _renderer.SubmitResources(frame, draws, new(0, 0, Plan.Width, Plan.Height), clear, lighting with { Exposure = parameters[0], Ambient = parameters[1] }, target, (ResourceDrawMode)(uint)parameters[2]);
    }
}
