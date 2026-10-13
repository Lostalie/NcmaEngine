using System.Text.Json;
using System.Text.Json.Serialization;
namespace Ncma.Assets;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PostProgramFile([property:JsonRequired] Guid ModuleId,[property:JsonRequired] string Path,[property:JsonRequired] string Sha256);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PostToneFile([property:JsonRequired] Guid ModuleId,[property:JsonRequired] string Path,[property:JsonRequired] string Sha256,
    [property:JsonRequired] string Profile,[property:JsonRequired] bool Shadows,[property:JsonRequired] bool Skinning);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PostProcessLibrarySelection([property:JsonRequired] int SchemaVersion,[property:JsonRequired] PostProgramFile[] Programs,[property:JsonRequired] PostToneFile[] Tones)
{
    public const int MaxBytes=16384;
    public const string IndexPath="assets/postprocess/defaults.json";
    public static readonly Guid BloomModule=Guid.Parse("7a14ee03-00c1-44b2-8844-000000000001");
    public static readonly Guid AcesModule=Guid.Parse("7a14ee03-00c1-44b2-8844-000000000010");
    public static readonly Guid ReinhardModule=Guid.Parse("7a14ee03-00c1-44b2-8844-000000000011");
    public static readonly Guid LinearModule=Guid.Parse("7a14ee03-00c1-44b2-8844-000000000012");
    private static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true};
    public PostProcessLibrarySelection CopyValidated()
    {
        if(SchemaVersion!=1||Programs is null||Tones is null||Programs.Length>8||Tones.Length>32||Programs.Length+Tones.Length==0)throw new ArgumentException("Post library schema/budget.");
        var programs=(PostProgramFile[])Programs.Clone();var tones=(PostToneFile[])Tones.Clone();var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var ids=new HashSet<Guid>();var keys=new HashSet<(Guid,string,bool,bool)>();
        void File(Guid id,string path,string hash,string extension){if(id==Guid.Empty||hash is null||hash.Length!=64||hash.Any(c=>!char.IsAsciiHexDigit(c)))throw new ArgumentException("Post library identity/hash.");AssetPaths.Validate(path);if(!path.EndsWith(extension,StringComparison.Ordinal)||!paths.Add(path)||path==IndexPath)throw new ArgumentException("Post library path.");}
        foreach(var p in programs){if(p is null)throw new ArgumentException("Null post program.");File(p.ModuleId,p.Path,p.Sha256,".nph");if(!ids.Add(p.ModuleId))throw new ArgumentException("Duplicate post module.");}
        foreach(var t in tones){if(t is null)throw new ArgumentException("Null tone.");File(t.ModuleId,t.Path,t.Sha256,".ncshader");if(t.Profile is not ("Scene3D" or "SceneEnvironment")||ids.Contains(t.ModuleId)||!keys.Add((t.ModuleId,t.Profile,t.Shadows,t.Skinning)))throw new ArgumentException("Tone module/profile closure.");}
        foreach(var t in tones)if(!keys.Contains((t.ModuleId,t.Profile,!t.Shadows,t.Skinning)))throw new ArgumentException("Both tone shadow variants required.");
        return this with{Programs=programs,Tones=tones};
    }
    public byte[] Encode(){var bytes=JsonSerializer.SerializeToUtf8Bytes(CopyValidated(),Json);if(bytes.Length>MaxBytes)throw new ArgumentException("Post index byte budget.");return bytes;}
    public static PostProcessLibrarySelection Decode(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length is <1 or >MaxBytes)throw new ArgumentException("Post index bytes.");byte[] owned=bytes.ToArray();using var doc=JsonDocument.Parse(owned);
        Unique(doc.RootElement);return (JsonSerializer.Deserialize<PostProcessLibrarySelection>(owned,Json)??throw new ArgumentException("Empty post index.")).CopyValidated();
    }
    internal static void Unique(JsonElement value){if(value.ValueKind==JsonValueKind.Object){var names=new HashSet<string>(StringComparer.Ordinal);foreach(var p in value.EnumerateObject()){if(!names.Add(p.Name))throw new ArgumentException("Duplicate post index/manifest field.");Unique(p.Value);}}else if(value.ValueKind==JsonValueKind.Array)foreach(var v in value.EnumerateArray())Unique(v);}
}
