using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ncma.Assets;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ShaderPackageFile(
    [property: JsonRequired] string Path, [property: JsonRequired] string Sha256,
    [property: JsonRequired] string Profile, [property: JsonRequired] bool Shadows,
    [property: JsonRequired] bool Skinning);

// Trusted project/startup configuration, not an Agent capability or a shader safety certificate.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ShaderPackageSelection(
    [property: JsonRequired] int SchemaVersion, [property: JsonRequired] ShaderPackageFile[] Packages)
{
    public const int MaxBytes = 16384;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public ShaderPackageSelection CopyValidated()
    {
        if (SchemaVersion != 1 || Packages is null || Packages.Length is < 1 or > 5)
            throw new ArgumentException("Shader selection schema/budget.");
        var owned = (ShaderPackageFile[])Packages.Clone();
        var keys = new HashSet<(string, bool, bool)>(); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in owned) {
            if (p is null || p.Profile is not ("Flat2D" or "Scene3D") || p.Profile == "Flat2D" && (p.Shadows || p.Skinning) ||
                p.Sha256 is null || p.Sha256.Length != 64 || p.Sha256.Any(c => !char.IsAsciiHexDigit(c)) ||
                !keys.Add((p.Profile,p.Shadows,p.Skinning))) throw new ArgumentException("Shader selection closure/hash.");
            AssetPaths.Validate(p.Path);
            if (!p.Path.EndsWith(".ncshader",StringComparison.Ordinal) || !paths.Add(p.Path)) throw new ArgumentException("Shader package path.");
        }
        foreach (var p in owned.Where(p => p.Profile == "Scene3D"))
            if (!keys.Contains((p.Profile,!p.Shadows,p.Skinning))) throw new ArgumentException("Both scene shadow variants required.");
        return this with { Packages = owned };
    }
    public static ShaderPackageSelection Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 1 or > MaxBytes) throw new ArgumentException("Shader selection byte budget.");
        using var doc = JsonDocument.Parse(bytes.ToArray());
        void Unique(JsonElement e) {
            if (e.ValueKind == JsonValueKind.Object) { var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in e.EnumerateObject()) { if (!names.Add(p.Name)) throw new ArgumentException("Duplicate shader selection field."); Unique(p.Value); } }
            else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) Unique(item);
        }
        Unique(doc.RootElement);
        return (JsonSerializer.Deserialize<ShaderPackageSelection>(bytes,Json) ?? throw new ArgumentException("Empty shader selection.")).CopyValidated();
    }
    public byte[] Encode() => JsonSerializer.SerializeToUtf8Bytes(CopyValidated(),Json);
}
