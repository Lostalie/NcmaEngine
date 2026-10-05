namespace Ncma.Rendering;
[Flags] public enum RenderUsage { None = 0, Sampled = 1, ColorTarget = 2, DepthTarget = 4 }
public enum RenderFormat { Rgba8, Rgba16Float, Depth32 }
public enum RenderRole { Shadow, Hdr, Depth, Output, ResourceOutput, SceneShadow, SceneHdr, SceneDepth, SceneOutput }
public enum RenderOperation : uint { Shadow = 1, Geometry, ToneMap, Clear, ResourceGeometry, SceneShadow, SceneGeometry, SceneTone }
public readonly record struct RenderCapabilities(uint MaxDimension, int MaxPasses, int MaxResources, bool ReferencePbr, bool Arrays, bool ResourcePbr = false, bool ScenePbr = false)
{
    public static RenderCapabilities ReferenceDx11 => new(4096, 64, 32, true, true);
    public static RenderCapabilities ResourceDx11 => new(4096, 64, 32, false, false, true);
    public static RenderCapabilities SceneDx11 => new(4096, 16, 16, false, false, false, true);
}
public readonly record struct RenderResourceId(int Index);
public sealed record RenderResource(string Name, RenderRole Role, RenderFormat Format, RenderUsage Usage, uint Width, uint Height, uint Layers = 1, bool Imported = false);
public sealed class RenderGraphException(string code, string message) : Exception(message) { public string Code { get; } = code; }
public sealed class RenderGraph
{
    private readonly List<RenderResource> _resources = [];
    private readonly List<Pass> _passes = [];
    private RenderResourceId? _output;
    private ulong _revision;
    private CompiledRenderGraph? _cache;
    private sealed record Pass(string Name, RenderOperation Operation, RenderResourceId[] Reads, RenderResourceId[] Writes, int[] Dependencies, uint Shader, float[] Parameters);
    public RenderResourceId AddResource(RenderResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource); if (_resources.Count >= 32) Fail("resource_budget", "Resource budget exceeded.");
        if (string.IsNullOrWhiteSpace(resource.Name) || resource.Name.Length > 128 || _resources.Any(r => r.Name == resource.Name)) Fail("resource_name", "Invalid/duplicate resource name.");
        _resources.Add(resource); _revision++; return new(_resources.Count - 1);
    }
    public int AddPass(string name, RenderOperation operation, IEnumerable<RenderResourceId> reads, IEnumerable<RenderResourceId> writes,
        uint shaderContract, IEnumerable<int>? dependsOn = null, IReadOnlyList<float>? parameters = null)
    {
        if (_passes.Count >= 64) Fail("pass_budget", "Pass budget exceeded.");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || _passes.Any(p => p.Name == name)) Fail("pass_name", "Invalid/duplicate pass name.");
        var input = reads.Take(33).ToArray(); var output = writes.Take(33).ToArray(); var deps = (dependsOn ?? []).Take(65).ToArray();
        if (input.Length > 32 || output.Length > 32 || deps.Length > 64 || parameters is { Count: > 4 }) Fail("pass_budget", "Pass input budget exceeded.");
        float[] copied = new float[4]; if (parameters is not null) for (int i = 0; i < parameters.Count; i++) copied[i] = parameters[i];
        _passes.Add(new(name, operation, input, output, deps, shaderContract, copied)); _revision++; return _passes.Count - 1;
    }
    public void SetOutput(RenderResourceId resource) { _output = resource; _revision++; }
    public CompiledRenderGraph Compile(RenderCapabilities capabilities)
    {
        if (_cache is not null && _cache.Revision == _revision && _cache.Capabilities == capabilities) return _cache;
        if (_passes.Count == 0 || _passes.Count > capabilities.MaxPasses || _resources.Count > capabilities.MaxResources) Fail("graph_budget", "Unsupported graph size.");
        if (_output is null) Fail("missing_output", "Graph has no declared output.");
        foreach (var r in _resources) {
            if (!Enum.IsDefined(r.Role) || !Enum.IsDefined(r.Format) || (r.Usage & ~(RenderUsage.Sampled | RenderUsage.ColorTarget | RenderUsage.DepthTarget)) != 0)
                Fail("resource_format", "Unknown resource contract.");
            if (r.Width == 0 || r.Height == 0 || r.Width > capabilities.MaxDimension || r.Height > capabilities.MaxDimension || r.Layers is < 1 or > 4)
                Fail("resource_dimensions", "Unsupported resource dimensions.");
            if (r.Layers > 1 && !capabilities.Arrays) Fail("capability", "Texture arrays unavailable.");
            bool valid = r.Role switch {
                RenderRole.Shadow => r.Format == RenderFormat.Depth32 && r.Usage == (RenderUsage.Sampled | RenderUsage.DepthTarget) && r.Width == 2048 && r.Height == 2048 && r.Layers == 4 && !r.Imported,
                RenderRole.Hdr => r.Format == RenderFormat.Rgba16Float && r.Usage == (RenderUsage.Sampled | RenderUsage.ColorTarget) && r.Layers == 1 && !r.Imported,
                RenderRole.Depth => r.Format == RenderFormat.Depth32 && r.Usage == (RenderUsage.Sampled | RenderUsage.DepthTarget) && r.Layers == 1 && !r.Imported,
                RenderRole.SceneShadow => r.Format == RenderFormat.Depth32 && r.Usage == (RenderUsage.Sampled | RenderUsage.DepthTarget) && r.Width == r.Height && r.Width is 256 or 512 or 1024 or 2048 && r.Layers == 1 && !r.Imported,
                RenderRole.SceneHdr => r.Format == RenderFormat.Rgba16Float && r.Usage == (RenderUsage.Sampled | RenderUsage.ColorTarget) && r.Layers == 1 && !r.Imported,
                RenderRole.SceneDepth => r.Format == RenderFormat.Depth32 && r.Usage == (RenderUsage.Sampled | RenderUsage.DepthTarget) && r.Layers == 1 && !r.Imported,
                RenderRole.Output or RenderRole.ResourceOutput or RenderRole.SceneOutput => r.Format == RenderFormat.Rgba8 && r.Usage == RenderUsage.ColorTarget && r.Layers == 1 && r.Imported, _ => false };
            if (!valid) Fail("resource_format", "Resource incompatible with supported operation set.");
        }
        foreach(var role in new[]{RenderRole.Shadow,RenderRole.Hdr,RenderRole.Depth,RenderRole.SceneShadow,RenderRole.SceneHdr,RenderRole.SceneDepth})
            if(_resources.Count(r=>r.Role==role)>1) Fail("resource_contract","v1 reference group supports one resource per semantic role.");
        int[] writer = Enumerable.Repeat(-1, _resources.Count).ToArray();
        for (int i = 0; i < _passes.Count; i++) foreach (var id in _passes[i].Writes) {
            ValidateId(id); if (writer[id.Index] != -1) Fail("multiple_writer", "Use a new logical output version rather than multiple writers.");
            writer[id.Index] = i;
        }
        ValidateId(_output!.Value);
        if (_resources[_output.Value.Index].Role is not (RenderRole.Output or RenderRole.ResourceOutput or RenderRole.SceneOutput) || writer[_output.Value.Index] < 0) Fail("missing_output", "Output is not written.");
        if (_passes.Any(p => p.Operation == RenderOperation.ResourceGeometry) && (_passes.Count != 1 || _resources.Count != 1)) Fail("resource_contract", "v3 supports one typed geometry stage, without reference-stage mixing.");
        var edges = new List<int>[_passes.Count];
        for (int i = 0; i < _passes.Count; i++) {
            var p = _passes[i]; edges[i] = [..p.Dependencies];
            foreach (int dependency in edges[i]) if (dependency < 0 || dependency >= _passes.Count || dependency == i) Fail("cycle", "Invalid/self dependency.");
            foreach (var id in p.Reads) {
                ValidateId(id); if (writer[id.Index] < 0) Fail("uninitialized_read", "Resource read before initialized.");
                edges[i].Add(writer[id.Index]);
            }
            if (p.Reads.Distinct().Count() != p.Reads.Length || p.Writes.Distinct().Count() != p.Writes.Length) Fail("resource_contract", "Duplicate resource binding.");
            foreach (float v in p.Parameters) if (!float.IsFinite(v)) Fail("parameter", "Non-finite parameter.");
            ValidateContract(p, capabilities);
        }
        var order = new List<int>(); var marks = new byte[_passes.Count];
        void Visit(int i) {
            if (marks[i] == 1) Fail("cycle", "Render graph dependency cycle.");
            if (marks[i] == 2) return;
            marks[i] = 1; foreach (var dep in edges[i]) Visit(dep); marks[i] = 2; order.Add(i);
        }
        // Compile all passes, including otherwise unused ones, so rejected input is deterministic.
        for (int i = 0; i < _passes.Count; i++) Visit(i);
        if (order[^1] != writer[_output.Value.Index]) Fail("output_order", "Final declared output must be the final pass.");
        if (_passes.Any(p => p.Operation is RenderOperation.SceneShadow or RenderOperation.SceneGeometry or RenderOperation.SceneTone))
        {
            if (_passes.Any(p => p.Operation is not (RenderOperation.SceneShadow or RenderOperation.SceneGeometry or RenderOperation.SceneTone)) ||
                _passes.Count(p => p.Operation == RenderOperation.SceneGeometry) != 1 || _passes.Count(p => p.Operation == RenderOperation.SceneShadow) > 1 ||
                order.Count(i => _passes[i].Operation == RenderOperation.SceneTone) == 0 ||
                order.FindIndex(i => _passes[i].Operation == RenderOperation.SceneTone) < order.FindIndex(i => _passes[i].Operation == RenderOperation.SceneGeometry))
                Fail("scene_contract", "Independent v4 scene stage group required.");
            if (_resources.Any(r => r.Role is not (RenderRole.SceneShadow or RenderRole.SceneHdr or RenderRole.SceneDepth or RenderRole.SceneOutput))) Fail("scene_contract", "No reference/v3 resources in scene group.");
            var geometry=_passes.Single(p=>p.Operation==RenderOperation.SceneGeometry);
            if (_passes.Any(p=>p.Operation==RenderOperation.SceneShadow) != (geometry.Reads.Length==1) ||
                _resources.Any(r=>r.Role==RenderRole.SceneShadow) != (geometry.Reads.Length==1))
                Fail("scene_contract","Scene shadow must be the geometry dependency, not an unused side pass.");
            if(_resources.Any(r=>r.Role==RenderRole.SceneOutput && (r.Width!=_resources[_output.Value.Index].Width || r.Height!=_resources[_output.Value.Index].Height)))
                Fail("resource_dimensions","Scene output versions share one physical target.");
        }
        var operations = order.Select(i => new CompiledRenderGraph.Operation(_passes[i].Name, _passes[i].Operation, _passes[i].Shader, (float[])_passes[i].Parameters.Clone())).ToArray();
        _cache = new(_revision, capabilities, operations, _resources.ToArray()); return _cache;
    }
    private void ValidateId(RenderResourceId id) { if (id.Index < 0 || id.Index >= _resources.Count) Fail("resource_handle", "Unknown logical resource."); }
    private void ValidateContract(Pass p, RenderCapabilities caps)
    {
        var reads = p.Reads.Select(id => _resources[id.Index]).ToArray(); var writes = p.Writes.Select(id => _resources[id.Index]).ToArray();
        bool Roles(RenderResource[] values, params RenderRole[] roles) => values.Select(r => r.Role).Order().SequenceEqual(roles.Order());
        bool valid = p.Operation switch {
            RenderOperation.Shadow => p.Shader == 1 && reads.Length == 0 && Roles(writes, RenderRole.Shadow),
            RenderOperation.Geometry => p.Shader == 1 && Roles(reads, RenderRole.Shadow) && Roles(writes, RenderRole.Hdr, RenderRole.Depth),
            RenderOperation.ToneMap => p.Shader == 2 && Roles(reads, RenderRole.Hdr, RenderRole.Depth) && Roles(writes, RenderRole.Output),
            RenderOperation.Clear => p.Shader == 0 && (reads.Length == 0 || Roles(reads, RenderRole.Output)) && Roles(writes, RenderRole.Output),
            RenderOperation.ResourceGeometry => p.Shader == 3 && reads.Length == 0 && Roles(writes, RenderRole.ResourceOutput),
            RenderOperation.SceneShadow => p.Shader == 4 && reads.Length == 0 && Roles(writes, RenderRole.SceneShadow),
            RenderOperation.SceneGeometry => p.Shader == 4 && (reads.Length == 0 || Roles(reads, RenderRole.SceneShadow)) && Roles(writes, RenderRole.SceneHdr, RenderRole.SceneDepth),
            RenderOperation.SceneTone => p.Shader == 4 && Roles(reads, RenderRole.SceneHdr, RenderRole.SceneDepth) && Roles(writes, RenderRole.SceneOutput),
            _ => false };
        if (!valid) Fail("shader_contract", "Operation/shader/input/output contract mismatch.");
        bool scene = p.Operation is RenderOperation.SceneShadow or RenderOperation.SceneGeometry or RenderOperation.SceneTone;
        if (scene && !caps.ScenePbr) Fail("capability", "Scene shaders unavailable.");
        if (!scene && p.Operation is not (RenderOperation.Clear or RenderOperation.ResourceGeometry) && !caps.ReferencePbr) Fail("capability", "Reference shaders unavailable.");
        if (p.Operation == RenderOperation.SceneShadow && p.Parameters.Any(v => v != 0) ||
            p.Operation == RenderOperation.SceneGeometry && (p.Parameters[0] is < 0 or > 1 || p.Parameters.Skip(1).Any(v => v != 0)) ||
            p.Operation == RenderOperation.SceneTone && (p.Parameters[0] is < .01f or > 16 || p.Parameters.Skip(1).Any(v => v != 0))) Fail("parameter", "Scene stage parameters.");
        if (p.Operation == RenderOperation.ResourceGeometry && !caps.ResourcePbr) Fail("capability", "Typed resource shaders unavailable.");
        if (p.Operation == RenderOperation.ResourceGeometry && (p.Parameters[0] is < .01f or > 16 || p.Parameters[1] is < 0 or > 1 || p.Parameters[2] is < 0 or > 2 || p.Parameters[2] != MathF.Truncate(p.Parameters[2]) || p.Parameters[3] != 0)) Fail("parameter", "Typed resource exposure/ambient/mode contract.");
        if (p.Operation == RenderOperation.Clear && p.Parameters.Any(v => v < 0 || v > 1)) Fail("parameter", "Clear RGBA outside [0,1].");
        if (p.Operation == RenderOperation.ToneMap && (p.Parameters[0] != 0 && p.Parameters[0] is < .01f or > 16)) Fail("parameter", "Tone exposure override outside [.01,16].");
        var spatial = reads.Concat(writes).Where(r => r.Role is not (RenderRole.Shadow or RenderRole.SceneShadow)).ToArray();
        if (spatial.Length > 1 && spatial.Any(r => r.Width != spatial[0].Width || r.Height != spatial[0].Height)) Fail("resource_dimensions", "Mismatched viewport resource dimensions.");
    }
    private static void Fail(string code, string message) => throw new RenderGraphException(code, message);
}
public sealed class CompiledRenderGraph
{
    internal sealed record Operation(string Name, RenderOperation Code, uint Shader, float[] Parameters);
    private readonly Operation[] _operations;
    public ulong Revision { get; }
    public RenderCapabilities Capabilities { get; }
    public int PassCount => _operations.Length;
    public uint Width { get; }
    public uint Height { get; }
    public bool RequiresReferenceResources { get; }
    public bool RequiresResourceService { get; }
    public bool RequiresSceneService { get; }
    internal ScenePassV4[] SceneOperations => _operations.Select(p => new ScenePassV4 { Operation = (uint)p.Code, Parameter = p.Parameters[0] }).ToArray();
    internal ReadOnlySpan<float> ResourceParameters => _operations[0].Parameters;
    public IReadOnlyList<string> PassNames { get; }
    public IReadOnlyList<RenderResource> Resources { get; }
    internal CompiledRenderGraph(ulong revision, RenderCapabilities capabilities, Operation[] operations, RenderResource[] resources)
    {
        var output=resources.First(r=>r.Role is RenderRole.Output or RenderRole.ResourceOutput or RenderRole.SceneOutput);Width=output.Width;Height=output.Height;
        RequiresReferenceResources=operations.Any(p=>p.Code is RenderOperation.Shadow or RenderOperation.Geometry or RenderOperation.ToneMap);
        RequiresResourceService=operations.Any(p=>p.Code==RenderOperation.ResourceGeometry);
        RequiresSceneService=operations.Any(p=>p.Code is RenderOperation.SceneShadow or RenderOperation.SceneGeometry or RenderOperation.SceneTone);
        Revision = revision; Capabilities = capabilities; _operations = operations;
        PassNames = Array.AsReadOnly(operations.Select(p => p.Name).ToArray()); Resources = Array.AsReadOnly(resources);
    }
    internal unsafe void Encode(Span<RenderPass> destination, ulong resources)
    {
        for (int i = 0; i < _operations.Length; i++) {
            var op = _operations[i]; RenderPass pass = new() { Operation = (uint)op.Code, ShaderContract = op.Shader, Resources = op.Code == RenderOperation.Clear ? 0 : resources };
            for (int j = 0; j < 4; j++) pass.Color[j] = op.Parameters[j]; destination[i] = pass;
        }
    }
}
public abstract class RenderPipeline { public abstract RenderGraph Build(uint width, uint height); }
public abstract class RenderFeature { public abstract RenderResourceId Add(RenderGraph graph, RenderFeatureContext context); }
public sealed record RenderFeatureContext(RenderResourceId Hdr, RenderResourceId Depth, RenderResourceId PreviousOutput, int PreviousPass, uint Width, uint Height);
public interface IToneMappingStage { int Add(RenderGraph graph, RenderResourceId hdr, RenderResourceId depth, RenderResourceId output); }
public sealed class ReferencePreviewPipeline(float toneExposureOverride = 0, IReadOnlyList<RenderFeature>? features = null, IToneMappingStage? toneMapping = null) : RenderPipeline
{
    public override RenderGraph Build(uint width, uint height)
    {
        var graph = new RenderGraph();
        var shadow = graph.AddResource(new("DirectionalShadow", RenderRole.Shadow, RenderFormat.Depth32, RenderUsage.Sampled | RenderUsage.DepthTarget, 2048, 2048, 4));
        var hdr = graph.AddResource(new("Hdr", RenderRole.Hdr, RenderFormat.Rgba16Float, RenderUsage.Sampled | RenderUsage.ColorTarget, width, height));
        var depth = graph.AddResource(new("Depth", RenderRole.Depth, RenderFormat.Depth32, RenderUsage.Sampled | RenderUsage.DepthTarget, width, height));
        var output = graph.AddResource(new("Swapchain", RenderRole.Output, RenderFormat.Rgba8, RenderUsage.ColorTarget, width, height, Imported: true));
        graph.AddPass("Directional shadow", RenderOperation.Shadow, [], [shadow], 1);
        graph.AddPass("PBR geometry", RenderOperation.Geometry, [shadow], [hdr, depth], 1);
        int tonePass = toneMapping?.Add(graph,hdr,depth,output) ?? graph.AddPass("HDR + contact shadow + tonemap", RenderOperation.ToneMap, [hdr, depth], [output], 2, parameters: [toneExposureOverride]);
        if (features is not null) foreach (var feature in features) {
            output = feature.Add(graph,new(hdr,depth,output,tonePass,width,height));
            tonePass = -1;
        }
        graph.SetOutput(output); return graph;
    }
}
// Samples use the same public graph/operation set as NSRP; no arbitrary shader text or DLL loading.
public sealed class ExposureToneStage(float exposure) : IToneMappingStage {
    public int Add(RenderGraph graph, RenderResourceId hdr, RenderResourceId depth, RenderResourceId output) =>
        graph.AddPass("Replacement tone stage",RenderOperation.ToneMap,[hdr,depth],[output],2,parameters:[exposure]);
}
public sealed class ExposureFeature(float exposure) : RenderFeature {
    public override RenderResourceId Add(RenderGraph graph,RenderFeatureContext context) {
        var output=graph.AddResource(new("Exposure feature output",RenderRole.Output,RenderFormat.Rgba8,RenderUsage.ColorTarget,context.Width,context.Height,Imported:true));
        if(context.PreviousPass<0) throw new RenderGraphException("feature_contract","Sample supports a single exposure feature.");
        graph.AddPass("Exposure feature",RenderOperation.ToneMap,[context.Hdr,context.Depth],[output],2,[context.PreviousPass],[exposure]);
        return output;
    }
}
public sealed class ClearPipeline(float red = .1f, float green = .2f, float blue = .3f) : RenderPipeline
{
    public override RenderGraph Build(uint width, uint height)
    {
        var graph = new RenderGraph();
        var output = graph.AddResource(new("Swapchain", RenderRole.Output, RenderFormat.Rgba8, RenderUsage.ColorTarget, width, height, Imported: true));
        graph.AddPass("Clear output", RenderOperation.Clear, [], [output], 0, parameters: [red, green, blue, 1]); graph.SetOutput(output); return graph;
    }
}
