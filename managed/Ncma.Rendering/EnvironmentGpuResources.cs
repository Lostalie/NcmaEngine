using System.Runtime.InteropServices;
using Ncma.Assets;
using Ncma.Interop;
namespace Ncma.Rendering;

[StructLayout(LayoutKind.Sequential)] internal unsafe struct EnvironmentGpuDescription {public uint Size,Version,Cube,Irradiance,Lut,Levels,Floats,Reserved;public float* Values;}
[StructLayout(LayoutKind.Sequential)] internal struct EnvironmentGpuApi {public uint Size,Version;public ulong Caps;public nint Publish,Destroy,Capture,Stats,Validate;}
[StructLayout(LayoutKind.Sequential)] public struct EnvironmentGpuStatistics {public uint Size,Version;public ulong Live,ResidentBytes,Publications,UploadedBytes,Captures,Retirements,BudgetBytes;}
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint PublishEnvironmentGpuNative(ulong module,ulong renderer,GpuMeshKey old,EnvironmentGpuDescription* description,GpuMeshKey* output,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CaptureEnvironmentGpuNative(ulong module,ulong renderer,GpuMeshKey key,float* output,uint capacity,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint DestroyEnvironmentGpuNative(ulong module,ulong renderer,GpuMeshKey key,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint EnvironmentGpuStatsNative(ulong module,ulong renderer,EnvironmentGpuStatistics* output,PluginError* error);

// Resource only. This does NOT bind an environment to a scene or assert IBL image validation.
public sealed class GpuEnvironmentResource : IDisposable
{
    internal RendererSession Owner {get;}
    internal GpuMeshKey Token;
    private EnvironmentPackage _package;
    public EnvironmentPackage Package {get{Verify();return _package;}}
    internal GpuEnvironmentResource(RendererSession owner,EnvironmentPackage package){Owner=owner;_package=package;}
    internal void Verify(){_=Owner.Handle;ObjectDisposedException.ThrowIf(Token.Value==0,this);}
    internal void Publish(GpuMeshKey key,EnvironmentPackage package){Token=key;_package=package;}
    public void Replace(EnvironmentPackage package,Func<bool> preparationAllowed)=>Owner.PublishEnvironment(this,package,preparationAllowed);
    // Offline diagnostic only; bounded GPU copy/drain. Never call from simulation/render ticks.
    public float[] CaptureForDiagnostics(Func<bool> preparationAllowed)=>Owner.CaptureEnvironment(this,preparationAllowed);
    public void Dispose(){if(Token.Value==0)return;Owner.ReleaseEnvironment(this);Token=default;}
}

public sealed unsafe partial class RendererSession
{
    private readonly HashSet<GpuEnvironmentResource> _environmentResources=[];
    private PublishEnvironmentGpuNative? _publishEnvironment;
    private DestroyEnvironmentGpuNative? _destroyEnvironment;
    private CaptureEnvironmentGpuNative? _captureEnvironment;
    private EnvironmentGpuStatsNative? _environmentStats;
    private RendererOperation? _validateEnvironment;
    private void EnsureEnvironmentApi()
    {
        Verify();if(_publishEnvironment is not null)return;
        if(sizeof(EnvironmentGpuDescription)!=40||sizeof(EnvironmentGpuApi)!=56||sizeof(EnvironmentGpuStatistics)!=64)throw new InvalidOperationException("Environment GPU x64 layout.");
        EnvironmentGpuApi api=default;PluginError error=default;
        PluginModule.Check(Module.Id,"query_environment_gpu",Module.ReadFunction<QuerySceneRender>(144)(Module.Context,14,&api,56,&error),error);
        if(api.Size!=56||api.Version!=1||api.Caps!=3||api.Publish==0||api.Destroy==0||api.Capture==0||api.Stats==0||api.Validate==0)throw new InvalidOperationException("Environment query14/API1.");
        var publish=Marshal.GetDelegateForFunctionPointer<PublishEnvironmentGpuNative>(api.Publish);
        var destroy=Marshal.GetDelegateForFunctionPointer<DestroyEnvironmentGpuNative>(api.Destroy);
        var capture=Marshal.GetDelegateForFunctionPointer<CaptureEnvironmentGpuNative>(api.Capture);
        var stats=Marshal.GetDelegateForFunctionPointer<EnvironmentGpuStatsNative>(api.Stats);
        var validate=Marshal.GetDelegateForFunctionPointer<RendererOperation>(api.Validate);
        _destroyEnvironment=destroy;_captureEnvironment=capture;_environmentStats=stats;_validateEnvironment=validate;_publishEnvironment=publish;
    }
    private void EnvironmentBoundary(Func<bool> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);EnsureEnvironmentApi();PluginError error=default;
        PluginModule.Check(Module.Id,"environment_gpu_boundary",_validateEnvironment!(Module.Context,Handle,&error),error);
        if(!allowed())throw new InvalidOperationException("Trusted off-simulation environment preparation approval required.");
        PluginModule.Check(Module.Id,"environment_gpu_boundary",_validateEnvironment!(Module.Context,Handle,&error),error);
    }
    public EnvironmentGpuStatistics EnvironmentStats
    {
        get{EnsureEnvironmentApi();PluginError error=default;EnvironmentGpuStatistics result=default;
            PluginModule.Check(Module.Id,"environment_gpu_stats",_environmentStats!(Module.Context,Handle,&result,&error),error);
            if(result.Size!=64||result.Version!=1||result.Live>8||result.ResidentBytes>result.BudgetBytes||result.BudgetBytes!=32ul*1024*1024)throw new InvalidOperationException("Environment stats response.");
            return result;}
    }
    public GpuEnvironmentResource CreateEnvironment(EnvironmentPackage package,Func<bool> preparationAllowed)
    {
        ArgumentNullException.ThrowIfNull(package);var candidate=new GpuEnvironmentResource(this,package);
        // Allocate managed ownership bookkeeping BEFORE native publication.
        BeginToneOperation();try{
            EnvironmentBoundary(preparationAllowed);_environmentResources.Add(candidate);
            try{PublishEnvironmentCore(candidate,package);return candidate;}catch{_environmentResources.Remove(candidate);throw;}
        }finally{EndToneOperation();}
    }
    internal void PublishEnvironment(GpuEnvironmentResource resource,EnvironmentPackage package,Func<bool> allowed)
    {
        ArgumentNullException.ThrowIfNull(package);BeginToneOperation();try{
            CheckEnvironment(resource);EnvironmentBoundary(allowed);PublishEnvironmentCore(resource,package);
        }finally{EndToneOperation();}
    }
    private void PublishEnvironmentCore(GpuEnvironmentResource resource,EnvironmentPackage package)
    {
        float[] values=package.CopyValues();var s=package.Settings;GpuMeshKey result=default;PluginError error=default;
        fixed(float* input=values){var d=new EnvironmentGpuDescription{Size=40,Version=1,Cube=s.CubeSize,Irradiance=s.IrradianceSize,Lut=s.LutSize,Levels=(uint)s.Levels,Floats=(uint)values.Length,Values=input};
            PluginModule.Check(Module.Id,"publish_environment_gpu",_publishEnvironment!(Module.Context,Handle,resource.Token,&d,&result,&error),error);}
        resource.Publish(result,package); // reference/primitive stores only, no post-publication allocation/callback.
    }
    private void CheckEnvironment(GpuEnvironmentResource resource)
    {ArgumentNullException.ThrowIfNull(resource);if(resource.Owner!=this||!_environmentResources.Contains(resource))throw new ArgumentException("Foreign/released environment.");resource.Verify();}
    internal float[] CaptureEnvironment(GpuEnvironmentResource resource,Func<bool> allowed)
    {
        BeginToneOperation();try{CheckEnvironment(resource);EnvironmentBoundary(allowed);
            float[] result=new float[resource.Package.Settings.FloatCount];PluginError error=default;
            fixed(float* values=result)PluginModule.Check(Module.Id,"capture_environment_gpu",_captureEnvironment!(Module.Context,Handle,resource.Token,values,(uint)result.Length,&error),error);
            return result;
        }finally{EndToneOperation();}
    }
    internal void ReleaseEnvironment(GpuEnvironmentResource resource)
    {
        // Explicit cleanup remains available after renderer fail-stop. Native drains before release.
        Verify();if(_toneOperation)throw new InvalidOperationException("Environment cleanup cannot reenter preparation.");
        CheckEnvironment(resource);EnsureEnvironmentApi();PluginError error=default;
        PluginModule.Check(Module.Id,"destroy_environment_gpu",_destroyEnvironment!(Module.Context,Handle,resource.Token,&error),error);
        _environmentResources.Remove(resource);
    }
}
