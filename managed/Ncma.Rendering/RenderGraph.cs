namespace Ncma.Rendering;
[Flags] public enum RenderUsage { None = 0, Sampled = 1, ColorTarget = 2, DepthTarget = 4 }
public enum RenderFormat { Rgba8, Rgba16Float, Depth32 }
public enum RenderRole { Shadow, Hdr, Depth, Output }
public enum RenderOperation : uint { Shadow = 1, Geometry, ToneMap, Clear }
public readonly record struct RenderCapabilities(uint MaxDimension, int MaxPasses, int MaxResources, bool ReferencePbr, bool Arrays)
{
    public static RenderCapabilities ReferenceDx11 => new(4096, 64, 32, true, true);
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
                RenderRole.Output => r.Format == RenderFormat.Rgba8 && r.Usage == RenderUsage.ColorTarget && r.Layers == 1 && r.Imported, _ => false };
            if (!valid) Fail("resource_format", "Resource incompatible with supported operation set.");
        }
        foreach(var role in new[]{RenderRole.Shadow,RenderRole.Hdr,RenderRole.Depth})
            if(_resources.Count(r=>r.Role==role)>1) Fail("resource_contract","v1 reference group supports one resource per semantic role.");
        int[] writer = Enumerable.Repeat(-1, _resources.Count).ToArray();
        for (int i = 0; i < _passes.Count; i++) foreach (var id in _passes[i].Writes) {
            ValidateId(id); if (writer[id.Index] != -1) Fail("multiple_writer", "Use a new logical output version rather than multiple writers.");
            writer[id.Index] = i;
        }
        ValidateId(_output!.Value);
        if (_resources[_output.Value.Index].Role != RenderRole.Output || writer[_output.Value.Index] < 0) Fail("missing_output", "Output is not written.");
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
            _ => false };
        if (!valid) Fail("shader_contract", "Operation/shader/input/output contract mismatch.");
        if (p.Operation != RenderOperation.Clear && !caps.ReferencePbr) Fail("capability", "Reference shaders unavailable.");
        if (p.Operation == RenderOperation.Clear && p.Parameters.Any(v => v < 0 || v > 1)) Fail("parameter", "Clear RGBA outside [0,1].");
        if (p.Operation == RenderOperation.ToneMap && (p.Parameters[0] != 0 && p.Parameters[0] is < .01f or > 16)) Fail("parameter", "Tone exposure override outside [.01,16].");
        var spatial = reads.Concat(writes).Where(r => r.Role != RenderRole.Shadow).ToArray();
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
    public IReadOnlyList<string> PassNames { get; }
    public IReadOnlyList<RenderResource> Resources { get; }
    internal CompiledRenderGraph(ulong revision, RenderCapabilities capabilities, Operation[] operations, RenderResource[] resources)
    {
        var output=resources.First(r=>r.Role==RenderRole.Output);Width=output.Width;Height=output.Height;
        RequiresReferenceResources=operations.Any(p=>p.Code!=RenderOperation.Clear);
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
