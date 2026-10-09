using System.Runtime.InteropServices;
using System.Text;
using Ncma.Interop;

namespace Ncma.Rendering;

[StructLayout(LayoutKind.Sequential)] internal unsafe struct SceneShadersNative { public uint Size,Version; public ShaderPairNative Geometry,Shadow,Tone; }
[StructLayout(LayoutKind.Sequential)] internal unsafe struct ComputeShaderNative { public uint Size,Version,Bytes,Reserved; public byte* Code; }
[StructLayout(LayoutKind.Sequential)] internal struct ShaderStagesApi { public uint Size,Version; public ulong Caps; public nint Source,CreateScene,ReplaceScene,CreateSkin,ReplaceSkin; }
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint StageSourceNative(ulong module,ulong renderer,uint kind,byte* output,uint capacity,uint* count,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint StageCreateSceneNative(ulong module,ulong renderer,SceneDescriptionV4* scene,SceneShadersNative* shaders,GpuMeshKey* key,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint StageReplaceSceneNative(ulong module,ulong renderer,GpuMeshKey key,SceneShadersNative* shaders,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint StageCreateSkinNative(ulong module,ulong renderer,SkinDescription* mesh,ComputeShaderNative* shader,GpuMeshKey* key,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint StageReplaceSkinNative(ulong module,ulong renderer,ComputeShaderNative* shader,PluginError* error);

public sealed record SceneShaderSelection(ShaderDescriptor GeometryVertex,ShaderDescriptor GeometryPixel,ShaderDescriptor ToneVertex,ShaderDescriptor TonePixel,ShaderDescriptor? ShadowVertex=null,ShaderDescriptor? ShadowPixel=null);
public readonly record struct RegisteredStageMetadata(string Role,string CatalogHash,Guid AssetId,string ContentHash,string BytecodeHash,bool Compiled);

public static class DefaultSceneShaders
{
    public static readonly Guid GeometryVertexId=Guid.Parse("fa182176-d879-40c0-bcf2-f7d09e545201"),GeometryPixelId=Guid.Parse("fa182176-d879-40c0-bcf2-f7d09e545202"),
        ShadowVertexId=Guid.Parse("fa182176-d879-40c0-bcf2-f7d09e545203"),ShadowPixelId=Guid.Parse("fa182176-d879-40c0-bcf2-f7d09e545204");
    internal static ShaderVertexInput[] Inputs => [new("POSITION",0,ShaderScalar.Float32,3,0),new("NORMAL",0,ShaderScalar.Float32,3,12),new("TEXCOORD",0,ShaderScalar.Float32,2,24),new("TANGENT",0,ShaderScalar.Float32,4,32)];
    internal static ShaderResourceBinding[] Resources(bool shadows)
    {
        string[] names=["BaseTex","NormalTex","MetalTex","RoughTex","AOTex","EmissiveTex","ShadowTex"];
        return names.Take(shadows?7:6).Select((n,i)=>new ShaderResourceBinding(n,ShaderResourceKind.Texture2D,ShaderResourceAccess.ReadOnly,i,1,0))
            .Concat(new[]{new ShaderResourceBinding("S",ShaderResourceKind.Sampler,ShaderResourceAccess.ReadOnly,0,1,0)})
            .Concat(shadows?new[]{new ShaderResourceBinding("SS",ShaderResourceKind.Sampler,ShaderResourceAccess.ReadOnly,6,1,0)}:[]).ToArray();
    }
    internal static ShaderDefinition[] Definitions(string source,bool shadows)
    {
        string variant=shadows?source:"#define NCMA_NO_SCENE_SHADOW\n"+source;
        ShaderDefinition D(Guid id,string name,ShaderStage stage,string entry,string text,ShaderVertexInput[] inputs,ShaderConstantBuffer[] constants,ShaderResourceBinding[] resources)=>
            new(1,id,name,ShaderProfile.Scene3D,stage,entry,text,ShaderContractCodec.HashSource(text),inputs,constants,resources,[]);
        var definitions=new List<ShaderDefinition>{
            D(GeometryVertexId,"Scene Geometry Vertex",ShaderStage.Vertex,"VSMain",variant,Inputs,DefaultSceneTone.Constants,[]),
            D(GeometryPixelId,"Scene Geometry Pixel",ShaderStage.Pixel,"PSMain",variant,[],DefaultSceneTone.Constants,Resources(shadows)),
            D(DefaultSceneTone.VertexId,"Scene Tone Vertex",ShaderStage.Vertex,"VSTone",source,DefaultSceneTone.Inputs,[],[]),
            D(DefaultSceneTone.PixelId,"Scene Tone Pixel",ShaderStage.Pixel,"PSTone",source,[],DefaultSceneTone.Constants,DefaultSceneTone.Resources)};
        if(shadows){definitions.Add(D(ShadowVertexId,"Scene Shadow Vertex",ShaderStage.Vertex,"VSShadowAlpha",source,Inputs,DefaultSceneTone.Constants,[]));
            definitions.Add(D(ShadowPixelId,"Scene Shadow Pixel",ShaderStage.Pixel,"PSShadow",source,[],DefaultSceneTone.Constants,DefaultSceneTone.Resources));}
        return definitions.ToArray();
    }
    public static ShaderCatalog CopyCatalog(RendererSession renderer,bool shadows=true)
    { ArgumentNullException.ThrowIfNull(renderer);return ShaderCatalog.Create(ShaderProfile.Scene3D,Definitions(renderer.CopyDefaultStageSource(0),shadows)); }
    internal static ShaderDescriptor Find(ShaderCatalog catalog,Guid id)
    { for(int page=0;page*ShaderCatalog.PageSize<catalog.Count;page++)foreach(var row in catalog.CopyPage(page))if(row.AssetId==id)return catalog.Require(id,row.ContentHash);throw new ArgumentException("Missing exact default shader identity."); }
    public static SceneShaderSelection Select(ShaderCatalog catalog,bool shadows=true) =>
        new(Find(catalog,GeometryVertexId),Find(catalog,GeometryPixelId),Find(catalog,DefaultSceneTone.VertexId),Find(catalog,DefaultSceneTone.PixelId),
            shadows?Find(catalog,ShadowVertexId):null,shadows?Find(catalog,ShadowPixelId):null);
}

public static class DefaultSkinShader
{
    public static readonly Guid ComputeId=Guid.Parse("fa182176-d879-40c0-bcf2-f7d09e545205");
    internal static ShaderConstantBuffer[] Constants => [new("Settings",0,16,new[]{"VertexCount","Offset","Unused","Unused2"}.Select((n,i)=>new ShaderConstantMember(n,ShaderScalar.UInt32,1,1,ShaderMatrixOrder.None,i*4,1,0)).ToArray())];
    internal static ShaderResourceBinding[] Resources => [new("Source",ShaderResourceKind.StructuredBuffer,ShaderResourceAccess.ReadOnly,0,1,80),new("Bones",ShaderResourceKind.StructuredBuffer,ShaderResourceAccess.ReadOnly,1,1,128),new("Output",ShaderResourceKind.RWByteAddressBuffer,ShaderResourceAccess.ReadWrite,0,1,0)];
    internal static ShaderDefinition Definition(string source)=>new(1,ComputeId,"Shared Skin Compute",ShaderProfile.Skinning,ShaderStage.Compute,"CSMain",source,ShaderContractCodec.HashSource(source),[],Constants,Resources,[]);
    public static ShaderCatalog CopyCatalog(RendererSession renderer)
    { ArgumentNullException.ThrowIfNull(renderer);return ShaderCatalog.Create(ShaderProfile.Skinning,[Definition(renderer.CopyDefaultStageSource(1))]); }
    public static ShaderDescriptor Select(ShaderCatalog catalog)=>DefaultSceneShaders.Find(catalog,ComputeId);
}

// CPU-only preparations, exact renderer lifetime and explicit trusted approval. No World/Agent authority.
public abstract class RegisteredShaderPreparation
{
    internal RendererSession Owner { get; }
    protected string CatalogHash { get; }
    private readonly Func<bool> _allowed; private bool _checking;
    private protected RegisteredShaderPreparation(RendererSession owner,string hash,Func<bool> allowed){Owner=owner;CatalogHash=hash;_allowed=allowed;}
    internal void Verify(RendererSession renderer){
        if(!ReferenceEquals(Owner,renderer))throw new ArgumentException("Foreign registered shader preparation.");
        renderer.VerifyShaderPreparation();if(_checking)throw new InvalidOperationException("Nonreentrant shader approval.");
        _checking=true;try{if(!_allowed())throw new InvalidOperationException("Trusted host off-simulation preparation permission required.");}finally{_checking=false;}renderer.VerifyShaderPreparation();
    }
    internal static CompiledShader PrepareOne(ShaderCatalog catalog,ShaderDescriptor selected,ShaderDefinition expected,ShaderCompilerService compiler){
        ArgumentNullException.ThrowIfNull(selected);if(catalog.Profile!=expected.Profile||selected.Stage!=expected.Stage)ShaderContractCodec.Fail("registered_profile_stage","stages");
        var exact=catalog.Require(selected.AssetId,selected.ContentHash);if(exact.CopyDefinition().Dependencies.Length!=0)ShaderContractCodec.Fail("registered_dependencies","stages");
        ShaderBindingValidation.Validate(exact,new(exact.ContentHash,exact.Stage,expected.Inputs,expected.Constants,expected.Resources));return compiler.Prepare(exact);
    }
    protected RegisteredStageMetadata Metadata(string role,CompiledShader shader)=>new(role,CatalogHash,shader.Descriptor.AssetId,shader.Descriptor.ContentHash,shader.BytecodeHash,true);
}
public sealed class RegisteredSceneShaders:RegisteredShaderPreparation
{
    internal CompiledShader[] Programs { get; }
    public bool Shadows { get; }
    private RegisteredSceneShaders(RendererSession renderer,string hash,CompiledShader[] shaders,bool shadows,Func<bool> allowed):base(renderer,hash,allowed){Programs=shaders;Shadows=shadows;}
    public static RegisteredSceneShaders Prepare(RendererSession renderer,ShaderCatalog catalog,SceneShaderSelection selected,Func<bool> preparationAllowed){
        ArgumentNullException.ThrowIfNull(renderer);ArgumentNullException.ThrowIfNull(catalog);ArgumentNullException.ThrowIfNull(selected);ArgumentNullException.ThrowIfNull(preparationAllowed);
        renderer.BeginToneOperation();try{
            bool shadows=selected.ShadowVertex is not null;if(shadows!=(selected.ShadowPixel is not null))ShaderContractCodec.Fail("registered_shadow_pair","stages");
            var expected=DefaultSceneShaders.Definitions("void unused(){}",shadows);
            ShaderDescriptor[] descriptors=shadows?[selected.GeometryVertex,selected.GeometryPixel,selected.ToneVertex,selected.TonePixel,selected.ShadowVertex!,selected.ShadowPixel!]:
                [selected.GeometryVertex,selected.GeometryPixel,selected.ToneVertex,selected.TonePixel];
            using var compiler=new ShaderCompilerService(renderer,preparationAllowed);var programs=new CompiledShader[descriptors.Length];
            for(int i=0;i<programs.Length;i++)programs[i]=PrepareOne(catalog,descriptors[i],expected[i],compiler);
            return new(renderer,catalog.ContentHash,programs,shadows,preparationAllowed);
        }finally{renderer.EndToneOperation();}
    }
    public RegisteredStageMetadata[] CopyMetadata()=>Programs.Select((s,i)=>Metadata(new[]{"geometry.vertex","geometry.pixel","tone.vertex","tone.pixel","shadow.vertex","shadow.pixel"}[i],s)).ToArray();
    internal byte[][] CopyCodes()=>Programs.Select(p=>p.CopyBytecode()).Concat(Shadows?Array.Empty<byte[]>():new byte[][]{[],[]}).ToArray();
}
public sealed class RegisteredSkinShader:RegisteredShaderPreparation
{
    internal CompiledShader Program { get; }
    private RegisteredSkinShader(RendererSession renderer,string hash,CompiledShader program,Func<bool> allowed):base(renderer,hash,allowed){Program=program;}
    public static RegisteredSkinShader Prepare(RendererSession renderer,ShaderCatalog catalog,ShaderDescriptor selected,Func<bool> preparationAllowed){
        ArgumentNullException.ThrowIfNull(renderer);ArgumentNullException.ThrowIfNull(catalog);ArgumentNullException.ThrowIfNull(preparationAllowed);
        renderer.BeginToneOperation();try{using var compiler=new ShaderCompilerService(renderer,preparationAllowed);return new(renderer,catalog.ContentHash,PrepareOne(catalog,selected,DefaultSkinShader.Definition("void unused(){}"),compiler),preparationAllowed);}
        finally{renderer.EndToneOperation();}
    }
    public RegisteredStageMetadata CopyMetadata()=>Metadata("skin.compute",Program);
}

public sealed unsafe partial class RendererSession
{
    private StageSourceNative? _stageSource; private StageCreateSceneNative? _stageCreateScene;private StageReplaceSceneNative? _stageReplaceScene;
    private StageCreateSkinNative? _stageCreateSkin;private StageReplaceSkinNative? _stageReplaceSkin;
    private void EnsureShaderStages(){
        Verify();if(_stageSource is not null)return;
        if(sizeof(SceneShadersNative)!=104||sizeof(ComputeShaderNative)!=24||sizeof(ShaderStagesApi)!=56)throw new PlatformNotSupportedException("Registered stages x64 ABI.");
        ShaderStagesApi api=default;PluginError error=default;PluginModule.Check(Module.Id,"query_shader_stages",Module.ReadFunction<QuerySceneRender>(144)(Module.Context,10,&api,56,&error),error);
        if(api.Size!=56||api.Version!=1||api.Caps!=3||api.Source==0||api.CreateScene==0||api.ReplaceScene==0||api.CreateSkin==0||api.ReplaceSkin==0)throw new InvalidOperationException("Registered stages query10/API1.");
        _stageCreateScene=Marshal.GetDelegateForFunctionPointer<StageCreateSceneNative>(api.CreateScene);_stageReplaceScene=Marshal.GetDelegateForFunctionPointer<StageReplaceSceneNative>(api.ReplaceScene);
        _stageCreateSkin=Marshal.GetDelegateForFunctionPointer<StageCreateSkinNative>(api.CreateSkin);_stageReplaceSkin=Marshal.GetDelegateForFunctionPointer<StageReplaceSkinNative>(api.ReplaceSkin);_stageSource=Marshal.GetDelegateForFunctionPointer<StageSourceNative>(api.Source);
    }
    internal string CopyDefaultStageSource(uint kind){
        EnsureShaderStages();byte[] bytes=new byte[ShaderContractCodec.MaxSourceBytes];uint count=0;PluginError error=default;
        fixed(byte* b=bytes)PluginModule.Check(Module.Id,"copy_stage_source",_stageSource!(Module.Context,Handle,kind,b,(uint)bytes.Length,&count,&error),error);
        if(count is 0 or > ShaderContractCodec.MaxSourceBytes)throw new InvalidOperationException("Stage source budget.");return new UTF8Encoding(false,true).GetString(bytes,0,(int)count);
    }
    internal void InstallSceneShaders(ScenePipelineSession session,uint resolution,RegisteredSceneShaders shaders,bool create){
        ArgumentNullException.ThrowIfNull(shaders);BeginToneOperation();try{
            EnsureScenePipeline();EnsureShaderStages();shaders.Verify(this);
            if(session.Owner!=this||shaders.Shadows!=(resolution!=0)|| (create?session.Key.Value!=0:session.Key.Value==0||!_scenePipelines.Contains(session)))throw new ArgumentException("Exact scene ownership/shadow group required.");
            var codes=shaders.CopyCodes();PluginError error=default;GpuMeshKey key=default;
            if(create)_scenePipelines.Add(session);
            try{fixed(byte* gv=codes[0])fixed(byte* gp=codes[1])fixed(byte* tv=codes[2])fixed(byte* tp=codes[3])fixed(byte* sv=codes[4])fixed(byte* sp=codes[5]){
                ShaderPairNative Pair(byte* v,byte* p,int vi,int pi)=>new(){Size=32,Version=1,VertexBytes=(uint)codes[vi].Length,PixelBytes=(uint)codes[pi].Length,Vertex=v,Pixel=p};
                SceneShadersNative native=new(){Size=104,Version=1,Geometry=Pair(gv,gp,0,1),Tone=Pair(tv,tp,2,3),Shadow=shaders.Shadows?Pair(sv,sp,4,5):default};
                uint result;if(create){SceneDescriptionV4 scene=new(){Size=16,Width=session.Plan.Width,Height=session.Plan.Height,Resolution=resolution};result=_stageCreateScene!(Module.Context,Handle,&scene,&native,&key,&error);}
                else result=_stageReplaceScene!(Module.Context,Handle,session.Key,&native,&error);
                PluginModule.Check(Module.Id,create?"create_registered_stages":"replace_registered_stages",result,error);
                if(create)session.Key=key;
            }}catch{if(create)_scenePipelines.Remove(session);throw;}
        }finally{EndToneOperation();}
    }
    public GpuMesh CreateSkinnedMesh(SkinUploadData data,RegisteredSkinShader shader){
        ArgumentNullException.ThrowIfNull(data);ArgumentNullException.ThrowIfNull(shader);BeginToneOperation();try{
            EnsureSkin();EnsureShaderStages();shader.Verify(this);byte[] code=shader.Program.CopyBytecode();var mesh=new GpuMesh(this,default,data.Attributes,data.BindingCount);GpuMeshKey key=default;PluginError error=default;_gpuMeshes.Add(mesh);
            try{fixed(byte* c=code)fixed(byte* v=data.Vertices)fixed(uint* i=data.Indices){
                ComputeShaderNative program=new(){Size=24,Version=1,Bytes=(uint)code.Length,Code=c};
                SkinDescription d=new(){Mesh=new(){Size=56,Layout=3,VertexCount=(uint)data.VertexCount,IndexCount=(uint)data.Indices.Length,VertexBytes=(uint)data.Vertices.Length,IndexBytes=checked((uint)data.Indices.Length*4),Stride=80,Vertices=v,Indices=i},Bindings=(uint)data.BindingCount};
                PluginModule.Check(Module.Id,"create_registered_skin",_stageCreateSkin!(Module.Context,Handle,&d,&program,&key,&error),error);
            }mesh.Publish(key);return mesh;}catch{_gpuMeshes.Remove(mesh);throw;}
        }finally{EndToneOperation();}
    }
    // Shared shader applies to ALL existing skin meshes in this renderer, not one GameObject.
    public void ReplaceSkinShader(RegisteredSkinShader shader){
        ArgumentNullException.ThrowIfNull(shader);BeginToneOperation();try{EnsureShaderStages();shader.Verify(this);byte[] code=shader.Program.CopyBytecode();PluginError error=default;
            fixed(byte* c=code){ComputeShaderNative d=new(){Size=24,Version=1,Bytes=(uint)code.Length,Code=c};PluginModule.Check(Module.Id,"replace_registered_skin",_stageReplaceSkin!(Module.Context,Handle,&d,&error),error);}
        }finally{EndToneOperation();}
    }
}
