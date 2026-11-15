using Ncma.Editor.Core;

namespace Ncma.Editor.Services;

// C2a is a trusted-local lifecycle foundation. There is deliberately no public
// constructor, MCP registration, process factory or JSON-to-approval conversion.
// C2b must provide the physical C1 lease/worker adapter before production use.
internal enum WorkflowBuildPhase { Review, Queued, Running, Stopping, AwaitingClose, Retained, Closed, Abandoned }
internal enum WorkflowBuildOutcome { None, ProcessExited, ProcessFailed, Rejected, Expired, Stale, LaunchFailed, ObservationFailed, Cancelled, TimedOut }
internal sealed record WorkflowBuildObservation(bool BuildStarted, bool ExecutionDrained, int? ExitCode, string FailureCode = "", bool TimedOut = false);
internal sealed record WorkflowBuildStatus(Guid JobId, WorkflowBuildPhase Phase, WorkflowBuildOutcome Outcome,
    bool BuildStarted, bool ExecutionDrained, int? ExitCode, bool LeaseRetained, bool CloseRetryRequired,
    string FailureType, string StopFailureType, string CloseFailureType, bool Abandoned = false);

internal sealed class WorkflowBuildEnvironment
{
    private readonly WorkflowEvidenceFile[] helpers;
    internal string WorkspaceRoot { get; }
    internal string ToolPath { get; }
    internal string RunnerPath { get; }
    internal string RunnerSha256 { get; }
    internal TimeSpan Timeout { get; }
    internal string Fingerprint { get; }
    internal WorkflowEvidenceFile[] CopyHelpers() => (WorkflowEvidenceFile[])helpers.Clone();
    internal WorkflowBuildEnvironment(string workspaceRoot, string toolPath, string runnerPath, string runnerSha256,
        IEnumerable<WorkflowEvidenceFile> helperFiles, TimeSpan timeout)
    {
        static string Absolute(string path)
        {
            WorkflowEvidenceRules.Text(path, 1024);
            if (!Path.IsPathFullyQualified(path)) throw new EditRejectedException("workflow_job_absolute_path");
            // Preserve rejection of aliases before GetFullPath erases them. The
            // command/pin layer still enforces local fixed-drive and grammar rules.
            foreach (string part in path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
                if (part is "." or ".." || part.EndsWith('.') || part.EndsWith(' '))
                    throw new IOException("workflow_path_alias");
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        WorkspaceRoot = Absolute(workspaceRoot); ToolPath = Absolute(toolPath); RunnerPath = Absolute(runnerPath);
        WorkflowEvidenceRules.Hash(runnerSha256); RunnerSha256 = runnerSha256;
        helpers = WorkflowEvidenceRules.Files(helperFiles);
        if (helpers.Length is < 1 or > 16 || timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(15))
            throw new EditRejectedException("workflow_job_environment_budget");
        Timeout = timeout;
        Fingerprint = WorkflowEvidenceRules.Digest(new { WorkspaceRoot, ToolPath, RunnerPath, RunnerSha256, helpers, timeout });
    }
}

// An opaque in-memory object issued by ONE owner. Display data/fingerprints are
// not executable permits. Actual UI confirmation and real adapters arrive later.
internal sealed class WorkflowBuildReview
{
    internal Guid ReviewId { get; } = Guid.NewGuid();
    internal Guid LeaseId { get; }
    internal WorkflowVerificationTask Task { get; }
    internal WorkflowBuildEnvironment Environment { get; }
    internal string OutputRoot { get; }
    internal string Fingerprint { get; }
    internal string CommandFingerprint { get; }
    internal string PreparationFingerprint { get; }
    internal TimeSpan ValidFor => WorkflowBuildJobs.ApprovalLifetime;
    internal bool InstallationAuthorized => false;
    internal bool RestoreAuthorized => Task.Recipe.Kind == WorkflowVerificationKind.OfflineRestore;
    internal bool IsSandbox => false;
    internal WorkflowBuildReview(Guid leaseId, WorkflowVerificationTask task, WorkflowBuildEnvironment environment, string preparationFingerprint = "")
    {
        LeaseId = leaseId; Task = task; Environment = environment;
        var command = new WorkflowBuildCommand(task, environment);
        OutputRoot = command.OutputRoot; CommandFingerprint = command.Fingerprint;
        PreparationFingerprint = preparationFingerprint;
        Fingerprint = WorkflowEvidenceRules.Digest(new { version = 1, ReviewId, LeaseId, task = task.Fingerprint,
            environment = environment.Fingerprint, CommandFingerprint, PreparationFingerprint, OutputRoot, lifetimeTicks = ValidFor.Ticks,
            InstallationAuthorized, RestoreAuthorized, IsSandbox });
    }
}

// Implementations are engine-owned, never supplied by models or deserialized.
// RequireCurrent checks original physical C1 pins + exact helper/tool closure.
// Start must be nonblocking, fixed-recipe and no-restore; it must retain ownership
// even if it throws AFTER creating a child. Stop only signals the owned job; no
// process sweep/force-kill. Observe never blocks and reports complete borrower
// drain, NOT just root exit. Close must retain failed resources for explicit retry.
// These are adapter obligations, not proof that the C2a data model implements IO.
internal interface IWorkflowBuildLease
{
    Guid LeaseId { get; }
    WorkflowVerificationTask Task { get; }
    WorkflowBuildEnvironment Environment { get; }
    string PreparationFingerprint => ""; // synthetic fixtures; production owners supply exact physical preparation
    void RequireCurrent();
    void Start(WorkflowBuildStartPermit permit);
    void RequestStop();
    WorkflowBuildObservation Observe();
    void Close();
}

// In-memory only; the only production implementation is private to the queue.
// Invalid after Start returns, single use, bound to the exact active lease.
internal abstract class WorkflowBuildStartPermit
{
    private protected WorkflowBuildStartPermit() { }
    internal abstract void Consume(IWorkflowBuildLease lease);
}

internal sealed class WorkflowBuildJobs
{
    internal const int MaxJobs = 16, MaxQueued = 3;
    internal static readonly TimeSpan ApprovalLifetime = TimeSpan.FromSeconds(60);
    private readonly int ownerThread = System.Environment.CurrentManagedThreadId;
    private readonly Func<WorkflowVerificationBinding, bool> isCurrent;
    private readonly TimeProvider clock;
    private readonly List<Entry> entries = [];
    private Entry? active;
    private bool entered, closing;
    private int queueSequence;
    private sealed class Entry(IWorkflowBuildLease lease, WorkflowBuildReview review, long created)
    {
        internal IWorkflowBuildLease? Owner = lease;
        internal IWorkflowBuildLease Lease => Owner ?? throw Reject("workflow_job_closed");
        internal readonly WorkflowBuildReview OriginalReview = review;
        internal readonly long Created = created;
        internal WorkflowBuildReview? Ticket = review;
        internal WorkflowBuildPhase Phase = WorkflowBuildPhase.Review;
        internal WorkflowBuildOutcome Outcome;
        internal long StartedAt;
        internal int QueueOrder;
        internal bool BuildStarted, Drained, StopRequested, ExecutionMayBeActive, Abandoned;
        internal int? ExitCode;
        internal string FailureType = "", StopFailureType = "", CloseFailureType = "";
    }
    private sealed class StartPermit(WorkflowBuildJobs owner, Entry entry) : WorkflowBuildStartPermit
    {
        private bool valid = true, consumed;
        internal void Revoke() => valid = false;
        internal override void Consume(IWorkflowBuildLease lease)
        {
            if (System.Environment.CurrentManagedThreadId != owner.ownerThread) throw Reject("workflow_job_thread");
            if (!valid || consumed) throw Reject("workflow_job_start_permit");
            consumed = true;
            if (!ReferenceEquals(entry.Owner, lease) || !ReferenceEquals(owner.active, entry) || owner.closing || entry.Phase != WorkflowBuildPhase.Running)
                throw Reject("workflow_job_start_permit");
            if (owner.Expired(entry)) throw Reject("workflow_job_review_expired");
            owner.RequireCurrent(entry);
            if (owner.Expired(entry)) throw Reject("workflow_job_review_expired");
        }
    }
    internal WorkflowBuildJobs(Func<WorkflowVerificationBinding, bool> isCurrent, TimeProvider? clock = null)
        => (this.isCurrent, this.clock) = (isCurrent ?? throw new ArgumentNullException(nameof(isCurrent)), clock ?? TimeProvider.System);
    private readonly struct Guard(WorkflowBuildJobs owner) : IDisposable { public void Dispose() => owner.entered = false; }
    private Guard Enter()
    {
        if (System.Environment.CurrentManagedThreadId != ownerThread) throw Reject("workflow_job_thread");
        if (entered) throw Reject("workflow_job_reentry");
        entered = true; return new(this);
    }
    private static EditRejectedException Reject(string code) => new(code);
    private Entry Find(Guid id) => entries.Find(e => e.OriginalReview.Task.Binding.JobId == id) ?? throw Reject("workflow_job_missing");
    private static string Type(Exception error) => error.GetType().Name.Length <= 128 ? error.GetType().Name : error.GetType().Name[..128];
    private bool Expired(Entry e) => clock.GetElapsedTime(e.Created) >= ApprovalLifetime;
    private void RequireCurrent(Entry e)
    {
        if (!isCurrent(e.OriginalReview.Task.Binding)) throw Reject("workflow_job_source_stale");
        e.Lease.RequireCurrent();
        if (!ReferenceEquals(e.Lease.Task, e.OriginalReview.Task) || e.Lease.LeaseId != e.OriginalReview.LeaseId ||
            !ReferenceEquals(e.Lease.Environment, e.OriginalReview.Environment) || e.Lease.PreparationFingerprint != e.OriginalReview.PreparationFingerprint || !isCurrent(e.OriginalReview.Task.Binding))
            throw Reject("workflow_job_inputs_changed");
    }
    // Ownership transfers only on successful return. Rejected preparation remains
    // caller-owned; do not lose/dispose an opaque lease in an exception path.
    internal WorkflowBuildReview Review(IWorkflowBuildLease lease)
    {
        using var guard = Enter(); ArgumentNullException.ThrowIfNull(lease);
        if (closing) throw Reject("workflow_job_closing");
        if (entries.Count == MaxJobs) throw Reject("workflow_job_lifetime_budget");
        if (lease.LeaseId == Guid.Empty) throw Reject("workflow_job_lease_identity");
        if (lease.Task.Recipe.Kind == WorkflowVerificationKind.Tests) {
            try { _ = WorkflowTestRequestCodec.Encode(lease.Task); _ = WorkflowTestLayout.RuntimeRoot(lease.Task, lease.Environment.WorkspaceRoot); }
            catch (IOException) { throw Reject("workflow_job_test_inputs_required"); }
        }
        if (entries.Any(e => e.OriginalReview.Task.Binding.JobId == lease.Task.Binding.JobId || e.OriginalReview.LeaseId == lease.LeaseId))
            throw Reject("workflow_job_duplicate");
        var review = new WorkflowBuildReview(lease.LeaseId, lease.Task, lease.Environment, lease.PreparationFingerprint);
        var e = new Entry(lease, review, clock.GetTimestamp());
        RequireCurrent(e);
        var observation = lease.Observe();
        if (observation.BuildStarted || observation.ExitCode is not null || !observation.ExecutionDrained)
            throw Reject("workflow_job_not_prepared");
        if (!isCurrent(review.Task.Binding)) throw Reject("workflow_job_source_stale");
        entries.Add(e); return review;
    }
    internal void Approve(WorkflowBuildReview displayed, string fingerprint, bool explicitlyReviewed)
    {
        using var guard = Enter(); ArgumentNullException.ThrowIfNull(displayed);
        var e = Find(displayed.Task.Binding.JobId);
        if (!ReferenceEquals(e.Ticket, displayed) || e.Phase != WorkflowBuildPhase.Review) throw Reject("workflow_job_ticket_invalid");
        e.Ticket = null; // Every attempt with this ticket consumes it, including rejection.
        if (closing || !explicitlyReviewed || fingerprint != displayed.Fingerprint) {
            Stop(e, WorkflowBuildOutcome.Rejected); throw Reject("workflow_job_confirmation_required");
        }
        if (Expired(e)) { Stop(e, WorkflowBuildOutcome.Expired); throw Reject("workflow_job_review_expired"); }
        if (entries.Count(x => x.Phase == WorkflowBuildPhase.Queued) >= MaxQueued) {
            Stop(e, WorkflowBuildOutcome.Rejected); throw Reject("workflow_job_queue_full");
        }
        try { RequireCurrent(e); }
        catch (Exception error) when (error is not OutOfMemoryException) {
            e.FailureType = Type(error); Stop(e, WorkflowBuildOutcome.Stale); throw Reject("workflow_job_review_stale");
        }
        if (Expired(e)) { Stop(e, WorkflowBuildOutcome.Expired); throw Reject("workflow_job_review_expired"); }
        e.QueueOrder = ++queueSequence;
        e.Phase = WorkflowBuildPhase.Queued; // No implicit process launch.
    }
    internal bool StartNext()
    {
        using var guard = Enter();
        if (closing) throw Reject("workflow_job_closing");
        if (active is not null || entries.Any(x => x.ExecutionMayBeActive && !x.Abandoned)) return false;
        var e = entries.Where(x => x.Phase == WorkflowBuildPhase.Queued).MinBy(x => x.QueueOrder);
        if (e is null) return false;
        if (Expired(e)) { Stop(e, WorkflowBuildOutcome.Expired); return false; }
        try { RequireCurrent(e); }
        catch (Exception error) when (error is not OutOfMemoryException) {
            e.FailureType = Type(error); Stop(e, WorkflowBuildOutcome.Stale); return false;
        }
        if (Expired(e)) { Stop(e, WorkflowBuildOutcome.Expired); return false; }
        // Publish ownership BEFORE calling anything that can create a child.
        active = e; e.ExecutionMayBeActive = true; e.Phase = WorkflowBuildPhase.Running; e.StartedAt = clock.GetTimestamp();
        var permit = new StartPermit(this, e);
        try { e.Lease.Start(permit); }
        catch (Exception error) when (error is not OutOfMemoryException) {
            e.FailureType = Type(error); Stop(e, WorkflowBuildOutcome.LaunchFailed);
        }
        finally { permit.Revoke(); }
        return true; // Start attempted, NOT evidence of launch/success.
    }
    private void Stop(Entry e, WorkflowBuildOutcome outcome)
    {
        if (e.Phase is WorkflowBuildPhase.AwaitingClose or WorkflowBuildPhase.Retained or WorkflowBuildPhase.Closed or WorkflowBuildPhase.Abandoned) return;
        e.Ticket = null;
        if (e.Outcome == WorkflowBuildOutcome.None) e.Outcome = outcome;
        e.Phase = WorkflowBuildPhase.Stopping;
        if (!e.StopRequested) SignalStop(e);
    }
    private static void SignalStop(Entry e)
    {
        e.StopRequested = true;
        try { e.Lease.RequestStop(); }
        catch (Exception error) when (error is not OutOfMemoryException) { if (e.StopFailureType.Length == 0) e.StopFailureType = Type(error); }
    }
    internal void Cancel(Guid id) { using var guard = Enter(); Stop(Find(id), WorkflowBuildOutcome.Cancelled); }
    internal void RetryStop(Guid id)
    {
        using var guard = Enter(); var e = Find(id);
        if (e.Phase is not (WorkflowBuildPhase.Stopping or WorkflowBuildPhase.Abandoned) || e.Drained) throw Reject("workflow_job_stop_not_pending");
        SignalStop(e); // Explicit human/host retry only; first failure is retained.
    }
    // Explicit trusted-owner escape from an uncooperative process tree. Only the
    // scheduling slot is released: the lease, pins, output and drain checks stay
    // owned. No kill, disposal, automatic retry or claim of execution completion.
    internal void Abandon(Guid id)
    {
        using var guard = Enter(); var e = Find(id);
        if (e.Abandoned) return;
        if (e.Phase != WorkflowBuildPhase.Stopping || e.Drained || !e.ExecutionMayBeActive)
            throw Reject("workflow_job_abandon_not_pending");
        e.Abandoned = true; e.Ticket = null; e.Phase = WorkflowBuildPhase.Abandoned;
        if (ReferenceEquals(active, e)) active = null;
    }
    internal WorkflowBuildStatus Poll(Guid id)
    {
        using var guard = Enter(); var e = Find(id);
        if (e.Phase is WorkflowBuildPhase.AwaitingClose or WorkflowBuildPhase.Retained or WorkflowBuildPhase.Closed) return Status(e);
        if (e.Phase is WorkflowBuildPhase.Review or WorkflowBuildPhase.Queued && Expired(e)) Stop(e, WorkflowBuildOutcome.Expired);
        if (e.Outcome == WorkflowBuildOutcome.None) {
            try { if (!isCurrent(e.OriginalReview.Task.Binding)) Stop(e, WorkflowBuildOutcome.Stale); }
            catch (Exception error) when (error is not OutOfMemoryException) {
                e.FailureType = Type(error); Stop(e, WorkflowBuildOutcome.Stale);
            }
        }
        WorkflowBuildObservation observation;
        try {
            observation = e.Lease.Observe();
            if (observation.BuildStarted) { e.ExecutionMayBeActive = true; e.BuildStarted = true; }
            if (observation.BuildStarted && !e.Abandoned && !ReferenceEquals(active, e)) Stop(e, WorkflowBuildOutcome.ObservationFailed);
            if ((e.BuildStarted && !observation.BuildStarted) || (!observation.BuildStarted && observation.ExitCode is not null) ||
                (observation.BuildStarted && observation.ExecutionDrained && observation.ExitCode is null && observation.FailureCode.Length == 0) ||
                (e.ExitCode is not null && observation.ExitCode != e.ExitCode)) throw Reject("workflow_job_observation_invalid");
        } catch (Exception error) when (error is not OutOfMemoryException) {
            e.ExecutionMayBeActive = true; // Lost observation is not evidence of no borrowers.
            if (e.FailureType.Length == 0) e.FailureType = Type(error);
            Stop(e, WorkflowBuildOutcome.ObservationFailed); return Status(e);
        }
        e.BuildStarted |= observation.BuildStarted; e.ExitCode = observation.ExitCode;
        if (observation.TimedOut) Stop(e, WorkflowBuildOutcome.TimedOut);
        if (observation.FailureCode.Length != 0) {
            if (e.FailureType.Length == 0) e.FailureType = observation.FailureCode.Length <= 128 ? observation.FailureCode : "workflow_job_diagnostic_budget";
            Stop(e, WorkflowBuildOutcome.ObservationFailed);
        }
        if (observation.ExecutionDrained && (e.Outcome != WorkflowBuildOutcome.None || observation.BuildStarted)) {
            e.Drained = true; e.ExecutionMayBeActive = false;
            if (e.Outcome == WorkflowBuildOutcome.None) e.Outcome = observation.ExitCode == 0 ? WorkflowBuildOutcome.ProcessExited : WorkflowBuildOutcome.ProcessFailed;
            e.Phase = WorkflowBuildPhase.AwaitingClose;
            if (ReferenceEquals(active, e)) active = null;
        } else if (e.Phase == WorkflowBuildPhase.Running && clock.GetElapsedTime(e.StartedAt) >= e.OriginalReview.Environment.Timeout) {
            Stop(e, WorkflowBuildOutcome.TimedOut);
        }
        return Status(e);
    }
    // No implicit close retries in Poll/Shutdown. A failed close retains the lease
    // and its diagnostics; a drained retained lease does not retire healthy jobs.
    internal bool Close(Guid id)
    {
        using var guard = Enter(); var e = Find(id);
        if (e.Phase == WorkflowBuildPhase.Closed) return true;
        if (!e.Drained || e.Phase is not (WorkflowBuildPhase.AwaitingClose or WorkflowBuildPhase.Retained)) throw Reject("workflow_job_not_drained");
        try { e.Lease.Close(); e.Owner = null; e.Phase = WorkflowBuildPhase.Closed; return true; }
        catch (Exception error) when (error is not OutOfMemoryException) {
            if (e.CloseFailureType.Length == 0) e.CloseFailureType = Type(error);
            e.Phase = WorkflowBuildPhase.Retained; return false;
        }
    }
    internal void Shutdown()
    {
        using var guard = Enter(); closing = true;
        foreach (var e in entries) Stop(e, WorkflowBuildOutcome.Cancelled);
    }
    internal WorkflowBuildStatus[] Snapshot() { using var guard = Enter(); return entries.Select(Status).ToArray(); }
    private static WorkflowBuildStatus Status(Entry e) => new(e.OriginalReview.Task.Binding.JobId, e.Phase, e.Outcome,
        e.BuildStarted, e.Drained, e.ExitCode, e.Phase != WorkflowBuildPhase.Closed, e.Phase == WorkflowBuildPhase.Retained,
        e.FailureType, e.StopFailureType, e.CloseFailureType, e.Abandoned);
}
