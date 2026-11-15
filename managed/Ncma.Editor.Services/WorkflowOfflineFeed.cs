namespace Ncma.Editor.Services;

// Local-host supplied cache root; only exact task-declared .nupkg files are read.
// Preparation is read-only. Copying requires this exact task's consumed original
// queue permit/output reservation. The owner must outlive all SDK borrowers.
internal sealed class WorkflowOfflineFeed : IDisposable
{
    internal const int MaximumArchives = 128;
    internal string Root => root;
    private readonly int thread = System.Environment.CurrentManagedThreadId;
    private readonly WorkflowVerificationTask task;
    private readonly string root;
    private readonly WorkflowEvidenceFile[] archives;
    private readonly WorkflowPhysicalPins pins = new(MaximumArchives * 3, 1024);
    private bool attempted, prepared, stagingAttempted, staged, closed;
    internal string Fingerprint { get; }
    internal bool NetworkAuthorized => false;
    internal WorkflowOfflineFeed(WorkflowVerificationTask task, string approvedCacheRoot)
    {
        if (task.Recipe.Kind != WorkflowVerificationKind.OfflineRestore) throw new IOException("workflow_offline_recipe_required");
        this.task = task; root = WorkflowBuildCommand.CommandPath(approvedCacheRoot);
        if (Path.GetPathRoot(root) == root) throw new IOException("workflow_offline_cache_root");
        archives = task.CopyInputs().Where(f => f.Path.StartsWith("offline-feed/", StringComparison.Ordinal)).ToArray();
        if (archives.Length is < 1 or > MaximumArchives || archives.Length != task.CopyInputs().Length) throw new IOException("workflow_offline_archive_budget");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var file in archives) {
            string[] parts = file.Path.Split('/');
            if (parts.Length != 4 || parts[3] != parts[1] + "." + parts[2] + ".nupkg" || !names.Add(parts[3]) ||
                file.Bytes is < 1 or > 64L * 1024 * 1024 || file.Bytes > 256L * 1024 * 1024 - total)
                throw new IOException("workflow_offline_archive_identity");
            total += file.Bytes;
        }
        Fingerprint = WorkflowEvidenceRules.Digest(new { version = 1, task = task.Fingerprint, root, archives, NetworkAuthorized });
    }
    private void Owner()
    {
        if (System.Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("workflow_offline_thread");
        ObjectDisposedException.ThrowIf(closed, this);
    }
    private string Source(WorkflowEvidenceFile file) => Path.Combine(root, file.Path["offline-feed/".Length..].Replace('/', Path.DirectorySeparatorChar));
    internal void Prepare()
    {
        Owner(); if (attempted) throw new IOException("workflow_offline_single_use"); attempted = true;
        pins.Directory(root);
        foreach (var file in archives) pins.File(Source(file), file.Sha256, file.Bytes);
        prepared = true;
    }
    internal void RequirePrepared() { Owner(); if (!prepared) throw new IOException("workflow_offline_not_prepared"); }
    internal void Stage(WorkflowBuildOutput output)
    {
        RequirePrepared();
        if (stagingAttempted) throw new IOException("workflow_offline_stage_single_use"); stagingAttempted = true;
        if (!output.Reserved || !output.Command.IsOfflineRestore || output.TaskFingerprint != task.Fingerprint)
            throw new IOException("workflow_offline_approval_required");
        foreach (var file in archives) pins.CopyDeclaredFile(Source(file), Path.Combine(output.Command.FeedRoot, Path.GetFileName(file.Path)), file.Sha256, file.Bytes);
        RequireFeedSet(output); staged = true;
    }
    private void RequireFeedSet(WorkflowBuildOutput output)
    {
        var names = archives.Select(f => Path.GetFileName(f.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase); int count = 0;
        foreach (string path in Directory.EnumerateFileSystemEntries(output.Command.FeedRoot)) {
            if (++count > MaximumArchives || !names.Remove(Path.GetFileName(path))) throw new IOException("workflow_offline_feed_set_changed");
        }
        if (names.Count != 0) throw new IOException("workflow_offline_feed_set_changed");
    }
    internal void RequireStaged(WorkflowBuildOutput output)
    {
        RequirePrepared();
        if (!staged || !output.Reserved || output.TaskFingerprint != task.Fingerprint) throw new IOException("workflow_offline_not_staged");
        RequireFeedSet(output);
    }
    public void Dispose()
    {
        if (System.Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("workflow_offline_thread");
        if (closed) return; pins.Dispose(); closed = true;
    }
}
