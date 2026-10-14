using Ncma.Assets;
using Ncma.Assets.Runtime;
using System.Security.Cryptography;
using System.Text.Json;
namespace Ncma.Rendering;

// CPU-owned, pinned immutable high-filter closure. No discovery, fallback, compilation or World access.
public sealed class RuntimeShadowLibrary : IDisposable
{
    public const int MaxPayloadBytes=32*1024*1024;
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private readonly List<RuntimeReadPin> _pins=[];
    private readonly Dictionary<(ShaderProfile,bool),RuntimeShaderPackage> _packages=[];
    private bool _disposed,_preparing,_resolving,_approving;
    private ShadowLibrarySelection _selection=null!;
    public string ContentHash {get;private set;}="";
    private RuntimeShadowLibrary(){}
    internal void Verify(){if(_owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Shadow library owner thread.");ObjectDisposedException.ThrowIf(_disposed,this);}
    internal void BeginResolution(){Verify();if(_resolving||_approving)throw new InvalidOperationException("Shadow resolution nonreentry.");_resolving=true;}
    internal void EndResolution()=>_resolving=false;
    public ShadowLibrarySelection CopySelection(){Verify();return _selection.CopyValidated();}
    public RuntimeShaderPackage Get(ShaderProfile profile,bool skin){Verify();return _packages.TryGetValue((profile,skin),out var p)?p:throw new ArgumentException("Exact high shadow variant required.");}
    public static RuntimeShadowLibrary Load(string root,string expectedIndexHash,CancellationToken cancellation=default)=>LoadCore(root,expectedIndexHash,false,cancellation);
    public static RuntimeShadowLibrary LoadDeployment(string root,CancellationToken cancellation=default)=>LoadCore(root,null,true,cancellation);
    public static RuntimeShadowLibrary ForHost(string deploymentRoot,string? projectRoot,ShadowLibraryReference? selection)=>selection is null?
        LoadDeployment(deploymentRoot):Load(projectRoot??throw new ArgumentException("Explicit shadow library requires project root."),selection.Validate().IndexHash);
    private static RuntimeShadowLibrary LoadCore(string root,string? hash,bool deployed,CancellationToken cancellation)
    {
        var result=new RuntimeShadowLibrary();try{
            root=RuntimeReadPin.Root(root);var records=new Dictionary<string,(long Size,string Hash)>(StringComparer.Ordinal);var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            byte[] Read(string path,int max,bool manifest=false){cancellation.ThrowIfCancellationRequested();var pin=new RuntimeReadPin(root,path,deploymentManifest:manifest);result._pins.Add(pin);return pin.Read(max,cancellation);}
            void Manifest(string path,byte[] bytes){if(deployed&&(!records.TryGetValue(path,out var row)||row.Size!=bytes.Length||row.Hash!=Convert.ToHexString(SHA256.HashData(bytes))))throw new ArgumentException("Shadow file differs from manifest.");}
            if(deployed){using var doc=JsonDocument.Parse(Read("deployment-manifest.json",4*1024*1024,true));
                var files=doc.RootElement.GetProperty("files");if(files.GetArrayLength() is <1 or >8192)throw new ArgumentException("Shadow manifest budget.");
                Unique(doc.RootElement);
                foreach(var file in files.EnumerateArray()){
                    string path=file.GetProperty("path").GetString()??throw new ArgumentException("Manifest path.");
                    string sha=(file.GetProperty("sha256").GetString()??"").ToUpperInvariant();long size=file.GetProperty("size").GetInt64();
                    if(!ShaderContractCodec.IsHash(sha)||size<0||!paths.Add(path)||!records.TryAdd(path,(size,sha)))throw new ArgumentException("Manifest duplicate/hash/size.");
                }
                if(!records.TryGetValue(ShadowLibrarySelection.IndexPath,out var row))throw new ArgumentException("Missing shadow library manifest.");hash=row.Hash;
            }
            byte[] index=Read(ShadowLibrarySelection.IndexPath,ShadowLibrarySelection.MaxBytes);Manifest(ShadowLibrarySelection.IndexPath,index);
            result.ContentHash=Convert.ToHexString(SHA256.HashData(index));
            if(hash is null||!ShaderContractCodec.IsHash(hash.ToUpperInvariant())||result.ContentHash!=hash.ToUpperInvariant())throw new ArgumentException("External shadow index hash.");
            result._selection=ShadowLibrarySelection.Decode(index);int total=0;
            foreach(var file in result._selection.Programs){byte[] bytes=Read(file.Path,RuntimeShaderPackage.MaxBytes);total=checked(total+bytes.Length);if(total>MaxPayloadBytes)throw new ArgumentException("Shadow library payload budget.");Manifest(file.Path,bytes);
                var package=RuntimeShaderPackage.Preflight(bytes,file.Sha256.ToUpperInvariant());var profile=file.Profile=="Scene3D"?ShaderProfile.Scene3D:ShaderProfile.SceneEnvironment;
                if(package.Profile!=profile||!package.Shadows||package.Skinning!=file.Skinning)throw new ArgumentException("Shadow program variant mismatch.");
                var role=profile==ShaderProfile.Scene3D?RuntimeShaderRole.GeometryPixel:RuntimeShaderRole.EnvironmentGeometryPixel;
                var pixel=package.CopyPage(0).Single(p=>p.Role==role);
                if(pixel.AuthorContentHash!=SceneShadowShaders.CreateHighPixel(profile).ContentHash)throw new ArgumentException("Official high-filter descriptor required.");
                result._packages.Add((profile,file.Skinning),package);
            }
            var skins=result._packages.Values.Where(p=>p.Skinning).ToArray();
            if(!skins[0].CopyBytecode(RuntimeShaderRole.SkinCompute).AsSpan().SequenceEqual(skins[1].CopyBytecode(RuntimeShaderRole.SkinCompute)))throw new ArgumentException("One shared skin kernel required.");
            cancellation.ThrowIfCancellationRequested();return result;
        }catch{result.Dispose();throw;}
    }
    private static void Unique(JsonElement e){if(e.ValueKind==JsonValueKind.Object){var keys=new HashSet<string>(StringComparer.Ordinal);foreach(var p in e.EnumerateObject()){if(!keys.Add(p.Name))throw new ArgumentException("Duplicate manifest field.");Unique(p.Value);}}else if(e.ValueKind==JsonValueKind.Array)foreach(var v in e.EnumerateArray())Unique(v);}
    public PreparedShadowLibrary Prepare(RendererSession renderer,RuntimeShaderFileSet sceneFiles,Func<bool> allowed)
    {
        Verify();ArgumentNullException.ThrowIfNull(renderer);ArgumentNullException.ThrowIfNull(sceneFiles);ArgumentNullException.ThrowIfNull(allowed);
        renderer.VerifyShaderPreparation();sceneFiles.Verify();if(_preparing||_resolving||_approving)throw new InvalidOperationException("Shadow admission nonreentry.");_preparing=true;
        try{
            bool Approved(){Verify();sceneFiles.Verify();renderer.VerifyShaderPreparation();if(_approving)throw new InvalidOperationException("Shadow approval nonreentry.");_approving=true;
                try{bool result=allowed();Verify();sceneFiles.Verify();renderer.VerifyShaderPreparation();return result;}finally{_approving=false;}}
            var prepared=new PreparedShadowLibrary(renderer,this,Approved);
            foreach(var key in _packages.Keys)prepared.Resolve(sceneFiles.Get(key.Item1,true,key.Item2));
            return prepared;
        }finally{_preparing=false;}
    }
    public void Dispose(){if(_owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Shadow owner thread.");if(_disposed)return;if(_preparing||_resolving||_approving)throw new InvalidOperationException("Cannot close during shadow admission.");_disposed=true;for(int i=_pins.Count-1;i>=0;i--)_pins[i].Dispose();_pins.Clear();_packages.Clear();}
}

public sealed record PreparedShadowPrograms(RegisteredSceneShaders? Scene,RegisteredEnvironmentSceneShaders? Environment);
public sealed class PreparedShadowLibrary
{
    private readonly RendererSession _renderer;
    private readonly RuntimeShadowLibrary _library;
    private readonly Func<bool> _allowed;
    private readonly Dictionary<string,PreparedShadowPrograms> _cache=new(StringComparer.Ordinal);
    private bool _busy;
    internal PreparedShadowLibrary(RendererSession renderer,RuntimeShadowLibrary library,Func<bool> allowed){_renderer=renderer;_library=library;_allowed=allowed;}
    public PreparedShadowPrograms Resolve(RuntimeShaderPackage basis)
    {
        ArgumentNullException.ThrowIfNull(basis);_library.Verify();_renderer.VerifyShaderPreparation();if(_busy)throw new InvalidOperationException("Shadow composition nonreentry.");_library.BeginResolution();_busy=true;
        try{
            void Approve(){_renderer.BeginToneOperation();try{if(!_allowed())throw new InvalidOperationException("Shadow approval revoked.");}finally{_renderer.EndToneOperation();}}
            Approve();if(_cache.TryGetValue(basis.ContentHash,out var previous))return previous;
            if(_cache.Count>=16)throw new InvalidOperationException("Shadow composition cache budget reached.");
            var package=RuntimeShaderPackage.ComposeGeometryPixel(basis,_library.Get(basis.Profile,basis.Skinning));
            var result=package.Profile==ShaderProfile.SceneEnvironment?
                new PreparedShadowPrograms(null,EnvironmentShaderPreparation.Prepare(_renderer,package,_allowed).Scene):
                new PreparedShadowPrograms(RuntimeShaderPreparation.Prepare(_renderer,package,_allowed).Scene,null);
            Approve();_cache.Add(basis.ContentHash,result);return result;
        }finally{_busy=false;_library.EndResolution();}
    }
}
