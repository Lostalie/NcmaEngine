using System.Runtime.InteropServices;
using Ncma.Assets;
using Ncma.Interop;
namespace Ncma.Rendering;

[StructLayout(LayoutKind.Sequential)] internal unsafe struct EnvironmentCookNative { public uint Size,Version,Width,Height,Cube,Irradiance,Lut,Samples,InputFloats,Reserved;public float* Input; }
[StructLayout(LayoutKind.Sequential)] internal struct EnvironmentCookOutputNative { public uint Size,Version,Algorithm,Floats,Levels,SourceBytes,Reserved,Reserved2; }
[StructLayout(LayoutKind.Sequential)] internal struct EnvironmentCookApiNative { public uint Size,Version;public ulong Caps;public nint Cook,Validate; }
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CookEnvironmentNative(ulong context,EnvironmentCookNative* input,EnvironmentCookOutputNative* output,float* values,uint capacity,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint ValidateEnvironmentNative(ulong context,PluginError* error);

// Explicit trusted OFF simulation/render preparation. Native CPU cook; not an Agent endpoint,
// async service or tick-safe inference. No renderer/window/device needed and no GPU resource.
public sealed unsafe class EnvironmentCookService : IDisposable
{
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private readonly PluginModule _module;
    private readonly PluginLease _lease;
    private readonly Func<bool> _allowed;
    private readonly CookEnvironmentNative _cook;
    private readonly ValidateEnvironmentNative _validate;
    private bool _busy,_disposed;
    private (Guid Id,ulong Generation,string Source,EnvironmentCookSettings Settings)? _key;
    private EnvironmentPackage? _cached;
    public ulong NativeCalls { get; private set; }
    public EnvironmentCookService(PluginModule module,Func<bool> preparationAllowed)
    {
        ArgumentNullException.ThrowIfNull(module);ArgumentNullException.ThrowIfNull(preparationAllowed);
        if(module.Kind!=ModuleKind.Renderer||module.AbiMinor<2||sizeof(EnvironmentCookNative)!=48||sizeof(EnvironmentCookOutputNative)!=32||sizeof(EnvironmentCookApiNative)!=32)
            throw new ArgumentException("Environment renderer x64 module contract.");
        _module=module;_allowed=preparationAllowed;_lease=module.AcquireLease();
        try{
            EnvironmentCookApiNative api=default;PluginError error=default;
            PluginModule.Check(module.Id,"query_environment_cook",module.ReadFunction<QuerySceneRender>(144)(module.Context,13,&api,32,&error),error);
            if(api.Size!=32||api.Version!=1||api.Caps!=1||api.Cook==0||api.Validate==0)throw new ArgumentException("Environment query13/API1.");
            _cook=Marshal.GetDelegateForFunctionPointer<CookEnvironmentNative>(api.Cook);_validate=Marshal.GetDelegateForFunctionPointer<ValidateEnvironmentNative>(api.Validate);
        }catch{_lease.Dispose();throw;}
    }
    private void Boundary()
    {
        PluginError error=default;PluginModule.Check(_module.Id,"environment_preparation",_validate(_module.Context,&error),error);
    }
    public EnvironmentPackage Cook(Guid assetId,ulong generation,HdrEnvironmentSource source,EnvironmentCookSettings settings,CancellationToken cancellation=default)
    {
        if(_owner!=Environment.CurrentManagedThreadId||_disposed||_busy)throw new InvalidOperationException("Environment service owner/lifetime/reentry.");
        ArgumentNullException.ThrowIfNull(source);settings.Validate();
        if(assetId==Guid.Empty||assetId==source.AssetId||generation==0)throw new ArgumentException("Environment cook asset/generation.");
        _busy=true;
        try{
            cancellation.ThrowIfCancellationRequested();Boundary();
            if(!_allowed())throw new InvalidOperationException("Trusted offline environment approval required.");
            Boundary();cancellation.ThrowIfCancellationRequested();
            var key=(assetId,generation,source.ContentHash,settings);if(_key==key)return _cached!;
            float[] values=new float[settings.FloatCount];EnvironmentCookOutputNative output=default;PluginError error=default;
            fixed(float* input=source.Pixels)fixed(float* result=values){
                var description=new EnvironmentCookNative{Size=48,Version=1,Width=source.Width,Height=source.Height,Cube=settings.CubeSize,Irradiance=settings.IrradianceSize,Lut=settings.LutSize,Samples=settings.Samples,InputFloats=(uint)source.Pixels.Length,Input=input};
                NativeCalls++;PluginModule.Check(_module.Id,"cook_environment",_cook(_module.Context,&description,&output,result,(uint)values.Length,&error),error);
            }
            cancellation.ThrowIfCancellationRequested();Boundary();
            if(output.Size!=32||output.Version!=1||output.Algorithm!=EnvironmentPackage.Algorithm||output.Floats!=values.Length||output.Levels!=settings.Levels||output.SourceBytes!=source.Pixels.Length*4||output.Reserved!=0||output.Reserved2!=0)
                throw new InvalidOperationException("Environment numerical response.");
            var candidate=EnvironmentPackage.Create(assetId,generation,source,settings,values);
            // Approval and cancellation rechecked before changing the single CPU cache entry.
            if(!_allowed())throw new InvalidOperationException("Environment approval revoked during cook.");Boundary();cancellation.ThrowIfCancellationRequested();
            _key=key;_cached=candidate;return candidate;
        }finally{_busy=false;}
    }
    public void Dispose()
    {
        if(_disposed)return;if(_owner!=Environment.CurrentManagedThreadId||_busy)throw new InvalidOperationException("Environment dispose owner/busy.");
        _lease.Dispose();_cached=null;_key=null;_disposed=true;
    }
}
