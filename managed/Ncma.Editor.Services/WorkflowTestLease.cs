namespace Ncma.Editor.Services;

// Actual fixed test process reuses the ORIGINAL queue, permit, Job transport,
// capture pins and output owner. No test is launched by preparation/review.
internal sealed class WorkflowTestLease : IWorkflowBuildLease
{
    private readonly int thread = System.Environment.CurrentManagedThreadId;
    private readonly WorkflowCompiledProduct build;
    private readonly WorkflowNativeLease? native;
    private readonly WorkflowHostStaging? staging;
    private bool staged;
    private IDisposable? borrowed;
    private readonly WorkflowCaptureBridge capture;
    private readonly WorkflowBuildOutput output;
    private readonly WorkflowJobProcess process;
    private readonly WorkflowPhysicalPins pins = new();
    private WorkflowToolchain? tools;
    private bool begun, prepared, closed, evidenceAttempted;
    public Guid LeaseId { get; } = Guid.NewGuid();
    public WorkflowVerificationTask Task { get; }
    public WorkflowBuildEnvironment Environment { get; }
    internal bool Ready => !closed && prepared && capture.Snapshot().Ready;
    public string PreparationFingerprint {
        get { RequireCurrent(); return WorkflowEvidenceRules.Digest(new { task = Task.Fingerprint, build = build.Fingerprint,
            native = native?.ProductFingerprint, staging = staging?.Fingerprint,
            tools = tools!.Fingerprint, environment = Environment.Fingerprint, capture = capture.ReadPreparedFacts(),
            request = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(WorkflowTestRequestCodec.Encode(Task))) }); }
    }
    internal WorkflowTestLease(WorkflowCompiledProduct build, string recipe) : this(build, recipe, null) { }
    internal WorkflowTestLease(WorkflowNativeLease native) : this(native.Build, "workflow-host-v1", native) { }
    private WorkflowTestLease(WorkflowCompiledProduct build, string recipe, WorkflowNativeLease? native)
    {
        if ((recipe == "workflow-host-v1") != (native is not null)) throw new IOException("workflow_host_requires_live_native_product");
        this.build = build; this.native = native; Task = native?.CreateHostTask() ?? build.CreateTests(recipe); Environment = build.Environment;
        staging = native is null ? null : new(Task, Environment.WorkspaceRoot);
        capture = new(Task, Environment); output = new(this); process = new(Task, Environment);
        if (!string.Equals(WorkflowTestLayout.RuntimeRoot(Task, Environment.WorkspaceRoot), build.RuntimeRoot, StringComparison.OrdinalIgnoreCase))
            throw new IOException("workflow_test_runtime_binding");
        borrowed = native is null ? build.Borrow() : native.BorrowProduct(); // transitively retains the same managed producer
    }
    private void Owner() { if (System.Environment.CurrentManagedThreadId != thread) throw new IOException("workflow_test_lease_thread"); }
    internal void BeginPreparation()
    {
        Owner(); if (begun || closed) throw new IOException("workflow_test_lease_single_use"); begun = true;
        build.RequireCurrent(); native?.RequireProduct(); tools = new(Environment.ToolPath, Task.Binding.ToolSha256, Task.Binding.ToolVersion);
        prepared = true; capture.BeginPreparation();
    }
    internal WorkflowCaptureBridgeStatus CaptureStatus() { Owner(); return capture.Snapshot(); }
    internal void CancelPreparation() { Owner(); capture.RequestClose(); }
    public void RequireCurrent()
    {
        Owner(); if (!Ready || borrowed is null) throw new IOException("workflow_test_lease_not_ready");
        build.RequireCurrent(); native?.RequireProduct(); if (staged) staging!.RequireCurrent();
        _ = tools!.CopyFiles(); _ = capture.ReadPreparedFacts();
    }
    public void Start(WorkflowBuildStartPermit permit)
    {
        RequireCurrent(); output.Reserve(permit);
        if (staging is not null) { staging.Prepare(output, build, native!); staged = true; staging.RequireCurrent(); }
        byte[] request = WorkflowTestRequestCodec.Encode(Task);
        string path = Path.Combine(output.Command.OutputRoot, "test-request.json");
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) {
            WorkflowPhysicalPins.ValidateHandle(file.SafeFileHandle, false); file.Write(request); file.Flush(true);
        }
        pins.File(path, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(request)), request.Length);
        process.Begin(output, tools!, capture);
    }
    public void RequestStop() { Owner(); process.RequestStop(); }
    public WorkflowBuildObservation Observe()
    {
        Owner(); var state = process.Observe();
        if (state.BuildStarted && state.ExecutionDrained && state.FailureCode.Length == 0) {
            try { build.RequireCurrent(); native?.RequireProduct(); if (staging is not null) staging.RequireCurrent(); }
            catch (Exception error) when (error is not OutOfMemoryException) { return state with { FailureCode = WorkflowObservedEvidence.Code(error) }; }
        }
        return state;
    }
    internal WorkflowVerificationReceipt CaptureReceipt(WorkflowBuildStatus status)
    {
        Owner(); if (evidenceAttempted || closed) throw new IOException("workflow_test_evidence_single_use");
        var actual = Observe(); var outcome = WorkflowObservedEvidence.Outcome(Task, status, actual);
        evidenceAttempted = true;
        WorkflowEvidenceFile[] inputs = [], products = [], logs = [];
        var cases = Task.CopyTestInventory().Select(id => new WorkflowCaseEvidence(id, WorkflowCaseOutcome.NotRun, "case_not_started")).ToArray();
        bool complete = false; string diagnostic = "test_not_started";
        if (actual.BuildStarted) {
            diagnostic = actual.FailureCode.Length == 0 ? "" : "execution_evidence_incomplete";
            bool observed = actual.FailureCode.Length == 0;
            void Failed(Exception error) { observed = false; if (diagnostic.Length == 0) diagnostic = WorkflowObservedEvidence.Code(error); }
            // Independent observations retain executed case prefixes even when
            // inputs become stale or log sealing fails. Neither failure can pass.
            try { RequireCurrent(); inputs = Task.CopyInputs(); }
            catch (Exception error) when (error is not OutOfMemoryException) { Failed(error); }
            try { logs = output.SealLogs(); }
            catch (Exception error) when (error is not OutOfMemoryException) { Failed(error); }
            try {
                string path = Path.Combine(output.Command.OutputRoot, "test-results.jsonl");
                var hash = pins.Observe(path, WorkflowTestJournal.MaximumBytes);
                var report = WorkflowTestJournal.Read(Task, pins.ReadFile(path, hash.Sha256, hash.Bytes, WorkflowTestJournal.MaximumBytes));
                products = [new("test-results.jsonl", hash.Bytes, hash.Sha256)]; cases = report.Cases;
                complete = report.Complete && observed;
                if (diagnostic.Length == 0) diagnostic = report.Diagnostic;
            } catch (Exception error) when (error is not OutOfMemoryException) { Failed(error); }
        }
        return new(Task, Task.Fingerprint, outcome, actual.ExitCode, 0, cases.Count(c => c.Outcome == WorkflowCaseOutcome.Failed), inputs, products, logs, cases,
            complete, diagnostic, inputsObserved: inputs.Length != 0);
    }
    public void Close()
    {
        Owner(); if (closed) return;
        if (!Observe().ExecutionDrained) throw new IOException("workflow_test_lease_not_drained");
        output.Close(); process.CloseDrained();
        if (!capture.TryClose()) throw new IOException("workflow_test_capture_closing");
        staging?.Dispose(); pins.Dispose(); tools?.Dispose(); borrowed?.Dispose(); borrowed = null; closed = true;
    }
}
