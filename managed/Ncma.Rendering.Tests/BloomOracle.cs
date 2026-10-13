using System.Numerics;

// Independent CPU image/filter implementation; never consumes the generated GPU graph or shader text.
internal sealed class BloomOracle(int width,int height,Vector3[] pixels)
{
    public int Width{get;}=width;public int Height{get;}=height;public Vector3[] Pixels{get;}=pixels;
    public static Vector3 HalfRound(Vector3 x)=>new((float)(Half)Math.Clamp(x.X,0,65504),(float)(Half)Math.Clamp(x.Y,0,65504),(float)(Half)Math.Clamp(x.Z,0,65504));
    private Vector3 Sample(double u,double v){
        double x=u*Width-.5,y=v*Height-.5;int ix=(int)Math.Floor(x),iy=(int)Math.Floor(y);float tx=(float)(x-ix),ty=(float)(y-iy);
        Vector3 P(int px,int py)=>Pixels[Math.Clamp(py,0,Height-1)*Width+Math.Clamp(px,0,Width-1)];
        return Vector3.Lerp(Vector3.Lerp(P(ix,iy),P(ix+1,iy),tx),Vector3.Lerp(P(ix,iy+1),P(ix+1,iy+1),tx),ty);
    }
    private static BloomOracle Make(int w,int h,Func<double,double,Vector3> color){var pixels=new Vector3[w*h];for(int y=0;y<h;y++)for(int x=0;x<w;x++)pixels[y*w+x]=HalfRound(color((x+.5)/w,(y+.5)/h));return new(w,h,pixels);}
    private BloomOracle Down(float? threshold=null,float knee=0)=>Make(Math.Max(1,(Width+1)/2),Math.Max(1,(Height+1)/2),(u,v)=>{
        Vector3 c=(Sample(u-.5/Width,v-.5/Height)+Sample(u+.5/Width,v-.5/Height)+Sample(u-.5/Width,v+.5/Height)+Sample(u+.5/Width,v+.5/Height))*.25f;
        if(threshold is {} t){double peak=Math.Max(c.X,Math.Max(c.Y,c.Z));double transition=Math.Clamp(peak-t+knee,0,2*knee);double retained=Math.Max(peak-t,transition*transition/Math.Max(4*knee,1e-5));c*=(float)(retained/Math.Max(peak,1e-5));}return c;
    });
    private BloomOracle Blur(bool horizontal)=>Make(Width,Height,(u,v)=>{
        double du=horizontal?1.0/Width:0,dv=horizontal?0:1.0/Height;Vector3 sum=Vector3.Zero;int[] weights=[1,4,6,4,1];
        for(int k=-2;k<=2;k++)sum+=Sample(u+k*du,v+k*dv)*(weights[k+2]/16f);return sum;
    });
    public BloomOracle Bloom(Ncma.Rendering.BloomSettings settings){
        if(!settings.Enabled||settings.Intensity==0)return this;var levels=new List<BloomOracle>();BloomOracle previous=this;
        for(int i=0;i<settings.Levels;i++){previous=previous.Down(i==0?settings.Threshold:null,settings.Threshold*settings.SoftKnee).Blur(true).Blur(false);levels.Add(previous);if(previous.Width==1&&previous.Height==1)break;}
        for(int i=levels.Count-2;i>=0;i--){var local=levels[i];var smaller=previous;previous=Make(local.Width,local.Height,(u,v)=>Vector3.Lerp(local.Sample(u,v),smaller.Sample(u,v),settings.Scatter));}
        return Make(Width,Height,(u,v)=>Sample(u,v)+previous.Sample(u,v)*settings.Intensity);
    }
    public byte[] Display(float exposure){var result=new byte[Width*Height*4];for(int i=0;i<Pixels.Length;i++){for(int c=0;c<3;c++){double x=Math.Clamp(Pixels[i][c]*exposure,0,1);double s=x<=.0031308?x*12.92:1.055*Math.Pow(x,1/2.4)-.055;result[i*4+c]=(byte)Math.Clamp(Math.Round(s*255),0,255);}result[i*4+3]=255;}return result;}
}
