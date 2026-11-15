namespace Ncma.Editor.Services;

internal sealed record WorkflowStageFile(WorkflowEvidenceFile Input, string Destination, string Group, string Relative);

// Pure exact mapping first; physical owner exists before the first exclusive IO.
// Not a sandbox: directory inventories detect additions at checked boundaries.
internal sealed class WorkflowHostStaging : IDisposable
{
    private readonly int thread = System.Environment.CurrentManagedThreadId;
    private readonly WorkflowVerificationTask task;
    private readonly WorkflowStageFile[] mapping;
    private readonly string outputRoot, root;
    private readonly WorkflowPhysicalPins pins = new(32, 4096);
    private readonly WorkflowPinnedTree tree;
    private bool attempted, ready, closed;
    internal string Fingerprint { get; }
    internal WorkflowHostStaging(WorkflowVerificationTask task, string workspace)
    {
        this.task = task; mapping = Map(task, workspace);
        outputRoot = WorkflowBuildCommand.CommandPath(Path.Combine(workspace, "out", "verification", "m8-6", "jobs", task.Binding.JobId.ToString("N")));
        root = Path.Combine(outputRoot, "inputs"); tree = new(root);
        Fingerprint = WorkflowEvidenceRules.Digest(new { version = 1, task = task.Fingerprint, mapping });
    }
    internal static WorkflowStageFile[] Map(WorkflowVerificationTask task, string workspace)
    {
        if (task.Recipe.Id != "workflow-host-v1") throw new IOException("workflow_stage_recipe");
        _ = WorkflowTestLayout.RuntimeRoot(task, workspace);
        var rows = new List<WorkflowStageFile>(); long total = 0;
        foreach (var file in task.CopyInputs()) {
            string[] parts = file.Path.Split('/'); string group = parts[2];
            if (group == "runtime") continue; // actual retained test executable, not staged
            string relative = string.Join('/', parts.Skip(3));
            string destination = "out/managed/" + (group == "mcp" ? "editor-mcp/" : "") + relative;
            if (file.Bytes > 64 * 1024 * 1024 || file.Bytes > 512L * 1024 * 1024 - total) throw new IOException("workflow_stage_byte_budget");
            total += file.Bytes; rows.Add(new(file, destination, group, relative));
        }
        _ = WorkflowEvidenceRules.Files(rows.Select(r => r.Input with { Path = r.Destination })); // collisions fail BEFORE IO
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "" };
        foreach (var row in rows) for (int slash = row.Destination.IndexOf('/'); slash >= 0; slash = row.Destination.IndexOf('/', slash + 1)) {
            directories.Add(row.Destination[..slash]);
            if (directories.Count > WorkflowPinnedTree.MaximumDirectories) throw new IOException("workflow_stage_directory_budget");
        }
        return rows.OrderBy(r => r.Destination, StringComparer.Ordinal).ToArray();
    }
    private void Owner()
    {
        if (System.Environment.CurrentManagedThreadId != thread) throw new IOException("workflow_stage_thread");
        ObjectDisposedException.ThrowIf(closed, this);
    }
    internal void Prepare(WorkflowBuildOutput output, WorkflowCompiledProduct build, WorkflowNativeLease native)
    {
        Owner(); if (attempted) throw new IOException("workflow_stage_single_use"); attempted = true;
        if (!output.Reserved || output.TaskFingerprint != task.Fingerprint || output.Command.OutputRoot != outputRoot || !ReferenceEquals(native.Build, build))
            throw new IOException("workflow_stage_binding");
        build.RequireCurrent(); native.RequireProduct();
        string managedPrefix = "compiled/" + build.Origin.Binding.JobId.ToString("N") + "/";
        string nativePrefix = "native/" + native.Task.Binding.JobId.ToString("N") + "/plugins/";
        if (mapping.Any(r => !r.Input.Path.StartsWith(r.Group == "plugins" ? nativePrefix : managedPrefix, StringComparison.Ordinal)))
            throw new IOException("workflow_stage_producer");
        pins.Directory(outputRoot); _ = pins.CreateStageChild(outputRoot, "inputs");
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        byte[] buffer = new byte[65536];
        foreach (var row in mapping) {
            string current = root; string[] parts = row.Destination.Split('/');
            for (int i = 0; i < parts.Length - 1; i++) {
                string child = Path.Combine(current, parts[i]);
                if (directories.Add(child)) _ = pins.CreateStageChild(current, parts[i]);
                current = child;
            }
            using var file = new FileStream(Path.Combine(current, parts[^1]), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            WorkflowPhysicalPins.ValidateHandle(file.SafeFileHandle, false);
            if (row.Group == "plugins") native.CopyPluginFile(row.Relative, file, buffer);
            else build.CopyHostFile(row.Group, row.Relative, file, buffer);
            file.Flush(true);
        }
        tree.Prepare();
        var expected = WorkflowEvidenceRules.Files(mapping.Select(r => r.Input with { Path = r.Destination }));
        if (WorkflowEvidenceRules.Digest(tree.CopyFiles()) != WorkflowEvidenceRules.Digest(expected)) throw new IOException("workflow_stage_bytes_changed");
        build.RequireCurrent(); native.RequireProduct(); ready = true;
    }
    internal void RequireCurrent() { Owner(); if (!ready) throw new IOException("workflow_stage_not_ready"); tree.RequireCurrent(); }
    public void Dispose()
    {
        if (System.Environment.CurrentManagedThreadId != thread) throw new IOException("workflow_stage_thread");
        if (closed) return; tree.Dispose(); pins.Dispose(); closed = true; // evidence kept, no deletion
    }
}
