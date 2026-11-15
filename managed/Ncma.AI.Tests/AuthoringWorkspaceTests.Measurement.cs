using Ncma.AI;
using Ncma.Editor.Modules;

internal static partial class AuthoringWorkspaceTests
{
    private static AiWorkspaceMeasurementLimits Limits(int attempts = 2) => new(new(Guid.NewGuid(), 1, AiProvider.DeepSeek,
        AiDeployment.RemoteHttps, AiProvider.DeepSeek.RemoteEndpoint, "fixture-model", new(Guid.NewGuid(), 1)), attempts);
    private static IEnumerable<(string, Action)> MeasurementBudgetCases()
    {
        yield return ("M8.7-E2E6 limits stable recipient digest excludes configuration credential identity", () => {
            Check(Limits().Fingerprint == Limits().Fingerprint && Limits(1).Fingerprint != Limits(2).Fingerprint);
            Reject(() => Limits(0)); Reject(() => Limits(33));
            using var f = new Fixture(); f.Ui.BeginMeasuredObservation(WorkspaceIdentity(), Limits());
            var s = f.Ui.MeasurementBudget!; Check(s.RemainingAttempts == 2 && !s.Closed && !s.GrantsExecutionAuthority && !s.CoversDirectTransportOrPlugins);
        });
        yield return ("M8.7-E2E6 conservative shared diagnostic authoring attempt quota no human bypass", () => {
            using var f = new Fixture(true); f.Remote(); f.Ui.BeginMeasuredObservation(WorkspaceIdentity(), Limits());
            var d = f.Ui.PrepareConnectionDiagnostic(); Reject(() => f.Ui.BeginConnectionDiagnostic(d, d.Fingerprint, false));
            var r = f.Prepare(); Reject(() => f.Ui.Send(r, r.Observation.Fingerprint, false));
            Reject(() => f.Ui.Send(r, r.Observation.Fingerprint, true));
            Check(f.Ui.MeasurementBudget!.ChargedAttempts == 2 && f.Transport.Calls == 0 && f.Owner.Edit!.State.UndoCount == 0);
        });
        yield return ("M8.7-E2E6 recipient mismatch and rotation cannot reset attempt quota", () => {
            using var f = new Fixture(true); f.Configure(); f.Ui.BeginMeasuredObservation(WorkspaceIdentity(), Limits(1));
            var wrong = f.Prepare(); Reject(() => f.Ui.Send(wrong, wrong.Observation.Fingerprint, true));
            Check(f.Ui.MeasurementBudget!.ChargedAttempts == 0 && f.Transport.Calls == 0);
            f.Remote(); var r = f.Prepare(); Reject(() => f.Ui.Send(r, r.Observation.Fingerprint, false));
            f.Remote(); var next = f.Prepare(); Reject(() => f.Ui.Send(next, next.Observation.Fingerprint, true));
            Check(f.Ui.MeasurementBudget!.ChargedAttempts == 1 && f.Transport.Calls == 0);
        });
        yield return ("M8.7-E2E6 measured seal stops new sends without closing workspace", () => {
            using var f = new Fixture(true); f.Remote(); f.Ui.BeginMeasuredObservation(WorkspaceIdentity(), Limits());
            var r = f.Prepare(); var report = f.Ui.SealMeasuredObservation();
            Check(f.Ui.Status == "send-review" && f.Domains.RegisteredModules.Length == 1 && f.Ui.MeasurementBudget!.Closed);
            Reject(() => f.Ui.Send(r, r.Observation.Fingerprint, true)); Reject(() => f.Ui.SealMeasuredObservation());
            Check(f.Transport.Calls == 0 && report.Workflows.Length == 1 && f.Ui.MeasurementBudget!.ChargedAttempts == 0);
        });
        yield return ("M8.7-E2E6 measured boundary owner duplicate and active diagnostic rejected", () => {
            using var f = new Fixture(); f.Configure();
            Task.Run(() => Reject(() => f.Ui.BeginMeasuredObservation(WorkspaceIdentity(), Limits()))).GetAwaiter().GetResult();
            _ = f.Ui.PrepareConnectionDiagnostic(); Reject(() => f.Ui.BeginMeasuredObservation(WorkspaceIdentity(), Limits()));
            Check(f.Ui.MeasurementBudget is null); Check(f.Ui.Disconnect());
            f.Ui.BeginMeasuredObservation(WorkspaceIdentity(), Limits()); Reject(() => f.Ui.BeginMeasuredObservation(WorkspaceIdentity(), Limits()));
            Task.Run(() => Reject(() => f.Ui.SealMeasuredObservation())).GetAwaiter().GetResult();
        });
        yield return ("M8.7-E2E6 active transport survives measured seal and remains explicitly drained", () => {
            using var f = new Fixture(true); f.Remote(); f.Transport.Block = true;
            f.Ui.BeginMeasuredObservation(WorkspaceIdentity(), Limits(1)); var r = f.Prepare();
            f.Ui.Send(r, r.Observation.Fingerprint, true); f.Domains.PumpModules(); Until(() => f.Transport.Calls == 1);
            var report = f.Ui.SealMeasuredObservation();
            Check(f.Ui.Status == "pending" && f.Transport.Disposals == 0 && f.Ui.MeasurementBudget!.Closed && f.Domains.RegisteredModules.Length == 1);
            Check(AiWorkflowObservationEvidence.Decode(report.Workflows.Single().WorkflowEvidence).Transport.Pending == 1);
            Check(!f.Ui.TryClose()); f.Transport.Hold.SetResult(); Until(f.Ui.TryClose);
            Check(f.Transport.Calls == 1 && f.Transport.Disposals == 1 && f.Ui.MeasurementBudget!.ChargedAttempts == 1);
        });
    }
}
