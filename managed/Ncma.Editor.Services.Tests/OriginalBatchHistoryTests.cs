using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Editor.Services;

internal static class OriginalBatchHistoryTests
{
    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("E6 local workspace uses sole history with complete mixed-batch delete approvals", () => {
            using var owner = new EditorSessionOwner("Original batch history");
            var workspace = new EditorWorkspace(owner); var edit = owner.Edit!;
            Guid a = workspace.CreateObject(workspace.Stamp), b = workspace.CreateObject(workspace.Stamp), c = workspace.CreateObject(workspace.Stamp);
            byte[] before = owner.Document.CaptureBytes(); int history = edit.State.UndoCount;
            JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
            var review = edit.ReviewOriginalBatch("Keep explicit runtime difference", edit.Revision, [
                new("ncma.scene.transaction", Json(new { operations = new[] { new { op = "rename", objectId = a, name = "Updated" } } })),
                new("ncma.scene.delete_object", Json(new { objectId = b })),
                new("ncma.scene.delete_object", Json(new { objectId = c })) ]);
            var result = edit.ApplyOriginalBatch(review, Guid.NewGuid(), new(["ncma.scene.transaction", "ncma.scene.delete_object"], [b, c]));
            if (!result.Changed || edit.State.UndoCount != history + 1) throw new Exception("Batch did not use one original history entry");
            byte[] after = owner.Document.CaptureBytes();
            workspace.History(workspace.Stamp, false);
            if (!before.SequenceEqual(owner.Document.CaptureBytes()) || edit.State.UndoCount != history) throw new Exception("Local Undo lost complete targets");
            workspace.History(workspace.Stamp, true);
            if (!after.SequenceEqual(owner.Document.CaptureBytes()) || edit.State.UndoCount != history + 1) throw new Exception("Local Redo lost complete targets");
        });
    }
}
