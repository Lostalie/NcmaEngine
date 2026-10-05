using System.Numerics;
using Ncma.Scene.Rendering;
namespace Ncma.Editor.Services;
using Vector3=System.Numerics.Vector3;

// Independent local browser state. No World, CameraData component write or scene history.
public sealed class EditorOrbitCamera
{
    public Vector3 Target {get;private set;}=new(0,0,-3);
    public float Yaw {get;private set;}=MathF.Atan2(6,11);
    public float Pitch {get;private set;}=MathF.Asin(4/MathF.Sqrt(173));
    public float Distance {get;private set;}=MathF.Sqrt(173);
    public ulong Revision {get;private set;}
    public void Orbit(float yaw,float pitch)
    {Finite(yaw,pitch);Yaw=Math.Clamp(yaw,-MathF.PI,MathF.PI);Pitch=Math.Clamp(pitch,-1.5f,1.5f);Revision=checked(Revision+1);}
    public void Pan(float right,float up)
    {Finite(right,up);if(Math.Abs(right)>1000||Math.Abs(up)>1000)throw new ArgumentException("Pan budget.");var r=new Vector3(MathF.Cos(Yaw),0,-MathF.Sin(Yaw));var u=Vector3.Normalize(Vector3.Cross(ViewDirection(),r));var next=Target+r*right+u*up;if(next.LengthSquared()>1e12f)throw new ArgumentException("Browser position budget.");Target=next;Revision=checked(Revision+1);}
    public void Zoom(float distance)
    {Finite(distance,0);if(distance is <.05f or >10000)throw new ArgumentException("Zoom range.");Distance=distance;Revision=checked(Revision+1);}
    private Vector3 ViewDirection()=>new(MathF.Sin(Yaw)*MathF.Cos(Pitch),MathF.Sin(Pitch),MathF.Cos(Yaw)*MathF.Cos(Pitch));
    public SceneCameraView View(uint width,uint height)
    {if(width is 0 or >16384||height is 0 or >16384)throw new ArgumentException("Camera target dimensions.");var position=Target+ViewDirection()*Distance;return new(Guid.Empty,Matrix4x4.CreateLookAt(position,Target,Vector3.UnitY)*Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/3,width/(float)height,.1f,1000),position,CameraData.Default);}
    private static void Finite(float a,float b){if(!float.IsFinite(a)||!float.IsFinite(b))throw new ArgumentException("Finite camera values required.");}
}
