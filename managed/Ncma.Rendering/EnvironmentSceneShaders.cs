using System.Runtime.InteropServices;
using System.Text;
using Ncma.Assets;
using Ncma.Interop;
namespace Ncma.Rendering;

[StructLayout(LayoutKind.Sequential)] internal struct EnvironmentSceneApi {public uint Size,Version;public ulong Caps;public nint Source,Validate,Create,Replace,Bind;}
[StructLayout(LayoutKind.Sequential)] internal struct EnvironmentBindingNative {public uint Size,Version;public GpuMeshKey Resource;public float Strength,Rotation;public uint Reserved,Reserved2;}
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint EnvironmentSceneSourceNative(ulong module,ulong renderer,byte* output,uint capacity,uint* count,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint EnvironmentSceneBindNative(ulong module,ulong renderer,GpuMeshKey scene,EnvironmentBindingNative* binding,PluginError* error);

public static class DefaultEnvironmentSceneShaders
{
    public static readonly Guid GeometryVertexId=Guid.Parse("fa182176-d879-40c0-bcf2-f7d09e545209"),GeometryPixelId=Guid.Parse("fa182176-d879-40c0-bcf2-f7d09e545210");
    internal static ShaderConstantBuffer[] Constants => [DefaultSceneTone.Constants[0] with{ByteSize=416,Members=DefaultSceneTone.Constants[0].Members.Concat(new[]{new ShaderConstantMember("EnvironmentSettings",ShaderScalar.Float32,1,4,ShaderMatrixOrder.None,400,1,0)}).ToArray()}];
    internal static ShaderResourceBinding[] Resources(bool shadows)=>DefaultSceneShaders.Resources(shadows).Concat(new[]{
        new ShaderResourceBinding("IrradianceTex",ShaderResourceKind.TextureCube,ShaderResourceAccess.ReadOnly,7,1,0),
        new ShaderResourceBinding("SpecularTex",ShaderResourceKind.TextureCube,ShaderResourceAccess.ReadOnly,8,1,0),
        new ShaderResourceBinding("BrdfTex",ShaderResourceKind.Texture2D,ShaderResourceAccess.ReadOnly,9,1,0),
        new ShaderResourceBinding("EnvironmentSampler",ShaderResourceKind.Sampler,ShaderResourceAccess.ReadOnly,7,1,0)}).ToArray();
    internal static ShaderDefinition[] Definitions(string environment,string original,bool shadows){
        var definitions=DefaultSceneShaders.Definitions(original,shadows).Select(d=>d with{Profile=ShaderProfile.SceneEnvironment}).ToArray();
        string variant=shadows?environment:"#define NCMA_NO_SCENE_SHADOW\n"+environment;
        definitions[0]=definitions[0] with{AssetId=GeometryVertexId,Name="Environment Geometry Vertex",Source=variant,SourceHash=ShaderContractCodec.HashSource(variant),Constants=Constants};
        definitions[1]=definitions[1] with{AssetId=GeometryPixelId,Name="Environment Geometry Pixel",Source=variant,SourceHash=ShaderContractCodec.HashSource(variant),Constants=Constants,Resources=Resources(shadows)};
        return definitions;
    }
    public static ShaderCatalog CopyCatalog(RendererSession renderer,bool shadows=true)=>ShaderCatalog.Create(ShaderProfile.SceneEnvironment,Definitions(renderer.CopyEnvironmentSceneSource(),renderer.CopyDefaultStageSource(0),shadows));
    public static SceneShaderSelection Select(ShaderCatalog catalog,bool shadows=true)=>new(DefaultSceneShaders.Find(catalog,GeometryVertexId),DefaultSceneShaders.Find(catalog,GeometryPixelId),DefaultSceneShaders.Find(catalog,DefaultSceneTone.VertexId),DefaultSceneShaders.Find(catalog,DefaultSceneTone.PixelId),shadows?DefaultSceneShaders.Find(catalog,DefaultSceneShaders.ShadowVertexId):null,shadows?DefaultSceneShaders.Find(catalog,DefaultSceneShaders.ShadowPixelId):null);
}

// Separate closed contract, no implicit upgrade/downgrade of Scene3D. Runtime admission is
// actual whole-group reflection; neither package hashes nor this wrapper are a safety sandbox.
public sealed class RegisteredEnvironmentSceneShaders:RegisteredShaderPreparation
{
    private readonly CompiledShader[] _programs=[];
    private readonly RuntimeShaderPackage? _runtime;
    public bool Shadows {get;}
    internal RegisteredEnvironmentSceneShaders(RendererSession owner,RuntimeShaderPackage package,Func<bool> allowed):base(owner,package.ContentHash,allowed){_runtime=package;Shadows=package.Shadows;}
    private RegisteredEnvironmentSceneShaders(RendererSession owner,string hash,CompiledShader[] programs,bool shadows,Func<bool> allowed):base(owner,hash,allowed){_programs=programs;Shadows=shadows;}
    public static RegisteredEnvironmentSceneShaders Prepare(RendererSession renderer,ShaderCatalog catalog,SceneShaderSelection selected,Func<bool> preparationAllowed){
        ArgumentNullException.ThrowIfNull(renderer);ArgumentNullException.ThrowIfNull(catalog);ArgumentNullException.ThrowIfNull(selected);ArgumentNullException.ThrowIfNull(preparationAllowed);
        renderer.BeginToneOperation();try{
            bool shadows=selected.ShadowVertex is not null;if(shadows!=(selected.ShadowPixel is not null))throw new ArgumentException("Complete shadow pair.");
            var expected=DefaultEnvironmentSceneShaders.Definitions("void unused(){}","void unused(){}",shadows);
            ShaderDescriptor[] descriptors=shadows?[selected.GeometryVertex,selected.GeometryPixel,selected.ToneVertex,selected.TonePixel,selected.ShadowVertex!,selected.ShadowPixel!]:[selected.GeometryVertex,selected.GeometryPixel,selected.ToneVertex,selected.TonePixel];
            using var compiler=new ShaderCompilerService(renderer,preparationAllowed);var programs=new CompiledShader[descriptors.Length];
            for(int i=0;i<programs.Length;i++)programs[i]=PrepareOne(catalog,descriptors[i],expected[i],compiler);
            return new(renderer,catalog.ContentHash,programs,shadows,preparationAllowed);
        }finally{renderer.EndToneOperation();}
    }
    public RuntimeShaderPackage CookPackage(RegisteredSkinShader? skin=null){
        Owner.BeginToneOperation();try{Verify(Owner);if(_runtime is not null)throw new InvalidOperationException("Already source-free; cooking requires compiler artifacts.");
            RuntimeShaderRole[] roles=[RuntimeShaderRole.EnvironmentGeometryVertex,RuntimeShaderRole.EnvironmentGeometryPixel,RuntimeShaderRole.ToneVertex,RuntimeShaderRole.TonePixel,RuntimeShaderRole.ShadowVertex,RuntimeShaderRole.ShadowPixel];
            var inputs=_programs.Select((p,i)=>new RuntimeShaderInput(roles[i],p)).ToList();
            if(skin is not null){skin.Verify(Owner);if(skin.Program is null)throw new ArgumentException("Compiler-created skin required for cook.");inputs.Add(new(RuntimeShaderRole.SkinCompute,skin.Program));}
            return RuntimeShaderPackage.Cook(ShaderProfile.SceneEnvironment,Shadows,skin is not null,inputs);
        }finally{Owner.EndToneOperation();}
    }
    internal byte[][] CopyCodes()=>_runtime is null?_programs.Select(p=>p.CopyBytecode()).Concat(Shadows?Array.Empty<byte[]>():new byte[][]{[],[]}).ToArray():
        new[]{RuntimeShaderRole.EnvironmentGeometryVertex,RuntimeShaderRole.EnvironmentGeometryPixel,RuntimeShaderRole.ToneVertex,RuntimeShaderRole.TonePixel,RuntimeShaderRole.ShadowVertex,RuntimeShaderRole.ShadowPixel}.Select(r=>!Shadows&&(r==RuntimeShaderRole.ShadowVertex||r==RuntimeShaderRole.ShadowPixel)?Array.Empty<byte>():_runtime.CopyBytecode(r)).ToArray();
}

public sealed class EnvironmentShaderPreparation:RegisteredShaderPreparation
{
    public RuntimeShaderPackage Package {get;}
    public RegisteredEnvironmentSceneShaders Scene {get;}
    public RegisteredSkinShader? Skin {get;}
    public bool BindingsValidated=>true;
    private EnvironmentShaderPreparation(RendererSession owner,RuntimeShaderPackage package,Func<bool> allowed):base(owner,package.ContentHash,allowed){Package=package;Scene=new(owner,package,allowed);if(package.Skinning)Skin=new(owner,package,allowed);}
    public static EnvironmentShaderPreparation Prepare(RendererSession renderer,RuntimeShaderPackage package,Func<bool> preparationAllowed){
        ArgumentNullException.ThrowIfNull(renderer);ArgumentNullException.ThrowIfNull(package);ArgumentNullException.ThrowIfNull(preparationAllowed);
        if(package.Profile!=ShaderProfile.SceneEnvironment)throw new ArgumentException("Exact environment profile required.");
        renderer.BeginToneOperation();try{var candidate=new EnvironmentShaderPreparation(renderer,package,preparationAllowed);candidate.Verify(renderer);renderer.AdmitEnvironmentShaders(package);candidate.Verify(renderer);return candidate;}finally{renderer.EndToneOperation();}
    }
    public RegisteredStageMetadata[] CopyMetadata()=>RuntimeShaderPreparation.Metadata(Package);
    public void VerifyFor(RendererSession renderer) {
        ArgumentNullException.ThrowIfNull(renderer);renderer.BeginToneOperation();
        try{Verify(renderer);}finally{renderer.EndToneOperation();}
    }
}

public sealed unsafe partial class RendererSession
{
    private EnvironmentSceneSourceNative? _environmentSource;
    private ValidateRuntimeShadersNative? _environmentAdmission;
    private StageCreateSceneNative? _environmentCreate;
    private StageReplaceSceneNative? _environmentReplace;
    private EnvironmentSceneBindNative? _environmentBind;
    private void EnsureEnvironmentSceneApi(){
        Verify();if(_environmentSource is not null)return;
        if(sizeof(EnvironmentSceneApi)!=56||sizeof(EnvironmentBindingNative)!=40)throw new InvalidOperationException("Environment scene x64 ABI.");
        EnvironmentSceneApi api=default;PluginError error=default;PluginModule.Check(Module.Id,"query_environment_scene",Module.ReadFunction<QuerySceneRender>(144)(Module.Context,15,&api,56,&error),error);
        if(api.Size!=56||api.Version!=1||api.Caps!=3||api.Source==0||api.Validate==0||api.Create==0||api.Replace==0||api.Bind==0)throw new InvalidOperationException("Environment query15/API1.");
        var source=Marshal.GetDelegateForFunctionPointer<EnvironmentSceneSourceNative>(api.Source);
        _environmentAdmission=Marshal.GetDelegateForFunctionPointer<ValidateRuntimeShadersNative>(api.Validate);_environmentCreate=Marshal.GetDelegateForFunctionPointer<StageCreateSceneNative>(api.Create);
        _environmentReplace=Marshal.GetDelegateForFunctionPointer<StageReplaceSceneNative>(api.Replace);_environmentBind=Marshal.GetDelegateForFunctionPointer<EnvironmentSceneBindNative>(api.Bind);_environmentSource=source;
    }
    internal string CopyEnvironmentSceneSource(){EnsureEnvironmentSceneApi();byte[] bytes=new byte[ShaderContractCodec.MaxSourceBytes];uint count=0;PluginError error=default;
        fixed(byte* b=bytes)PluginModule.Check(Module.Id,"environment_scene_source",_environmentSource!(Module.Context,Handle,b,(uint)bytes.Length,&count,&error),error);
        if(count is 0 or >ShaderContractCodec.MaxSourceBytes)throw new InvalidOperationException("Environment source bound.");return new UTF8Encoding(false,true).GetString(bytes,0,(int)count);}
    internal void InstallEnvironmentSceneShaders(ScenePipelineSession session,uint resolution,RegisteredEnvironmentSceneShaders shaders,bool create){
        ArgumentNullException.ThrowIfNull(shaders);BeginToneOperation();try{
            EnsureScenePipeline();EnsureEnvironmentSceneApi();shaders.Verify(this);
            if(session.Owner!=this||shaders.Shadows!=(resolution!=0)||(create?session.Key.Value!=0:session.Key.Value==0||!_scenePipelines.Contains(session)||session.RegisteredEnvironmentShaders is null))throw new ArgumentException("Exact environment scene ownership/group required.");
            byte[][] codes=shaders.CopyCodes();GpuMeshKey key=default;PluginError error=default;if(create)_scenePipelines.Add(session);
            try{fixed(byte* gv=codes[0])fixed(byte* gp=codes[1])fixed(byte* tv=codes[2])fixed(byte* tp=codes[3])fixed(byte* sv=codes[4])fixed(byte* sp=codes[5]){
                ShaderPairNative Pair(byte* v,byte* p,int i)=>new(){Size=32,Version=1,Vertex=v,Pixel=p,VertexBytes=(uint)codes[i].Length,PixelBytes=(uint)codes[i+1].Length};
                SceneShadersNative native=new(){Size=104,Version=2,Geometry=Pair(gv,gp,0),Tone=Pair(tv,tp,2),Shadow=shaders.Shadows?Pair(sv,sp,4):default};
                uint result;if(create){SceneDescriptionV4 d=new(){Size=16,Width=session.Plan.Width,Height=session.Plan.Height,Resolution=resolution};result=_environmentCreate!(Module.Context,Handle,&d,&native,&key,&error);}else result=_environmentReplace!(Module.Context,Handle,session.Key,&native,&error);
                PluginModule.Check(Module.Id,create?"create_environment_scene":"replace_environment_scene",result,error);if(create)session.Key=key;
            }}catch{if(create)_scenePipelines.Remove(session);throw;}
        }finally{EndToneOperation();}
    }
    internal void AdmitEnvironmentShaders(RuntimeShaderPackage package){
        EnsureEnvironmentSceneApi();if(package.Profile!=ShaderProfile.SceneEnvironment)throw new ArgumentException("Exact environment package required.");
        byte[][] codes=new byte[11][];for(int i=0;i<codes.Length;i++)codes[i]=[];
        foreach(var row in Enumerable.Range(0,(package.Count+7)/8).SelectMany(package.CopyPage))codes[(int)row.Role]=package.CopyBytecode(row.Role);
        fixed(byte* gv=codes[9])fixed(byte* gp=codes[10])fixed(byte* tv=codes[4])fixed(byte* tp=codes[5])fixed(byte* sv=codes[6])fixed(byte* sp=codes[7])fixed(byte* cs=codes[8]){
            ShaderPairNative Pair(byte* v,byte* p,int i)=>new(){Size=32,Version=1,Vertex=v,Pixel=p,VertexBytes=(uint)codes[i].Length,PixelBytes=(uint)codes[i+1].Length};
            RuntimeShadersNative d=new(){Size=176,Version=2,Profile=3,Flags=(package.Shadows?1u:0u)|(package.Skinning?2u:0u),Scene=new(){Size=104,Version=2,Geometry=Pair(gv,gp,9),Tone=Pair(tv,tp,4),Shadow=package.Shadows?Pair(sv,sp,6):default},Skin=package.Skinning?new(){Size=24,Version=1,Bytes=(uint)codes[8].Length,Code=cs}:default};
            PluginError error=default;PluginModule.Check(Module.Id,"admit_environment_shaders",_environmentAdmission!(Module.Context,Handle,&d,&error),error);
        }
    }
    internal void BindEnvironment(ScenePipelineSession session,GpuEnvironmentResource? resource,EnvironmentLightingConfiguration config,Func<bool> allowed){
        ArgumentNullException.ThrowIfNull(allowed);BeginToneOperation();try{EnsureEnvironmentSceneApi();
            void Check(){config.Validate();if(session.Owner!=this||session.Key.Value==0||!_scenePipelines.Contains(session)||session.RegisteredEnvironmentShaders is null)throw new ArgumentException("Live environment scene required.");
                if(config.Enabled){if(resource is null)throw new ArgumentException("Enabled environment resource required.");CheckEnvironment(resource);var p=resource.Package;if(p.AssetId!=config.AssetId||p.Generation!=config.Generation||p.ContentHash!=config.ContentHash)throw new ArgumentException("Exact environment identity/generation/hash required.");}
                else if(resource is not null)throw new ArgumentException("Off requires no resource.");}
            Check();EnvironmentBoundary(allowed);Check();
            EnvironmentBindingNative d=new(){Size=40,Version=1,Resource=resource?.Token??default,Strength=config.Strength,Rotation=config.RotationRadians};PluginError error=default;
            PluginModule.Check(Module.Id,"bind_environment_scene",_environmentBind!(Module.Context,Handle,session.Key,&d,&error),error);
        }finally{EndToneOperation();}
    }
}
