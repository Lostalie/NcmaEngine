using System.Text.Json;
using Ncma.Editor.Core;

namespace Ncma.Editor.Services;

public sealed record DomainProposalReceipt(Guid ProposalId, string Fingerprint, string Status, string Code,
    bool Changed, ulong ExecutionRevision, bool RecoveryRequired = false);

public sealed partial class EditorDomainProposals
{
    private readonly HashSet<Guid> consumed = [];
    private readonly Queue<DomainProposalReceipt> receipts = [];
    private DomainProposalReview? latest;
    public DomainProposalReview? LatestReview {
        get { Check(); return latest is not null && IsCurrent(latest) ? latest : null; }
    }
    public DomainProposalReceipt[] Receipts { get { Check(); return receipts.ToArray(); } }
    public void Discard(DomainProposalReview review)
    {
        Check(); ArgumentNullException.ThrowIfNull(review);
        if (!ReferenceEquals(review.Owner, this)) throw new EditRejectedException("proposal_foreign_review");
        consumed.Add(review.ProposalId);
    }
    // Trusted local human UI only. Never registered as an Agent approval capability.
    // Original destructive capability, exact target scope and trusted-script checks remain active.
    public CapabilityResult Confirm(DomainProposalReview review, string fingerprint, bool humanReviewed,
        Guid? confirmedDelete = null, bool bindingsReviewed = false)
    {
        Check(); ArgumentNullException.ThrowIfNull(review);
        if (!humanReviewed || fingerprint != review.Fingerprint || !IsCurrent(review))
            throw new EditRejectedException("proposal_confirmation_required");
        var scope = review.CopyImpact();
        if (confirmedDelete != review.DeleteTarget || scope.Bindings.Length != 0 && !bindingsReviewed)
            throw new EditRejectedException("proposal_separate_confirmation_required");
        if (!consumed.Add(review.ProposalId)) throw new EditRejectedException("proposal_consumed");
        busy = true;
        try {
            // Recreate through ORIGINAL commands immediately before commit; validators/resource
            // callbacks cannot use this service or owner lifecycle reentrantly.
            if (review.Batch is not null && !edit.DescribeOriginalBatchChanges(review.Batch).AsSpan().SequenceEqual(review.CopyChanges()) ||
                Source() != review.Source || review.RequestStillCurrent?.Invoke(true) == false) throw new EditRejectedException("proposal_source_stale");
            var permissions = new CapabilityPermissions(
                review.Participant is not null ? [review.Capability] :
                    review.DeleteTarget is null ? ["ncma.scene.transaction"] : ["ncma.scene.transaction", "ncma.scene.delete_object"],
                review.DeleteTarget is Guid id ? [id] : [], scope.Objects, scope.CreatedObjects, scope.ComponentTypes, scope.Bindings,
                allowDocumentHistory: false, requireTrustedBindings: true,
                isCurrent: () => !closed && Fresh(review.Started) && Source() == review.Source &&
                    review.RequestStillCurrent?.Invoke(false) != false);
            var result = review.Participant is not null ? edit.InvokeReviewedParticipant(review.Participant, permissions) :
                edit.ApplyOriginalBatch(review.Batch!, review.ProposalId, permissions);
            Record(new(review.ProposalId, review.Fingerprint, result.Status, result.Code, result.Changed, result.ExecutionRevision,
                edit.State.HistoryInvalidated || edit.State.Frozen));
            return result;
        }
        catch {
            Record(new(review.ProposalId, review.Fingerprint, "error", "proposal_commit_failed", false, edit.Revision,
                edit.State.HistoryInvalidated || edit.State.Frozen));
            throw;
        }
        finally { busy = false; }
    }
    private void Record(DomainProposalReceipt receipt)
    {
        receipts.Enqueue(receipt); if (receipts.Count > 64) receipts.Dequeue();
    }
}
