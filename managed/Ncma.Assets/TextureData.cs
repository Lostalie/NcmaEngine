using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
namespace Ncma.Assets;

public enum TextureSemantic : uint { Color=1, Normal=2, Data=3 }
public readonly record struct TextureMip(uint Width,uint Height,uint Offset,uint RowPitch);
/// <summary>Owned top-left RGBA8, complete deterministic mip chain; no GPU handles or file IO.</summary>
public sealed class TextureData
{
    public const int MaxBytes=64*1024*1024, MaxDimension=4096;
    private readonly byte[] _pixels;
    private readonly TextureMip[] _mips;
    public ReadOnlySpan<byte> Pixels=>_pixels;
    public ReadOnlySpan<TextureMip> Mips=>_mips;
    public uint Width=>_mips[0].Width;
    public uint Height=>_mips[0].Height;
    public TextureSemantic Semantic {get;}
    public bool NormalYDown {get;}
    public bool Srgb=>Semantic==TextureSemantic.Color;
    public string ContentHash {get;}
    private TextureData(byte[] pixels,TextureMip[] mips,TextureSemantic semantic,bool normalYDown)
    { _pixels=pixels;_mips=mips;Semantic=semantic;NormalYDown=normalYDown;ContentHash=Convert.ToHexString(SHA256.HashData(Encode())); }
    public static void ValidateDimensions(uint width,uint height,TextureSemantic semantic,bool normalYDown=false) => _ = Layout(width,height,semantic,normalYDown,out _);
    public static TextureData Prepare(uint width,uint height,TextureSemantic semantic,ReadOnlySpan<byte> rgba,bool normalYDown=false,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();var mips=Layout(width,height,semantic,normalYDown,out int total);
        if(rgba.Length!=(long)width*height*4)throw new ArgumentException("RGBA stride/byte count mismatch.");
        byte[] pixels=new byte[total];rgba.CopyTo(pixels);
        for(int level=1;level<mips.Length;level++) {
            var src=mips[level-1];var dst=mips[level];
            for(uint y=0;y<dst.Height;y++) {
                cancellation.ThrowIfCancellationRequested();
                for(uint x=0;x<dst.Width;x++) {
                    // Integer area footprint includes all texels of odd-sized images; version 1 policy.
                    uint x0=x*src.Width/dst.Width,x1=(x+1)*src.Width/dst.Width,y0=y*src.Height/dst.Height,y1=(y+1)*src.Height/dst.Height;
                    Vector4 sum=default;int count=0;
                    for(uint sy=y0;sy<y1;sy++)for(uint sx=x0;sx<x1;sx++) {
                        int at=checked((int)(src.Offset+sy*src.RowPitch+sx*4));Vector4 v=new(pixels[at]/255f,pixels[at+1]/255f,pixels[at+2]/255f,pixels[at+3]/255f);
                        if(semantic==TextureSemantic.Color){v.X=Linear(v.X)*v.W;v.Y=Linear(v.Y)*v.W;v.Z=Linear(v.Z)*v.W;}
                        else if(semantic==TextureSemantic.Normal){v.X=v.X*2-1;v.Y=v.Y*2-1;v.Z=v.Z*2-1;}
                        sum+=v;count++;
                    }
                    sum/=count;
                    if(semantic==TextureSemantic.Color){sum.X=SrgbValue(sum.W>0?sum.X/sum.W:0);sum.Y=SrgbValue(sum.W>0?sum.Y/sum.W:0);sum.Z=SrgbValue(sum.W>0?sum.Z/sum.W:0);}
                    else if(semantic==TextureSemantic.Normal){var n=new Vector3(sum.X,sum.Y,sum.Z);n=n.LengthSquared()<1e-8f?Vector3.UnitZ:Vector3.Normalize(n);sum.X=n.X*.5f+.5f;sum.Y=n.Y*.5f+.5f;sum.Z=n.Z*.5f+.5f;}
                    int target=checked((int)(dst.Offset+y*dst.RowPitch+x*4));pixels[target]=Byte(sum.X);pixels[target+1]=Byte(sum.Y);pixels[target+2]=Byte(sum.Z);pixels[target+3]=Byte(sum.W);
                }
            }
        }
        cancellation.ThrowIfCancellationRequested();return new(pixels,mips,semantic,normalYDown);
    }
    public byte[] Encode()
    {
        byte[] result=new byte[32+_mips.Length*16+_pixels.Length];Span<byte> s=result;
        uint[] header=[0x31525854,1,Width,Height,(uint)Semantic,NormalYDown?1u:0u,(uint)_mips.Length,(uint)_pixels.Length];
        for(int i=0;i<8;i++)BinaryPrimitives.WriteUInt32LittleEndian(s[(i*4)..],header[i]);
        for(int i=0;i<_mips.Length;i++){var m=_mips[i];int at=32+i*16;BinaryPrimitives.WriteUInt32LittleEndian(s[at..],m.Width);BinaryPrimitives.WriteUInt32LittleEndian(s[(at+4)..],m.Height);BinaryPrimitives.WriteUInt32LittleEndian(s[(at+8)..],m.Offset);BinaryPrimitives.WriteUInt32LittleEndian(s[(at+12)..],m.RowPitch);}
        _pixels.CopyTo(s[(32+_mips.Length*16)..]);return result;
    }
    public static TextureData Decode(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length<48||bytes.Length>MaxBytes+240)throw new ArgumentException("TXR1 size budget.");
        // Copy once: strict owned asset, never retain the caller's mutable input.
        byte[] bytesCopy=bytes.ToArray();
        uint U(int at)=>BinaryPrimitives.ReadUInt32LittleEndian(bytesCopy.AsSpan(at));
        if(U(0)!=0x31525854||U(4)!=1||U(20)>1)throw new ArgumentException("Unsupported texture format/version.");
        var semantic=(TextureSemantic)U(16);var layout=Layout(U(8),U(12),semantic,U(20)==1,out int total);
        if(U(24)!=layout.Length||U(28)!=total||bytes.Length!=32+layout.Length*16+total)throw new ArgumentException("Texture length/mip count mismatch.");
        for(int i=0;i<layout.Length;i++){var m=layout[i];int at=32+i*16;if(U(at)!=m.Width||U(at+4)!=m.Height||U(at+8)!=m.Offset||U(at+12)!=m.RowPitch)throw new ArgumentException("Noncanonical/duplicate mip or stride.");}
        return new(bytesCopy.AsSpan(32+layout.Length*16).ToArray(),layout,semantic,U(20)==1);
    }
    private static TextureMip[] Layout(uint width,uint height,TextureSemantic semantic,bool normalYDown,out int total)
    {
        if(width is <1 or >MaxDimension||height is <1 or >MaxDimension||!Enum.IsDefined(semantic)||(normalYDown&&semantic!=TextureSemantic.Normal))throw new ArgumentException("Texture dimensions/semantic.");
        var levels=new List<TextureMip>();long bytes=0;
        while(true){levels.Add(new(width,height,checked((uint)bytes),width*4));bytes+=width*(long)height*4;if(bytes>MaxBytes)throw new ArgumentException("Texture mip byte budget.");if(width==1&&height==1)break;width=Math.Max(1,width/2);height=Math.Max(1,height/2);}
        total=(int)bytes;return levels.ToArray();
    }
    private static byte Byte(float x)=>(byte)Math.Clamp((int)MathF.Round(x*255),0,255);
    private static float Linear(float x)=>x<=.04045f?x/12.92f:MathF.Pow((x+.055f)/1.055f,2.4f);
    private static float SrgbValue(float x)=>x<=.0031308f?12.92f*x:1.055f*MathF.Pow(x,1/2.4f)-.055f;
}
