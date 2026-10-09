using System.Runtime.InteropServices;
using System.Text;
using Ncma.Interop;

namespace Ncma.Rendering;

[StructLayout(LayoutKind.Sequential)] internal struct UiShadersApi { public uint Size,Version;public ulong Caps;public nint Source,Create,Replace; }
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint InstallUiShadersNative(ulong module,ulong renderer,ShaderPairNative* pair,PluginError* error);
public sealed record UiShaderSelection(ShaderDescriptor Vertex,ShaderDescriptor Pixel);

// The official and user definitions use the SAME Flat2D catalog/compiler; no Scene3D dependency.
public static class DefaultUiShaders
{
    public static readonly Guid VertexId=Guid.Parse("fa182176-d879-40c0-bcf2-f7d09e545301"),PixelId=Guid.Parse("fa182176-d879-40c0-bcf2-f7d09e545302");
    internal static ShaderVertexInput[] Inputs => [new("POSITION",0,ShaderScalar.Float32,2,0),new("TEXCOORD",0,ShaderScalar.Float32,2,8),new("COLOR",0,ShaderScalar.Float32,4,16),
        new("TEXCOORD",1,ShaderScalar.Float32,2,32),new("TEXCOORD",2,ShaderScalar.Float32,2,40),new("TEXCOORD",3,ShaderScalar.Float32,1,48)];
    internal static ShaderConstantBuffer[] Constants => [new("Frame",0,16,[new("Size",ShaderScalar.Float32,1,2,ShaderMatrixOrder.None,0,1,0),new("Pad",ShaderScalar.Float32,1,2,ShaderMatrixOrder.None,8,1,0)])];
    internal static ShaderResourceBinding[] Resources => [new("Image",ShaderResourceKind.Texture2D,ShaderResourceAccess.ReadOnly,0,1,0),new("Linear",ShaderResourceKind.Sampler,ShaderResourceAccess.ReadOnly,0,1,0)];
    internal static ShaderDefinition[] Definitions(string source)
    {
        ShaderDefinition D(Guid id,string name,ShaderStage stage,string entry,ShaderVertexInput[] inputs,ShaderConstantBuffer[] constants,ShaderResourceBinding[] resources)=>
            new(1,id,name,ShaderProfile.Flat2D,stage,entry,source,ShaderContractCodec.HashSource(source),inputs,constants,resources,[]);
        return [D(VertexId,"Flat UI Vertex",ShaderStage.Vertex,"VSMain",Inputs,Constants,[]),
            D(PixelId,"Flat UI Pixel",ShaderStage.Pixel,"PSMain",[],[],Resources)];
    }
    public static ShaderCatalog CopyCatalog(RendererSession renderer)
    { ArgumentNullException.ThrowIfNull(renderer);return ShaderCatalog.Create(ShaderProfile.Flat2D,Definitions(renderer.CopyDefaultUiSource())); }
    public static UiShaderSelection Select(ShaderCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ShaderDescriptor Find(Guid id){foreach(var row in Enumerable.Range(0,(catalog.Count+ShaderCatalog.PageSize-1)/ShaderCatalog.PageSize).SelectMany(catalog.CopyPage))if(row.AssetId==id)return catalog.Require(id,row.ContentHash);throw new ArgumentException("Missing exact UI shader identity.");}
        return new(Find(VertexId),Find(PixelId));
    }
}

public sealed class RegisteredUiShaders:RegisteredShaderPreparation
{
    internal CompiledShader Vertex { get; } internal CompiledShader Pixel { get; }
    private RegisteredUiShaders(RendererSession renderer,string hash,CompiledShader vertex,CompiledShader pixel,Func<bool> allowed):base(renderer,hash,allowed){Vertex=vertex;Pixel=pixel;}
    public static RegisteredUiShaders Prepare(RendererSession renderer,ShaderCatalog catalog,UiShaderSelection selected,Func<bool> preparationAllowed)
    {
        ArgumentNullException.ThrowIfNull(renderer);ArgumentNullException.ThrowIfNull(catalog);ArgumentNullException.ThrowIfNull(selected);ArgumentNullException.ThrowIfNull(preparationAllowed);
        renderer.BeginToneOperation();try{var expected=DefaultUiShaders.Definitions("void unused(){}");using var compiler=new ShaderCompilerService(renderer,preparationAllowed);
            return new(renderer,catalog.ContentHash,PrepareOne(catalog,selected.Vertex,expected[0],compiler),PrepareOne(catalog,selected.Pixel,expected[1],compiler),preparationAllowed);
        }finally{renderer.EndToneOperation();}
    }
    public RegisteredStageMetadata[] CopyMetadata()=>[Metadata("ui.vertex",Vertex),Metadata("ui.pixel",Pixel)];
}

public sealed unsafe partial class RendererSession
{
    private CopyToneSourceNative? _uiShaderSource;private InstallUiShadersNative? _uiShaderCreate,_uiShaderReplace;
    private RegisteredUiShaders? _registeredUiShaders;private ulong _uiShaderGeneration;
    public RegisteredUiShaders? RegisteredUiShaders { get { Verify();return _registeredUiShaders; } }
    // Host cache keys should include this generation. Existing target revisions retain their pixels.
    public ulong UiShaderGeneration { get { Verify();return _uiShaderGeneration; } }
    private void EnsureUiShaders()
    {
        Verify();if(_uiShaderSource is not null)return;
        if(sizeof(UiShadersApi)!=40||sizeof(ShaderPairNative)!=32)throw new PlatformNotSupportedException("Registered UI x64 ABI.");
        UiShadersApi api=default;PluginError error=default;
        PluginModule.Check(Module.Id,"query_ui_shaders",Module.ReadFunction<QuerySceneRender>(144)(Module.Context,11,&api,40,&error),error);
        if(api.Size!=40||api.Version!=1||api.Caps!=1||api.Source==0||api.Create==0||api.Replace==0)throw new InvalidOperationException("Registered UI query11/API1.");
        _uiShaderCreate=Marshal.GetDelegateForFunctionPointer<InstallUiShadersNative>(api.Create);_uiShaderReplace=Marshal.GetDelegateForFunctionPointer<InstallUiShadersNative>(api.Replace);
        _uiShaderSource=Marshal.GetDelegateForFunctionPointer<CopyToneSourceNative>(api.Source);
    }
    internal string CopyDefaultUiSource()
    {
        EnsureUiShaders();byte[] bytes=new byte[ShaderContractCodec.MaxSourceBytes];uint count=0;PluginError error=default;
        fixed(byte* b=bytes)PluginModule.Check(Module.Id,"copy_ui_shader_source",_uiShaderSource!(Module.Context,Handle,b,(uint)bytes.Length,&count,&error),error);
        if(count is 0 or > ShaderContractCodec.MaxSourceBytes)throw new InvalidOperationException("UI source budget.");return new UTF8Encoding(false,true).GetString(bytes,0,(int)count);
    }
    // Explicit preparation boundary BEFORE any lazy UI image/target creation. No render-tick compile.
    public void CreateUiShaders(RegisteredUiShaders shaders)=>InstallUiShaders(shaders,true);
    public void ReplaceUiShaders(RegisteredUiShaders shaders)=>InstallUiShaders(shaders,false);
    private void InstallUiShaders(RegisteredUiShaders shaders,bool create)
    {
        ArgumentNullException.ThrowIfNull(shaders);BeginToneOperation();try{EnsureUiShaders();shaders.Verify(this);
            ulong generation=checked(_uiShaderGeneration+1);byte[] vertex=shaders.Vertex.CopyBytecode(),pixel=shaders.Pixel.CopyBytecode();PluginError error=default;
            fixed(byte* v=vertex)fixed(byte* p=pixel){ShaderPairNative pair=new(){Size=32,Version=1,VertexBytes=(uint)vertex.Length,PixelBytes=(uint)pixel.Length,Vertex=v,Pixel=p};
                PluginModule.Check(Module.Id,create?"create_ui_shaders":"replace_ui_shaders",(create?_uiShaderCreate!:_uiShaderReplace!)(Module.Context,Handle,&pair,&error),error);}
            _registeredUiShaders=shaders;_uiShaderGeneration=generation;
        }finally{EndToneOperation();}
    }
}
