using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ncma.Assets;

// Copied catalog metadata, not a source decoder, signing certificate or native resource.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EnvironmentAssetDescriptor(
    [property: JsonRequired] int Version, [property: JsonRequired] Guid ProjectId,
    [property: JsonRequired] Guid AssetId, [property: JsonRequired] ulong Generation,
    [property: JsonRequired] string ContentHash, [property: JsonRequired] string Path)
{
    public const int MaxBytes = 8192;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 4 };
    public EnvironmentAssetDescriptor Validate()
    {
        if (Version != 1 || ProjectId == Guid.Empty || AssetId == Guid.Empty || Generation == 0)
            throw new ArgumentException("Environment descriptor identity/version.");
        AssetRecordCodec.ValidateHash(ContentHash);
        AssetPaths.Validate(Path, "out/assets");
        if (Path != $"out/assets/{ProjectId:N}/{AssetId:N}/{Generation}-{ContentHash}.nce")
            throw new ArgumentException("Environment descriptor canonical generation path.");
        return this;
    }
    public static EnvironmentAssetDescriptor FromPackage(Guid project, EnvironmentPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return new EnvironmentAssetDescriptor(1, project, package.AssetId, package.Generation, package.ContentHash,
            $"out/assets/{project:N}/{package.AssetId:N}/{package.Generation}-{package.ContentHash}.nce").Validate();
    }
    public byte[] Encode()
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(Validate(), Json);
        if (bytes.Length > MaxBytes) throw new ArgumentException("Environment descriptor byte budget.");
        return bytes;
    }
    public static EnvironmentAssetDescriptor Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 2 or > MaxBytes) throw new ArgumentException("Environment descriptor byte budget.");
        using var doc = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 4 });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Environment descriptor object required.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in doc.RootElement.EnumerateObject())
            if (!names.Add(p.Name)) throw new ArgumentException("Duplicate environment descriptor field.");
        return (JsonSerializer.Deserialize<EnvironmentAssetDescriptor>(bytes, Json) ?? throw new ArgumentException("Empty environment descriptor.")).Validate();
    }
}
