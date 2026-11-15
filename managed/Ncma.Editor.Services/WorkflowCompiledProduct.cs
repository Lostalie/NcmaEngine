namespace Ncma.Editor.Services;

// Live compiled-byte owner: only created by the actual compile lease, never by
// reading a success JSON. Runtime dependencies here are managed application
// files; the installed framework/SDK remains a trusted external prerequisite.
internal sealed class WorkflowCompiledProduct : IDisposable
{
    private readonly int thread = System.Environment.CurrentManagedThreadId;
    private readonly WorkflowCompileLease producer;
    private readonly WorkflowManagedRuntime runtime, mcp, gameplay;
    private bool attempted, ready, closed;
    private int borrowers;
    internal string Fingerprint { get; private set; } = "";
    internal WorkflowVerificationTask Origin => producer.Task;
    internal WorkflowBuildEnvironment Environment => producer.Environment;
    internal WorkflowVerificationReceipt? Receipt { get; private set; }
    internal string RuntimeRoot { get; }
    internal WorkflowCompiledProduct(WorkflowCompileLease producer)
    {
        this.producer = producer;
        RuntimeRoot = Path.Combine(producer.Output.Command.ArtifactsRoot, "bin", "Ncma.Editor.Services.Tests", producer.Task.Binding.Configuration.ToLowerInvariant());
        runtime = new(RuntimeRoot, "Ncma.Editor.Services.Tests", true);
        string bin = Path.Combine(producer.Output.Command.ArtifactsRoot, "bin"), configuration = producer.Task.Binding.Configuration.ToLowerInvariant();
        mcp = new(Path.Combine(bin, "Ncma.Editor.Mcp", configuration), "Ncma.Editor.Mcp", true);
        gameplay = new(Path.Combine(bin, "Ncma.Gameplay.Sample", configuration), "Ncma.Gameplay.Sample", false);
    }
    internal WorkflowVerificationReceipt Prepare(WorkflowBuildStatus status)
    {
        if (attempted || closed) throw new IOException("workflow_compiled_product_single_use");
        var actual = producer.Observe(); var outcome = WorkflowObservedEvidence.Outcome(Origin, status, actual);
        attempted = true;
        WorkflowEvidenceFile[] inputs = [], outputs = [], logs = []; int? warnings = null, errors = null;
        bool complete = actual.FailureCode.Length == 0; string diagnostic = complete ? "" : "execution_evidence_incomplete";
        if (actual.BuildStarted) {
            void Failed(Exception error) { complete = false; if (diagnostic.Length == 0) diagnostic = WorkflowObservedEvidence.Code(error); }
            try { producer.RequireCurrent(); inputs = Origin.CopyInputs(); }
            catch (Exception error) when (error is not OutOfMemoryException) { Failed(error); }
            try {
                logs = producer.Output.SealLogs();
                (warnings, errors) = WorkflowObservedEvidence.CompilerCounts(producer.Output.ReadSealedLog("stdout.log"), producer.Output.ReadSealedLog("stderr.log"));
                if (warnings is null || errors is null) { complete = false; if (diagnostic.Length == 0) diagnostic = "compiler_summary_unobserved"; }
            } catch (Exception error) when (error is not OutOfMemoryException) { Failed(error); }
            try {
                if (Directory.Exists(RuntimeRoot)) {
                    runtime.Prepare(); outputs = runtime.CopyFiles();
                    mcp.Prepare(); outputs = [.. outputs, .. mcp.CopyFiles().Select(f => f with { Path = "host/mcp/" + f.Path })];
                    gameplay.Prepare(); outputs = [.. outputs, .. gameplay.CopyFiles().Select(f => f with { Path = "host/gameplay/" + f.Path })];
                }
                else { complete = false; if (diagnostic.Length == 0) diagnostic = "compiled_products_missing"; }
            } catch (Exception error) when (error is not OutOfMemoryException) { Failed(error); }
        }
        Receipt = new(Origin, Origin.Fingerprint, outcome, actual.ExitCode, warnings, errors, inputs, outputs, logs, [], complete, diagnostic, inputsObserved: inputs.Length != 0);
        if (Receipt.Conclusion == WorkflowVerificationConclusion.CompileSucceeded && status.Outcome == WorkflowBuildOutcome.ProcessExited) {
            Fingerprint = WorkflowEvidenceRules.Digest(new { version = 1, task = Origin.Fingerprint, preparation = producer.PreparationFingerprint,
                runtime = runtime.Fingerprint, mcp = mcp.Fingerprint, gameplay = gameplay.Fingerprint, receipt = Receipt.Fingerprint }); ready = true;
        }
        return Receipt;
    }
    internal void RequireCurrent()
    {
        if (System.Environment.CurrentManagedThreadId != thread) throw new IOException("workflow_compiled_product_thread");
        if (closed || !ready) throw new IOException("workflow_compiled_product_not_ready");
        producer.RequireCurrent(); runtime.RequireCurrent(); mcp.RequireCurrent(); gameplay.RequireCurrent();
    }
    internal WorkflowVerificationTask CreateTests(string recipe)
    {
        RequireCurrent(); string prefix = "compiled/" + Origin.Binding.JobId.ToString("N") + "/";
        var files = runtime.CopyFiles().Select(f => f with { Path = prefix + "runtime/" + f.Path });
        if (recipe == "workflow-host-v1") files = files.Concat(mcp.CopyFiles().Select(f => f with { Path = prefix + "mcp/" + f.Path }))
            .Concat(gameplay.CopyFiles().Select(f => f with { Path = prefix + "gameplay/" + f.Path }));
        return new(Origin.Binding with { JobId = Guid.NewGuid(), RecipeId = recipe }, files, WorkflowTestInventory.Copy(recipe));
    }
    internal IDisposable Borrow()
    {
        RequireCurrent(); if (borrowers == WorkflowBuildJobs.MaxJobs) throw new IOException("workflow_compiled_borrow_budget"); borrowers++; return new Borrower(this);
    }
    internal void CopyHostFile(string group, string relative, Stream destination, byte[] buffer)
    {
        if (System.Environment.CurrentManagedThreadId != thread || closed || !ready || borrowers == 0) throw new IOException("workflow_compiled_copy_not_retained");
        (group switch { "mcp" => mcp, "gameplay" => gameplay, _ => throw new IOException("workflow_compiled_copy_group") }).CopyLocked(relative, destination, buffer);
    }
    internal WorkflowVerificationTask CreateNativeTask(WorkflowNativeSelection selection)
    {
        RequireCurrent(); string prefix = "compiled/" + Origin.Binding.JobId.ToString("N") + "/runtime/";
        return new(Origin.Binding with { JobId = Guid.NewGuid(), RecipeId = "workflow-native-prepare-v1" },
            runtime.CopyFiles().Select(f => f with { Path = prefix + f.Path }).Concat(selection.CopyFiles()).Append(selection.Descriptor()), []);
    }
    private sealed class Borrower(WorkflowCompiledProduct owner) : IDisposable
    {
        private bool released;
        public void Dispose() {
            if (System.Environment.CurrentManagedThreadId != owner.thread) throw new IOException("workflow_compiled_borrow_thread");
            if (!released) { owner.borrowers--; released = true; }
        }
    }
    public void Dispose()
    {
        if (System.Environment.CurrentManagedThreadId != thread) throw new IOException("workflow_compiled_product_thread");
        if (closed) return; if (borrowers != 0) throw new IOException("workflow_compiled_product_borrowed");
        gameplay.Dispose(); mcp.Dispose(); runtime.Dispose(); closed = true;
    }
}
