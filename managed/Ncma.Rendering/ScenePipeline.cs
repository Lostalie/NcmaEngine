using System.Numerics;
using System.Runtime.InteropServices;
using Ncma.Interop;
namespace Ncma.Rendering;
[StructLayout(LayoutKind.Sequential)] internal struct SceneDescriptionV4 { public uint Size, Width, Height, Resolution; }
[StructLayout(LayoutKind.Sequential)] internal struct ScenePassV4 { public uint Operation; public float Parameter, Reserved, Reserved2; }
[StructLayout(LayoutKind.Sequential)] internal unsafe struct SceneFrameV4 { public ResourceFrameV3 Base; public GpuMeshKey Pipeline; public uint Casters, Passes; public fixed float LightVP[16]; public fixed float Shadow[4]; }
[StructLayout(LayoutKind.Sequential)] internal struct SceneApiV4 { public uint Size, Version; public ulong Caps; public nint Create, Destroy, Submit, Stats; }
[StructLayout(LayoutKind.Sequential)] public struct ScenePipelineStats { public uint Size, MaxDraws; public ulong Generation, Pipelines, ResidentBytes, Creates, GeometryDraws, ShadowDraws, CopiedBytes, ConstantUploadBytes; }
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CreateSceneV4(ulong module, ulong renderer, SceneDescriptionV4* input, GpuMeshKey* output, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint SubmitSceneV4(ulong module, ulong renderer, SceneFrameV4* input, SceneGpuDraw* geometry, SceneGpuDraw* casters, ScenePassV4* passes, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint SceneStatsV4(ulong module, ulong renderer, ScenePipelineStats* output, PluginError* error);
[StructLayout(LayoutKind.Sequential)] public struct SceneGpuDraw
{
    internal ResourceDraw Draw;
    internal Vector4 Surface;
    public static SceneGpuDraw Create(GpuMesh mesh, GpuMaterial material, MeshDrawRange range, Matrix4x4 model, Matrix4x4 viewProjection, bool overrideScalars = false, float metallic = 0, float roughness = .5f, bool directLight = true)
    {
        if (!float.IsFinite(metallic) || !float.IsFinite(roughness) || metallic is < 0 or > 1 || roughness is < .045f or > 1) throw new ArgumentException("Scene override scalar range.");
        return new() { Draw = ResourceDraw.Create(mesh, material, range, model, viewProjection), Surface = new(metallic, roughness, overrideScalars ? 1 : 0, directLight ? 0 : 1) };
    }
}
public readonly record struct SceneShadowSettings(float Bias = .0005f, float SlopeBias = .002f, bool Pcss = false, float LightRadius = 3)
{ public float Distance { get; init; } = 40; public SceneShadowSettings():this(.0005f,.002f,false,3){} }
public sealed class Scene3DPipeline(float exposure = 1, float ambient = .03f, uint shadowResolution = 1024,
    bool shadows = true, IReadOnlyList<ScenePipelineFeature>? features = null, ISceneGeometryStage? geometry = null, ISceneToneStage? tone = null) : RenderPipeline
{
    public override RenderGraph Build(uint width, uint height)
    {
        var graph = new RenderGraph(); RenderResourceId? shadow = shadows ? graph.AddResource(new("Scene shadow", RenderRole.SceneShadow, RenderFormat.Depth32, RenderUsage.Sampled | RenderUsage.DepthTarget, shadowResolution, shadowResolution)) : null;
        var hdr = graph.AddResource(new("Scene HDR", RenderRole.SceneHdr, RenderFormat.Rgba16Float, RenderUsage.Sampled | RenderUsage.ColorTarget, width, height));
        var depth = graph.AddResource(new("Scene depth", RenderRole.SceneDepth, RenderFormat.Depth32, RenderUsage.Sampled | RenderUsage.DepthTarget, width, height));
        var output = graph.AddResource(new("Scene output", RenderRole.SceneOutput, RenderFormat.Rgba8, RenderUsage.ColorTarget, width, height, Imported: true));
        if (shadow is { } s) graph.AddPass("Scene directional shadow", RenderOperation.SceneShadow, [], [s], 4);
        (geometry ?? new SceneGeometryStage(ambient)).Add(graph, shadow, hdr, depth);
        int previous = (tone ?? new SceneToneStage(exposure)).Add(graph, hdr, depth, output);
        if (features is not null) { if (features.Count > 12) throw new ArgumentException("Scene Feature budget."); foreach (var feature in features) (output, previous) = feature.Add(graph, hdr, depth, output, previous, width, height); }
        graph.SetOutput(output); return graph;
    }
}
public interface ISceneGeometryStage { void Add(RenderGraph graph, RenderResourceId? shadow, RenderResourceId hdr, RenderResourceId depth); }
public sealed class SceneGeometryStage(float ambient = .03f) : ISceneGeometryStage
{ public void Add(RenderGraph graph, RenderResourceId? shadow, RenderResourceId hdr, RenderResourceId depth) => graph.AddPass("Scene HDR geometry", RenderOperation.SceneGeometry, shadow is { } s ? [s] : [], [hdr, depth], 4, parameters: [ambient]); }
public interface ISceneToneStage { int Add(RenderGraph graph, RenderResourceId hdr, RenderResourceId depth, RenderResourceId output); }
public sealed class SceneToneStage(float exposure = 1) : ISceneToneStage
{ public int Add(RenderGraph graph, RenderResourceId hdr, RenderResourceId depth, RenderResourceId output) => graph.AddPass("Scene tone mapping", RenderOperation.SceneTone, [hdr, depth], [output], 4, parameters: [exposure]); }
public abstract class ScenePipelineFeature
{ public abstract (RenderResourceId Output, int Pass) Add(RenderGraph graph, RenderResourceId hdr, RenderResourceId depth, RenderResourceId previous, int previousPass, uint width, uint height); }
public sealed class SceneExposureFeature(float exposure) : ScenePipelineFeature
{
    public override (RenderResourceId, int) Add(RenderGraph graph, RenderResourceId hdr, RenderResourceId depth, RenderResourceId previous, int previousPass, uint width, uint height)
    { var output = graph.AddResource(new("Scene exposure output", RenderRole.SceneOutput, RenderFormat.Rgba8, RenderUsage.ColorTarget, width, height, Imported: true)); return (output, graph.AddPass("Scene exposure Feature", RenderOperation.SceneTone, [hdr, depth], [output], 4, [previousPass], [exposure])); }
}
public sealed class ScenePipelineSession : IDisposable
{
    internal RendererSession Owner { get; }
    internal GpuMeshKey Key;
    internal ScenePassV4[] Operations;
    public CompiledRenderGraph Plan { get; private set; }
    public RegisteredSceneTone? RegisteredTone { get; private set; }
    public RegisteredSceneShaders? RegisteredShaders { get; private set; }
    public RegisteredEnvironmentSceneShaders? RegisteredEnvironmentShaders { get; private set; }
    public Ncma.Assets.EnvironmentLightingConfiguration EnvironmentLighting { get; private set; } = Ncma.Assets.EnvironmentLightingConfiguration.Off;
    public bool Shadows => Plan.Resources.Any(r => r.Role == RenderRole.SceneShadow);
    public ScenePipelineSession(RendererSession renderer, RenderPipeline pipeline, uint width, uint height, RegisteredSceneTone? tone = null, RegisteredSceneShaders? shaders = null, RegisteredEnvironmentSceneShaders? environmentShaders = null)
    {
        Owner = renderer ?? throw new ArgumentNullException(nameof(renderer)); ArgumentNullException.ThrowIfNull(pipeline);
        Plan = pipeline.Build(width, height).Compile(RenderCapabilities.SceneDx11); if (!Plan.RequiresSceneService) throw new ArgumentException("Scene graph required.");
        if((tone is not null?1:0)+(shaders is not null?1:0)+(environmentShaders is not null?1:0)>1)throw new ArgumentException("Choose one exact shader contract.");
        Operations = Plan.SceneOperations;uint resolution=Plan.Resources.SingleOrDefault(r => r.Role == RenderRole.SceneShadow)?.Width ?? 0;
        if(environmentShaders is not null)renderer.InstallEnvironmentSceneShaders(this,resolution,environmentShaders,true);
        else if(shaders is not null)renderer.InstallSceneShaders(this,resolution,shaders,true);else renderer.CreateScene(this,resolution,tone);
        RegisteredTone = tone;RegisteredShaders=shaders;RegisteredEnvironmentShaders=environmentShaders;
    }
    public void ReplaceTone(RegisteredSceneTone tone)
    { ObjectDisposedException.ThrowIf(Key.Value == 0, this); Owner.ReplaceTone(this, tone); RegisteredTone = tone;RegisteredShaders=null; }
    public void ReplaceShaders(RegisteredSceneShaders shaders)
    { ObjectDisposedException.ThrowIf(Key.Value == 0, this);Owner.InstallSceneShaders(this,Plan.Resources.SingleOrDefault(r=>r.Role==RenderRole.SceneShadow)?.Width??0,shaders,false);RegisteredShaders=shaders;RegisteredTone=null; }
    public void Submit(ulong frame, ReadOnlySpan<SceneGpuDraw> geometry, ReadOnlySpan<SceneGpuDraw> casters, ResourceLighting lighting, Matrix4x4 lightViewProjection,
        SceneShadowSettings shadow, GpuViewTarget? target = null, Vector4 viewport = default, Vector4 clear = default)
    { ObjectDisposedException.ThrowIf(Key.Value == 0, this); Owner.SubmitScene(this, frame, geometry, casters, lighting, lightViewProjection, shadow, target, viewport, clear); }
    public void ReplaceEnvironmentShaders(RegisteredEnvironmentSceneShaders shaders)
    { ObjectDisposedException.ThrowIf(Key.Value==0,this);Owner.InstallEnvironmentSceneShaders(this,Plan.Resources.SingleOrDefault(r=>r.Role==RenderRole.SceneShadow)?.Width??0,shaders,false);RegisteredEnvironmentShaders=shaders; }
    public void ConfigureEnvironment(GpuEnvironmentResource? resource,Ncma.Assets.EnvironmentLightingConfiguration configuration,Func<bool> preparationAllowed)
    { ObjectDisposedException.ThrowIf(Key.Value==0,this);Owner.BindEnvironment(this,resource,configuration,preparationAllowed);EnvironmentLighting=configuration; }
    public void Configure(RenderPipeline pipeline)
    {
        ObjectDisposedException.ThrowIf(Key.Value == 0, this); _ = Owner.Handle; ArgumentNullException.ThrowIfNull(pipeline);
        var candidate=pipeline.Build(Plan.Width,Plan.Height).Compile(RenderCapabilities.SceneDx11);
        if(!candidate.RequiresSceneService || (candidate.Resources.SingleOrDefault(r=>r.Role==RenderRole.SceneShadow)?.Width??0)!=(Plan.Resources.SingleOrDefault(r=>r.Role==RenderRole.SceneShadow)?.Width??0))
            throw new ArgumentException("Changing target/shadow allocation requires a new scene lease.");
        var operations=candidate.SceneOperations; Plan=candidate; Operations=operations;
    }
    public void Dispose() { if (Key.Value == 0) return; Owner.DestroyScene(this); Key = default;EnvironmentLighting=Ncma.Assets.EnvironmentLightingConfiguration.Off; }
}
public sealed unsafe partial class RendererSession
{
    private readonly HashSet<ScenePipelineSession> _scenePipelines = [];
    private CreateSceneV4? _createScene; private DestroyMesh? _destroyScene; private SubmitSceneV4? _submitScene; private SceneStatsV4? _sceneStats;
    private void EnsureScenePipeline()
    {
        Verify(); if (_createScene is not null) return; var query = Module.ReadFunction<QuerySceneRender>(144); SceneApiV4 api = default; PluginError error = default;
        PluginModule.Check(Module.Id, "query_scene_pipeline", query(Module.Context, 4, &api, 48, &error), error);
        if (api.Size != 48 || api.Version != 4 || api.Caps != 7 || api.Create == 0 || api.Destroy == 0 || api.Submit == 0 || api.Stats == 0) throw new ArgumentException("Scene service v4 contract.");
        _destroyScene = Marshal.GetDelegateForFunctionPointer<DestroyMesh>(api.Destroy); _submitScene = Marshal.GetDelegateForFunctionPointer<SubmitSceneV4>(api.Submit); _sceneStats = Marshal.GetDelegateForFunctionPointer<SceneStatsV4>(api.Stats); _createScene = Marshal.GetDelegateForFunctionPointer<CreateSceneV4>(api.Create);
    }
    internal void CreateScene(ScenePipelineSession session, uint resolution, RegisteredSceneTone? tone = null)
    {
        EnsureScenePipeline(); if (tone is not null) { CreateRegisteredScene(session, resolution, tone); return; }
        _scenePipelines.Add(session); SceneDescriptionV4 d = new() { Size = 16, Width = session.Plan.Width, Height = session.Plan.Height, Resolution = resolution }; GpuMeshKey key = default; PluginError error = default;
        try { PluginModule.Check(Module.Id, "create_scene_pipeline", _createScene!(Module.Context, Handle, &d, &key, &error), error); session.Key = key; } catch { _scenePipelines.Remove(session); throw; }
    }
    internal void DestroyScene(ScenePipelineSession session)
    { Verify(); PluginError error = default; PluginModule.Check(Module.Id, "destroy_scene_pipeline", _destroyScene!(Module.Context, Handle, session.Key, &error), error); _scenePipelines.Remove(session); }
    public ScenePipelineStats PipelineStats
    { get { EnsureScenePipeline(); ScenePipelineStats s = default; PluginError error = default; PluginModule.Check(Module.Id, "scene_pipeline_stats", _sceneStats!(Module.Context, Handle, &s, &error), error); if (s.Size != 72 || s.MaxDraws != 4096 || s.Generation != Handle) throw new ArgumentException("Scene stats contract."); return s; } }
    internal void SubmitScene(ScenePipelineSession session, ulong frame, ReadOnlySpan<SceneGpuDraw> geometry, ReadOnlySpan<SceneGpuDraw> casters, ResourceLighting lighting,
        Matrix4x4 lightVP, SceneShadowSettings shadow, GpuViewTarget? target, Vector4 viewport, Vector4 clear)
    {
        EnsureScenePipeline(); if (geometry.Length > 4096 || casters.Length > 4096 || session.Owner != this || target is not null && target.Owner != this) throw new ArgumentException("Scene batch/owner.");
        SceneFrameV4 f = new() { Base = new() { Size = 240, Count = (uint)geometry.Length, Frame = frame, Generation = Handle, Target = target?.Key ?? default }, Pipeline = session.Key, Casters = (uint)casters.Length, Passes = (uint)session.Operations.Length };
        if (viewport == default) viewport = new(0, 0, session.Plan.Width, session.Plan.Height);
        f.Base.Viewport[0] = viewport.X; f.Base.Viewport[1] = viewport.Y; f.Base.Viewport[2] = viewport.Z; f.Base.Viewport[3] = viewport.W;
        f.Base.Clear[0] = clear.X; f.Base.Clear[1] = clear.Y; f.Base.Clear[2] = clear.Z; f.Base.Clear[3] = clear.W;
        f.Base.Camera[0] = lighting.Camera.X; f.Base.Camera[1] = lighting.Camera.Y; f.Base.Camera[2] = lighting.Camera.Z; f.Base.Camera[3] = 1;
        f.Base.Light[0] = lighting.LightDirection.X; f.Base.Light[1] = lighting.LightDirection.Y; f.Base.Light[2] = lighting.LightDirection.Z;
        f.Base.LightColor[0] = lighting.LightColor.X; f.Base.LightColor[1] = lighting.LightColor.Y; f.Base.LightColor[2] = lighting.LightColor.Z; f.Base.LightColor[3] = lighting.LightColor.W;
        ReadOnlySpan<float> matrix = [lightVP.M11,lightVP.M12,lightVP.M13,lightVP.M14,lightVP.M21,lightVP.M22,lightVP.M23,lightVP.M24,lightVP.M31,lightVP.M32,lightVP.M33,lightVP.M34,lightVP.M41,lightVP.M42,lightVP.M43,lightVP.M44];
        for (int i = 0; i < 16; i++) f.LightVP[i] = matrix[i]; f.Shadow[0] = shadow.Bias; f.Shadow[1] = shadow.SlopeBias; f.Shadow[2] = shadow.Pcss ? 1 : 0; f.Shadow[3] = shadow.LightRadius;
        PluginError error = default; fixed (SceneGpuDraw* g = geometry) fixed (SceneGpuDraw* c = casters) fixed (ScenePassV4* p = session.Operations) PluginModule.Check(Module.Id, "submit_scene_pipeline", _submitScene!(Module.Context, Handle, &f, g, c, p, &error), error);
        SubmitCalls++; CopiedBytes += 240 + (ulong)(geometry.Length + casters.Length) * 256 + (ulong)session.Operations.Length * 16;
    }
}
