namespace Ncma.Assets.Authoring;

public sealed record AssetChangeIntent(Guid ProjectId, ulong Generation, string Path, string Kind);
public sealed record AssetChangeBatch(AssetChangeIntent[] Items, bool RequiresRescan);

// Notifications are observations only. No file writes, metadata generation or World access in callbacks.
public sealed class AssetChangeQueue : IDisposable
{
    public const int Capacity = 128;
    private readonly object _gate = new();
    private readonly Queue<AssetChangeIntent> _pending = [];
    private readonly Guid _projectId;
    private readonly ulong _generation;
    private FileSystemWatcher? _watcher;
    private bool _overflow, _closed;

    public AssetChangeQueue(Guid projectId, ulong generation)
    {
        if (projectId == Guid.Empty || generation == 0) throw new ArgumentException("Expected project identity/generation.");
        _projectId = projectId; _generation = generation;
    }
    public void Observe(string relativePath, string kind)
    {
        AssetPaths.Validate(relativePath);
        if (kind is not ("created" or "changed" or "deleted" or "renamed")) throw new ArgumentException("Unknown asset observation.");
        lock (_gate)
        {
            if (_closed) return;
            if (_pending.Count >= Capacity) { _overflow = true; return; }
            _pending.Enqueue(new(_projectId, _generation, relativePath, kind));
        }
    }
    public AssetChangeBatch Drain()
    {
        lock (_gate)
        {
            var result = new AssetChangeBatch(_pending.ToArray(), _overflow);
            _pending.Clear(); _overflow = false; return result;
        }
    }
    public void Start(AssetProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        // Reject pre-existing links/journals before enabling notifications; later intents are revalidated by the owner.
        _ = new AssetCatalogScanner(paths).Scan();
        string root = Path.Combine(paths.Root, "assets");
        lock (_gate)
        {
            if (_closed || _watcher is not null) throw new InvalidOperationException("Watcher already started or closed.");
            var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite };
            void Event(string full, string kind)
            {
                try { Observe(Path.GetRelativePath(paths.Root, full).Replace('\\', '/'), kind); }
                catch (ArgumentException) { lock (_gate) if (!_closed) _overflow = true; }
            }
            watcher.Created += (_, e) => Event(e.FullPath, "created");
            watcher.Changed += (_, e) => Event(e.FullPath, "changed");
            watcher.Deleted += (_, e) => Event(e.FullPath, "deleted");
            watcher.Renamed += (_, e) => { Event(e.OldFullPath, "renamed"); Event(e.FullPath, "renamed"); };
            watcher.Error += (_, _) => { lock (_gate) if (!_closed) _overflow = true; };
            try { watcher.EnableRaisingEvents = true; _watcher = watcher; }
            catch { watcher.Dispose(); throw; }
        }
    }
    public void Dispose()
    {
        FileSystemWatcher? watcher;
        lock (_gate) { _closed = true; _pending.Clear(); _overflow = false; watcher = _watcher; _watcher = null; }
        watcher?.Dispose(); // Outside lock: shutdown can wait for callbacks.
    }
}
