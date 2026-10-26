using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Assets.Authoring;
using Ncma.Editor.App;
using Ncma.Editor.Core;
using Ncma.Editor.Modules;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Modules;
using Ncma.Ui;

internal static class DomainIntegrationTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action) {
        try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or EditRejectedException) { return; }
        throw new Exception("Expected domain rejection.");
    }
    private sealed class Clock : TimeProvider { internal long Now; public override long TimestampFrequency => 1000; public override long GetTimestamp() => Now; }
    private sealed class Fixture : IDisposable {
        internal readonly Clock Time = new();
        internal readonly EditorSessionOwner Owner;
        internal readonly EditorDomainProposals Domains;
        internal readonly EditorWorkspace Workspace;
        internal readonly Guid Id;
        internal Fixture() {
            Owner = new("Domain integration", workflowTime: Time); Id = Owner.Document.CreateObject("Original 😀");
            Owner.Edit!.Resynchronize(); Owner.ConfigureDomainProposals(Guid.NewGuid()); Domains = Owner.DomainProposals!; Workspace = new(Owner);
        }
        internal byte[] Rename(string name = "Changed 😀") => Draft("ncma.scene.transaction", new { operations = new[] { new { op = "rename", objectId = Id, name } } });
        internal DomainProposalReview Review(byte[]? bytes = null) => Domains.Review(Guid.NewGuid(), Domains.CaptureSource(), bytes ?? Rename());
        internal DomainReadRequest[] Reads => [new("ncma.scene.object.inspect", JsonSerializer.SerializeToElement(new { objectId = Id, section = "summary" }))];
        public void Dispose() => Owner.Dispose();
    }
    private static byte[] Draft(string capability, object input) => JsonSerializer.SerializeToUtf8Bytes(new { version = 1, capability, input });
    private static ModuleOperation Operation() => new(Guid.NewGuid(), "1.0", Guid.NewGuid(), "test.scene.propose", Guid.NewGuid(), Guid.NewGuid());
    private sealed class Adapter(Func<ModuleRequest, Task<ModuleReply>> run) : IModuleAdapter {
        public Task<ModuleReply> InvokeAsync(ModuleRequest request, TimeSpan budget, CancellationToken cancellation) => run(request);
    }
    private static ModuleReply Reply(ModuleRequest request, byte[] bytes) => new(2, request.RequestId, request.SessionId,
        request.Epoch, request.Fingerprint, request.Operation.OutputSchema, ModuleReplyStatus.Proposal, new(bytes));
    private static void Wait(Func<bool> done) {
        var watch = Stopwatch.StartNew(); while (!done()) { if (watch.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Domain bridge drain timeout."); Thread.Sleep(1); }
    }
    internal static IEnumerable<(string, Action)> Cases(string output, string repository)
    {
        yield return ("M8.3 complete scene diff / single confirm / original Undo Redo", () => {
            using var f = new Fixture(); byte[] before = f.Owner.Document.CaptureBytes(); var r = f.Review();
            string details = Encoding.UTF8.GetString(r.CopyChanges()); Check(details.Contains("Original") && details.Contains("Changed"), "Both complete sides.");
            Reject(() => f.Domains.Confirm(r, r.Fingerprint, false)); Reject(() => f.Domains.Confirm(r, "bad", true));
            Check(before.SequenceEqual(f.Owner.Document.CaptureBytes()), "Review has no writes.");
            var copy = r.CopyChanges(); copy[0] = 0;
            var result = f.Domains.Confirm(r, r.Fingerprint, true); Check(result.Status == "ok" && result.Changed, result.Code);
            Check(f.Owner.Edit!.State.UndoCount == 1 && f.Owner.Document.GetObjectName(f.Id) == "Changed 😀", "One original history.");
            Reject(() => f.Domains.Confirm(r, r.Fingerprint, true));
            f.Workspace.History(f.Workspace.Stamp, false); Check(before.SequenceEqual(f.Owner.Document.CaptureBytes()), "Undo exact authoring bytes.");
            f.Workspace.History(f.Workspace.Stamp, true); Check(f.Owner.Document.GetObjectName(f.Id) == "Changed 😀", "Redo original history.");
            Check(f.Domains.Receipts.Length == 1, "Single receipt.");
        });
        yield return ("M8.3 destructive UUID is independently confirmed and history retains it", () => {
            using var f = new Fixture(); byte[] before = f.Owner.Document.CaptureBytes();
            var r = f.Review(Draft("ncma.scene.delete_object", new { objectId = f.Id }));
            Check(r.DestructiveTarget == f.Id, "Exact destructive scope.");
            Reject(() => f.Domains.Confirm(r, r.Fingerprint, true)); Reject(() => f.Domains.Confirm(r, r.Fingerprint, true, Guid.NewGuid()));
            Check(f.Domains.Confirm(r, r.Fingerprint, true, f.Id).Status == "ok", "Delete succeeds only after exact confirmation.");
            Check(f.Owner.Edit!.HistoryDestructiveTargets(false).SequenceEqual([f.Id]), "Original history destructive target.");
            f.Workspace.History(f.Workspace.Stamp, false); Check(before.SequenceEqual(f.Owner.Document.CaptureBytes()), "Delete Undo.");
        });
        yield return ("M8.3 revocation / expiry / foreign / frozen / thread rejects without history", () => {
            using var f = new Fixture(); var r = f.Review(); f.Time.Now = 60_000;
            Reject(() => f.Domains.Confirm(r, r.Fingerprint, true)); f.Time.Now = 0;
            f.Domains.Invalidate(); Reject(() => f.Domains.Confirm(r, r.Fingerprint, true));
            r = f.Review(); using var other = new Fixture(); Reject(() => other.Domains.Confirm(r, r.Fingerprint, true));
            Task.Run(() => Reject(() => f.Domains.Confirm(r, r.Fingerprint, true))).GetAwaiter().GetResult();
            f.Owner.Edit!.SetFrozen(true); Check(!f.Domains.IsCurrent(r), "Frozen hidden."); Reject(() => f.Domains.Confirm(r, r.Fingerprint, true)); f.Owner.Edit.SetFrozen(false);
            f.Domains.Discard(r); Reject(() => f.Domains.Confirm(r, r.Fingerprint, true)); Check(f.Owner.Edit.State.UndoCount == 0, "No denied history.");
        });
        yield return ("M8.3 approved observation to real mailbox / exact source / copied payload / one result", () => {
            using var f = new Fixture(); ModuleRequest? observed = null;
            var bridge = new DomainModuleBridge(f.Domains, Operation(), new Adapter(request => { observed = request; return Task.FromResult(Reply(request, f.Rename())); }));
            var r = f.Domains.ReviewObservation(bridge.Target, f.Reads); byte[] bytes = r.CopyBytes();
            Reject(() => f.Domains.ApproveObservation(r, r.Fingerprint, false));
            var approved = f.Domains.ApproveObservation(r, r.Fingerprint, true); bytes[0] = 0;
            bridge.Begin(approved);
            try {
                Reject(() => f.Domains.ClaimObservation(approved, bridge.Target));
                Wait(() => { bridge.Pump(); return bridge.Status != "pending"; });
                Check(bridge.Status == "review" && bridge.Review is not null && observed is not null, "Module returns draft only.");
                Check(observed!.Context == DomainModuleBridge.Context(approved.Source) && observed.Observation.CopyBytes()[0] == '{', "Exact context / private bytes.");
                Check(f.Owner.Edit!.State.UndoCount == 0, "No automatic commit.");
                Reject(() => f.Domains.ReceiveModuleResult(approved, f.Rename()));
                var review = bridge.Review!; Check(f.Domains.Confirm(review, review.Fingerprint, true).Status == "ok", "Original commit after second approval.");
            } finally { bridge.Cancel(); Wait(bridge.TryClose); }
        });
        yield return ("M8.3 observation grants are recipient-specific, bounded, single-use and stale", () => {
            using var f = new Fixture(); var bridge = new DomainModuleBridge(f.Domains, Operation(), new Adapter(r => Task.FromResult(Reply(r, f.Rename()))));
            Reject(() => f.Domains.ReviewObservation(bridge.Target with { ConfigurationId = Guid.NewGuid() }, f.Reads));
            Reject(() => f.Domains.ReviewObservation(bridge.Target, Enumerable.Repeat(f.Reads[0], 9).ToArray()));
            Reject(() => f.Domains.ReviewObservation(bridge.Target, [new("ncma.runtime.inspect", JsonSerializer.SerializeToElement(new { }))]));
            var r = f.Domains.ReviewObservation(bridge.Target, f.Reads); var approved = f.Domains.ApproveObservation(r, r.Fingerprint, true);
            Reject(() => f.Domains.ClaimObservation(approved, bridge.Target with { OutputSchema = Guid.NewGuid() }));
            f.Domains.Invalidate(); Reject(() => bridge.Begin(approved));
            r = f.Domains.ReviewObservation(bridge.Target, f.Reads); f.Time.Now = 60_000; Reject(() => f.Domains.ApproveObservation(r, r.Fingerprint, true));
            Check(f.Owner.Edit!.State.UndoCount == 0, "No observation writes.");
        });
        yield return ("M8.3 late completion after invalidation cannot revive or execute", () => {
            using var f = new Fixture(); var started = new TaskCompletionSource<ModuleRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
            var done = new TaskCompletionSource<ModuleReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            var bridge = new DomainModuleBridge(f.Domains, Operation(), new Adapter(r => { started.TrySetResult(r); return done.Task; }));
            var review = f.Domains.ReviewObservation(bridge.Target, f.Reads); bridge.Begin(f.Domains.ApproveObservation(review, review.Fingerprint, true));
            try {
                bridge.Pump(); Wait(() => started.Task.IsCompleted); f.Domains.Invalidate(); bridge.Pump();
                Check(bridge.Status == "cancelled" && !bridge.TryClose(), "Retained worker not falsely closed.");
                done.SetResult(Reply(started.Task.Result, f.Rename())); Wait(bridge.TryClose);
                Check(f.Domains.LatestReview is null && f.Owner.Edit!.State.UndoCount == 0, "Late result discarded.");
            } finally { done.TrySetCanceled(); bridge.Cancel(); Wait(bridge.TryClose); }
        });
        yield return ("M8.3 UI proposal keeps semantic actions, file grants and exact original memento", () => {
            string root = Path.Combine(output, "domain-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "assets"));
            using var owner = new EditorSessionOwner("Domain UI"); var workspace = new EditorWorkspace(owner);
            using var ui = new EditorUiWorkspace(workspace, root); Guid project = Guid.NewGuid();
            owner.ConfigureAssets(root, project, 1, uiScope: ui.Scope); owner.ConfigureDomainProposals(project);
            var file = ui.ReviewFile(workspace.Stamp, "assets/hud.ncmaui", true); ui.ConfirmFile(file.Review, file.Document, true);
            var domains = owner.DomainProposals!; var candidate = ui.Capture()! with { Name = "Proposed UI" };
            byte[] scene = owner.Document.CaptureBytes(), original = File.ReadAllBytes(Path.Combine(root, "assets/hud.ncmaui"));
            var envelope = ui.PrepareDomainProposal(ui.Stamp, candidate);
            var review = domains.Review(Guid.NewGuid(), domains.CaptureSource(), Encoding.UTF8.GetBytes(envelope.GetRawText()));
            Check(original.SequenceEqual(File.ReadAllBytes(Path.Combine(root, "assets/hud.ncmaui"))), "Read-only UI preflight.");
            var result = domains.Confirm(review, review.Fingerprint, true); Check(result.Status == "ok", result.Code);
            ui.Synchronize(); Check(ui.Capture()!.Name == "Proposed UI" && scene.SequenceEqual(owner.Document.CaptureBytes()), "File-only shared-history change.");
            workspace.History(workspace.Stamp, false); ui.Synchronize(); Check(original.SequenceEqual(File.ReadAllBytes(Path.Combine(root, "assets/hud.ncmaui"))), "UI Undo.");
            var invalid = candidate with { Elements = candidate.Elements.Select(e => e with { Action = "unregistered.execute" }).ToArray() };
            Reject(() => ui.PrepareDomainProposal(ui.Stamp, invalid));
            envelope = ui.PrepareDomainProposal(ui.Stamp, candidate); review = domains.Review(Guid.NewGuid(), domains.CaptureSource(), Encoding.UTF8.GetBytes(envelope.GetRawText()));
            ui.Resources.Revoke();
            result = domains.Confirm(review, review.Fingerprint, true);
            Check(result.Status != "ok" && result.Code == "participant_review_changed" && original.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"assets/hud.ncmaui"))), "Resource epoch invalidates exact review before writing.");
            envelope = ui.PrepareDomainProposal(ui.Stamp, candidate); review = domains.Review(Guid.NewGuid(), domains.CaptureSource(), Encoding.UTF8.GetBytes(envelope.GetRawText()));
            // External file changes keep edit revision unchanged: the new original memento must not replace the reviewed one.
            File.WriteAllBytes(Path.Combine(root, "assets/hud.ncmaui"), UiCodec.Encode(candidate with { Name = "External" }));
            result = domains.Confirm(review, review.Fingerprint, true); Check(result.Status != "ok" && result.Code == "participant_review_changed", result.Code);
            Reject(() => domains.Confirm(review, review.Fingerprint, true));
        });
        yield return ("M8.3 asset metadata uses original exact source hash and independent file scope", () => {
            string root = Path.Combine(output, "domain-asset-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "assets"));
            byte[] source = [1, 2, 3]; File.WriteAllBytes(Path.Combine(root, "assets/source.bin"), source);
            Guid project = Guid.NewGuid(), id = Guid.NewGuid(); bool granted = false;
            using var owner = new EditorSessionOwner("Domain asset");
            owner.ConfigureAssets(root, project, 1, new(["assets/source.bin.ncmeta"], ["assets/source.bin"], [id], () => granted));
            owner.ConfigureDomainProposals(project); var d = owner.DomainProposals!;
            var record = new AssetRecord(1, id, AssetKind.Texture, "assets/source.bin", Convert.ToHexString(SHA256.HashData(source)), "test", 1, new(1, 60, true), [], [], null);
            using var json = JsonDocument.Parse(AssetRecordCodec.Encode(record));
            byte[] draft = Draft(AssetMetadataCommands.CapabilityName, new { operation = "create", path = "assets/source.bin.ncmeta", expectedAssetRevision = owner.Assets!.Clock.Revision, record = json.RootElement });
            Reject(() => d.Review(Guid.NewGuid(), d.CaptureSource(), draft)); granted = true;
            var review = d.Review(Guid.NewGuid(), d.CaptureSource(), draft); granted = false;
            var denied = d.Confirm(review, review.Fingerprint, true); Check(denied.Status != "ok" && !File.Exists(Path.Combine(root, "assets/source.bin.ncmeta")), "Grant revoked before publication.");
            granted = true; review = d.Review(Guid.NewGuid(), d.CaptureSource(), draft);
            Check(d.Confirm(review, review.Fingerprint, true).Status == "ok", "Metadata through original transaction.");
            var workspace = new EditorWorkspace(owner); workspace.History(workspace.Stamp, false); Check(!File.Exists(Path.Combine(root, "assets/source.bin.ncmeta")), "Metadata Undo.");
            File.WriteAllBytes(Path.Combine(root, "assets/source.bin"), [4, 5, 6]);
            Reject(() => d.Review(Guid.NewGuid(), d.CaptureSource(), draft));
        });
        yield return ("M8.3 real animation proposal retains graph/resource approval and original history", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository, authoring: true); var owner = f.Owner;
            owner.ConfigureDomainProposals(owner.Assets!.ProjectId); var domains = owner.DomainProposals!; f.ApproveUi();
            Guid proposal = Guid.NewGuid();
            var prepared = f.Call("ncma.animgraph.propose", new { graphId = f.Graph, proposalId = proposal,
                operations = Ncma.Animation.AnimationGraphEdits.Operations(new { op = "graph.rename", name = "Domain animation" }) }, Guid.NewGuid());
            Check(!prepared.GetProperty("isError").GetBoolean(), "Original graph preparation.");
            byte[] bytes = Draft(AnimationGraphCommands.CapabilityName, new { proposalId = proposal, expectedAssetRevision = owner.Assets.Clock.Revision });
            Reject(() => domains.Review(Guid.NewGuid(), domains.CaptureSource(), bytes));
            var r = f.Writer!.CaptureReview(proposal); f.Writer.Approve(r, r.Fingerprint, r.Graph, true);
            var review = domains.Review(Guid.NewGuid(), domains.CaptureSource(), bytes);
            Check(domains.Confirm(review, review.Fingerprint, true).Status == "ok", "Original graph transaction after separate approval.");
            Check(Ncma.Animation.AnimationGraphCodec.Decode(File.ReadAllBytes(f.File)).Name == "Domain animation", "Graph result.");
            f.Writer.Synchronize(); f.Workspace.History(f.Workspace.Stamp, false); f.Writer.Synchronize();
            Check(Ncma.Animation.AnimationGraphCodec.Decode(File.ReadAllBytes(f.File)).Name == "Approved graph", "Original graph Undo.");
        });
        yield return ("M8.3 actual presenter requires complete review and rejects stale confirm events", () => Presenter());
        yield return ("M8.3 graph observation approval never overrides original read-grant revocation", () => {
            using var f = new GraphInspectionTests.Fixture(output,repository);
            f.Owner.ConfigureDomainProposals(Guid.NewGuid());var domains=f.Owner.DomainProposals!;
            var bridge=new DomainModuleBridge(domains,Operation(),new Adapter(_=>throw new Exception("Revoked observation must never dispatch.")));
            DomainReadRequest[] reads=[new("ncma.animgraph.inspect",JsonSerializer.SerializeToElement(new{graphId=f.Graph,section="nodes"}))];
            Reject(()=>domains.ReviewObservation(bridge.Target,reads));f.ApproveUi();
            var review=domains.ReviewObservation(bridge.Target,reads);var approved=domains.ApproveObservation(review,review.Fingerprint,true);
            f.Graphs.Revoke();Reject(()=>bridge.Begin(approved));Check(domains.PendingRequest is null,"Failed claim consumed.");
            Check(f.Owner.Edit!.State.UndoCount==0,"No observation history.");
        });
    }
    private static void Presenter()
    {
        using var f = new Fixture(); var view = new EditorPresenter(f.Workspace, null, workspaceStyle: true); ulong frame = 0;
        void Build() => view.Build(++frame, 1280, 720);
        GuiEvent Event(ulong high, ulong low, double value = 0) {
            var item = view.Items.ToArray().Single(x => x.WidgetHigh == high && x.WidgetLow == low); var stamp = view.Frame;
            return new() { Kind = item.Kind, WidgetHigh = high, WidgetLow = low, Phase = 3, Value = value,
                Frame = stamp.Frame, ViewGeneration = stamp.ViewGeneration, DocumentGeneration = stamp.DocumentGeneration, Revision = stamp.Revision };
        }
        Build(); view.Apply([Event(24, 14)], []); Build(); view.Apply([Event(24, 74)], []);
        _ = f.Review(); Build(); view.Apply([Event(80, 3)], []); Build();
        Check(view.Items.ToArray().Single(i => i.WidgetHigh == 80 && i.WidgetLow == 10).Enabled == 0, "No unchecked submission.");
        // Review is paginated losslessly, including complete original input and both values.
        while (view.Items.ToArray().Single(i => i.WidgetHigh == 80 && i.WidgetLow == 6).Enabled != 0) { view.Apply([Event(80, 6)], []); Build(); }
        view.Apply([Event(80, 7, 1)], []); Build(); var confirm = Event(80, 10);
        view.Apply([confirm], []); Check(f.Owner.Edit!.State.UndoCount == 1, "Presenter original commit.");
        view.Apply([confirm], []); Check(f.Owner.Edit.State.UndoCount == 1, "Stale event no duplicate.");
        f.Owner.Edit.SetFrozen(true); Build(); Check(!view.Items.ToArray().Any(i => i.WidgetHigh == 80 && i.WidgetLow == 10), "Play hides proposal values.");
        f.Owner.Edit.SetFrozen(false);
    }
}
