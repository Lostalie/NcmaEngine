using System.Security.Cryptography;
using System.Text;
using Ncma.Application;
using Ncma.Editor.Modules;
using Ncma.Editor.Services;
using Ncma.Modules;

internal static partial class AuthoringWorkspaceTests
{
    private static string JointHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static MeasurementInputsPlan JointPlan(ProcessMeasurementIdentity identity) => new(identity, 1000, 1000, 1, 4);
    private static byte[] JointProcess(ProcessMeasurementIdentity id)
    {
        // Synthetic missing window, different frequency from Stopwatch, not real sampling or host provenance.
        var series = new ProcessSeriesReport(1, id.RunId, 1, 1, 1000, 0, 0, 1000, 1000, 1, 1, 1, 0, ProcessSeriesStop.ExplicitSeal, []);
        return ProcessMeasurementEvidence.Encode(new(1, id, series, false, 0, 0, 0, 0, 0));
    }
    private static AiWorkspaceProcessRecord JointRecord(AiWorkspaceObservationRecord? supplied = null)
    {
        var workspace = supplied ?? WorkspaceRecord(); var id = workspace.Identity;
        var processId = new ProcessMeasurementIdentity(id.RunId, id.SourceSha256, new('C', 64), new('D', 64), id.WorkloadSha256,
            MeasurementConfiguration.Debug, MeasurementVariant.Ai, MeasurementHost.Editor);
        var plan = JointPlan(processId);
        var inputs = new MeasurementInputsReceipt(plan.Fingerprint, processId, 1, MeasurementScenario.ControlledFixture, 4, 1, 8, new('E', 64), new('F', 64));
        byte[] left = AiWorkspaceObservationEvidence.Encode(workspace), right = JointProcess(processId);
        return AiWorkspaceProcessEvidence.Create(plan, inputs, id, left, right, JointHash(left), JointHash(right));
    }
    private static IEnumerable<(string, Action)> JointEvidenceCases()
    {
        yield return ("M8.7-E2E5 joint canonical bytes expected identity hashes and no authority", () => {
            var r = JointRecord(); byte[] bytes = AiWorkspaceProcessEvidence.Encode(r);
            var copy = AiWorkspaceProcessEvidence.DecodeExpected(bytes, JointPlan(r.ProcessIdentity), r.WorkspaceIdentity, r.WorkspaceSha256, r.ProcessSha256);
            Check(bytes.SequenceEqual(AiWorkspaceProcessEvidence.Encode(copy)) && r.WorkspaceEvidence.SequenceEqual(copy.WorkspaceEvidence) && r.ProcessEvidence.SequenceEqual(copy.ProcessEvidence));
            Check(!copy.PerformanceAccepted && !copy.CommonClockVerified && !copy.GrantsExecutionAuthority && !copy.ProvenanceVerified && !copy.WorkloadExecutionVerified);
            ObservationReject(() => AiWorkspaceProcessEvidence.DecodeExpected(bytes, JointPlan(r.ProcessIdentity), r.WorkspaceIdentity, new('0', 64), r.ProcessSha256));
            ObservationReject(() => AiWorkspaceProcessEvidence.DecodeExpected(bytes, JointPlan(r.ProcessIdentity), r.WorkspaceIdentity with { RunId = Guid.NewGuid() }, r.WorkspaceSha256, r.ProcessSha256));
        });
        yield return ("M8.7-E2E5 cross run source workload configuration variant host rejected", () => {
            var r = JointRecord();
            foreach (var id in new[] { r.ProcessIdentity with { RunId = Guid.NewGuid() }, r.ProcessIdentity with { SourceSha256 = new('0', 64) },
                r.ProcessIdentity with { WorkloadSha256 = new('0', 64) }, r.ProcessIdentity with { Configuration = MeasurementConfiguration.Release },
                r.ProcessIdentity with { Variant = MeasurementVariant.Default }, r.ProcessIdentity with { Variant = MeasurementVariant.AiExtensions } }) {
                var plan = JointPlan(id); var inputs = r.Inputs with { Identity = id, PlanFingerprint = plan.Fingerprint }; byte[] process = JointProcess(id);
                ObservationReject(() => AiWorkspaceProcessEvidence.Create(plan, inputs, r.WorkspaceIdentity, r.WorkspaceEvidence, process, r.WorkspaceSha256, JointHash(process)));
            }
            ObservationReject(() => JointPlan(r.ProcessIdentity with { Host = MeasurementHost.Player }));
            ObservationReject(() => AiWorkspaceProcessEvidence.DecodeExpected(AiWorkspaceProcessEvidence.Encode(r), JointPlan(r.ProcessIdentity with { ProductManifestSha256 = new('0', 64) }), r.WorkspaceIdentity, r.WorkspaceSha256, r.ProcessSha256));
        });
        yield return ("M8.7-E2E5 declared nonempty workload and input receipt invariants", () => {
            var r = JointRecord();
            foreach (var input in new[] { r.Inputs with { PlanFingerprint = new('0', 64) }, r.Inputs with { CheckSequence = 0 },
                r.Inputs with { Scenario = MeasurementScenario.EditorIdle }, r.Inputs with { WorkloadFiles = 0 }, r.Inputs with { WorkloadFiles = 513 },
                r.Inputs with { ProductFiles = 3 }, r.Inputs with { ProductFiles = 1025 }, r.Inputs with { PayloadBytes = 0 },
                r.Inputs with { PayloadBytes = MeasurementInputLease.MaximumPayloadBytes + 1 }, r.Inputs with { WorkloadFilesSha256 = "invalid" } })
                ObservationReject(() => AiWorkspaceProcessEvidence.Encode(r with { Inputs = input }));
            var openWorld = r with { Inputs = r.Inputs with { Scenario = MeasurementScenario.OpenWorldExploration } };
            Check(!AiWorkspaceProcessEvidence.Decode(AiWorkspaceProcessEvidence.Encode(openWorld)).WorkloadExecutionVerified);
        });
        yield return ("M8.7-E2E5 process cadence plan capacity and frame limit crosschecks", () => {
            var r = JointRecord();
            foreach (var plan in new[] { new MeasurementInputsPlan(r.ProcessIdentity, 2000, 1000, 2, 4), new MeasurementInputsPlan(r.ProcessIdentity, 1000, 500, 2, 4),
                new MeasurementInputsPlan(r.ProcessIdentity, 1000, 1000, 2, 4) })
                ObservationReject(() => AiWorkspaceProcessEvidence.Create(plan, r.Inputs with { PlanFingerprint = plan.Fingerprint }, r.WorkspaceIdentity,
                    r.WorkspaceEvidence, r.ProcessEvidence, r.WorkspaceSha256, r.ProcessSha256));
            var report = ProcessMeasurementEvidence.DecodeExpected(r.ProcessEvidence, r.ProcessIdentity) with { BoundToLoop = true, Iterations = 5, CompletedIterations = 5 };
            byte[] more = ProcessMeasurementEvidence.Encode(report);
            ObservationReject(() => AiWorkspaceProcessEvidence.Create(JointPlan(r.ProcessIdentity), r.Inputs, r.WorkspaceIdentity, r.WorkspaceEvidence, more, r.WorkspaceSha256, JointHash(more)));
        });
        yield return ("M8.7-E2E5 nested tamper and detached caller arrays", () => {
            var r = JointRecord(); byte[] before = AiWorkspaceProcessEvidence.Encode(r);
            var copied = AiWorkspaceProcessEvidence.Create(JointPlan(r.ProcessIdentity), r.Inputs, r.WorkspaceIdentity, r.WorkspaceEvidence, r.ProcessEvidence, r.WorkspaceSha256, r.ProcessSha256);
            r.WorkspaceEvidence[0] = 0; r.ProcessEvidence[0] = 0;
            Check(before.SequenceEqual(AiWorkspaceProcessEvidence.Encode(copied)));
            ObservationReject(() => AiWorkspaceProcessEvidence.Encode(r));
            ObservationReject(() => AiWorkspaceProcessEvidence.Encode(r with { WorkspaceSha256 = JointHash(r.WorkspaceEvidence), ProcessSha256 = JointHash(r.ProcessEvidence) }));
            var decoded = AiWorkspaceProcessEvidence.Decode(before); decoded.ProcessEvidence[0] = 0;
            Check(before.SequenceEqual(AiWorkspaceProcessEvidence.Encode(copied)));
        });
        yield return ("M8.7-E2E5 malformed duplicate noncanonical derived and byte budgets", () => {
            var r = JointRecord(); string json = Encoding.UTF8.GetString(AiWorkspaceProcessEvidence.Encode(r));
            foreach (string text in new[] { "{}", "[]", json + " ", json.Replace("\"Version\":1", "\"Version\":1,\"Version\":1"),
                json.Replace("\"Version\":1", "\"Version\":1,\"Extra\":[]"), json.Replace("\"PerformanceAccepted\":false", "\"PerformanceAccepted\":true") })
                ObservationReject(() => AiWorkspaceProcessEvidence.Decode(Encoding.UTF8.GetBytes(text)));
            ObservationReject(() => AiWorkspaceProcessEvidence.Decode(new byte[AiWorkspaceProcessEvidence.MaximumBytes + 1]));
            ObservationReject(() => AiWorkspaceProcessEvidence.Encode(r with { WorkspaceEvidence = [] }));
            ObservationReject(() => AiWorkspaceProcessEvidence.Encode(r with { ProcessEvidence = new byte[ProcessSeriesEvidence.MaximumBytes + 1] }));
        });
        yield return ("M8.7-E2E5 empty workspace and unbound missing process remain explicit", () => {
            using var f = new Fixture(); f.Ui.BeginObservation(WorkspaceIdentity()); var r = JointRecord(f.Ui.SealObservation());
            var summary = AiWorkspaceProcessEvidence.Inspect(r);
            Check(summary.StartedWorkflows == 0 && summary.Workflows.Length == 0 && !summary.ProcessBoundToLoop && !summary.ProcessCadenceComplete && !summary.ProcessCountersAvailable);
            Check(!summary.PerformanceAccepted && !summary.CommonClockVerified && !summary.ProvenanceVerified);
            Check(AiWorkspaceProcessEvidence.Decode(AiWorkspaceProcessEvidence.Encode(r)).Inputs.WorkloadFiles == 1);
        });
        yield return ("M8.7-E2E5 configuration groups remain separate no aggregate latency", () => {
            using var f = new Fixture(); f.Configure(); f.Ui.BeginObservation(WorkspaceIdentity());
            _ = f.Prepare(); f.Configure(); _ = f.Prepare(); Check(f.Ui.TryClose());
            var r = JointRecord(f.Ui.SealObservation()); var summary = AiWorkspaceProcessEvidence.Inspect(r);
            Check(summary.Workflows.Length == 2 && summary.MultipleCapturedConfigurations && summary.Workflows[0].ConfigurationId != summary.Workflows[1].ConfigurationId);
            Check(summary.Workflows.All(w => w.Workflow.Requests == 0 && w.Workflow.ReceiptBackedSuccesses == 0));
            Check(!Encoding.UTF8.GetString(AiWorkspaceProcessEvidence.Encode(r)).Contains("fixture-model"));
        });
        yield return ("M8.7-E2E5 workspace dropped failed scope is not silently complete", () => {
            using var f = new Fixture(); f.Configure(); f.Ui.BeginObservation(WorkspaceIdentity(), workflowCapacity: 1, workflowByteLimit: 1, totalByteLimit: 1);
            _ = f.Prepare(); _ = f.Prepare(); Check(f.Ui.TryClose()); var r = JointRecord(f.Ui.SealObservation());
            var summary = AiWorkspaceProcessEvidence.Inspect(r);
            Check(summary.StartedWorkflows == 2 && summary.FailedWorkflows == 1 && summary.DroppedWorkflows == 1 && summary.Workflows.Length == 0 && !summary.PerformanceAccepted);
            ObservationReject(() => AiWorkspaceProcessEvidence.DecodeExpected(AiWorkspaceProcessEvidence.Encode(r), new(r.ProcessIdentity, 2000, 1000, 2, 4), r.WorkspaceIdentity, r.WorkspaceSha256, r.ProcessSha256));
        });
        yield return ("M8.7-E2E5 joint export original create new hash readback no overwrite", () => {
            if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Windows evidence environment required");
            var r = JointRecord(); string root = Path.Combine(Path.GetTempPath(), "NcmaJointEvidence", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); string path = Path.Combine(root, "joint.ncobserve");
            var receipt = AiWorkspaceProcessEvidence.ExportNew(path, r); byte[] bytes = File.ReadAllBytes(path);
            Check(receipt.Bytes == bytes.Length && receipt.Sha256 == JointHash(bytes));
            ObservationReject(() => AiWorkspaceProcessEvidence.ExportNew(path, r)); Check(bytes.SequenceEqual(File.ReadAllBytes(path)));
            _ = AiWorkspaceProcessEvidence.DecodeExpected(bytes, JointPlan(r.ProcessIdentity), r.WorkspaceIdentity, r.WorkspaceSha256, r.ProcessSha256);
        });
    }
}
