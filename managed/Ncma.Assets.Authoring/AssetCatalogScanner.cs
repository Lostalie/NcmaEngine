namespace Ncma.Assets.Authoring;

public sealed record AssetScanResult(AssetCatalog Catalog, AssetDiagnostic[] Diagnostics)
{
    public bool Valid => Diagnostics.All(d => d.Code is "source_missing" or "source_orphan" or "staging_orphan" or "asset_missing" or "asset_tombstone");
}

// Read-only scan. Unknown files never acquire identity, and no cache/source/file is written or deleted.
public sealed class AssetCatalogScanner
{
    public const int MaxEntries = 16384, MaxTotalDescriptorBytes = 64 * 1024 * 1024;
    private readonly AssetProjectPaths _paths;
    public AssetCatalogScanner(AssetProjectPaths paths) => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public AssetScanResult Scan()
    {
        string assets = _paths.Resolve("assets/.catalog-probe");
        assets = Path.GetDirectoryName(assets)!;
        var queue = new Queue<string>(); queue.Enqueue(assets);
        var files = new List<string>();
        var sources = new List<string>();
        var cases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int entries = 0;
        while (queue.Count > 0)
        {
            string directory = queue.Dequeue(); AssetProjectPaths.RejectReparse(directory);
            string probe = Path.GetRelativePath(_paths.Root, Path.Combine(directory, ".probe")).Replace('\\', '/');
            using var lease = new Storage.AssetDirectoryLease(_paths, [probe]);
            var bounded = new List<string>();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++entries > MaxEntries) throw new ArgumentException("Asset tree exceeds scan budget.");
                bounded.Add(entry);
            }
            bounded.Sort(StringComparer.Ordinal);
            foreach (string entry in bounded)
            {
                AssetProjectPaths.RejectReparse(entry);
                string relative = Path.GetRelativePath(_paths.Root, entry).Replace('\\', '/');
                AssetPaths.Validate(relative);
                if (!cases.Add(relative)) throw new ArgumentException("Case-colliding asset tree.");
                if (relative.EndsWith(".ncmeta.journal", StringComparison.Ordinal))
                    throw new Ncma.Editor.Core.EditCommandRejectedException("asset_recovery_required");
                if (Directory.Exists(entry)) queue.Enqueue(entry);
                else if (relative.EndsWith(".ncmeta", StringComparison.Ordinal)) files.Add(relative);
                else sources.Add(relative);
            }
        }
        if (files.Count > AssetCatalog.MaxRecords) throw new ArgumentException("Too many asset descriptors.");
        int totalBytes = 0;
        var records = new List<AssetRecord>();
        var diagnostics = new List<AssetDiagnostic>();
        foreach (string file in files)
        {
            string path = _paths.Resolve(file, requireFile: true);
            using var lease = new Storage.AssetDirectoryLease(_paths, [file]);
            using var stream = Storage.WindowsAssetFile.Open(path);
            if (stream.Length is < 2 or > AssetRecordCodec.MaxBytes || stream.Length > MaxTotalDescriptorBytes - totalBytes)
                throw new ArgumentException("Asset descriptors exceed scan byte budget.");
            byte[] bytes = stream.Read(AssetRecordCodec.MaxBytes); totalBytes += bytes.Length;
            var record = AssetRecordCodec.Decode(bytes); records.Add(record);
            try { _paths.Resolve(record.SourcePath, requireFile: true); }
            catch (FileNotFoundException) { diagnostics.Add(new("source_missing", record.AssetId, record.SourcePath, "Source identity retained; source file is missing.")); }
            catch (DirectoryNotFoundException) { diagnostics.Add(new("source_missing", record.AssetId, record.SourcePath, "Source identity retained; source directory is missing.")); }
        }
        var approved = records.Select(r => r.SourcePath).ToHashSet(StringComparer.Ordinal);
        foreach (string source in sources.Where(s => !approved.Contains(s)))
            diagnostics.Add(new(source.EndsWith(".before", StringComparison.Ordinal) || source.EndsWith(".after", StringComparison.Ordinal)
                ? "staging_orphan" : "source_orphan", Guid.Empty, source, "Unregistered file retained; no identity or write approval inferred."));
        var catalog = new AssetCatalog(records); diagnostics.AddRange(catalog.Diagnostics);
        return new(catalog, diagnostics.OrderBy(d => d.Path, StringComparer.Ordinal).ThenBy(d => d.AssetId).ToArray());
    }
}
