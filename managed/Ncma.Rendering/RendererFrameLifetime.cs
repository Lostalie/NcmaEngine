using System.Runtime.InteropServices;
using Ncma.Interop;
namespace Ncma.Rendering;

[StructLayout(LayoutKind.Sequential)] internal struct RendererFrameApi { public uint Size,Version;public ulong Caps;public nint Read,Abandon; }
[StructLayout(LayoutKind.Sequential)] internal struct RendererFrameStateNative { public uint Size,Version,Flags,Reserved;public ulong Generation,Frame,Abandoned; }
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint ReadRendererFrameNative(ulong module,ulong renderer,RendererFrameStateNative* state,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint AbandonRendererFrameNative(ulong module,ulong renderer,ulong frame,PluginError* error);
public readonly record struct RendererFrameState(ulong Generation,ulong Frame,bool Active,bool Failed,ulong AbandonedFrames);
public sealed unsafe partial class RendererSession
{
    private ReadRendererFrameNative? _readFrameState;
    private AbandonRendererFrameNative? _abandonFrame;
    private void EnsureFrameApi()
    {
        Verify();if(_readFrameState is not null)return;
        if(Module.AbiMinor<2)throw new NotSupportedException("Explicit frame abandonment requires renderer query 17.");
        if(sizeof(RendererFrameApi)!=32||sizeof(RendererFrameStateNative)!=40)throw new InvalidOperationException("Renderer frame x64 layout.");
        RendererFrameApi api=default;PluginError error=default;
        PluginModule.Check(Module.Id,"query_renderer_frame",Module.ReadFunction<QuerySceneRender>(144)(Module.Context,17,&api,32,&error),error);
        if(api.Size!=32||api.Version!=1||api.Caps!=1||api.Read==0||api.Abandon==0)throw new InvalidOperationException("Renderer frame query17/API1.");
        var read=Marshal.GetDelegateForFunctionPointer<ReadRendererFrameNative>(api.Read);
        _abandonFrame=Marshal.GetDelegateForFunctionPointer<AbandonRendererFrameNative>(api.Abandon);_readFrameState=read;
    }
    public RendererFrameState ReadFrameState()
    {
        EnsureFrameApi();RendererFrameStateNative state=default;PluginError error=default;
        PluginModule.Check(Module.Id,"read_renderer_frame",_readFrameState!(Module.Context,Handle,&state,&error),error);
        if(state.Size!=40||state.Version!=1||(state.Flags&~3u)!=0||state.Reserved!=0||state.Generation!=Handle)throw new InvalidOperationException("Invalid renderer frame state.");
        return new(state.Generation,state.Frame,(state.Flags&1)!=0,(state.Flags&2)!=0,state.Abandoned);
    }
    // Terminal for this renderer. Does not Present, wait, release leases or recover
    // gameplay/device state. The caller still closes all ordinary resource owners.
    public void AbandonFrameForShutdown(ulong exactFrame)
    {
        EnsureFrameApi();PluginError error=default;
        PluginModule.Check(Module.Id,"abandon_renderer_frame",_abandonFrame!(Module.Context,Handle,exactFrame,&error),error);
    }
}
