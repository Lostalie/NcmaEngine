using System.Text.Json;
using System.Text.Json.Serialization;
namespace Ncma.Assets;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ShadowProgramFile([property:JsonRequired] string Profile,[property:JsonRequired] bool Skinning,
    [property:JsonRequired] string Path,[property:JsonRequired] string Sha256);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ShadowLibrarySelection([property:JsonRequired] int SchemaVersion,[property:JsonRequired] ShadowProgramFile[] Programs)
{
    public const string IndexPath="assets/shadows/defaults.json";
    public const int MaxBytes=8192;
    private static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true};
    public ShadowLibrarySelection CopyValidated()
    {
        if(SchemaVersion!=1||Programs is null||Programs.Length!=4)throw new ArgumentException("Exact four-program shadow library required.");
        var rows=(ShadowProgramFile[])Programs.Clone();var keys=new HashSet<(string,bool)>();var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var p in rows){
            if(p is null||p.Profile is not ("Scene3D" or "SceneEnvironment")||!keys.Add((p.Profile,p.Skinning))||
                p.Sha256 is null||p.Sha256.Length!=64||p.Sha256.Any(c=>!char.IsAsciiHexDigit(c)))throw new ArgumentException("Shadow library profile/duplicate/hash.");
            AssetPaths.Validate(p.Path,"assets/shadows");
            if(!p.Path.EndsWith(".ncshader",StringComparison.Ordinal)||!paths.Add(p.Path))throw new ArgumentException("Shadow library path/alias.");
        }
        return this with{Programs=rows.OrderBy(p=>p.Profile,StringComparer.Ordinal).ThenBy(p=>p.Skinning).ToArray()};
    }
    public byte[] Encode(){byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(CopyValidated(),Json);if(bytes.Length>MaxBytes)throw new ArgumentException("Shadow index budget.");return bytes;}
    public static ShadowLibrarySelection Decode(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length is <1 or >MaxBytes)throw new ArgumentException("Shadow index budget.");
        byte[] owned=bytes.ToArray();using var doc=JsonDocument.Parse(owned,new(){MaxDepth=8});PostProcessLibrarySelection.Unique(doc.RootElement);
        return (JsonSerializer.Deserialize<ShadowLibrarySelection>(owned,Json)??throw new ArgumentException("Shadow index required.")).CopyValidated();
    }
}
