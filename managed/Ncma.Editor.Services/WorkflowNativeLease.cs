namespace Ncma.Editor.Services;

// Original queue/permit/Job owner, NOT a second executor or installer.
internal sealed class WorkflowNativeLease : IWorkflowBuildLease
{
    private readonly int thread = System.Environment.CurrentManagedThreadId;
    internal WorkflowCompiledProduct Build { get; }
    private IDisposable? borrowed;
    private readonly WorkflowCaptureBridge capture;
    private readonly WorkflowBuildOutput output;
    private readonly WorkflowJobProcess process;
    private readonly WorkflowPhysicalPins pins = new();
    private readonly WorkflowNativeToolchain nativeTools;
    private readonly WorkflowPinnedTree plugins;
    private WorkflowToolchain? tools;
    private bool begun, prepared, closed, sealing, productReady;
    private int borrowers;
    internal WorkflowNativeRequest Request { get; }
    internal WorkflowNativePlan Plan => Request.Plan;
    public Guid LeaseId { get; } = Guid.NewGuid();
    public WorkflowVerificationTask Task { get; }
    public WorkflowBuildEnvironment Environment { get; }
    internal string ProductFingerprint { get; private set; } = "";
    internal WorkflowVerificationReceipt? Receipt { get; private set; }
    internal bool Ready => !closed && prepared && capture.Snapshot().Ready;
    public string PreparationFingerprint {
        get { RequireCurrent(); return WorkflowEvidenceRules.Digest(new { task = Task.Fingerprint, build = Build.Fingerprint,
            tools = tools!.Fingerprint, environment = Environment.Fingerprint, capture = capture.ReadPreparedFacts(),
            nativeRequest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Request.Encode())) }); }
    }
    internal WorkflowNativeLease(WorkflowCompiledProduct build, WorkflowNativeSelection selection)
    {
        Build = build; Task = build.CreateNativeTask(selection); Environment = build.Environment;
        Request = new(Task, Environment.WorkspaceRoot, selection); Request.Validate();
        capture = new(Task, Environment); output = new(this); process = new(Task, Environment);
        nativeTools = new(selection); plugins = new(Plan.PluginRoot);
        borrowed = build.Borrow(); // final acquisition, host owns before Prepare
    }
    private void Owner() { if (System.Environment.CurrentManagedThreadId != thread) throw new IOException("workflow_native_lease_thread"); }
    internal void BeginPreparation()
    {
        Owner(); if (begun || closed) throw new IOException("workflow_native_lease_single_use"); begun = true;
        Build.RequireCurrent(); tools = new(Environment.ToolPath, Task.Binding.ToolSha256, Task.Binding.ToolVersion);
        nativeTools.Prepare(); prepared = true; capture.BeginPreparation();
    }
    internal WorkflowCaptureBridgeStatus CaptureStatus() { Owner(); return capture.Snapshot(); }
    internal void CancelPreparation() { Owner(); capture.RequestClose(); }
    public void RequireCurrent()
    {
        Owner(); if (!Ready || borrowed is null) throw new IOException("workflow_native_lease_not_ready");
        Build.RequireCurrent(); nativeTools.RequireCurrent(); _ = tools!.CopyFiles(); _ = capture.ReadPreparedFacts();
    }
    public void Start(WorkflowBuildStartPermit permit)
    {
        RequireCurrent(); output.Reserve(permit); byte[] bytes = Request.Encode();
        string path = Path.Combine(output.Command.OutputRoot, "native-request.json");
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) {
            WorkflowPhysicalPins.ValidateHandle(file.SafeFileHandle, false); file.Write(bytes); file.Flush(true);
        }
        pins.File(path, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), bytes.Length);
        process.Begin(output, tools!, capture);
    }
    public void RequestStop() { Owner(); process.RequestStop(); }
    public WorkflowBuildObservation Observe()
    {
        Owner(); var state = process.Observe();
        if (state.BuildStarted && state.ExecutionDrained && state.FailureCode.Length == 0) {
            try { Build.RequireCurrent(); nativeTools.RequireCurrent(); }
            catch (Exception error) when (error is not OutOfMemoryException) { return state with { FailureCode = WorkflowObservedEvidence.Code(error) }; }
        }
        return state;
    }
    internal WorkflowVerificationReceipt Seal(WorkflowBuildStatus status)
    {
        Owner(); if (sealing || closed) throw new IOException("workflow_native_evidence_single_use");
        var actual = Observe(); var outcome = WorkflowObservedEvidence.Outcome(Task, status, actual); sealing = true;
        WorkflowEvidenceFile[] inputs = [], products = [], logs = []; bool complete = actual.FailureCode.Length == 0;
        string diagnostic = complete ? "" : "execution_evidence_incomplete";
        void Failed(Exception error) { complete = false; if (diagnostic.Length == 0) diagnostic = WorkflowObservedEvidence.Code(error); }
        if (actual.BuildStarted) {
            try { RequireCurrent(); inputs = Task.CopyInputs(); } catch (Exception error) when (error is not OutOfMemoryException) { Failed(error); }
            try { logs = output.SealLogs(); } catch (Exception error) when (error is not OutOfMemoryException) { Failed(error); }
            try {
                string path = Path.Combine(Plan.OutputRoot, "native-result.json"); var hash = pins.Observe(path, 16384);
                var result = WorkflowNativeResult.Read(Task, pins.ReadFile(path, hash.Sha256, hash.Bytes, 16384));
                products = [new("native-result.json", hash.Bytes, hash.Sha256)];
                if (!result.Complete) { complete = false; if (diagnostic.Length == 0) diagnostic = result.Diagnostic.Length == 0 ? "native_build_incomplete" : result.Diagnostic; }
            } catch (Exception error) when (error is not OutOfMemoryException) { Failed(error); }
            try {
                string path = Path.Combine(Plan.NativeRoot, "CMakeCache.txt"); var hash = pins.Observe(path, 4194304);
                Plan.VerifyCache(pins.ReadFile(path, hash.Sha256, hash.Bytes, 4194304));
                products = [.. products, new("CMakeCache.txt", hash.Bytes, hash.Sha256)];
                plugins.Prepare(); products = [.. products, .. plugins.CopyFiles()];
            } catch (Exception error) when (error is not OutOfMemoryException) { Failed(error); }
        }
        Receipt = new(Task, Task.Fingerprint, outcome, actual.ExitCode, null, null, inputs, products, logs, [], complete, diagnostic, inputsObserved: inputs.Length != 0);
        if (Receipt.Conclusion == WorkflowVerificationConclusion.NativeInputsPrepared && status.Outcome == WorkflowBuildOutcome.ProcessExited) {
            ProductFingerprint = WorkflowEvidenceRules.Digest(new { task = Task.Fingerprint, receipt = Receipt.Fingerprint, plugins = plugins.Fingerprint }); productReady = true;
        }
        return Receipt;
    }
    internal void RequireProduct() { RequireCurrent(); if (!productReady) throw new IOException("workflow_native_product_not_ready"); plugins.RequireCurrent(); }
    internal WorkflowEvidenceFile[] CopyProducts() { RequireProduct(); return plugins.CopyFiles(); }
    internal WorkflowVerificationTask CreateHostTask()
    {
        RequireProduct(); var task = Build.CreateTests("workflow-host-v1");
        string prefix = "native/" + Task.Binding.JobId.ToString("N") + "/plugins/";
        return new(task.Binding, task.CopyInputs().Concat(plugins.CopyFiles().Select(f => f with { Path = prefix + f.Path })), task.CopyTestInventory());
    }
    internal void CopyPluginFile(string relative, Stream destination, byte[] buffer)
    {
        Owner(); if (closed || !productReady || borrowers == 0) throw new IOException("workflow_native_copy_not_retained");
        plugins.CopyLocked(relative, destination, buffer);
    }
    internal IDisposable BorrowProduct() {
        RequireProduct(); if (borrowers == WorkflowBuildJobs.MaxJobs) throw new IOException("workflow_native_borrow_budget"); borrowers++; return new Borrower(this);
    }
    private sealed class Borrower(WorkflowNativeLease owner) : IDisposable {
        private bool released;
        public void Dispose() { owner.Owner(); if (!released) { owner.borrowers--; released = true; } }
    }
    public void Close()
    {
        Owner(); if (closed) return;
        if (borrowers != 0) throw new IOException("workflow_native_product_borrowed");
        if (!Observe().ExecutionDrained) throw new IOException("workflow_native_lease_not_drained");
        output.Close(); process.CloseDrained(); if (!capture.TryClose()) throw new IOException("workflow_native_capture_closing");
        plugins.Dispose(); pins.Dispose(); nativeTools.Dispose(); tools?.Dispose(); borrowed?.Dispose(); borrowed = null; closed = true;
    }
}
