using System.Text.Json;
using Ncma.Scene;

namespace Ncma.Editor.Core;

public sealed record ParticipantMemento(byte[] Before, byte[] After);

// Optional trusted pure proposal validator. MUST NOT read/write files or authorize a grant.
// Enables endpoint review without invoking the participant's durable Prepare operation.
public interface IEditCommandProposal { void ValidateProposal(JsonElement input); }

// Optional trusted, pure projection of a durable command onto the ORIGINAL scene operations.
// No file IO, grants or live World writes here. Core validates these operations/impact before
// Prepare and commits the scene plus compensatable participant as one history entry.
public interface IEditCommandSceneParticipant : IEditCommandParticipant
{
    JsonElement SceneOperations(JsonElement input);
}

// Trusted composition only. Implementations own their scope/revision validation and durable journal.
// Core owns the sole history, no filesystem or asset implementation. Publish must be compensatable;
// an unsuccessful compensation freezes the session for explicit recovery, never claims rollback.
public interface IEditCommandParticipant
{
    ParticipantMemento Prepare(JsonElement input);
    void Authorize(ParticipantMemento memento);
    void Validate(ParticipantMemento memento, bool forward);
    void Publish(ParticipantMemento memento, bool forward);
    void Compensate(ParticipantMemento memento, bool forward);
    // Called only after the World installation and final authorization have succeeded.
    // A durable participant retains its journal/leases until this decision.
    void Complete(ParticipantMemento memento, bool forward) { }
}

public sealed partial class EditSession
{
    private readonly Dictionary<string, IEditCommandParticipant> _participants = new(StringComparer.Ordinal);
    private sealed record ParticipantEntry(IEditCommandParticipant Owner, ParticipantMemento Memento)
    {
        public int Bytes => checked(Memento.Before.Length + Memento.After.Length);
    }

    public void RegisterCommandParticipant(CapabilityDescriptor descriptor, IEditCommandParticipant participant,
        (CapabilityDescriptor Descriptor, Func<JsonElement,object> Inspect)? inspection = null)
    {
        _document.VerifyAccess(); ArgumentNullException.ThrowIfNull(descriptor); ArgumentNullException.ThrowIfNull(participant);
        if (_invoking || _frozen || InteractionBusy || _history.Length != 0 || _participants.Count >= 16)
            throw new InvalidOperationException("Participant registration requires idle trusted startup.");
        if (descriptor.Risk != MutationRisk.Reversible || string.IsNullOrWhiteSpace(descriptor.Name) || descriptor.Name.Length > 128 ||
            string.IsNullOrWhiteSpace(descriptor.Description) || descriptor.Description.Length > 256 ||
            descriptor.InputSchema.ValueKind != JsonValueKind.Object || descriptor.OutputSchema.ValueKind != JsonValueKind.Object ||
            descriptor.InputSchema.GetRawText().Length > MaxInputBytes || descriptor.OutputSchema.GetRawText().Length > MaxInputBytes ||
            _capabilities.ContainsKey(descriptor.Name)) throw new ArgumentException("Invalid reversible participant descriptor.");
        if (inspection is { } read) {
            ArgumentNullException.ThrowIfNull(read.Descriptor); ArgumentNullException.ThrowIfNull(read.Inspect);
            if (_inspections.Count >= MaxRegisteredInspections || read.Descriptor.Name == descriptor.Name) throw new ArgumentException("Participant inspection capacity/name.");
            ValidateInspection(read.Descriptor);
        }
        _capabilities.Add(descriptor.Name, descriptor with { InputSchema = descriptor.InputSchema.Clone(), OutputSchema = descriptor.OutputSchema.Clone() });
        _participants.Add(descriptor.Name, participant);
        if (inspection is { } extra) RegisterInspection(extra.Descriptor, extra.Inspect);
    }

    private CapabilityResult ParticipantTransaction(CapabilityRequest request, CapabilityPermissions permissions, string fingerprint)
    {
        var owner = _participants[request.Capability];
        byte[] before = _document.CaptureBytes();
        var candidate = SceneDocumentCodec.Decode(before);
        CapabilityRequest? impactRequest = null;
        if (owner is IEditCommandSceneParticipant sceneOwner) {
            impactRequest = ParticipantSceneRequest(request, sceneOwner);
            foreach (var op in impactRequest.Input.GetProperty("operations").EnumerateArray()) {
                AuthorizeIntent(permissions, op); candidate = Apply(candidate, op);
                if (permissions.RequireTrustedBindings && Text(op,"op") == "set_bindings")
                    foreach (var binding in candidate.Objects.Single(o => o.Id == Uuid(op,"objectId")).Behaviours) RequireBinding(binding);
            }
            // Validate the complete proposed World before even preparing durable side effects.
            _ = _document.PrepareRestore(candidate, request.ExpectedRevision);
        }
        ParticipantMemento prepared;
        using (_document.ReadOnly()) prepared = owner.Prepare(request.Input);
        if (prepared is null || prepared.Before is null || prepared.After is null ||
            (long)prepared.Before.Length + prepared.After.Length > MaxHistoryBytes)
            throw new ArgumentException("Participant memento exceeds history budget.");
        var memento = new ParticipantMemento((byte[])prepared.Before.Clone(), (byte[])prepared.After.Clone());
        RequireReviewedParticipant(owner,memento);
        using (_document.ReadOnly()) owner.Authorize(memento);
        return Commit(request, fingerprint, candidate, before, _capabilities[request.Capability].Description,
            request.Capability, null, _selection, permissions: permissions, participant: new(owner, memento), impactRequest: impactRequest);
    }

    private CapabilityRequest ParticipantSceneRequest(CapabilityRequest request, IEditCommandSceneParticipant owner)
    {
        JsonElement operations;
        using (_document.ReadOnly()) operations = owner.SceneOperations(request.Input).Clone();
        if (operations.ValueKind != JsonValueKind.Array || operations.GetArrayLength() is < 1 or > MaxOperations)
            throw new ArgumentException("Participant requires bounded original scene operations.");
        var input = JsonSerializer.SerializeToElement(new { operations }); CheckInput(input);
        return request with { Capability = "ncma.scene.transaction", Input = input };
    }

    private void InstallWithParticipant(ParticipantEntry? entry, bool forward, Action install, Func<bool> authorized)
    {
        if (entry is null) { install(); return; }
        bool attempted = false, installed = false;
        try
        {
            using (_document.ReadOnly())
            {
                entry.Owner.Validate(entry.Memento, forward);
                if (!authorized()) throw new EditRejectedException("scope_denied");
                RequireReviewedParticipant(entry.Owner,entry.Memento);
                attempted = true;
                entry.Owner.Publish(entry.Memento, forward);
                entry.Owner.Authorize(entry.Memento);
            }
            if (!authorized()) throw new EditRejectedException("scope_denied");
            RequireReviewedParticipant(entry.Owner,entry.Memento);
            install();
            installed = true;
            using (_document.ReadOnly()) entry.Owner.Complete(entry.Memento, forward);
        }
        catch
        {
            // A finalization failure is not an ordinary pre-install rejection. Never pretend the
            // already installed document/history revision was rolled back by a filesystem participant.
            if (installed) { _invalidated = true; _frozen = true; }
            if (attempted)
            {
                try { using (_document.ReadOnly()) entry.Owner.Compensate(entry.Memento, forward); }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    _invalidated = true; _frozen = true;
                    throw new InvalidOperationException("Participant recovery failed; session frozen for explicit recovery.", error);
                }
            }
            throw;
        }
    }
}
