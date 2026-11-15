namespace Ncma.Editor.Services;

// Read-only, owner-thread preparation. No build/restore invocation or output
// staging. Its three pinned files do NOT establish a complete offline closure.
internal sealed class WorkflowRestoreInspection : IDisposable
{
    private readonly int ownerThread = System.Environment.CurrentManagedThreadId;
    private readonly WorkflowVerificationTask task;
    private readonly WorkflowPhysicalPins pins = new();
    private readonly string source, relativeProject, name, evidencePrefix;
    private WorkflowRestoreAssets? result;
    private bool attempted, closed;
    internal string RestoreOutput { get; }
    internal string RelativeProject => relativeProject;
    internal string TaskFingerprint => task.Fingerprint;
    internal bool CompilationAuthorized => false;
    internal WorkflowRestoreInspection(WorkflowVerificationTask task, WorkflowBuildEnvironment environment, Guid restoreId, string relativeProject)
    {
        if (restoreId == Guid.Empty || task.Recipe.Id != "editor-services-build-v1") throw new IOException("workflow_restore_scope");
        this.task = task; this.relativeProject = relativeProject; name = WorkflowRestoreAssets.ProjectName(relativeProject);
        var command = new WorkflowBuildCommand(task, environment); source = Path.Combine(command.CaptureRoot, "source");
        RestoreOutput = Path.Combine(environment.WorkspaceRoot, "out", "verification", "m8-6", "restores", restoreId.ToString("N"), "artifacts", "obj", name);
        _ = WorkflowBuildCommand.CommandPath(RestoreOutput);
        evidencePrefix = "restore/" + restoreId.ToString("N") + "/obj/" + name + "/";
    }
    private void Owner()
    {
        if (System.Environment.CurrentManagedThreadId != ownerThread) throw new InvalidOperationException("workflow_restore_thread");
    }
    internal void Prepare()
    {
        Owner(); if (attempted || closed) throw new InvalidOperationException("workflow_restore_single_use"); attempted = true;
        var declared = task.CopyInputs().ToDictionary(f => f.Path, StringComparer.Ordinal);
        var names = new[] { "project.assets.json", name + ".csproj.nuget.g.props", name + ".csproj.nuget.g.targets" };
        // Validate the COMPLETE three-file declaration before acquiring any file.
        var files = new WorkflowEvidenceFile[3];
        for (int i = 0; i < names.Length; i++) {
            if (!declared.TryGetValue(evidencePrefix + names[i], out var file) || file.Bytes < 1 || file.Bytes > (i == 0 ? WorkflowRestoreAssets.MaximumBytes : 1048576))
                throw new IOException("workflow_restore_declared_input_missing_or_large");
            files[i] = file;
        }
        byte[] assets = pins.ReadFile(Path.Combine(RestoreOutput, names[0]), files[0].Sha256, files[0].Bytes, WorkflowRestoreAssets.MaximumBytes);
        for (int i = 1; i < names.Length; i++) pins.File(Path.Combine(RestoreOutput, names[i]), files[i].Sha256, files[i].Bytes);
        // Generated imports are pinned but not evaluated; their transitive paths,
        // packages, SDK and restore provenance must be checked by the later gate.
        result = WorkflowRestoreAssets.Parse(assets, source, relativeProject, RestoreOutput);
    }
    internal WorkflowRestoreAssets Read()
    {
        Owner(); if (closed || result is null) throw new InvalidOperationException("workflow_restore_not_inspected");
        return result;
    }
    public void Dispose()
    {
        Owner(); if (closed) return; pins.Dispose(); result = null; closed = true;
    }
}
