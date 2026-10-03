using System.Text;
using System.Text.Json;
using Ncma;
using Ncma.Editor.Core;
namespace Ncma.ManagedHost;

public static unsafe partial class NativeEntry
{
    private static readonly JsonSerializerOptions EditorJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IncludeFields = true };
    private static CapabilityPermissions UserPermissions(Guid? approved = null) =>
        new(["ncma.scene.transaction", "ncma.scene.delete_object", "ncma.history.undo", "ncma.history.redo"], approved is Guid id ? [id] : []);
    private static JsonElement Intent(SceneSession scene, uint kind, Guid id, BinaryReader reader, string label)
    {
        object[] operations;
        switch (kind)
        {
            case 0:
                string name = ReadText(reader); bool spatial = ReadFlag(reader);
                var create = new { op = "create", objectId = id, name };
                operations = spatial ? [create, Component(scene, id, Transform.Identity)] : [create]; break;
            case 1: operations = [new { op = "rename", objectId = id, name = ReadText(reader) }]; break;
            case 2: operations = [Component(scene, id, ReadTransform(reader))]; break;
            case 3: operations = [new { op = "set_bindings", objectId = id, bindings = ReadBindings(reader).Select(b => new Ncma.Scene.BehaviourBindingData(b.Id, b.TypeName, b.Enabled,
                b.Properties.Select(p => new Ncma.Scene.ExportData(p.Name, (Ncma.Scene.ExportKind)p.Kind, p.Value)).ToArray())).ToArray() }]; break;
            case 4: operations = [new { op = "remove_component", objectId = id, typeId = ReadText(reader) }]; break;
            default: throw new ArgumentException("Unsupported editor intent.");
        }
        return JsonSerializer.SerializeToElement(new { operations, label, selection = kind == 0 ? (Guid?)id : scene.Editor!.State.Selection }, EditorJson);
    }
    private static object Component(SceneSession scene, Guid id, Transform value) => new {
        op = "set_component", objectId = id, typeId = "ncma.transform", version = 1,
        data = scene.Document.World.Components.Encode(SceneWorld.ToData(value)) };
    private static void Accepted(SceneSession scene, CapabilityResult result)
    {
        if (result.Status != "ok") throw new InvalidOperationException(result.Code +
            (result.Data.TryGetProperty("message", out var text) ? ": " + text.GetString() : ""));
        if (result.Changed && !result.Replayed) scene.World.Restored();
    }
    private static void WriteEditorState(BinaryWriter writer, EditState state)
    {
        WriteUuid(writer, state.SessionId); writer.Write(state.Revision);
        writer.Write(state.UndoCount); writer.Write(state.RedoCount);
        WriteText(writer, state.UndoLabel); WriteText(writer, state.RedoLabel);
        foreach (bool flag in new[] { state.Dirty, state.HistoryInvalidated, state.EditBusy, state.Frozen, state.Selection.HasValue }) writer.Write((byte)(flag ? 1 : 0));
        if (state.Selection is Guid selected) WriteUuid(writer, selected);
        WriteText(writer, state.FilePath ?? "");
    }
    // These typed UI entry points are trusted native-host calls, never exposed as Agent permissions.
    private static void EditorCall(SceneSession scene, int operation, BinaryReader reader, BinaryWriter writer, Action end)
    {
        if (operation == 30)
        {
            end(); if (scene.Editor is not null) throw new InvalidOperationException("One edit session per document.");
            if (s_world == scene.World) throw new InvalidOperationException("End gameplay before activating the editor.");
            scene.Editor = new(scene.Document); return;
        }
        var edit = scene.Editor ?? throw new InvalidOperationException("Activate an editor session first.");
        switch (operation)
        {
            case 31:
                string json = ReadText(reader); end();
                WriteText(writer, EditSession.EncodeResult(edit.InvokeJson(Encoding.UTF8.GetBytes(json)))); break; // Always read-only.
            case 32:
                uint kind = reader.ReadUInt32(); Guid id = ReadUuid(reader); ulong revision = reader.ReadUInt64(); string label = ReadText(reader);
                JsonElement intent = Intent(scene, kind, id, reader, label); end();
                Accepted(scene, edit.Invoke(new(EditSession.ContractVersion, Guid.NewGuid(), edit.SessionId, revision, "ncma.scene.transaction", intent), UserPermissions()));
                WriteEditorState(writer, edit.State); break;
            case 33:
                Guid? selected = ReadFlag(reader) ? ReadUuid(reader) : null; end(); edit.Select(selected); break;
            case 34: end(); WriteEditorState(writer, edit.State); break;
            case 35:
                bool redo = ReadFlag(reader); end();
                Accepted(scene, edit.Invoke(new(EditSession.ContractVersion, Guid.NewGuid(), edit.SessionId, edit.Revision,
                    redo ? "ncma.history.redo" : "ncma.history.undo", JsonSerializer.SerializeToElement(new { })), UserPermissions(edit.HistoryTarget(redo))));
                WriteEditorState(writer, edit.State); break;
            case 36:
                Guid target = ReadUuid(reader); ulong baseline = reader.ReadUInt64(); string gesture = ReadText(reader); end();
                WriteUuid(writer, edit.BeginInteraction(target, gesture, baseline, UserPermissions())); break;
            case 37:
                Guid draft = ReadUuid(reader); uint draftKind = reader.ReadUInt32(); Guid objectId = ReadUuid(reader);
                JsonElement all = Intent(scene, draftKind, objectId, reader, "Preview"); end();
                var preview = JsonSerializer.SerializeToElement(new { operations = all.GetProperty("operations") }, EditorJson);
                _ = edit.UpdateInteraction(draft, preview, UserPermissions()); break;
            case 38:
                Guid committed = ReadUuid(reader); end(); Accepted(scene, edit.CommitInteraction(committed, UserPermissions()));
                WriteEditorState(writer, edit.State); break;
            case 39: Guid cancelled = ReadUuid(reader); end(); edit.CancelInteraction(cancelled); break;
            case 40: bool frozen = ReadFlag(reader); end(); edit.SetFrozen(frozen); break;
            case 41: end(); edit.Resynchronize(); break;
            case 43:
                ulong newRevision = reader.ReadUInt64(); end(); Accepted(scene, edit.NewDocument(newRevision, UserPermissions()));
                WriteEditorState(writer, edit.State); break;
            case 44:
                string opened = ReadText(reader); ulong openRevision = reader.ReadUInt64(); end(); Accepted(scene, edit.OpenDocument(opened, openRevision, UserPermissions()));
                WriteEditorState(writer, edit.State); break;
            case 45:
                string saved = ReadText(reader); end(); edit.SaveDocument(string.IsNullOrEmpty(saved) ? null : saved, UserPermissions());
                WriteEditorState(writer, edit.State); break;
            case 46:
                Guid deleted = ReadUuid(reader); ulong deleteRevision = reader.ReadUInt64(); end();
                Accepted(scene, edit.Invoke(new(EditSession.ContractVersion, Guid.NewGuid(), edit.SessionId, deleteRevision,
                    "ncma.scene.delete_object", JsonSerializer.SerializeToElement(new { objectId = deleted })), UserPermissions(deleted)));
                WriteEditorState(writer, edit.State); break;
            default: throw new ArgumentException("Unknown scene operation.");
        }
    }
}
