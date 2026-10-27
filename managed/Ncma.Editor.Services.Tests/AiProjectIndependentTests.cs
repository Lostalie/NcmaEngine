#if NCMA_AI
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Ncma.AI;
using Ncma.Application;
using Ncma.Editor.App;
using Ncma.Editor.Core;
using Ncma.Editor.Modules;
using Ncma.Editor.Services;
using Ncma.Gui;

internal static class AiProjectIndependentTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Project-independent AI assertion"); }
    private static void Reject(Action action, string code) {
        try { action(); } catch (EditRejectedException e) when (e.Message == code) { return; }
        throw new Exception("Expected rejection: " + code);
    }
    private static void Until(Func<bool> condition) {
        var time = Stopwatch.StartNew();
        while (!condition()) { if (time.Elapsed > TimeSpan.FromSeconds(3)) throw new TimeoutException("Projectless AI fixture"); Thread.Sleep(1); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal int Calls;
        internal byte[]? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            Body = await request.Content!.ReadAsByteArrayAsync(token); Interlocked.Increment(ref Calls);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new {
                id = "projectless-fixture", @object = "chat.completion", created = 1, model = "fixture-model",
                choices = new[] { new { index = 0, message = new { role = "assistant", content = "{\"ncmaDiagnostic\":\"ok\"}" }, finish_reason = "stop" } }
            })) };
            response.Content.Headers.ContentType = new("application/json"); return response;
        }
    }
    private static GuiEvent Event(EditorPresenter view, ulong high, ulong low, int bytes = 0) {
        var item = view.Items.ToArray().Single(i => i.WidgetHigh == high && i.WidgetLow == low);
        return new() { Kind = item.Kind, WidgetHigh = high, WidgetLow = low, Phase = 3, TextLength = (uint)bytes,
            Frame = view.Frame.Frame, ViewGeneration = view.Frame.ViewGeneration, DocumentGeneration = view.Frame.DocumentGeneration, Revision = view.Frame.Revision };
    }
    private static bool Enabled(EditorPresenter view, ulong high, ulong low) => view.Items.ToArray().Single(i => i.WidgetHigh == high && i.WidgetLow == low).Enabled != 0;
    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("AI editor-session configuration without project rejects every authoring intent", () => {
            var ai = new AiAuthoringWorkspace();
            try {
                Check(!ai.ProjectAvailable && !ai.UiAvailable && !ai.AnimationAvailable && !ai.MetadataContentAvailable);
                Check(ai.Status == "unconfigured" && ai.ConfigureLocal("http://127.0.0.1:8000/v1/chat/completions", "fixture-model"));
                Guid configuration = ai.Profile!.ConfigurationId;
                foreach (AiAuthoringIntent intent in Enum.GetValues<AiAuthoringIntent>())
                    Reject(() => ai.Prepare(intent, Guid.NewGuid(), "private instruction"), "ai_project_required_for_authoring");
                Check(ai.Profile.ConfigurationId == configuration && ai.Diagnostic is null && ai.Status == "configured-not-connected");
                Check(ai.Disconnect() && ai.Profile is null);
            } finally { Check(ai.TryClose()); }
            Check(ai.Status == "closed");
        });
        yield return ("AI editor-session remote credential configure cancel revoke without project or transport", () => {
            var ai = new AiAuthoringWorkspace(); int prompts = 0;
            try {
                Check(ai.ConfigureRemote("fixture-model", profile => { prompts++; Check(profile.Endpoint == AiProvider.DeepSeek.RemoteEndpoint); return "fake-fixture-token"; }));
                Check(ai.Profile!.Deployment == AiDeployment.RemoteHttps && ai.Diagnostic is null && prompts == 1);
                Check(!ai.ConfigureRemote("fixture-model", _ => { prompts++; return null; }));
                Check(prompts == 2 && ai.Profile is null && ai.Status == "unconfigured");
            } finally { Check(ai.TryClose()); }
        });
        yield return ("AI projectless diagnostic retains explicit approval and exact request without authoring", () => {
            var handler = new Handler(); int authoringFactories = 0;
            var ai = new AiAuthoringWorkspace(null, (_, _, _) => { authoringFactories++; throw new Exception("Unexpected authoring"); },
                diagnosticFactory: (snapshot, credential) => new(snapshot, credential, handler));
            try {
                Check(ai.ConfigureLocal("http://127.0.0.1:8000/v1/chat/completions", "fixture-model"));
                var review = ai.PrepareConnectionDiagnostic(); Check(handler.Calls == 0);
                bool refused = false;
                try { ai.BeginConnectionDiagnostic(review, review.Fingerprint, false); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { refused = true; }
                Check(refused && handler.Calls == 0);
                ai.BeginConnectionDiagnostic(review, review.Fingerprint, true); Until(() => ai.Diagnostic is not null);
                Check(ai.Diagnostic!.Code == "diagnostic_ok" && handler.Calls == 1 && authoringFactories == 0);
                Check(handler.Body!.SequenceEqual(review.Body.CopyBytes()));
                Reject(() => ai.Prepare(AiAuthoringIntent.Scene, Guid.NewGuid(), "private instruction"), "ai_project_required_for_authoring");
            } finally { Until(ai.TryClose); }
        });
        yield return ("AI projectless actual menu config diagnostic enabled authoring disabled and document unchanged", () => {
            using var owner = new EditorSessionOwner("Unsaved private scratch scene");
            Guid id = owner.Document.CreateObject("PRIVATE_SCRATCH_OBJECT"); owner.Edit!.Resynchronize();
            var workspace = new EditorWorkspace(owner); workspace.Select(workspace.Stamp, id);
            byte[] before = owner.Document.CaptureBytes(); var ai = new AiAuthoringWorkspace();
            var view = new EditorPresenter(workspace, null, workspaceStyle: true); view.AttachAiAuthoring(ai);
            ulong frame = 0;
            void Build() => view.Build(++frame, 1440, 900);
            void Click(ulong high, ulong low) { view.Apply([Event(view, high, low)], []); Build(); }
            try {
                Build(); Click(24, 14); Check(Enabled(view, 24, 75)); Click(24, 75);
                Check(Encoding.UTF8.GetString(view.Text).Contains("未加载项目：可配置模型"));
                byte[] model = Encoding.UTF8.GetBytes("fixture-model"); view.Apply([Event(view, 81, 2, model.Length)], model); Build(); Click(81, 3);
                Check(ai.Profile is not null && Enabled(view, 81, 28));
                foreach (ulong low in new ulong[] { 5, 6, 8, 21, 25, 27 }) Check(!Enabled(view, 81, low));
                Click(81, 8); Check(ai.Status == "configured-not-connected"); // Forged disabled authoring click.
                Click(81, 28); Check(ai.Status == "diagnostic-review");
                // The scene stays visible behind the modal. Only the model panel and its
                // diagnostic review must exclude scene data, not the entire GUI packet.
                Check(Encoding.UTF8.GetString(view.Text).Contains("PRIVATE_SCRATCH_OBJECT"));
                foreach (var item in view.Items.ToArray().Where(i => i.WidgetHigh == 81))
                    Check(!Encoding.UTF8.GetString(view.Text.Slice((int)item.LabelOffset, (int)item.LabelLength)).Contains("PRIVATE_SCRATCH_OBJECT"));
                Click(81, 18); Build(); Click(24, 14); Click(24, 75);
                Check(ai.Profile is not null && Enabled(view, 81, 28));
                Check(before.SequenceEqual(owner.Document.CaptureBytes()) && owner.Edit.State.UndoCount == 0 && owner.DomainProposals is null);
            } finally { Check(ai.TryClose()); }
        });
    }
    internal static IEnumerable<(string, Action)> HostCases(string plugins)
    {
        foreach (bool cpu in new[] { true, false }) {
            bool cpuOnly = cpu;
            yield return ($"AI actual {(cpuOnly ? "CPU" : "DX11")} no-project startup owns connection workspace and closes cleanly", () => {
                using var host = new CandidatePresentation(plugins, null, true, cpuOnly, false);
                host.Start(); var ai = host.VerificationAi;
                Check(!ai.ProjectAvailable && ai.Status == "unconfigured" && host.VerificationEditor.DomainProposals is null);
                var view = host.VerificationPresenter;
                view.Build(1, 1440, 900); view.Apply([Event(view, 24, 14)], []); view.Build(2, 1440, 900);
                Check(Enabled(view, 24, 75)); view.Apply([Event(view, 24, 75)], []); view.Build(3, 1440, 900);
                Check(Enabled(view, 81, 3) && !Enabled(view, 81, 8));
                Check(ai.ConfigureLocal("http://127.0.0.1:1/v1/chat/completions", "unused-model"));
                for (ulong i = 4; i <= 11; i++) {
                    var frame = new FrameContext(i, 1.0 / 60);
                    host.PumpPlatform(); host.ApplyEditorIntents(frame); host.PumpAgent(frame); host.BeginPresentation(frame);
                    host.SubmitInput(frame); host.AdvancePlay(frame); host.Present(frame);
                }
                Check(ai.Diagnostic is null); if (!cpuOnly) Check(host.VerificationStats.ValidationErrors == 0);
                host.Dispose(); host.Dispose(); Check(ai.Status == "closed" && ai.Profile is null && host.VerificationAiLifetime.Modules == 0);
            });
        }
    }
}
#endif
