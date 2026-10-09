using System.Runtime.InteropServices;
using System.Text;
using Ncma.Interop;

namespace Ncma.Rendering;

[StructLayout(LayoutKind.Sequential)] internal unsafe struct ShaderPairNative { public uint Size, Version, VertexBytes, PixelBytes; public byte* Vertex; public byte* Pixel; }
[StructLayout(LayoutKind.Sequential)] internal struct ShaderPipelineApi { public uint Size, Version; public ulong Caps; public nint Source, Create, Replace; }
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CopyToneSourceNative(ulong module, ulong renderer, byte* output, uint capacity, uint* required, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CreateRegisteredSceneNative(ulong module, ulong renderer, SceneDescriptionV4* scene, ShaderPairNative* shaders, GpuMeshKey* key, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint ReplaceToneNative(ulong module, ulong renderer, GpuMeshKey key, ShaderPairNative* shaders, PluginError* error);

// Trusted copied source metadata. No disk IO, compilation, GPU initialization or Agent endpoint.
public static class DefaultSceneTone
{
    public static readonly Guid VertexId = Guid.Parse("fa182176-d879-40c0-bcf2-f7d09e545101");
    public static readonly Guid PixelId = Guid.Parse("fa182176-d879-40c0-bcf2-f7d09e545102");
    internal static ShaderVertexInput[] Inputs => [new("SV_VERTEXID", 0, ShaderScalar.UInt32, 1, -1)];
    internal static ShaderResourceBinding[] Resources => [new("BaseTex", ShaderResourceKind.Texture2D, ShaderResourceAccess.ReadOnly, 0, 1, 0), new("S", ShaderResourceKind.Sampler, ShaderResourceAccess.ReadOnly, 0, 1, 0)];
    internal static ShaderConstantBuffer[] Constants {
        get {
            string[] names = ["MVP", "Model", "NormalMatrix", "LightVP", "Base", "Emissive", "Surface", "Camera", "LightDirection", "LightColor", "Settings", "Channels", "ShadowParameters"];
            return [new("C", 0, 400, names.Select((n, i) => new ShaderConstantMember(n, ShaderScalar.Float32, i < 4 ? 4 : 1, 4,
                i < 4 ? ShaderMatrixOrder.ColumnMajor : ShaderMatrixOrder.None, i < 4 ? i * 64 : 256 + (i - 4) * 16, 1, 0)).ToArray())];
        }
    }
    public static ShaderCatalog CopyCatalog(RendererSession renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer); string source = renderer.CopyDefaultToneSource();
        return ShaderCatalog.Create(ShaderProfile.Scene3D, [
            new(1, VertexId, "Scene Tone Vertex", ShaderProfile.Scene3D, ShaderStage.Vertex, "VSTone", source, ShaderContractCodec.HashSource(source), Inputs, [], [], []),
            new(1, PixelId, "Scene Tone Pixel", ShaderProfile.Scene3D, ShaderStage.Pixel, "PSTone", source, ShaderContractCodec.HashSource(source), [], Constants, Resources, [])]);
    }
}

public readonly record struct RegisteredToneMetadata(string CatalogHash, Guid VertexId, Guid PixelId, string VertexHash, string PixelHash, string VertexBytecodeHash, string PixelBytecodeHash, bool Compiled);

// Explicit trusted off-simulation preparation; same catalog/compiler/validator for default and user.
// CPU-only immutable candidate, exact renderer lifetime. Not a persistent GPU handle or cooked package.
public sealed class RegisteredSceneTone
{
    internal RendererSession Owner { get; }
    internal CompiledShader Vertex { get; }
    internal CompiledShader Pixel { get; }
    private readonly Func<bool> _allowed;
    private bool _checking;
    private readonly string _catalogHash;
    private RegisteredSceneTone(RendererSession owner, string catalogHash, CompiledShader vertex, CompiledShader pixel, Func<bool> allowed)
    { Owner = owner; _catalogHash = catalogHash; Vertex = vertex; Pixel = pixel; _allowed = allowed; }
    public static RegisteredSceneTone Prepare(RendererSession renderer, ShaderCatalog catalog, ShaderDescriptor vertex, ShaderDescriptor pixel, Func<bool> preparationAllowed)
    {
        ArgumentNullException.ThrowIfNull(renderer); renderer.BeginToneOperation();
        try { return PrepareCore(renderer, catalog, vertex, pixel, preparationAllowed); } finally { renderer.EndToneOperation(); }
    }
    private static RegisteredSceneTone PrepareCore(RendererSession renderer, ShaderCatalog catalog, ShaderDescriptor vertex, ShaderDescriptor pixel, Func<bool> preparationAllowed)
    {
        ArgumentNullException.ThrowIfNull(renderer); ArgumentNullException.ThrowIfNull(catalog); ArgumentNullException.ThrowIfNull(vertex); ArgumentNullException.ThrowIfNull(pixel); ArgumentNullException.ThrowIfNull(preparationAllowed);
        if (catalog.Profile != ShaderProfile.Scene3D || vertex.Stage != ShaderStage.Vertex || pixel.Stage != ShaderStage.Pixel) ShaderContractCodec.Fail("tone_profile_stage", "tone");
        var v = catalog.Require(vertex.AssetId, vertex.ContentHash); var p = catalog.Require(pixel.AssetId, pixel.ContentHash);
        if (v.CopyDefinition().Dependencies.Length != 0 || p.CopyDefinition().Dependencies.Length != 0) ShaderContractCodec.Fail("tone_dependencies", "tone");
        ShaderBindingValidation.Validate(v, new(v.ContentHash, v.Stage, DefaultSceneTone.Inputs, [], []));
        ShaderBindingValidation.Validate(p, new(p.ContentHash, p.Stage, [], DefaultSceneTone.Constants, DefaultSceneTone.Resources));
        using var compiler = new ShaderCompilerService(renderer, preparationAllowed);
        return new(renderer, catalog.ContentHash, compiler.Prepare(v), compiler.Prepare(p), preparationAllowed);
    }
    public RegisteredToneMetadata CopyMetadata() => new(_catalogHash, Vertex.Descriptor.AssetId, Pixel.Descriptor.AssetId, Vertex.Descriptor.ContentHash, Pixel.Descriptor.ContentHash, Vertex.BytecodeHash, Pixel.BytecodeHash, true);
    internal void Verify(RendererSession renderer)
    {
        if (!ReferenceEquals(renderer, Owner)) throw new ArgumentException("Foreign renderer Tone preparation.");
        Owner.VerifyShaderPreparation(); if (_checking) throw new InvalidOperationException("Nonreentrant Tone approval required.");
        _checking = true;
        try { if (!_allowed()) throw new InvalidOperationException("Trusted host off-simulation Tone installation permission required."); }
        finally { _checking = false; }
        Owner.VerifyShaderPreparation();
    }
}

public sealed unsafe partial class RendererSession
{
    private bool _toneOperation;
    internal void BeginToneOperation()
    { VerifyShaderPreparation(); if (_toneOperation) throw new InvalidOperationException("Nonreentrant registered Tone operation required."); _toneOperation = true; }
    internal void EndToneOperation() => _toneOperation = false;
    private ShaderPipelineApi? _shaderPipeline;
    private CopyToneSourceNative? _copyToneSource; private CreateRegisteredSceneNative? _createRegisteredScene; private ReplaceToneNative? _replaceTone;
    private void EnsureShaderPipeline()
    {
        Verify(); if (_shaderPipeline is not null) return;
        if (sizeof(ShaderPairNative) != 32 || sizeof(ShaderPipelineApi) != 40) throw new PlatformNotSupportedException("Shader pipeline x64 ABI.");
        ShaderPipelineApi api = default; PluginError error = default;
        PluginModule.Check(Module.Id, "query_shader_pipeline", Module.ReadFunction<QuerySceneRender>(144)(Module.Context, 9, &api, 40, &error), error);
        if (api.Size != 40 || api.Version != 1 || api.Caps != 1 || api.Source == 0 || api.Create == 0 || api.Replace == 0) throw new InvalidOperationException("Shader pipeline query9/API1 mismatch.");
        _copyToneSource = Marshal.GetDelegateForFunctionPointer<CopyToneSourceNative>(api.Source); _createRegisteredScene = Marshal.GetDelegateForFunctionPointer<CreateRegisteredSceneNative>(api.Create); _replaceTone = Marshal.GetDelegateForFunctionPointer<ReplaceToneNative>(api.Replace); _shaderPipeline = api;
    }
    internal string CopyDefaultToneSource()
    {
        EnsureShaderPipeline(); var bytes = new byte[ShaderContractCodec.MaxSourceBytes]; uint count = 0; PluginError error = default;
        fixed (byte* output = bytes) PluginModule.Check(Module.Id, "copy_tone_source", _copyToneSource!(Module.Context, Handle, output, (uint)bytes.Length, &count, &error), error);
        if (count is 0 or > ShaderContractCodec.MaxSourceBytes) throw new InvalidOperationException("Tone source response budget.");
        return new UTF8Encoding(false, true).GetString(bytes, 0, (int)count);
    }
    internal void CreateRegisteredScene(ScenePipelineSession session, uint resolution, RegisteredSceneTone tone)
    {
        BeginToneOperation(); try { CreateRegisteredSceneCore(session, resolution, tone); } finally { EndToneOperation(); }
    }
    private void CreateRegisteredSceneCore(ScenePipelineSession session, uint resolution, RegisteredSceneTone tone)
    {
        EnsureShaderPipeline(); tone.Verify(this);
        if (session.Owner != this || session.Key.Value != 0) throw new ArgumentException("Foreign/already installed scene.");
        byte[] vertex = tone.Vertex.CopyBytecode(), pixel = tone.Pixel.CopyBytecode(); PluginError error = default; GpuMeshKey key = default;
        SceneDescriptionV4 scene = new() { Size = 16, Width = session.Plan.Width, Height = session.Plan.Height, Resolution = resolution };
        _scenePipelines.Add(session);
        try {
            fixed (byte* v = vertex) fixed (byte* p = pixel) { ShaderPairNative pair = new() { Size = 32, Version = 1, VertexBytes = (uint)vertex.Length, PixelBytes = (uint)pixel.Length, Vertex = v, Pixel = p };
                PluginModule.Check(Module.Id, "create_registered_scene", _createRegisteredScene!(Module.Context, Handle, &scene, &pair, &key, &error), error); }
            session.Key = key;
        } catch { _scenePipelines.Remove(session); throw; }
    }
    internal void ReplaceTone(ScenePipelineSession session, RegisteredSceneTone tone)
    {
        BeginToneOperation(); try { ReplaceToneCore(session, tone); } finally { EndToneOperation(); }
    }
    private void ReplaceToneCore(ScenePipelineSession session, RegisteredSceneTone tone)
    {
        EnsureShaderPipeline(); ArgumentNullException.ThrowIfNull(tone); tone.Verify(this);
        if (session.Owner != this || !_scenePipelines.Contains(session) || session.Key.Value == 0) throw new ArgumentException("Foreign/closed scene Tone installation.");
        byte[] vertex = tone.Vertex.CopyBytecode(), pixel = tone.Pixel.CopyBytecode(); PluginError error = default;
        fixed (byte* v = vertex) fixed (byte* p = pixel) { ShaderPairNative pair = new() { Size = 32, Version = 1, VertexBytes = (uint)vertex.Length, PixelBytes = (uint)pixel.Length, Vertex = v, Pixel = p };
            PluginModule.Check(Module.Id, "replace_registered_tone", _replaceTone!(Module.Context, Handle, session.Key, &pair, &error), error); }
    }
}
