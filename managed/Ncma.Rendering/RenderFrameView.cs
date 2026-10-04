using System.Numerics;
using Ncma.Runtime;
namespace Ncma.Rendering;
// Immutable value copied on World owner thread. It contains no live GameObject/World reference.
public readonly record struct RenderFrameView(Guid WorldId,ulong Revision,Guid ObjectId,TransformData? Transform)
{
    public bool HasReferenceModel => Transform is not null;
    public void CopyColumnMajorModel(Span<float> output)
    {
        if(output.Length<16)throw new ArgumentException("Model output needs 16 floats.");
        if(Transform is not { } transform)throw new InvalidOperationException("Non-spatial object has no reference model.");
        transform=TransformData.Validate(transform);
        // System.Numerics uses row vectors; row-major flattening is the equivalent transpose
        // interpreted as Eigen/HLSL column-major with column vectors (translation at 12..14).
        var m=Matrix4x4.CreateScale(transform.Scale)*Matrix4x4.CreateFromQuaternion(transform.Rotation)*Matrix4x4.CreateTranslation(transform.Position);
        output[0]=m.M11;output[1]=m.M12;output[2]=m.M13;output[3]=m.M14;
        output[4]=m.M21;output[5]=m.M22;output[6]=m.M23;output[7]=m.M24;
        output[8]=m.M31;output[9]=m.M32;output[10]=m.M33;output[11]=m.M34;
        output[12]=m.M41;output[13]=m.M42;output[14]=m.M43;output[15]=m.M44;
    }
    public static RenderFrameView Capture(World world,Guid objectId)
    {
        var item=world.FindObject(objectId);
        return new(world.Identity,world.Revision,objectId,item.Has<TransformData>()?item.Get<TransformData>():null);
    }
}
