using Ncma.Scene;
namespace Ncma.Editor.Core;

public sealed partial class EditSession
{
    // Trusted UI host API. File paths are deliberately absent from Agent capability schemas.
    public CapabilityResult NewDocument(ulong expectedRevision, CapabilityPermissions? permissions = null) =>
        ReplaceDocument(new(1, "Untitled", []), new(null, null), "New scene", expectedRevision, permissions);

    public CapabilityResult OpenDocument(string path, ulong expectedRevision, CapabilityPermissions? permissions = null)
    {
        Writable(permissions ?? CapabilityPermissions.ReadOnly, "ncma.scene.transaction");
        if (expectedRevision != Revision) throw new EditRejectedException("revision_conflict");
        string full = SceneDocumentFiles.ResolvePath(path);
        var snapshot = SceneDocumentCodec.Decode(SceneDocumentFiles.ReadBytes(full));
        // Content, not JSON whitespace, defines the saved baseline. Validator changes remain Dirty.
        var context = new FileContext(full, Hash(SceneDocumentCodec.Encode(snapshot)));
        return ReplaceDocument(snapshot, context, "Open scene", expectedRevision, permissions);
    }
    private CapabilityResult ReplaceDocument(SceneDocumentSnapshot snapshot, FileContext file, string label,
        ulong expectedRevision, CapabilityPermissions? permissions)
    {
        Writable(permissions ?? CapabilityPermissions.ReadOnly, "ncma.scene.transaction");
        if (expectedRevision != Revision) throw new EditRejectedException("revision_conflict");
        _invoking = true;
        try
        {
            var request = new CapabilityRequest(ContractVersion, Guid.NewGuid(), SessionId, expectedRevision,
                "ncma.scene.transaction", Json("{}"));
            Guid? selected = snapshot.Objects.Length == 0 ? null : snapshot.Objects[0].Id;
            return Commit(request, request.RequestId.ToString(), snapshot, _document.CaptureBytes(), label,
                request.Capability, null, selected, file);
        }
        finally { _invoking = false; }
    }
    public void SaveDocument(string? path = null, CapabilityPermissions? permissions = null)
    {
        Writable(permissions ?? CapabilityPermissions.ReadOnly, "ncma.scene.transaction");
        string full = SceneDocumentFiles.ResolvePath(path ?? _file.Path ?? throw new ArgumentException("Choose a scene file first."));
        // Prepare all state before IO. Failed atomic file saves leave history and association untouched.
        string hash = Hash(_document.CaptureBytes());
        var context = new FileContext(full, hash);
        _invoking = true;
        try
        {
            SceneDocumentFiles.Save(_document, full);
            _file = context;
            _currentHash = hash;
        }
        finally { _invoking = false; }
    }
}
