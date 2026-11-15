namespace Ncma.Editor.Services;

// Production offline-restore lease. Uses the ORIGINAL preparation bridge, queue,
// single-use permit and output owner. Local host retains this object before Begin.
// Build/test/installation require their own approvals and are never chained here.
internal sealed class WorkflowOfflineLease : IWorkflowRestoreProducer
{
    private readonly int thread = System.Environment.CurrentManagedThreadId;
    private readonly WorkflowCaptureBridge capture;
    private readonly WorkflowOfflineFeed feed;
    private readonly WorkflowBuildOutput output;
    private readonly WorkflowJobProcess process;
    private WorkflowToolchain? toolchain;
    private WorkflowRestoreProduct? product;
    private bool begun, closed;
    internal string PreparationFailure { get; private set; } = "";
    public Guid LeaseId { get; } = Guid.NewGuid();
    public WorkflowVerificationTask Task { get; }
    public WorkflowBuildEnvironment Environment { get; }
    internal WorkflowBuildOutput Output => output;
    internal string ApprovedCacheRoot => feed.Root;
    internal bool SdkStarted => process.SdkStarted;
    WorkflowBuildOutput IWorkflowRestoreProducer.Output => Output;
    bool IWorkflowRestoreProducer.SdkStarted => SdkStarted;
    public string PreparationFingerprint {
        get { RequireCurrent(); return WorkflowEvidenceRules.Digest(new { task = Task.Fingerprint, environment = Environment.Fingerprint,
            toolchain = toolchain!.Fingerprint, feed = feed.Fingerprint, capture = capture.ReadPreparedFacts() }); }
    }
    internal bool Ready {
        get { Owner(); return begun && !closed && PreparationFailure.Length == 0 && capture.Snapshot().Ready; }
    }
    internal WorkflowOfflineLease(WorkflowVerificationTask task, WorkflowBuildEnvironment environment, string approvedCacheRoot)
    {
        if (task.Recipe.Kind != WorkflowVerificationKind.OfflineRestore) throw new IOException("workflow_offline_recipe_required");
        Task = task; Environment = environment;
        capture = new(task, environment); feed = new(task, approvedCacheRoot); output = new(this); process = new(task, environment);
    }
    private void Owner() { if (System.Environment.CurrentManagedThreadId != thread) throw new IOException("workflow_offline_lease_thread"); }
    internal void BeginPreparation()
    {
        Owner(); if (begun || closed) throw new IOException("workflow_offline_lease_single_use"); begun = true;
        try {
            toolchain = new(Environment.ToolPath, Task.Binding.ToolSha256, Task.Binding.ToolVersion);
            feed.Prepare(); capture.BeginPreparation();
        } catch (Exception error) when (error is not OutOfMemoryException) { PreparationFailure = error.GetType().Name; throw; }
    }
    internal WorkflowCaptureBridgeStatus CaptureStatus() { Owner(); return capture.Snapshot(); }
    internal WorkflowRestoreProduct SealProduct(WorkflowBuildStatus status)
    {
        Owner(); if (product is not null) throw new IOException("workflow_restore_seal_single_use");
        product = new(this); // publish ownership BEFORE any file handles are acquired
        product.Prepare(status); return product;
    }
    public void RequireCurrent()
    {
        Owner(); if (!Ready) throw new IOException("workflow_offline_lease_not_ready");
        _ = capture.ReadPreparedFacts(); _ = toolchain!.CopyFiles(); feed.RequirePrepared();
    }
    public void Start(WorkflowBuildStartPermit permit)
    {
        RequireCurrent(); output.Reserve(permit); feed.Stage(output); feed.RequireStaged(output);
        process.Begin(output, toolchain!, capture);
    }
    public void RequestStop() { Owner(); process.RequestStop(); }
    internal void CancelPreparation() { Owner(); capture.RequestClose(); }
    public WorkflowBuildObservation Observe()
    {
        Owner(); var observation = process.Observe();
        if (observation.BuildStarted && observation.ExecutionDrained && observation.FailureCode.Length == 0) {
            try { feed.RequireStaged(output); }
            catch (Exception error) when (error is not OutOfMemoryException) { observation = observation with { FailureCode = error.GetType().Name }; }
        }
        return observation;
    }
    public void Close()
    {
        Owner(); if (closed) return;
        if (!Observe().ExecutionDrained) throw new IOException("workflow_offline_lease_not_drained");
        product?.Dispose();
        output.Close(); process.CloseDrained();
        if (!capture.TryClose()) throw new IOException("workflow_offline_capture_closing");
        feed.Dispose(); toolchain?.Dispose(); closed = true;
    }
}
