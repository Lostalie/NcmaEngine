using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ncma.Assets;

public static class AssetRecordCodec
{
    public const int MaxBytes = 4 * 1024 * 1024;
    public const int MaxSubassets = 4096, MaxDependencies = 4096;
    // .ncmeta v1 has its own frozen kind closure. New independent containers (NCE/NCP)
    // do not implicitly broaden this format or its metadata-only Agent schemas.
    public static bool SupportsKind(AssetKind kind) => Enum.IsDefined(kind) && kind <= AssetKind.AnimationGraph;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 24,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static byte[] Encode(AssetRecord record)
    {
        Validate(record);
        var canonical = record with
        {
            Subassets = record.Subassets.OrderBy(s => s.AssetId).ToArray(),
            Dependencies = record.Dependencies.OrderBy(d => d.AssetId).ToArray()
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(canonical, Json);
        if (bytes.Length > MaxBytes) throw new ArgumentException("Asset descriptor exceeds 4 MiB.");
        return bytes;
    }

    public static AssetRecord Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 2 or > MaxBytes) throw new ArgumentException("Invalid descriptor byte budget.");
        using var doc = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 24 });
        RejectDuplicates(doc.RootElement);
        var record = doc.RootElement.Deserialize<AssetRecord>(Json) ?? throw new ArgumentException("Empty descriptor.");
        Validate(record);
        return record;
    }

    public static AssetRecord Copy(AssetRecord record)
    {
        Validate(record);
        return record with { Subassets = (SubassetRecord[])record.Subassets.Clone(), Dependencies = (AssetDependency[])record.Dependencies.Clone() };
    }

    public static void Validate(AssetRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Version != 1 || record.AssetId == Guid.Empty || !SupportsKind(record.Kind) ||
            record.ImporterVersion < 1 || record.Settings is null || record.Settings.Version != 1 ||
            record.Settings.SampleRate is < 1 or > 120 || record.Subassets is null || record.Dependencies is null ||
            record.Subassets.Length > MaxSubassets || record.Dependencies.Length > MaxDependencies)
            throw new ArgumentException("Invalid asset descriptor identity/version/budget.");
        AssetPaths.Validate(record.SourcePath); ValidateHash(record.SourceHash); Text(record.Importer, 128);
        if (record.SourcePath.EndsWith(".ncmeta", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Metadata cannot be an asset source.");
        var ids = new HashSet<Guid> { record.AssetId };
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sub in record.Subassets)
        {
            if (sub is null || sub.AssetId == Guid.Empty || !ids.Add(sub.AssetId) || !SupportsKind(sub.Kind) || !keys.Add(sub.SourceKey))
                throw new ArgumentException("Invalid/duplicate subasset identity or source key.");
            Text(sub.SourceKey, 512); Text(sub.Name, 256);
        }
        var dependencies = new HashSet<Guid>();
        foreach (var dependency in record.Dependencies)
            if (dependency is null || dependency.AssetId == Guid.Empty || !dependencies.Add(dependency.AssetId) ||
                dependency.AssetId == record.AssetId || !SupportsKind(dependency.ExpectedKind))
                throw new ArgumentException("Invalid/duplicate asset dependency.");
        if (record.Generation is { } generation)
        {
            if (generation.Number == 0) throw new ArgumentException("Invalid generation number.");
            ValidateHash(generation.ContentHash);
            AssetPaths.Validate(generation.RelativePath, "out/assets");
            string[] parts = generation.RelativePath.Split('/');
            if (parts.Length < 4 || !Guid.TryParseExact(parts[2], "N", out var project) || project == Guid.Empty || parts[2] != project.ToString("N"))
                throw new ArgumentException("Derived cache requires a canonical project UUID directory.");
            if (!generation.RelativePath.EndsWith(".nca", StringComparison.Ordinal)) throw new ArgumentException("Expected .nca derived data.");
        }
    }

    public static void ValidateHash(string hash)
    {
        if (hash is null || hash.Length != 64 || hash.Any(c => !(c is >= '0' and <= '9' or >= 'A' and <= 'F')))
            throw new ArgumentException("Expected canonical uppercase SHA-256.");
    }

    private static void Text(string value, int limit)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > limit || value.Any(char.IsControl))
            throw new ArgumentException("Invalid descriptor text.");
    }

    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in value.EnumerateObject())
            {
                if (!names.Add(p.Name)) throw new ArgumentException("Duplicate asset field: " + p.Name);
                RejectDuplicates(p.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }
}
