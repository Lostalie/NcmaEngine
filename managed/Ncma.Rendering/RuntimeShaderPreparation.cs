using System.Runtime.InteropServices;
using Ncma.Interop;

namespace Ncma.Rendering;

[StructLayout(LayoutKind.Sequential)] internal struct RuntimeShadersNative {
    public uint Size,Version,Profile,Flags;
    public ShaderPairNative Ui;
    public SceneShadersNative Scene;
    public ComputeShaderNative Skin;
}
[StructLayout(LayoutKind.Sequential)] internal struct RuntimeShadersApi { public uint Size,Version;public ulong Caps;public nint Validate; }
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint ValidateRuntimeShadersNative(ulong module,ulong renderer,RuntimeShadersNative* shaders,PluginError* error);

// Successful real native reflection, not a shader safety sandbox or GPU resource. Source-free
// wrappers can only originate here AFTER the whole package passes. Every install still validates
// actual bytes and exact owner/lifetime/approval. No filesystem, World or Agent authority.
public sealed class RuntimeShaderPreparation : RegisteredShaderPreparation
{
    public RuntimeShaderPackage Package { get; }
    public RegisteredUiShaders? Ui { get; }
    public RegisteredSceneShaders? Scene { get; }
    public RegisteredSkinShader? Skin { get; }
    public bool BindingsValidated => true;
    private RuntimeShaderPreparation(RendererSession renderer,RuntimeShaderPackage package,Func<bool> allowed):base(renderer,package.ContentHash,allowed) {
        Package=package;
        if(package.Profile==ShaderProfile.Flat2D)Ui=new(renderer,package,allowed);
        else {Scene=new(renderer,package,allowed);if(package.Skinning)Skin=new(renderer,package,allowed);}
    }
    public static RuntimeShaderPreparation Prepare(RendererSession renderer,RuntimeShaderPackage package,Func<bool> preparationAllowed) {
        ArgumentNullException.ThrowIfNull(renderer);ArgumentNullException.ThrowIfNull(package);ArgumentNullException.ThrowIfNull(preparationAllowed);
        renderer.BeginToneOperation();
        try {
            var candidate=new RuntimeShaderPreparation(renderer,package,preparationAllowed);
            candidate.Verify(renderer);renderer.ValidateRuntimeShaders(package);candidate.Verify(renderer);
            return candidate;
        } finally {renderer.EndToneOperation();}
    }
    public RegisteredStageMetadata[] CopyMetadata()=>Metadata(Package);
    public void VerifyFor(RendererSession renderer) {
        ArgumentNullException.ThrowIfNull(renderer);renderer.BeginToneOperation();
        try{Verify(renderer);}finally{renderer.EndToneOperation();}
    }
    internal static RegisteredStageMetadata[] Metadata(RuntimeShaderPackage package) {
        string[] roles=["ui.vertex","ui.pixel","geometry.vertex","geometry.pixel","tone.vertex","tone.pixel","shadow.vertex","shadow.pixel","skin.compute"];
        return Enumerable.Range(0,(package.Count+7)/8).SelectMany(package.CopyPage)
            .Select(m=>new RegisteredStageMetadata(roles[(int)m.Role],package.ContentHash,m.AssetId,m.AuthorContentHash,m.BytecodeHash,true)).ToArray();
    }
}

public sealed unsafe partial class RendererSession
{
    private DefaultRuntimeShaderService? _defaultRuntimeShaders;
    private bool _uiKernelReady;
    public DefaultRuntimeShaderService DefaultRuntimeShaders { get { VerifyShaderPreparation();return _defaultRuntimeShaders??=new(this); } }
    // Called by shared UI composition before images/cache. Explicitly preserves a user selection.
    public void PrepareDefaultUiShaders() {
        VerifyShaderPreparation();if(_registeredUiShaders is not null)return;
        bool preparing=true;
        try {var prepared=DefaultRuntimeShaders.Prepare(ShaderProfile.Flat2D,false,false,()=>preparing);
            if(_uiKernelReady)ReplaceUiShaders(prepared.Ui!);else CreateUiShaders(prepared.Ui!);
        }finally{preparing=false;}
    }
    private ValidateRuntimeShadersNative? _validateRuntimeShaders;
    internal void ValidateRuntimeShaders(RuntimeShaderPackage package) {
        VerifyShaderPreparation();
        if(_validateRuntimeShaders is null) {
            if(sizeof(RuntimeShadersNative)!=176||sizeof(RuntimeShadersApi)!=24)throw new PlatformNotSupportedException("Runtime shader x64 ABI.");
            RuntimeShadersApi api=default;PluginError e=default;
            PluginModule.Check(Module.Id,"query_runtime_shaders",Module.ReadFunction<QuerySceneRender>(144)(Module.Context,12,&api,24,&e),e);
            if(api.Size!=24||api.Version!=1||api.Caps!=1||api.Validate==0)throw new InvalidOperationException("Runtime shaders query12/API1.");
            _validateRuntimeShaders=Marshal.GetDelegateForFunctionPointer<ValidateRuntimeShadersNative>(api.Validate);
        }
        byte[][] code=new byte[9][];
        for(int i=0;i<9;i++)code[i]=[];
        foreach(var row in Enumerable.Range(0,(package.Count+7)/8).SelectMany(package.CopyPage))code[(int)row.Role]=package.CopyBytecode(row.Role);
        fixed(byte* uv=code[0])fixed(byte* up=code[1])fixed(byte* gv=code[2])fixed(byte* gp=code[3])
        fixed(byte* tv=code[4])fixed(byte* tp=code[5])fixed(byte* sv=code[6])fixed(byte* sp=code[7])fixed(byte* cs=code[8]) {
            ShaderPairNative Pair(byte* v,byte* p,int i)=>new(){Size=32,Version=1,Vertex=v,Pixel=p,VertexBytes=(uint)code[i].Length,PixelBytes=(uint)code[i+1].Length};
            RuntimeShadersNative d=new(){Size=176,Version=1,Profile=(uint)package.Profile,Flags=(package.Shadows?1u:0u)|(package.Skinning?2u:0u)};
            if(package.Profile==ShaderProfile.Flat2D)d.Ui=Pair(uv,up,0);
            else {d.Scene=new(){Size=104,Version=1,Geometry=Pair(gv,gp,2),Tone=Pair(tv,tp,4),Shadow=package.Shadows?Pair(sv,sp,6):default};
                if(package.Skinning)d.Skin=new(){Size=24,Version=1,Bytes=(uint)code[8].Length,Code=cs};}
            PluginError error=default;PluginModule.Check(Module.Id,"validate_runtime_shaders",_validateRuntimeShaders(Module.Context,Handle,&d,&error),error);
        }
    }
}

// Shared bounded official startup preparation. User packages use Prepare directly and never
// replace this cache implicitly. C4-C supplies trusted file selection/deployment; until then
// official source cooking remains an explicit off-frame startup step, never a submission step.
public sealed class DefaultRuntimeShaderService(RendererSession renderer)
{
    private readonly Dictionary<(ShaderProfile Profile,bool Shadows,bool Skin),RuntimeShaderPackage> _packages=[];
    public RuntimeShaderPreparation Prepare(ShaderProfile profile,bool shadows,bool skin,Func<bool> allowed) {
        ArgumentNullException.ThrowIfNull(allowed);
        RuntimeShaderPackage package;
        renderer.BeginToneOperation();
        try {
            renderer.VerifyShaderPreparation();
            if(!allowed())throw new InvalidOperationException("Trusted startup shader preparation required.");
            renderer.VerifyShaderPreparation();
            if(profile is not (ShaderProfile.Flat2D or ShaderProfile.Scene3D)||profile==ShaderProfile.Flat2D&&(shadows||skin))throw new ArgumentException("Default shader profile/features.");
            if(!_packages.TryGetValue((profile,shadows,skin),out package!)) {
                using var compiler=new ShaderCompilerService(renderer,allowed);var inputs=new List<RuntimeShaderInput>();
                if(profile==ShaderProfile.Flat2D){
                    var selected=DefaultUiShaders.Select(DefaultUiShaders.CopyCatalog(renderer));
                    inputs.Add(new(RuntimeShaderRole.UiVertex,compiler.Prepare(selected.Vertex)));inputs.Add(new(RuntimeShaderRole.UiPixel,compiler.Prepare(selected.Pixel)));
                }else{
                    var selected=DefaultSceneShaders.Select(DefaultSceneShaders.CopyCatalog(renderer,shadows),shadows);
                    ShaderDescriptor[] shaders=shadows?[selected.GeometryVertex,selected.GeometryPixel,selected.ToneVertex,selected.TonePixel,selected.ShadowVertex!,selected.ShadowPixel!]:
                        [selected.GeometryVertex,selected.GeometryPixel,selected.ToneVertex,selected.TonePixel];
                    for(int i=0;i<shaders.Length;i++)inputs.Add(new((RuntimeShaderRole)(i+2),compiler.Prepare(shaders[i])));
                    if(skin)inputs.Add(new(RuntimeShaderRole.SkinCompute,compiler.Prepare(DefaultSkinShader.Select(DefaultSkinShader.CopyCatalog(renderer)))));
                }
                package=RuntimeShaderPackage.Cook(profile,shadows,skin,inputs);_packages.Add((profile,shadows,skin),package);
            }
        }finally{renderer.EndToneOperation();}
        return RuntimeShaderPreparation.Prepare(renderer,package,allowed);
    }
}
