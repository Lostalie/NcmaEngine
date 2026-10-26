using System.Text.Json;
using Ncma.AI;
using Ncma.Animation;
using Ncma.Editor.Core;
using Ncma.Editor.Services;

namespace Ncma.Editor.Modules;

// Owner-thread staging only. Read approval, resource preparation/approval and final domain
// confirmation remain separate. Never called on the HTTP thread and never writes graph files.
internal sealed class AiAnimationDraftStage(EditorDomainProposals domains, EditorAnimationGraphWorkspace workspace)
{
    private AnimationGraphEditorStamp expected;
    private ulong readPermission;
    private bool bound;
    private Guid? proposal;
    private ApprovedDomainRequest? request;
    internal DomainProposalReview? Review { get; private set; }
    internal string? Status { get; private set; }

    internal void Bind(DomainProposalSource source, IReadOnlyList<DomainReadRequest> reads)
    {
        if (bound) throw new EditRejectedException("animation_stage_busy");
        expected = workspace.RequireDomainSource(source); readPermission = workspace.Reads.PermissionRevision;
        if (reads.Count is < 1 or > EditorDomainProposals.MaximumObservationReads) throw new ArgumentException("animation_read_budget");
        for (int i = 0; i < reads.Count; i++) {
            var read = reads[i];
            if (read.Capability is not ("ncma.animgraph.inspect" or "ncma.animgraph.validate" or "ncma.animgraph.bones") ||
                read.Input.ValueKind != JsonValueKind.Object || !read.Input.TryGetProperty("graphId", out var id) ||
                !id.TryGetGuid(out var graph) || graph != expected.Graph)
                throw new EditRejectedException("animation_exact_graph_observation_required");
        }
        bound = true;
    }
    internal bool IsCurrent()
    {
        try { return bound && workspace.Stamp == expected && !workspace.HasDraft && workspace.Reads.PermissionRevision == readPermission; }
        catch (Exception e) when (e is InvalidOperationException or EditRejectedException) { return false; }
    }
    internal bool IdentityCurrent()
    {
        try {
            var current = workspace.Stamp;
            return bound && (current with { Assets = expected.Assets }) == expected && !workspace.HasDraft &&
                workspace.Reads.PermissionRevision == readPermission;
        }
        catch (Exception e) when (e is InvalidOperationException or EditRejectedException) { return false; }
    }
    internal DomainProposalReview? Stage(ApprovedDomainRequest approved, byte[] bytes)
    {
        if (!IsCurrent() || request is not null || !domains.RequestCurrent(approved))
            throw new EditRejectedException("animation_draft_stale");
        var draft = AiAnimationDraft.Decode(bytes);
        if (draft.GraphId != expected.Graph) throw new EditRejectedException("animation_draft_graph_mismatch");
        request = approved;
        try {
            proposal = workspace.PrepareDomainProposal(expected, draft.CopyOperations());
            RequireCurrent(); Status = "resource-review"; return null;
        }
        catch { Cancel(); throw; }
    }
    private void RequireCurrent()
    {
        if (!IsCurrent() || request is null || proposal is null || !domains.RequestCurrent(request))
            throw new EditRejectedException("animation_draft_stale");
    }
    internal bool PendingCurrent() => Status != "resource-review" ||
        IsCurrent() && request is not null && domains.RequestIdentityCurrent(request);
    internal AnimationGraphDefinition CaptureCandidate() { RequireCurrent(); return workspace.CandidateCopy(proposal!.Value); }
    internal AnimationGraphWriteReview CaptureResources()
    {
        RequireCurrent();
        var result = workspace.CaptureReview(proposal!.Value); // explicit human action only, never Pump
        RequireCurrent(); return result;
    }
    internal DomainProposalReview ApproveResources(AnimationGraphWriteReview displayed, string fingerprint, Guid exactGraph, bool humanReviewed)
    {
        RequireCurrent();
        if (displayed.Proposal != proposal || displayed.Graph != expected.Graph)
            throw new EditRejectedException("animation_resource_review_foreign");
        // Original approval checks complete resource hashes, read epoch, exact graph and human flag.
        workspace.Approve(displayed, fingerprint, exactGraph, humanReviewed);
        try {
            RequireCurrent();
            byte[] envelope = JsonSerializer.SerializeToUtf8Bytes(new { version = 1, capability = "ncma.animgraph.transaction",
                input = new { proposalId = proposal!.Value, expectedAssetRevision = expected.Assets } });
            Review = domains.ReceiveModuleResult(request!, envelope); Status = "review"; return Review;
        }
        catch { Cancel(); throw; } // consumed result is never silently prepared/retried
    }
    internal void Cancel()
    {
        bound = false; Status = null;
        if (request is not null) {
            try { domains.CancelRequest(request); }
            catch (Exception e) when (e is EditRejectedException or ObjectDisposedException) { }
        }
        if (Review is not null) {
            try { domains.Discard(Review); }
            catch (Exception e) when (e is EditRejectedException or ObjectDisposedException) { }
        }
        if (proposal is Guid id) {
            try { workspace.CancelProposal(id); }
            catch (Exception e) when (e is EditRejectedException or ObjectDisposedException) { }
        }
        request = null; proposal = null; Review = null;
    }
}
