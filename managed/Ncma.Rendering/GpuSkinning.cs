using System.Numerics;
using System.Runtime.InteropServices;
using Ncma.Interop;
namespace Ncma.Rendering;

[StructLayout(LayoutKind.Sequential)] public readonly record struct GpuSkinPalette(Matrix4x4 Model,Matrix4x4 Normal)
{
    public static GpuSkinPalette Create(Matrix4x4 model)
    { if(!Matrix4x4.Invert(model,out var inverse))throw new ArgumentException("Skin palette must be invertible.");return new(model,Matrix4x4.Transpose(inverse)); }
}
public readonly record struct GpuSkinRequest(GpuMesh Mesh,int PaletteOffset);
[StructLayout(LayoutKind.Sequential)] public struct GpuSkinStats
{public uint Size,MaxRequests;public ulong Generation,Meshes,ResidentBytes,Creates,Batches,PaletteBytes,Vertices,Captures;public double CpuMilliseconds,GpuMilliseconds;public uint GpuSampleValid,Reserved;}
[StructLayout(LayoutKind.Sequential)] internal unsafe struct SkinDescription {public MeshDescription Mesh;public uint Bindings,Reserved;}
[StructLayout(LayoutKind.Sequential)] internal struct SkinRequest {public GpuMeshKey Mesh;public uint Offset,Count;}
[StructLayout(LayoutKind.Sequential)] internal struct SkinBatch {public uint Size,Count;public ulong Frame,Generation;public uint Palettes,Reserved;}
[StructLayout(LayoutKind.Sequential)] internal struct SkinApi {public uint Size,Version;public ulong Caps;public nint Create,Destroy,Update,Capture,Stats;}
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CreateSkin(ulong module,ulong renderer,SkinDescription* input,GpuMeshKey* output,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint UpdateSkin(ulong module,ulong renderer,SkinBatch* input,SkinRequest* requests,GpuSkinPalette* palette,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CaptureSkin(ulong module,ulong renderer,GpuMeshKey mesh,byte* output,uint bytes,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint SkinStatistics(ulong module,ulong renderer,GpuSkinStats* output,PluginError* error);
public sealed unsafe partial class RendererSession
{
    private CreateSkin? _createSkin;private DestroyMesh? _destroySkin;private UpdateSkin? _updateSkin;private CaptureSkin? _captureSkin;private SkinStatistics? _skinStats;
    private void EnsureSkin()
    {
        Verify();if(_createSkin is not null)return;
        if(sizeof(SkinDescription)!=56||sizeof(SkinRequest)!=24||sizeof(SkinBatch)!=32||sizeof(GpuSkinPalette)!=128||sizeof(GpuSkinStats)!=96)throw new PlatformNotSupportedException("x64 skin layouts required.");
        SkinApi api=default;PluginError error=default;var query=Module.ReadFunction<QuerySceneRender>(144);
        PluginModule.Check(Module.Id,"query_gpu_skin",query(Module.Context,5,&api,56,&error),error);
        if(api.Size!=56||api.Version!=5||api.Caps!=3||api.Create==0||api.Destroy==0||api.Update==0||api.Capture==0||api.Stats==0)throw new ArgumentException("GPU skin service contract.");
        _destroySkin=Marshal.GetDelegateForFunctionPointer<DestroyMesh>(api.Destroy);_updateSkin=Marshal.GetDelegateForFunctionPointer<UpdateSkin>(api.Update);_captureSkin=Marshal.GetDelegateForFunctionPointer<CaptureSkin>(api.Capture);_skinStats=Marshal.GetDelegateForFunctionPointer<SkinStatistics>(api.Stats);_createSkin=Marshal.GetDelegateForFunctionPointer<CreateSkin>(api.Create);
    }
    public GpuMesh CreateSkinnedMesh(SkinUploadData data)
    {
        ArgumentNullException.ThrowIfNull(data);EnsureSkin();var mesh=new GpuMesh(this,default,data.Attributes,data.BindingCount);_gpuMeshes.Add(mesh);
        GpuMeshKey key=default;PluginError error=default;
        try{fixed(byte* v=data.Vertices)fixed(uint* i=data.Indices){SkinDescription d=new(){Mesh=new(){Size=56,Layout=3,VertexCount=(uint)data.VertexCount,IndexCount=(uint)data.Indices.Length,VertexBytes=(uint)data.Vertices.Length,IndexBytes=checked((uint)data.Indices.Length*4),Stride=80,Vertices=v,Indices=i},Bindings=(uint)data.BindingCount};PluginModule.Check(Module.Id,"create_gpu_skin",_createSkin!(Module.Context,Handle,&d,&key,&error),error);}mesh.Publish(key);return mesh;}
        catch{_gpuMeshes.Remove(mesh);throw;}
    }
    public void UpdateSkins(ulong frame,ReadOnlySpan<GpuSkinRequest> requests,ReadOnlySpan<GpuSkinPalette> palettes)
    { if(!TryUpdateSkins(frame,requests,palettes))throw new PluginException(Module.Id,"update_gpu_skin",PluginResult.Busy,"All bounded palette slots are in use."); }
    // Nonblocking backpressure is not a solver fault. Caller may skip this presentation, never a simulation tick.
    public bool TryUpdateSkins(ulong frame,ReadOnlySpan<GpuSkinRequest> requests,ReadOnlySpan<GpuSkinPalette> palettes)
    {
        EnsureSkin();if(requests.Length is <1 or >32 || palettes.Length is <1 or >32768)throw new ArgumentException("GPU skin batch budget.");
        Span<SkinRequest> values=stackalloc SkinRequest[32];int end=0;
        for(int i=0;i<requests.Length;i++){var r=requests[i];if(r.Mesh is null||r.Mesh.Owner!=this||!r.Mesh.IsAnimated||r.PaletteOffset!=end)throw new ArgumentException("GPU skin owner/type/palette range.");
            values[i]=new(){Mesh=r.Mesh.Key,Offset=(uint)end,Count=(uint)r.Mesh.SkinBindings};end=checked(end+r.Mesh.SkinBindings);}
        if(end!=palettes.Length)throw new ArgumentException("Complete palette ranges required.");
        SkinBatch batch=new(){Size=32,Count=(uint)requests.Length,Frame=frame,Generation=Handle,Palettes=(uint)palettes.Length};PluginError error=default;
        fixed(SkinRequest* r=values)fixed(GpuSkinPalette* p=palettes) {
            uint result=_updateSkin!(Module.Context,Handle,&batch,r,p,&error);
            if(result==(uint)PluginResult.Busy)return false;
            PluginModule.Check(Module.Id,"update_gpu_skin",result,error);
        }
        return true;
    }
    public GpuSkinStats SkinStats
    {get{EnsureSkin();GpuSkinStats s=default;PluginError error=default;PluginModule.Check(Module.Id,"gpu_skin_stats",_skinStats!(Module.Context,Handle,&s,&error),error);if(s.Size!=96||s.MaxRequests!=32||s.Generation!=Handle||s.Reserved!=0)throw new ArgumentException("GPU skin stats layout.");return s;}}
    // Explicit trusted diagnostic only; normal rendering never reads back GPU vertices.
    public void CaptureSkinVertices(GpuMesh mesh,Span<byte> output)
    {EnsureSkin();if(mesh.Owner!=this||!mesh.IsAnimated||output.Length!=mesh.VertexCount*48)throw new ArgumentException("Skin capture type/owner/capacity.");PluginError error=default;fixed(byte* p=output)PluginModule.Check(Module.Id,"capture_gpu_skin",_captureSkin!(Module.Context,Handle,mesh.Key,p,(uint)output.Length,&error),error);}
}
