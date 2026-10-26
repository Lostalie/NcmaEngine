using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Ncma.AI;
using Ncma.Editor.App;
using Ncma.Editor.Modules;
using Ncma.Editor.Services;
using Ncma.Gui;

// Actual compiled presenter, public GUI packet/event path; no private-field reflection.
// Registered only. Fake HTTP and all cases execute at whole-stage validation, not at build.
internal static class AiPresenterTests
{
    private static void Check(bool value) { if (!value) throw new Exception("AI presenter assertion"); }
    private static GuiEvent Event(EditorPresenter view, ulong high, ulong low, double value = 0, int bytes = 0) {
        var item = view.Items.ToArray().Single(i => i.WidgetHigh == high && i.WidgetLow == low);
        return new() { Kind = item.Kind, WidgetHigh = high, WidgetLow = low, Value = value, Phase = 3,
            Frame = view.Frame.Frame, ViewGeneration = view.Frame.ViewGeneration, DocumentGeneration = view.Frame.DocumentGeneration,
            Revision = view.Frame.Revision, TextLength = (uint)bytes };
    }
    private static string Label(EditorPresenter view, GuiItem item) => Encoding.UTF8.GetString(view.Text.Slice((int)item.LabelOffset, (int)item.LabelLength));
    private static string Text(EditorPresenter view) => Encoding.UTF8.GetString(view.Text);
    internal static IEnumerable<(string, Action)> Cases()
    {
#if !NCMA_AI
        yield return ("M8.4-D default actual presenter has no model workspace action", () => {
            using var owner = new EditorSessionOwner("Default product");
            var view = new EditorPresenter(new(owner), null, workspaceStyle: true);
            view.Build(1, 1440, 900); view.Apply([Event(view, 24, 14)], []); view.Build(2, 1440, 900);
            var entry = view.Items.ToArray().Single(i => i.WidgetHigh == 24 && i.WidgetLow == 75);
            Check(entry.Kind == (uint)GuiItemKind.Label && Label(view, entry).Contains("默认产品无模型模块"));
            view.Apply([Event(view, 24, 75) with { Kind = (uint)GuiItemKind.Button }], []); view.Build(3, 1440, 900);
            Check(!view.Items.ToArray().Any(i => i.WidgetHigh == 81) && owner.Edit!.State.UndoCount == 0);
        });
#else
        yield return ("M8.4-D presenter actual menu configuration and repeated drawing never probe", () => {
            using var f = new Fixture(); Check(f.Ui.Status == "unconfigured");
            f.Configure(); for (int i = 0; i < 12; i++) f.Build();
            Check(f.Ui.Status == "configured-not-connected" && f.Handlers.Count == 0);
            Check(Text(f.View).Contains("已配置 · 未验证连接") && !f.Has(29)); f.Unchanged();
        });
        yield return ("M8.4-D presenter modal blocks background scene events and shortcuts", () => {
            using var f = new Fixture();
            Check(!f.View.SceneHudInputAllowed && !f.View.Shortcut(90, true, false, false, true));
            f.View.Apply([Event(f.View, 2, 8)], []); f.Build(); f.Unchanged();
            f.Click(18); Check(f.View.SceneHudInputAllowed);
            f.View.Apply([Event(f.View, 2, 8)], []); f.Build();
            Check(f.Owner.Document.ObjectCount == 2 && f.Owner.Edit!.State.UndoCount == 1);
        });
        yield return ("M8.4-D presenter invalid input and unapplied config cannot dispatch", () => {
            using var f = new Fixture(); f.Configure(); Guid config = f.Ui.Profile!.ConfigurationId;
            f.Input(1, "http://localhost:8000/v1/chat/completions"); f.Click(3);
            Check(f.Ui.Profile!.ConfigurationId == config && !f.Enabled(28) && f.Handlers.Count == 0);
            Check(f.View.LastMessage == "ai_action_rejected_refresh_or_cancel");
            f.Input(2, new string('x', 1024)); Check(!Text(f.View).Contains(new string('x', 1024)));
            f.Input(1, "http://127.0.0.1:8000/v1/chat/completions"); f.Click(3); Check(f.Enabled(28)); f.Unchanged();
        });
        yield return ("M8.4-D presenter diagnostic full pages checkbox stale and duplicate event fencing", () => {
            using var f = new Fixture(); f.Configure(); f.Click(28);
            Check(f.Handlers.Count == 1 && f.Calls == 0 && !f.Enabled(13) && !f.Enabled(29));
            f.Click(13, 1); f.Click(29); Check(f.Calls == 0); // Forged disabled events must not approve.
            string pages = f.ReadAll(); Check(pages.Contains("ncmaDiagnostic") && pages.Contains(f.Ui.Profile!.Endpoint));
            Check(pages.Contains("正文SHA256") && pages.Contains("maximumCalls") && !pages.Contains("PrivateSceneObject"));
            Check(!f.Enabled(29)); var old = Event(f.View, 81, 29); f.Click(13, 1);
            f.View.Apply([old], []); Check(f.Calls == 0); // Previously disabled packet has old generation.
            var send = Event(f.View, 81, 29); f.View.Apply([send, send], []); f.Build();
            Until(() => f.Ui.Diagnostic is not null); f.Build(); Check(f.Calls == 1 && f.Ui.Diagnostic!.Code == "diagnostic_ok");
            Check(f.Handlers.Single().Sent!.SequenceEqual(Wire(pages, "完整HTTP正文（认证头密钥不显示）\n")));
            Check(Text(f.View).Contains("本次协议回包通过") && !f.Has(29));
            f.View.Apply([send], []); Check(f.Calls == 1); f.Unchanged();
        });
        yield return ("M8.4-D presenter scene request and original diff require separate full approval and Undo", () => {
            using var f = new Fixture(); f.Configure(); f.Input(7, "Rename the selected object 😀 " + new string('q', 800)); f.Click(8);
            Check(f.Calls == 0 && !f.Enabled(14)); string outbound = f.ReadAll();
            Check(outbound.Contains("完整HTTP JSON正文") && outbound.Contains(f.Id.ToString("D")));
            f.Click(13, 1); f.Click(14); f.CompleteAuthoring(); f.Unchanged();
            Check(f.Handlers.Single().Sent!.SequenceEqual(Wire(outbound, "完整HTTP JSON正文（不含认证头）\n")));
            f.Click(10); Check(f.Has(17) && !f.Enabled(17)); f.Click(17); f.Unchanged();
            string diff = f.ReadAll(); Check(diff.Contains("After") && diff.Contains("完整差异"));
            Check(!f.Enabled(17)); f.Click(13, 1); var commit = Event(f.View, 81, 17);
            f.View.Apply([commit, commit], []); f.Build();
            Check(f.Owner.Document.GetObjectName(f.Id) == "After" && f.Owner.Edit!.State.UndoCount == 1);
            Check(f.Calls == 1 && f.Domains.Receipts.Count(r => r.Changed) == 1);
            f.Click(18); Check(f.View.Shortcut(90, true, false, false, true)); f.Build();
            Check(f.Owner.Document.GetObjectName(f.Id) == "PrivateSceneObject" && f.Owner.Edit!.State.UndoCount == 0);
            Check(f.View.Shortcut(89, true, false, false, true)); f.Build(); Check(f.Owner.Document.GetObjectName(f.Id) == "After");
        });
        yield return ("M8.4-D presenter changed document rejects old UI send and old backend review", () => {
            using var f = new Fixture(); f.Configure(); f.Input(7, "Rename"); f.Click(8); f.ReadAll(); f.Click(13, 1);
            var stale = Event(f.View, 81, 14);
            f.Workspace.CreateObject(f.Workspace.Stamp);
            f.View.Apply([stale], []); f.Build(); Check(f.Calls == 0);
            // Rebuild can display old cached pages, but original source validation must still reject.
            f.Click(14); Check(f.Calls == 0 && f.View.LastMessage == "ai_action_rejected_refresh_or_cancel");
            Check(f.Owner.Document.GetObjectName(f.Id) == "PrivateSceneObject" && f.Owner.Edit!.State.UndoCount == 1);
        });
        yield return ("M8.4-D presenter close disconnect and repeated configure retain uncooperative request", () => {
            using var f = new Fixture(); f.Configure(); f.HoldNext = true; f.Click(28); f.ReadAll(); f.Click(13, 1); f.Click(29);
            Until(() => f.Calls == 1); var handler = f.Handlers.Single();
            f.Click(18); Check(!f.Has(28) && handler.Disposals == 0 && f.Ui.Status == "diagnostic-draining");
            f.Open(); f.Click(4); Check(f.Ui.Profile is null && handler.Disposals == 0 && f.Ui.Status == "draining");
            f.Click(3); Check(f.Ui.Profile is null && f.Handlers.Count == 1 && handler.Disposals == 0);
            handler.Release.TrySetResult(); Until(() => f.Ui.Diagnostic is not null); f.Click(4);
            Check(handler.Disposals == 1 && f.Ui.Status == "unconfigured"); f.Configure(); f.Unchanged();
        });
        yield return ("M8.4-D presenter canceled approved pages cannot reappear across 12 reopen cycles", () => {
            using var f = new Fixture(); f.Configure();
            for (int i = 0; i < 12; i++) {
                f.Click(28); f.ReadAll(); f.Click(13, 1); var stale = Event(f.View, 81, 29);
                f.Click(9); Check(!f.Has(29)); f.View.Apply([stale], []); Check(f.Calls == 0);
                f.Click(18); f.Open(); Check(!f.Has(13) && !f.Has(29));
            }
            f.Click(4); Check(f.Handlers.All(h => h.Disposals == 1) && f.Domains.RegisteredModules.Length == 0); f.Unchanged();
        });
#endif
    }
#if NCMA_AI
    private static byte[] Wire(string pages, string marker) {
        int start = pages.IndexOf(marker, StringComparison.Ordinal), end = pages.IndexOf("\n正文SHA256 ", StringComparison.Ordinal);
        Check(start >= 0 && end > start + marker.Length);
        return Encoding.UTF8.GetBytes(pages[(start + marker.Length)..end]);
    }
    private static void Until(Func<bool> condition) {
        var time = Stopwatch.StartNew();
        while (!condition()) { if (time.Elapsed > TimeSpan.FromSeconds(3)) throw new TimeoutException("AI presenter fixture drain"); Thread.Sleep(1); }
    }
    private sealed class Handler(Guid id, bool diagnostic, bool hold) : HttpMessageHandler
    {
        internal int Calls, Disposals;
        internal byte[]? Sent;
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            Check(request.Headers.Authorization is null);
            Sent = await request.Content!.ReadAsByteArrayAsync(token); Interlocked.Increment(ref Calls);
            if (hold) await Release.Task; // Deliberately ignores cancellation; no real network.
            string content = diagnostic ? "{\"ncmaDiagnostic\":\"ok\"}" : JsonSerializer.Serialize(new { version = 1, capability = "ncma.scene.transaction",
                input = new { operations = new[] { new { op = "rename", objectId = id, name = "After" } } } });
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new {
                id = "presenter-fixture", @object = "chat.completion", created = 1, model = "fixture-model",
                choices = new[] { new { index = 0, message = new { role = "assistant", content }, finish_reason = "stop" } }
            })) };
            response.Content.Headers.ContentType = new("application/json"); return response;
        }
        protected override void Dispose(bool disposing) { if (disposing) Interlocked.Increment(ref Disposals); base.Dispose(disposing); }
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly EditorSessionOwner Owner = new("Presenter private project");
        internal readonly EditorWorkspace Workspace;
        internal readonly EditorPresenter View;
        internal readonly AiAuthoringWorkspace Ui;
        internal readonly Guid Id;
        internal readonly List<Handler> Handlers = [];
        internal bool HoldNext;
        private readonly byte[] before;
        private ulong frame;
        internal EditorDomainProposals Domains => Owner.DomainProposals!;
        internal int Calls => Handlers.Sum(h => h.Calls);
        internal Fixture() {
            Id = Owner.Document.CreateObject("PrivateSceneObject"); Owner.Edit!.Resynchronize(); Owner.ConfigureDomainProposals(Guid.NewGuid());
            Workspace = new(Owner); Workspace.Select(Workspace.Stamp, Id); before = Owner.Document.CaptureBytes();
            Ui = new(Domains, (snapshot, operation, credential) => new(snapshot, operation, credential, NewHandler(false)),
                diagnosticFactory: (snapshot, credential) => new(snapshot, credential, NewHandler(true)));
            View = new(Workspace, null, workspaceStyle: true); View.AttachAiAuthoring(Ui); Build(); Open();
        }
        private Handler NewHandler(bool diagnostic) {
            var handler = new Handler(Id, diagnostic, HoldNext); HoldNext = false; Handlers.Add(handler); return handler;
        }
        internal void Build() => View.Build(++frame, 1440, 900);
        internal bool Has(ulong id) => View.Items.ToArray().Any(i => i.WidgetHigh == 81 && i.WidgetLow == id);
        internal bool Enabled(ulong id) => View.Items.ToArray().Single(i => i.WidgetHigh == 81 && i.WidgetLow == id).Enabled != 0;
        internal void Click(ulong id, double value = 0, ulong high = 81) { View.Apply([Event(View, high, id, value)], []); Build(); }
        internal void Input(ulong id, string text) { byte[] bytes = Encoding.UTF8.GetBytes(text); View.Apply([Event(View, 81, id, bytes: bytes.Length)], bytes); Build(); }
        internal void Open() { Click(14, high: 24); Click(75, high: 24); }
        internal void Configure() { Input(2, "fixture-model"); Click(3); Check(Ui.Status == "configured-not-connected"); }
        internal string ReadAll() {
            var text = new StringBuilder();
            for (int i = 0; i < 1200; i++) {
                var items = View.Items.ToArray(); int page = Array.FindIndex(items, item => item.WidgetHigh == 81 && Label(View, item).StartsWith("完整审阅 ", StringComparison.Ordinal));
                Check(page >= 0 && page + 1 < items.Length); text.Append(Label(View, items[page + 1]));
                if (!Enabled(12)) { Check(Enabled(13)); return text.ToString(); }
                Click(12);
            }
            throw new Exception("Review page budget");
        }
        internal void CompleteAuthoring() { Until(() => { Domains.PumpModules(); return Ui.Status != "pending"; }); Build(); Check(Ui.Status == "review"); }
        internal void Unchanged() => Check(before.SequenceEqual(Owner.Document.CaptureBytes()) && Owner.Edit!.State.UndoCount == 0);
        public void Dispose() { foreach (var h in Handlers) h.Release.TrySetResult(); Until(Ui.TryClose); Owner.Dispose(); }
    }
#endif
}
