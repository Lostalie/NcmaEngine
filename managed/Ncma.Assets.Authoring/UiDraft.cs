using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Ui;

namespace Ncma.Assets.Authoring;

// Ephemeral interaction only, NOT a second command history. Each Update replaces the candidate
// relative to the frozen starting document; cancellation never touches disk or World.
public sealed class UiDraft : IDisposable
{
    private readonly EditSession _edit; private readonly UiCommands _commands; private readonly AssetRevisionClock _clock;
    private readonly string _path; private readonly UiDefinition _before; private UiDefinition _candidate;
    private readonly ulong _revision, _assetRevision, _generation; private readonly Guid _session;
    private bool _ended;
    private readonly EditInteractionLease _interaction;
    public UiDraft(EditSession edit, UiCommands commands, AssetRevisionClock clock, string path)
    {
        _edit = edit ?? throw new ArgumentNullException(nameof(edit)); _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock)); _path = path;
        if (edit.State.Frozen || edit.State.EditBusy || edit.State.HistoryInvalidated) throw new InvalidOperationException("UI draft requires an idle edit session.");
        _revision = edit.Revision; _assetRevision = clock.Revision; _generation = edit.DocumentGeneration; _session = edit.SessionId;
        _before = commands.Read(path); _candidate = UiCodec.Copy(_before);
        _interaction = edit.BeginHostInteraction(UiCommands.CapabilityName);
    }
    public UiDefinition Capture() { Check(); return UiCodec.Copy(_candidate); }
    public void Update(IReadOnlyList<UiEdit> edits) { Check(); _candidate = UiEdits.Apply(_before, edits); }
    public void Cancel() { _edit.Document.VerifyAccess(); _interaction.Dispose(); _ended = true; }
    public void Dispose() => Cancel();
    public CapabilityResult? Commit(CapabilityPermissions permissions)
    {
        Guid proposal = Guid.Empty;
        try {
            Check(); ArgumentNullException.ThrowIfNull(permissions);
            byte[] before = UiCodec.Encode(_before), after = UiCodec.Encode(_candidate);
            if (before.AsSpan().SequenceEqual(after)) return null;
            // Recheck the exact file at completion, not every presentation frame.
            if (!SHA256.HashData(UiCodec.Encode(_commands.Read(_path))).AsSpan().SequenceEqual(SHA256.HashData(before))) throw new EditCommandRejectedException("ui_document_conflict");
            proposal = _commands.PrepareProposal(_path, _candidate, _assetRevision);
            return _interaction.Commit(new(2, Guid.NewGuid(), _session, _revision, UiCommands.CapabilityName,
                JsonSerializer.SerializeToElement(new { proposalId = proposal, expectedAssetRevision = _assetRevision })), permissions);
        } finally { if (proposal != Guid.Empty) _commands.CancelProposal(proposal); Cancel(); }
    }
    private void Check()
    {
        _edit.Document.VerifyAccess();
        if (_ended || _edit.SessionId != _session || _edit.DocumentGeneration != _generation || _edit.Revision != _revision ||
            _clock.Revision != _assetRevision || !_interaction.IsActive)
            throw new EditCommandRejectedException("ui_draft_stale");
    }
}
