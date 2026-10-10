using System.Numerics;
using Ncma.Assets;

internal static unsafe partial class Program
{
    // Independent test-side numerical consumer, not production shader/cook code.
    private sealed class IblImageOracle
    {
        private readonly float[][] _irr;
        private readonly float[][][] _spec;
        private readonly float[] _lut;
        private readonly EnvironmentCookSettings _s;
        internal IblImageOracle(EnvironmentPackage package)
        {
            _s=package.Settings;_irr=Enumerable.Range(0,6).Select(package.CopyIrradianceFace).ToArray();
            _spec=Enumerable.Range(0,_s.Levels).Select(l=>Enumerable.Range(0,6).Select(f=>package.CopySpecularFace(l,f)).ToArray()).ToArray();_lut=package.CopyBrdfLut();
        }
        internal Vector2 Brdf(float nv,float rough)=>Bilinear(_lut,(int)_s.LutSize,2,nv*(_s.LutSize-1),rough*(_s.LutSize-1)).XY();
        internal Vector3 Evaluate(Vector3 position,Vector3 normal,Vector3 baseColor,float metal,float rough,float ao,float strength,float rotation,Vector3 emissive=default)
        {
            var n=Vector3.Normalize(normal);var v=Vector3.Normalize(-position);float nv=Math.Max(.0001f,Vector3.Dot(n,v));
            var irr=Cube(_irr,(int)_s.IrradianceSize,Rotate(n,rotation));
            var r=Rotate(Vector3.Reflect(-v,n),rotation);float lod=rough*(_s.Levels-1);int l=(int)lod;
            var pref=Vector3.Lerp(Cube(_spec[l],(int)_s.CubeSize>>l,r),Cube(_spec[Math.Min(l+1,_s.Levels-1)],(int)_s.CubeSize>>Math.Min(l+1,_s.Levels-1),r),lod-l);
            return Display(IblLinear(nv,baseColor,metal,rough,ao,strength,irr,pref,Brdf(nv,rough))+emissive);
        }
        internal static Vector3 IblLinear(float nv,Vector3 color,float metal,float rough,float ao,float strength,Vector3 irradiance,Vector3 pref,Vector2 brdf)
        {
            var f0=Vector3.Lerp(new(.04f),color,metal);var f=f0+(Vector3.Max(new(1-rough),f0)-f0)*MathF.Pow(1-nv,5);
            return ((Vector3.One-f)*(1-metal)*color*irradiance/MathF.PI+pref*(f0*brdf.X+new Vector3(brdf.Y)))*ao*strength;
        }
        internal static Vector3 Display(Vector3 linear)
        {
            for(int c=0;c<3;c++){float x=linear[c];x=Math.Clamp(x*(2.51f*x+.03f)/(x*(2.43f*x+.59f)+.14f),0,1);linear[c]=x<=.0031308f?12.92f*x:1.055f*MathF.Pow(x,1/2.4f)-.055f;}return linear;
        }
        internal static Vector3 Rotate(Vector3 d,float angle)=>new(MathF.Cos(angle)*d.X-MathF.Sin(angle)*d.Z,d.Y,MathF.Sin(angle)*d.X+MathF.Cos(angle)*d.Z);
        private static (int Face,float U,float V) Address(Vector3 d)
        {
            var a=Vector3.Abs(d);int face;float u,v;
            if(a.X>=a.Y&&a.X>=a.Z){face=d.X>=0?0:1;u=(d.X>=0?-d.Z:d.Z)/a.X;v=-d.Y/a.X;}
            else if(a.Y>=a.Z){face=d.Y>=0?2:3;u=d.X/a.Y;v=(d.Y>=0?d.Z:-d.Z)/a.Y;}
            else{face=d.Z>=0?4:5;u=(d.Z>=0?d.X:-d.X)/a.Z;v=-d.Y/a.Z;}
            return(face,u,v);
        }
        private static Vector3 Direction(int face,float u,float v)=>face switch{0=>new(1,-v,-u),1=>new(-1,-v,u),2=>new(u,1,v),3=>new(u,-1,-v),4=>new(u,-v,1),_=>new(-u,-v,-1)};
        private static Vector3 Cube(float[][] faces,int size,Vector3 direction)
        {
            var a=Address(direction);float px=(a.U+1)*size/2-.5f,py=(a.V+1)*size/2-.5f;int x=(int)MathF.Floor(px),y=(int)MathF.Floor(py);
            Vector3 Tap(int tx,int ty){int face=a.Face;if(tx<0||ty<0||tx>=size||ty>=size){var other=Address(Direction(face,2*(tx+.5f)/size-1,2*(ty+.5f)/size-1));face=other.Face;tx=Math.Clamp((int)((other.U+1)*size/2),0,size-1);ty=Math.Clamp((int)((other.V+1)*size/2),0,size-1);}int i=(ty*size+tx)*4;return new(faces[face][i],faces[face][i+1],faces[face][i+2]);}
            return Vector3.Lerp(Vector3.Lerp(Tap(x,y),Tap(x+1,y),px-x),Vector3.Lerp(Tap(x,y+1),Tap(x+1,y+1),px-x),py-y);
        }
        private static Vector3 Bilinear(float[] values,int size,int channels,float px,float py)
        {
            int x=(int)MathF.Floor(px),y=(int)MathF.Floor(py);Vector3 Tap(int tx,int ty){int i=(Math.Clamp(ty,0,size-1)*size+Math.Clamp(tx,0,size-1))*channels;return new(values[i],values[i+1],channels>2?values[i+2]:0);}
            return Vector3.Lerp(Vector3.Lerp(Tap(x,y),Tap(x+1,y),px-x),Vector3.Lerp(Tap(x,y+1),Tap(x+1,y+1),px-x),py-y);
        }
    }
}

internal static class IblVectorExtensions
{
    internal static Vector2 XY(this Vector3 v)=>new(v.X,v.Y);
}
