#if NCMA_AI
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.AI;
using Ncma.Animation;
using Ncma.Assets;
using Ncma.Assets.Authoring;
using Ncma.Editor.App;
using Ncma.Editor.Modules;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Ui;

// Registered, not executed during implementation. Uses real GUI event dispatch and writers.
// Graph fixture retains actual paired MCP/read approval and real ActionSample assets.
internal static class AiDomainPresenterTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Domain presenter assertion"); }
    private static void Until(Func<bool> condition) {
        var time = Stopwatch.StartNew(); while (!condition()) { if (time.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Domain presenter drain"); Thread.Sleep(1); }
    }
    private enum Domain { Ui, Metadata, Animation }
    internal static IEnumerable<(string, Action)> Cases(string output, string repository)
    {
        yield return ("M8.4-D UI presenter exact full wire independent diff Commit and original Undo Redo", () => {
            using var f = new Fixture(Domain.Ui, output, repository); f.Prepare(); f.Send(); f.Unchanged();
            Check(f.Outbound.Contains("local.ui.document") && !f.Outbound.Contains("hud.ncmaui"));
            f.RefreshResult(); f.Commit(); Check(UiCodec.Decode(File.ReadAllBytes(f.FilePath)).Name == "After UI 中文 😀"); f.HistoryRoundtrip();
        });
        yield return ("M8.4-D UI presenter file write grant revoked after visible diff rejects final event", () => {
            using var f = new Fixture(Domain.Ui, output, repository); f.Prepare(); f.Send(); f.RefreshResult(); f.ReadAll(); f.Click(13, 1);
            f.Ui!.Revoke(); f.Click(17); f.Unchanged(); Check(!f.Has(17));
        });
        yield return ("M8.4-D UI presenter 12 cancelled approval pages never send or revive after reopen", () => {
            using var f = new Fixture(Domain.Ui, output, repository);
            for (int i = 0; i < 12; i++) {
                f.Prepare(); f.ReadAll(); f.Click(13, 1); var stale = f.Event(14);
                f.Click(9); f.View.Apply([stale], []); f.Build(); Check(f.Calls == 0 && !f.Has(14));
                f.Click(18); f.Open();
            }
            f.Click(4); Check(f.Domains.RegisteredModules.Length == 0 && f.Handlers.All(h => h.Disposals == 1)); f.Unchanged();
        });
        yield return ("M8.4-D metadata presenter local full content has no outbound approval controls", () => {
            using var f = new Fixture(Domain.Metadata, output, repository); f.Click(25);
            string pages = f.ReadAll(requireApproval: false);
            Check(pages.Contains(f.Target.ToString("D")) && pages.Contains("assets/private.bin") && !f.Has(13) && !f.Has(14) && !f.Has(17));
            f.Click(26); Check(f.View.LastMessage == "metadata_local_content_revalidated_not_exported" && f.Calls == 0);
            f.Click(27); Check(!f.Has(26) && !f.Has(14)); f.Unchanged();
        });
        yield return ("M8.4-D metadata presenter selected browser UUID full export settings-only original Commit Undo", () => {
            using var f = new Fixture(Domain.Metadata, output, repository); f.Prepare(); f.Send(); f.Unchanged();
            Check(f.Outbound.Contains("assets/private.bin") && f.Outbound.Contains(f.Target.ToString("D")) && f.Outbound.Contains("metadata"));
            f.RefreshResult(); f.Commit();
            var record = AssetRecordCodec.Decode(File.ReadAllBytes(f.FilePath));
            Check(record.Settings.SampleRate == 30 && !record.Settings.GenerateTangents && record.SourceHash == f.SourceHash); f.HistoryRoundtrip();
        });
        yield return ("M8.4-D metadata presenter external bytes before send and withdrawn write grant before commit reject", () => {
            using (var f = new Fixture(Domain.Metadata, output, repository)) {
                f.Prepare(); f.ReadAll(); f.Click(13, 1);
                byte[] changed = [..f.Before, (byte)' ']; File.WriteAllBytes(f.FilePath, changed); f.Click(14);
                Check(f.Calls == 0 && File.ReadAllBytes(f.FilePath).SequenceEqual(changed) && f.Owner.Edit!.State.UndoCount == f.UndoBefore && !f.Has(14));
            }
            using (var f = new Fixture(Domain.Metadata, output, repository)) {
                f.Prepare(); f.Send(); f.RefreshResult(); f.ReadAll(); f.Click(13, 1); f.Granted = false; f.Click(17); f.Unchanged();
            }
        });
        yield return ("M8.4-D animation presenter no implicit read grant and revoked displayed resource review rejects", () => {
            using var f = new Fixture(Domain.Animation, output, repository); f.Prepare(expectSuccess: false);
            Check(f.Calls == 0 && !f.Has(14)); f.GrantGraph(); f.Prepare(); f.Send(); f.Click(22); f.ReadAll(); f.Click(13, 1);
            f.Input(23, f.Target.ToString("D")); f.Graph!.Graphs.Revoke(); f.Click(24); f.Unchanged(); Check(!f.Has(17));
        });
        yield return ("M8.4-D animation presenter read approval resource UUID approval and separate final Commit Undo", () => {
            using var f = new Fixture(Domain.Animation, output, repository); f.GrantGraph(); f.Prepare(); f.Send(); f.Unchanged();
            Check(f.Controller.Status == "resource-review" && !f.Has(17)); f.Click(22); Check(!f.Enabled(24));
            string resources = f.ReadAll(); Check(resources.Contains("completeCandidate") && resources.Contains("After graph"));
            f.Click(13, 1); f.Input(23, Guid.NewGuid().ToString("D")); Check(!f.Enabled(24)); f.Click(24); f.Unchanged();
            f.Input(23, f.Target.ToString("D")); Check(f.Enabled(24)); f.Click(24);
            Check(f.Has(17) && !f.Enabled(17)); f.Unchanged(); f.Commit();
            Check(AnimationGraphCodec.Decode(File.ReadAllBytes(f.FilePath)).Name == "After graph"); f.HistoryRoundtrip();
        });
        yield return ("M8.4-D animation presenter cancel after resource approval discards candidate and old commit event", () => {
            using var f = new Fixture(Domain.Animation, output, repository); f.GrantGraph(); f.Prepare(); f.Send(); f.Click(22); f.ReadAll(); f.Click(13, 1);
            f.Input(23, f.Target.ToString("D")); f.Click(24); f.ReadAll(); f.Click(13, 1); var stale = f.Event(17);
            f.Click(9); f.View.Apply([stale], []); f.Build(); Check(f.Graph!.Writer!.Pending.Length == 0 && !f.Has(17)); f.Unchanged();
        });
    }
    internal sealed class Handler(string proposal) : HttpMessageHandler
    {
        internal int Calls, Disposals; internal byte[]? Sent;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            Check(request.Headers.Authorization is null); Sent = await request.Content!.ReadAsByteArrayAsync(token); Interlocked.Increment(ref Calls);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new {
                id = "domain-presenter", @object = "chat.completion", created = 1, model = "fixture-model",
                choices = new[] { new { index = 0, message = new { role = "assistant", content = proposal }, finish_reason = "stop" } }
            })) }; response.Content.Headers.ContentType = new("application/json"); return response;
        }
        protected override void Dispose(bool disposing) { if (disposing) Interlocked.Increment(ref Disposals); base.Dispose(disposing); }
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly EditorSessionOwner Owner;
        internal readonly EditorWorkspace Workspace;
        internal readonly EditorPresenter View;
        internal readonly AiAuthoringWorkspace Controller;
        internal readonly EditorUiWorkspace? Ui;
        internal readonly EditorAssetWorkflow? Browser;
        internal readonly GraphInspectionTests.Fixture? Graph;
        internal readonly string FilePath, Root;
        internal readonly Guid Target;
        internal readonly byte[] Before, SceneBefore;
        internal readonly int UndoBefore;
        internal readonly List<Handler> Handlers = [];
        internal readonly string SourceHash = "";
        internal bool Granted = true;
        internal string Outbound = "";
        private readonly Domain domain;
        private readonly string draft;
        private ulong frame = 1000;
        internal EditorDomainProposals Domains => Owner.DomainProposals!;
        internal int Calls => Handlers.Sum(h => h.Calls);
        internal Fixture(Domain domain, string output, string repository) {
            this.domain = domain;
            if (domain == Domain.Animation) {
                Graph = new(output, repository, authoring: true); Owner = Graph.Owner; Workspace = Graph.Workspace; Root = Graph.Root; FilePath = Graph.File; Target = Graph.Graph;
                Owner.ConfigureDomainProposals(Owner.Assets!.ProjectId);
                Controller = new(Domains, (snapshot, operation, credential) => new(snapshot, operation, credential, NewHandler()), animation: Graph.Writer);
                View = new(Workspace, null, Root, workspaceStyle: true); View.AttachGraphAuthoring(Graph.Writer!);
                draft = JsonSerializer.Serialize(new { version = 1, capability = AiAnimationDraft.Capability, input = new { graphId = Target,
                    operations = AnimationGraphEdits.Operations(new { op = "graph.rename", name = "After graph" }) } });
            } else {
                Root = Path.Combine(output, "ai-domain-presenter-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(Root, "assets"));
                Owner = new("Domain presenter"); Workspace = new(Owner); Guid project = Guid.NewGuid();
                if (domain == Domain.Ui) {
                    Ui = new(Workspace, Root); Owner.ConfigureAssets(Root, project, 1, uiScope: Ui.Scope); Owner.ConfigureDomainProposals(project);
                    var review = Ui.ReviewFile(Workspace.Stamp, "assets/hud.ncmaui", true); Ui.ConfirmFile(review.Review, review.Document, true);
                    FilePath = Path.Combine(Root, "assets/hud.ncmaui"); Target = Ui.DocumentId;
                    using var definition = JsonDocument.Parse(UiCodec.Encode(Ui.Capture()! with { Name = "After UI 中文 😀" }));
                    draft = JsonSerializer.Serialize(new { version = 1, capability = AiUiDraft.Capability, input = new { documentId = Target, definition = definition.RootElement } });
                    Controller = new(Domains, (snapshot, operation, credential) => new(snapshot, operation, credential, NewHandler()), ui: Ui);
                    View = new(Workspace, null, Root, workspaceStyle: true); View.AttachUi(Ui, null);
                } else {
                    Target = Guid.NewGuid(); byte[] source = [1, 2, 3]; SourceHash = Convert.ToHexString(SHA256.HashData(source));
                    File.WriteAllBytes(Path.Combine(Root, "assets/private.bin"), source); FilePath = Path.Combine(Root, "assets/descriptor.ncmeta");
                    File.WriteAllBytes(FilePath, AssetRecordCodec.Encode(new(1, Target, AssetKind.Texture, "assets/private.bin", SourceHash, "fixture", 1, new(1, 60, true), [], [], null)));
                    Browser = new(Workspace, Root, project, 1);
                    Owner.ConfigureAssets(Root, project, 1, new AssetWriteScope(["assets/descriptor.ncmeta"], ["assets/private.bin"], [Target], () => Granted));
                    Owner.ConfigureDomainProposals(project);
                    Controller = new(Domains, (snapshot, operation, credential) => new(snapshot, operation, credential, NewHandler()), metadata: Owner.CreateMetadataContentReview());
                    View = new(Workspace, null, Root, assetWorkflow: Browser, workspaceStyle: true);
                    draft = JsonSerializer.Serialize(new { version = 1, capability = AiMetadataDraft.Capability, input = new { assetId = Target, settings = new { version = 1, sampleRate = 30, generateTangents = false } } });
                }
            }
            Before = File.ReadAllBytes(FilePath); SceneBefore = Owner.Document.CaptureBytes(); UndoBefore = Owner.Edit!.State.UndoCount;
            View.AttachAiAuthoring(Controller); Build();
            if (domain == Domain.Metadata) {
                var row = View.Items.ToArray().Single(i => i.WidgetHigh == 16 && Encoding.UTF8.GetString(View.Text.Slice((int)i.TextOffset, (int)i.TextLength)) == Target.ToString("D"));
                Click(row.WidgetLow, high: 16);
            }
            Open(); Input(2, "fixture-model"); Click(3); Click(domain == Domain.Ui ? 6u : domain == Domain.Metadata ? 27u : 21u);
            Input(7, "Change the selected document only");
        }
        private Handler NewHandler() { var handler = new Handler(draft); Handlers.Add(handler); return handler; }
        internal void Build() => View.Build(++frame, 1440, 900);
        internal GuiEvent Event(ulong low, double value = 0, int bytes = 0, ulong high = 81) {
            var item = View.Items.ToArray().Single(i => i.WidgetHigh == high && i.WidgetLow == low);
            return new() { Kind = item.Kind, WidgetHigh = high, WidgetLow = low, Phase = 3, Value = value, TextLength = (uint)bytes,
                Frame = View.Frame.Frame, ViewGeneration = View.Frame.ViewGeneration, DocumentGeneration = View.Frame.DocumentGeneration, Revision = View.Frame.Revision };
        }
        internal bool Has(ulong low) => View.Items.ToArray().Any(i => i.WidgetHigh == 81 && i.WidgetLow == low);
        internal bool Enabled(ulong low) => View.Items.ToArray().Single(i => i.WidgetHigh == 81 && i.WidgetLow == low).Enabled != 0;
        internal void Click(ulong low, double value = 0, ulong high = 81) { View.Apply([Event(low, value, high: high)], []); Build(); }
        internal void Input(ulong low, string text) { byte[] bytes = Encoding.UTF8.GetBytes(text); View.Apply([Event(low, bytes: bytes.Length)], bytes); Build(); }
        internal void Open() { Click(14, high: 24); Click(75, high: 24); }
        internal void GrantGraph() { Graph!.ApproveUi(); Build(); }
        internal string ReadAll(bool requireApproval = true) {
            var text = new StringBuilder();
            for (int i = 0; i < 1200; i++) {
                var items = View.Items.ToArray(); int index = Array.FindIndex(items, item => item.WidgetHigh == 81 && Label(item).StartsWith("完整审阅 ", StringComparison.Ordinal));
                Check(index >= 0 && index + 1 < items.Length); text.Append(Label(items[index + 1]));
                if (!Enabled(12)) { if (requireApproval) Check(Enabled(13)); return text.ToString(); } Click(12);
            }
            throw new Exception("Page budget");
        }
        private string Label(GuiItem item) => Encoding.UTF8.GetString(View.Text.Slice((int)item.LabelOffset, (int)item.LabelLength));
        internal void Prepare(bool expectSuccess = true) { Click(8); Check(Has(14) == expectSuccess); }
        internal void Send() {
            Check(!Enabled(14)); Click(14); Unchanged(); Outbound = ReadAll(); Click(13, 1); Click(14);
            Until(() => { Domains.PumpModules(); return Controller.Status != "pending"; }); Build();
            Check(Controller.Status == (domain == Domain.Animation ? "resource-review" : "review"));
            const string marker = "完整HTTP JSON正文（不含认证头）\n";
            int start = Outbound.IndexOf(marker, StringComparison.Ordinal), end = Outbound.IndexOf("\n正文SHA256 ", StringComparison.Ordinal);
            Check(start >= 0 && end > start + marker.Length && Handlers.Last().Sent!.SequenceEqual(Encoding.UTF8.GetBytes(Outbound[(start + marker.Length)..end])));
        }
        internal void RefreshResult() { Click(10); Check(Has(17) && !Enabled(17)); }
        internal void Commit() {
            Check(!Enabled(17)); Click(17); Unchanged(); string diff = ReadAll(); Check(diff.Contains("完整差异")); Click(13, 1);
            var commit = Event(17); View.Apply([commit, commit], []); Build();
            Check(Owner.Edit!.State.UndoCount == UndoBefore + 1 && Domains.Receipts.Count(r => r.Changed) == 1 && SceneBefore.SequenceEqual(Owner.Document.CaptureBytes()));
        }
        internal void HistoryRoundtrip() {
            byte[] after = File.ReadAllBytes(FilePath); Check(!Before.SequenceEqual(after)); Click(18);
            Ui?.Synchronize(); Graph?.Writer?.Synchronize();
            Check(View.Shortcut(90, true, false, false, true)); Build(); Check(Before.SequenceEqual(File.ReadAllBytes(FilePath)));
            Ui?.Synchronize(); Graph?.Writer?.Synchronize();
            Check(View.Shortcut(89, true, false, false, true)); Build(); Check(after.SequenceEqual(File.ReadAllBytes(FilePath)));
        }
        internal void Unchanged() => Check(Before.SequenceEqual(File.ReadAllBytes(FilePath)) && SceneBefore.SequenceEqual(Owner.Document.CaptureBytes()) && Owner.Edit!.State.UndoCount == UndoBefore);
        public void Dispose() {
            Until(Controller.TryClose); Browser?.Dispose(); Ui?.Dispose(); if (Graph is not null) Graph.Dispose(); else Owner.Dispose();
        }
    }
}
#endif
