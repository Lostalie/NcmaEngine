namespace Ncma.Editor.Services;

internal interface IWorkflowRestoreProducer : IWorkflowBuildLease
{
    WorkflowBuildOutput Output { get; }
    bool SdkStarted { get; }
}

// Live producer-owned result, NOT deserializable authority. A successful SDK
// exit alone does not authorize compilation; later build approval must also
// verify the project/package/import graph and mapping against these exact bytes.
internal sealed class WorkflowRestoreProduct : IDisposable
{
    private readonly IWorkflowRestoreProducer producer;
    private readonly int thread = System.Environment.CurrentManagedThreadId;
    private readonly WorkflowPinnedTree artifacts, packages;
    private WorkflowEvidenceFile[] logs = [];
    private bool attempted, ready, closed;
    private int borrowers;
    internal string Fingerprint { get; private set; } = "";
    internal WorkflowVerificationReceipt? Receipt { get; private set; }
    internal bool Retained => borrowers != 0;
    internal WorkflowVerificationTask Origin => producer.Task;
    internal WorkflowBuildEnvironment Environment => producer.Environment;
    internal string PackagesRoot => producer.Output.Command.PackagesRoot;
    internal WorkflowRestoreProduct(IWorkflowRestoreProducer producer)
    {
        this.producer = producer;
        artifacts = new(producer.Output.Command.ArtifactsRoot);
        packages = new(producer.Output.Command.PackagesRoot);
    }
    internal WorkflowVerificationReceipt Prepare(WorkflowBuildStatus status)
    {
        if (attempted || closed) throw new IOException("workflow_restore_product_single_use");
        var actual = producer.Observe(); var outcome = WorkflowObservedEvidence.Outcome(Origin, status, actual);
        attempted = true;
        WorkflowEvidenceFile[] inputs = [], outputs = []; int? warnings = null, errors = null;
        bool complete = actual.FailureCode.Length == 0 && !actual.TimedOut;
        string diagnostic = complete ? "" : "execution_evidence_incomplete";
        if (actual.BuildStarted) {
            void Failed(Exception error) { complete = false; if (diagnostic.Length == 0) diagnostic = WorkflowObservedEvidence.Code(error); }
            if (!producer.SdkStarted) { complete = false; diagnostic = "workflow_restore_sdk_not_started"; }
            try { producer.RequireCurrent(); inputs = Origin.CopyInputs(); }
            catch (Exception error) when (error is not OutOfMemoryException) { Failed(error); }
            try {
                logs = producer.Output.SealLogs();
                (warnings, errors) = WorkflowObservedEvidence.CompilerCounts(producer.Output.ReadSealedLog("stdout.log"), producer.Output.ReadSealedLog("stderr.log"));
                if (warnings is null || errors is null) { complete = false; if (diagnostic.Length == 0) diagnostic = "restore_summary_unobserved"; }
            } catch (Exception error) when (error is not OutOfMemoryException) { Failed(error); }
            try {
                artifacts.Prepare(); packages.Prepare();
                const string prefix = "obj/Ncma.Editor.Services.Tests/";
                outputs = artifacts.CopyFiles().Where(f => f.Path.StartsWith(prefix, StringComparison.Ordinal))
                    .Select(f => f with { Path = f.Path[prefix.Length..] })
                    .Where(f => Origin.Recipe.RequiredOutputs.Contains(f.Path)).ToArray();
                if (Origin.Recipe.RequiredOutputs.Any(name => !outputs.Any(f => f.Path == name && f.Bytes > 0)))
                    throw new IOException("workflow_restore_product_missing");
            } catch (Exception error) when (error is not OutOfMemoryException) { Failed(error); }
        }
        Receipt = new(Origin, Origin.Fingerprint, outcome, actual.ExitCode, warnings, errors, inputs, outputs, logs, [], complete, diagnostic, inputsObserved: inputs.Length != 0);
        if (Receipt.Conclusion == WorkflowVerificationConclusion.RestorePrepared && status.Outcome == WorkflowBuildOutcome.ProcessExited) {
            Fingerprint = WorkflowEvidenceRules.Digest(new { version = 2, task = Origin.Fingerprint,
                preparation = producer.PreparationFingerprint, artifacts = artifacts.Fingerprint, packages = packages.Fingerprint, receipt = Receipt.Fingerprint });
            ready = true;
        }
        return Receipt;
    }
    internal void RequireCurrent()
    {
        if (!ready || closed) throw new IOException("workflow_restore_product_unavailable");
        producer.RequireCurrent(); artifacts.RequireCurrent(); packages.RequireCurrent();
    }
    internal WorkflowEvidenceFile[] CopyArtifacts() { RequireCurrent(); return artifacts.CopyFiles(); }
    internal WorkflowEvidenceFile[] CopyPackages() { RequireCurrent(); return packages.CopyFiles(); }
    internal WorkflowEvidenceFile[] CopyLogs() { RequireCurrent(); return (WorkflowEvidenceFile[])logs.Clone(); }
    internal WorkflowVerificationTask CreateCompileTask()
    {
        RequireCurrent();
        var values = new List<WorkflowEvidenceFile>();
        var retainedArtifacts = artifacts.CopyFiles().ToDictionary(f => f.Path, StringComparer.Ordinal);
        var retainedPackages = packages.CopyFiles().ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string prefix = "restore/" + Origin.Binding.JobId.ToString("N") + "/";
        int projects = 0;
        foreach (var file in retainedArtifacts.Values.Where(f => f.Path.EndsWith("/project.assets.json", StringComparison.Ordinal))) {
            string[] parts = file.Path.Split('/');
            if (parts.Length != 3 || parts[0] != "obj" || ++projects > WorkflowRestoreGraph.MaximumProjects) throw new IOException("workflow_restore_artifact_layout");
            string name = parts[1]; string relative = "managed/" + name + "/" + name + ".csproj";
            string directory = Path.Combine(producer.Output.Command.ArtifactsRoot, "obj", name);
            var assets = WorkflowRestoreAssets.Parse(artifacts.Read(file.Path, WorkflowRestoreAssets.MaximumBytes),
                Path.Combine(producer.Output.Command.CaptureRoot, "source"), relative, directory);
            if (!assets.CopyPackageFolders().ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals([PackagesRoot])) throw new IOException("workflow_restore_foreign_package_root");
            foreach (string leaf in new[] { "project.assets.json", name + ".csproj.nuget.g.props", name + ".csproj.nuget.g.targets" }) {
                string path = "obj/" + name + "/" + leaf;
                if (!retainedArtifacts.TryGetValue(path, out var declared)) throw new IOException("workflow_restore_generated_input_missing");
                values.Add(declared with { Path = prefix + path });
            }
            foreach (var package in assets.CopyPackages()) {
                string[] identity = package.RelativePath.Split('/');
                foreach (string path in package.Files.Append(identity[0] + "." + identity[1] + ".nupkg")) required.Add(package.RelativePath + "/" + path);
            }
        }
        foreach (string path in required) {
            if (!retainedPackages.TryGetValue(path, out var file)) throw new IOException("workflow_restore_package_input_missing");
            values.Add(file with { Path = "packages/0/" + path });
        }
        return new(Origin.Binding with { JobId = Guid.NewGuid(), RecipeId = "editor-services-build-v1" }, values, []);
    }
    internal IDisposable Borrow()
    {
        RequireCurrent(); if (borrowers == WorkflowBuildJobs.MaxJobs) throw new IOException("workflow_restore_borrow_budget");
        borrowers++; return new Borrower(this);
    }
    private sealed class Borrower(WorkflowRestoreProduct owner) : IDisposable
    {
        private bool released;
        public void Dispose() {
            if (System.Environment.CurrentManagedThreadId != owner.thread) throw new IOException("workflow_restore_borrow_thread");
            if (released) return; owner.borrowers--; released = true;
        }
    }
    public void Dispose()
    {
        if (closed) return;
        if (Retained) throw new IOException("workflow_restore_product_borrowed");
        packages.Dispose(); artifacts.Dispose(); closed = true;
    }
}
