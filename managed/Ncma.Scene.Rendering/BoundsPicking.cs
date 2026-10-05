using System.Numerics;
namespace Ncma.Scene.Rendering;
using Vector3=System.Numerics.Vector3;

public static class BoundsPicking
{
    public static (Vector3 Origin,Vector3 Direction) Ray(SceneCameraView camera,float u,float v)
    {
        if(!float.IsFinite(u)||!float.IsFinite(v)||u is <0 or >1||v is <0 or >1||!Matrix4x4.Invert(camera.ViewProjection,out var inverse))throw new ArgumentException("Invalid pick camera/coordinates.");
        float x=(u-camera.Data.ViewportX)/camera.Data.ViewportWidth*2-1,y=1-(v-camera.Data.ViewportY)/camera.Data.ViewportHeight*2;
        Vector3 Unproject(float z){var p=Vector4.Transform(new Vector4(x,y,z,1),inverse);if(!float.IsFinite(p.W)||Math.Abs(p.W)<1e-8f)throw new ArgumentException("Invalid unprojection.");return new(p.X/p.W,p.Y/p.W,p.Z/p.W);}
        var near=Unproject(0);var difference=Unproject(1)-near;if(!float.IsFinite(difference.LengthSquared())||difference.LengthSquared()<1e-10f)throw new ArgumentException("Invalid pick ray.");return (near,Vector3.Normalize(difference));
    }
    public static bool Hit(Vector3 min,Vector3 max,Matrix4x4 model,Vector3 origin,Vector3 direction,out float distance)
    {
        static bool Finite(Vector3 p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z);
        if(!Finite(min)||!Finite(max)||!Finite(origin)||!Finite(direction)||direction.LengthSquared()<1e-10f||min.X>max.X||min.Y>max.Y||min.Z>max.Z||!SceneRenderValidation.Finite(model))throw new ArgumentException("Invalid finite pick bounds/ray.");
        if(!Matrix4x4.Invert(model,out var inverse))throw new ArgumentException("Invalid pick model.");
        var o=Vector3.Transform(origin,inverse);var d=Vector3.TransformNormal(direction,inverse);float near=0,far=float.PositiveInfinity;
        bool Axis(float low,float high,float pos,float ray){if(Math.Abs(ray)<1e-8f)return pos>=low&&pos<=high;float a=(low-pos)/ray,b=(high-pos)/ray;if(a>b)(a,b)=(b,a);near=Math.Max(near,a);far=Math.Min(far,b);return near<=far;}
        bool hit=Axis(min.X,max.X,o.X,d.X)&&Axis(min.Y,max.Y,o.Y,d.Y)&&Axis(min.Z,max.Z,o.Z,d.Z);distance=near;return hit;
    }
}
