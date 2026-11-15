using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.Editor.Modules;
using Ncma.Modules;

internal static partial class DomainWorkflowTests
{
    private static ObservationIdentity WorkflowIdentity() => new(Guid.NewGuid(), new('A', 64), new('B', 64),
        ObservationConfiguration.Release, ObservationVariant.Ai, Stopwatch.Frequency);
    private static AiWorkflowObservationReport ObservedSuccess()
    {
        using var f = new Fixture(); f.Workflow.BeginObservation(WorkflowIdentity()); f.Start(); f.Complete();
        var review = f.Workflow.Review!; Check(f.Workflow.Confirm(review, review.Fingerprint, true).Status == "ok");
        Until(f.Workflow.TryUnregister); return f.Workflow.SealObservation();
    }
    private static IEnumerable<(string, Action)> ObservationCases()
    {
        yield return ("M8.7-E2B3 workflow opt-in default owner once and invalid bounds do not send", () => {
            using var f = new Fixture(); Reject(() => f.Workflow.SealObservation());
            Reject(() => f.Workflow.BeginObservation(WorkflowIdentity(), 0));
            Reject(() => f.Workflow.BeginObservation(WorkflowIdentity(), eventCapacity: 4097));
            Reject(() => f.Workflow.BeginObservation(WorkflowIdentity(), transportCapacity: 0));
            Task.Run(() => Reject(() => f.Workflow.BeginObservation(WorkflowIdentity()))).GetAwaiter().GetResult();
            f.Workflow.BeginObservation(WorkflowIdentity()); Reject(() => f.Workflow.BeginObservation(WorkflowIdentity()));
            Task.Run(() => Reject(() => f.Workflow.SealObservation())).GetAwaiter().GetResult();
            var report = f.Workflow.SealObservation(); Check(AiWorkflowObservationEvidence.Inspect(report).CompleteCapturedScope && report.Requests.Length == 0);
            Reject(() => f.Workflow.BeginObservation(WorkflowIdentity())); Reject(() => f.Workflow.SealObservation()); Check(f.Transport.Calls == 0);
            using var next = new Fixture(); next.Start(); Reject(() => next.Workflow.BeginObservation(WorkflowIdentity()));
        });
        yield return ("M8.7-E2B3 exact module transport review rejected-confirm original receipt and Undo", () => {
            using var f = new Fixture(); f.Workflow.BeginObservation(WorkflowIdentity()); var send = f.Start(); f.Complete();
            var review = f.Workflow.Review!; Check(f.Owner.Edit!.State.UndoCount == 0);
            Reject(() => f.Workflow.Confirm(review, review.Fingerprint, false)); Check(f.Owner.Edit.State.UndoCount == 0);
            Check(f.Workflow.Confirm(review, review.Fingerprint, true).Status == "ok"); Until(f.Workflow.TryUnregister);
            var report = f.Workflow.SealObservation(); var summary = AiWorkflowObservationEvidence.Inspect(report);
            Check(report.Requests.Length == 1 && report.Requests[0].RequestId == send.Observation.RequestId && report.Requests[0].Source == review.Source);
            Check(summary.JoinedTransportSamples == 1 && summary.UnjoinedTransportSamples == 0 && summary.ReviewReady == 1 && summary.ConfirmAttempts == 2 && summary.ReceiptBackedSuccesses == 1 && summary.ChangedReceipts == 1 && summary.CompleteCapturedScope);
            Check(report.Events[1].Kind == WorkflowObservedKind.ConfirmThrew && !report.Events[1].ReceiptObserved && report.Events[2].ReceiptObserved);
            var terminal = report.Requests[0].Module!.Events.Single(e => e.Kind == ObservationKind.Terminal);
            Check(terminal.Request == report.Transport.Samples.Single().Request && terminal.Outcome == ObservationOutcome.Proposal);
            new Ncma.Editor.Services.EditorWorkspace(f.Owner).History(new Ncma.Editor.Services.EditorWorkspace(f.Owner).Stamp, false);
            Check(f.Before.SequenceEqual(f.Owner.Document.CaptureBytes())); _ = AiWorkflowObservationEvidence.Encode(report);
        });
        yield return ("M8.7-E2B3 seal pending is detached and does not cancel transport or mutate authoring", () => {
            using var f = new Fixture(); f.Transport.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Workflow.BeginObservation(WorkflowIdentity()); f.Start(); f.Workflow.Pump(); Until(() => Volatile.Read(ref f.Transport.Calls) == 1);
            var report = f.Workflow.SealObservation(); byte[] frozen = AiWorkflowObservationEvidence.Encode(report);
            var summary = AiWorkflowObservationEvidence.Inspect(report); Check(summary.PendingModuleRequests == 1 && !summary.CompleteCapturedScope && report.Transport.Pending == 1);
            Check(f.Transport.Disposals == 0 && f.Owner.Edit!.State.UndoCount == 0);
            f.Transport.Hold.TrySetResult(); f.Complete(); Check(f.Workflow.Review is not null);
            Check(AiWorkflowObservationEvidence.Encode(report).SequenceEqual(frozen));
        });
        yield return ("M8.7-E2B3 cancel final result remains cancelled despite late transport completion", () => {
            using var f = new Fixture(); f.Transport.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Workflow.BeginObservation(WorkflowIdentity()); f.Start(); f.Workflow.Pump(); Until(() => Volatile.Read(ref f.Transport.Calls) == 1);
            Check(!f.Workflow.TryUnregister() && f.Transport.Disposals == 0); f.Transport.Hold.TrySetResult(); Until(f.Workflow.TryUnregister);
            var report = f.Workflow.SealObservation(); var summary = AiWorkflowObservationEvidence.Inspect(report);
            Check(summary.JoinedTransportSamples == 1 && summary.UndrainedModuleRequests == 0 && summary.ConfirmAttempts == 0 && summary.ReceiptBackedSuccesses == 0);
            Check(report.Requests[0].Module!.Events.Single(e => e.Kind == ObservationKind.Terminal).Outcome == ObservationOutcome.Cancelled);
            Check(f.Transport.Disposals == 1 && f.Owner.Edit!.State.UndoCount == 0 && f.Before.SequenceEqual(f.Owner.Document.CaptureBytes()));
        });
        yield return ("M8.7-E2B3 sequential requests retain distinct mailbox identities after bridge release", () => {
            using var f = new Fixture(); f.Workflow.BeginObservation(WorkflowIdentity());
            for (int i = 0; i < 2; ++i) {
                f.Transport.Proposal = Draft("ncma.scene.transaction", new { operations = new[] { new { op = "rename", objectId = f.Id, name = "Observed " + i } } });
                f.Start(); f.Complete(); var review = f.Workflow.Review!; Check(f.Workflow.Confirm(review, review.Fingerprint, true).Status == "ok");
            }
            Until(f.Workflow.TryUnregister); var report = f.Workflow.SealObservation(); var summary = AiWorkflowObservationEvidence.Inspect(report);
            Check(summary.Requests == 2 && summary.JoinedTransportSamples == 2 && summary.ReceiptBackedSuccesses == 2 && summary.CompleteCapturedScope && f.Owner.Edit!.State.UndoCount == 2);
            Check(report.Transport.Samples.Select(s => s.Request.SessionId).Distinct().Count() == 2);
        });
        yield return ("M8.7-E2B3 rejects cross-run session request counters module hash and event tampering", () => {
            var report = ObservedSuccess(); var sample = report.Transport.Samples[0];
            Reject(() => AiWorkflowObservationEvidence.Encode(report with { Transport = report.Transport with { Identity = report.Identity with { RunId = Guid.NewGuid() } } }));
            Reject(() => AiWorkflowObservationEvidence.Encode(report with { Transport = report.Transport with { Samples = [sample with { Request = sample.Request with { SessionId = Guid.NewGuid() } }] } }));
            Reject(() => AiWorkflowObservationEvidence.Encode(report with { Transport = report.Transport with { Pending = 1 } }));
            Reject(() => AiWorkflowObservationEvidence.Encode(report with { Requests = [report.Requests[0] with { Module = report.Requests[0].Module! with { EvidenceSha256 = new('0', 64) } }] }));
            Reject(() => AiWorkflowObservationEvidence.Encode(report with { Events = [report.Events[0], report.Events[0]] }));
            Reject(() => AiWorkflowObservationEvidence.Encode(report with { Events = [report.Events[0], report.Events[1] with { ReviewFingerprint = new('C', 64) }] }));
            Reject(() => AiWorkflowObservationEvidence.Encode(report with { Transport = report.Transport with { Samples = [], Invoked = 0, Completed = 0 } }));
            _ = AiWorkflowObservationEvidence.Encode(report);
        });
        yield return ("M8.7-E2B3 domain event budget preserves business commit and marks missing coverage", () => {
            using var f = new Fixture(); f.Workflow.BeginObservation(WorkflowIdentity(), eventCapacity: 1); f.Start(); f.Complete();
            var review = f.Workflow.Review!; Check(f.Workflow.Confirm(review, review.Fingerprint, true).Status == "ok"); Until(f.Workflow.TryUnregister);
            var report = f.Workflow.SealObservation(); var summary = AiWorkflowObservationEvidence.Inspect(report);
            Check(report.Events.Length == 1 && report.DroppedEvents == 1 && !summary.CompleteCapturedScope && summary.ReceiptBackedSuccesses == 0 && f.Owner.Edit!.State.UndoCount == 1);
        });
        yield return ("M8.7-E2B3 request budget retains unjoined transport explicitly without blocking next request", () => {
            using var f = new Fixture(); f.Workflow.BeginObservation(WorkflowIdentity(), requestCapacity: 1);
            for (int i = 0; i < 2; ++i) { f.Start(); f.Complete(); }
            Until(f.Workflow.TryUnregister); var report = f.Workflow.SealObservation(); var summary = AiWorkflowObservationEvidence.Inspect(report);
            Check(report.Requests.Length == 1 && report.DroppedRequests == 1 && summary.JoinedTransportSamples == 1 && summary.UnjoinedTransportSamples == 1 && !summary.CompleteCapturedScope && f.Transport.Calls == 2);
        });
        yield return ("M8.7-E2B3 direct original service commit is not misreported as observed workflow confirmation", () => {
            using var f = new Fixture(); f.Workflow.BeginObservation(WorkflowIdentity()); f.Start(); f.Complete();
            var review = f.Workflow.Review!; Check(f.Domains.Confirm(review, review.Fingerprint, true).Status == "ok"); Until(f.Workflow.TryUnregister);
            var report = f.Workflow.SealObservation(); var summary = AiWorkflowObservationEvidence.Inspect(report);
            Check(report.WorkflowConfirmCallsOnly && summary.ConfirmAttempts == 0 && summary.ReceiptBackedSuccesses == 0 && f.Owner.Edit!.State.UndoCount == 1);
            Check(summary.Scope.Contains("not all domain commits", StringComparison.Ordinal));
        });
        yield return ("M8.7-E2B3 bounded numeric export excludes transport strings and preserves existing file", () => {
            var report = ObservedSuccess(); var sample = report.Transport.Samples[0];
            var safe = report with { Transport = report.Transport with { Samples = [sample with { Diagnostic = sample.Diagnostic with { Code = "SECRET_BODY", ExceptionType = "SECRET_KEY" } }] } };
            byte[] bytes = AiWorkflowObservationEvidence.Encode(safe); string text = Encoding.UTF8.GetString(bytes);
            Check(!text.Contains("SECRET", StringComparison.Ordinal) && !text.Contains("Rename the selected", StringComparison.Ordinal));
            Reject(() => AiWorkflowObservationEvidence.Encode(report, 1));
            using var json = JsonDocument.Parse(bytes); Check(json.RootElement.GetProperty("Version").GetInt32() == 1 && json.RootElement.GetProperty("Summary").GetProperty("ReceiptBackedSuccesses").GetInt32() == 1);
            string root = Path.Combine(Path.GetTempPath(), "ncma-workflow-observation-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string file = Path.Combine(root, "joined.ncobserve"); var receipt = AiWorkflowObservationEvidence.ExportNew(file, safe);
            Check(receipt.Sha256 == Convert.ToHexString(SHA256.HashData(bytes)) && File.ReadAllBytes(file).SequenceEqual(bytes));
            try { AiWorkflowObservationEvidence.ExportNew(file, safe); throw new Exception("Expected CreateNew collision"); } catch (IOException) { }
            Check(File.ReadAllBytes(file).SequenceEqual(bytes)); // Retain exact fixture evidence.
        });
    }
}
