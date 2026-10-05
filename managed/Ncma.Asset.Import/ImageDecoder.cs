using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Ncma.Assets;
namespace Ncma.Asset.Import;
/// <summary>Trusted OFF-frame tooling. Input is approved copied bytes, never an FBX texture path.</summary>
public sealed unsafe class ImageDecoder : IDisposable
{
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private readonly ToolCodePin _pin;
    private nint _library;
    private readonly delegate* unmanaged[Cdecl]<uint,byte*,uint,Info*,byte*,uint,Error*,uint> _decode;
    [StructLayout(LayoutKind.Sequential)] private struct Info {public uint Size,Width,Height,RowPitch,Bytes,Container,Reserved,Reserved2;}
    [StructLayout(LayoutKind.Sequential)] private struct Error {public uint Code,Reserved,Required,Length;public fixed byte Message[512];}
    public ImageDecoder(string libraryPath,string expectedHash)
    {
        AssetRecordCodec.ValidateHash(expectedHash);_pin=new(libraryPath);
        try {
            if(Convert.ToHexString(SHA256.HashData(_pin.Stream))!=expectedHash)throw new ArgumentException("Image tool hash mismatch.");
            _library=NativeLibrary.Load(libraryPath,typeof(ImageDecoder).Assembly,DllImportSearchPath.UseDllDirectoryForDependencies|DllImportSearchPath.System32);
            _decode=(delegate* unmanaged[Cdecl]<uint,byte*,uint,Info*,byte*,uint,Error*,uint>)NativeLibrary.GetExport(_library,"ncma_image_decode_v1");
        }catch{if(_library!=0)NativeLibrary.Free(_library);_pin.Dispose();throw;}
    }
    public TextureData Decode(ReadOnlySpan<byte> encoded,TextureSemantic semantic,bool normalYDown=false,CancellationToken cancellation=default)
    {
        Verify();cancellation.ThrowIfCancellationRequested();
        if(encoded.Length is <8 or >16*1024*1024)throw new ArgumentException("Encoded image budget.");
        byte[] owned=encoded.ToArray();Info info=new(){Size=32};Error error=default;
        fixed(byte* p=owned){uint code=_decode(1,p,(uint)owned.Length,&info,null,0,&error);if(code!=NCMA_BUFFER_TOO_SMALL)Fail(code);}
        if(info.Width is <1 or >4096||info.Height is <1 or >4096||info.RowPitch!=info.Width*4||info.Bytes!=(long)info.Width*info.Height*4||info.Bytes>TextureData.MaxBytes||info.Reserved!=0||info.Reserved2!=0||info.Container is <1 or >2)
            throw new ArgumentException("Image tool output contract.");
        TextureData.ValidateDimensions(info.Width,info.Height,semantic,normalYDown); // Full mip budget BEFORE RGBA allocation/decompression.
        cancellation.ThrowIfCancellationRequested();byte[] pixels=new byte[info.Bytes];Info expected=info;
        fixed(byte* p=owned)fixed(byte* output=pixels){uint code=_decode(1,p,(uint)owned.Length,&info,output,(uint)pixels.Length,&error);if(code!=0)Fail(code);}
        if(info.Width!=expected.Width||info.Height!=expected.Height||info.Bytes!=expected.Bytes||info.RowPitch!=expected.RowPitch)throw new ArgumentException("Image tool result changed.");
        return TextureData.Prepare(info.Width,info.Height,semantic,pixels,normalYDown,cancellation);
    }
    private const uint NCMA_BUFFER_TOO_SMALL=5;
    private static void Fail(uint code)=>throw new ArgumentException("Image decode rejected, code="+code);
    private void Verify(){if(Environment.CurrentManagedThreadId!=_owner)throw new InvalidOperationException("Image tool owner thread required.");ObjectDisposedException.ThrowIf(_library==0,this);}
    public void Dispose(){if(_library==0)return;Verify();NativeLibrary.Free(_library);_library=0;_pin.Dispose();}
}
