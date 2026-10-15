using System.Runtime.InteropServices;
using Ncma.Interop;
namespace Ncma.Rendering;

/// <summary>Opaque historical resource token identifies production, never grants a lease or access.</summary>
public readonly record struct GpuWorkTimingSample(GpuTimingSample Timing,ulong Resource,ulong ContentRevision);
[StructLayout(LayoutKind.Sequential)]
internal struct GpuWorkSampleNative { public GpuSampleNative Timing; public ulong Resource,ContentRevision; }
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate uint ReadGpuWorkSampleNative(ulong module,ulong renderer,uint domain,GpuWorkSampleNative* output,PluginError* error);
public sealed unsafe partial class RendererSession
{
    private ReadGpuWorkSampleNative? _readGpuWorkSample;
    /// <summary>At most three nonblocking polls, no lazy skin/UI initialization or GPU waits.</summary>
    public GpuWorkTimingSample ReadGpuWorkTiming(GpuTimingDomain domain)
    {
        Verify();
        if(domain is not (GpuTimingDomain.SkinDispatch or GpuTimingDomain.UiProduction))throw new ArgumentOutOfRangeException(nameof(domain));
        if(_readGpuWorkSample is null)
        {
            if(Module.AbiMinor<2)throw new NotSupportedException("Work timing requires query20/v1.");
            if(sizeof(GpuWorkSampleNative)!=88||sizeof(RendererTimingApi)!=24)throw new InvalidOperationException("GPU work timing x64 layout.");
            RendererTimingApi api=default;PluginError queryError=default;
            PluginModule.Check(Module.Id,"query_gpu_work_timing",Module.ReadFunction<QuerySceneRender>(144)(Module.Context,20,&api,24,&queryError),queryError);
            if(api.Size!=24||api.Version!=1||api.Caps!=3||api.Read==0)throw new InvalidOperationException("GPU work timing query20/v1.");
            _readGpuWorkSample=Marshal.GetDelegateForFunctionPointer<ReadGpuWorkSampleNative>(api.Read);
        }
        GpuWorkSampleNative value=default;PluginError error=default;
        PluginModule.Check(Module.Id,"read_gpu_work_timing",_readGpuWorkSample(Module.Context,Handle,(uint)domain,&value,&error),error);
        var timing=ValidateGpuTiming(value.Timing,Handle,domain);
        if(domain==GpuTimingDomain.SkinDispatch?(value.Resource!=0||value.ContentRevision!=0):
            (timing.Sequence==0?(value.Resource!=0||value.ContentRevision!=0):(value.Resource==0||value.ContentRevision==0)))
            throw new InvalidOperationException("Invalid GPU work source metadata.");
        return new(timing,value.Resource,value.ContentRevision);
    }
}
