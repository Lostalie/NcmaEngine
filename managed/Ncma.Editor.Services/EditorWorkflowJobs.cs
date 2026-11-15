using System.Text.Json;
using Ncma.Editor.Core;

namespace Ncma.Editor.Services;

public sealed record WorkflowLocalJobView(Guid JobId, Guid WorkflowId, string Recipe, string Phase,
    string Outcome, bool Prepared, bool Started, bool Drained, bool Retained, string Diagnostic, string ProductFingerprint,
    string EvidenceConclusion, string EvidenceFingerprint, bool SourceCurrent, string PlanHash,
    string StopDiagnostic, string CloseDiagnostic, bool Abandoned = false);

// Opaque local UI review. Serialized display text or a matching hash cannot
// recreate this token; the original queue still consumes its own exact ticket.
public sealed class WorkflowLocalJobReview
{
    internal WorkflowBuildReview Original { get; }
    public Guid JobId => Original.Task.Binding.JobId;
    public string Fingerprint => Original.Fingerprint;
    public string Display { get; }
    internal WorkflowLocalJobReview(WorkflowBuildReview original, string cacheRoot, WorkflowNativePlan? native = null)
    {
        Original = original;
        Display = JsonSerializer.Serialize(new { version = 1, original.Fingerprint, original.LeaseId,
            binding = original.Task.Binding, inputs = original.Task.CopyInputs(), testInventory = original.Task.CopyTestInventory(), original.Task.InventorySha256,
            workspace = original.Environment.WorkspaceRoot, cacheRoot, tool = original.Environment.ToolPath,
            command = new WorkflowBuildCommand(original.Task, original.Environment).CopyArguments(),
            nugetConfig = original.RestoreAuthorized ? WorkflowBuildCommand.OfflineNugetConfig : null,
            runner = original.Environment.RunnerPath, runnerSha256 = original.Environment.RunnerSha256,
            helpers = original.Environment.CopyHelpers(), original.PreparationFingerprint,
            original.CommandFingerprint, original.OutputRoot, timeoutSeconds = original.Environment.Timeout.TotalSeconds,
            approvalSeconds = original.ValidFor.TotalSeconds, original.RestoreAuthorized,
            networkAuthorized = false, buildAuthorized = original.Task.Recipe.Kind == WorkflowVerificationKind.Compile,
            nativeBuildAuthorized = original.Task.Recipe.Kind == WorkflowVerificationKind.NativePrepare,
            nativePreparation = native is null ? null : new { native.SourceRoot, native.NativeRoot, native.PluginRoot,
                native.CMake, native.Selection.VisualStudioRoot, native.Selection.ToolsetVersion, native.Selection.WindowsSdkRoot, native.Selection.WindowsSdkVersion,
                tools = native.Selection.CopyFiles(), includes = native.Selection.IncludeDirectories(), libraries = native.Selection.LibraryDirectories(),
                configure = native.ConfigureArguments(), build = native.BuildArguments(), environment = native.EnvironmentOverrides(),
                warning = "Trusted installed SDK headers/libraries/support DLLs are NOT a hermetic byte snapshot. This prepares dependencies, not a host-test pass." },
            testAuthorized = original.Task.Recipe.Kind == WorkflowVerificationKind.Tests, installationAuthorized = false,
            hostStaging = original.Task.Recipe.Id == "workflow-host-v1" ? WorkflowHostStaging.Map(original.Task, original.Environment.WorkspaceRoot) : null,
            warning = "Local trusted MSBuild only; NOT an OS sandbox. No implicit build/test/download/install. Raw logs remain local. Cancellation may wait for SDK natural exit; no forced termination. Explicit abandonment releases only the scheduling slot; old processes may continue and resources remain owned until drain." });
    }
}

// Sole UI owner of the original queue and preparation leases. No MCP entry,
// model credentials, process command text, or independent approval ledger.
public sealed class EditorWorkflowJobs
{
    private readonly int thread = System.Environment.CurrentManagedThreadId;
    private readonly EditorSessionOwner owner;
    private readonly WorkflowBuildJobs queue;
    private readonly List<Entry> entries = [];
    private readonly List<WorkflowSourceReview> sourceReviews = [];
    private readonly List<WorkflowSourceCaptureJob> sourceCaptures = [];
    private bool closing;
    private sealed class Entry(IWorkflowBuildLease lease)
    {
        internal readonly IWorkflowBuildLease Lease = lease;
        internal WorkflowRestoreProduct? Restore;
        internal WorkflowCompiledProduct? Compiled;
        internal WorkflowVerificationReceipt? Receipt;
        internal bool Ready => Lease switch { WorkflowOfflineLease value => value.Ready, WorkflowCompileLease value => value.Ready, WorkflowTestLease value => value.Ready, WorkflowNativeLease value => value.Ready, _ => false };
        internal string CacheRoot => Lease is WorkflowOfflineLease value ? value.ApprovedCacheRoot : "";
        internal void Begin() {
            if (Lease is WorkflowOfflineLease offline) offline.BeginPreparation();
            else if (Lease is WorkflowCompileLease compile) compile.BeginPreparation();
            else if (Lease is WorkflowNativeLease native) native.BeginPreparation();
            else ((WorkflowTestLease)Lease).BeginPreparation();
        }
        internal WorkflowCaptureBridgeStatus Preparation() => Lease switch {
            WorkflowOfflineLease offline => offline.CaptureStatus(), WorkflowCompileLease compile => compile.CaptureStatus(),
            WorkflowNativeLease native => native.CaptureStatus(),
            WorkflowTestLease test => test.CaptureStatus(), _ => throw new IOException("workflow_lease_kind") };
        internal void CancelPreparation() {
            if (Lease is WorkflowOfflineLease offline) offline.CancelPreparation();
            else if (Lease is WorkflowCompileLease compile) compile.CancelPreparation();
            else if (Lease is WorkflowNativeLease native) native.CancelPreparation();
            else ((WorkflowTestLease)Lease).CancelPreparation();
        }
        internal WorkflowLocalJobReview? Review;
        internal WorkflowBuildStatus? Status;
        internal string Diagnostic = "", ProductFingerprint = "", PreparationStopDiagnostic = "", PreparationCloseDiagnostic = "";
        internal bool Registered, Closed, SealAttempted, AuditLinked, PreparationStopRequested;
    }
    internal EditorWorkflowJobs(EditorSessionOwner owner, TimeProvider? time)
    {
        this.owner = owner; queue = new(IsSourceCurrent, time);
    }
    private void Owner()
    {
        if (System.Environment.CurrentManagedThreadId != thread) throw new EditRejectedException("workflow_local_thread");
        _ = owner.Edit;
    }
    internal bool IsSourceCurrent(WorkflowVerificationBinding binding)
    {
        Owner();
        return !closing && owner.Play is null && owner.Workflows?.IsVerificationSourceCurrent(binding) == true &&
            (binding.ReviewBundleSha256 is null || sourceReviews.Any(r => !r.Revoked && r.Consumed && r.CaptureId == binding.CaptureId &&
                r.WorkflowId == binding.WorkflowId && r.PlanHash == binding.PlanHash && r.Source == binding.Source && r.Assets == binding.Assets && r.BundleSha256 == binding.ReviewBundleSha256));
    }
    private Entry Find(Guid id) => entries.Find(e => e.Lease.Task.Binding.JobId == id) ?? throw new EditRejectedException("workflow_local_job_missing");
    public bool CanPrepare(Guid workflowId)
    {
        Owner(); if (closing || owner.Play is not null || entries.Count == WorkflowBuildJobs.MaxJobs || owner.Edit is not { } edit) return false;
        var plan = owner.Workflows!.Capture().SingleOrDefault(p => p.WorkflowId == workflowId);
        return plan is not null && owner.Workflows.IsVerificationSourceCurrent(workflowId, plan.Hash,
            new(edit.SessionId, edit.DocumentGeneration, edit.Revision));
    }
    public Guid PrepareOffline(string workspace, Guid captureId, string cacheRoot, Guid workflowId, string configuration, string variant)
        => PrepareOfflineCore(workspace, captureId, cacheRoot, workflowId, configuration, variant, null);
    public WorkflowSourceReview BindReviewedSource(Guid workflowId, string workspace, ReadOnlySpan<byte> reviewedBundle)
    {
        Owner();
        if (!CanPrepare(workflowId) || sourceReviews.Count == WorkflowBuildJobs.MaxJobs) throw new EditRejectedException("workflow_source_handoff_unavailable");
        var edit = owner.Edit!; var plan = owner.Workflows!.Capture().Single(p => p.WorkflowId == workflowId);
        var review = new WorkflowSourceReview(workflowId, workspace, plan.Hash, new(edit.SessionId, edit.DocumentGeneration, edit.Revision), reviewedBundle,
            owner.Workflows.VerificationAssetSource(workflowId));
        sourceReviews.Add(review); return review; // helper bytes observed; no execution or executable approval
    }
    public bool IsSourceReviewCurrent(WorkflowSourceReview review)
    {
        Owner(); return !closing && owner.Play is null && sourceReviews.Contains(review) && !review.Revoked &&
            owner.Workflows!.IsVerificationSourceCurrent(review.WorkflowId, review.PlanHash, review.Source) &&
            review.Assets == owner.Workflows.VerificationAssetSource(review.WorkflowId);
    }
    public void RevokeSourceReview(WorkflowSourceReview review)
    {
        Owner(); if (!sourceReviews.Contains(review)) throw new EditRejectedException("workflow_source_foreign_handoff");
        review.Revoked = true; // original queue/Pump performs stop; never implicit release/Undo
    }
    public bool CanPrepareSourceReview(WorkflowSourceReview review) => IsSourceReviewCurrent(review) && !review.Consumed;
    public bool CanCreateSourceCapture(WorkflowSourceReview review) => CanPrepareSourceReview(review) && !review.CaptureRequested &&
        sourceCaptures.Count < WorkflowBuildJobs.MaxJobs && sourceCaptures.All(c => c.Snapshot().Closed);
    public bool RequiresCreatedCapture(WorkflowSourceReview review) { Owner(); return sourceReviews.Contains(review) && review.CaptureRequested; }
    public Guid BeginSourceCapture(WorkflowSourceReview review, string exportedBundle, string gitExecutable)
    {
        Owner(); if (!CanCreateSourceCapture(review)) throw new EditRejectedException("workflow_source_creation_unavailable");
        review.CaptureRequested = true;
        var capture = new WorkflowSourceCaptureJob(review, exportedBundle, gitExecutable); sourceCaptures.Add(capture);
        capture.Begin(); return capture.Id; // ownership precedes every helper/file/process acquisition
    }
    private WorkflowSourceCaptureJob FindSourceCapture(Guid id) => sourceCaptures.Find(c => c.Id == id) ?? throw new EditRejectedException("workflow_source_capture_missing");
    public WorkflowSourceCaptureView[] SourceCaptures() { Owner(); return sourceCaptures.Select(c => c.Snapshot()).ToArray(); }
    public void CancelSourceCapture(Guid id) { Owner(); var capture = FindSourceCapture(id); capture.Review.Revoked = true; capture.RequestClose(); }
    public bool CloseSourceCapture(Guid id) { Owner(); return FindSourceCapture(id).TryClose(); }
    public bool CanPrepareCreatedOffline(Guid id)
    {
        Owner(); var capture = FindSourceCapture(id); return CanPrepareSourceReview(capture.Review) && capture.Snapshot().Ready;
    }
    public Guid PrepareCreatedOffline(Guid id, string cacheRoot, string configuration, string variant)
    {
        Owner(); var capture = FindSourceCapture(id); var review = capture.Review;
        if (!CanPrepareCreatedOffline(id)) throw new EditRejectedException("workflow_source_capture_not_ready");
        var created = capture.RequireReady();
        return PrepareReviewedOfflineCore(review, created.CaptureId, cacheRoot, configuration, variant, created);
    }
    public Guid PrepareReviewedOffline(WorkflowSourceReview review, Guid captureId, string cacheRoot, string configuration, string variant)
    {
        Owner(); if (review.CaptureRequested) throw new EditRejectedException("workflow_source_requires_created_capture");
        return PrepareReviewedOfflineCore(review, captureId, cacheRoot, configuration, variant, null);
    }
    private Guid PrepareReviewedOfflineCore(WorkflowSourceReview review, Guid captureId, string cacheRoot, string configuration, string variant, WorkflowCreatedSource? created)
    {
        Owner();
        if (!IsSourceReviewCurrent(review) || review.Consumed || captureId == Guid.Empty || sourceReviews.Any(r => r.CaptureId == captureId) || variant is not ("extensions" or "ai-extensions"))
            throw new EditRejectedException("workflow_source_handoff_stale");
        review.Consumed = true; // attempted preparation cannot replay a failed handoff
        return PrepareOfflineCore(review.Workspace, captureId, cacheRoot, review.WorkflowId, configuration, variant, review, created);
    }
    private Guid PrepareOfflineCore(string workspace, Guid captureId, string cacheRoot, Guid workflowId, string configuration, string variant, WorkflowSourceReview? review, WorkflowCreatedSource? created = null)
    {
        Owner(); if (closing || owner.Play is not null || entries.Count == WorkflowBuildJobs.MaxJobs)
            throw new EditRejectedException("workflow_local_preparation_unavailable");
        var plan = owner.Workflows!.Capture().SingleOrDefault(p => p.WorkflowId == workflowId) ?? throw new EditRejectedException("workflow_missing");
        var edit = owner.Edit!;
        // Reject stopped/stale plans BEFORE disk reads, helper startup or owners.
        var source = new EditorViewStamp(edit.SessionId, edit.DocumentGeneration, edit.Revision);
        if (!owner.Workflows!.IsVerificationSourceCurrent(workflowId, plan.Hash, source))
            throw new EditRejectedException("workflow_job_source_stale");
        var input = WorkflowLocalInputs.Offline(workspace, captureId, cacheRoot, workflowId, plan.Hash,
            new(edit.SessionId, edit.DocumentGeneration, edit.Revision), configuration, variant, review?.BundleSha256, created,
            owner.Workflows.VerificationAssetSource(workflowId));
        if (review is not null) review.CaptureId = captureId;
        var entry = new Entry(new WorkflowOfflineLease(input.Task, input.Environment, cacheRoot)); entries.Add(entry);
        try { entry.Begin(); }
        catch (Exception error) when (error is not OutOfMemoryException) { entry.Diagnostic = error.GetType().Name; throw; }
        return input.Task.Binding.JobId;
    }
    public Guid PrepareBuild(Guid restoreJob)
    {
        Owner(); var source = Find(restoreJob);
        if (closing || owner.Play is not null || entries.Count == WorkflowBuildJobs.MaxJobs || source.Closed || source.Restore is null ||
            source.Receipt?.Conclusion != WorkflowVerificationConclusion.RestorePrepared || !IsSourceCurrent(source.Lease.Task.Binding))
            throw new EditRejectedException("workflow_build_requires_current_live_restore");
        var entry = new Entry(new WorkflowCompileLease(source.Restore)); entries.Add(entry);
        try { entry.Begin(); }
        catch (Exception error) when (error is not OutOfMemoryException) { entry.Diagnostic = error.GetType().Name; throw; }
        return entry.Lease.Task.Binding.JobId;
    }
    public WorkflowLocalJobReview Review(Guid id)
    {
        Owner(); var entry = Find(id);
        if (closing || entry.Registered || entry.Closed || entry.Diagnostic.Length != 0) throw new EditRejectedException("workflow_local_review_unavailable");
        var review = queue.Review(entry.Lease); entry.Registered = true;
        entry.Review = new(review, entry.CacheRoot, (entry.Lease as WorkflowNativeLease)?.Plan); entry.Status = queue.Poll(id); return entry.Review;
    }
    public Guid PrepareTests(Guid buildJob, string recipe)
    {
        Owner();
        // Host tests must enter through the live native product, never a build ID alone.
        if (recipe == "workflow-host-v1") throw new EditRejectedException("workflow_host_requires_live_native_product");
        var source = Find(buildJob);
        if (closing || owner.Play is not null || entries.Count == WorkflowBuildJobs.MaxJobs || source.Closed || source.Compiled is null ||
            source.Receipt?.Conclusion != WorkflowVerificationConclusion.CompileSucceeded || !IsSourceCurrent(source.Lease.Task.Binding))
            throw new EditRejectedException("workflow_tests_require_current_live_build");
        var entry = new Entry(new WorkflowTestLease(source.Compiled, recipe)); entries.Add(entry);
        try { entry.Begin(); }
        catch (Exception error) when (error is not OutOfMemoryException) { entry.Diagnostic = WorkflowObservedEvidence.Code(error); throw; }
        return entry.Lease.Task.Binding.JobId;
    }
    public Guid PrepareNative(Guid buildJob, string visualStudioRoot, string toolsetVersion, string windowsSdkRoot, string windowsSdkVersion)
    {
        Owner(); var source = Find(buildJob);
        if (closing || owner.Play is not null || entries.Count == WorkflowBuildJobs.MaxJobs || source.Closed || source.Compiled is null ||
            source.Receipt?.Conclusion != WorkflowVerificationConclusion.CompileSucceeded || !IsSourceCurrent(source.Lease.Task.Binding))
            throw new EditRejectedException("workflow_native_requires_current_live_build");
        var selection = WorkflowNativeSelection.Observe(visualStudioRoot, toolsetVersion, windowsSdkRoot, windowsSdkVersion);
        var entry = new Entry(new WorkflowNativeLease(source.Compiled, selection)); entries.Add(entry);
        try { entry.Begin(); }
        catch (Exception error) when (error is not OutOfMemoryException) { entry.Diagnostic = WorkflowObservedEvidence.Code(error); throw; }
        return entry.Lease.Task.Binding.JobId;
    }
    public Guid PrepareHostTests(Guid nativeJob)
    {
        Owner(); var source = Find(nativeJob);
        if (closing || owner.Play is not null || entries.Count == WorkflowBuildJobs.MaxJobs || source.Closed ||
            source.Lease is not WorkflowNativeLease native || source.Receipt?.Conclusion != WorkflowVerificationConclusion.NativeInputsPrepared || !IsSourceCurrent(source.Lease.Task.Binding))
            throw new EditRejectedException("workflow_host_requires_current_live_native_product");
        var entry = new Entry(new WorkflowTestLease(native)); entries.Add(entry);
        try { entry.Begin(); }
        catch (Exception error) when (error is not OutOfMemoryException) { entry.Diagnostic = WorkflowObservedEvidence.Code(error); throw; }
        return entry.Lease.Task.Binding.JobId;
    }
    public WorkflowVerificationReceipt CaptureEvidence(Guid id)
    {
        Owner(); var entry = Find(id);
        if (entry.Receipt is { } receipt) { LinkEvidence(entry); return receipt; } // immutable retained observation; no re-run/rebaseline
        if (entry.Closed || entry.SealAttempted || !entry.Registered) throw new EditRejectedException("workflow_evidence_unavailable");
        var status = queue.Poll(id);
        _ = WorkflowObservedEvidence.Outcome(entry.Lease.Task, status, entry.Lease.Observe());
        entry.SealAttempted = true;
        try {
            if (entry.Lease is WorkflowOfflineLease offline) {
                entry.Restore = offline.SealProduct(status); entry.Receipt = entry.Restore.Receipt!;
                entry.ProductFingerprint = entry.Restore.Fingerprint;
            } else if (entry.Lease is WorkflowCompileLease compile) {
                entry.Compiled = compile.SealProduct(status); entry.Receipt = entry.Compiled.Receipt!;
                entry.ProductFingerprint = entry.Compiled.Fingerprint;
            } else if (entry.Lease is WorkflowTestLease test) entry.Receipt = test.CaptureReceipt(status);
            else if (entry.Lease is WorkflowNativeLease native) { entry.Receipt = native.Seal(status); entry.ProductFingerprint = native.ProductFingerprint; }
            else throw new EditRejectedException("workflow_evidence_recipe");
            LinkEvidence(entry); return entry.Receipt;
        } catch (Exception error) when (error is not OutOfMemoryException) { entry.Diagnostic = WorkflowObservedEvidence.Code(error); throw; }
    }
    private void LinkEvidence(Entry entry)
    {
        if (entry.AuditLinked) return;
        owner.Workflows!.RecordVerification(entry.Lease.Task, entry.Receipt!); entry.AuditLinked = true;
    }
    public WorkflowVerificationReceipt? Evidence(Guid id) { Owner(); return Find(id).Receipt; }
    public void Approve(WorkflowLocalJobReview review, string fingerprint, bool confirmed)
    {
        Owner(); var entry = Find(review.JobId);
        if (!ReferenceEquals(entry.Review, review)) throw new EditRejectedException("workflow_local_foreign_review");
        entry.Review = null;
        queue.Approve(review.Original, fingerprint, confirmed);
    }
    public bool StartApproved() { Owner(); return queue.StartNext(); }
    public void Pump()
    {
        Owner();
        foreach (var capture in sourceCaptures) if (!capture.Snapshot().Closed && !IsSourceReviewCurrent(capture.Review)) capture.RequestClose();
        foreach (var entry in entries) {
            if (entry.Closed) continue;
            if (entry.Registered) entry.Status = queue.Poll(entry.Lease.Task.Binding.JobId);
            else {
                if (!entry.PreparationStopRequested && !IsSourceCurrent(entry.Lease.Task.Binding)) {
                    entry.Review = null;
                    if (entry.Diagnostic.Length == 0) entry.Diagnostic = "workflow_job_source_stale";
                    StopPreparation(entry); // signal once; keep owner until explicit drained close
                }
                var preparation = entry.Preparation();
                if (preparation.FailureType.Length != 0 && entry.Diagnostic.Length == 0) entry.Diagnostic = preparation.FailureType;
            }
        }
    }
    public void Cancel(Guid id)
    {
        Owner(); var entry = Find(id); entry.Review = null;
        if (entry.Registered) queue.Cancel(id);
        else { entry.Diagnostic = "preparation_cancelled"; if (!entry.PreparationStopRequested) StopPreparation(entry); }
    }
    public void Abandon(Guid id)
    {
        Owner(); var entry = Find(id);
        if (!entry.Registered || entry.Closed) throw new EditRejectedException("workflow_local_abandon_unavailable");
        queue.Abandon(id); entry.Review = null;
        entry.Status = queue.Snapshot().Single(s => s.JobId == id);
    }
    private static void StopPreparation(Entry entry)
    {
        entry.PreparationStopRequested = true;
        try { entry.CancelPreparation(); }
        catch (Exception error) when (error is not OutOfMemoryException) {
            if (entry.PreparationStopDiagnostic.Length == 0) entry.PreparationStopDiagnostic = WorkflowObservedEvidence.Code(error);
        }
    }
    public void RetryStop(Guid id)
    {
        Owner(); var entry = Find(id);
        if (entry.Closed) throw new EditRejectedException("workflow_local_stop_unavailable");
        if (entry.Registered) { queue.RetryStop(id); entry.Status = queue.Poll(id); }
        else if (entry.PreparationStopRequested && entry.PreparationStopDiagnostic.Length != 0) StopPreparation(entry);
        else throw new EditRejectedException("workflow_local_stop_unavailable"); // explicit only; no automatic retry in Pump
    }
    // Explicit post-exit operation; no automatic success receipt from exit=0.
    public string SealRestore(Guid id)
    {
        Owner(); var entry = Find(id);
        if (entry.Lease is not WorkflowOfflineLease) throw new EditRejectedException("workflow_local_seal_unavailable");
        if (CaptureEvidence(id).Conclusion != WorkflowVerificationConclusion.RestorePrepared)
            throw new EditRejectedException("workflow_restore_product_not_successful");
        return entry.ProductFingerprint;
    }
    public bool Close(Guid id)
    {
        Owner(); var entry = Find(id); if (entry.Closed) return true;
        entry.Review = null;
        try {
            if (entry.Registered) {
                queue.Cancel(id); entry.Status = queue.Poll(id);
                if (!entry.Status.ExecutionDrained) return false;
                entry.Closed = queue.Close(id); entry.Status = queue.Snapshot().Single(s => s.JobId == id);
            } else { entry.Lease.Close(); entry.Closed = true; }
            return entry.Closed;
        } catch (Exception error) when (error is not OutOfMemoryException) {
            if (entry.PreparationCloseDiagnostic.Length == 0) entry.PreparationCloseDiagnostic = WorkflowObservedEvidence.Code(error); return false;
        }
    }
    public WorkflowLocalJobView[] Capture()
    {
        Owner();
        return entries.Select(e => new WorkflowLocalJobView(e.Lease.Task.Binding.JobId, e.Lease.Task.Binding.WorkflowId,
            e.Lease.Task.Recipe.Id, e.Closed ? "Closed" : e.Status?.Phase.ToString() ?? "Preparing",
            e.Status?.Outcome.ToString() ?? "None", !e.Closed && e.Ready,
            e.Status?.BuildStarted ?? false, e.Status?.ExecutionDrained ?? false, !e.Closed,
            e.Diagnostic.Length != 0 ? e.Diagnostic : e.Status?.FailureType ?? "",
            e.ProductFingerprint, e.Receipt?.Conclusion.ToString() ?? "", e.Receipt?.Fingerprint ?? "",
            !e.Closed && IsSourceCurrent(e.Lease.Task.Binding), e.Lease.Task.Binding.PlanHash,
            e.Status?.StopFailureType ?? e.PreparationStopDiagnostic, e.PreparationCloseDiagnostic.Length != 0 ? e.PreparationCloseDiagnostic : e.Status?.CloseFailureType ?? "", e.Status?.Abandoned ?? false)).ToArray();
    }
    internal bool TryCloseAll()
    {
        Owner(); closing = true; queue.Shutdown();
        bool complete = true;
        for (int i = entries.Count - 1; i >= 0; i--) if (!entries[i].Closed) complete &= Close(entries[i].Lease.Task.Binding.JobId);
        for (int i = sourceCaptures.Count - 1; i >= 0; i--) complete &= sourceCaptures[i].TryClose();
        return complete;
    }
}
