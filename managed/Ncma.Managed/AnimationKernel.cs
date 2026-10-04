using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
namespace Ncma;

// Numerical resource only; consumer supplies time/from-pose/blend/ranges explicitly.
internal sealed unsafe class AnimationKernel : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint Version();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong Create(uint version);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Destroy(ulong handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte TextCall(ulong handle,byte* output,uint capacity,uint* required);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte ErrorCall(byte* output,uint capacity,uint* required);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte SampleCall(ulong handle,uint clip,double time,float* from,uint fromCount,float weight,float* output,uint capacity,uint* required);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte MotionCall(ulong handle,uint clip,double from,double to,float* output,uint capacity,uint* required);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte NotifyCall(ulong handle,uint clip,double from,double to,byte* output,uint capacity,uint* required);
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private nint _module; private ulong _handle;
    private readonly Destroy _destroy;
    private readonly TextCall _library;
    private readonly ErrorCall _error;
    private readonly SampleCall _sample;
    private readonly MotionCall _motion;
    private readonly NotifyCall _notifies;
    internal JsonElement Metadata { get; }
    internal int Bones { get; }
    internal AnimationKernel(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Explicit absolute animation DLL required.");
        _module=NativeLibrary.Load(Path.GetFullPath(path));
        try {
            T Bind<T>(string name) where T:Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_module,name));
            if (Bind<Version>("ncma_animation_abi_version")()!=2) throw new NotSupportedException("Animation ABI 2 required.");
            _destroy=Bind<Destroy>("ncma_animation_destroy"); _library=Bind<TextCall>("ncma_animation_read_library");
            _error=Bind<ErrorCall>("ncma_animation_read_error"); _sample=Bind<SampleCall>("ncma_animation_sample");
            _motion=Bind<MotionCall>("ncma_animation_motion"); _notifies=Bind<NotifyCall>("ncma_animation_notifies");
            _handle=Bind<Create>("ncma_animation_create")(2); if (_handle==0) throw new InvalidOperationException(ErrorText());
            uint count=0; if (_library(_handle,null,0,&count)!=2 || count is <2 or >1048576) throw new InvalidOperationException("Invalid library count.");
            byte[] bytes=new byte[count]; fixed(byte* p=bytes) Check(_library(_handle,p,count,&count));
            using var json=JsonDocument.Parse(bytes.AsMemory(0,bytes.Length-1)); Metadata=json.RootElement.Clone();
            if (Metadata.GetProperty("schema_version").GetInt32()!=2) throw new NotSupportedException("Unsupported library metadata.");
            Bones=Metadata.GetProperty("bones").GetArrayLength();
            if (Bones is <1 or >1024) throw new InvalidOperationException("Invalid skeleton size.");
        } catch { if (_handle!=0) (_destroy ?? throw new InvalidOperationException("Missing resource release function."))(_handle); NativeLibrary.Free(_module); _module=0; throw; }
    }
    private void Verify() {
        if (Environment.CurrentManagedThreadId!=_owner) throw new InvalidOperationException("Animation owner thread required.");
        ObjectDisposedException.ThrowIf(_handle==0,this);
    }
    private string ErrorText() {
        uint size=0; if (_error(null,0,&size)!=2 || size is <1 or >4096) return "Animation kernel error.";
        byte[] bytes=new byte[size]; fixed(byte* p=bytes) if (_error(p,size,&size)!=1) return "Animation error copy failed.";
        return new UTF8Encoding(false,true).GetString(bytes,0,bytes.Length-1);
    }
    private void Check(byte value) { if (value!=1) throw new InvalidOperationException(ErrorText()); }
    internal float[] Sample(int clip,double time,float[]? from=null,float weight=1) {
        Verify(); float[] result=new float[Bones*26]; uint count=0;
        fixed(float* p=result) fixed(float* f=from)
            Check(_sample(_handle,(uint)clip,time,f,(uint)(from?.Length??0),weight,p,(uint)result.Length,&count));
        if (count!=result.Length || result.Any(v=>!float.IsFinite(v))) throw new InvalidOperationException("Invalid numerical pose.");
        return result;
    }
    internal float[] Motion(int clip,double from,double to) {
        Verify(); float[] result=new float[10]; uint count=0;
        fixed(float* p=result) Check(_motion(_handle,(uint)clip,from,to,p,10,&count));
        if (count!=10 || result.Any(v=>!float.IsFinite(v))) throw new InvalidOperationException("Invalid motion.");
        return result;
    }
    internal JsonElement[] Notifies(int clip,double from,double to) {
        Verify(); uint count=0;
        if (_notifies(_handle,(uint)clip,from,to,null,0,&count)!=2 || count is <2 or >1048576) throw new InvalidOperationException(ErrorText());
        byte[] bytes=new byte[count]; fixed(byte* p=bytes) Check(_notifies(_handle,(uint)clip,from,to,p,count,&count));
        using var json=JsonDocument.Parse(bytes.AsMemory(0,bytes.Length-1));
        return json.RootElement.EnumerateArray().Select(e=>e.Clone()).ToArray();
    }
    public void Dispose() {
        if (Environment.CurrentManagedThreadId!=_owner) throw new InvalidOperationException("Animation owner thread required.");
        if (_handle==0) return;
        _destroy(_handle); _handle=0; NativeLibrary.Free(_module); _module=0;
    }
}
