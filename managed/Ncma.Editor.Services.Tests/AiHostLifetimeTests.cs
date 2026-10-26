#if NCMA_AI
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Ncma.AI;
using Ncma.Application;
using Ncma.Editor.App;
using Ncma.Editor.Modules;
using Ncma.Scene;

// Starts the real CandidatePresentation and native owners when run at stage-end.
// Only outbound HTTP is substituted; missing native/sample/font inputs fail, never skip.
internal static class AiHostLifetimeTests
{
    private static void Check(bool value) { if (!value) throw new Exception("AI host lifetime assertion"); }
    private static void Reject(Action action, string? message = null) {
        try { action(); } catch (InvalidOperationException e) { if (message is not null) Check(e.Message == message); return; }
        throw new Exception("Host operation must reject while closing");
    }
    private static void Until(Func<bool> condition) {
        var timer = Stopwatch.StartNew(); while (!condition()) { if (timer.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Host AI drain"); Thread.Sleep(1); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls, Disposals;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            Check(request.Headers.Authorization is null); Interlocked.Increment(ref Calls);
            await Release.Task; // Intentionally does not cooperate with cancellation.
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new {
                id = "host-fixture", @object = "chat.completion", created = 1, model = "fixture-model",
                choices = new[] { new { index = 0, message = new { role = "assistant", content = "{\"ncmaDiagnostic\":\"ok\"}" }, finish_reason = "stop" } }
            })) };
            response.Content.Headers.ContentType = new("application/json"); return response;
        }
        protected override void Dispose(bool disposing) { if (disposing) Interlocked.Increment(ref Disposals); base.Dispose(disposing); }
    }
    internal static IEnumerable<(string, Action)> Cases(string repository, string plugins, string output)
    {
        foreach (bool cpu in new[] { true, false }) {
            bool cpuOnly = cpu;
            yield return ($"M8.4-D actual {(cpuOnly ? "CPU GUI" : "DX11")} host close retains all owners until AI drain and forbids resumed frames", () => {
                string root = Path.Combine(output, "ai-host-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
                foreach (string suffix in new[] { ".dll", ".deps.json" })
                    File.Copy(Path.Combine(repository, "out/managed/Ncma.Gameplay.Sample" + suffix), Path.Combine(root, "gameplay" + suffix));
                var document = new SceneDocument("AI host empty scene"); SceneDocumentFiles.Save(document, Path.Combine(root, "start.ncmascene"));
                string path = Path.Combine(root, "project.ncmaproject");
                File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new ProjectConfiguration(1, Guid.NewGuid(), "AI host", "start.ncmascene", "gameplay.dll", "Direct3D11", []),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                var handler = new Handler(); AiAuthoringWorkspace? ai = null;
                var host = new CandidatePresentation(plugins, ProjectContext.Load(path), true, cpuOnly, false) {
                    AiFactoryForVerification = domains => ai = new(domains,
                        (_, _, _) => throw new InvalidOperationException("Domain transport not used by this fixture"),
                        diagnosticFactory: (snapshot, credential) => new(snapshot, credential, handler))
                };
                try {
                    host.Start(); Check(ai is not null && ReferenceEquals(host.VerificationAi, ai));
                    var before = host.VerificationAiLifetime; var owner = host.VerificationEditor; byte[] scene = owner.Document.CaptureBytes();
                    Check(!before.Closed && !before.Closing && before.Editor is not null && before.Gui is not null && before.Window is not null &&
                        before.Physics is not null && before.Modules == 3 && (before.Renderer is null) == cpuOnly);
                    Check(ai!.ConfigureLocal("http://127.0.0.1:8000/v1/chat/completions", "fixture-model"));
                    var review = ai.PrepareConnectionDiagnostic(); ai.BeginConnectionDiagnostic(review, review.Fingerprint, true); Until(() => handler.Calls == 1);
                    // Non-owner close must not revoke or change any lifetime state.
                    Task.Run(() => Reject(host.Dispose)).GetAwaiter().GetResult(); Check(ai.Profile is not null && host.VerificationAiLifetime == before);
                    for (int attempt = 0; attempt < 3; attempt++) {
                        Reject(host.Dispose, "ai_shutdown_draining"); var retained = host.VerificationAiLifetime;
                        Check(retained.Closing && !retained.Closed && retained.Editor == before.Editor && retained.Gui == before.Gui && retained.Renderer == before.Renderer &&
                            retained.Window == before.Window && retained.Physics == before.Physics && retained.Modules == before.Modules);
                        Check(ai.Profile is null && handler.Disposals == 0 && scene.SequenceEqual(owner.Document.CaptureBytes()));
                    }
                    var frame = new FrameContext(1, 0);
                    Reject(() => host.PumpPlatform()); Reject(() => host.ApplyEditorIntents(frame)); Reject(() => host.PumpAgent(frame));
                    Reject(() => host.BeginPresentation(frame)); Reject(() => host.SubmitInput(frame)); Reject(() => host.AdvancePlay(frame)); Reject(() => host.Present(frame));
                    Reject(host.Start); Check(!host.CanReviewGraphicsRecovery); Reject(() => host.ReviewGraphicsRecovery());
                    if (!cpuOnly) Check(host.VerificationStats.ValidationErrors == 0); // Retained native renderer remains owned, not destroyed.
                    handler.Release.TrySetResult(); Until(() => ai.Diagnostic is not null); host.Dispose(); host.Dispose();
                    var closed = host.VerificationAiLifetime;
                    Check(closed.Closed && closed.Closing && closed.Editor is null && closed.Gui is null && closed.Renderer is null && closed.Window is null && closed.Physics is null && closed.Modules == 0);
                    Check(handler.Disposals == 1 && ai.Status == "closed");
                }
                finally {
                    handler.Release.TrySetResult();
                    if (ai is not null) Until(() => ai.TryClose());
                    host.Dispose();
                }
            });
        }
    }
}
#endif
