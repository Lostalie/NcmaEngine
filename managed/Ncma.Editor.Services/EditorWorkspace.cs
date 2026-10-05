using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Gameplay;
using Ncma.Scene;

namespace Ncma.Editor.Services;

public readonly record struct EditorViewStamp(Guid SessionId, ulong Generation, ulong Revision);
public sealed record EditorObjectRow(Guid Id, string Name, int Components, int Bindings);
public sealed record EditorPage(EditorViewStamp Stamp, EditState State, int Total, int Offset,
    EditorObjectRow[] Rows, SceneObjectData? Selected, PlayStatus? Play);

// Trusted human-UI routing only. No additional scene store or history; never registered as MCP tools.
public sealed class EditorWorkspace(EditorSessionOwner owner)
{
    private static readonly string[] LocalCapabilities = ["ncma.scene.transaction", "ncma.scene.delete_object", "ncma.history.undo", "ncma.history.redo",
        "ncma.assets.metadata.edit", "ncma.assets.files.edit", "ncma.assets.import.commit"];
    private static readonly CapabilityPermissions Local = new(LocalCapabilities);
    private SceneDocumentSnapshot? _cache, _preview;
    private ulong _cachedRevision = ulong.MaxValue, _cachedGeneration;
    private Guid? _draft, _draftObject;
    public EditorSessionOwner Owner { get; } = owner;
    public int SnapshotReads { get; private set; }
    private EditSession Edit => Owner.Edit ?? throw new InvalidOperationException("Editor required.");
    public EditorViewStamp Stamp => new(Edit.SessionId, Edit.DocumentGeneration, Edit.Revision);
    public bool HasDraft { get { _ = Edit.State; return _draft.HasValue; } }
    private void Check(EditorViewStamp stamp)
    {
        _ = Edit.State;
        if (stamp != Stamp) throw new EditRejectedException("stale_view");
    }
    private SceneDocumentSnapshot Snapshot()
    {
        var stamp = Stamp;
        if (_cache is null || _cachedRevision != stamp.Revision || _cachedGeneration != stamp.Generation)
        {
            _cache = Owner.Document.CaptureSnapshot(); SnapshotReads++;
            _cachedRevision = stamp.Revision; _cachedGeneration = stamp.Generation;
        }
        return _cache;
    }
    public EditorPage Capture(int offset = 0, int limit = 32)
    {
        if (offset < 0 || limit is < 1 or > 64) throw new ArgumentException("Invalid page.");
        var state = Edit.State;
        if (_draft.HasValue && (!state.EditBusy || state.HistoryInvalidated || state.Frozen)) CancelDraft();
        var snapshot = Snapshot();
        var selected = (_preview ?? snapshot).Objects.FirstOrDefault(o => o.Id == state.Selection);
        if (selected is not null)
            selected = selected with { Components = selected.Components.Select(c => c with { Data = c.Data.Clone() }).ToArray(),
                Behaviours = selected.Behaviours.Select(b => b with { Exports = (ExportData[])b.Exports.Clone() }).ToArray() };
        return new(Stamp, state, snapshot.Objects.Length, offset,
            snapshot.Objects.Skip(offset).Take(limit).Select(o => new EditorObjectRow(o.Id, o.Name, o.Components.Length, o.Behaviours.Length)).ToArray(),
            selected, Owner.Play?.Status);
    }
    private static CapabilityPermissions Permissions(Guid? target = null) => target is Guid id ? new(LocalCapabilities, [id]) : Local;
    private CapabilityResult Invoke(string capability, JsonElement input, EditorViewStamp stamp, Guid? target = null)
    {
        Check(stamp); CancelDraft();
        var result = Edit.Invoke(new(EditSession.ContractVersion, Guid.NewGuid(), Edit.SessionId, stamp.Revision, capability, input), Permissions(target));
        if (result.Status != "ok") throw new EditRejectedException(result.Code);
        return result;
    }
    public CapabilityResult Transaction(EditorViewStamp stamp, string label, object[] operations, Guid? selection = null) =>
        Invoke("ncma.scene.transaction", JsonSerializer.SerializeToElement(new { label, operations, selection }), stamp);
    public CapabilityResult AssetCommand(EditorViewStamp stamp,string capability,object input)
    {
        if(!LocalCapabilities.Contains(capability)||!capability.StartsWith("ncma.assets.",StringComparison.Ordinal))throw new ArgumentException("Unsupported local asset command.");
        return Invoke(capability,JsonSerializer.SerializeToElement(input),stamp);
    }
    public Guid CreateObject(EditorViewStamp stamp)
    {
        Guid id = Guid.NewGuid(); Transaction(stamp, "Create GameObject", [new { op = "create", objectId = id, name = "GameObject" }], id); return id;
    }
    public void Select(EditorViewStamp stamp, Guid? id) { Check(stamp); CancelDraft(); Edit.Select(id); }
    public void Delete(EditorViewStamp stamp, Guid id, Guid confirmedId)
    {
        if (id != confirmedId || id == Guid.Empty) throw new EditRejectedException("delete_confirmation_required");
        Invoke("ncma.scene.delete_object", JsonSerializer.SerializeToElement(new { objectId = id }), stamp, confirmedId);
    }
    public void History(EditorViewStamp stamp, bool redo) => Invoke(redo ? "ncma.history.redo" : "ncma.history.undo",
        JsonSerializer.SerializeToElement(new { }), stamp, Edit.HistoryTarget(redo));
    public void New(EditorViewStamp stamp, bool discardConfirmed)
    {
        Check(stamp); RequireReplacement(discardConfirmed); CancelDraft();
        var result = Edit.NewDocument(stamp.Revision, Local); if (result.Status != "ok") throw new EditRejectedException(result.Code);
    }
    public void Open(EditorViewStamp stamp, string path, bool discardConfirmed)
    {
        Check(stamp); RequireReplacement(discardConfirmed); CancelDraft();
        var result = Edit.OpenDocument(path, stamp.Revision, Local); if (result.Status != "ok") throw new EditRejectedException(result.Code);
    }
    private void RequireReplacement(bool confirmed)
    { if (Edit.State.Dirty && !confirmed) throw new EditRejectedException("unsaved_confirmation_required"); }
    public void Save(EditorViewStamp stamp, string? path) { Check(stamp); CancelDraft(); Edit.SaveDocument(path, Local); }
    public void BeginDraft(EditorViewStamp stamp, Guid objectId, string label)
    {
        Check(stamp); CancelDraft();
        _draft = Edit.BeginInteraction(objectId, label, stamp.Revision, Local); _draftObject = objectId;
    }
    public void UpdateDraft(EditorViewStamp stamp, Guid objectId, object operation)
    {
        Check(stamp);
        if (_draft is not Guid token || _draftObject != objectId) throw new EditRejectedException("draft_target_mismatch");
        _preview = Edit.UpdateInteraction(token, JsonSerializer.SerializeToElement(new { operations = new[] { operation } }), Local);
    }
    public void CommitDraft(EditorViewStamp stamp, Guid objectId)
    {
        Check(stamp);
        if (_draft is not Guid token || _draftObject != objectId) throw new EditRejectedException("draft_target_mismatch");
        _draft = null; _draftObject = null; _preview = null;
        var result = Edit.CommitInteraction(token, Local); if (result.Status != "ok") throw new EditRejectedException(result.Code);
    }
    public void CancelDraft()
    {
        var state = Edit.State; // Owner affinity applies even if there is no pending draft.
        if (_draft is Guid token && state.EditBusy) Edit.CancelInteraction(token);
        _draft = null; _draftObject = null; _preview = null;
    }
    public void PlayControl(EditorViewStamp stamp, string action)
    {
        Check(stamp); CancelDraft();
        switch (action)
        {
            case "start": Owner.StartPlay(); break;
            case "stop": Owner.StopPlay(); break;
            case "pause": (Owner.Play ?? throw new InvalidOperationException("No Play.")).Pause(); break;
            case "resume": (Owner.Play ?? throw new InvalidOperationException("No Play.")).Resume(); break;
            case "step": (Owner.Play ?? throw new InvalidOperationException("No Play.")).Step(); break;
            case "restart": Owner.StopPlay(); Owner.StartPlay(); break;
            default: throw new ArgumentException("Unknown Play action.");
        }
    }
    public void Reload(EditorViewStamp stamp, string path) { Check(stamp); CancelDraft(); Owner.ReloadGameplay(path); }
}
