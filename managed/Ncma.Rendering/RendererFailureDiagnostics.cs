using System.Runtime.InteropServices;
using Ncma.Interop;
namespace Ncma.Rendering;

public enum GpuFailureOperation : uint
{
    None, Startup, NativeApi, CompletionQuery, CompletionPoll, CompletionTimeout,
    DeviceRemoved, Present, Readback, Resize, Execution, ExplicitAbandon, Timing
}
[StructLayout(LayoutKind.Sequential)]
internal struct RendererFailureNative
{
    public uint Size, Version, Flags, Result;
    public ulong Generation, Frame;
    public uint Operation; public int NativeCode, DeviceReason; public uint Backend;
}
[StructLayout(LayoutKind.Sequential)]
internal struct RendererFailureApi { public uint Size, Version; public ulong Caps; public nint Read; }
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate uint ReadRendererFailureNative(ulong module, ulong renderer, RendererFailureNative* output, PluginError* error);

public sealed unsafe partial class RendererSession
{
    private ReadRendererFailureNative? _readFailure;
    private static ReadRendererFailureNative QueryFailureApi(PluginModule module)
    {
        if(module.Kind!=ModuleKind.Renderer)throw new ArgumentException("Renderer module required.");
        if(module.AbiMinor<2)throw new NotSupportedException("Renderer diagnostics require query18/v1.");
        if(sizeof(RendererFailureApi)!=24||sizeof(RendererFailureNative)!=48)throw new InvalidOperationException("Renderer diagnostic x64 layout.");
        RendererFailureApi api=default;PluginError error=default;
        PluginModule.Check(module.Id,"query_renderer_failure",module.ReadFunction<QuerySceneRender>(144)(module.Context,18,&api,24,&error),error);
        if(api.Size!=24||api.Version!=1||api.Caps!=1||api.Read==0)throw new InvalidOperationException("Renderer diagnostic query18/v1.");
        return Marshal.GetDelegateForFunctionPointer<ReadRendererFailureNative>(api.Read);
    }
    private static NativeFailureDiagnostic? ReadFailure(PluginModule module,ulong renderer,ReadRendererFailureNative read)
    {
        RendererFailureNative state=default;PluginError error=default;
        PluginModule.Check(module.Id,"read_renderer_failure",read(module.Context,renderer,&state,&error),error);
        if(state.Size!=48||state.Version!=1||(state.Flags&~7u)!=0||(renderer!=0&&state.Generation!=renderer))throw new InvalidOperationException("Invalid renderer failure identity/layout.");
        if(state.Flags==0)
        {
            if(state.Result!=0||state.Frame!=0||state.Operation!=0||state.NativeCode!=0||state.DeviceReason!=0||state.Backend!=(renderer==0?0u:1u)||renderer==0&&state.Generation!=0)throw new InvalidOperationException("Invalid empty renderer failure.");
            return null;
        }
        if((state.Flags&1)==0||state.Backend!=1||state.Generation==0||!Enum.IsDefined((GpuFailureOperation)state.Operation)||state.Operation==0||
            !Enum.IsDefined((PluginResult)state.Result)||state.Result==0||((state.Flags&2)==0&&state.NativeCode!=0)||((state.Flags&4)==0&&state.DeviceReason!=0)||
            ((state.Flags&2)!=0&&state.NativeCode>=0))throw new InvalidOperationException("Invalid copied renderer failure values.");
        return new(state.Backend,state.Generation,state.Frame,state.Operation,(PluginResult)state.Result,(state.Flags&2)!=0?state.NativeCode:null,(state.Flags&4)!=0?state.DeviceReason:null);
    }
    /// <summary>First GPU failure in this device lifetime; may precede a later unrelated host exception.</summary>
    public NativeFailureDiagnostic? ReadFirstFailure()
    {
        Verify();_readFailure??=QueryFailureApi(Module);return ReadFailure(Module,_handle,_readFailure);
    }
    /// <summary>Copied last admitted startup failure; no device/World ownership or forced cleanup.</summary>
    public static NativeFailureDiagnostic? ReadStartupFailure(PluginModule module)
    {
        ArgumentNullException.ThrowIfNull(module);return ReadFailure(module,0,QueryFailureApi(module));
    }
}
