using Ncma.Editor.Core;
using Ncma.Editor.Services;

// Lifecycle-contract fixtures ONLY: no subprocess, capture IO, sandbox or physical
// approval proof. C2b/C3 must add real adapter/borrower/drain evidence at stage E.
internal static class WorkflowBuildJobTests
{
    private static string Hash(char c = 'A') => new(c, 64);
    private sealed class Clock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => ticks;
        internal void Advance(int seconds) => ticks += seconds * 1000L;
    }
    private sealed class Lease : IWorkflowBuildLease
    {
        public Guid LeaseId { get; set; } = Guid.NewGuid();
        public WorkflowVerificationTask Task { get; set; }
        public WorkflowBuildEnvironment Environment { get; }
        internal int Validates, Starts, Stops, Closes;
        internal bool Invalid, StartFails, StopFails, CloseFails, ObserveFails;
        internal Action? OnValidate, OnStart, OnClose;
        internal WorkflowBuildObservation Observation = new(false, true, null);
        internal Lease(Guid? jobId = null, string recipe = "editor-services-build-v1")
        {
            var binding = new WorkflowVerificationBinding(1, jobId ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Hash(),
                new(Guid.NewGuid(), 1, 2), Guid.NewGuid(), Hash('B'), Hash('C'), Hash('D'), null, recipe, "Debug", "default", "synthetic-tool", Hash('E'));
            Task = new(binding, [new("tool.dll", 1, Hash())], recipe == "editor-services-build-v1" ? [] : ["case-a"]);
            Environment = new(@"F:\NcmaEngineRelease\source", @"C:\fixture\dotnet.exe", @"C:\fixture\powershell.exe", Hash('F'),
                [new("scripts/WorkflowBuildInputs.ps1", 1, Hash())], TimeSpan.FromSeconds(90));
        }
        public void RequireCurrent() { Validates++; OnValidate?.Invoke(); if (Invalid) throw new IOException("SECRET must never be copied"); }
        public void Start(WorkflowBuildStartPermit permit)
        {
            permit.Consume(this);
            Starts++; Observation = new(true, false, null); OnStart?.Invoke();
            if (StartFails) throw new IOException("SECRET after child creation");
        }
        public void RequestStop() { Stops++; if (StopFails) throw new UnauthorizedAccessException("SECRET stop"); }
        public WorkflowBuildObservation Observe() { if (ObserveFails) throw new IOException("SECRET observe"); return Observation; }
        public void Close() { Closes++; OnClose?.Invoke(); if (CloseFails) throw new IOException("SECRET close"); }
        internal void End(int exit = 0) => Observation = new(true, true, exit);
    }
    private static void Check(bool value) { if (!value) throw new Exception("M8.6-C2a lifecycle assertion"); }
    private static void Reject(Action action, string code)
    {
        try { action(); } catch (EditRejectedException e) when (e.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
    private static WorkflowBuildReview Queue(WorkflowBuildJobs jobs, Lease lease)
    {
        var review = jobs.Review(lease); jobs.Approve(review, review.Fingerprint, true); return review;
    }
    private static WorkflowBuildStatus View(WorkflowBuildJobs jobs, Lease lease) => jobs.Snapshot().Single(s => s.JobId == lease.Task.Binding.JobId);
    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("M8.6-C2a synthetic lease explicit approval FIFO and no implicit launch", () => {
            var jobs = new WorkflowBuildJobs(_ => true); var first = new Lease(); var second = new Lease();
            var a = jobs.Review(first); var b = jobs.Review(second);
            Check(!jobs.StartNext() && first.Starts == 0 && !a.InstallationAuthorized && !a.RestoreAuthorized && !a.IsSandbox);
            Check(a.OutputRoot.EndsWith(first.Task.Binding.JobId.ToString("N"), StringComparison.Ordinal));
            jobs.Approve(b, b.Fingerprint, true); jobs.Approve(a, a.Fingerprint, true);
            Check(first.Starts == 0 && second.Starts == 0 && jobs.StartNext() && second.Starts == 1 && first.Starts == 0);
            Check(!jobs.StartNext()); second.End();
            var ended = jobs.Poll(second.Task.Binding.JobId);
            Check(ended.Outcome == WorkflowBuildOutcome.ProcessExited && ended.LeaseRetained && ended.Phase == WorkflowBuildPhase.AwaitingClose);
            Check(jobs.StartNext() && first.Starts == 1 && jobs.Close(second.Task.Binding.JobId));
            Check(second.Closes == 1 && jobs.Close(second.Task.Binding.JobId) && second.Closes == 1);
        });
        yield return ("M8.6-C2a opaque foreign copied consumed and declined tickets", () => {
            var jobs = new WorkflowBuildJobs(_ => true); var other = new WorkflowBuildJobs(_ => true); var lease = new Lease();
            var review = jobs.Review(lease);
            var copy = new WorkflowBuildReview(review.LeaseId, review.Task, review.Environment);
            Reject(() => jobs.Approve(copy, copy.Fingerprint, true), "workflow_job_ticket_invalid");
            Reject(() => other.Approve(review, review.Fingerprint, true), "workflow_job_missing");
            Reject(() => jobs.Approve(review, review.Fingerprint, false), "workflow_job_confirmation_required");
            Reject(() => jobs.Approve(review, review.Fingerprint, true), "workflow_job_ticket_invalid");
            Check(lease.Starts == 0 && lease.Stops == 1 && jobs.Poll(lease.Task.Binding.JobId).Outcome == WorkflowBuildOutcome.Rejected);
            Check(jobs.Close(lease.Task.Binding.JobId));
            var next = new Lease(); var ticket = jobs.Review(next);
            Reject(() => jobs.Approve(ticket, Hash(), true), "workflow_job_confirmation_required");
            Reject(() => jobs.Approve(ticket, ticket.Fingerprint, true), "workflow_job_ticket_invalid");
        });
        yield return ("M8.6-C2a monotonic review queue and callback expiry", () => {
            foreach (int mode in new[] { 0, 1, 2, 3 }) {
                var clock = new Clock(); var jobs = new WorkflowBuildJobs(_ => true, clock); var lease = new Lease(); var review = jobs.Review(lease);
                if (mode == 0) { clock.Advance(60); Reject(() => jobs.Approve(review, review.Fingerprint, true), "workflow_job_review_expired"); }
                else if (mode == 2) { lease.OnValidate = () => clock.Advance(60); Reject(() => jobs.Approve(review, review.Fingerprint, true), "workflow_job_review_expired"); }
                else {
                    jobs.Approve(review, review.Fingerprint, true);
                    if (mode == 1) clock.Advance(60); else lease.OnValidate = () => clock.Advance(60);
                    Check(!jobs.StartNext());
                }
                Check(lease.Starts == 0 && jobs.Poll(lease.Task.Binding.JobId).Outcome == WorkflowBuildOutcome.Expired);
            }
        });
        yield return ("M8.6-C2a source and synthetic lease invalidation before approve and launch", () => {
            bool current = true; var jobs = new WorkflowBuildJobs(_ => current); var lease = new Lease(); var review = jobs.Review(lease);
            current = false; Reject(() => jobs.Approve(review, review.Fingerprint, true), "workflow_job_review_stale");
            Check(jobs.Poll(lease.Task.Binding.JobId).Outcome == WorkflowBuildOutcome.Stale && lease.Starts == 0);
            current = true; var queued = new Lease(); Queue(jobs, queued); queued.Invalid = true;
            Check(!jobs.StartNext() && queued.Starts == 0 && View(jobs, queued).FailureType == "IOException");
            var before = new Lease { Invalid = true };
            try { jobs.Review(before); throw new Exception("Expected invalid lease"); } catch (IOException) { }
            Check(before.Closes == 0 && jobs.Snapshot().Length == 2); // caller retains rejected preparation
            var changed = new Lease(); var changeReview = jobs.Review(changed); changed.OnValidate = () => changed.LeaseId = Guid.NewGuid();
            Reject(() => jobs.Approve(changeReview, changeReview.Fingerprint, true), "workflow_job_review_stale");
        });
        yield return ("M8.6-C2a postspawn launch exception retains ownership and blocks next launch", () => {
            var jobs = new WorkflowBuildJobs(_ => true); var broken = new Lease { StartFails = true, StopFails = true }; var next = new Lease();
            Queue(jobs, broken); Queue(jobs, next); Check(jobs.StartNext());
            Check(!jobs.StartNext() && broken.Starts == 1 && broken.Stops == 1);
            Reject(() => jobs.Close(broken.Task.Binding.JobId), "workflow_job_not_drained");
            var status = jobs.Poll(broken.Task.Binding.JobId);
            Check(status.Outcome == WorkflowBuildOutcome.LaunchFailed && status.FailureType == "IOException" && status.StopFailureType == "UnauthorizedAccessException");
            Check(broken.Stops == 1 && !jobs.StartNext()); broken.StopFails = false; jobs.RetryStop(broken.Task.Binding.JobId);
            Check(broken.Stops == 2); broken.End(1); status = jobs.Poll(broken.Task.Binding.JobId);
            Check(status.Outcome == WorkflowBuildOutcome.LaunchFailed && status.ExecutionDrained && jobs.StartNext());
        });
        yield return ("M8.6-C2a root exit is not descendant drain timeout cancel preserve first outcome", () => {
            var clock = new Clock(); var jobs = new WorkflowBuildJobs(_ => true, clock); var lease = new Lease(); Queue(jobs, lease); jobs.StartNext();
            lease.Observation = new(true, false, 0); clock.Advance(90);
            var status = jobs.Poll(lease.Task.Binding.JobId);
            Check(status.Outcome == WorkflowBuildOutcome.TimedOut && !status.ExecutionDrained && lease.Stops == 1);
            jobs.Cancel(lease.Task.Binding.JobId); jobs.Poll(lease.Task.Binding.JobId);
            Check(lease.Stops == 1 && View(jobs, lease).Outcome == WorkflowBuildOutcome.TimedOut);
            lease.End(); Check(jobs.Poll(lease.Task.Binding.JobId).Outcome == WorkflowBuildOutcome.TimedOut);
            Check(jobs.Close(lease.Task.Binding.JobId));
            var cancelled = new Lease(); Queue(jobs, cancelled); jobs.StartNext(); jobs.Cancel(cancelled.Task.Binding.JobId); cancelled.End(7);
            Check(jobs.Poll(cancelled.Task.Binding.JobId).Outcome == WorkflowBuildOutcome.Cancelled);
        });
        yield return ("M8.6-C2a drained failed close retains owner without retiring healthy work", () => {
            var jobs = new WorkflowBuildJobs(_ => true); var lease = new Lease { CloseFails = true }; Queue(jobs, lease); jobs.StartNext(); lease.End(); jobs.Poll(lease.Task.Binding.JobId);
            Check(!jobs.Close(lease.Task.Binding.JobId)); var status = jobs.Poll(lease.Task.Binding.JobId);
            Check(status.CloseRetryRequired && status.LeaseRetained && status.CloseFailureType == "IOException" && lease.Closes == 1);
            var next = new Lease(); Queue(jobs, next); Check(jobs.StartNext()); jobs.Shutdown();
            Check(lease.Closes == 1 && View(jobs, lease).Outcome == WorkflowBuildOutcome.ProcessExited);
            lease.CloseFails = false; Check(jobs.Close(lease.Task.Binding.JobId));
            Check(!View(jobs, lease).LeaseRetained && View(jobs, lease).CloseFailureType == "IOException");
        });
        yield return ("M8.6-C2a shutdown cancels every pending owner no retry or resume", () => {
            var jobs = new WorkflowBuildJobs(_ => true); var running = new Lease(); var queued = new Lease(); var pending = new Lease();
            Queue(jobs, running); Queue(jobs, queued); var review = jobs.Review(pending); jobs.StartNext(); jobs.Shutdown(); jobs.Shutdown();
            Check(running.Stops == 1 && queued.Stops == 1 && pending.Stops == 1 && queued.Starts == 0);
            Reject(() => jobs.StartNext(), "workflow_job_closing"); Reject(() => jobs.Review(new Lease()), "workflow_job_closing");
            Reject(() => jobs.Approve(review, review.Fingerprint, true), "workflow_job_ticket_invalid");
            foreach (var lease in new[] { queued, pending }) { Check(jobs.Poll(lease.Task.Binding.JobId).ExecutionDrained); Check(jobs.Close(lease.Task.Binding.JobId)); }
            Check(!jobs.Poll(running.Task.Binding.JobId).ExecutionDrained); running.End(); jobs.Poll(running.Task.Binding.JobId); Check(jobs.Close(running.Task.Binding.JobId));
        });
        yield return ("M8.6-C2a owner thread and validation launch close reentry guards", () => {
            var jobs = new WorkflowBuildJobs(_ => true); var lease = new Lease();
            System.Threading.Tasks.Task.Run(() => Reject(() => jobs.Snapshot(), "workflow_job_thread")).GetAwaiter().GetResult();
            lease.OnValidate = () => Reject(() => jobs.Shutdown(), "workflow_job_reentry");
            var review = Queue(jobs, lease);
            lease.OnStart = () => Reject(() => jobs.Approve(review, review.Fingerprint, true), "workflow_job_reentry");
            lease.OnClose = () => Reject(() => jobs.StartNext(), "workflow_job_reentry");
            Check(jobs.StartNext() && lease.Starts == 1); lease.End(); jobs.Poll(lease.Task.Binding.JobId); Check(jobs.Close(lease.Task.Binding.JobId));
        });
        yield return ("M8.6-C2a bounded queue lifetime dedup and build-only recipe", () => {
            var jobs = new WorkflowBuildJobs(_ => true);
            for (int i = 0; i < WorkflowBuildJobs.MaxQueued; i++) Queue(jobs, new Lease());
            var full = new Lease(); var review = jobs.Review(full);
            Reject(() => jobs.Approve(review, review.Fingerprint, true), "workflow_job_queue_full");
            Reject(() => jobs.Review(new Lease(full.Task.Binding.JobId)), "workflow_job_duplicate");
            Reject(() => jobs.Review(new Lease(recipe: "workflow-contracts-v1")), "workflow_job_test_inputs_required"); // fixed recipe still needs exact catalogue + compiled closure
            for (int i = jobs.Snapshot().Length; i < WorkflowBuildJobs.MaxJobs; i++) jobs.Review(new Lease());
            Reject(() => jobs.Review(new Lease()), "workflow_job_lifetime_budget");
            Check(jobs.Snapshot().Length == WorkflowBuildJobs.MaxJobs && full.Starts == 0);
        });
        yield return ("M8.6-C2a invalid observations and lost status cannot release active ownership", () => {
            var jobs = new WorkflowBuildJobs(_ => true); var lease = new Lease(); var next = new Lease(); Queue(jobs, lease); Queue(jobs, next); jobs.StartNext();
            jobs.Poll(lease.Task.Binding.JobId); lease.Observation = new(false, true, null);
            Check(jobs.Poll(lease.Task.Binding.JobId).Outcome == WorkflowBuildOutcome.ObservationFailed && !jobs.StartNext());
            Reject(() => jobs.Close(lease.Task.Binding.JobId), "workflow_job_not_drained");
            lease.ObserveFails = true; Check(!jobs.Poll(lease.Task.Binding.JobId).ExecutionDrained && lease.Stops == 1);
            lease.ObserveFails = false; lease.End(9); Check(jobs.Poll(lease.Task.Binding.JobId).ExecutionDrained && jobs.StartNext());
            next.End(9); Check(jobs.Poll(next.Task.Binding.JobId).Outcome == WorkflowBuildOutcome.ProcessFailed);
            var inconsistent = new Lease(); Queue(jobs, inconsistent); jobs.StartNext();
            inconsistent.Observation = new(true, true, null); // started, but invalid proof of drain
            Check(!jobs.Poll(inconsistent.Task.Binding.JobId).ExecutionDrained);
            inconsistent.Observation = new(false, true, null); // cannot retract prior start evidence
            Check(!jobs.Poll(inconsistent.Task.Binding.JobId).ExecutionDrained && !jobs.StartNext());
            inconsistent.End(); Check(jobs.Poll(inconsistent.Task.Binding.JobId).ExecutionDrained);
            var lost = new Lease(); Queue(jobs, lost); Check(jobs.StartNext());
            lost.Observation = new(true, true, null, "workflow_process_missing_sdk_exit");
            var lostStatus = jobs.Poll(lost.Task.Binding.JobId);
            Check(lostStatus.ExecutionDrained && lostStatus.ExitCode is null && lostStatus.Outcome == WorkflowBuildOutcome.ObservationFailed);
            Check(WorkflowObservedEvidence.Outcome(lost.Task, lostStatus, lost.Observation) == WorkflowProcessOutcome.ObservationLost);
            Check(jobs.Close(lost.Task.Binding.JobId) && lost.Closes == 1);
        });
        yield return ("Workflow abandoned descendant releases only scheduling slot and cannot steal next owner", () => {
            var jobs = new WorkflowBuildJobs(_ => true); var stuck = new Lease(); var next = new Lease(); var third = new Lease();
            Queue(jobs, stuck); Queue(jobs, next); Queue(jobs, third); Check(jobs.StartNext());
            // A root result with a living descendant/held stdout is still not drained.
            stuck.Observation = new(true, false, 0); jobs.Cancel(stuck.Task.Binding.JobId);
            var before = jobs.Poll(stuck.Task.Binding.JobId);
            Check(!before.ExecutionDrained && !before.Abandoned && !jobs.StartNext());
            jobs.Abandon(stuck.Task.Binding.JobId); jobs.Abandon(stuck.Task.Binding.JobId);
            var abandoned = jobs.Poll(stuck.Task.Binding.JobId);
            Check(abandoned.Phase == WorkflowBuildPhase.Abandoned && abandoned.Abandoned && abandoned.LeaseRetained &&
                !abandoned.ExecutionDrained && abandoned.Outcome == WorkflowBuildOutcome.Cancelled && abandoned.ExitCode == 0);
            Check(stuck.Stops == 1 && stuck.Closes == 0 && next.Starts == 0 && jobs.StartNext());
            Reject(() => jobs.Close(stuck.Task.Binding.JobId), "workflow_job_not_drained");
            jobs.Cancel(stuck.Task.Binding.JobId);
            for (int i = 0; i < 5; i++) Check(jobs.Poll(stuck.Task.Binding.JobId).Phase == WorkflowBuildPhase.Abandoned);
            Check(!jobs.StartNext() && next.Starts == 1 && third.Starts == 0 && stuck.Stops == 1);
            stuck.End(); var ended = jobs.Poll(stuck.Task.Binding.JobId);
            Check(ended.Abandoned && ended.ExecutionDrained && ended.Phase == WorkflowBuildPhase.AwaitingClose);
            Check(WorkflowObservedEvidence.Outcome(stuck.Task, ended, stuck.Observation) == WorkflowProcessOutcome.ObservationLost);
            Check(!jobs.StartNext() && jobs.Close(stuck.Task.Binding.JobId) && stuck.Closes == 1);
            Check(View(jobs, stuck).Abandoned && !View(jobs, stuck).LeaseRetained);
            next.End(); jobs.Poll(next.Task.Binding.JobId); Check(jobs.StartNext() && third.Starts == 1);
        });
        yield return ("Workflow abandonment retains timeout launch and observation failures without automatic retries", () => {
            foreach (string reason in new[] { "timeout", "launch", "observation" }) {
                var clock = new Clock(); var jobs = new WorkflowBuildJobs(_ => true, clock);
                var stuck = new Lease { StartFails = reason == "launch", StopFails = true, CloseFails = true };
                Queue(jobs, stuck); Check(jobs.StartNext());
                if (reason == "timeout") clock.Advance(90);
                if (reason == "observation") stuck.ObserveFails = true;
                var before = jobs.Poll(stuck.Task.Binding.JobId);
                Check(before.Phase == WorkflowBuildPhase.Stopping && !before.ExecutionDrained && !jobs.StartNext());
                jobs.Abandon(stuck.Task.Binding.JobId);
                // Review expiry still applies, so use a fresh approval after the timeout.
                var next = new Lease(); Queue(jobs, next); Check(jobs.StartNext());
                var status = jobs.Poll(stuck.Task.Binding.JobId);
                Check(status.Abandoned && status.Phase == WorkflowBuildPhase.Abandoned && status.Outcome == before.Outcome &&
                    status.FailureType == before.FailureType && status.StopFailureType == "UnauthorizedAccessException");
                Check(stuck.Closes == 0 && stuck.Stops == 1 && !jobs.StartNext());
                stuck.StopFails = false; jobs.RetryStop(stuck.Task.Binding.JobId); Check(stuck.Stops == 2);
                stuck.ObserveFails = false; stuck.End(); status = jobs.Poll(stuck.Task.Binding.JobId);
                Check(status.ExecutionDrained && status.Abandoned && status.Outcome == before.Outcome);
                Check(WorkflowObservedEvidence.Outcome(stuck.Task, status, stuck.Observation) == WorkflowProcessOutcome.ObservationLost);
                Check(!jobs.Close(stuck.Task.Binding.JobId)); jobs.Poll(stuck.Task.Binding.JobId);
                jobs.Shutdown(); Check(stuck.Closes == 1 && next.Stops == 1);
                stuck.CloseFails = false; Check(jobs.Close(stuck.Task.Binding.JobId));
                Check(View(jobs, stuck).Abandoned && View(jobs, stuck).CloseFailureType == "IOException");
            }
        });
        yield return ("Workflow abandonment is explicit owner only and never implied by cancel or shutdown", () => {
            var jobs = new WorkflowBuildJobs(_ => true); var lease = new Lease(); var review = jobs.Review(lease);
            Reject(() => jobs.Abandon(lease.Task.Binding.JobId), "workflow_job_abandon_not_pending");
            jobs.Approve(review, review.Fingerprint, true);
            Reject(() => jobs.Abandon(lease.Task.Binding.JobId), "workflow_job_abandon_not_pending");
            lease.OnStart = () => Reject(() => jobs.Abandon(lease.Task.Binding.JobId), "workflow_job_reentry");
            Check(jobs.StartNext());
            Reject(() => jobs.Abandon(lease.Task.Binding.JobId), "workflow_job_abandon_not_pending");
            jobs.Shutdown();
            System.Threading.Tasks.Task.Run(() => Reject(() => jobs.Abandon(lease.Task.Binding.JobId), "workflow_job_thread")).GetAwaiter().GetResult();
            for (int i = 0; i < 5; i++) Check(!jobs.Poll(lease.Task.Binding.JobId).Abandoned);
            Check(lease.Stops == 1 && lease.Closes == 0); jobs.Abandon(lease.Task.Binding.JobId);
            jobs.Shutdown(); Check(jobs.Poll(lease.Task.Binding.JobId).Phase == WorkflowBuildPhase.Abandoned && lease.Stops == 1);
            Reject(() => jobs.StartNext(), "workflow_job_closing");
            lease.End(); jobs.Poll(lease.Task.Binding.JobId); Check(jobs.Close(lease.Task.Binding.JobId));
            var neverStarted = new WorkflowBuildJobs(_ => true); var pending = new Lease(); neverStarted.Review(pending);
            neverStarted.Cancel(pending.Task.Binding.JobId);
            Reject(() => neverStarted.Abandon(pending.Task.Binding.JobId), "workflow_job_abandon_not_pending");
        });
        yield return ("M8.6-C2a review helper closure copies and immutable status copies", () => {
            var jobs = new WorkflowBuildJobs(_ => true); var lease = new Lease(); var review = jobs.Review(lease); string fingerprint = review.Fingerprint;
            var helpers = review.Environment.CopyHelpers(); helpers[0] = new("evil.ps1", 0, Hash('F'));
            Check(review.Environment.CopyHelpers()[0].Path != "evil.ps1" && review.Fingerprint == fingerprint);
            var snapshot = jobs.Snapshot(); snapshot[0] = snapshot[0] with { Phase = WorkflowBuildPhase.Closed, LeaseRetained = false };
            Check(View(jobs, lease).Phase == WorkflowBuildPhase.Review && View(jobs, lease).LeaseRetained);
            Reject(() => new WorkflowBuildEnvironment("relative", @"C:\dotnet.exe", @"C:\powershell.exe", Hash(), [new("a.ps1", 1, Hash())], TimeSpan.FromSeconds(1)), "workflow_job_absolute_path");
            Reject(() => new WorkflowBuildEnvironment(@"F:\fixture", @"C:\dotnet.exe", @"C:\powershell.exe", Hash(), [], TimeSpan.FromSeconds(1)), "workflow_job_environment_budget");
        });
    }
}
