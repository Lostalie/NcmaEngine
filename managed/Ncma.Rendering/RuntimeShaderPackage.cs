using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ncma.Rendering;

public enum RuntimeShaderRole { UiVertex, UiPixel, GeometryVertex, GeometryPixel, ToneVertex, TonePixel, ShadowVertex, ShadowPixel, SkinCompute, EnvironmentGeometryVertex, EnvironmentGeometryPixel }
public sealed record RuntimeShaderInput(RuntimeShaderRole Role, CompiledShader Shader);
public readonly record struct RuntimeShaderMetadata(RuntimeShaderRole Role, Guid AssetId, string AuthorContentHash,
    string BindingContractHash, string BytecodeHash, int BytecodeBytes, uint CompilerVersion, uint CompilerFlags);

// Source-free, immutable CPU artifact. Preflight is NOT native reflection, GPU validation, an
// execution grant or a package signature. C4-B must validate actual bindings before installation.
public sealed class RuntimeShaderPackage
{
    public const int Version = 1, HeaderBytes = 60, EntryHeaderBytes = 128, MaxBytes = 8 * 1024 * 1024, MaxPrograms = 9;
    private const uint Magic = 0x3153434e; // NCS1, little endian
    private readonly byte[] _bytes;
    private readonly Entry[] _entries;
    private sealed record Entry(RuntimeShaderMetadata Metadata, int Offset);
    public string ContentHash { get; }
    public ShaderProfile Profile { get; }
    public bool Shadows { get; }
    public bool Skinning { get; }
    public int Count => _entries.Length;
    public bool GpuValidated => false;
    private RuntimeShaderPackage(byte[] bytes, Entry[] entries, ShaderProfile profile, bool shadows, bool skin)
    { _bytes=bytes; _entries=entries; Profile=profile; Shadows=shadows; Skinning=skin; ContentHash=Hash(bytes); }
    public byte[] CopyBytes() => (byte[])_bytes.Clone();
    public RuntimeShaderMetadata[] CopyPage(int page)
    {
        if(page<0 || page>(Count-1)/ShaderCatalog.PageSize) Fail("page");
        return _entries.Skip(page*ShaderCatalog.PageSize).Take(ShaderCatalog.PageSize).Select(e=>e.Metadata).ToArray();
    }
    public byte[] CopyBytecode(RuntimeShaderRole role)
    {
        var entry=_entries.FirstOrDefault(e=>e.Metadata.Role==role);
        if(entry is null) Fail("role_missing");
        return _bytes.AsSpan(entry!.Offset,entry.Metadata.BytecodeBytes).ToArray();
    }
    internal static string Hash(ReadOnlySpan<byte> bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string code)=>throw new ShaderContractException("runtime_"+code,"package");
    private static RuntimeShaderRole[] Closure(ShaderProfile profile,bool shadows,bool skin)
    {
        if(profile==ShaderProfile.Flat2D && !shadows && !skin) return [RuntimeShaderRole.UiVertex,RuntimeShaderRole.UiPixel];
        if(profile is not (ShaderProfile.Scene3D or ShaderProfile.SceneEnvironment)) Fail("profile");
        return new[]{profile==ShaderProfile.SceneEnvironment?RuntimeShaderRole.EnvironmentGeometryVertex:RuntimeShaderRole.GeometryVertex,profile==ShaderProfile.SceneEnvironment?RuntimeShaderRole.EnvironmentGeometryPixel:RuntimeShaderRole.GeometryPixel,RuntimeShaderRole.ToneVertex,RuntimeShaderRole.TonePixel}
            .Concat(shadows?new[]{RuntimeShaderRole.ShadowVertex,RuntimeShaderRole.ShadowPixel}:[])
            .Concat(skin?new[]{RuntimeShaderRole.SkinCompute}:[]).ToArray();
    }
    // Only compiler-created immutable artifacts, exact role closure, no arbitrary dependency graph.
    // Caller owns the trusted off-frame cook and any file/transaction publication; this API has no IO.
    public static RuntimeShaderPackage Cook(ShaderProfile profile,bool shadows,bool skin,IEnumerable<RuntimeShaderInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);var roles=Closure(profile,shadows,skin);
        var supplied=inputs.Take(MaxPrograms+1).ToArray();
        if(supplied.Length!=roles.Length || supplied.Any(x=>x is null || x.Shader is null) ||
            supplied.Select(x=>x.Role).Distinct().Count()!=roles.Length || supplied.Any(x=>!roles.Contains(x.Role))) Fail("closure");
        var ordered=roles.Select(r=>supplied.Single(x=>x.Role==r)).ToArray();
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        writer.Write(new byte[HeaderBytes]);var ids=new HashSet<Guid>();
        foreach(var input in ordered){
            var shader=input.Shader;var d=shader.Descriptor;var layout=ShaderRuntimeLayout.For(input.Role,shadows,profile);
            if(!ids.Add(d.AssetId) || d.Profile!=layout.Profile || d.Stage!=layout.Stage || d.CopyDefinition().Dependencies.Length!=0) Fail("descriptor");
            if(shader.CompilerVersion!=47 || shader.CompilerFlags!=0x48800) Fail("compiler");
            ShaderBindingValidation.Validate(d,new(d.ContentHash,d.Stage,layout.Inputs,layout.Constants,layout.Resources));
            ShaderReflectionValidation.Validate(d,shader);
            byte[] code=shader.CopyBytecode();ValidateDxbc(code,layout.Stage);
            if(stream.Length+EntryHeaderBytes+code.Length>MaxBytes) Fail("budget");
            writer.Write((int)input.Role);writer.Write(d.AssetId.ToByteArray());writer.Write(Convert.FromHexString(d.ContentHash));
            writer.Write(Convert.FromHexString(layout.Hash));writer.Write(shader.CompilerVersion);writer.Write(shader.CompilerFlags);
            writer.Write(code.Length);writer.Write(SHA256.HashData(code));writer.Write(code);
        }
        byte[] bytes=stream.ToArray();Write(bytes,0,Magic);Write(bytes,4,Version);Write(bytes,8,1); // DX11 only
        Write(bytes,12,(uint)profile);Write(bytes,16,(shadows?1u:0u)|(skin?2u:0u));Write(bytes,20,(uint)roles.Length);Write(bytes,24,(uint)bytes.Length);
        SHA256.HashData(bytes.AsSpan(HeaderBytes)).CopyTo(bytes,28);
        return Preflight(bytes,Hash(bytes));
    }
    // expectedHash MUST come from a separately trusted project/package selection, not the blob.
    // Pure managed: before plugin/renderer/gameplay initialization, no native calls or author source.
    public static RuntimeShaderPackage Preflight(ReadOnlySpan<byte> bytes,string expectedHash)
    {
        if(bytes.Length<HeaderBytes || bytes.Length>MaxBytes) Fail("budget");
        // Own one bounded snapshot before parsing or hashing caller-mutable storage.
        byte[] owned=bytes.ToArray();
        if(!ShaderContractCodec.IsHash(expectedHash) || Hash(owned)!=expectedHash) Fail("hash");
        if(Read(owned,0)!=Magic || Read(owned,4)!=Version) Fail("version");
        if(Read(owned,8)!=1) Fail("backend");
        uint profile=Read(owned,12),flags=Read(owned,16);
        if(profile is not (0 or 1 or 3) || flags>3 || Read(owned,24)!=owned.Length) Fail("header");
        var roles=Closure((ShaderProfile)profile,(flags&1)!=0,(flags&2)!=0);
        if(Read(owned,20)!=roles.Length || !SHA256.HashData(owned.AsSpan(HeaderBytes)).AsSpan().SequenceEqual(owned.AsSpan(28,32))) Fail("closure_hash");
        var entries=new Entry[roles.Length];var ids=new HashSet<Guid>();int at=HeaderBytes;
        for(int i=0;i<roles.Length;i++){
            if(at>owned.Length-EntryHeaderBytes) Fail("entry_budget");
            if(Read(owned,at)!=(uint)roles[i]) Fail("role_order");
            Guid id=new(owned.AsSpan(at+4,16));if(id==Guid.Empty || !ids.Add(id)) Fail("identity");
            string author=Convert.ToHexString(owned.AsSpan(at+20,32)),binding=Convert.ToHexString(owned.AsSpan(at+52,32));
            if(owned.AsSpan(at+20,32).IndexOfAnyExcept((byte)0)<0) Fail("identity");
            var layout=ShaderRuntimeLayout.For(roles[i],(flags&1)!=0,(ShaderProfile)profile);
            if(binding!=layout.Hash) Fail("binding_contract");
            uint compiler=Read(owned,at+84),compilerFlags=Read(owned,at+88),length=Read(owned,at+92);
            if(compiler!=47 || compilerFlags!=0x48800) Fail("compiler");
            if(length<32 || length>1048576 || length>owned.Length-at-EntryHeaderBytes) Fail("bytecode_budget");
            string codeHash=Convert.ToHexString(owned.AsSpan(at+96,32));at+=EntryHeaderBytes;
            var code=owned.AsSpan(at,(int)length);if(Hash(code)!=codeHash) Fail("bytecode_hash");
            ValidateDxbc(code,layout.Stage);
            entries[i]=new(new(roles[i],id,author,binding,codeHash,(int)length,compiler,compilerFlags),at);at+=(int)length;
        }
        if(at!=owned.Length) Fail("trailing_bytes");
        return new(owned,entries,(ShaderProfile)profile,(flags&1)!=0,(flags&2)!=0);
    }
    private static uint Read(ReadOnlySpan<byte> bytes,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(at,4));
    private static void Write(Span<byte> bytes,int at,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(bytes.Slice(at,4),value);
    // Structural SM5 envelope only, not instruction/RDEF/signature verification or code safety.
    // Reject debug/private/source chunks; source authoring is never embedded in our O3 compiler output.
    private static void ValidateDxbc(ReadOnlySpan<byte> bytes,ShaderStage stage)
    {
        if(bytes.Length<32 || bytes.Length>1048576 || Read(bytes,0)!=0x43425844 || Read(bytes,20)!=1 || Read(bytes,24)!=bytes.Length) Fail("dxbc_header");
        uint count=Read(bytes,28);if(count is <1 or >16 || 32+count*4>bytes.Length) Fail("dxbc_chunks");
        Span<(int Start,int End)> ranges=stackalloc (int,int)[16];Span<uint> tags=stackalloc uint[16];bool program=false;int cursor=32+(int)count*4;
        for(int i=0;i<count;i++){
            uint offset=Read(bytes,32+i*4);if(offset!=cursor || offset%4!=0 || offset>bytes.Length-8) Fail("dxbc_offset");
            int start=(int)offset;uint tag=Read(bytes,start),size=Read(bytes,start+4);if(size>bytes.Length-start-8) Fail("dxbc_chunk_budget");
            // RDEF, ISGN, OSGN, SHEX, STAT are the closed D3DCompiler47/O3 output.
            if(tag is not (0x46454452 or 0x4e475349 or 0x4e47534f or 0x58454853 or 0x54415453)) Fail("dxbc_chunk_kind");
            int end=start+8+(int)size;
            for(int j=0;j<i;j++)if(tags[j]==tag || start<ranges[j].End && end>ranges[j].Start) Fail("dxbc_overlap");
            tags[i]=tag;ranges[i]=(start,end);cursor=end;
            if(tag==0x58454853){
                uint kind=stage==ShaderStage.Vertex?1u:stage==ShaderStage.Pixel?0u:5u;
                if(size<8 || Read(bytes,start+8)!=(kind<<16|0x50) || (ulong)Read(bytes,start+12)*4!=size) Fail("dxbc_stage");
                program=true;
            }
        }
        if(!program || count!=5 || cursor!=bytes.Length) Fail("dxbc_program");
    }
}

// Same closed layout tables as author compilation. No ShaderDefinition/source reconstruction.
internal sealed record ShaderRuntimeLayout(ShaderProfile Profile,ShaderStage Stage,ShaderVertexInput[] Inputs,
    ShaderConstantBuffer[] Constants,ShaderResourceBinding[] Resources)
{
    public string Hash=>RuntimeShaderPackage.Hash(JsonSerializer.SerializeToUtf8Bytes(new{version=1,Profile,Stage,
        Inputs=Inputs.OrderBy(x=>x.Semantic,StringComparer.Ordinal).ThenBy(x=>x.Index),
        Constants=Constants.OrderBy(x=>x.Slot).Select(x=>x with{Members=x.Members.OrderBy(m=>m.ByteOffset).ToArray()}),
        Resources=Resources.OrderBy(x=>ShaderContractCodec.Namespace(x.Kind)).ThenBy(x=>x.Slot)}));
    public static ShaderRuntimeLayout For(RuntimeShaderRole role,bool shadows,ShaderProfile profile=ShaderProfile.Scene3D)=>role switch{
        RuntimeShaderRole.UiVertex=>new(ShaderProfile.Flat2D,ShaderStage.Vertex,DefaultUiShaders.Inputs,DefaultUiShaders.Constants,[]),
        RuntimeShaderRole.UiPixel=>new(ShaderProfile.Flat2D,ShaderStage.Pixel,[],[],DefaultUiShaders.Resources),
        RuntimeShaderRole.GeometryVertex=>new(ShaderProfile.Scene3D,ShaderStage.Vertex,DefaultSceneShaders.Inputs,DefaultSceneTone.Constants,[]),
        RuntimeShaderRole.ShadowVertex=>new(profile,ShaderStage.Vertex,DefaultSceneShaders.Inputs,DefaultSceneTone.Constants,[]),
        RuntimeShaderRole.EnvironmentGeometryVertex=>new(ShaderProfile.SceneEnvironment,ShaderStage.Vertex,DefaultSceneShaders.Inputs,DefaultEnvironmentSceneShaders.Constants,[]),
        RuntimeShaderRole.EnvironmentGeometryPixel=>new(ShaderProfile.SceneEnvironment,ShaderStage.Pixel,[],DefaultEnvironmentSceneShaders.Constants,DefaultEnvironmentSceneShaders.Resources(shadows)),
        RuntimeShaderRole.GeometryPixel=>new(ShaderProfile.Scene3D,ShaderStage.Pixel,[],DefaultSceneTone.Constants,DefaultSceneShaders.Resources(shadows)),
        RuntimeShaderRole.ToneVertex=>new(profile,ShaderStage.Vertex,DefaultSceneTone.Inputs,[],[]),
        RuntimeShaderRole.TonePixel or RuntimeShaderRole.ShadowPixel=>new(profile,ShaderStage.Pixel,[],DefaultSceneTone.Constants,DefaultSceneTone.Resources),
        RuntimeShaderRole.SkinCompute=>new(ShaderProfile.Skinning,ShaderStage.Compute,[],DefaultSkinShader.Constants,DefaultSkinShader.Resources),
        _=>throw new ShaderContractException("runtime_role","package")
    };
}
