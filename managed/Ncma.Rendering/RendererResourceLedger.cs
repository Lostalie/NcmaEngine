using System.Runtime.InteropServices;
using Ncma.Interop;

namespace Ncma.Rendering;

public enum RendererResourceDomain { Meshes, Skin, TexturesMaterialsTargets, ScenePipelines, Environments, UiImagesLists, UiTargets, PostBindings }
public readonly record struct RendererResourceReading(RendererResourceDomain Domain, ulong LogicalBytes, ulong LiveResources);
public sealed record RendererResourceLedger(ulong Generation, bool PureUi, bool Failed, bool ActiveFrame, RendererResourceReading[] Domains)
{
    public int Version => 1;
    public bool PhysicalGpuMemoryAvailable => false;
    public bool CompleteNativeMemoryCoverage => false;
    public bool MemoryAccepted => false;
    public ulong? TotalBytes => null;
    public string Scope => "copied existing logical counters; domains overlap, especially mesh/skin; no cross-domain sum; " +
        "zero does not prove no resident allocations; excludes complete driver, shader, kernel, swapchain, ImGui and allocator coverage; " +
        "no resource initialization, GPU query resolution, waits or ownership transfer; observation is explicit, not automatic per-frame";
}
[StructLayout(LayoutKind.Sequential)] internal unsafe struct ResourceLedgerNative
{
    public uint Size,Version;public ulong Generation;public uint Flags,Reserved;
    public fixed ulong Bytes[8];public fixed ulong Counts[8];
}
[StructLayout(LayoutKind.Sequential)] internal struct ResourceLedgerApi {public uint Size,Version;public ulong Caps;public nint Read;}
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint ReadResourceLedger(ulong module,ulong renderer,ResourceLedgerNative* output,uint capacity,PluginError* error);

public sealed unsafe partial class RendererSession
{
    private ReadResourceLedger? _readResourceLedger;
    public RendererResourceLedger CaptureResourceLedger()
    {
        Verify();
        if(_readResourceLedger is null) {
            if(sizeof(ResourceLedgerNative)!=152||sizeof(ResourceLedgerApi)!=24)throw new PlatformNotSupportedException("Resource ledger x64 layout.");
            if(Module.AbiMinor<2)throw new NotSupportedException("Resource ledger requires renderer query support.");
            ResourceLedgerApi api=default;PluginError error=default;
            PluginModule.Check(Module.Id,"query_resource_ledger",Module.ReadFunction<QuerySceneRender>(144)(Module.Context,22,&api,24,&error),error);
            if(api.Size!=24||api.Version!=1||api.Caps!=1||api.Read==0)throw new InvalidOperationException("Resource ledger query22/v1 contract.");
            _readResourceLedger=Marshal.GetDelegateForFunctionPointer<ReadResourceLedger>(api.Read);
        }
        ResourceLedgerNative value=default;PluginError readError=default;
        PluginModule.Check(Module.Id,"read_resource_ledger",_readResourceLedger(Module.Context,Handle,&value,152,&readError),readError);
        if(value.Size!=152||value.Version!=1||value.Generation!=Handle||value.Reserved!=0||(value.Flags&~7u)!=0)
            throw new InvalidOperationException("Resource ledger response contract.");
        var domains=new RendererResourceReading[8];
        for(int i=0;i<domains.Length;i++)domains[i]=new((RendererResourceDomain)i,value.Bytes[i],value.Counts[i]);
        return new(value.Generation,(value.Flags&1)!=0,(value.Flags&2)!=0,(value.Flags&4)!=0,domains);
    }
}
