using Ncma.Editor.Core;
using Ncma.Assets.Authoring.Storage;

namespace Ncma.Assets.Authoring;

// Trusted project composition. One live authoring owner/clock per root; closing revokes history/replay grants.
public sealed class AssetProjectAuthoring : IDisposable
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly AssetProjectPaths _paths;
    private readonly WindowsAssetFile _lock;
    private readonly AssetDirectoryLease _lockParents;
    private readonly AssetChangeQueue _changes;
    private bool _disposed;
    private Task _completion = Task.CompletedTask;
    public Task Completion { get { if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Owner required."); return _completion; } }
    public Guid ProjectId { get; }
    public ulong Generation { get; }
    public AssetRevisionClock Clock { get; } = new();
    public AssetMetadataCommands Metadata { get; }
    public AssetFileCommands Files { get; }
    public AssetImportCommands Imports { get; }
    private readonly List<ImportCoordinator> _coordinators = [];
    private AssetScanResult _snapshot = new(new AssetCatalog([]), []);
    public Task GenerationPreparation { get; private set; } = Task.CompletedTask;
    private ulong _snapshotRevision;
    public AssetScanResult Snapshot { get { Verify(); return new(_snapshot.Catalog, (AssetDiagnostic[])_snapshot.Diagnostics.Clone()); } }
    public AssetProjectAuthoring(string root, Guid projectId, ulong generation, EditSession edit, AssetWriteScope scope,
        Action<string>? faultInjection = null)
    {
        ArgumentNullException.ThrowIfNull(edit); ArgumentNullException.ThrowIfNull(scope);
        if (projectId == Guid.Empty || generation == 0) throw new ArgumentException("Expected project identity/generation.");
        var state = edit.State;
        if (state.UndoCount != 0 || state.RedoCount != 0 || state.Frozen || state.EditBusy || state.HistoryInvalidated ||
            edit.Describe().Any(d => d.Name is AssetMetadataCommands.CapabilityName or AssetFileCommands.CapabilityName or AssetImportCommands.CapabilityName))
            throw new InvalidOperationException("Asset composition requires a fresh idle edit session.");
        _paths = new(root); ProjectId = projectId; Generation = generation;
        _paths.EnsureCacheDirectory("out/asset-authoring");
        const string lockPath = "out/asset-authoring/project.lock";
        _lockParents = new(_paths, [lockPath]);
        try
        {
            string full = _paths.ResolveCache(lockPath);
            _lock = WindowsAssetFile.Open(full, create: !File.Exists(full));
        }
        catch { _lockParents.Dispose(); throw; }
        var bound = scope.BindCurrent(() => !_disposed);
        Metadata = new(_paths, bound, faultInjection, Clock);
        _changes = new(projectId, generation);
        try
        {
            Files = new(_paths, bound, projectId, generation, Clock, () => !_disposed, faultInjection);
            Imports = new(_paths, bound, projectId, generation, Clock, () => !_disposed, faultInjection);
            RecoverStartup(); _snapshot = new AssetCatalogScanner(_paths).Scan(); _snapshotRevision = Clock.Revision;
            // Both descriptors are validated before startup registration; no request supplies filesystem grants.
            edit.RegisterCommandParticipant(AssetMetadataCommands.Descriptor, Metadata);
            edit.RegisterCommandParticipant(AssetFileCommands.Descriptor, Files);
            edit.RegisterCommandParticipant(AssetImportCommands.Descriptor, Imports);
            _changes.Start(_paths);
            GenerationPreparation = Imports.PrepareStoredGenerationsAsync(Records());
        }
        catch { Dispose(); throw; }
    }
    private void RecoverStartup()
    {
        var queue = new Queue<string>(); queue.Enqueue("assets/.probe"); int entries = 0;
        var journals = new List<string>();
        while (queue.TryDequeue(out string? probe))
        {
            using var lease = new AssetDirectoryLease(_paths, [probe]);
            string directory = Path.GetDirectoryName(_paths.Resolve(probe))!;
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++entries > AssetCatalogScanner.MaxEntries) throw new ArgumentException("Asset recovery scan exceeds budget.");
                string relative = Path.GetRelativePath(_paths.Root, entry).Replace('\\', '/');
                _paths.Resolve(relative);
                if (Directory.Exists(entry)) queue.Enqueue(relative + "/.probe");
                else if (relative.EndsWith(".ncmeta.journal", StringComparison.Ordinal)) journals.Add(relative);
            }
        }
        foreach (string journal in journals.Order(StringComparer.Ordinal))
        {
            string metadata = journal[..^8];
            // The bounded journal's memento determines participant kind; authorize before touching any target.
            using var lease = new AssetDirectoryLease(_paths, [journal]);
            using var file = WindowsAssetFile.Open(_paths.Resolve(journal));
            byte[] bytes = file.Read(AssetDiskTransaction.MaxJournalBytes); file.Dispose();
            AssetDiskTransaction.RejectDuplicates(bytes);
            using var doc = System.Text.Json.JsonDocument.Parse(bytes);
            byte[] before = doc.RootElement.GetProperty("Memento").GetProperty("Before").GetBytesFromBase64();
            var images = AssetDiskTransaction.Decode(before);
            byte[] after = doc.RootElement.GetProperty("Memento").GetProperty("After").GetBytesFromBase64();
            if (images.Length == 1)
            {
                var target = AssetDiskTransaction.Decode(after)[0];
                if (target.Data is not null && AssetRecordCodec.Decode(target.Data).Generation is not null) Imports.Recover(metadata);
                else Metadata.Recover(metadata);
            }
            else Files.Recover(metadata);
        }
    }
    public bool Refresh(ulong generation, bool force = false)
    {
        Verify(); if (generation != Generation) throw new EditCommandRejectedException("asset_project_stale");
        var batch = _changes.Drain();
        if (!force && !batch.RequiresRescan && batch.Items.Length == 0) return false;
        if (batch.Items.Any(i => i.ProjectId != ProjectId || i.Generation != Generation)) throw new EditCommandRejectedException("asset_project_stale");
        // All observed paths, including deletions, are checked again. Failure retains the previous complete index.
        foreach (var intent in batch.Items) try { _paths.Resolve(intent.Path); } catch (DirectoryNotFoundException) { }
        var candidate = new AssetCatalogScanner(_paths).Scan();
        Clock.Advance(); _snapshot = candidate; _snapshotRevision = Clock.Revision; return true;
    }
    public ImportCoordinator CreateImportCoordinator(ImportWorkerLaunch launch, Func<string, bool> approvedSource, ImportLimits? limits = null)
    {
        Verify(); if (_coordinators.Count >= 1) throw new InvalidOperationException("One coordinator per project owner.");
        var coordinator = new ImportCoordinator(_paths, ProjectId, Generation, Clock, launch, approvedSource, () => !_disposed, limits);
        _coordinators.Add(coordinator); return coordinator;
    }
    private AssetRecord[] Records()
    {
        var result = new List<AssetRecord>();
        for (int offset = 0; offset < _snapshot.Catalog.Count; offset += 128) result.AddRange(_snapshot.Catalog.List(offset, 128));
        return result.ToArray();
    }
    public Task PrepareStoredGenerationsAsync() { Verify(); return GenerationPreparation = Imports.PrepareStoredGenerationsAsync(Records()); }
    public IDisposable PinForPlay()
    {
        Verify(); if (_snapshotRevision != Clock.Revision) throw new EditCommandRejectedException("asset_catalog_stale");
        if (!GenerationPreparation.IsCompleted) throw new EditCommandRejectedException("asset_generation_preparing");
        GenerationPreparation.GetAwaiter().GetResult(); // Completed only: no owner-thread IO or waiting.
        return Imports.Generations.PinForPlay(Records());
    }
    private void Verify()
    {
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Asset project requires owner thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
    public void Dispose()
    {
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Asset project requires owner thread.");
        if (_disposed) return; _disposed = true;
        _changes.Dispose(); foreach (var coordinator in _coordinators) coordinator.Dispose(); Imports?.Dispose(); Metadata.Dispose(); Files?.Dispose();
        var completion = Task.WhenAll(_coordinators.Select(c => c.Completion).Append(Imports?.Completion ?? Task.CompletedTask));
        _completion = completion.ContinueWith(_ => { _lock.Dispose(); _lockParents.Dispose(); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
