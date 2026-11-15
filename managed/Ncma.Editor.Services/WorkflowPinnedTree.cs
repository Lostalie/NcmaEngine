namespace Ncma.Editor.Services;

// Bounded post-drain inventory. Retains file bytes and directory identities;
// checks additions/removals separately because a directory handle is NOT a lock
// against creating children. No recursive cleanup or symlink traversal.
internal sealed class WorkflowPinnedTree : IDisposable
{
    internal const int MaximumFiles = 2048, MaximumDirectories = 2048;
    private readonly int thread = System.Environment.CurrentManagedThreadId;
    private readonly WorkflowPhysicalPins pins = new(MaximumFiles, 4096);
    private readonly string root;
    private WorkflowEvidenceFile[] files = [];
    private Dictionary<string, WorkflowEvidenceFile> byPath = new(StringComparer.Ordinal);
    private HashSet<string> entries = new(StringComparer.OrdinalIgnoreCase);
    private bool attempted, ready, closed;
    internal string Fingerprint { get; private set; } = "";
    internal WorkflowPinnedTree(string root) => this.root = WorkflowBuildCommand.CommandPath(root);
    private void Owner()
    {
        if (System.Environment.CurrentManagedThreadId != thread) throw new IOException("workflow_tree_thread");
        ObjectDisposedException.ThrowIf(closed, this);
    }
    private HashSet<string> Walk(Action<string, string>? file)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(); pending.Push(root);
        int directories = 0, count = 0;
        while (pending.TryPop(out string? directory)) {
            if (++directories > MaximumDirectories) throw new IOException("workflow_tree_directory_budget");
            pins.Directory(directory); // refuses reparse before enumeration
            foreach (string path in Directory.EnumerateFileSystemEntries(directory)) {
                string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("workflow_tree_reparse");
                bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                if (!result.Add((isDirectory ? "d/" : "f/") + relative)) throw new IOException("workflow_tree_alias");
                if (isDirectory) {
                    if (directories + pending.Count >= MaximumDirectories) throw new IOException("workflow_tree_directory_budget");
                    pending.Push(path);
                } else {
                    if (++count > MaximumFiles) throw new IOException("workflow_tree_file_budget");
                    file?.Invoke(path, relative);
                }
            }
        }
        return result;
    }
    internal void Prepare()
    {
        Owner(); if (attempted) throw new IOException("workflow_tree_single_use"); attempted = true;
        var measured = new List<WorkflowEvidenceFile>(); long total = 0;
        entries = Walk((path, relative) => {
            var value = pins.Observe(path, 64 * 1024 * 1024, allowEmpty: true);
            if (value.Bytes > 512L * 1024 * 1024 - total) throw new IOException("workflow_tree_byte_budget");
            total += value.Bytes; measured.Add(new(relative, value.Bytes, value.Sha256));
        });
        if (!entries.SetEquals(Walk(null))) throw new IOException("workflow_tree_changed_during_capture");
        files = WorkflowEvidenceRules.Files(measured);
        byPath = files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        Fingerprint = WorkflowEvidenceRules.Digest(new { version = 1, root, files, entries = entries.Order(StringComparer.Ordinal).ToArray() });
        ready = true;
    }
    internal void RequireCurrent()
    {
        Owner(); if (!ready) throw new IOException("workflow_tree_not_ready");
        if (!entries.SetEquals(Walk(null))) throw new IOException("workflow_tree_set_changed");
    }
    internal WorkflowEvidenceFile[] CopyFiles() { RequireCurrent(); return (WorkflowEvidenceFile[])files.Clone(); }
    // Caller brackets the whole batch with RequireCurrent; no O(files^2) walks.
    internal void CopyLocked(string relative, Stream destination, byte[] buffer)
    {
        Owner(); if (!ready || !byPath.TryGetValue(relative, out var file)) throw new IOException("workflow_tree_file_missing");
        pins.CopyObserved(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)), file.Sha256, file.Bytes, destination, buffer);
    }
    internal byte[] Read(string relative, int maximumBytes)
    {
        RequireCurrent();
        var file = files.SingleOrDefault(f => f.Path == relative) ?? throw new IOException("workflow_tree_file_missing");
        return pins.ReadFile(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)), file.Sha256, file.Bytes, maximumBytes);
    }
    public void Dispose()
    {
        if (System.Environment.CurrentManagedThreadId != thread) throw new IOException("workflow_tree_thread");
        if (closed) return; pins.Dispose(); closed = true;
    }
}
