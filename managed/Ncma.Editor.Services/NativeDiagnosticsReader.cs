using System.Runtime.InteropServices;
using System.Text.Json;
using Ncma.Application;
namespace Ncma.Editor.Services;

// Explicit, bounded copied spdlog pages. No managed callback enters native logging threads.
public sealed unsafe class NativeDiagnosticsReader : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate byte Read(ulong after,uint maximum,byte* output,uint capacity,uint* required);
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private nint _module;
    private readonly Read _read;
    private ulong _after;
    public NativeDiagnosticsReader(string path) {
        if(!Path.IsPathFullyQualified(path))throw new ArgumentException("Absolute diagnostics DLL path required.");
        _module=NativeLibrary.Load(path);
        try {_read=Marshal.GetDelegateForFunctionPointer<Read>(NativeLibrary.GetExport(_module,"ncma_diagnostics_read_v1"));}
        catch{NativeLibrary.Free(_module);_module=0;throw;}
    }
    private void Verify() {if(Environment.CurrentManagedThreadId!=_owner)throw new InvalidOperationException("Log owner thread required.");ObjectDisposedException.ThrowIf(_module==0,this);}
    public int Pump(ApplicationLog target,Guid correlation) {
        Verify();uint count=0;byte result=_read(_after,32,null,0,&count);
        for(int attempt=0;attempt<3;attempt++) {
            if(result!=2 || count is <2 or >262144)throw new InvalidOperationException("Invalid native diagnostic page.");
            byte[] bytes=new byte[count];uint copied=0;
            fixed(byte* p=bytes)result=_read(_after,32,p,count,&copied);
            if(result==2){count=copied;continue;}
            if(result!=1 || copied!=count || bytes[^1]!=0)throw new InvalidOperationException("Invalid native diagnostic copy.");
            using var json=JsonDocument.Parse(bytes.AsMemory(0,bytes.Length-1));var page=json.RootElement;
            if(page.GetProperty("schemaVersion").GetInt32()!=1)throw new NotSupportedException("Unknown diagnostic schema.");
            if(page.GetProperty("dropped").GetUInt64()>0)target.Write("warning","native.logs_dropped",$"Native ring dropped {page.GetProperty("dropped").GetUInt64()} earlier messages.",correlation);
            int read=0;
            foreach(var entry in page.GetProperty("events").EnumerateArray()) {
                ulong sequence=entry.GetProperty("sequence").GetUInt64();if(sequence<=_after)throw new InvalidOperationException("Native log sequence mismatch.");
                target.Write(entry.GetProperty("level").GetString()!,"native."+entry.GetProperty("code").GetString(),entry.GetProperty("message").GetString()!,correlation);
                _after=sequence;read++;
            }
            return read;
        }
        throw new InvalidOperationException("Native diagnostic page changed repeatedly.");
    }
    public void Dispose() {if(_module==0)return;Verify();NativeLibrary.Free(_module);_module=0;}
}
