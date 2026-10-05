using System.Buffers.Binary;
using Ncma.Assets;
namespace Ncma.Rendering;

// Original source attributes, never CPU bind-pose/deformed vertices. Prepared once off-frame.
public sealed class SkinUploadData
{
    private readonly byte[] _vertices;
    internal MeshUploadData Attributes { get; }
    public ReadOnlySpan<byte> Vertices => _vertices;
    public ReadOnlySpan<uint> Indices => Attributes.Indices;
    public ReadOnlySpan<MeshDrawRange> Ranges => Attributes.Ranges;
    public ReadOnlySpan<MeshUploadDiagnostic> Diagnostics => Attributes.Diagnostics;
    public int BindingCount { get; }
    public int VertexCount => Attributes.VertexCount;
    public bool CanUseNormalMap => Attributes.CanUseNormalMap;
    private SkinUploadData(byte[] vertices, MeshUploadData attributes, int bindings)
    { _vertices=vertices;Attributes=attributes;BindingCount=bindings; }
    public static SkinUploadData Prepare(MeshPayload source)
    {
        var mesh=ModelPayloadCodec.DecodeMesh(ModelPayloadCodec.Encode(source));
        if(!mesh.Skinned || mesh.Bindings.Length is <1 or >1024)throw new ArgumentException("Original skinned source required.");
        var attributes=MeshUploadData.PrepareStatic(mesh with {Skinned=false,BoneCount=0,Bindings=[],Vertices=mesh.Vertices.Select(v=>v with{Joints=default,Weights=default}).ToArray()});
        if((long)mesh.Vertices.Length*128+attributes.Indices.Length*4L>MeshUploadData.MaxUploadBytes)throw new ArgumentException("Resident source/output skin byte budget.");
        var data=new byte[checked(mesh.Vertices.Length*80)];
        for(int i=0;i<mesh.Vertices.Length;i++){
            var row=data.AsSpan(i*80,80);attributes.Vertices.Slice(i*48,48).CopyTo(row);var v=mesh.Vertices[i];
            Put(0,v.Joints.X,v.Weights.X);Put(1,v.Joints.Y,v.Weights.Y);Put(2,v.Joints.Z,v.Weights.Z);Put(3,v.Joints.W,v.Weights.W);
            void Put(int k,ushort joint,float weight){BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i*80+48+k*4),weight==0?0u:joint);BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(i*80+64+k*4),weight);}
        }
        return new(data,attributes,mesh.Bindings.Length);
    }
}
