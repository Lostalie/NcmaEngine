using Ncma.Assets;
using Ncma.Assets.Runtime;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ncma.Rendering;

// Startup-only trusted IO boundary. All files and ancestors stay pinned until host shutdown;
// the renderer receives only immutable bytecode. No project write or Agent execution endpoint.
public sealed class RuntimeShaderFileSet : IDisposable
{
    public const string DefaultSelectionPath = "assets/shaders/defaults.json";
    private readonly List<RuntimeReadPin> _pins = [];
    private readonly Dictionary<(ShaderProfile,bool,bool),RuntimeShaderPackage> _packages = [];
    private bool _disposed;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    internal void Verify() {
        if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Shader file leases are owner-thread-only.");
        ObjectDisposedException.ThrowIf(_disposed,this);
    }
    private RuntimeShaderFileSet() { }
    public int Count { get { Verify(); return _packages.Count; } }
    public static RuntimeShaderFileSet Load(string root, ShaderPackageSelection? selection = null, CancellationToken cancellation = default)
        => LoadCore(root,selection,cancellation,false);
    private static RuntimeShaderFileSet LoadCore(string root,ShaderPackageSelection? selection,CancellationToken cancellation,bool deployed)
    {
        var result = new RuntimeShaderFileSet();
        try {
            root = RuntimeReadPin.Root(root);
            if (selection is null) {
                string? expectedHash=null; long expectedSize=0;
                if(deployed) {
                    var manifest=new RuntimeReadPin(root,"deployment-manifest.json",deploymentManifest:true);result._pins.Add(manifest);
                    using var document=JsonDocument.Parse(manifest.Read(4*1024*1024,cancellation));
                    void Unique(JsonElement e) {
                        if(e.ValueKind==JsonValueKind.Object){var names=new HashSet<string>(StringComparer.Ordinal);foreach(var p in e.EnumerateObject()){if(!names.Add(p.Name))throw new ArgumentException("Duplicate manifest field.");Unique(p.Value);}}
                        else if(e.ValueKind==JsonValueKind.Array)foreach(var item in e.EnumerateArray())Unique(item);
                    }
                    Unique(document.RootElement);
                    var records=document.RootElement.GetProperty("files");
                    if(records.GetArrayLength() is < 1 or > 8192)throw new ArgumentException("Shader manifest budget.");
                    var matches=records.EnumerateArray().Where(p=>p.GetProperty("path").GetString()==DefaultSelectionPath).ToArray();
                    if(matches.Length!=1)throw new ArgumentException("Shader selection must be present exactly once in deployment manifest.");
                    expectedHash=matches[0].GetProperty("sha256").GetString();expectedSize=matches[0].GetProperty("size").GetInt64();
                    if(expectedHash is null || expectedHash.Length!=64 || expectedHash.Any(c=>!char.IsAsciiHexDigit(c)))throw new ArgumentException("Shader index manifest hash.");
                }
                var index = new RuntimeReadPin(root,DefaultSelectionPath); result._pins.Add(index);
                byte[] indexBytes=index.Read(ShaderPackageSelection.MaxBytes,cancellation);
                if(deployed && (indexBytes.Length!=expectedSize || !Convert.ToHexString(SHA256.HashData(indexBytes)).Equals(expectedHash,StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("Pinned shader index differs from deployment manifest.");
                selection = ShaderPackageSelection.Decode(indexBytes);
            } else selection = selection.CopyValidated();
            foreach (var entry in selection.Packages) {
                cancellation.ThrowIfCancellationRequested();
                var pin = new RuntimeReadPin(root,entry.Path); result._pins.Add(pin);
                var package = RuntimeShaderPackage.Preflight(pin.Read(8*1024*1024,cancellation),entry.Sha256);
                var profile = entry.Profile switch { "Flat2D" => ShaderProfile.Flat2D, "Scene3D" => ShaderProfile.Scene3D,
                    "SceneEnvironment" => ShaderProfile.SceneEnvironment, _ => throw new ArgumentException("Unknown selected shader profile.") };
                if (package.Profile != profile || package.Shadows != entry.Shadows || package.Skinning != entry.Skinning)
                    throw new ArgumentException("Shader file differs from selected profile/features.");
                result._packages.Add((profile,entry.Shadows,entry.Skinning),package);
            }
            foreach (var package in result._packages.Values.Where(p => p.Skinning && !p.Shadows))
                if (!package.CopyBytecode(RuntimeShaderRole.SkinCompute).SequenceEqual(result._packages[(package.Profile,true,true)].CopyBytecode(RuntimeShaderRole.SkinCompute)))
                    throw new ArgumentException("Scene variants require identical shared skin bytecode.");
            // The renderer owns ONE compute kernel for all live skin resources, including Edit
            // and Play using different geometry profiles. Reject conflicting groups before use.
            var skins=result._packages.Values.Where(p=>p.Skinning).ToArray();
            if(skins.Length>1) {
                byte[] shared=skins[0].CopyBytecode(RuntimeShaderRole.SkinCompute);
                foreach(var package in skins.Skip(1))
                    if(!shared.AsSpan().SequenceEqual(package.CopyBytecode(RuntimeShaderRole.SkinCompute)))
                        throw new ArgumentException("Selected profiles require identical renderer-wide skin bytecode.");
            }
            return result;
        } catch { result.Dispose(); throw; }
    }
    // Formal deployed hosts require their checked package selection. Source-tree test hosts may
    // still use explicit default cooking; an explicit project selection NEVER falls back.
    public static RuntimeShaderFileSet? ForHost(string deploymentRoot, string? projectRoot, ShaderPackageSelection? selection)
    {
        if (selection is not null) return Load(projectRoot ?? throw new ArgumentException("Project root required."),selection);
        if (File.Exists(Path.Combine(deploymentRoot,"deployment-manifest.json"))) return LoadCore(deploymentRoot,null,default,true);
        if (File.Exists(Path.Combine(deploymentRoot,DefaultSelectionPath)))
            return Load(deploymentRoot);
        return null;
    }
    public RuntimeShaderPackage Get(ShaderProfile profile,bool shadows,bool skin)
    {
        Verify();
        return _packages.TryGetValue((profile,shadows,skin),out var package) ? package : throw new ArgumentException("Selected shader closure missing; no fallback.");
    }
    public void InstallSelection(RendererSession renderer,Func<bool> allowed)
    {
        Verify(); ArgumentNullException.ThrowIfNull(renderer);
        // Whole selection admission before publishing anything to the default service.
        foreach (var package in _packages.Values) {
            if (package.Profile == ShaderProfile.SceneEnvironment) EnvironmentShaderPreparation.Prepare(renderer,package,allowed);
            else RuntimeShaderPreparation.Prepare(renderer,package,allowed);
        }
        renderer.DefaultRuntimeShaders.UseFiles(this,allowed);
    }
    public void Dispose() {
        if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Shader file leases are owner-thread-only.");
        if (_disposed) return; _disposed = true;
        for (int i=_pins.Count-1;i>=0;--i) _pins[i].Dispose(); _pins.Clear(); _packages.Clear();
    }
}
