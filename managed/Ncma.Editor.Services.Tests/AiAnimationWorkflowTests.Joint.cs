using System.Security.Cryptography;
using System.Text.Json;
using Ncma.AI;
using Ncma.Animation;
using Ncma.Assets;
using Ncma.Assets.Runtime;
using Ncma.Editor.Core;
using Ncma.Editor.Modules;
using Ncma.Editor.Services;

internal static partial class AiAnimationWorkflowTests
{
    private static string JointHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static JsonElement SpeedBinding(AnimationGraphDefinition graph, out Guid parameter, string mode = "valid")
    {
        parameter = Guid.NewGuid();
        var value = new AnimationParameter(parameter, "Playback speed", mode == "type" ? AnimationParameterKind.Int : AnimationParameterKind.Float,
            mode == "type" ? 0 : 2, mode == "type" ? 2 : 0, false);
        var node = AnimationGraphNode.Create(Guid.NewGuid(), "Speed", AnimationNodeKind.Parameter) with { ParameterId = parameter };
        var clip = graph.Nodes.Single(n => n.Kind == AnimationNodeKind.Clip);
        var link = new AnimationGraphLink(Guid.NewGuid(), node.Id, mode == "port" ? "missing" : "value", clip.Id, "speed");
        return AnimationGraphEdits.Operations(new { op = "parameter.upsert", parameter = value }, new { op = "node.upsert", node }, new { op = "link.upsert", link });
    }
    private static JsonElement Cycle(AnimationGraphDefinition graph)
    {
        var a = AnimationGraphNode.Create(Guid.NewGuid(), "Cycle A", AnimationNodeKind.CachePose);
        var b = AnimationGraphNode.Create(Guid.NewGuid(), "Cycle B", AnimationNodeKind.CachePose);
        var output = graph.Links.Single();
        return AnimationGraphEdits.Operations(new { op = "node.upsert", node = a }, new { op = "node.upsert", node = b },
            new { op = "link.upsert", link = new AnimationGraphLink(Guid.NewGuid(), a.Id, "pose", b.Id, "pose") },
            new { op = "link.upsert", link = new AnimationGraphLink(Guid.NewGuid(), b.Id, "pose", a.Id, "pose") },
            new { op = "link.upsert", link = output with { From = a.Id } });
    }
    private static RuntimeAssetSnapshot JointAssets(GraphInspectionTests.Fixture f)
        => RuntimeAssetLoader.Prepare(f.Root, f.Owner.Assets!.ProjectId,
            [new(new(f.Rig), AssetKind.Skeleton), new(new(f.Clip), AssetKind.Clip)]);

    // Real pinned payloads/program preparation; no made-up duration or generation.
    // This CPU check is not a World step, GPU result or human playback acceptance.
    private static AnimationProgram CheckPlayback(RuntimeAssetLease source, AnimationGraphDefinition graph, double speed, Guid parameter = default)
    {
        using var snapshot = source.CreateGraphPreview(graph); using var lease = snapshot.AcquireLease();
        var asset = (RuntimeAnimationGraphAsset)lease.Require(graph.AssetId, AssetKind.AnimationGraph);
        var program = asset.PrepareProgram(lease);
        Check(program.ContentHash == JointHash(AnimationGraphCodec.Encode(graph)) && program.ResourceGeneration > 0);
        var instance = new AnimationGraphInstance(program, new(Guid.NewGuid(), Guid.NewGuid(), 0));
        var start = instance.ReadDebug(instance.Frame.Context);
        var pending = instance.Prepare(instance.Frame.Context, .01); instance.Abort(pending);
        Check(instance.Frame.Context.Tick == 0 && instance.ReadDebug(instance.Frame.Context).Instructions.SequenceEqual(start.Instructions));
        for (ulong tick = 1; tick <= 3; tick++) {
            var token = instance.Prepare(instance.Frame.Context, .01);
            instance.Commit(token, instance.Frame.Context with { Tick = tick });
            var debug = instance.ReadDebug(instance.Frame.Context);
            var clip = debug.Instructions.Single(i => i.Operation == AnimationPoseOperation.Clip);
            Check(debug.SnapshotValid && debug.Frame.GraphId == graph.AssetId && Math.Abs(clip.Current - .01 * tick * speed) < 1e-9);
            if (parameter != Guid.Empty) Check(instance.CommittedParameter(parameter) == speed);
        }
        return program;
    }
    private static void Unchanged(GraphInspectionTests.Fixture f, byte[] before, byte[] scene, int undo = 0)
        => Check(before.SequenceEqual(File.ReadAllBytes(f.File)) && scene.SequenceEqual(f.Owner.Document.CaptureBytes()) && f.Owner.Edit!.State.UndoCount == undo && f.Owner.Play is null);

    private static void JointAnimationCommit(string output, string repository, string? plugins = null)
    {
        using var f = new GraphInspectionTests.Fixture(output, repository, authoring: true);
        using var h = new WorkspaceHarness(f);
        byte[] before = File.ReadAllBytes(f.File), scene = f.Owner.Document.CaptureBytes();
        var original = AnimationGraphCodec.Decode(before); var operations = SpeedBinding(original, out Guid parameter);
        var candidate = AnimationGraphEdits.Apply(original, operations); h.SetOperations(operations);
        Reject(() => h.Ui.Prepare(AiAuthoringIntent.AnimationGraph, f.Graph, "Bind a speed parameter")); Check(h.Transport.Calls == 0);
        f.ApproveUi();
        using var assets = JointAssets(f); using var source = assets.AcquireLease();
        _ = CheckPlayback(source, original, 1);
        var send = h.Ui.Prepare(AiAuthoringIntent.AnimationGraph, f.Graph, "Bind a Float parameter with default 2 to the existing Clip speed input");
        Reject(() => h.Ui.Send(send, send.Observation.Fingerprint, false)); Check(h.Transport.Calls == 0);
        h.Ui.Send(send, send.Observation.Fingerprint, true);
        Until(() => { f.PumpModules(); return h.Ui.Status != "pending"; });
        Check(h.Ui.Status == "resource-review" && h.Ui.RefreshReview() is null && h.Domains.Receipts.Length == 0);
        Check(h.Transport.Calls == 1 && h.Transport.Sent!.SequenceEqual(send.WireRequest.CopyBytes())); Unchanged(f, before, scene);
        // An unapproved candidate cannot mutate a newly prepared or already published source graph.
        _ = CheckPlayback(source, original, 1);
        var resources = h.Ui.RefreshAnimationResources();
        using var json = JsonDocument.Parse(resources.CopyBytes());
        var resource = json.RootElement.GetProperty("review");
        Check(resource.GetProperty("Graph").GetGuid() == f.Graph && resource.GetProperty("BeforeHash").GetString() == JointHash(before));
        Check(resource.GetProperty("AfterHash").GetString() == JointHash(AnimationGraphCodec.Encode(candidate)));
        Check(resource.GetProperty("Dependencies").EnumerateArray().Select(v => v.GetGuid()).SequenceEqual(source.List().Select(a => a.Id)));
        Check(resource.GetProperty("ResourceHashes").EnumerateArray().Select(v => v.GetString()).SequenceEqual(source.List().Select(a => a.Kind + ":" + a.Generation + ":" + a.ContentHash)));
        Reject(() => h.Ui.ApproveAnimationResources(resources, resources.Fingerprint, f.Graph, false)); Unchanged(f, before, scene);
        var review = h.Ui.ApproveAnimationResources(resources, resources.Fingerprint, f.Graph, true);
        Check(review.Source == send.Observation.Source && review.ProposalId == send.Observation.RequestId);
        Reject(() => h.Ui.Confirm(review, review.Fingerprint, false)); Unchanged(f, before, scene); Check(h.Domains.Receipts.Length == 0);
        var result = h.Ui.Confirm(review, review.Fingerprint, true); byte[] after = File.ReadAllBytes(f.File);
        Check(result.Status == "ok" && result.Changed && !result.Replayed && result.RequestId == review.ProposalId && result.SessionId == review.Source.EditSessionId);
        Check(after.SequenceEqual(AnimationGraphCodec.Encode(candidate)) && f.Owner.Edit!.State.UndoCount == 1);
        var receipt = h.Domains.Receipts.Single();
        Check(receipt.ProposalId == review.ProposalId && receipt.Fingerprint == review.Fingerprint && receipt.Status == result.Status &&
            receipt.Code == result.Code && receipt.Changed == result.Changed && receipt.ExecutionRevision == result.ExecutionRevision &&
            receipt.ExecutionRevision > review.Source.Revision && !receipt.RecoveryRequired);
        Reject(() => h.Ui.Confirm(review, review.Fingerprint, true)); Check(h.Domains.Receipts.Length == 1 && after.SequenceEqual(File.ReadAllBytes(f.File)));
        f.Writer!.Synchronize(); var committed = AnimationGraphCodec.Decode(File.ReadAllBytes(f.File));
        var program = CheckPlayback(source, committed, 2, parameter);
        if (plugins is not null) {
            using var preview = source.CreateGraphPreview(committed);
            JointGpuPlayback(preview, committed, parameter, plugins, output);
        }
        f.Workspace.History(f.Workspace.Stamp, false); f.Writer.Synchronize(); Unchanged(f, before, scene);
        _ = CheckPlayback(source, AnimationGraphCodec.Decode(File.ReadAllBytes(f.File)), 1);
        f.Workspace.History(f.Workspace.Stamp, true); f.Writer.Synchronize(); Unchanged(f, after, scene, 1);
        _ = CheckPlayback(source, AnimationGraphCodec.Decode(File.ReadAllBytes(f.File)), 2, parameter);
        Console.WriteLine("M87-JOINT " + JsonSerializer.Serialize(new {
            scenario = plugins is null ? "animation-speed-cpu" : "animation-speed-dx11", requestId = send.Observation.RequestId,
            requestSource = send.Observation.Source, observationFingerprint = send.Observation.Fingerprint, wireSha256 = JointHash(send.WireRequest.CopyBytes()),
            resourceProposalId = resource.GetProperty("Proposal").GetGuid(), resourceFingerprint = resources.Fingerprint,
            resourceClosureSha256 = JointHash(JsonSerializer.SerializeToUtf8Bytes(resource.GetProperty("ResourceHashes"))),
            review.ProposalId, review.Fingerprint, diffSha256 = JointHash(review.CopyChanges()), receipt,
            beforeSha256 = JointHash(before), afterSha256 = JointHash(after), program.ContentHash, program.ResourceGeneration,
            deterministicTransport = true, manualAccepted = false, worldPlayAccepted = false
        }));
    }

    private static IEnumerable<(string, Action)> JointAnimationCases(string output, string repository)
    {
        yield return ("M8.7-C2 animation speed binding approvals receipt pinned playback and Undo Redo", () => JointAnimationCommit(output, repository));
        foreach (string mode in new[] { "cancel", "read-revoke", "external-file", "external-file-ui" }) {
            string reason = mode;
            yield return ($"M8.7-C2 animation speed binding {reason} keeps original playback", () => {
                using var f = new GraphInspectionTests.Fixture(output, repository, authoring: true); f.ApproveUi();
                using var h = new WorkspaceHarness(f); byte[] before = File.ReadAllBytes(f.File), scene = f.Owner.Document.CaptureBytes();
                var original = AnimationGraphCodec.Decode(before); h.SetOperations(SpeedBinding(original, out _));
                var send = h.Ui.Prepare(AiAuthoringIntent.AnimationGraph, f.Graph, "Bind a Float speed parameter");
                h.Ui.Send(send, send.Observation.Fingerprint, true); Until(() => { f.PumpModules(); return h.Ui.Status != "pending"; });
                var resources = h.Ui.RefreshAnimationResources(); var review = h.Ui.ApproveAnimationResources(resources, resources.Fingerprint, f.Graph, true);
                if (reason == "cancel") h.Ui.Cancel(); else if (reason == "read-revoke") f.Writer!.Reads.Revoke();
                else File.WriteAllBytes(f.File, AnimationGraphCodec.Encode(original with { Name = "Independent human change" }));
                byte[] protectedBytes = File.ReadAllBytes(f.File);
                var history = f.Owner.Edit!.State;
                CapabilityResult? result = null;
                if (reason is "external-file" or "external-file-ui") {
                    // Original EditSession translates a participant file conflict into a result.
                    // An attempted command has one failure receipt, unlike pre-confirmation rejection.
                    result = reason == "external-file-ui" ? h.Ui.Confirm(review, review.Fingerprint, true)
                        : h.Domains.Confirm(review, review.Fingerprint, true);
                    Check(result.Status == "conflict" && result.Code == "graph_file_conflict" && !result.Changed && !result.Replayed &&
                        result.RequestId == review.ProposalId && result.SessionId == review.Source.EditSessionId &&
                        result.Revision == history.Revision && result.ExecutionRevision == history.Revision);
                    var receipt = h.Domains.Receipts.Single();
                    Check(receipt == new DomainProposalReceipt(review.ProposalId, review.Fingerprint,
                        result.Status, result.Code, false, result.ExecutionRevision, false));
                    Check(!h.Domains.IsCurrent(review));
                    string? replayCode = null;
                    try { h.Domains.Confirm(review, review.Fingerprint, true); }
                    catch (EditRejectedException error) { replayCode = error.Code; }
                    Check(replayCode == "proposal_confirmation_required" && h.Domains.Receipts.Single() == receipt);
                }
                else {
                    Reject(() => h.Domains.Confirm(review, review.Fingerprint, true));
                    Check(h.Domains.Receipts.Length == 0);
                }
                Check(f.Owner.Edit.State == history); Unchanged(f, protectedBytes, scene);
                using var assets = JointAssets(f); using var lease = assets.AcquireLease();
                _ = CheckPlayback(lease, AnimationGraphCodec.Decode(protectedBytes), 1);
                h.Ui.Cancel(); Check(f.Writer!.Pending.Length == 0);
                Check(f.Owner.Edit.State == history); Unchanged(f, protectedBytes, scene);
                Console.WriteLine("M87-JOINT " + JsonSerializer.Serialize(new { scenario = "animation-speed-" + reason,
                    requestId = send.Observation.RequestId, review.ProposalId, review.Fingerprint,
                    beforeSha256 = JointHash(protectedBytes), afterSha256 = JointHash(File.ReadAllBytes(f.File)),
                    result, receipts = h.Domains.Receipts, receiptCount = h.Domains.Receipts.Length,
                    historyUnchanged = f.Owner.Edit.State == history, manualAccepted = false }));
            });
        }
        foreach (string mode in new[] { "port", "type", "cycle" }) {
            string reason = mode;
            yield return ($"M8.7-C2 animation {reason} rejected by original graph validator", () => {
                using var f = new GraphInspectionTests.Fixture(output, repository, authoring: true); f.ApproveUi();
                using var h = new Harness(f); byte[] before = File.ReadAllBytes(f.File), scene = f.Owner.Document.CaptureBytes();
                var original = AnimationGraphCodec.Decode(before);
                h.SetOperations(reason == "cycle" ? Cycle(original) : SpeedBinding(original, out _, reason));
                var send = h.Prepare(); h.Workflow.ApproveAndBegin(send, send.Observation.Fingerprint, true);
                string? code = null;
                try { h.Complete(); } catch (AnimationGraphValidationException error) { code = error.Code; }
                Check(code == (reason == "port" ? "output_pin" : reason == "type" ? "link_type" : "pose_cycle"));
                Check(h.Transport.Calls == 1 && h.Transport.Sent!.SequenceEqual(send.WireRequest.CopyBytes()) && h.Workflow.Review is null && f.Writer!.Pending.Length == 0 && h.Domains.Receipts.Length == 0);
                Unchanged(f, before, scene);
                Console.WriteLine("M87-JOINT " + JsonSerializer.Serialize(new { scenario = "animation-invalid-" + reason, requestId = send.Observation.RequestId,
                    code, beforeSha256 = JointHash(before), afterSha256 = JointHash(File.ReadAllBytes(f.File)), receiptCount = 0, manualAccepted = false }));
            });
        }
    }
}
