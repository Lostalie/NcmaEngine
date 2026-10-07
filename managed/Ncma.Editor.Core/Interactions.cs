using System.Text.Json;
using Ncma.Scene;
namespace Ncma.Editor.Core;

public sealed class EditRejectedException(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
}
public sealed partial class EditSession
{
    private void Writable(CapabilityPermissions permissions, string capability)
    {
        Observe();
        if (_invoking || InteractionBusy) throw new EditRejectedException("edit_busy");
        if (_frozen) throw new EditRejectedException("play_frozen");
        if (_invalidated) throw new EditRejectedException("history_invalidated");
        if (!permissions.Allows(capability)) throw new EditRejectedException("permission_denied");
        _ = _document.CaptureSnapshot(); // Owner and safe boundary check.
    }
    public Guid BeginInteraction(Guid objectId, string label, ulong expectedRevision, CapabilityPermissions? permissions = null)
    {
        permissions ??= CapabilityPermissions.ReadOnly;
        Writable(permissions, "ncma.scene.transaction");
        if (expectedRevision != Revision) throw new EditRejectedException("revision_conflict");
        _ = _document.World.FindObject(objectId);
        Ncma.Runtime.World.ValidateName(label);
        Guid token = Guid.NewGuid();
        _draft = new(token, Revision, objectId, label, Json("""{"operations":[]}"""));
        return token;
    }
    public SceneDocumentSnapshot UpdateInteraction(Guid token, JsonElement input, CapabilityPermissions? permissions = null)
    {
        _document.VerifyAccess();
        permissions ??= CapabilityPermissions.ReadOnly;
        var draft = _draft;
        if (_invoking) throw new EditRejectedException("edit_busy");
        if (draft is null || draft.Token != token) throw new EditRejectedException("session_mismatch");
        if (_frozen || !permissions.Allows("ncma.scene.transaction")) throw new EditRejectedException("permission_denied");
        if (draft.Revision != Revision) throw new EditRejectedException("revision_conflict");
        CheckInput(input); Closed(input, ["operations"], ["operations"]);
        var operations = input.GetProperty("operations");
        if (operations.ValueKind != JsonValueKind.Array || operations.GetArrayLength() is < 1 or > MaxOperations)
            throw new ArgumentException("Requires 1..128 operations.");
        var candidate = _document.CaptureSnapshot();
        foreach (var op in operations.EnumerateArray())
        {
            if (Uuid(op, "objectId") != draft.Selection) throw new ArgumentException("Interaction target changed.");
            candidate = Apply(candidate, op);
        }
        _invoking = true;
        try
        {
            SceneDocumentSnapshot? normalized = null;
            _ = _document.PrepareRestore(candidate, draft.Revision, value => normalized = value);
            JsonElement owned = input.Clone();
            _draft = draft with { Input = owned };
            return SceneDocumentCodec.Copy(normalized!); // Owned non-authoritative preview.
        }
        finally { _invoking = false; }
    }
    public CapabilityResult CommitInteraction(Guid token, CapabilityPermissions? permissions = null)
    {
        _document.VerifyAccess();
        if (_invoking) throw new EditRejectedException("edit_busy");
        var draft = _draft;
        if (draft is null || draft.Token != token) throw new EditRejectedException("session_mismatch");
        _draft = null; // End on success, conflict or rejection; the authoritative document is untouched until commit.
        var input = JsonSerializer.SerializeToElement(new { operations = draft.Input.GetProperty("operations"), label = draft.Label, selection = draft.Selection }, OutputJson);
        if (draft.Input.GetProperty("operations").GetArrayLength() == 0)
        {
            var empty = new CapabilityRequest(ContractVersion, Guid.NewGuid(), SessionId, draft.Revision, "ncma.scene.transaction", input);
            Observe();
            if (!(permissions ?? CapabilityPermissions.ReadOnly).Allows("ncma.scene.transaction"))
                return Result(empty, "denied", "permission_denied", false, new { });
            if (_frozen) return Result(empty, "denied", "play_frozen", false, new { });
            if (_invalidated) return Result(empty, "conflict", "history_invalidated", false, new { });
            if (draft.Revision != Revision) return Result(empty, "conflict", "revision_conflict", false, new { });
            _ = _document.CaptureSnapshot();
            return Result(empty, "ok", "no_change", false, new { history = State });
        }
        return Invoke(new(ContractVersion, Guid.NewGuid(), SessionId, draft.Revision, "ncma.scene.transaction", input), permissions);
    }
    public void CancelInteraction(Guid token)
    {
        _document.VerifyAccess();
        if (_invoking) throw new EditRejectedException("edit_busy");
        if (_draft is null || _draft.Token != token) throw new EditRejectedException("session_mismatch");
        _draft = null;
    }
}
