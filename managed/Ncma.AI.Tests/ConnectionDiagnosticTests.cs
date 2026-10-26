using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Ncma.AI;
using Ncma.Editor.Modules;
using Ncma.Editor.Services;

internal static class ConnectionDiagnosticTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Connection diagnostic assertion"); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is InvalidOperationException or ArgumentException or AiProtocolException) { return; } throw new Exception("Expected diagnostic rejection"); }
    private static void Until(Func<bool> condition) { var watch = Stopwatch.StartNew(); while (!condition()) { if (watch.ElapsedMilliseconds > 3000) throw new TimeoutException("diagnostic fixture"); Thread.Sleep(1); } }
    private sealed class Clock : TimeProvider { internal long Now; public override long TimestampFrequency => 1000; public override long GetTimestamp() => Now; }
    private sealed class Handler : HttpMessageHandler
    {
        internal int Calls, Disposals;
        internal byte[]? Sent;
        internal string? Authorization;
        internal HttpStatusCode Status = HttpStatusCode.OK;
        internal string Content = "{\"ncmaDiagnostic\":\"ok\"}";
        internal int CompletionTokens = 8;
        internal bool Oversized, Hold, DelayUntilCancelled;
        internal Action? Callback;
        internal Exception? Failure;
        internal readonly TaskCompletionSource<bool> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            Check(request.Headers.ConnectionClose == false);
            Calls++; Sent = await request.Content!.ReadAsByteArrayAsync(token); Authorization = request.Headers.Authorization?.Parameter; Callback?.Invoke();
            if (Failure is not null) throw Failure;
            if (DelayUntilCancelled) await Task.Delay(Timeout.Infinite, token);
            if (Hold) await Release.Task; // Deliberately ignores token; must keep the lane owned.
            byte[] bytes = Oversized ? new byte[AiConnectionDiagnosticSession.MaximumResponseBytes + 1] : JsonSerializer.SerializeToUtf8Bytes(new {
                id = "fixture", @object = "chat.completion", created = 1, model = "diagnostic-model",
                choices = new[] { new { index = 0, message = new { role = "assistant", content = Content }, finish_reason = "stop" } },
                usage = new { prompt_tokens = 10, completion_tokens = CompletionTokens, total_tokens = 10 + CompletionTokens }
            });
            var response = new HttpResponseMessage(Status) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentType = new("application/json"); return response;
        }
        protected override void Dispose(bool disposing) { if (disposing) Disposals++; base.Dispose(disposing); }
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly AiConnectionConfiguration Config = new();
        internal readonly Handler Handler = new();
        internal readonly Clock Time = new();
        internal readonly AiConnectionDiagnosticSession Session;
        internal readonly AiSessionCredential? Credential;
        internal Fixture(bool remote = false, int timeout = 10_000) {
            var profile = new AiConnectionProfile(Guid.NewGuid(), 1, AiProvider.DeepSeek,
                remote ? AiDeployment.RemoteHttps : AiDeployment.LocalLoopback,
                remote ? AiProvider.DeepSeek.RemoteEndpoint : "http://127.0.0.1:8000/v1/chat/completions", "diagnostic-model",
                remote ? new(Guid.NewGuid(), 1) : null, timeoutMilliseconds: timeout);
            Config.Configure(profile); if (remote) Credential = new(profile, "fixture-only-secret");
            Session = new(Config.Capture(), Credential, Handler, Time);
        }
        internal void Begin() => Session.Begin(Session.Review, Session.Review.Fingerprint, true);
        internal AiCallDiagnostic Complete() { Until(() => Session.LastDiagnostic is not null); return Session.LastDiagnostic!; }
        public void Dispose() { Handler.Release.TrySetResult(true); Until(Session.TryClose); Credential?.Dispose(); Config.Close(); }
    }
    internal static IEnumerable<(string, Action)> All()
    {
        yield return ("D diagnostic fallback exposes only exception type without message or inner exception", () => {
            using var f = new Fixture();
            f.Handler.Failure = new IOException("SECRET_BODY", new Exception("SECRET_INNER"));
            f.Begin(); var diagnostic = f.Complete();
            Check(diagnostic.Code == "invalid_response_or_request" && diagnostic.ExceptionType == nameof(IOException));
            Check(!diagnostic.ToString().Contains("SECRET", StringComparison.Ordinal) &&
                !JsonSerializer.Serialize(diagnostic).Contains("SECRET", StringComparison.Ordinal));
        });
        yield return ("D diagnostic exact immutable preview requires approval once, no project payload", () => {
            using var f = new Fixture(); var review = f.Session.Review; byte[] expected = review.Body.CopyBytes();
            Check(f.Handler.Calls == 0 && review.MaximumOutputTokens == 64 && review.TimeoutMilliseconds == 10_000);
            using var json = JsonDocument.Parse(expected); Check(json.RootElement.GetProperty("max_tokens").GetInt32() == 64 && !json.RootElement.TryGetProperty("observation", out _));
            byte[] copy = review.Body.CopyBytes(); copy[0] = 0; Check(review.Body.CopyBytes()[0] != 0);
            Reject(() => f.Session.Begin(review, review.Fingerprint, false)); Reject(() => f.Session.Begin(review, "wrong", true)); Check(f.Handler.Calls == 0);
            f.Begin(); Check(f.Complete().Code == "diagnostic_ok" && expected.SequenceEqual(f.Handler.Sent!));
            Reject(f.Begin); Check(f.Handler.Calls == 1);
            Reject(() => AiChatCodec.Decode(JsonSerializer.SerializeToUtf8Bytes(new { id = "x", @object = "chat.completion", created = 1, model = "diagnostic-model",
                choices = new[] { new { index = 0, message = new { role = "assistant", content = "{\"ncmaDiagnostic\":\"ok\"}" }, finish_reason = "stop" } } })));
        });
        yield return ("D diagnostic expiry foreign ticket owner and configuration revoke reject before HTTP", () => {
            using var f = new Fixture(); using var other = new Fixture();
            Reject(() => f.Session.Begin(other.Session.Review, other.Session.Review.Fingerprint, true));
            Task.Run(() => Reject(f.Begin)).GetAwaiter().GetResult();
            f.Time.Now = 60_000; Reject(f.Begin); other.Config.Clear(); Reject(other.Begin);
            Check(f.Handler.Calls == 0 && other.Handler.Calls == 0);
        });
        yield return ("D diagnostic remote authentication is header only and revoked credential cannot send", () => {
            using (var f = new Fixture(remote: true)) {
                f.Begin(); Check(f.Complete().Code == "diagnostic_ok" && f.Handler.Authorization == "fixture-only-secret");
                Check(!Encoding.UTF8.GetString(f.Session.Review.Body.CopyBytes()).Contains("fixture-only-secret", StringComparison.Ordinal));
                Check(!JsonSerializer.Serialize(f.Session.LastDiagnostic).Contains("fixture-only-secret", StringComparison.Ordinal));
            }
            using var revoked = new Fixture(remote: true); revoked.Credential!.Dispose(); Reject(revoked.Begin); Check(revoked.Handler.Calls == 0);
        });
        yield return ("D diagnostic HTTP statuses are distinct, no error content or retry", () => {
            foreach (var entry in new[] { (302, "redirect_denied"), (401, "authentication_failed"), (429, "rate_limited"), (503, "service_unavailable") }) {
                using var f = new Fixture(); f.Handler.Status = (HttpStatusCode)entry.Item1; f.Handler.Content = "SECRET_SERVER_BODY";
                f.Begin(); var result = f.Complete(); Check(result.Code == entry.Item2 && result.ResponseBytes == 0 && f.Handler.Calls == 1);
            }
        });
        yield return ("D diagnostic closed reply and budgets reject authoring commands duplicate fields and overrun", () => {
            foreach (string content in new[] { "{\"version\":1,\"capability\":\"ncma.scene.transaction\",\"input\":{}}", "{\"ncmaDiagnostic\":\"ok\",\"extra\":true}", "{\"ncmaDiagnostic\":\"ok\",\"ncmaDiagnostic\":\"ok\"}", "{\"ncmaDiagnostic\":\"bad\"}" }) {
                using var f = new Fixture(); f.Handler.Content = content; f.Begin(); Check(f.Complete().Code != "diagnostic_ok");
            }
            using (var f = new Fixture()) { f.Handler.Oversized = true; f.Begin(); Check(f.Complete().Code == "response_byte_budget"); }
            using (var f = new Fixture()) { f.Handler.CompletionTokens = 65; f.Begin(); Check(f.Complete().Code == "completion_token_budget"); }
        });
        yield return ("D diagnostic timeout and ignored cancellation retain transport until actual completion", () => {
            using (var f = new Fixture(timeout: 25)) { f.Handler.DelayUntilCancelled = true; f.Begin(); Check(f.Complete().Code == "timed_out"); }
            using var held = new Fixture(); held.Handler.Hold = true; held.Begin(); Until(() => held.Handler.Calls == 1);
            held.Session.Cancel(); Check(!held.Session.TryClose() && held.Handler.Disposals == 0); Reject(held.Begin);
            held.Handler.Release.TrySetResult(true); Check(held.Complete().Code == "cancelled"); Check(held.Session.TryClose() && held.Handler.Disposals == 1);
        });
        yield return ("D diagnostic actual controller excludes authoring, rejects reentry and survives 12 replacements", () => {
            using var owner = new EditorSessionOwner("PRIVATE_PROJECT_NAME"); owner.ConfigureDomainProposals(Guid.NewGuid());
            var domains = owner.DomainProposals!; byte[] original = owner.Document.CaptureBytes();
            Handler? handler = null;
            var ui = new AiAuthoringWorkspace(domains, (_, _, _) => throw new Exception("No authoring adapter expected"),
                diagnosticFactory: (snapshot, credential) => new(snapshot, credential, handler = new()));
            try {
                Check(ui.ConfigureLocal("http://127.0.0.1:8000/v1/chat/completions", "diagnostic-model"));
                for (int i = 0; i < 12; i++) {
                    var review = ui.PrepareConnectionDiagnostic();
                    Check(domains.RegisteredModules.Length == 0 && !Encoding.UTF8.GetString(review.Body.CopyBytes()).Contains("PRIVATE_PROJECT_NAME", StringComparison.Ordinal));
                    handler!.Callback = () => Reject(() => ui.ConfigureLocal("http://127.0.0.1:8000/v1/chat/completions", "reentrant"));
                    ui.BeginConnectionDiagnostic(review, review.Fingerprint, true); Until(() => ui.Diagnostic is not null);
                    Check(ui.Status == "diagnostic-complete" && ui.Diagnostic!.Code == "diagnostic_ok");
                    ui.Cancel(); Check(ui.Status == "diagnostic-cancelled");
                }
                Check(original.SequenceEqual(owner.Document.CaptureBytes()) && owner.Edit!.State.UndoCount == 0);
            }
            finally { Until(ui.TryClose); }
        });
        yield return ("D diagnostic controller revokes first, blocks credential prompt and retains uncooperative work", () => {
            using var owner = new EditorSessionOwner("Diagnostic lifecycle"); owner.ConfigureDomainProposals(Guid.NewGuid());
            var handler = new Handler { Hold = true }; int prompts = 0;
            var ui = new AiAuthoringWorkspace(owner.DomainProposals!, (_, _, _) => throw new Exception("No authoring expected"),
                diagnosticFactory: (snapshot, credential) => new(snapshot, credential, handler));
            try {
                Check(ui.ConfigureLocal("http://127.0.0.1:8000/v1/chat/completions", "diagnostic-model"));
                var review = ui.PrepareConnectionDiagnostic(); ui.BeginConnectionDiagnostic(review, review.Fingerprint, true); Until(() => handler.Calls == 1);
                Check(!ui.ConfigureRemote("diagnostic-model", _ => { prompts++; return "fixture-key"; }));
                Check(ui.Profile is null && prompts == 0 && !ui.TryClose() && handler.Disposals == 0);
                handler.Release.TrySetResult(true); Until(ui.TryClose); Check(handler.Disposals == 1 && ui.Status == "closed");
            }
            finally { handler.Release.TrySetResult(true); Until(ui.TryClose); }
        });
    }
}
