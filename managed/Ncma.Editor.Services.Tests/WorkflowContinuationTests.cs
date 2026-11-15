using System.Text;
using Ncma.Editor.Core;
using Ncma.Editor.Services;

internal static partial class WorkflowTests
{
    // Real editor/MCP/ledger and production source guard; execution lease is
    // deliberately synthetic. These are NOT real SDK/Job-drain acceptance.
    private sealed class ContinuationLease(WorkflowVerificationBinding binding) : IWorkflowBuildLease
    {
        public Guid LeaseId { get; } = Guid.NewGuid();
        public WorkflowVerificationTask Task { get; } = new(binding, [new("fixture.dll", 1, new string('A', 64))], []);
        public WorkflowBuildEnvironment Environment { get; } = new(@"F:\NcmaEngineRelease\source", @"C:\fixture\dotnet.exe", @"C:\fixture\powershell.exe",
            new string('A', 64), [new("scripts/WorkflowBuildInputs.ps1", 1, new string('A', 64))], TimeSpan.FromSeconds(90));
        internal int Starts, Stops, Closes;
        internal bool StopFails, CloseFails;
        internal WorkflowBuildObservation Observation = new(false, true, null);
        public void RequireCurrent() { }
        public void Start(WorkflowBuildStartPermit permit) { permit.Consume(this); Starts++; Observation = new(true, false, null); }
        public void RequestStop() { Stops++; if (StopFails) throw new IOException("private stop diagnostic must stay local"); }
        public WorkflowBuildObservation Observe() => Observation;
        public void Close() { Closes++; if (CloseFails) throw new IOException("private close diagnostic must stay local"); }
    }
    private static WorkflowVerificationBinding ContinuationBinding(GraphInspectionTests.Fixture f, Guid id)
    {
        var edit = f.Owner.Edit!; string hash = new('A', 64);
        return new(1, Guid.NewGuid(), edit.SessionId, id, f.Owner.Workflows!.Capture().Single(p => p.WorkflowId == id).Hash,
            new(edit.SessionId, edit.DocumentGeneration, edit.Revision), Guid.NewGuid(), hash, hash, hash, null,
            "editor-services-build-v1", "Debug", "default", "synthetic-execution-only", hash) {
            Assets = f.Owner.Workflows!.VerificationAssetSource(id) };
    }
    private static void SourceReject(Action action)
    {
        try { action(); } catch (EditRejectedException error) when (error.Code == "workflow_job_source_stale") { return; }
        throw new Exception("Expected source rejection BEFORE input IO");
    }
    internal static IEnumerable<(string, Action)> ContinuationCases(string output, string repository)
    {
        foreach (var source in SourceHandoffCases(output, repository)) yield return source;
        foreach (var capture in WorkflowSourceCaptureTests.Cases(output)) yield return capture;
        yield return ("M8.6-D stopped plans invalidate queued jobs even when plan hash is unchanged", () => {
            foreach (string reason in new[] { "cancelled", "revoked", "expired", "conflict" }) {
                using var f = new GraphInspectionTests.Fixture(output, repository); Registry(f);
                Guid id = Propose(f, [Step("ncma.scene.validate", new { }, "valid")], deadline: 1); Approve(f, id);
                var binding = ContinuationBinding(f, id); var host = f.Owner.WorkflowJobs!;
                Check(host.IsSourceCurrent(binding) && host.CanPrepare(id));
                var lease = new ContinuationLease(binding); var queue = new WorkflowBuildJobs(host.IsSourceCurrent);
                var review = queue.Review(lease); queue.Approve(review, review.Fingerprint, true);
                if (reason == "cancelled") f.Owner.Workflows!.Cancel(id);
                else if (reason == "revoked") f.Owner.Workflows!.Revoke();
                else if (reason == "expired") f.Time.Now = 1000;
                else f.Workspace.CreateObject(f.Workspace.Stamp);
                byte[] scene = f.Owner.Document.CaptureBytes(); var state = f.Owner.Edit!.State;
                Check(!host.IsSourceCurrent(binding) && !host.CanPrepare(id));
                Check(f.Owner.Workflows!.Capture().Single().Hash == binding.PlanHash);
                SourceReject(() => host.PrepareOffline("not a path", Guid.NewGuid(), "not a cache", id, "Debug", "default"));
                Check(host.Capture().Length == 0 && !queue.StartNext() && lease.Starts == 0 && lease.Stops == 1);
                var stopped = queue.Poll(binding.JobId); Check(stopped.Outcome == WorkflowBuildOutcome.Stale && stopped.ExecutionDrained);
                Check(queue.Close(binding.JobId) && scene.SequenceEqual(f.Owner.Document.CaptureBytes()) && state == f.Owner.Edit.State);
            }
        });
        yield return ("M8.6-D running stale job retains independent stop and close failures until explicit retries", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository); Registry(f);
            Guid id = Propose(f, [Step("ncma.scene.validate", new { }, "valid")]); Approve(f, id);
            var binding = ContinuationBinding(f, id); var lease = new ContinuationLease(binding) { StopFails = true, CloseFails = true };
            var queue = new WorkflowBuildJobs(f.Owner.WorkflowJobs!.IsSourceCurrent); var review = queue.Review(lease);
            queue.Approve(review, review.Fingerprint, true); Check(queue.StartNext());
            byte[] scene = f.Owner.Document.CaptureBytes(); var history = f.Owner.Edit!.State;
            f.Owner.Workflows!.Cancel(id); var stopped = queue.Poll(binding.JobId);
            Check(stopped.Phase == WorkflowBuildPhase.Stopping && !stopped.ExecutionDrained && stopped.LeaseRetained && stopped.StopFailureType == "IOException");
            for (int i = 0; i < 5; i++) _ = queue.Poll(binding.JobId);
            Check(lease.Stops == 1 && lease.Closes == 0); lease.StopFails = false; queue.RetryStop(binding.JobId); Check(lease.Stops == 2);
            lease.Observation = new(true, true, 0); stopped = queue.Poll(binding.JobId);
            Check(stopped.Outcome == WorkflowBuildOutcome.Stale && stopped.StopFailureType == "IOException");
            Check(!queue.Close(binding.JobId)); stopped = queue.Poll(binding.JobId);
            Check(stopped.LeaseRetained && stopped.CloseFailureType == "IOException" && lease.Closes == 1);
            lease.CloseFails = false; Check(queue.Close(binding.JobId));
            Check(scene.SequenceEqual(f.Owner.Document.CaptureBytes()) && history == f.Owner.Edit.State);
        });
        yield return ("M8.6-D completed receipts allow exact committed source but never inherit later human edits", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository); Registry(f); Guid obj = Guid.NewGuid();
            Guid id = Propose(f, [Step("ncma.scene.transaction", new { operations = new[] { new { op = "create", objectId = obj, name = "Committed" } } })]);
            var before = ContinuationBinding(f, id); Approve(f, id); var ticket = Next(f, id);
            Check(Execute(f, ticket).GetProperty("isError").GetBoolean()); ApproveTool(f); Data(Execute(f, ticket));
            var host = f.Owner.WorkflowJobs!; var committed = ContinuationBinding(f, id);
            Check(!host.IsSourceCurrent(before) && host.IsSourceCurrent(committed) && host.CanPrepare(id));
            Check(!host.IsSourceCurrent(committed with { WorkspaceSession = Guid.NewGuid() }));
            f.Owner.Edit!.SetFrozen(true); Check(!host.IsSourceCurrent(committed)); f.Owner.Edit.SetFrozen(false); Check(host.IsSourceCurrent(committed));
            f.Workspace.Transaction(f.Workspace.Stamp, "Later human edit", [new { op = "rename", objectId = obj, name = "Retain later value" }]);
            byte[] later = f.Owner.Document.CaptureBytes(); var history = f.Owner.Edit.State;
            Check(!host.IsSourceCurrent(committed) && !host.IsSourceCurrent(ContinuationBinding(f, id)) && !host.CanPrepare(id));
            SourceReject(() => host.PrepareOffline("not a path", Guid.NewGuid(), "not a cache", id, "Debug", "default"));
            f.Owner.Workflows!.Cancel(id); Check(f.Owner.Workflows.Capture().Single().Status == "completed");
            Check(later.SequenceEqual(f.Owner.Document.CaptureBytes()) && history == f.Owner.Edit.State && f.Owner.Workflows.Capture().Single().Receipts.Single().Changed);
        });
        yield return ("M8.6-D failed plan repair cannot revive an old build approval", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository); Registry(f);
            Guid id = Propose(f, [Step("ncma.scene.object.inspect", new { objectId = Guid.NewGuid(), section = "summary" })]); Approve(f, id);
            var before = ContinuationBinding(f, id); var host = f.Owner.WorkflowJobs!;
            var lease = new ContinuationLease(before); var queue = new WorkflowBuildJobs(host.IsSourceCurrent);
            var review = queue.Review(lease); queue.Approve(review, review.Fingerprint, true);
            Check(Execute(f, Next(f, id)).GetProperty("isError").GetBoolean()); Check(!host.IsSourceCurrent(before));
            Data(f.Call(EditorWorkflows.Capability, new { op = "repair", workflowId = id, input = new { objectId = Guid.NewGuid(), section = "summary" } }, Guid.NewGuid()));
            var repaired = ContinuationBinding(f, id); Check(repaired.PlanHash != before.PlanHash && host.IsSourceCurrent(repaired) && !host.IsSourceCurrent(before));
            Check(!queue.StartNext() && lease.Starts == 0); Check(queue.Poll(before.JobId).Outcome == WorkflowBuildOutcome.Stale); Check(queue.Close(before.JobId));
            Check(host.Capture().Length == 0 && f.Owner.Edit!.State.UndoCount == 0);
        });
        yield return ("M8.6-D actual presenter disables stale preparation and explains irreversible work", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository); Registry(f);
            Guid id = Propose(f, [Step("ncma.scene.validate", new { }, "valid")]);
            Click(f, 40, 8); Click(f, 24, 12); Click(f, 24, 70); Click(f, 61, 1000);
            Check(f.View.Items.ToArray().Single(i => i.WidgetHigh == 62 && i.WidgetLow == 6).Enabled != 0);
            var old = f.Event(62, 6); f.Owner.Workflows!.Cancel(id); f.Build();
            Check(f.View.Items.ToArray().Single(i => i.WidgetHigh == 62 && i.WidgetLow == 6).Enabled == 0);
            f.View.Apply([old], []); f.Build(); Check(f.Owner.WorkflowJobs!.Capture().Length == 0);
            string text = Encoding.UTF8.GetString(f.View.Text); Check(text.Contains("不隐式 Undo", StringComparison.Ordinal) && text.Contains("不回滚", StringComparison.Ordinal));
            Check(f.Owner.Edit!.State.UndoCount == 0);
        });
    }
}
