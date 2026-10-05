using System.Buffers.Binary;
using System.Numerics;
using Ncma.Assets;
namespace Ncma.Rendering;

// Explicit OFF-frame bind-pose preparation, not animated GPU skinning. Never silently strip skin.
// Layout2: baked position/normal/UV/tangent@0..47, original palette joints@48,
// weights@56, zero reserved@72..79. Source MSH1 and binding palette remain independently owned.
public sealed class BindPoseMeshUploadData
{
    public const int LayoutVersion=2, VertexStride=80;
    private readonly byte[] _vertices, _source, _palette;
    private readonly MeshUploadData _geometry;
    public ReadOnlySpan<byte> Vertices => _vertices;
    public ReadOnlySpan<byte> SourceMesh => _source;
    public ReadOnlySpan<byte> Palette => _palette; // LE column-major float16 per geometry binding, NOT per bone.
    public ReadOnlySpan<uint> Indices => _geometry.Indices;
    public ReadOnlySpan<MeshDrawRange> Ranges => _geometry.Ranges;
    public ReadOnlySpan<MeshUploadDiagnostic> Diagnostics => _geometry.Diagnostics;
    public MeshBounds Bounds => _geometry.Bounds;
    public int VertexCount => _vertices.Length/VertexStride;
    public int BindingCount => _palette.Length/64;
    // Normal-map equivalence to DCC/animated skin is not asserted by this bind-pose preview.
    public bool CanUseNormalMap => false;
    public long UploadBytes => _vertices.LongLength+Indices.Length*4L;
    public long OwnedBytes => UploadBytes+_source.LongLength+_palette.LongLength+_geometry.Vertices.Length;
    private BindPoseMeshUploadData(byte[] vertices,byte[] source,byte[] palette,MeshUploadData geometry)
    { _vertices=vertices;_source=source;_palette=palette;_geometry=geometry; }
    public static BindPoseMeshUploadData Prepare(MeshPayload source,SkeletonPayload skeleton,CancellationToken cancellation=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(skeleton);cancellation.ThrowIfCancellationRequested();
        if(!source.Skinned)throw new ArgumentException("Explicit bind-pose preparation requires a skinned mesh.");
        if(source.Vertices is null||source.Tangents is null||source.Indices is null||source.TriangleMaterials is null||source.Bindings is null)
            throw new ArgumentException("Missing bind-pose input arrays.");
        // Bound combined retained source/CPU/GPU preparation before allocating the upload buffers.
        long estimate=checked(source.Vertices.LongLength*(VertexStride+48+56L)+source.Tangents.LongLength*16+
            source.Indices.LongLength*8+source.TriangleMaterials.LongLength*4+source.Bindings.LongLength*(68+64L)+36);
        if(estimate>MeshUploadData.MaxUploadBytes)throw new ArgumentException("Combined bind-pose preparation budget exceeded.");
        byte[] encoded=ModelPayloadCodec.Encode(source);
        var mesh=ModelPayloadCodec.DecodeMesh(encoded);
        var bones=ModelPayloadCodec.DecodeSkeleton(ModelPayloadCodec.Encode(skeleton)).Bones;
        if(bones.Length!=mesh.BoneCount)throw new ArgumentException("Mesh/skeleton bone count mismatch.");
        cancellation.ThrowIfCancellationRequested();
        var global=new Matrix4x4[bones.Length];
        for(int i=0;i<bones.Length;i++) {
            var b=bones[i];var t=b.BindLocal;
            var local=Matrix4x4.CreateScale(t.Scale)*Matrix4x4.CreateFromQuaternion(t.Rotation)*Matrix4x4.CreateTranslation(t.Position);
            global[i]=b.Parent<0?local:local*global[b.Parent];
        }
        var matrices=new Matrix4x4[mesh.Bindings.Length];byte[] palette=new byte[matrices.Length*64];
        for(int i=0;i<matrices.Length;i++) {
            if((i&1023)==0)cancellation.ThrowIfCancellationRequested();
            var b=mesh.Bindings[i];var c=b.GeometryToBone;
            matrices[i]=new Matrix4x4(c[0],c[1],c[2],c[3],c[4],c[5],c[6],c[7],c[8],c[9],c[10],c[11],c[12],c[13],c[14],c[15])*global[b.Bone];
            var m=matrices[i];ReadOnlySpan<float> values=[m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
            for(int k=0;k<16;k++) {if(!float.IsFinite(values[k]))throw new ArgumentException("Binding matrix overflow.");BinaryPrimitives.WriteSingleLittleEndian(palette.AsSpan(i*64+k*4),values[k]);}
        }
        var baked=new ImportVertex[mesh.Vertices.Length];
        for(int i=0;i<baked.Length;i++) {
            if((i&1023)==0)cancellation.ThrowIfCancellationRequested();var v=mesh.Vertices[i];
            Matrix4x4 blend=default;
            Add(v.Joints.X,v.Weights.X);Add(v.Joints.Y,v.Weights.Y);Add(v.Joints.Z,v.Weights.Z);Add(v.Joints.W,v.Weights.W);
            float determinant=blend.GetDeterminant();
            if(!float.IsFinite(determinant)||determinant<=.00000001f||!Matrix4x4.Invert(blend,out var inverse))
                throw new ArgumentException("Singular/reflected/overflow blended bind transform is unsupported.");
            var position=Vector3.Transform(v.Position,blend);
            var normal=Vector3.Normalize(Vector3.TransformNormal(v.Normal,Matrix4x4.Transpose(inverse)));
            if(!float.IsFinite(position.X)||!float.IsFinite(position.Y)||!float.IsFinite(position.Z))throw new ArgumentException("Bind-pose position overflow.");
            baked[i]=v with {Position=position,Normal=normal,Joints=default,Weights=Vector4.Zero};
            void Add(ushort joint,float weight) {if(weight!=0)blend+=matrices[joint]*weight;}
        }
        // Deliberate explicit preview conversion; original MSH1/weights are retained above.
        // Rebuild orthogonal preview tangents; do not silently promise animated tangent/DCC equivalence.
        var geometry=MeshUploadData.PrepareStatic(mesh with {Skinned=false,BoneCount=0,Bindings=[],Vertices=baked,Tangents=[]},cancellation);
        byte[] vertices=new byte[checked(baked.Length*VertexStride)];
        for(int i=0;i<baked.Length;i++) {
            if((i&1023)==0)cancellation.ThrowIfCancellationRequested();Span<byte> row=vertices.AsSpan(i*VertexStride,VertexStride);
            geometry.Vertices.Slice(i*48,48).CopyTo(row);var v=mesh.Vertices[i];
            BinaryPrimitives.WriteUInt16LittleEndian(row[48..],v.Joints.X);BinaryPrimitives.WriteUInt16LittleEndian(row[50..],v.Joints.Y);
            BinaryPrimitives.WriteUInt16LittleEndian(row[52..],v.Joints.Z);BinaryPrimitives.WriteUInt16LittleEndian(row[54..],v.Joints.W);
            BinaryPrimitives.WriteSingleLittleEndian(row[56..],v.Weights.X);BinaryPrimitives.WriteSingleLittleEndian(row[60..],v.Weights.Y);
            BinaryPrimitives.WriteSingleLittleEndian(row[64..],v.Weights.Z);BinaryPrimitives.WriteSingleLittleEndian(row[68..],v.Weights.W);
        }
        var result=new BindPoseMeshUploadData(vertices,encoded,palette,geometry);
        if(result.OwnedBytes>MeshUploadData.MaxUploadBytes)throw new ArgumentException("Combined bind-pose preparation budget exceeded.");
        cancellation.ThrowIfCancellationRequested();return result;
    }
}
