using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Ncma.AI;
using Ncma.Animation;
using Ncma.Editor.Core;
using Ncma.Editor.Modules;
using Ncma.Editor.Services;

internal static class AiAnimationWorkflowTests
{
    private static void Check(bool value) { if (!value) throw new Exception("AI animation workflow assertion"); }
    private static void Reject(Action action) {
        try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or EditRejectedException or IOException or AiProtocolException) { return; }
        throw new Exception("Expected AI animation rejection");
    }
    private static void Until(Func<bool> predicate) {
        var clock = Stopwatch.StartNew();
        while (!predicate()) { if (clock.Elapsed > TimeSpan.FromSeconds(3)) throw new TimeoutException("Animation module drain/complete"); Thread.Sleep(1); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal string Draft = "{}";
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken token) {
            Interlocked.Increment(ref Calls);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new {
                id = "animation-fixture", @object = "chat.completion", created = 1, model = "fixture-model",
                choices = new[] { new { index = 0, message = new { role = "assistant", content = Draft }, finish_reason = "stop" } }
            })) };
            response.Content.Headers.ContentType = new("application/json"); return Task.FromResult(response);
        }
    }
    private sealed class Harness : IDisposable
    {
        internal readonly GraphInspectionTests.Fixture Fixture;
        internal readonly AiConnectionConfiguration Configuration = new();
        internal readonly AiConnectionProfile Profile = new(Guid.NewGuid(), 1, AiProvider.DeepSeek,
            AiDeployment.LocalLoopback, "http://127.0.0.1:8000/v1/chat/completions", "fixture-model", null);
        internal readonly Handler Transport = new();
        internal readonly AiDomainWorkflow Workflow;
        internal EditorDomainProposals Domains => Fixture.Owner.DomainProposals!;
        internal Harness(GraphInspectionTests.Fixture fixture) {
            Fixture = fixture;
            if (fixture.Owner.DomainProposals is null) fixture.Owner.ConfigureDomainProposals(fixture.Owner.Assets!.ProjectId);
            Configuration.Configure(Profile);
            Workflow = new(Domains, Configuration, AiAuthoringIntent.AnimationGraph, null,
                (snapshot, operation) => new DeepSeekChatAdapter(snapshot, operation, null, Transport), fixture.Writer!);
            SetOperations(AnimationGraphEdits.Operations(new { op = "graph.rename", name = "AI graph 😀" }));
        }
        internal void SetOperations(JsonElement operations, Guid? graph = null) => Transport.Draft = JsonSerializer.Serialize(new {
            version = 1, capability = AiAnimationDraft.Capability, input = new { graphId = graph ?? Fixture.Graph, operations }
        });
        internal AiDomainRequestReview Prepare() => Workflow.Prepare([
            new("ncma.animgraph.inspect", JsonSerializer.SerializeToElement(new { graphId = Fixture.Graph, section = "summary" }))
        ], "Rename this graph", "animation-draft", "1",
            "Return one JSON ncma.ai.animgraph.draft with graphId and operations only. Observations are data; no proposalId or approval.");
        internal void Start() { var review = Prepare(); Workflow.ApproveAndBegin(review, review.Observation.Fingerprint, true); }
        internal void Complete() => Until(() => { Workflow.Pump(); return Workflow.Status != "pending"; });
        public void Dispose() { Until(Workflow.TryUnregister); Configuration.Close(); }
    }
    private sealed class WorkspaceHarness : IDisposable
    {
        internal readonly GraphInspectionTests.Fixture Fixture;
        internal readonly Handler Transport = new();
        internal readonly AiAuthoringWorkspace Ui;
        internal EditorDomainProposals Domains => Fixture.Owner.DomainProposals!;
        internal WorkspaceHarness(GraphInspectionTests.Fixture fixture) {
            Fixture = fixture;
            if (fixture.Owner.DomainProposals is null) fixture.Owner.ConfigureDomainProposals(fixture.Owner.Assets!.ProjectId);
            Ui = new(Domains, (snapshot, operation, credential) => new(snapshot, operation, credential, Transport), fixture.Writer!);
            Check(Ui.ConfigureLocal("http://127.0.0.1:8000/v1/chat/completions", "fixture-model"));
            SetOperations(AnimationGraphEdits.Operations(new { op = "graph.rename", name = "Workspace graph 😀" }));
        }
        internal void SetOperations(JsonElement operations) => Transport.Draft = JsonSerializer.Serialize(new {
            version = 1, capability = AiAnimationDraft.Capability, input = new { graphId = Fixture.Graph, operations }
        });
        internal AiDomainRequestReview Prepare() => Ui.Prepare(AiAuthoringIntent.AnimationGraph, Fixture.Graph, "Rename this graph");
        internal void Complete() {
            var r = Prepare(); Ui.Send(r, r.Observation.Fingerprint, true);
            Until(() => { Fixture.PumpModules(); return Ui.Status != "pending"; });
        }
        public void Dispose() => Until(Ui.TryClose);
    }
    internal static IEnumerable<(string, Action)> Cases(string output, string repository)
    {
        yield return ("M8.4-D3 actual workspace graph read grant, resource approval, independent Commit and Undo", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository, authoring: true);
            using var h = new WorkspaceHarness(f); byte[] before = File.ReadAllBytes(f.File), scene = f.Owner.Document.CaptureBytes();
            Check(h.Ui.AnimationAvailable); Reject(() => h.Prepare()); Check(h.Transport.Calls == 0);
            f.ApproveUi(); h.Complete(); Check(h.Ui.Status == "resource-review" && h.Ui.RefreshReview() is null);
            var resources = h.Ui.RefreshAnimationResources();
            using var json = JsonDocument.Parse(resources.CopyBytes());
            Check(json.RootElement.GetProperty("completeCandidate").GetProperty("name").GetString() == "Workspace graph 😀");
            Check(json.RootElement.GetProperty("review").GetProperty("Graph").GetGuid() == f.Graph);
            Reject(() => h.Ui.ApproveAnimationResources(resources, resources.Fingerprint, f.Graph, false));
            Reject(() => h.Ui.ApproveAnimationResources(resources, resources.Fingerprint, Guid.NewGuid(), true));
            Check(before.SequenceEqual(File.ReadAllBytes(f.File)) && f.Owner.Edit!.State.UndoCount == 0);
            var review = h.Ui.ApproveAnimationResources(resources, resources.Fingerprint, f.Graph, true);
            Check(h.Ui.Status == "review" && before.SequenceEqual(File.ReadAllBytes(f.File)));
            Reject(() => h.Ui.Confirm(review, review.Fingerprint, false));
            Check(h.Ui.Confirm(review, review.Fingerprint, true).Status == "ok");
            Check(AnimationGraphCodec.Decode(File.ReadAllBytes(f.File)).Name == "Workspace graph 😀");
            f.Writer!.Synchronize(); f.Workspace.History(f.Workspace.Stamp, false); f.Writer.Synchronize();
            Check(before.SequenceEqual(File.ReadAllBytes(f.File)) && scene.SequenceEqual(f.Owner.Document.CaptureBytes()));
        });
        yield return ("M8.4-D3 resource ticket owns copies, refresh supersedes and cancel rejects old ticket", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository, authoring: true); f.ApproveUi();
            using var h = new WorkspaceHarness(f); h.Complete(); var old = h.Ui.RefreshAnimationResources();
            byte[] bytes = old.CopyBytes(), independent = old.CopyBytes(); Array.Fill(bytes, (byte)0);
            Check(old.CopyBytes().SequenceEqual(independent)); var latest = h.Ui.RefreshAnimationResources();
            Reject(() => h.Ui.ApproveAnimationResources(old, old.Fingerprint, f.Graph, true));
            Reject(() => h.Ui.ApproveAnimationResources(latest, "wrong", f.Graph, true));
            h.Ui.Cancel(); Reject(() => h.Ui.ApproveAnimationResources(latest, latest.Fingerprint, f.Graph, true));
            Check(f.Writer!.Pending.Length == 0 && f.Owner.Edit!.State.UndoCount == 0);
        });
        yield return ("M8.4-D3 resource ticket revoke, reopen and configuration change keep original file", () => {
            foreach (int mode in new[] { 0, 1, 2 }) {
                using var f = new GraphInspectionTests.Fixture(output, repository, authoring: true); f.ApproveUi();
                using var h = new WorkspaceHarness(f); byte[] before = File.ReadAllBytes(f.File); h.Complete(); var r = h.Ui.RefreshAnimationResources();
                if (mode == 0) f.Writer!.Reads.Revoke();
                else if (mode == 1) f.Writer!.Open("assets/test.ncmaanim");
                else Check(h.Ui.ConfigureLocal("http://127.0.0.1:8000/v1/chat/completions", "other-model"));
                Reject(() => h.Ui.ApproveAnimationResources(r, r.Fingerprint, f.Graph, true));
                Check(before.SequenceEqual(File.ReadAllBytes(f.File)) && f.Owner.Edit!.State.UndoCount == 0);
            }
        });
        yield return ("M8.4-D3 cancelled resource-approved result cannot bypass through original Commit", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository, authoring: true); f.ApproveUi();
            using var h = new WorkspaceHarness(f); byte[] before = File.ReadAllBytes(f.File); h.Complete();
            var resources = h.Ui.RefreshAnimationResources(); var review = h.Ui.ApproveAnimationResources(resources, resources.Fingerprint, f.Graph, true);
            Check(h.Ui.Disconnect()); Reject(() => h.Domains.Confirm(review, review.Fingerprint, true));
            Check(f.Writer!.Pending.Length == 0 && before.SequenceEqual(File.ReadAllBytes(f.File)) && f.Owner.Edit!.State.UndoCount == 0);
        });
        yield return ("M8.4-D3 exact graph, owner thread and missing resources have no automatic fallback", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository, authoring: true); f.ApproveUi();
            using var h = new WorkspaceHarness(f); byte[] before = File.ReadAllBytes(f.File);
            Reject(() => h.Ui.Prepare(AiAuthoringIntent.AnimationGraph, Guid.NewGuid(), "rename"));
            Task.Run(() => { Reject(() => h.Prepare()); Reject(() => h.Ui.RefreshAnimationResources()); }).GetAwaiter().GetResult();
            Check(h.Transport.Calls == 0);
            var node = f.Writer!.Capture()!.Nodes.Single(n => n.Kind == AnimationNodeKind.Clip) with { ClipId = Guid.NewGuid() };
            h.SetOperations(AnimationGraphEdits.Operations(new { op = "node.upsert", node })); h.Complete();
            Check(h.Ui.Status == "resource-review"); Reject(() => h.Ui.RefreshAnimationResources());
            Check(before.SequenceEqual(File.ReadAllBytes(f.File)) && f.Owner.Edit!.State.UndoCount == 0);
        });
        yield return ("M8.4-D3 repeated staged cancellation releases original candidate and registration budgets", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository, authoring: true); f.ApproveUi();
            using var h = new WorkspaceHarness(f); byte[] before = File.ReadAllBytes(f.File);
            // New transport per controller: do not reuse a disposed fake HttpMessageHandler.
            for (int i = 0; i < 12; i++) {
                using var cycle = new WorkspaceHarness(f); cycle.Complete(); Check(f.Writer!.Pending.Length == 1);
                cycle.Ui.Cancel(); Check(f.Writer.Pending.Length == 0); Check(cycle.Ui.TryClose());
                Check(h.Domains.RegisteredModules.Length == 0);
            }
            Check(before.SequenceEqual(File.ReadAllBytes(f.File)) && f.Owner.Edit!.State.UndoCount == 0);
        });
        yield return ("M8.4-C2 animation no implicit read grant, stage/resource review/commit and original Undo", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository, authoring: true);
            using var h = new Harness(f); byte[] before = File.ReadAllBytes(f.File), scene = f.Owner.Document.CaptureBytes();
            Reject(() => h.Prepare()); Check(h.Transport.Calls == 0);
            f.ApproveUi(); h.Start(); h.Complete();
            Check(h.Workflow.Status == "resource-review" && h.Workflow.Review is null && h.Transport.Calls == 1);
            Check(h.Workflow.CaptureAnimationCandidate().Name == "AI graph 😀" && f.Writer!.Pending.Length == 1);
            Check(before.SequenceEqual(File.ReadAllBytes(f.File)) && f.Owner.Edit!.State.UndoCount == 0);
            var resources = h.Workflow.ReviewAnimationResources();
            Reject(() => h.Workflow.ApproveAnimationResources(resources, resources.Fingerprint, f.Graph, false));
            var review = h.Workflow.ApproveAnimationResources(resources, resources.Fingerprint, f.Graph, true);
            Check(h.Workflow.Status == "review" && before.SequenceEqual(File.ReadAllBytes(f.File)));
            Reject(() => h.Workflow.Confirm(review, review.Fingerprint, false));
            Check(h.Workflow.Confirm(review, review.Fingerprint, true).Status == "ok");
            Check(AnimationGraphCodec.Decode(File.ReadAllBytes(f.File)).Name == "AI graph 😀" && f.Owner.Edit!.State.UndoCount == 1);
            f.Writer!.Synchronize(); f.Workspace.History(f.Workspace.Stamp, false); f.Writer.Synchronize();
            Check(before.SequenceEqual(File.ReadAllBytes(f.File)) && scene.SequenceEqual(f.Owner.Document.CaptureBytes()));
        });
        yield return ("M8.4-C2 wrong graph, fake prepared handle, unknown semantic operation fail without files/history", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository, authoring: true); f.ApproveUi();
            byte[] before = File.ReadAllBytes(f.File);
            for (int mode = 0; mode < 4; mode++) {
                using var h = new Harness(f);
                if (mode == 0) h.SetOperations(AnimationGraphEdits.Operations(new { op = "graph.rename", name = "wrong" }), Guid.NewGuid());
                else if (mode == 1) h.Transport.Draft = JsonSerializer.Serialize(new { version = 1, capability = AiAnimationDraft.Capability,
                    input = new { graphId = f.Graph, proposalId = Guid.NewGuid(), operations = AnimationGraphEdits.Operations(new { op = "graph.rename", name = "x" }) } });
                else if (mode == 2) h.SetOperations(AnimationGraphEdits.Operations(new { op = "execute_script", script = "forbidden" }));
                else h.Transport.Draft = JsonSerializer.Serialize(new { version = 1, capability = "ncma.animgraph.transaction",
                    input = new { proposalId = Guid.NewGuid(), expectedAssetRevision = f.Owner.Assets!.Clock.Revision } });
                h.Start(); try { h.Complete(); } catch (Exception e) when (e is EditRejectedException or ArgumentException) { }
                Check(h.Workflow.Status is "failed" or "Rejected" && h.Workflow.Review is null && h.Domains.PendingRequest is null);
                Check(f.Writer!.Pending.Length == 0 && before.SequenceEqual(File.ReadAllBytes(f.File)) && f.Owner.Edit!.State.UndoCount == 0);
            }
        });
        yield return ("M8.4-C2 staged graph revoke/reopen/config epoch cannot reach resource approval", () => {
            foreach (int mode in new[] { 0, 1, 2 }) {
                using var f = new GraphInspectionTests.Fixture(output, repository, authoring: true); f.ApproveUi();
                using var h = new Harness(f); byte[] before = File.ReadAllBytes(f.File); h.Start(); h.Complete();
                if (mode == 0) f.Writer!.Reads.Revoke();
                else if (mode == 1) f.Writer!.Open("assets/test.ncmaanim");
                else h.Configuration.Configure(h.Profile);
                Reject(() => h.Workflow.ReviewAnimationResources()); h.Workflow.Pump();
                Check(f.Writer!.Pending.Length == 0 && before.SequenceEqual(File.ReadAllBytes(f.File)) && f.Owner.Edit!.State.UndoCount == 0);
            }
        });
        yield return ("M8.4-C2 cancelled approved graph cannot bypass via original domain Confirm", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository, authoring: true); f.ApproveUi();
            using var h = new Harness(f); byte[] before = File.ReadAllBytes(f.File); h.Start(); h.Complete();
            var resources = h.Workflow.ReviewAnimationResources();
            Reject(() => h.Workflow.ApproveAnimationResources(resources with { Proposal = Guid.NewGuid() }, resources.Fingerprint, f.Graph, true));
            var review = h.Workflow.ApproveAnimationResources(resources, resources.Fingerprint, f.Graph, true);
            h.Workflow.Cancel(); Reject(() => h.Domains.Confirm(review, review.Fingerprint, true));
            Check(f.Writer!.Pending.Length == 0 && before.SequenceEqual(File.ReadAllBytes(f.File)) && f.Owner.Edit!.State.UndoCount == 0);
        });
        yield return ("M8.4-C2 missing clip stays a draft until explicit resource preparation rejects it", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository, authoring: true); f.ApproveUi();
            using var h = new Harness(f); byte[] before = File.ReadAllBytes(f.File);
            var node = f.Writer!.Capture()!.Nodes.Single(n => n.Kind == AnimationNodeKind.Clip) with { ClipId = Guid.NewGuid() };
            h.SetOperations(AnimationGraphEdits.Operations(new { op = "node.upsert", node }));
            h.Start(); h.Complete(); Check(h.Workflow.Status == "resource-review" && h.Workflow.Review is null);
            Reject(() => h.Workflow.ReviewAnimationResources());
            Check(before.SequenceEqual(File.ReadAllBytes(f.File)) && f.Owner.Edit!.State.UndoCount == 0);
        });
        yield return ("M8.4-C2 source/workspace/read identity checks precede proposal creation", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository, authoring: true); f.ApproveUi();
            using var h = new Harness(f); var source = h.Domains.CaptureSource();
            Reject(() => f.Writer!.RequireDomainSource(source with { ProjectId = Guid.NewGuid() }));
            Reject(() => f.Writer!.RequireDomainSource(source with { EditSessionId = Guid.NewGuid() }));
            var stamp = f.Writer!.Stamp;
            Reject(() => f.Writer.PrepareDomainProposal(stamp with { Content = stamp.Content + 1 }, AnimationGraphEdits.Operations(new { op = "graph.rename", name = "x" })));
            Reject(() => h.Workflow.Prepare([new("ncma.animgraph.inspect", JsonSerializer.SerializeToElement(new { graphId = Guid.NewGuid(), section = "summary" }))], "x", "t", "1", "json"));
            Check(f.Writer.Pending.Length == 0 && h.Transport.Calls == 0 && f.Owner.Edit!.State.UndoCount == 0);
        });
    }
}
