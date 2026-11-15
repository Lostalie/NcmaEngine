#if NCMA_AI
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.Application;
using Ncma.Editor.App;
using Ncma.Editor.Modules;
using Ncma.Editor.Services;
using Ncma.Runtime;
using Ncma.Scene;

internal static partial class AiHostLifetimeTests
{
    private sealed class SceneFixture : IDisposable
    {
        internal readonly string Root, ScenePath;
        internal readonly byte[] SceneBytes;
        internal readonly MeasurementInputLease Inputs;
        internal readonly PreparedAiSceneMeasurement Prepared;
        private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
        internal SceneFixture(string output)
        {
            Root = Path.Combine(output, "scene-input-" + Guid.NewGuid().ToString("N"));
            string product = Path.Combine(Root, "product"), load = Path.Combine(Root, "workload"), capture = Path.Combine(Root, "capture.json");
            Directory.CreateDirectory(product); Directory.CreateDirectory(load);
            byte[] captureBytes = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, kind = "ncma.captured-build.v1", sourceInputsSha256 = new string('A', 64), files = Array.Empty<object>() });
            File.WriteAllBytes(capture, captureBytes);
            var files = new List<object>();
            foreach (string name in new[] { "NcmaEngine.exe", "NcmaEngine.dll", "NcmaEngine.deps.json", "NcmaEngine.runtimeconfig.json" }) {
                byte[] bytes = Encoding.UTF8.GetBytes("synthetic input, NEVER EXECUTED: " + name); File.WriteAllBytes(Path.Combine(product, name), bytes);
                files.Add(new { path = name, size = bytes.Length, sha256 = Sha(bytes) });
            }
#if DEBUG
            const MeasurementConfiguration configuration = MeasurementConfiguration.Debug;
#else
            const MeasurementConfiguration configuration = MeasurementConfiguration.Release;
#endif
            byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, product = "fixture-editor", configuration = configuration.ToString(), rid = "win-x64", tfm = "net8.0",
                publishMode = "framework-dependent", sourceInputsSha256 = new string('A', 64), captureManifestSha256 = Sha(captureBytes), aiEnabled = true, extensionsEnabled = false, files });
            File.WriteAllBytes(Path.Combine(product, DeploymentManifest.FileName), manifest);
            var scene = new SceneDocument("Controlled fixture"); scene.SetComponent(scene.CreateObject("Observed"), TransformData.Identity); SceneBytes = scene.CaptureBytes();
            ScenePath = Path.Combine(load, AiSceneMeasurementRequest.SceneFileName); File.WriteAllBytes(ScenePath, SceneBytes);
            byte[] workload = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, kind = "ncma.measurement-workload.v1", scenario = "controlled-fixture",
                files = new[] { new { path = AiSceneMeasurementRequest.SceneFileName, size = SceneBytes.Length, sha256 = Sha(SceneBytes) } } });
            File.WriteAllBytes(Path.Combine(load, MeasurementInputLease.WorkloadManifestName), workload);
            var request = new AiSceneMeasurementRequest(1, new(1, new(Guid.NewGuid(), new('A', 64), Sha(captureBytes), Sha(manifest), Sha(workload), configuration,
                MeasurementVariant.Ai, Ncma.Application.MeasurementHost.Editor), 3000, 1000, 3, 8, capture, product, load), Sha(SceneBytes),
                new("deepseek", Ncma.AI.AiDeployment.LocalLoopback, "http://127.0.0.1:8000/v1/chat/completions", "fixture-model", 2048, 60000, 2));
            Inputs = request.Inputs.OpenInputs(); Inputs.Prepare(); Prepared = request.Prepare(Inputs);
        }
        public void Dispose() => Inputs.Dispose(); // Keep all fixture files, release only this owned lease.
    }
    private static void SceneReject(Action action)
    { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or Ncma.Editor.Core.EditRejectedException) { return; } throw new Exception("Scene boundary must reject"); }
    private static IEnumerable<(string, Action)> SceneMeasurementCases(string plugins, string output)
    {
#if NCMA_EXTENSIONS
        yield return ("M8.7-E2E8 combined host refuses AI-only scene composition before Start", () => {
            using var fixture = new SceneFixture(output); var host = new CandidatePresentation(plugins, null, true, true, false);
            SceneReject(() => host.ConfigureAiSceneMeasurement(fixture.Prepared)); host.Dispose();
        });
#else
        yield return ("M8.7-E2E8 scene host owner prestart once and no preview or GPU substitution", () => {
            using var f = new SceneFixture(output);
            foreach (var host in new[] { new CandidatePresentation(plugins, null, true, false, false), new CandidatePresentation(plugins, null, true, true, true) }) {
                SceneReject(() => host.ConfigureAiSceneMeasurement(f.Prepared)); host.Dispose();
            }
            var actual = new CandidatePresentation(plugins, null, true, true, false);
            Task.Run(() => SceneReject(() => actual.ConfigureAiSceneMeasurement(f.Prepared))).GetAwaiter().GetResult();
            actual.ConfigureAiSceneMeasurement(f.Prepared); SceneReject(() => actual.ConfigureAiSceneMeasurement(f.Prepared)); actual.Dispose();
            Check(actual.AiSceneMeasurementState.InitialSha256 is null && !actual.AiMeasurementStatus.HasEvidence);
        });
        yield return ("M8.7-E2E8 actual CPU editor uses memory scene original history and immutable input", () => {
            using var f = new SceneFixture(output); var host = new CandidatePresentation(plugins, null, true, true, false);
            host.ConfigureAiSceneMeasurement(f.Prepared); var lifetime = new ApplicationLifetime();
            try {
                lifetime.Start([host]); var owner = host.VerificationEditor; var workspace = new EditorWorkspace(owner);
                Check(owner.Document.ObjectCount == 1 && owner.Edit!.State.FilePath is null && owner.Play is null && owner.Assets is null && owner.DomainProposals is not null);
                Check(host.VerificationAi.ProjectAvailable && host.VerificationAi.Status == "unconfigured");
                byte[] before = owner.Document.CaptureBytes(); workspace.CreateObject(workspace.Stamp); Check(owner.Document.ObjectCount == 2);
                workspace.History(workspace.Stamp, false); Check(before.SequenceEqual(owner.Document.CaptureBytes()));
                var snapshot = owner.Document.CaptureSnapshot(); var row = snapshot.Objects[0];
                SceneReject(() => owner.Document.RestoreSnapshot(snapshot with { Objects = [row with { Behaviours = [new(Guid.NewGuid(), "NeverLoad.Script", true, [])] }] }));
                Check(before.SequenceEqual(owner.Document.CaptureBytes()));
                SceneReject(() => { using var writer = new FileStream(f.ScenePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); });
                Check(lifetime.Run(new MonotonicClock(), host, 8) == 8); lifetime.Dispose();
                Check(host.AiMeasurementStatus.HasEvidence && !host.AiMeasurementStatus.OuterRetained && host.AiMeasurementStatus.Budget!.ChargedAttempts == 0);
                Check(host.AiSceneMeasurementState.InitialSha256 == host.AiSceneMeasurementState.FinalSha256 && f.SceneBytes.SequenceEqual(File.ReadAllBytes(f.ScenePath)));
                Check(host.VerificationAiLifetime.Modules == 0 && !File.Exists(Path.Combine(host.VerificationUserDataRoot, "editor/preferences.json")) && !File.Exists(Path.Combine(host.VerificationUserDataRoot, "editor/workspace.json")));
            } finally { if (lifetime.RetainedServiceCount != 0 && lifetime.ShutdownAttempts == 0) lifetime.Dispose(); }
        });
        yield return ("M8.7-E2E8 scene failed close retains dependencies seal does not drain or retry", () => {
            using var f = new SceneFixture(output); var handler = new Handler();
            var host = new CandidatePresentation(plugins, null, true, true, false) {
                AiFactoryForVerification = domain => new(domain, (_, _, _) => throw new InvalidOperationException("No authoring transport expected"),
                    diagnosticFactory: (configuration, credential) => new(configuration, credential, handler))
            };
            host.ConfigureAiSceneMeasurement(f.Prepared); AiAuthoringWorkspace? ai = null;
            try {
                host.Start(); ai = host.VerificationAi; Check(ai.ConfigureLocal("http://127.0.0.1:8000/v1/chat/completions", "fixture-model"));
                var review = ai.PrepareConnectionDiagnostic(); ai.BeginConnectionDiagnostic(review, review.Fingerprint, true); Until(() => handler.Calls == 1);
                Reject(host.Dispose, "ai_shutdown_draining"); Check(host.AiMeasurementStatus.OuterRetained);
                host.SealAiMeasurement(); Check(host.AiSceneMeasurementState.FinalSha256 is not null && host.AiMeasurementStatus.HasEvidence && handler.Disposals == 0);
                Check(f.Inputs.State == MeasurementInputsState.Ready && f.SceneBytes.SequenceEqual(File.ReadAllBytes(f.ScenePath)));
                handler.Release.TrySetResult(); Until(() => ai.TryClose()); host.Dispose(); Check(!host.AiMeasurementStatus.OuterRetained && handler.Disposals == 1);
            } finally { handler.Release.TrySetResult(); if (ai is not null) Until(() => ai.TryClose()); host.Dispose(); }
        });
#endif
    }
}
#endif
