using System.Numerics;
using System.Runtime.InteropServices;
using Ncma.Assets;
using Ncma.Interop;
namespace Ncma.Rendering;
[StructLayout(LayoutKind.Sequential)] internal unsafe struct TextureDescriptionV3 {public uint Size,Width,Height,Format,MipCount,Bytes,Reserved,Reserved2;public TextureMip* Mips;public byte* Pixels;}
[StructLayout(LayoutKind.Sequential)] internal unsafe struct MaterialDescriptionV3 {public uint Size,Flags;public fixed ulong Textures[12];public fixed float Base[4];public fixed float Emissive[4];public fixed float Surface[4];public uint Channels,Reserved;}
[StructLayout(LayoutKind.Sequential)] internal struct TargetDescriptionV3 {public uint Size,Width,Height,Reserved;}
[StructLayout(LayoutKind.Sequential)] public struct ResourceRenderStats {public uint Size,Reserved;public ulong Generation,Textures,Materials,Targets,ResidentBytes,UploadedBytes,Creates,Draws;}
[StructLayout(LayoutKind.Sequential)] internal struct ResourceApiV3 {public uint Size,Version;public ulong Caps;public nint Texture,Material,Target,Destroy,Submit,Stats,Capture;}
[StructLayout(LayoutKind.Sequential)] internal unsafe struct ResourceFrameV3 {public uint Size,Count;public ulong Frame,Generation;public GpuMeshKey Target;public fixed float Viewport[4];public fixed float Clear[4];public fixed float Camera[4];public fixed float Light[4];public fixed float LightColor[4];public float Exposure,Ambient;public uint Mode,Reserved;}
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CreateTextureV3(ulong module,ulong renderer,TextureDescriptionV3* input,GpuMeshKey* output,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CreateMaterialV3(ulong module,ulong renderer,MaterialDescriptionV3* input,GpuMeshKey* output,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CreateTargetV3(ulong module,ulong renderer,TargetDescriptionV3* input,GpuMeshKey* output,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint SubmitResourcesV3(ulong module,ulong renderer,ResourceFrameV3* input,ResourceDraw* draws,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint StatsResourcesV3(ulong module,ulong renderer,ResourceRenderStats* output,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CaptureTargetV3(ulong module,ulong renderer,GpuMeshKey target,byte* output,uint capacity,PluginError* error);
public enum ResourceDrawMode:uint {Pbr,Unlit,NormalDiagnostic}
public readonly record struct ResourceLighting(Vector3 Camera,Vector3 LightDirection,Vector4 LightColor,float Exposure=1,float Ambient=.03f);
public abstract class GpuResource : IDisposable
{
    internal RendererSession Owner {get;}
    private GpuMeshKey _key;
    internal GpuMeshKey Key {get{_=Owner.Handle;ObjectDisposedException.ThrowIf(_key.Value==0,this);return _key;}}
    internal GpuResource(RendererSession owner){Owner=owner;}
    internal void Publish(GpuMeshKey key)=>_key=key;
    public void Dispose(){if(_key.Value==0)return;Owner.ReleaseResource(this,_key);_key=default;}
}
public sealed class GpuTexture : GpuResource {public TextureSemantic Semantic {get;}public bool NormalYDown {get;}internal GpuTexture(RendererSession owner,TextureData data):base(owner){Semantic=data.Semantic;NormalYDown=data.NormalYDown;}}
public sealed class GpuMaterial : GpuResource {public bool UsesNormalMap {get;}internal GpuMaterial(RendererSession owner,bool normal):base(owner){UsesNormalMap=normal;}}
// Opaque renderer-owned identity. It is never an ImTextureID, COM pointer or serialized asset.
[StructLayout(LayoutKind.Sequential)] public readonly record struct GuiImageToken(ulong Value,ulong Generation);
public sealed class GpuViewTarget : GpuResource {public uint Width{get;}public uint Height{get;}public GuiImageToken ImageToken {get{var key=Key;return new(key.Value,key.Generation);}}internal GpuViewTarget(RendererSession owner,uint width,uint height):base(owner){Width=width;Height=height;}}
[StructLayout(LayoutKind.Sequential)] public unsafe struct ResourceDraw
{
    internal GpuMeshKey Mesh,Material;
    public uint FirstIndex,IndexCount;
    internal uint Reserved,Reserved2;
    internal fixed float Model[16];internal fixed float MVP[16];internal fixed float Normal[16];
    public static ResourceDraw Create(GpuMesh mesh,GpuMaterial material,MeshDrawRange range,Matrix4x4 model,Matrix4x4 viewProjection)
    {
        ArgumentNullException.ThrowIfNull(mesh);ArgumentNullException.ThrowIfNull(material);
        if(mesh.Owner!=material.Owner)throw new ArgumentException("Foreign resource owner.");
        if(material.UsesNormalMap&&!mesh.CanUseNormalMap)throw new ArgumentException("Normal-map disabled for unsafe/missing UV/tangents or bind preview.");
        if(model.M14!=0||model.M24!=0||model.M34!=0||model.M44!=1||!float.IsFinite(model.GetDeterminant())||model.GetDeterminant()<=1e-8f||!Matrix4x4.Invert(model,out var inverse))throw new ArgumentException("Positive invertible affine model required.");
        ResourceDraw d=new(){Mesh=mesh.Key,Material=material.Key,FirstIndex=range.FirstIndex,IndexCount=range.IndexCount};
        Copy(model,d.Model);Copy(model*viewProjection,d.MVP);Copy(Matrix4x4.Transpose(inverse),d.Normal);return d;
    }
    private static void Copy(Matrix4x4 m,float* output){ReadOnlySpan<float> values=[m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];for(int i=0;i<16;i++){if(!float.IsFinite(values[i]))throw new ArgumentException("Nonfinite draw matrix.");output[i]=values[i];}}
}
public sealed unsafe partial class RendererSession
{
    private readonly HashSet<GpuResource> _resources=[];
    private CreateTextureV3? _createTexture;private CreateMaterialV3? _createMaterial;private CreateTargetV3? _createTarget;
    private DestroyMesh? _destroyResource;private SubmitResourcesV3? _submitResources;private StatsResourcesV3? _resourceStats;private CaptureTargetV3? _captureTarget;
    private void EnsureResources()
    {
        Verify();if(_createTexture is not null)return;if(!SupportsStaticMeshes)throw new NotSupportedException("Renderer 1.2 resource query required.");
        var query=Module.ReadFunction<QuerySceneRender>(144);ResourceApiV3 api=default;PluginError error=default;
        PluginModule.Check(Module.Id,"query_resources",query(Module.Context,3,&api,72,&error),error);
        if(api.Size!=72||api.Version!=3||api.Caps!=0x3c||api.Texture==0||api.Material==0||api.Target==0||api.Destroy==0||api.Submit==0||api.Stats==0||api.Capture==0)throw new PluginException(Module.Id,"query_resources",PluginResult.AbiMismatch,"Resource service layout/capabilities.");
        var texture=Marshal.GetDelegateForFunctionPointer<CreateTextureV3>(api.Texture);var material=Marshal.GetDelegateForFunctionPointer<CreateMaterialV3>(api.Material);var target=Marshal.GetDelegateForFunctionPointer<CreateTargetV3>(api.Target);
        var destroy=Marshal.GetDelegateForFunctionPointer<DestroyMesh>(api.Destroy);var submit=Marshal.GetDelegateForFunctionPointer<SubmitResourcesV3>(api.Submit);var stats=Marshal.GetDelegateForFunctionPointer<StatsResourcesV3>(api.Stats);var capture=Marshal.GetDelegateForFunctionPointer<CaptureTargetV3>(api.Capture);
        _createMaterial=material;_createTarget=target;_destroyResource=destroy;_submitResources=submit;_resourceStats=stats;_captureTarget=capture;_createTexture=texture;
    }
    public GpuTexture CreateTexture(TextureData data)
    {
        ArgumentNullException.ThrowIfNull(data);EnsureResources();var lease=new GpuTexture(this,data);_resources.Add(lease);GpuMeshKey key=default;PluginError error=default;
        try{fixed(TextureMip* mips=data.Mips)fixed(byte* pixels=data.Pixels){TextureDescriptionV3 d=new(){Size=48,Width=data.Width,Height=data.Height,Format=data.Srgb?2u:1u,MipCount=(uint)data.Mips.Length,Bytes=(uint)data.Pixels.Length,Mips=mips,Pixels=pixels};PluginModule.Check(Module.Id,"create_texture",_createTexture!(Module.Context,Handle,&d,&key,&error),error);}lease.Publish(key);return lease;}catch{_resources.Remove(lease);throw;}
    }
    public GpuMaterial CreateMaterial(MaterialDefinition definition,ReadOnlySpan<GpuTexture?> textures,bool enableNormalMap)
    {
        MaterialCodec.Validate(definition);if(textures.Length!=6)throw new ArgumentException("Six material texture slots required.");EnsureResources();MaterialDescriptionV3 d=new(){Size=160,Flags=definition.Mode==MaterialMode.AlphaMask?1u:0u,Channels=definition.MetallicChannel|(definition.RoughnessChannel<<8)|(definition.AOChannel<<16)};
        d.Base[0]=definition.BaseColor.R;d.Base[1]=definition.BaseColor.G;d.Base[2]=definition.BaseColor.B;d.Base[3]=definition.BaseColor.A;
        d.Emissive[0]=definition.Emissive.R;d.Emissive[1]=definition.Emissive.G;d.Emissive[2]=definition.Emissive.B;d.Emissive[3]=definition.Emissive.A;
        d.Surface[0]=definition.Metallic;d.Surface[1]=definition.Roughness;d.Surface[2]=definition.NormalScale;d.Surface[3]=definition.AlphaCutoff;
        for(int i=0;i<6;i++)if(textures[i] is { } t){if(t.Owner!=this||t.Semantic!=MaterialSurfaceContract.Semantic(i))throw new ArgumentException("Foreign texture or semantic/color-space role mismatch.");var k=t.Key;d.Textures[i*2]=k.Value;d.Textures[i*2+1]=k.Generation;}
        bool normal=enableNormalMap&&textures[1] is not null;if(normal){d.Flags|=2;if(textures[1]!.NormalYDown)d.Flags|=4;}
        var lease=new GpuMaterial(this,normal);_resources.Add(lease);GpuMeshKey key=default;PluginError error=default;
        try{PluginModule.Check(Module.Id,"create_material",_createMaterial!(Module.Context,Handle,&d,&key,&error),error);lease.Publish(key);return lease;}catch{_resources.Remove(lease);throw;}
    }
    public GpuViewTarget CreateViewTarget(uint width,uint height)
    {
        EnsureResources();TargetDescriptionV3 d=new(){Size=16,Width=width,Height=height};var lease=new GpuViewTarget(this,width,height);_resources.Add(lease);GpuMeshKey key=default;PluginError error=default;
        try{PluginModule.Check(Module.Id,"create_target",_createTarget!(Module.Context,Handle,&d,&key,&error),error);lease.Publish(key);return lease;}catch{_resources.Remove(lease);throw;}
    }
    internal void ReleaseResource(GpuResource lease,GpuMeshKey key){Verify();PluginError error=default;PluginModule.Check(Module.Id,"destroy_resource",_destroyResource!(Module.Context,Handle,key,&error),error);_resources.Remove(lease);}
    public ResourceRenderStats ResourceStats {get{EnsureResources();ResourceRenderStats s=default;PluginError error=default;PluginModule.Check(Module.Id,"resource_stats",_resourceStats!(Module.Context,Handle,&s,&error),error);if(s.Size!=72||s.Reserved!=0||s.Generation!=Handle)throw new ArgumentException("Resource stats contract.");return s;}}
    public void SubmitResources(ulong frame,ReadOnlySpan<ResourceDraw> draws,Vector4 viewport,Vector4 linearClear,ResourceLighting lighting,GpuViewTarget? target=null,ResourceDrawMode mode=ResourceDrawMode.Pbr)
    {
        EnsureResources();if(draws.Length>4096)throw new ArgumentException("Draw budget.");if(target is not null&&target.Owner!=this)throw new ArgumentException("Foreign view target.");
        ResourceFrameV3 f=new(){Size=136,Count=(uint)draws.Length,Frame=frame,Generation=Handle,Target=target?.Key??default,Exposure=lighting.Exposure,Ambient=lighting.Ambient,Mode=(uint)mode};
        f.Viewport[0]=viewport.X;f.Viewport[1]=viewport.Y;f.Viewport[2]=viewport.Z;f.Viewport[3]=viewport.W;
        f.Clear[0]=linearClear.X;f.Clear[1]=linearClear.Y;f.Clear[2]=linearClear.Z;f.Clear[3]=linearClear.W;
        f.Camera[0]=lighting.Camera.X;f.Camera[1]=lighting.Camera.Y;f.Camera[2]=lighting.Camera.Z;f.Camera[3]=1;
        f.Light[0]=lighting.LightDirection.X;f.Light[1]=lighting.LightDirection.Y;f.Light[2]=lighting.LightDirection.Z;
        f.LightColor[0]=lighting.LightColor.X;f.LightColor[1]=lighting.LightColor.Y;f.LightColor[2]=lighting.LightColor.Z;f.LightColor[3]=lighting.LightColor.W;
        PluginError error=default;fixed(ResourceDraw* p=draws)PluginModule.Check(Module.Id,"submit_resources",_submitResources!(Module.Context,Handle,&f,p,&error),error);
        SubmitCalls++;CopiedBytes+=136+(ulong)draws.Length*240;
    }
    public void CaptureTarget(GpuViewTarget target,Span<byte> pixels)
    {ArgumentNullException.ThrowIfNull(target);EnsureResources();if(target.Owner!=this)throw new ArgumentException("Foreign target.");PluginError error=default;fixed(byte* p=pixels)PluginModule.Check(Module.Id,"capture_target",_captureTarget!(Module.Context,Handle,target.Key,p,(uint)pixels.Length,&error),error);}
}
