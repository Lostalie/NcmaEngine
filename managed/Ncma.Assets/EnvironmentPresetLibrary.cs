using System.Text.Json;
using System.Text.Json.Serialization;
using Ncma.Assets.Runtime;

namespace Ncma.Assets;

// Product resource, not a project asset or a claim of real HDR source provenance.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EnvironmentPresetIndex(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Preset,
    [property: JsonRequired] bool Synthetic,
    [property: JsonRequired] Guid AssetId,
    [property: JsonRequired] ulong Generation,
    [property: JsonRequired] string Path,
    [property: JsonRequired] int ByteCount,
    [property: JsonRequired] string Sha256)
{
    public const string IndexPath = "assets/environments/defaults.json";
    public const string PackagePath = "assets/environments/neutral-v1.nce";
    public const string NeutralName = "neutral-synthetic-v1";
    public const int MaxBytes = 8192;
    public static Guid NeutralAssetId => new("814e3094-f91b-4cda-bd9d-a7d25839d4f2");
    public static Guid NeutralSourceId => new("ec0e2cbd-0ac8-464a-923c-ac1f11c84865");
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 4 };

    public EnvironmentPresetIndex Validate()
    {
        if (Version != 1 || Preset != NeutralName || !Synthetic || AssetId != NeutralAssetId ||
            Generation != 1 || Path != PackagePath || ByteCount < EnvironmentPackage.HeaderBytes ||
            ByteCount > EnvironmentPackage.MaxBytes || !EnvironmentPackage.CanonicalHash(Sha256))
            throw new ArgumentException("Closed synthetic environment preset index.");
        return this;
    }
    public byte[] Encode() => JsonSerializer.SerializeToUtf8Bytes(Validate(), Json);
    public static EnvironmentPresetIndex Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 2 or > MaxBytes) throw new ArgumentException("Environment preset index budget.");
        using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 4 });
        RequireUnique(document.RootElement);
        return (JsonSerializer.Deserialize<EnvironmentPresetIndex>(bytes, Json) ??
            throw new ArgumentException("Empty environment preset index.")).Validate();
    }
    internal static void RequireUnique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object) {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in value.EnumerateObject()) {
                if (!names.Add(p.Name)) throw new ArgumentException("Duplicate environment deployment field.");
                RequireUnique(p.Value);
            }
        } else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RequireUnique(item);
    }
    public static EnvironmentPresetIndex FromPackage(EnvironmentPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        RequireNeutral(package);
        return new(1, NeutralName, true, package.AssetId, package.Generation, PackagePath, package.ByteCount, package.ContentHash);
    }
    internal static void RequireNeutral(EnvironmentPackage package)
    {
        var source = HdrEnvironmentSource.Neutral(NeutralSourceId);
        if (package.AssetId != NeutralAssetId || package.Generation != 1 || package.SourceId != source.AssetId ||
            package.SourceHash != source.ContentHash || package.SourceWidth != source.Width ||
            package.SourceHeight != source.Height || package.Settings != EnvironmentCookSettings.Default)
            throw new ArgumentException("Synthetic neutral recipe identity/settings mismatch.");
        // Integrity and recipe metadata are not proof of Cook provenance or safe executable code.
    }
}

// Explicit startup/tooling lease; no renderer, project mutation, file publication or GPU authority.
public sealed class EnvironmentPresetLibrary : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly List<RuntimeReadPin> _pins = [];
    private EnvironmentPresetIndex _index = null!;
    private EnvironmentPackage _package = null!;
    private bool _disposed;
    private EnvironmentPresetLibrary() { }
    private void Verify()
    {
        if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Environment preset lease owner thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
    public EnvironmentPresetIndex Index { get { Verify(); return _index; } }
    public EnvironmentPackage Package { get { Verify(); return _package; } }

    // Trusted caller supplies the independent build index hash; deployed callers use LoadDeployment.
    public static EnvironmentPresetLibrary Load(string root, string expectedIndexHash, CancellationToken cancellation = default)
        => LoadCore(root, expectedIndexHash, false, cancellation);
    public static EnvironmentPresetLibrary LoadDeployment(string root, CancellationToken cancellation = default)
        => LoadCore(root, null, true, cancellation);
    private static EnvironmentPresetLibrary LoadCore(string root, string? expectedHash, bool deployed, CancellationToken cancellation)
    {
        var result = new EnvironmentPresetLibrary();
        try {
            root = RuntimeReadPin.Root(root);
            JsonDocument? manifest = null;
            try {
                long expectedSize = 0;
                if (deployed) {
                    var pin = new RuntimeReadPin(root, "deployment-manifest.json", deploymentManifest: true);
                    result._pins.Add(pin);
                    manifest = JsonDocument.Parse(pin.Read(4 * 1024 * 1024, cancellation), new JsonDocumentOptions { MaxDepth = 32 });
                    EnvironmentPresetIndex.RequireUnique(manifest.RootElement);
                    if (manifest.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
                        throw new ArgumentException("Environment deployment manifest version.");
                    var entry = ManifestFile(manifest.RootElement, EnvironmentPresetIndex.IndexPath);
                    expectedHash = entry.Hash; expectedSize = entry.Size;
                }
                if (!EnvironmentPackage.CanonicalHash(expectedHash)) throw new ArgumentException("Independent preset index hash required.");
                var indexPin = new RuntimeReadPin(root, EnvironmentPresetIndex.IndexPath);
                result._pins.Add(indexPin);
                byte[] bytes = indexPin.Read(EnvironmentPresetIndex.MaxBytes, cancellation);
                if (EnvironmentPackage.Hash(bytes) != expectedHash || deployed && bytes.Length != expectedSize)
                    throw new ArgumentException("Preset index differs from independent hash/size.");
                var index = EnvironmentPresetIndex.Decode(bytes);
                if (manifest is not null) {
                    var entry = ManifestFile(manifest.RootElement, index.Path);
                    if (entry.Hash != index.Sha256 || entry.Size != index.ByteCount)
                        throw new ArgumentException("Preset NCE differs from deployment manifest.");
                }
                var packagePin = new RuntimeReadPin(root, index.Path);
                result._pins.Add(packagePin);
                byte[] packageBytes = packagePin.Read(EnvironmentPackage.MaxBytes, cancellation);
                if (packageBytes.Length != index.ByteCount) throw new ArgumentException("Preset NCE size mismatch.");
                var package = EnvironmentPackage.Decode(packageBytes, index.Sha256);
                EnvironmentPresetIndex.RequireNeutral(package);
                cancellation.ThrowIfCancellationRequested();
                result._index = index; result._package = package;
                return result;
            } finally { manifest?.Dispose(); }
        } catch { result.Dispose(); throw; }
    }
    private static (string Hash, long Size) ManifestFile(JsonElement manifest, string wanted)
    {
        var files = manifest.GetProperty("files");
        if (files.ValueKind != JsonValueKind.Array || files.GetArrayLength() is < 2 or > 8192)
            throw new ArgumentException("Environment deployment file budget.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        (string Hash, long Size)? found = null;
        foreach (var file in files.EnumerateArray()) {
            string path = file.GetProperty("path").GetString() ?? throw new ArgumentException("Manifest file path.");
            if (!seen.Add(path)) throw new ArgumentException("Duplicate manifest file.");
            if (path != wanted) continue;
            string hash = file.GetProperty("sha256").GetString() ?? "";
            long size = file.GetProperty("size").GetInt64();
            if (!EnvironmentPackage.CanonicalHash(hash) || size <= 0) throw new ArgumentException("Manifest file hash/size.");
            found = (hash, size);
        }
        return found ?? throw new ArgumentException("Environment preset file absent from manifest.");
    }
    public void Dispose()
    {
        if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Environment preset dispose owner thread.");
        if (_disposed) return;
        for (int i = _pins.Count - 1; i >= 0; --i) _pins[i].Dispose();
        _pins.Clear(); _disposed = true;
    }
}
