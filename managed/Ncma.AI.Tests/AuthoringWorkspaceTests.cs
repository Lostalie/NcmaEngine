using Ncma.Editor.Core;
using Ncma.Editor.Modules;
using Ncma.Editor.Services;
using Ncma.AI;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

internal static class AuthoringWorkspaceTests
{
    private static void Check(bool value) { if (!value) throw new Exception("AI authoring workspace assertion"); }
    private static void Reject(Action action) {
        try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or EditRejectedException) { return; }
        throw new Exception("Expected AI UI controller rejection");
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly EditorSessionOwner Owner = new("AI UI controller");
        internal readonly AiAuthoringWorkspace Ui;
        internal readonly Guid Id;
        internal EditorDomainProposals Domains => Owner.DomainProposals!;
        internal Fixture(bool fakeTransport = false) {
            Id = Owner.Document.CreateObject("Before"); Owner.Edit!.Resynchronize(); Owner.ConfigureDomainProposals(Guid.NewGuid());
            Ui = fakeTransport ? new(Domains, (snapshot, operation, credential) => {
                CapturedCredential = credential; CapturedProfile = snapshot.Profile;
                Transport.Proposal = JsonSerializer.Serialize(new { version = 1, capability = "ncma.scene.transaction",
                    input = new { operations = new[] { new { op = "rename", objectId = Id, name = "After" } } } });
                return new(snapshot, operation, credential, Transport);
            }) : new(Domains);
        }
        internal readonly Handler Transport = new();
        internal AiSessionCredential? CapturedCredential;
        internal AiConnectionProfile? CapturedProfile;
        internal void Configure() => Check(Ui.ConfigureLocal("http://127.0.0.1:8000/v1/chat/completions", "fixture-model"));
        internal void Remote() => Check(Ui.ConfigureRemote("fixture-model", _ => "fixture-not-a-user-key"));
        internal AiDomainRequestReview Prepare() => Ui.Prepare(AiAuthoringIntent.Scene, Id, "Rename the object");
        public void Dispose() { Transport.Hold.TrySetResult(); Until(Ui.TryClose); Owner.Dispose(); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal readonly TaskCompletionSource Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Block;
        internal string Proposal = "{}";
        internal int Calls, Disposals;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            Check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == "fixture-not-a-user-key");
            Interlocked.Increment(ref Calls);
            if (Block) await Hold.Task; // Deliberately uncooperative; owner must retain transport.
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new {
                id = "workspace-fixture", @object = "chat.completion", created = 1, model = "fixture-model",
                choices = new[] { new { index = 0, message = new { role = "assistant", content = Proposal }, finish_reason = "stop" } }
            })) };
            response.Content.Headers.ContentType = new("application/json"); return response;
        }
        protected override void Dispose(bool disposing) { if (disposing) Interlocked.Increment(ref Disposals); base.Dispose(disposing); }
    }
    private static void Until(Func<bool> condition) {
        var timer = Stopwatch.StartNew();
        while (!condition()) { if (timer.Elapsed > TimeSpan.FromSeconds(3)) throw new TimeoutException("Workspace did not drain"); Thread.Sleep(1); }
    }
    internal static IEnumerable<(string, Action)> All()
    {
        yield return ("D1 optional UI controller startup/configure performs no registration or service probe", () => {
            using var f = new Fixture(); Check(f.Ui.Status == "unconfigured" && f.Ui.Diagnostic is null && f.Domains.RegisteredModules.Length == 0);
            f.Configure(); Check(f.Ui.Status == "configured-not-connected" && f.Ui.Diagnostic is null && f.Domains.RegisteredModules.Length == 0);
            Check(f.Ui.Disconnect() && f.Ui.Status == "unconfigured");
        });
        yield return ("D1 local-only UI rejects remote endpoints, userinfo and missing model", () => {
            using var f = new Fixture();
            foreach (string url in new[] { "https://api.deepseek.com/chat/completions", "http://localhost:8000/v1/chat/completions", "http://key@127.0.0.1/v1/chat/completions" })
                Reject(() => f.Ui.ConfigureLocal(url, "model"));
            Reject(() => f.Ui.ConfigureLocal("http://127.0.0.1:8000/v1/chat/completions", ""));
            Check(f.Ui.Status == "unconfigured" && f.Domains.RegisteredModules.Length == 0);
        });
        yield return ("D1 full outbound preview does not call transport or mutate authoring", () => {
            using var f = new Fixture(); f.Configure(); byte[] before = f.Owner.Document.CaptureBytes(); var r = f.Prepare();
            Check(r.WireRequest.Length > 0 && f.Ui.Status == "send-review" && f.Ui.Diagnostic is null);
            Reject(() => f.Ui.Send(r, r.Observation.Fingerprint, false));
            Check(f.Ui.Diagnostic is null && f.Owner.Edit!.State.UndoCount == 0 && before.SequenceEqual(f.Owner.Document.CaptureBytes()));
            f.Ui.Cancel(); Reject(() => f.Ui.Send(r, r.Observation.Fingerprint, true));
        });
        yield return ("D1 reconfigure/disconnect invalidates prior approvals and releases registration", () => {
            using var f = new Fixture(); f.Configure(); var r = f.Prepare(); f.Configure();
            Check(f.Domains.RegisteredModules.Length == 0); Reject(() => f.Ui.Send(r, r.Observation.Fingerprint, true));
            for (int i = 0; i < 20; i++) { _ = f.Prepare(); Check(f.Domains.RegisteredModules.Length == 1); Check(f.Ui.Disconnect()); f.Configure(); }
            Check(f.Domains.RegisteredModules.Length == 0);
        });
        yield return ("D1 UI controller exact target, unsupported domains and owner thread", () => {
            using var f = new Fixture(); f.Configure();
            Reject(() => f.Ui.Prepare(AiAuthoringIntent.AnimationGraph, f.Id, "Change"));
            Reject(() => f.Ui.Prepare(AiAuthoringIntent.AssetMetadata, f.Id, "Change"));
            Reject(() => f.Ui.Prepare(AiAuthoringIntent.UiDocument, f.Id, "Change"));
            Reject(() => f.Ui.Prepare(AiAuthoringIntent.Scene, Guid.Empty, "Change"));
            Task.Run(() => { Reject(f.Configure); Reject(() => f.Ui.TryClose()); }).GetAwaiter().GetResult();
            Check(f.Domains.RegisteredModules.Length == 0);
        });
        yield return ("D1 owner invalidation rejects cached send review and close stays idempotent", () => {
            using var f = new Fixture(); f.Configure(); var r = f.Prepare(); f.Domains.Invalidate();
            Reject(() => f.Ui.Send(r, r.Observation.Fingerprint, true)); Check(f.Ui.TryClose() && f.Ui.TryClose());
            Check(f.Domains.RegisteredModules.Length == 0); Reject(f.Configure);
        });
        yield return ("D2 explicit remote prompt fixes recipient and excludes secret from preview", () => {
            using var f = new Fixture(); int prompts = 0;
            Check(f.Ui.ConfigureRemote("fixture-model", p => {
                prompts++; Check(p.Endpoint == AiProvider.DeepSeek.RemoteEndpoint && p.Deployment == AiDeployment.RemoteHttps && p.Credential is not null);
                return "fixture-not-a-user-key";
            }));
            var r = f.Prepare();
            Check(prompts == 1 && f.Ui.Diagnostic is null && !Encoding.UTF8.GetString(r.WireRequest.CopyBytes()).Contains("fixture-not-a-user-key") &&
                !Encoding.UTF8.GetString(r.Observation.CopyBytes()).Contains("fixture-not-a-user-key"));
            Reject(() => f.Ui.Send(r, r.Observation.Fingerprint, false)); Check(f.Owner.Edit!.State.UndoCount == 0);
        });
        yield return ("D2 cancel input revokes old configuration and cached approval without transport", () => {
            using var f = new Fixture(); f.Remote(); var r = f.Prepare();
            Check(!f.Ui.ConfigureRemote("fixture-model", _ => null) && f.Ui.Profile is null && f.Ui.Status == "unconfigured");
            Reject(() => r.Approve(r.Observation.Fingerprint, true)); Check(f.Domains.RegisteredModules.Length == 0);
        });
        yield return ("D2 invalid model never opens prompt and preserves prior valid configuration", () => {
            using var f = new Fixture(); f.Configure(); var p = f.Ui.Profile; int prompts = 0;
            Reject(() => f.Ui.ConfigureRemote("", _ => { prompts++; return "fixture-not-a-user-key"; }));
            Check(prompts == 0 && ReferenceEquals(p, f.Ui.Profile));
        });
        yield return ("D2 invalid key or prompt failure stays disconnected with no stale registration", () => {
            foreach (string? key in new[] { "", "bad\r\nkey", new string('x', 4097), null }) {
                using var f = new Fixture(); f.Configure(); _ = f.Prepare();
                Reject(() => f.Ui.ConfigureRemote("fixture-model", _ => key ?? throw new InvalidOperationException("fixture prompt failure")));
                Check(f.Ui.Profile is null && f.Ui.Diagnostic is null && f.Domains.RegisteredModules.Length == 0);
            }
        });
        yield return ("D2 prompt owner thread and nested lifecycle calls cannot alter candidate", () => {
            using var f = new Fixture(); int prompts = 0;
            Task.Run(() => Reject(() => f.Ui.ConfigureRemote("fixture-model", _ => { prompts++; return "fixture-key"; }))).GetAwaiter().GetResult();
            Check(prompts == 0);
            Check(f.Ui.ConfigureRemote("fixture-model", _ => {
                Reject(() => f.Ui.Disconnect()); Reject(() => f.Ui.TryClose()); Reject(f.Configure);
                return "fixture-not-a-user-key";
            })); Check(f.Ui.Status == "configured-not-connected");
        });
        yield return ("D2 repeated remote rotation uses new reference and retires credential", () => {
            using var f = new Fixture(true);
            for (int i = 0; i < 20; i++) {
                f.Remote(); _ = f.Prepare(); var credential = f.CapturedCredential!; var profile = f.CapturedProfile!;
                Check(credential.IsCurrent(profile)); f.Configure();
                Check(!credential.IsCurrent(profile) && f.Ui.Profile!.Credential is null && f.Domains.RegisteredModules.Length == 0);
            }
            Check(f.Transport.Calls == 0);
        });
        yield return ("D2 revoke immediately invalidates secret and approval while retaining uncooperative transport", () => {
            using var f = new Fixture(true); f.Transport.Block = true; f.Remote(); var r = f.Prepare();
            f.Ui.Send(r, r.Observation.Fingerprint, true); f.Domains.PumpModules(); Until(() => Volatile.Read(ref f.Transport.Calls) == 1);
            Check(!f.Ui.Disconnect() && f.Ui.Profile is null && f.Ui.Status == "draining");
            Check(!f.CapturedCredential!.IsCurrent(f.CapturedProfile!) && f.Transport.Disposals == 0 && f.Domains.RegisteredModules.Length == 1);
            Reject(() => r.Approve(r.Observation.Fingerprint, true)); int prompts = 0;
            Check(!f.Ui.ConfigureRemote("fixture-model", _ => { prompts++; return "fixture-key"; }) && prompts == 0);
            Check(!f.Ui.TryClose() && f.Transport.Disposals == 0);
            f.Transport.Hold.SetResult(); Until(f.Ui.TryClose);
            Check(f.Transport.Disposals == 1 && f.Domains.RegisteredModules.Length == 0 && f.Owner.Edit!.State.UndoCount == 0);
        });
        yield return ("D2 disconnect after reply blocks original direct Commit and clears credential", () => {
            using var f = new Fixture(true); f.Remote(); var r = f.Prepare(); f.Ui.Send(r, r.Observation.Fingerprint, true);
            Until(() => { f.Domains.PumpModules(); return f.Ui.Status != "pending"; });
            var review = f.Ui.RefreshReview()!; Check(review is not null);
            Check(f.Ui.Disconnect() && !f.CapturedCredential!.IsCurrent(f.CapturedProfile!));
            Reject(() => f.Domains.Confirm(review!, review!.Fingerprint, true));
            Check(f.Owner.Document.GetObjectName(f.Id) == "Before" && f.Owner.Edit!.State.UndoCount == 0);
        });
    }
}
