using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace Ncma.Assets;

public readonly record struct EnvironmentCookSettings(uint CubeSize,uint IrradianceSize,uint LutSize,uint Samples)
{
    public static EnvironmentCookSettings Default => new(16,8,32,512);
    public int Levels { get { Validate(); return BitOperations.Log2(CubeSize)+1; } }
    public int FloatCount { get { Validate(); return checked((int)Count().Floats); } }
    public ulong SampleBudget { get { Validate(); return Count().Work; } }
    private (ulong Floats,ulong Work) Count()
    {
        ulong spec=0;for(uint size=CubeSize;size!=0;size/=2)spec+=6ul*size*size;
        ulong diffuse=6ul*IrradianceSize*IrradianceSize,lut=(ulong)LutSize*LutSize;
        return ((spec+diffuse)*4+lut*2,(spec+diffuse+lut)*Samples);
    }
    public void Validate()
    {
        if(!Power(CubeSize,2,64)||!Power(IrradianceSize,2,16)||!Power(LutSize,2,64)||!Power(Samples,64,2048))
            throw new ArgumentException("Environment dimensions/samples.");
        var count=Count();if(count.Work>64ul*1024*1024||count.Floats*4>EnvironmentPackage.MaxBytes-EnvironmentPackage.HeaderBytes)
            throw new ArgumentException("Environment sample/byte budget.");
    }
    internal static bool Power(uint value,uint low,uint high)=>value>=low&&value<=high&&BitOperations.IsPow2(value);
}

// Trusted decoded linear HDR source. Does not read HDR/EXR files or guess source gamma.
public sealed class HdrEnvironmentSource
{
    private readonly float[] _values;
    public Guid AssetId { get; }
    public uint Width { get; }
    public uint Height { get; }
    public string ContentHash { get; }
    public ReadOnlySpan<float> Pixels => _values;
    private HdrEnvironmentSource(Guid id,uint width,uint height,float[] values)
    {
        AssetId=id;Width=width;Height=height;_values=values;
        byte[] bytes=new byte[32+values.Length*4];BinaryPrimitives.WriteUInt32LittleEndian(bytes,0x31534845);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4),1);BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8),width);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12),height);id.TryWriteBytes(bytes.AsSpan(16,16));
        for(int i=0;i<values.Length;i++)BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(32+i*4),values[i]);
        ContentHash=Convert.ToHexString(SHA256.HashData(bytes));
    }
    internal static void Dimensions(uint width,uint height)
    { if(!EnvironmentCookSettings.Power(height,2,256)||width!=height*2)throw new ArgumentException("Environment equirect dimensions."); }
    public static HdrEnvironmentSource Prepare(Guid id,uint width,uint height,ReadOnlySpan<float> rgba)
    {
        Dimensions(width,height);if(id==Guid.Empty||rgba.Length!=(long)width*height*4)throw new ArgumentException("Environment source identity/length.");
        var values=rgba.ToArray();for(int i=0;i<values.Length;i++){
            float x=values[i];if(!float.IsFinite(x)||x<0||x>65504||(i%4==3&&x!=1))throw new ArgumentException("Linear environment RGB/alpha.");
            if(x==0)values[i]=0; // Canonicalize negative zero on authored input.
        }
        return new(id,width,height,values);
    }
    public static HdrEnvironmentSource Neutral(Guid id)
    {
        float[] pixels=new float[16*8*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=pixels[i+1]=pixels[i+2]=.18f;pixels[i+3]=1;}
        return Prepare(id,16,8,pixels);
    }
}

// Source-free NCE1 v1. Hash integrity/closed numerical format, NOT provenance, signing or GPU admission.
public sealed class EnvironmentPackage
{
    public const int HeaderBytes=160,MaxBytes=4*1024*1024,Algorithm=1;
    private readonly byte[] _bytes;
    private readonly float[] _values;
    public Guid AssetId { get; }
    public Guid SourceId { get; }
    public ulong Generation { get; }
    public uint SourceWidth { get; }
    public uint SourceHeight { get; }
    public string SourceHash { get; }
    public string ContentHash { get; }
    public EnvironmentCookSettings Settings { get; }
    public bool GpuValidated => false;
    public int ByteCount => _bytes.Length;
    private EnvironmentPackage(byte[] bytes,float[] values)
    {
        _bytes=bytes;_values=values;AssetId=new(bytes.AsSpan(48,16));SourceId=new(bytes.AsSpan(64,16));
        Generation=BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(80));
        SourceWidth=U(bytes,16);SourceHeight=U(bytes,20);Settings=new(U(bytes,24),U(bytes,28),U(bytes,32),U(bytes,36));
        SourceHash=Convert.ToHexString(bytes.AsSpan(88,32));ContentHash=Hash(bytes);
    }
    private static uint U(byte[] b,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
    internal static string Hash(ReadOnlySpan<byte> bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    internal static bool CanonicalHash(string? hash)=>hash is {Length:64}&&hash.All(c=>c is >= '0' and <= '9' or >= 'A' and <= 'F');
    internal static EnvironmentPackage Create(Guid asset,ulong generation,HdrEnvironmentSource source,EnvironmentCookSettings settings,float[] values)
    {
        settings.Validate();if(asset==Guid.Empty||asset==source.AssetId||generation==0||values.Length!=settings.FloatCount)
            throw new ArgumentException("Environment package identity/layout.");
        byte[] bytes=new byte[HeaderBytes+values.Length*4];
        uint[] fields=[0x3145434e,1,Algorithm,HeaderBytes,source.Width,source.Height,settings.CubeSize,settings.IrradianceSize,settings.LutSize,settings.Samples,(uint)settings.Levels,(uint)values.Length];
        for(int i=0;i<fields.Length;i++)BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i*4),fields[i]);
        asset.TryWriteBytes(bytes.AsSpan(48,16));source.AssetId.TryWriteBytes(bytes.AsSpan(64,16));BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(80),generation);
        Convert.FromHexString(source.ContentHash).CopyTo(bytes,88);
        for(int i=0;i<values.Length;i++)BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(HeaderBytes+i*4),values[i]);
        SHA256.HashData(bytes.AsSpan(HeaderBytes)).CopyTo(bytes,120);return Decode(bytes,Hash(bytes));
    }
    public static EnvironmentPackage Decode(ReadOnlySpan<byte> supplied,string expectedHash)
    {
        if(supplied.Length<HeaderBytes||supplied.Length>MaxBytes||!CanonicalHash(expectedHash))throw new ArgumentException("Environment package size/hash.");
        byte[] bytes=supplied.ToArray();if(Hash(bytes)!=expectedHash)throw new ArgumentException("Environment expected hash.");
        if(U(bytes,0)!=0x3145434e||U(bytes,4)!=1||U(bytes,8)!=Algorithm||U(bytes,12)!=HeaderBytes||
            bytes.AsSpan(152,8).IndexOfAnyExcept((byte)0)>=0)throw new ArgumentException("Environment header/version.");
        HdrEnvironmentSource.Dimensions(U(bytes,16),U(bytes,20));var settings=new EnvironmentCookSettings(U(bytes,24),U(bytes,28),U(bytes,32),U(bytes,36));settings.Validate();
        if(U(bytes,40)!=settings.Levels||U(bytes,44)!=settings.FloatCount||bytes.Length!=HeaderBytes+settings.FloatCount*4)
            throw new ArgumentException("Environment closed layout.");
        var id=new Guid(bytes.AsSpan(48,16));var sourceId=new Guid(bytes.AsSpan(64,16));
        if(id==Guid.Empty||sourceId==Guid.Empty||id==sourceId||BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(80))==0||bytes.AsSpan(88,32).IndexOfAnyExcept((byte)0)<0)
            throw new ArgumentException("Environment identity.");
        if(!SHA256.HashData(bytes.AsSpan(HeaderBytes)).AsSpan().SequenceEqual(bytes.AsSpan(120,32)))throw new ArgumentException("Environment payload checksum.");
        float[] values=new float[settings.FloatCount];int diffuse=checked((int)(6*settings.IrradianceSize*settings.IrradianceSize*4)),lut=checked((int)(settings.LutSize*settings.LutSize*2)),end=values.Length-lut;
        for(int i=0;i<values.Length;i++){
            float value=BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(HeaderBytes+i*4));float high=i<diffuse?65504*MathF.PI:i<end?65504:2;
            if(!float.IsFinite(value)||value<0||value>high||(value==0&&BitConverter.SingleToInt32Bits(value)<0)||(i<end&&i%4==3&&value!=1))
                throw new ArgumentException("Environment numerical/alpha range.");
            values[i]=value;
        }
        return new(bytes,values);
    }
    public byte[] CopyBytes()=>(byte[])_bytes.Clone();
    public float[] CopyValues()=>(float[])_values.Clone();
    public float[] CopyIrradianceFace(int face)
    { Face(face);int count=checked((int)(Settings.IrradianceSize*Settings.IrradianceSize*4));return _values.AsSpan(face*count,count).ToArray(); }
    public float[] CopySpecularFace(int level,int face)
    {
        Face(face);if(level<0||level>=Settings.Levels)throw new ArgumentException("Environment mip level.");
        int offset=checked((int)(6*Settings.IrradianceSize*Settings.IrradianceSize*4));uint size=Settings.CubeSize;
        for(int i=0;i<level;i++){offset=checked(offset+(int)(6*size*size*4));size/=2;}
        int count=checked((int)(size*size*4));return _values.AsSpan(offset+face*count,count).ToArray();
    }
    public float[] CopyBrdfLut()=>_values.AsSpan(_values.Length-checked((int)(Settings.LutSize*Settings.LutSize*2))).ToArray();
    private static void Face(int face){if(face is <0 or >5)throw new ArgumentException("Environment cube face.");}
}

// Copied GPU binding value (B2) and scene configuration (C1); formal host integration remains C2.
public readonly record struct EnvironmentLightingConfiguration(Guid AssetId,ulong Generation,string ContentHash,float Strength,float RotationRadians,bool Enabled)
{
    public static EnvironmentLightingConfiguration Off => new(Guid.Empty,0,"",0,0,false);
    public void Validate()
    {
        if(!float.IsFinite(Strength)||Strength<0||Strength>16||!float.IsFinite(RotationRadians)||RotationRadians<-MathF.PI||RotationRadians>MathF.PI)
            throw new ArgumentException("Environment intensity/rotation.");
        if(Enabled?(AssetId==Guid.Empty||Generation==0||!EnvironmentPackage.CanonicalHash(ContentHash)):this!=Off)
            throw new ArgumentException("Environment enabled identity/off closure.");
    }
}
