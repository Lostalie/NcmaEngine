using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Ncma.Editor.Modules;
using Ncma.Modules;

internal static partial class AuthoringWorkspaceTests
{
    private static ObservationIdentity WorkspaceIdentity() => new(Guid.NewGuid(), new('A', 64), new('B', 64),
        ObservationConfiguration.Debug, ObservationVariant.Ai, Stopwatch.Frequency);
    private static AiWorkspaceObservationRecord WorkspaceRecord()
    {
        using var f = new Fixture(); f.Configure(); f.Ui.BeginObservation(WorkspaceIdentity());
        _ = f.Prepare(); Check(f.Ui.TryClose()); return f.Ui.SealObservation();
    }
    private static void ObservationReject(Action action)
    {
        try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException) { return; }
        throw new Exception("Expected workspace evidence rejection");
    }
    private static IEnumerable<(string, Action)> ObservationCases()
    {
        yield return ("M8.7-E2E4 observer default off and invalid limits grant no authority", () => {
            using var f = new Fixture(true); f.Remote(); Reject(() => f.Ui.SealObservation());
            foreach (var limits in new[] { (0, 1, 1), (33, 1, 1), (1, 0, 1), (1, 1048577, 1048577), (1, 2, 1), (1, 1, 8388609) })
                Reject(() => f.Ui.BeginObservation(WorkspaceIdentity(), limits.Item1, limits.Item2, limits.Item3));
            Reject(() => f.Ui.BeginObservation(WorkspaceIdentity() with { Variant = ObservationVariant.Default }));
            Reject(() => f.Ui.BeginObservation(WorkspaceIdentity() with { TimestampFrequency = 1 }));
            Check(f.Transport.Calls == 0 && f.Domains.RegisteredModules.Length == 0);
            _ = f.Prepare(); Reject(() => f.Ui.SealObservation()); Check(f.Transport.Calls == 0);
        });
        yield return ("M8.7-E2E4 observer owner single boundary and prompt reentry", () => {
            using var f = new Fixture();
            Task.Run(() => { Reject(() => f.Ui.BeginObservation(WorkspaceIdentity())); Reject(() => f.Ui.SealObservation()); }).GetAwaiter().GetResult();
            Check(f.Ui.ConfigureRemote("fixture-model", _ => {
                Reject(() => f.Ui.BeginObservation(WorkspaceIdentity())); return "fixture-not-a-user-key";
            }));
            f.Ui.BeginObservation(WorkspaceIdentity()); Reject(() => f.Ui.BeginObservation(WorkspaceIdentity()));
            _ = f.Ui.SealObservation(); Reject(() => f.Ui.SealObservation()); Reject(() => f.Ui.BeginObservation(WorkspaceIdentity()));
            using var late = new Fixture(); late.Configure(); _ = late.Prepare(); Check(late.Ui.Disconnect());
            Reject(() => late.Ui.BeginObservation(WorkspaceIdentity()));
        });
        yield return ("M8.7-E2E4 empty closed workspace seal leaves explicit empty scope", () => {
            using var f = new Fixture(); var id = WorkspaceIdentity(); f.Ui.BeginObservation(id);
            Check(f.Ui.TryClose()); var r = f.Ui.SealObservation();
            Check(r.Identity == id && r.StartedWorkflows == 0 && r.Workflows.Length == 0 && r.DroppedWorkflows == 0 && r.FailedWorkflows == 0);
            Check(!r.PerformanceAccepted && !r.GrantsExecutionAuthority && !r.ProvenanceVerified);
            Check(AiWorkspaceObservationEvidence.DecodeExpected(AiWorkspaceObservationEvidence.Encode(r), id).Workflows.Length == 0);
            Reject(() => f.Ui.BeginObservation(id));
        });
        yield return ("M8.7-E2E4 replacement seals retired and active prefixes separately", () => {
            using var f = new Fixture(); f.Configure(); var id = WorkspaceIdentity(); var config = f.Ui.Profile!.ConfigurationId;
            f.Ui.BeginObservation(id); _ = f.Prepare(); _ = f.Prepare();
            var r = f.Ui.SealObservation(); Check(r.StartedWorkflows == 2 && r.Workflows.Length == 2 && r.FailedWorkflows == 0);
            Check(r.Workflows[0].ReleasedBeforeSeal && !r.Workflows[1].ReleasedBeforeSeal && r.Workflows.All(w => w.ConfigurationId == config));
            Check(!r.MultipleCapturedConfigurations && f.Domains.RegisteredModules.Length == 1 && f.Ui.Status == "send-review");
            _ = AiWorkspaceObservationEvidence.Decode(AiWorkspaceObservationEvidence.Encode(r));
        });
        yield return ("M8.7-E2E4 configuration epochs separated without recipient or secret", () => {
            using var f = new Fixture(true); f.Remote(); f.Ui.BeginObservation(WorkspaceIdentity());
            _ = f.Prepare(); var first = f.Ui.Profile!.ConfigurationId; f.Remote(); _ = f.Prepare();
            Check(f.Ui.TryClose()); var r = f.Ui.SealObservation();
            Check(r.Workflows.Length == 2 && r.Workflows[0].ConfigurationId == first && r.MultipleCapturedConfigurations);
            string json = Encoding.UTF8.GetString(AiWorkspaceObservationEvidence.Encode(r));
            Check(!json.Contains("fixture-model") && !json.Contains("fixture-not-a-user-key") && !json.Contains("deepseek") && f.Transport.Calls == 0);
        });
        yield return ("M8.7-E2E4 original send and commit human gates retained with actual wrapper receipts", () => {
            using var f = new Fixture(true); f.Remote(); f.Ui.BeginObservation(WorkspaceIdentity());
            var send = f.Prepare(); Reject(() => f.Ui.Send(send, send.Observation.Fingerprint, false));
            Check(f.Transport.Calls == 0 && f.Owner.Edit!.State.UndoCount == 0);
            f.Ui.Send(send, send.Observation.Fingerprint, true);
            Until(() => { f.Domains.PumpModules(); return f.Ui.Status != "pending"; });
            var review = f.Ui.RefreshReview()!; Reject(() => f.Ui.Confirm(review, review.Fingerprint, false));
            Check(f.Owner.Document.GetObjectName(f.Id) == "Before" && f.Owner.Edit!.State.UndoCount == 0);
            Check(f.Ui.Confirm(review, review.Fingerprint, true).Status == "ok"); Check(f.Ui.TryClose());
            var r = f.Ui.SealObservation(); var d = AiWorkflowObservationEvidence.Decode(r.Workflows.Single().WorkflowEvidence);
            Check(d.Summary.ConfirmAttempts == 2 && d.Summary.ReceiptBackedSuccesses == 1 && d.Summary.ChangedReceipts == 1);
            Check(f.Transport.Calls == 1 && f.Owner.Document.GetObjectName(f.Id) == "After" && f.Owner.Edit!.State.UndoCount == 1);
        });
        yield return ("M8.7-E2E4 pending seal does not cancel drain or release transport", () => {
            using var f = new Fixture(true); f.Remote(); f.Transport.Block = true; f.Ui.BeginObservation(WorkspaceIdentity());
            var send = f.Prepare(); f.Ui.Send(send, send.Observation.Fingerprint, true); f.Domains.PumpModules();
            Until(() => Volatile.Read(ref f.Transport.Calls) == 1);
            Check(!f.Ui.TryClose() && f.Domains.RegisteredModules.Length == 1);
            var r = f.Ui.SealObservation(); byte[] bytes = AiWorkspaceObservationEvidence.Encode(r);
            var d = AiWorkflowObservationEvidence.Decode(r.Workflows.Single().WorkflowEvidence);
            Check(!r.Workflows[0].ReleasedBeforeSeal && d.Transport.Pending == 1 && !d.Summary.CompleteCapturedScope);
            Check(f.Transport.Disposals == 0 && f.Domains.RegisteredModules.Length == 1 && f.Ui.Status == "draining");
            f.Transport.Hold.SetResult(); Until(f.Ui.TryClose);
            Check(f.Transport.Disposals == 1 && bytes.SequenceEqual(AiWorkspaceObservationEvidence.Encode(r)));
        });
        yield return ("M8.7-E2E4 bounded prefix records capacity and reserved byte gaps", () => {
            foreach (bool capacity in new[] { true, false }) {
                using var f = new Fixture(); f.Configure();
                f.Ui.BeginObservation(WorkspaceIdentity(), workflowCapacity: capacity ? 1 : 32, totalByteLimit: AiWorkspaceObservationEvidence.MaximumWorkflowBytes);
                _ = f.Prepare(); _ = f.Prepare(); _ = f.Prepare(); Check(f.Ui.TryClose()); var r = f.Ui.SealObservation();
                Check(r.StartedWorkflows == 3 && r.Workflows.Length == 1 && r.DroppedWorkflows == 2 && r.FailedWorkflows == 0);
                _ = AiWorkspaceObservationEvidence.Decode(AiWorkspaceObservationEvidence.Encode(r));
            }
        });
        yield return ("M8.7-E2E4 serialization failure is evidence gap not business failure or retry", () => {
            using var f = new Fixture(); f.Configure(); f.Ui.BeginObservation(WorkspaceIdentity(), workflowCapacity: 1, workflowByteLimit: 1, totalByteLimit: 1);
            _ = f.Prepare(); _ = f.Prepare(); Check(f.Ui.TryClose()); var r = f.Ui.SealObservation();
            Check(r.StartedWorkflows == 2 && r.Workflows.Length == 0 && r.FailedWorkflows == 1 && r.DroppedWorkflows == 1);
            Check(f.Owner.Edit!.State.UndoCount == 0 && f.Domains.RegisteredModules.Length == 0);
            _ = AiWorkspaceObservationEvidence.Decode(AiWorkspaceObservationEvidence.Encode(r));
        });
        yield return ("M8.7-E2E4 consumer exact identity nested validation and detached bytes", () => {
            var r = WorkspaceRecord(); byte[] data = AiWorkspaceObservationEvidence.Encode(r);
            var decoded = AiWorkspaceObservationEvidence.DecodeExpected(data, r.Identity);
            Check(data.SequenceEqual(AiWorkspaceObservationEvidence.Encode(decoded)));
            ObservationReject(() => AiWorkspaceObservationEvidence.DecodeExpected(data, r.Identity with { RunId = Guid.NewGuid() }));
            decoded.Workflows[0].WorkflowEvidence[0] = 0;
            Check(data.SequenceEqual(AiWorkspaceObservationEvidence.Encode(r)));
            ObservationReject(() => AiWorkspaceObservationEvidence.Encode(decoded));
            ObservationReject(() => AiWorkspaceObservationEvidence.Encode(r with { StartedWorkflows = 2 }));
            ObservationReject(() => AiWorkspaceObservationEvidence.Encode(r with { Workflows = [r.Workflows[0] with { Intent = AiAuthoringIntent.UiDocument }] }));
        });
        yield return ("M8.7-E2E4 consumer rejects malformed budgets duplicates and forged scope", () => {
            var r = WorkspaceRecord(); string json = Encoding.UTF8.GetString(AiWorkspaceObservationEvidence.Encode(r));
            foreach (string text in new[] { json + " ", json.Replace("\"Version\":1", "\"Version\":1,\"Version\":1"),
                json.Replace("\"Version\":1", "\"Version\":1,\"Extra\":true"), json.Replace("\"PerformanceAccepted\":false", "\"PerformanceAccepted\":true"), "{}" })
                ObservationReject(() => AiWorkspaceObservationEvidence.Decode(Encoding.UTF8.GetBytes(text)));
            ObservationReject(() => AiWorkspaceObservationEvidence.Decode(new byte[AiWorkspaceObservationEvidence.MaximumBytes + 1]));
            ObservationReject(() => AiWorkspaceObservationEvidence.Encode(r with { Workflows = Enumerable.Repeat(r.Workflows[0], 33).ToArray() }));
            ObservationReject(() => AiWorkspaceObservationEvidence.Encode(r with { StartedWorkflows = 2, Workflows = [r.Workflows[0], r.Workflows[0]] }));
            ObservationReject(() => AiWorkspaceObservationEvidence.Encode(r with { Workflows = [r.Workflows[0] with { ConfigurationId = Guid.Empty }] }));
        });
        yield return ("M8.7-E2E4 export original create new readback no overwrite or cleanup", () => {
            if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Windows evidence environment required");
            var r = WorkspaceRecord(); string root = Path.Combine(Path.GetTempPath(), "NcmaWorkspaceEvidence", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); string path = Path.Combine(root, "workspace.ncobserve");
            var receipt = AiWorkspaceObservationEvidence.ExportNew(path, r); byte[] bytes = File.ReadAllBytes(path);
            Check(receipt.Bytes == bytes.Length && receipt.Sha256 == Convert.ToHexString(SHA256.HashData(bytes)));
            ObservationReject(() => AiWorkspaceObservationEvidence.ExportNew(path, r)); Check(bytes.SequenceEqual(File.ReadAllBytes(path)));
            Check(AiWorkspaceObservationEvidence.DecodeExpected(bytes, r.Identity).Workflows.Length == 1);
        });
    }
}
