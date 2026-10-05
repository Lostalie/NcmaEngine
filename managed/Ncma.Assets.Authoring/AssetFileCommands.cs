using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Assets.Authoring.Storage;

namespace Ncma.Assets.Authoring;

public sealed class AssetFileCommands : IEditCommandParticipant, IDisposable
{
    public const string CapabilityName = "ncma.assets.files.edit";
    public const long MaxSnapshotBytes = 1024L * 1024 * 1024;
    public const int MaxSnapshots = 128;
    private readonly AssetProjectPaths _paths;
    private readonly AssetWriteScope _scope;
    private readonly AssetRevisionClock _clock;
    private readonly AssetDiskTransaction _disk;
    private readonly string _cache;
    private readonly Func<bool> _current;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly ulong _generation;
    public AssetFileCommands(AssetProjectPaths paths, AssetWriteScope scope, Guid projectId, ulong generation,
        AssetRevisionClock clock, Func<bool> current, Action<string>? faultInjection = null)
    {
        if (projectId == Guid.Empty || generation == 0) throw new ArgumentException("Expected project identity/generation.");
        _paths = paths; _scope = scope; _clock = clock; _current = current; _generation = generation;
        _disk = new(paths, faultInjection); _cache = "out/asset-authoring/" + projectId.ToString("N") + "/blobs";
        paths.EnsureCacheDirectory(_cache);
    }
    public static CapabilityDescriptor Descriptor => new(CapabilityName, "Move source and metadata preserving UUID, or copy with new identities; reversible and journaled.",
        MutationRisk.Reversible, JsonSerializer.SerializeToElement(new
        {
            type = "object", additionalProperties = false,
            required = new[] { "operation", "sourceMetadata", "destinationMetadata", "destinationSource", "newAssetId", "expectedAssetRevision", "projectGeneration" },
            properties = new Dictionary<string, object> {
                ["operation"] = new Dictionary<string, object> { ["enum"] = new[] { "move", "copy" } },
                ["sourceMetadata"] = new { type = "string", maxLength = 1024 }, ["destinationMetadata"] = new { type = "string", maxLength = 1024 },
                ["destinationSource"] = new { type = "string", maxLength = 1024 }, ["newAssetId"] = new { type = new[] { "string", "null" }, format = "uuid" },
                ["expectedAssetRevision"] = new { type = "integer", minimum = 0 }, ["projectGeneration"] = new { type = "integer", minimum = 1 } }
        }), AssetMetadataCommands.Descriptor.OutputSchema);
    public ParticipantMemento Prepare(JsonElement input)
    {
        Verify();
        string[] names = ["operation", "sourceMetadata", "destinationMetadata", "destinationSource", "newAssetId", "expectedAssetRevision", "projectGeneration"];
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Count() != names.Length ||
            input.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != names.Length ||
            input.EnumerateObject().Any(p => !names.Contains(p.Name, StringComparer.Ordinal))) throw new ArgumentException("Invalid file command fields.");
        if (input.GetProperty("expectedAssetRevision").GetUInt64() != _clock.Revision) throw new EditCommandRejectedException("asset_revision_conflict");
        if (input.GetProperty("projectGeneration").GetUInt64() != _generation) throw new EditCommandRejectedException("asset_project_stale");
        string origin = input.GetProperty("sourceMetadata").GetString()!, destination = input.GetProperty("destinationMetadata").GetString()!,
            sourceDestination = AssetPaths.Validate(input.GetProperty("destinationSource").GetString()!);
        if (!origin.EndsWith(".ncmeta", StringComparison.Ordinal) || !destination.EndsWith(".ncmeta", StringComparison.Ordinal) ||
            sourceDestination.EndsWith(".ncmeta", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Invalid asset file extensions.");
        using var parents = new AssetDirectoryLease(_paths, [origin, destination, sourceDestination]);
        byte[] originalBytes;
        using (var metadata = WindowsAssetFile.Open(_paths.Resolve(origin, requireFile: true))) originalBytes = metadata.Read(AssetRecordCodec.MaxBytes);
        var original = AssetRecordCodec.Decode(originalBytes);
        _scope.Require(origin, original);
        string operation = input.GetProperty("operation").GetString()!;
        if (operation is not ("move" or "copy")) throw new ArgumentException("Unknown file operation.");
        Guid id = operation == "move" ? original.AssetId : input.GetProperty("newAssetId").GetGuid();
        if (id == Guid.Empty || (operation == "copy" && id == original.AssetId) || (operation == "move" && input.GetProperty("newAssetId").ValueKind != JsonValueKind.Null))
            throw new ArgumentException("Copy requires a new root UUID; move must not supply one.");
        var remap = original.Subassets.ToDictionary(s => s.AssetId, s => operation == "copy" ? Guid.NewGuid() : s.AssetId);
        var result = original with { AssetId = id, SourcePath = sourceDestination,
            Subassets = original.Subassets.Select(s => s with { AssetId = remap[s.AssetId] }).ToArray(),
            Dependencies = original.Dependencies.Select(d => d with { AssetId = remap.GetValueOrDefault(d.AssetId, d.AssetId) }).ToArray(),
            Generation = operation == "copy" ? null : original.Generation };
        _scope.Require(destination, result);
        var catalog = new AssetCatalogScanner(_paths).Scan().Catalog;
        if (operation == "copy") _ = new AssetCatalog(Enumerable.Range(0, (catalog.Count + 127) / 128).SelectMany(n => catalog.List(n * 128, 128)).Append(result));
        using var sourceParents = new AssetDirectoryLease(_paths, [original.SourcePath]);
        using var source = WindowsAssetFile.Open(_paths.Resolve(original.SourcePath, requireFile: true));
        if (source.Hash(AssetDiskTransaction.MaxSourceBytes) != original.SourceHash) throw new EditCommandRejectedException("asset_source_changed");
        string blob = Snapshot(source);
        var presentSource = new DiskImage(original.SourcePath, null, blob, original.SourceHash);
        DiskImage[] before = [AssetDiskTransaction.Inline(origin, originalBytes), presentSource,
            AssetDiskTransaction.Inline(destination, null), AssetDiskTransaction.Inline(sourceDestination, null)];
        DiskImage[] after = [AssetDiskTransaction.Inline(origin, operation == "move" ? null : originalBytes),
            operation == "move" ? AssetDiskTransaction.Inline(original.SourcePath, null) : presentSource,
            AssetDiskTransaction.Inline(destination, AssetRecordCodec.Encode(result)), new(sourceDestination, null, blob, original.SourceHash)];
        var memento = new ParticipantMemento(AssetDiskTransaction.Encode(before), AssetDiskTransaction.Encode(after));
        source.Dispose();
        Authorize(memento); _disk.Validate(memento, true); return memento;
    }
    private string Snapshot(WindowsAssetFile source)
    {
        using var directories = new AssetDirectoryLease(_paths, [_cache + "/.probe"]);
        string full = Path.GetDirectoryName(_paths.ResolveCache(_cache + "/.probe"))!;
        long bytes = 0; int count = 0;
        foreach (string entry in Directory.EnumerateFileSystemEntries(full))
        {
            if (++count >= MaxSnapshots) throw new ArgumentException("Asset immutable snapshot count budget exhausted.");
            AssetProjectPaths.RejectReparse(entry);
            if (Directory.Exists(entry)) throw new ArgumentException("Unexpected snapshot directory.");
            bytes = checked(bytes + new FileInfo(entry).Length);
            if (bytes > MaxSnapshotBytes - source.Length) throw new ArgumentException("Asset immutable snapshot byte budget exhausted.");
        }
        string relative = _cache + "/" + Guid.NewGuid().ToString("N") + ".blob";
        using var target = WindowsAssetFile.Open(_paths.ResolveCache(relative), create: true);
        try { source.CopyTo(target, AssetDiskTransaction.MaxSourceBytes); return relative; }
        catch { target.Delete(); throw; }
    }
    public void Authorize(ParticipantMemento memento)
    {
        Verify(); var (before, after) = AssetDiskTransaction.States(memento, true);
        if (before.Length != 4) throw new ArgumentException("Expected four file states.");
        foreach (var image in before.Concat(after))
        {
            if (image.Blob is not null && (!image.Blob.StartsWith(_cache + "/", StringComparison.Ordinal) ||
                !Guid.TryParseExact(Path.GetFileNameWithoutExtension(image.Blob), "N", out _) || !image.Blob.EndsWith(".blob", StringComparison.Ordinal)))
                throw new ArgumentException("Foreign asset snapshot.");
            if (image.Data is not null)
            {
                if (!image.Path.EndsWith(".ncmeta", StringComparison.Ordinal)) throw new ArgumentException("Expected metadata image.");
                _scope.Require(image.Path, AssetRecordCodec.Decode(image.Data));
            }
        }
        var a = AssetRecordCodec.Decode(before[0].Data ?? throw new ArgumentException("Missing original metadata."));
        var b = AssetRecordCodec.Decode(after[2].Data ?? throw new ArgumentException("Missing destination metadata."));
        if (before[1].Path != a.SourcePath || after[3].Path != b.SourcePath || before[1].Hash != a.SourceHash || after[3].Hash != b.SourceHash ||
            before[2].Hash is not null || before[3].Hash is not null || after[0].Hash != after[1].Hash && after[0].Hash is null)
            throw new ArgumentException("Invalid asset move/copy history.");
        if (a.AssetId == b.AssetId)
        {
            if (after[0].Hash is not null || after[1].Hash is not null || !AssetRecordCodec.Encode(a with { SourcePath = b.SourcePath }).SequenceEqual(after[2].Data!))
                throw new ArgumentException("Move may only change source path.");
        }
        else
        {
            if (after[0].Hash != before[0].Hash || after[1].Hash != before[1].Hash || b.Generation is not null ||
                b.Subassets.Length != a.Subassets.Length || b.Subassets.Any(s => a.Subassets.Any(old => old.AssetId == s.AssetId))) throw new ArgumentException("Invalid copy identity mapping.");
            // Codec sorting may reorder UUIDs: match the persistent importer source keys, never array indices.
            var remap = a.Subassets.ToDictionary(s => s.AssetId, s => b.Subassets.Single(n => n.SourceKey == s.SourceKey).AssetId);
            var expected = a with { AssetId = b.AssetId, SourcePath = b.SourcePath, Generation = null,
                Subassets = a.Subassets.Select(s => s with { AssetId = remap[s.AssetId] }).ToArray(),
                Dependencies = a.Dependencies.Select(d => d with { AssetId = remap.GetValueOrDefault(d.AssetId, d.AssetId) }).ToArray() };
            if (!AssetRecordCodec.Encode(expected).SequenceEqual(after[2].Data!)) throw new ArgumentException("Copy changes unauthorized fields.");
        }
    }
    public void Validate(ParticipantMemento memento, bool forward)
    {
        Authorize(memento); RequireNoJournal(memento); _disk.Validate(memento, forward); _ = checked(_clock.Revision + 1);
    }
    public void Publish(ParticipantMemento memento, bool forward) { Validate(memento, forward); _disk.Publish(memento, forward, Journal(memento)); _clock.Advance(); }
    public void Complete(ParticipantMemento memento, bool forward) => _disk.Complete();
    public void Compensate(ParticipantMemento memento, bool forward) => _disk.Compensate(memento, forward, Journal(memento));
    public void Recover(string metadataPath) { Verify(); _disk.Recover(metadataPath + ".journal", Authorize); _clock.Advance(); }
    private static string Journal(ParticipantMemento memento) => AssetDiskTransaction.Decode(memento.Before)[0].Path + ".journal";
    private void RequireNoJournal(ParticipantMemento memento)
    {
        string journal = _paths.Resolve(Journal(memento));
        if (File.Exists(journal) || Directory.Exists(journal)) throw new EditCommandRejectedException("asset_recovery_required");
    }
    private void Verify()
    {
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Asset commands require owner thread.");
        if (!_current()) throw new EditCommandRejectedException("asset_project_stale", denied: true);
    }
    public void Dispose() => _disk.Dispose();
}
