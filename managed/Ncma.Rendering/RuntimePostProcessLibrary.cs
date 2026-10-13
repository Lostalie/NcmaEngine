using Ncma.Assets;
using Ncma.Assets.Runtime;
using System.Security.Cryptography;
using System.Text.Json;
namespace Ncma.Rendering;

// Trusted startup IO only. CPU file preflight is separate from actual native whole-library admission.
public sealed class RuntimePostProcessLibrary : IDisposable
{
    public const int MaxPayloadBytes=32*1024*1024;
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private readonly List<RuntimeReadPin> _pins=[];
    private readonly Dictionary<Guid,PostProcessPackage> _programs=[];
    private readonly Dictionary<(Guid,ShaderProfile,bool,bool),RuntimeShaderPackage> _tones=[];
    private PostProcessLibrarySelection _selection=null!;
    private bool _disposed,_preparing;
    public string ContentHash{get;private set;}="";
    private RuntimePostProcessLibrary(){}
    internal void Verify(){if(Environment.CurrentManagedThreadId!=_owner)throw new InvalidOperationException("Post library owner thread.");ObjectDisposedException.ThrowIf(_disposed,this);}
    public PostProcessLibrarySelection CopySelection(){Verify();return _selection.CopyValidated();}
    public PostProcessPackage Program(Guid module){Verify();return _programs.TryGetValue(module,out var p)?p:throw new ArgumentException("Unknown post module; no fallback.");}
    public RuntimeShaderPackage Tone(Guid module,ShaderProfile profile,bool shadows,bool skin){Verify();return _tones.TryGetValue((module,profile,shadows,skin),out var p)?p:throw new ArgumentException("Missing exact tone variant; no fallback.");}
    public static RuntimePostProcessLibrary Load(string root,string expectedIndexHash,CancellationToken cancellation=default)=>LoadCore(root,expectedIndexHash,false,cancellation);
    public static RuntimePostProcessLibrary LoadDeployment(string root,CancellationToken cancellation=default)=>LoadCore(root,null,true,cancellation);
    private static RuntimePostProcessLibrary LoadCore(string root,string? expectedIndexHash,bool deployed,CancellationToken cancellation)
    {
        var result=new RuntimePostProcessLibrary();try{
            root=RuntimeReadPin.Root(root);var records=new Dictionary<string,(long Size,string Hash)>(StringComparer.Ordinal);var manifestPaths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            byte[] Read(string path,int max,bool manifest=false){cancellation.ThrowIfCancellationRequested();var pin=new RuntimeReadPin(root,path,deploymentManifest:manifest);result._pins.Add(pin);return pin.Read(max,cancellation);}
            void CheckManifest(string path,byte[] bytes){if(!deployed)return;if(!records.TryGetValue(path,out var row)||row.Size!=bytes.Length||row.Hash!=Convert.ToHexString(SHA256.HashData(bytes)))throw new ArgumentException("Post file differs from deployment manifest.");}
            if(deployed){using var doc=JsonDocument.Parse(Read("deployment-manifest.json",4*1024*1024,true));PostProcessLibrarySelection.Unique(doc.RootElement);
                var files=doc.RootElement.GetProperty("files");if(files.GetArrayLength() is <1 or >8192)throw new ArgumentException("Post manifest budget.");
                foreach(var f in files.EnumerateArray()){string path=f.GetProperty("path").GetString()??throw new ArgumentException("Manifest path.");string hash=f.GetProperty("sha256").GetString()??"";long size=f.GetProperty("size").GetInt64();
                    if(!ShaderContractCodec.IsHash(hash.ToUpperInvariant())||size<0||!manifestPaths.Add(path)||!records.TryAdd(path,(size,hash.ToUpperInvariant())))throw new ArgumentException("Post manifest duplicate/hash/size.");}
                if(!records.TryGetValue(PostProcessLibrarySelection.IndexPath,out var index))throw new ArgumentException("Missing post library manifest index.");expectedIndexHash=index.Hash;}
            byte[] bytes=Read(PostProcessLibrarySelection.IndexPath,PostProcessLibrarySelection.MaxBytes);CheckManifest(PostProcessLibrarySelection.IndexPath,bytes);
            result.ContentHash=Convert.ToHexString(SHA256.HashData(bytes));if(expectedIndexHash is null||result.ContentHash!=expectedIndexHash.ToUpperInvariant())throw new ArgumentException("Post library external index hash.");
            result._selection=PostProcessLibrarySelection.Decode(bytes);int total=0;
            byte[] Payload(string path){var b=Read(path,8*1024*1024);total=checked(total+b.Length);if(total>MaxPayloadBytes)throw new ArgumentException("Post library total payload budget.");CheckManifest(path,b);return b;}
            foreach(var f in result._selection.Programs)result._programs.Add(f.ModuleId,PostProcessPackage.Preflight(Payload(f.Path),f.Sha256.ToUpperInvariant()));
            foreach(var f in result._selection.Tones){var p=RuntimeShaderPackage.Preflight(Payload(f.Path),f.Sha256.ToUpperInvariant());var profile=f.Profile=="Scene3D"?ShaderProfile.Scene3D:ShaderProfile.SceneEnvironment;
                if(p.Profile!=profile||p.Shadows!=f.Shadows||p.Skinning!=f.Skinning)throw new ArgumentException("Post tone selected variant mismatch.");result._tones.Add((f.ModuleId,profile,f.Shadows,f.Skinning),p);}
            var skins=result._tones.Values.Where(p=>p.Skinning).ToArray();if(skins.Length>1){var skin=skins[0].CopyBytecode(RuntimeShaderRole.SkinCompute);foreach(var p in skins.Skip(1))if(!skin.AsSpan().SequenceEqual(p.CopyBytecode(RuntimeShaderRole.SkinCompute)))throw new ArgumentException("Post library requires one renderer-wide skin program.");}
            cancellation.ThrowIfCancellationRequested();return result;
        }catch{result.Dispose();throw;}
    }
    public PreparedPostProcessLibrary Prepare(RendererSession renderer,RuntimeShaderFileSet sceneFiles,Func<bool> allowed)
    {
        Verify();ArgumentNullException.ThrowIfNull(renderer);ArgumentNullException.ThrowIfNull(sceneFiles);ArgumentNullException.ThrowIfNull(allowed);
        renderer.VerifyShaderPreparation();sceneFiles.Verify();if(_preparing)throw new InvalidOperationException("Reentrant post library preparation.");_preparing=true;
        try{
            bool Approved(){Verify();sceneFiles.Verify();renderer.VerifyShaderPreparation();bool ok=allowed();Verify();sceneFiles.Verify();renderer.VerifyShaderPreparation();return ok;}
            bool Gate(){renderer.BeginToneOperation();try{return Approved();}finally{renderer.EndToneOperation();}}
            if(!Gate())throw new InvalidOperationException("Trusted off-simulation post library approval required.");
            // Tone packages may replace ONLY Tone, not silently change scene geometry, shadow or shared skin.
            foreach(var p in _tones.Values){var original=sceneFiles.Get(p.Profile,p.Shadows,p.Skinning);
                foreach(var row in Enumerable.Range(0,(p.Count+7)/8).SelectMany(p.CopyPage).Where(x=>x.Role is not (RuntimeShaderRole.ToneVertex or RuntimeShaderRole.TonePixel)))
                    if(!p.CopyBytecode(row.Role).AsSpan().SequenceEqual(original.CopyBytecode(row.Role)))throw new ArgumentException("Tone library changed a non-Tone stage.");}
            var programs=new Dictionary<Guid,PreparedPostProcessPrograms>();var scenes=new Dictionary<(Guid,bool,bool),RuntimeShaderPreparation>();var environments=new Dictionary<(Guid,bool,bool),EnvironmentShaderPreparation>();
            foreach(var pair in _programs)programs.Add(pair.Key,PreparedPostProcessPrograms.Prepare(renderer,pair.Value,Approved));
            foreach(var pair in _tones){var k=pair.Key;if(k.Item2==ShaderProfile.SceneEnvironment)environments.Add((k.Item1,k.Item3,k.Item4),EnvironmentShaderPreparation.Prepare(renderer,pair.Value,Approved));else scenes.Add((k.Item1,k.Item3,k.Item4),RuntimeShaderPreparation.Prepare(renderer,pair.Value,Approved));}
            if(!Gate())throw new InvalidOperationException("Post library approval revoked before publication.");
            return new(renderer,Approved,programs,scenes,environments);
        }finally{_preparing=false;}
    }
    public void Dispose(){if(Environment.CurrentManagedThreadId!=_owner)throw new InvalidOperationException("Post library owner thread.");if(_disposed)return;if(_preparing)throw new InvalidOperationException("Cannot close during post library admission.");_disposed=true;for(int i=_pins.Count-1;i>=0;i--)_pins[i].Dispose();_pins.Clear();_programs.Clear();_tones.Clear();}
}
public sealed class PreparedPostProcessLibrary
{
    private readonly RendererSession _owner;private readonly Func<bool> _allowed;
    private readonly Dictionary<Guid,PreparedPostProcessPrograms> _programs;
    private readonly Dictionary<(Guid,bool,bool),RuntimeShaderPreparation> _scenes;
    private readonly Dictionary<(Guid,bool,bool),EnvironmentShaderPreparation> _environments;
    internal PreparedPostProcessLibrary(RendererSession owner,Func<bool> allowed,Dictionary<Guid,PreparedPostProcessPrograms> programs,Dictionary<(Guid,bool,bool),RuntimeShaderPreparation> scenes,Dictionary<(Guid,bool,bool),EnvironmentShaderPreparation> environments)
    {_owner=owner;_allowed=allowed;_programs=programs;_scenes=scenes;_environments=environments;}
    public void VerifyFor(RendererSession renderer){if(!ReferenceEquals(renderer,_owner))throw new ArgumentException("Foreign post library renderer.");renderer.BeginToneOperation();try{if(!_allowed())throw new InvalidOperationException("Post library approval revoked.");}finally{renderer.EndToneOperation();}}
    public PreparedPostProcessPrograms Program(RendererSession renderer,Guid module){VerifyFor(renderer);return _programs.TryGetValue(module,out var p)?p:throw new ArgumentException("Unknown admitted post module.");}
    public RuntimeShaderPreparation SceneTone(RendererSession renderer,Guid module,bool shadows,bool skin){VerifyFor(renderer);return _scenes.TryGetValue((module,shadows,skin),out var p)?p:throw new ArgumentException("Missing admitted scene tone.");}
    public EnvironmentShaderPreparation EnvironmentTone(RendererSession renderer,Guid module,bool shadows,bool skin){VerifyFor(renderer);return _environments.TryGetValue((module,shadows,skin),out var p)?p:throw new ArgumentException("Missing admitted environment tone.");}
}
