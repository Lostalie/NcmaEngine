#if NCMA_AI
using System.Diagnostics;
using System.Text.Json;
using Ncma.AI;
using Ncma.Application;
using Ncma.Editor.App;
using Ncma.Editor.Modules;
using Ncma.Modules;
using Ncma.Scene;

internal static partial class AiHostLifetimeTests
{
    private static ObservationIdentity MeasurementIdentity() => new(Guid.NewGuid(), new('A', 64), new('B', 64),
#if DEBUG
        ObservationConfiguration.Debug,
#else
        ObservationConfiguration.Release,
#endif
#if NCMA_EXTENSIONS
        ObservationVariant.AiExtensions,
#else
        ObservationVariant.Ai,
#endif
        Stopwatch.Frequency);
    private static AiWorkspaceMeasurementLimits MeasurementLimits() => new(new(Guid.NewGuid(), 1, AiProvider.DeepSeek,
        AiDeployment.LocalLoopback, "http://127.0.0.1:8000/v1/chat/completions", "fixture-model", null), 2);
    private static CandidatePresentation MeasurementHost(string repository, string plugins, string output, bool cpu, Handler handler)
    {
        string root = Path.Combine(output, "ai-measurement-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        foreach (string suffix in new[] { ".dll", ".deps.json" })
            File.Copy(Path.Combine(repository, "out/managed/Ncma.Gameplay.Sample" + suffix), Path.Combine(root, "gameplay" + suffix));
        var document = new SceneDocument("AI measurement fixture"); document.CreateObject("Observed");
        SceneDocumentFiles.Save(document, Path.Combine(root, "start.ncmascene"));
        string path = Path.Combine(root, "project.ncmaproject");
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new ProjectConfiguration(1, Guid.NewGuid(), "AI measurement", "start.ncmascene", "gameplay.dll", "Direct3D11", []),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return new(plugins, ProjectContext.Load(path), true, cpu, false) {
            AiFactoryForVerification = domains => new(domains, (_, _, _) => throw new InvalidOperationException("Unexpected authoring transport"),
                diagnosticFactory: (snapshot, credential) => new(snapshot, credential, handler))
        };
    }
    private static IEnumerable<(string, Action)> MeasurementCases(string repository, string plugins, string output)
    {
        yield return ("M8.7-E2E6 host prestart single owner and preview boundary without native start", () => {
            var host = new CandidatePresentation(plugins, null, true, true, false);
            Check(!host.AiMeasurementStatus.Configured && host.CopyAiMeasurementEvidence() is null);
            Reject(host.SealAiMeasurement); var id = MeasurementIdentity();
            Task.Run(() => Reject(() => host.ConfigureAiMeasurement(id, MeasurementLimits()))).GetAwaiter().GetResult();
            host.ConfigureAiMeasurement(id, MeasurementLimits()); Reject(() => host.ConfigureAiMeasurement(id, MeasurementLimits()));
            Reject(host.SealAiMeasurement); host.Dispose();
            Check(host.AiMeasurementStatus.SealAttempted && !host.AiMeasurementStatus.HasEvidence && host.AiMeasurementStatus.FailureType == "InvalidOperationException");
            var preview = new CandidatePresentation(plugins, null, true, true, true);
            Reject(() => preview.ConfigureAiMeasurement(id, MeasurementLimits())); preview.Dispose();
        });
        foreach (bool cpu in new[] { true, false }) {
            bool cpuOnly = cpu;
            yield return ($"M8.7-E2E6 actual {(cpuOnly ? "CPU" : "DX11")} host original loop auto seal survives workspace release", () => {
                var handler = new Handler(); var host = MeasurementHost(repository, plugins, output, cpuOnly, handler);
                var id = MeasurementIdentity(); host.ConfigureAiMeasurement(id, MeasurementLimits());
                var lifetime = new ApplicationLifetime();
                try {
                    lifetime.Start([host]); var ai = host.VerificationAi;
                    Reject(() => host.ConfigureAiMeasurement(id, MeasurementLimits()));
                    byte[] scene = host.VerificationEditor.Document.CaptureBytes();
                    var processId = new ProcessMeasurementIdentity(id.RunId, id.SourceSha256, new('C', 64), new('D', 64), id.WorkloadSha256,
                        id.Configuration == ObservationConfiguration.Debug ? MeasurementConfiguration.Debug : MeasurementConfiguration.Release,
                        id.Variant == ObservationVariant.Ai ? MeasurementVariant.Ai : MeasurementVariant.AiExtensions, Ncma.Application.MeasurementHost.Editor);
                    var sampling = new ProcessMeasurement(processId, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1), 10);
                    Check(lifetime.Run(new MonotonicClock(), host, 8, measurement: sampling) == 8);
                    var process = sampling.Seal(); Check(process.BoundToLoop && process.CompletedIterations == 8);
                    Check(host.AiMeasurementStatus.Attached && !host.AiMeasurementStatus.SealAttempted && scene.SequenceEqual(host.VerificationEditor.Document.CaptureBytes()));
                    if (!cpuOnly) Check(host.VerificationStats.ValidationErrors == 0);
                    lifetime.Dispose(); var status = host.AiMeasurementStatus;
                    Check(status.HasEvidence && status.FailureType is null && !status.OuterRetained && status.Budget!.Closed && status.Budget.ChargedAttempts == 0);
                    byte[] data = host.CopyAiMeasurementEvidence()!;
                    Check(AiWorkspaceObservationEvidence.DecodeExpected(data, id).StartedWorkflows == 0 && handler.Calls == 0 && ai.Status == "closed");
                    data[0] = 0; Check(host.CopyAiMeasurementEvidence()![0] != 0); Reject(host.SealAiMeasurement);
                    Check(host.VerificationAiLifetime.Modules == 0);
                } finally { handler.Release.TrySetResult(); if (lifetime.RetainedServiceCount != 0 && lifetime.ShutdownAttempts == 0) lifetime.Dispose(); }
            });
        }
        yield return ("M8.7-E2E6 retained host explicit seal is not transport drain or close retry", () => {
            var handler = new Handler(); var host = MeasurementHost(repository, plugins, output, true, handler);
            var id = MeasurementIdentity(); host.ConfigureAiMeasurement(id, MeasurementLimits()); AiAuthoringWorkspace? ai = null;
            try {
                host.Start(); ai = host.VerificationAi; Check(ai.ConfigureLocal("http://127.0.0.1:8000/v1/chat/completions", "fixture-model"));
                var review = ai.PrepareConnectionDiagnostic(); ai.BeginConnectionDiagnostic(review, review.Fingerprint, true); Until(() => handler.Calls == 1);
                Reject(host.Dispose, "ai_shutdown_draining"); Check(host.AiMeasurementStatus.OuterRetained && !host.AiMeasurementStatus.SealAttempted);
                host.SealAiMeasurement(); byte[] data = host.CopyAiMeasurementEvidence()!; var status = host.AiMeasurementStatus;
                Check(status.HasEvidence && status.OuterRetained && status.Budget!.Closed && status.Budget.ChargedAttempts == 1 && handler.Disposals == 0);
                Check(AiWorkspaceObservationEvidence.DecodeExpected(data, id).StartedWorkflows == 0); // Diagnostics consume budget but are not authoring observations.
                Check(host.AiMeasurementStatus == status && data.SequenceEqual(host.CopyAiMeasurementEvidence()!));
                handler.Release.TrySetResult(); Until(() => ai.TryClose()); host.Dispose();
                Check(!host.AiMeasurementStatus.OuterRetained && handler.Disposals == 1 && data.SequenceEqual(host.CopyAiMeasurementEvidence()!));
            } finally { handler.Release.TrySetResult(); if (ai is not null) Until(() => ai.TryClose()); host.Dispose(); }
        });
    }
}
#endif
