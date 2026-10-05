using Ncma.Assets.Authoring.Storage;
using Ncma.Editor.Core;

namespace Ncma.Assets.Authoring;

// Immutable publication BEFORE descriptor install. The existing descriptor journal is the atomic
// visibility boundary; a crash before that can leave only an unreferenced rebuildable generation.
// Pinning is conservative for the entire editor/history session, not just the current history cursor.
public sealed class DerivedGenerationStore : IDisposable
{
    private readonly AssetProjectPaths _paths;
    private readonly Guid _project;
    private readonly object _gate = new();
    private readonly Dictionary<string, Pin> _pins = new(StringComparer.Ordinal);
    private bool _closed;
    private sealed record Pin(WindowsAssetFile File, AssetDirectoryLease Parents, string Hash, long Bytes);
    public const int MaxGenerations = 128;
    public const long MaxBytes = 1024L * 1024 * 1024;
    public DerivedGenerationStore(AssetProjectPaths paths, Guid project) { if (project == Guid.Empty) throw new ArgumentException("Empty project."); _paths = paths; _project = project; }
    public void Prepare(AssetRecord record, byte[]? bytes, CancellationToken cancellation = default, Action<string>? fault = null)
    {
        var generation = record.Generation ?? throw new ArgumentException("Missing generation."); ValidatePath(record);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_pins.TryGetValue(generation.RelativePath, out var existing)) { if (existing.Hash != generation.ContentHash) throw new ArgumentException("Generation hash mismatch."); return; }
            if (_pins.Count >= MaxGenerations) throw new EditCommandRejectedException("asset_generation_budget");
            cancellation.ThrowIfCancellationRequested();
            string relative = generation.RelativePath;
            _paths.EnsureCacheDirectory(relative[..relative.LastIndexOf('/')], derived: true);
            var parents = new AssetDirectoryLease(_paths, [relative]); WindowsAssetFile? file = null;
            try
            {
                string full = _paths.ResolveDerived(relative);
                if (!File.Exists(full))
                {
                    if (bytes is null) throw new EditCommandRejectedException("asset_generation_missing");
                    if (bytes.Length > DerivedAssetCodec.MaxBytes || Storage.AssetDiskTransaction.Hash(bytes) != generation.ContentHash) throw new ArgumentException("Generation candidate hash mismatch.");
                    ModelAssetManifestCodec.ValidateBundle(bytes, record); cancellation.ThrowIfCancellationRequested();
                    RequireRetainedBudget(bytes.Length, cancellation);
                    string stage = relative + "." + Guid.NewGuid().ToString("N") + ".prepared";
                    using var candidate = WindowsAssetFile.Open(_paths.ResolveDerived(stage), create: true);
                    // Failures keep owned ignored orphan diagnostics; never remove unknown paths recursively.
                    candidate.Write(bytes); fault?.Invoke("generation_written"); cancellation.ThrowIfCancellationRequested();
                    candidate.Rename(full); fault?.Invoke("generation_published");
                }
                file = WindowsAssetFile.OpenReadLease(full);
                if (file.Hash(DerivedAssetCodec.MaxBytes, cancellation) != generation.ContentHash) throw new EditCommandRejectedException("asset_generation_conflict");
                if (_pins.Values.Sum(p => p.Bytes) + file.Length > MaxBytes) throw new EditCommandRejectedException("asset_generation_budget");
                ModelAssetManifestCodec.ValidateBundle(file.Read(DerivedAssetCodec.MaxBytes), record); cancellation.ThrowIfCancellationRequested();
                _pins.Add(relative, new(file, parents, generation.ContentHash, file.Length)); file = null;
            }
            catch { parents.Dispose(); throw; }
            finally { file?.Dispose(); }
        }
    }
    // Cheap owner check: no large IO/hash. A prepared no-write/no-delete lease is the evidence.
    public void RequirePinned(AssetRecord record)
    {
        if (record.Generation is null) return; ValidatePath(record);
        lock (_gate) if (_closed || !_pins.TryGetValue(record.Generation.RelativePath, out var pin) || pin.Hash != record.Generation.ContentHash)
            throw new EditCommandRejectedException("asset_generation_not_prepared");
    }
    public IDisposable PinForPlay(IEnumerable<AssetRecord> records)
    {
        // Separate actual read leases keep old generations alive even after the editor store closes.
        var leases = new List<IDisposable>();
        try { foreach (var record in records.Take(MaxGenerations + 1)) { RequirePinned(record); if (leases.Count >= MaxGenerations * 2) throw new ArgumentException("Play pin budget.");
            if (record.Generation is null) continue; var parents = new AssetDirectoryLease(_paths, [record.Generation.RelativePath]); leases.Add(parents);
            leases.Add(WindowsAssetFile.OpenReadLease(_paths.ResolveDerived(record.Generation.RelativePath, true))); } return new Group(leases); }
        catch { foreach (var lease in leases) lease.Dispose(); throw; }
    }
    // Explicit exact project-relative targets only, caller supplies approved candidates. In-use files
    // fail their DELETE sharing check. Never traverse directories, overwrite strangers or auto-GC.
    public void Collect(string[] exactPaths, Func<string, bool> approved, AssetCatalog liveCatalog)
    {
        if (exactPaths.Length > MaxGenerations || exactPaths.Distinct(StringComparer.Ordinal).Count() != exactPaths.Length) throw new ArgumentException("Invalid exact GC set.");
        var live = new HashSet<string>(); for (int offset = 0; offset < liveCatalog.Count; offset += 128) foreach (var r in liveCatalog.List(offset, 128)) if (r.Generation is not null) live.Add(r.Generation.RelativePath);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            var targets = new List<(WindowsAssetFile File, AssetDirectoryLease Parents)>();
            try
            {
                foreach (string path in exactPaths)
                {
                    ValidateRelative(path); if (!approved(path) || _pins.ContainsKey(path) || live.Contains(path)) throw new EditCommandRejectedException("asset_generation_pinned_or_denied");
                    var parents = new AssetDirectoryLease(_paths, [path]); WindowsAssetFile? file = null;
                    try {
                        file = WindowsAssetFile.Open(_paths.ResolveDerived(path, true));
                        string expectedHash = Path.GetFileNameWithoutExtension(path).Split('-')[^1]; AssetRecordCodec.ValidateHash(expectedHash);
                        if (file.Hash(DerivedAssetCodec.MaxBytes) != expectedHash) throw new EditCommandRejectedException("asset_generation_conflict");
                        targets.Add((file, parents)); file = null;
                    } catch { parents.Dispose(); throw; } finally { file?.Dispose(); }
                }
                // Entire exact set is validated and leased before any deletion. Stop on the first IO failure.
                foreach (var target in targets) target.File.Delete();
            }
            finally { foreach (var target in targets) { target.File.Dispose(); target.Parents.Dispose(); } }
        }
    }
    private void RequireRetainedBudget(int incomingBytes, CancellationToken cancellation)
    {
        string root = $"out/assets/{_project:N}"; var queue = new Queue<string>(); queue.Enqueue(root);
        int entries = 0, files = 0; long bytes = 0;
        while (queue.TryDequeue(out string? directory))
        {
            cancellation.ThrowIfCancellationRequested();
            using var lease = new AssetDirectoryLease(_paths, [directory + "/.probe"]);
            foreach (string entry in Directory.EnumerateFileSystemEntries(_paths.ResolveDerived(directory)))
            {
                if (++entries > MaxGenerations * 4) throw new EditCommandRejectedException("asset_generation_budget");
                string relative = Path.GetRelativePath(_paths.Root, entry).Replace('\\', '/'); _paths.ResolveDerived(relative);
                if (Directory.Exists(entry)) { queue.Enqueue(relative); continue; }
                // Unknown/orphan files also consume the budget; never silently delete them.
                bytes = checked(bytes + new FileInfo(entry).Length);
                if (++files >= MaxGenerations || bytes + incomingBytes > MaxBytes) throw new EditCommandRejectedException("asset_generation_budget");
            }
        }
        if (bytes + incomingBytes > MaxBytes) throw new EditCommandRejectedException("asset_generation_budget");
    }
    private void ValidatePath(AssetRecord record)
    {
        _ = AssetRecordCodec.Encode(record); if (record.Generation is null) return; ValidateRelative(record.Generation.RelativePath);
        if (record.Generation.RelativePath != $"out/assets/{_project:N}/{record.AssetId:N}/{record.Generation.Number}-{record.Generation.ContentHash}.nca") throw new ArgumentException("Generation path does not match identity/content.");
    }
    private void ValidateRelative(string path)
    {
        AssetPaths.Validate(path, $"out/assets/{_project:N}"); string[] parts = path.Split('/');
        if (parts.Length != 5 || !Guid.TryParseExact(parts[3], "N", out Guid root) || root == Guid.Empty || !path.EndsWith(".nca", StringComparison.Ordinal)) throw new ArgumentException("Invalid derived target.");
        string[] name = Path.GetFileNameWithoutExtension(path).Split('-');
        if (name.Length != 2 || !ulong.TryParse(name[0], out ulong number) || number == 0 || number.ToString(System.Globalization.CultureInfo.InvariantCulture) != name[0]) throw new ArgumentException("Invalid generation number.");
        AssetRecordCodec.ValidateHash(name[1]);
    }
    private sealed class Group(List<IDisposable> leases) : IDisposable { public void Dispose() { foreach (var lease in leases) lease.Dispose(); leases.Clear(); } }
    public void Dispose() { lock (_gate) { if (_closed) return; _closed = true; foreach (var pin in _pins.Values) { pin.File.Dispose(); pin.Parents.Dispose(); } _pins.Clear(); } }
}
