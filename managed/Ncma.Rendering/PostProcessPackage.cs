using System.Buffers.Binary;
using System.Security.Cryptography;
namespace Ncma.Rendering;

public sealed record PostProgramDefinition(int Inputs,ShaderDescriptor Vertex,ShaderDescriptor Pixel);
internal sealed record PostProgramData(PostProgramMetadata Metadata,byte[] Vertex,byte[] Pixel);
public static class PostProgramContract
{
    internal static readonly ShaderVertexInput[] Inputs=[new("SV_VERTEXID",0,ShaderScalar.UInt32,1,-1)];
    internal static readonly ShaderConstantBuffer[] Constants=[new("Post",0,16,[new("Params",ShaderScalar.Float32,1,4,ShaderMatrixOrder.None,0,1,0)])];
    internal static ShaderResourceBinding[] Resources(int inputs)=>inputs==1?[new("Input0",ShaderResourceKind.Texture2D,ShaderResourceAccess.ReadOnly,0,1,0),new("LinearClamp",ShaderResourceKind.Sampler,ShaderResourceAccess.ReadOnly,0,1,0)]:[new("Input0",ShaderResourceKind.Texture2D,ShaderResourceAccess.ReadOnly,0,1,0),new("Input1",ShaderResourceKind.Texture2D,ShaderResourceAccess.ReadOnly,1,1,0),new("LinearClamp",ShaderResourceKind.Sampler,ShaderResourceAccess.ReadOnly,0,1,0)];
    public static void Validate(PostProgramDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);ArgumentNullException.ThrowIfNull(definition.Vertex);ArgumentNullException.ThrowIfNull(definition.Pixel);
        if(definition.Inputs is <1 or >2||definition.Vertex.Profile!=ShaderProfile.HdrPostProcess||definition.Pixel.Profile!=ShaderProfile.HdrPostProcess||definition.Vertex.Stage!=ShaderStage.Vertex||definition.Pixel.Stage!=ShaderStage.Pixel||definition.Vertex.CopyDefinition().Dependencies.Length!=0||definition.Pixel.CopyDefinition().Dependencies.Length!=0)throw new ArgumentException("Closed HDR post program contract.");
        ShaderBindingValidation.Validate(definition.Vertex,new(definition.Vertex.ContentHash,ShaderStage.Vertex,Inputs,[],[]));
        ShaderBindingValidation.Validate(definition.Pixel,new(definition.Pixel.ContentHash,ShaderStage.Pixel,[],Constants,Resources(definition.Inputs)));
    }
}

// Independent NPH1 source-free artifact, NOT an extension/reinterpretation of frozen NCS1.
// Hash proves identity, not provenance/permission. Actual whole-program reflection occurs on every admission.
public sealed class PostProcessPackage
{
    public const int MaxBytes=8*1024*1024;
    private readonly byte[] _bytes;internal PostProgramData[] Programs{get;}
    public string ContentHash{get;}public bool GpuValidated=>false;
    private PostProcessPackage(byte[] bytes,PostProgramData[] programs){_bytes=bytes;Programs=programs;ContentHash=Hash(bytes);}
    private static string Hash(ReadOnlySpan<byte> bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    public byte[] CopyBytes()=>(byte[])_bytes.Clone();
    public PostProgramMetadata[] CopyPage()=>Programs.Select(p=>p.Metadata).ToArray();
    public static PostProcessPackage Cook(RendererSession renderer,ShaderCatalog catalog,IEnumerable<PostProgramDefinition> definitions,Func<bool> preparationAllowed)
    {
        ArgumentNullException.ThrowIfNull(renderer);ArgumentNullException.ThrowIfNull(catalog);ArgumentNullException.ThrowIfNull(definitions);ArgumentNullException.ThrowIfNull(preparationAllowed);
        renderer.BeginToneOperation();try{
            if(catalog.Profile!=ShaderProfile.HdrPostProcess)throw new ArgumentException("HDR post catalog required.");var selected=definitions.Take(9).ToArray();if(selected.Length is <1 or >8)throw new ArgumentException("Post program budget.");
            using var compiler=new ShaderCompilerService(renderer,preparationAllowed);using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
            writer.Write(0x3148504e);writer.Write(1);writer.Write(0);writer.Write(selected.Length);writer.Write(47);writer.Write(0x48800);
            foreach(var p in selected){PostProgramContract.Validate(p);_=catalog.Require(p.Vertex.AssetId,p.Vertex.ContentHash);_=catalog.Require(p.Pixel.AssetId,p.Pixel.ContentHash);
                var v=compiler.Prepare(p.Vertex).CopyBytecode();var f=compiler.Prepare(p.Pixel).CopyBytecode();
                if(stream.Length+92+v.Length+f.Length>MaxBytes)throw new ArgumentException("Post bytecode budget.");
                writer.Write(p.Pixel.AssetId.ToByteArray());writer.Write(p.Inputs);writer.Write(v.Length);writer.Write(f.Length);writer.Write(Convert.FromHexString(p.Vertex.ContentHash));writer.Write(Convert.FromHexString(p.Pixel.ContentHash));writer.Write(v);writer.Write(f);
            }
            if(!preparationAllowed())throw new InvalidOperationException("Post preparation revoked.");renderer.VerifyShaderPreparation();
            var bytes=stream.ToArray();BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8),bytes.Length);return Preflight(bytes,Hash(bytes));
        }finally{renderer.EndToneOperation();}
    }
    public static PostProcessPackage Preflight(ReadOnlySpan<byte> source,string expectedHash)
    {
        if(source.Length is <24 or >MaxBytes)throw new ArgumentException("Post package bytes.");
        byte[] bytes=source.ToArray();if(!ShaderContractCodec.IsHash(expectedHash)||Hash(bytes)!=expectedHash)throw new ArgumentException("Post package hash.");
        using var stream=new MemoryStream(bytes,false);using var reader=new BinaryReader(stream);
        if(reader.ReadInt32()!=0x3148504e||reader.ReadInt32()!=1||reader.ReadInt32()!=bytes.Length)throw new ArgumentException("Post package version/length.");int count=reader.ReadInt32();
        if(count is <1 or >8||reader.ReadInt32()!=47||reader.ReadInt32()!=0x48800)throw new ArgumentException("Post package count/compiler.");
        var programs=new PostProgramData[count];var ids=new HashSet<Guid>();
        for(int i=0;i<count;i++){
            if(stream.Length-stream.Position<92)throw new ArgumentException("Post truncated entry.");var id=new Guid(reader.ReadBytes(16));int inputs=reader.ReadInt32(),vl=reader.ReadInt32(),pl=reader.ReadInt32();
            if(id==Guid.Empty||!ids.Add(id)||inputs is <1 or >2||vl is <4 or >1048576||pl is <4 or >1048576||stream.Length-stream.Position<64L+vl+pl)throw new ArgumentException("Post entry bounds/identity.");
            string vh=Convert.ToHexString(reader.ReadBytes(32)),ph=Convert.ToHexString(reader.ReadBytes(32));byte[] v=reader.ReadBytes(vl),p=reader.ReadBytes(pl);
            RuntimeShaderPackage.ValidateDxbc(v,ShaderStage.Vertex);RuntimeShaderPackage.ValidateDxbc(p,ShaderStage.Pixel);
            programs[i]=new(new(id,inputs,vh,ph,Hash(v),Hash(p)),v,p);
        }
        if(stream.Position!=stream.Length)throw new ArgumentException("Post trailing bytes.");return new(bytes,programs);
    }
}
