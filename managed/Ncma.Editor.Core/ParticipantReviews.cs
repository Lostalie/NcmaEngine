using System.Text.Json;

namespace Ncma.Editor.Core;

// Opt-in ONLY for participants whose Prepare performs checked reads, not durable publication.
// Domain-owned scopes and resource checks still run. No universal Prepare/IO introspection API.
public interface IEditCommandReview
{
    JsonElement DescribePrepared(ParticipantMemento prepared);
}

public sealed class ParticipantCommandReview
{
    internal readonly CapabilityRequest Request;
    internal readonly string MementoHash;
    internal readonly Guid Document, Catalog;
    internal readonly ulong Generation;
    private readonly byte[] details;
    internal ParticipantCommandReview(CapabilityRequest request, Guid document, Guid catalog, ulong generation,
        string hash, byte[] details)
    { Request = request with { Input = request.Input.Clone() }; Document = document; Catalog = catalog;
        Generation = generation; MementoHash = hash; this.details = (byte[])details.Clone(); }
    public byte[] CopyDetails() => (byte[])details.Clone();
}

public sealed partial class EditSession
{
    private string? expectedParticipantMemento;
    private static string MementoHash(ParticipantMemento m, byte[] details) => Hash(JsonSerializer.SerializeToUtf8Bytes(new {
        before = Hash(m.Before), after = Hash(m.After), details = Hash(details) }));
    private void RequireReviewedParticipant(IEditCommandParticipant owner, ParticipantMemento memento)
    {
        if(expectedParticipantMemento is null)return;
        using var read=_document.ReadOnly();
        if(owner is not IEditCommandReview reviewer)throw new EditRejectedException("participant_review_unsupported");
        byte[] details=JsonSerializer.SerializeToUtf8Bytes(reviewer.DescribePrepared(memento),OutputJson);
        if(details.Length>MaxInputBytes||MementoHash(memento,details)!=expectedParticipantMemento)
            throw new EditRejectedException("participant_review_changed");
    }
    public ParticipantCommandReview ReviewParticipantCommand(CapabilityRequest request)
    {
        Observe();
        if (_invoking || InteractionBusy || _frozen || _invalidated || request.ContractVersion != ContractVersion ||
            request.SessionId != SessionId || request.ExpectedRevision != Revision || request.RequestId == Guid.Empty)
            throw new EditRejectedException("participant_review_stale");
        CheckInput(request.Input);
        if (!_participants.TryGetValue(request.Capability, out var owner) || owner is not IEditCommandReview reviewer)
            throw new EditRejectedException("participant_review_unsupported");
        _invoking = true;
        try {
            using var read = _document.ReadOnly();
            var prepared = owner.Prepare(request.Input);
            if (prepared is null || prepared.Before is null || prepared.After is null ||
                (long)prepared.Before.Length + prepared.After.Length > MaxHistoryBytes)
                throw new ArgumentException("Participant review budget.");
            var owned = new ParticipantMemento((byte[])prepared.Before.Clone(), (byte[])prepared.After.Clone());
            owner.Authorize(owned); owner.Validate(owned, true);
            byte[] details = JsonSerializer.SerializeToUtf8Bytes(reviewer.DescribePrepared(owned), OutputJson);
            if (details.Length > MaxInputBytes) throw new EditRejectedException("participant_review_byte_budget");
            return new(request, _document.SessionId, BehaviourCatalogGeneration, DocumentGeneration, MementoHash(owned,details), details);
        }
        finally { _invoking = false; }
    }
    // Not a new command or permission path. Original Invoke/participant/history/compensation executes;
    // the prepared before/after must additionally equal the exact local human review.
    public CapabilityResult InvokeReviewedParticipant(ParticipantCommandReview review, CapabilityPermissions permissions)
    {
        ArgumentNullException.ThrowIfNull(review); Observe();
        if (_invoking || expectedParticipantMemento is not null || review.Document != _document.SessionId ||
            review.Catalog != BehaviourCatalogGeneration || review.Generation != DocumentGeneration)
            throw new EditRejectedException("participant_review_stale");
        expectedParticipantMemento = review.MementoHash;
        try { return Invoke(review.Request, permissions); }
        finally { expectedParticipantMemento = null; }
    }
}
