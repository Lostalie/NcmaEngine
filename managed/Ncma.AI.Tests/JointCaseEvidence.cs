using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Editor.Modules;
using Ncma.Editor.Services;

// Test output only. Original receipts remain authoritative; this is not a product
// ledger, approval or real-service/human acceptance. Never output raw proposals.
internal static class JointCaseEvidence
{
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static void Committed(string scenario, AiDomainRequestReview request, DomainProposalReview review,
        CapabilityResult result, EditorDomainProposals domains, byte[] before, byte[] after)
    {
        var receipts = domains.Receipts;
        if (receipts.Length != 1 || result.Status != "ok" || !result.Changed || result.Replayed ||
            review.Source != request.Observation.Source || before.AsSpan().SequenceEqual(after))
            throw new Exception("Joint case commit/source boundary");
        var receipt = receipts[0];
        if (receipt.ProposalId != review.ProposalId || receipt.Fingerprint != review.Fingerprint ||
            receipt.Status != result.Status || receipt.Code != result.Code || receipt.Changed != result.Changed ||
            receipt.ExecutionRevision != result.ExecutionRevision || receipt.ExecutionRevision <= review.Source.Revision || receipt.RecoveryRequired)
            throw new Exception("Joint case original receipt mismatch");
        Emit(scenario, "committed", request, review, receipt, before, after);
    }
    internal static void Rejected(string scenario, AiDomainRequestReview request, DomainProposalReview review,
        EditorDomainProposals domains, byte[] before, byte[] after)
    {
        if (domains.Receipts.Length != 0 || !before.AsSpan().SequenceEqual(after))
            throw new Exception("Precommit rejection changed bytes or invented a receipt");
        Emit(scenario, "rejected-before-commit", request, review, null, before, after);
    }
    private static void Emit(string scenario, string outcome, AiDomainRequestReview request, DomainProposalReview review,
        DomainProposalReceipt? receipt, byte[] before, byte[] after)
        => Console.WriteLine("M87-JOINT " + JsonSerializer.Serialize(new {
            scenario, outcome, requestId = request.Observation.RequestId, requestSource = request.Observation.Source,
            observationFingerprint = request.Observation.Fingerprint, wireSha256 = Hash(request.WireRequest.CopyBytes()),
            review.ProposalId, review.Source, review.Fingerprint, review.PayloadSha256,
            diffSha256 = Hash(review.CopyChanges()), beforeSha256 = Hash(before), afterSha256 = Hash(after),
            receipt, deterministicTransport = true, realServiceAccepted = false, humanAccepted = false
        }));
}
