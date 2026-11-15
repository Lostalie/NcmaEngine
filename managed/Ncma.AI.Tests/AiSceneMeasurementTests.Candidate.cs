using System.Diagnostics;
using System.Text;
using Ncma.Application;
using Ncma.Editor.Modules;
using Ncma.Editor.Services;
using Ncma.Modules;

internal static partial class AiSceneMeasurementTests
{
    private sealed class CandidateClock : IFrameClock { public double Seconds => 0; }
    private sealed class CandidatePipeline : IFramePipeline
    {
        public bool PumpPlatform() => true;
        public void ApplyEditorIntents(FrameContext f) { } public void PumpAgent(FrameContext f) { }
        public void BeginPresentation(FrameContext f) { } public void SubmitInput(FrameContext f) { }
        public void AdvancePlay(FrameContext f) { } public void Present(FrameContext f) { }
    }
    private static (AiSceneCandidateRecord Record, PreparedAiSceneMeasurement Expected) CandidateRecord()
    {
        var f = new Fixture(); using var inputs = f.Lease(); inputs.Prepare(); var expected = f.Request.Prepare(inputs);
        var id = new ObservationIdentity(f.Request.Inputs.Identity.RunId, f.Request.Inputs.Identity.SourceSha256,
            f.Request.Inputs.Identity.WorkloadSha256, ObservationConfiguration.Release, ObservationVariant.Ai, Stopwatch.Frequency);
        var ai = new AiAuthoringWorkspace(null); ai.BeginMeasuredObservation(id, f.Request.Recipient.Limits());
        using var app = new ApplicationLifetime(); app.Start([]); var driver = new MeasurementRunDriver(f.Request.Inputs.Plan(), inputs);
        var status = driver.RunAndClose(app, new CandidateClock(), new CandidatePipeline());
        Check(ai.TryClose()); byte[] workspace = AiWorkspaceObservationEvidence.Encode(ai.SealMeasuredObservation());
        byte[] process = ProcessMeasurementEvidence.Encode(driver.ProcessReport!);
        byte[] joint = AiWorkspaceProcessEvidence.Encode(AiWorkspaceProcessEvidence.Create(f.Request.Inputs.Plan(), expected.Inputs, id,
            workspace, process, Hash(workspace), Hash(process)));
        // Synthetic data shape. Never claim the fixture EXE was executed or passed BindCurrentEditor.
        var record = new AiSceneCandidateRecord(1, expected.RequestSha256, f.Request.SceneSha256, expected.PolicySha256, expected.Inputs,
            new(Environment.ProcessId, new('A', 64), new('B', 64)), status, ai.MeasurementBudget, f.Request.SceneSha256,
            f.Request.SceneSha256, false, false, joint, null);
        return (record, expected);
    }
    private static IEnumerable<(string, Action)> CandidateEvidenceCases()
    {
        yield return ("M8.7-E2E8 closure evidence retains original joint bytes independent expectations", () => {
            var (record, expected) = CandidateRecord(); byte[] bytes = AiSceneCandidateEvidence.Encode(record);
            var copy = AiSceneCandidateEvidence.DecodeExpected(bytes, expected);
            byte[] jointCopy = copy.JointEvidence ?? throw new Exception("Expected retained joint evidence");
            Check(jointCopy.SequenceEqual(record.JointEvidence!) && !copy.PerformanceAccepted && !copy.ProvenanceVerified);
            jointCopy[0] = 0; Check(record.JointEvidence![0] != 0);
            Reject(() => AiSceneCandidateEvidence.DecodeExpected(AiSceneCandidateEvidence.Encode(record with { RequestSha256 = new('0', 64) }), expected));
        });
        yield return ("M8.7-E2E8 closure rejects contradictory state PID input policy and payload", () => {
            var (r, _) = CandidateRecord();
            foreach (var bad in new[] { r with { ApplicationRetained = true }, r with { InputsRetained = true }, r with { InitialSceneSha256 = null },
                r with { FinalSceneSha256 = null }, r with { Budget = r.Budget! with { Closed = false } }, r with { PolicySha256 = new('0', 64) },
                r with { Entry = r.Entry with { ProcessId = Environment.ProcessId + 1 } }, r with { Run = r.Run! with { CompletedFrames = 0 } },
                r with { Inputs = r.Inputs with { WorkloadFiles = 2 } }, r with { JointEvidence = [0] }, r with { JointEvidence = null },
                r with { FailureType = "message/path/secret" } }) Reject(() => AiSceneCandidateEvidence.Encode(bad));
        });
        yield return ("M8.7-E2E8 missing startup and retained prefixes are failures not success", () => {
            var (r, _) = CandidateRecord();
            var startup = r with { Run = null, Budget = null, InitialSceneSha256 = null, FinalSceneSha256 = null, JointEvidence = null, FailureType = "IOException" };
            Check(AiSceneCandidateEvidence.Decode(AiSceneCandidateEvidence.Encode(startup)).JointEvidence is null);
            var retained = r with { Run = r.Run! with { State = MeasurementRunState.Retained, ApplicationRetained = true, InputsRetained = true },
                ApplicationRetained = true, InputsRetained = true, FailureType = "MeasurementCloseRetained" };
            Check(AiSceneCandidateEvidence.Decode(AiSceneCandidateEvidence.Encode(retained)).ApplicationRetained);
            Reject(() => AiSceneCandidateEvidence.Encode(startup with { FailureType = null }));
            Reject(() => AiSceneCandidateEvidence.Encode(retained with { ApplicationRetained = false }));
        });
        yield return ("M8.7-E2E8 closure canonical bounded decoder rejects unknown duplicate and forged claims", () => {
            var (r, _) = CandidateRecord(); string json = Encoding.UTF8.GetString(AiSceneCandidateEvidence.Encode(r));
            foreach (string bad in new[] { "null", "{}", json + " ", json.Replace("\"Version\":1", "\"Version\":1,\"Version\":1"),
                json.Replace("\"Version\":1", "\"unknown\":true,\"Version\":1"), json.Replace("\"PerformanceAccepted\":false", "\"PerformanceAccepted\":true") })
                Reject(() => AiSceneCandidateEvidence.Decode(Encoding.UTF8.GetBytes(bad)));
            Reject(() => AiSceneCandidateEvidence.Decode(new byte[AiSceneCandidateEvidence.MaximumBytes + 1]));
        });
        yield return ("M8.7-E2E8 closure explicit CreateNew roundtrip never overwrites", () => {
            var (r, expected) = CandidateRecord(); string folder = Path.Combine(Path.GetTempPath(), "ncma-scene-evidence-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder); string path = Path.Combine(folder, "scene.ncobserve");
            var receipt = AiSceneCandidateEvidence.ExportNew(path, r); byte[] bytes = File.ReadAllBytes(path);
            Check(receipt.Bytes == bytes.Length && receipt.Sha256 == Hash(bytes)); _ = AiSceneCandidateEvidence.DecodeExpected(bytes, expected);
            Reject(() => AiSceneCandidateEvidence.ExportNew(path, r)); Check(bytes.SequenceEqual(File.ReadAllBytes(path)));
        });
    }
}
