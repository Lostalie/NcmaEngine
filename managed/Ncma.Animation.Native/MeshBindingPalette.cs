using System.Numerics;
using Ncma.Assets;
namespace Ncma.Animation.Native;

// Owned, immutable mesh-local bindings. Different meshes of the SAME rig may have different inverse binds.
// No skeleton-wide inverse-bind substitution and no per-frame CPU vertex stream.
public sealed class MeshBindingPalette
{
    private readonly (int Bone,Matrix4x4 GeometryToBone)[] _bindings;
    public int BoneCount{get;}
    public int BindingCount=>_bindings.Length;
    public MeshBindingPalette(MeshPayload mesh,int boneCount)
    {
        var value=ModelPayloadCodec.DecodeMesh(ModelPayloadCodec.Encode(mesh));
        if(!value.Skinned || value.BoneCount!=boneCount || value.Bindings.Length is <1 or >1024)throw new ArgumentException("Skinned mesh/rig/palette budget mismatch.");
        BoneCount=boneCount;_bindings=new (int,Matrix4x4)[value.Bindings.Length];
        for(int i=0;i<_bindings.Length;i++){var b=value.Bindings[i];var m=b.GeometryToBone;
            var matrix=new Matrix4x4(m[0],m[1],m[2],m[3],m[4],m[5],m[6],m[7],m[8],m[9],m[10],m[11],m[12],m[13],m[14],m[15]);
            Validate(matrix);_bindings[i]=((int)b.Bone,matrix);}
    }
    public void Compose(ReadOnlySpan<Matrix4x4> boneModels,Span<Matrix4x4> palette)
    {
        if(boneModels.Length!=BoneCount || palette.Length<BindingCount || boneModels.Overlaps(palette))throw new ArgumentException("Palette capacity/aliasing/rig count.");
        // Validate COMPLETE candidate before publishing; stack scratch is bounded to 64KiB.
        Span<Matrix4x4> candidate=stackalloc Matrix4x4[BindingCount];
        for(int i=0;i<BindingCount;i++){var b=_bindings[i];var result=b.GeometryToBone*boneModels[b.Bone];Validate(result);candidate[i]=result;}
        candidate.CopyTo(palette);
    }
    private static void Validate(Matrix4x4 m)
    {
        ReadOnlySpan<Matrix4x4> matrix=[m];foreach(float f in System.Runtime.InteropServices.MemoryMarshal.Cast<Matrix4x4,float>(matrix))if(!float.IsFinite(f))throw new ArgumentException("Nonfinite binding matrix.");
        if(m.M14!=0 || m.M24!=0 || m.M34!=0 || m.M44!=1 || !Matrix4x4.Invert(m,out var inverse) || m.GetDeterminant()<=0)throw new ArgumentException("Binding palette must be positive invertible affine.");
        ReadOnlySpan<Matrix4x4> inv=[inverse];foreach(float f in System.Runtime.InteropServices.MemoryMarshal.Cast<Matrix4x4,float>(inv))if(!float.IsFinite(f))throw new ArgumentException("Binding inverse overflow.");
    }
}
