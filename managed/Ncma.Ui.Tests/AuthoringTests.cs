using System.Text.Json;
using Ncma.Assets.Authoring;
using Ncma.Editor.Core;
using Ncma.Scene;
using Ncma.Ui;

internal static class AuthoringTests
{
    private static void Check(bool value) { if (!value) throw new Exception("UI authoring assertion failed."); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException) { return; } throw new Exception("UI authoring accepted invalid input."); }
    private static string Root() { string path = Path.Combine(AppContext.BaseDirectory, "ui-authoring-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(path, "assets")); return path; }
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    private static CapabilityRequest Request(EditSession edit, string cap, JsonElement input) => new(2, Guid.NewGuid(), edit.SessionId, edit.Revision, cap, input);
    private static CapabilityPermissions Permissions => new([UiCommands.CapabilityName, "ncma.history.undo", "ncma.history.redo", "ncma.scene.transaction"]);
    public static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("UI file commit, shared scene/asset Undo/Redo, draft cancel and restart", () => {
            string root = Root(); var d = UiDefinition.Create(Guid.NewGuid(), "HUD"); var clock = new AssetRevisionClock();
            var scope = new UiWriteScope(new Dictionary<string, Guid> { ["assets/Hud.ncmaui"] = d.AssetId }, (_, _) => false, () => true);
            using var commands = new UiCommands(new(root), scope, clock); var edit = new EditSession(new SceneDocument()); edit.RegisterCommandParticipant(UiCommands.Descriptor, commands);
            Guid proposal = commands.PrepareProposal("assets/Hud.ncmaui", d, clock.Revision);
            var request = Request(edit, UiCommands.CapabilityName, Json(new { proposalId = proposal, expectedAssetRevision = clock.Revision }));
            Check(edit.Invoke(request).Status == "denied"); Check(edit.Invoke(request, Permissions).Changed); Check(edit.Invoke(request, Permissions).Replayed);
            Check(commands.Read("assets/Hud.ncmaui").AssetId == d.AssetId);
            byte[] scene = edit.Document.CaptureBytes(); var before = commands.Read("assets/Hud.ncmaui");
            var changed = before.Elements[0] with { Name = "Edited" };
            var draft = new UiDraft(edit, commands, clock, "assets/Hud.ncmaui"); draft.Update([new(UiEditKind.Replace, changed.Id, changed)]);
            Check(edit.State.EditBusy);
            Check(edit.Invoke(Request(edit, "ncma.history.undo", Json(new { })), Permissions).Code == "edit_busy");
            Check(edit.Invoke(Request(edit, "ncma.scene.transaction", Json(new { operations = new[] { new { op = "create", objectId = Guid.NewGuid(), name = "Blocked" } } })), Permissions).Code == "edit_busy");
            Reject(() => edit.Select(null)); Reject(() => edit.NewDocument(edit.Revision, Permissions));
            Reject(() => new UiDraft(edit, commands, clock, "assets/Hud.ncmaui"));
            Reject(() => Task.Run(() => draft.Capture()).GetAwaiter().GetResult());
            Check(commands.Read("assets/Hud.ncmaui").Elements[0].Name == "Root"); draft.Cancel(); Reject(() => draft.Capture());
            Check(!edit.State.EditBusy);
            draft = new(edit, commands, clock, "assets/Hud.ncmaui");
            for (int i = 0; i < 100; i++) draft.Update([new(UiEditKind.Replace, changed.Id, changed with { Name = "Edited" + i })]);
            Check(draft.Commit(Permissions)?.Changed == true && edit.State.UndoCount == 2);
            Check(edit.Document.CaptureBytes().AsSpan().SequenceEqual(scene));
            Check(edit.Invoke(Request(edit, "ncma.history.undo", Json(new { })), Permissions).Changed);
            Check(commands.Read("assets/Hud.ncmaui").Elements[0].Name == "Root");
            Check(edit.Invoke(Request(edit, "ncma.history.redo", Json(new { })), Permissions).Changed);
            Check(commands.Read("assets/Hud.ncmaui").Elements[0].Name == "Edited99");
            var stale = new UiDraft(edit, commands, clock, "assets/Hud.ncmaui"); edit.SetFrozen(true); Reject(() => stale.Capture()); edit.SetFrozen(false);
            Check(edit.Invoke(Request(edit, "ncma.history.undo", Json(new { })), Permissions).Changed);
            Check(edit.Invoke(Request(edit, "ncma.history.undo", Json(new { })), Permissions).Changed && !File.Exists(Path.Combine(root, "assets/Hud.ncmaui")));
            Check(edit.Invoke(Request(edit, "ncma.history.redo", Json(new { })), Permissions).Changed);
            using var reopened = new UiCommands(new(root), scope, new()); Check(reopened.Read("assets/Hud.ncmaui").AssetId == d.AssetId);
        });
        yield return ("UI exact grants, resource type, stale proposal/replay and external file conflict", () => {
            string root = Root(); var d = UiDefinition.Create(Guid.NewGuid(), "HUD"); var clock = new AssetRevisionClock(); bool current = true;
            Guid font = Guid.NewGuid(); var scope = new UiWriteScope(new Dictionary<string, Guid> { ["assets/Hud.ncmaui"] = d.AssetId }, (id, kind) => id == font && kind == UiResourceKind.Font, () => current);
            using var commands = new UiCommands(new(root), scope, clock); var edit = new EditSession(new SceneDocument()); edit.RegisterCommandParticipant(UiCommands.Descriptor, commands);
            Reject(() => commands.PrepareProposal("../Hud.ncmaui", d, 0)); Reject(() => commands.PrepareProposal("assets/Other.ncmaui", d, 0));
            Reject(() => commands.PrepareProposal("assets/Hud.ncmaui", d with { AssetId = Guid.NewGuid() }, 0));
            var text = UiElement.Create(Guid.NewGuid(), "Text", UiKind.Text, d.Root) with { Font = Guid.NewGuid() };
            Reject(() => commands.PrepareProposal("assets/Hud.ncmaui", d with { Elements = [d.Elements[0], text] }, 0));
            Guid p = commands.PrepareProposal("assets/Hud.ncmaui", d, 0); var request = Request(edit, UiCommands.CapabilityName, Json(new { proposalId = p, expectedAssetRevision = 0 }));
            Check(edit.Invoke(request, Permissions).Changed); current = false; Check(edit.Invoke(request, Permissions).Status == "denied"); Check(edit.Invoke(Request(edit, "ncma.history.undo", Json(new { })), Permissions).Status == "denied"); current = true;
            Reject(() => commands.PrepareProposal("assets/Hud.ncmaui", d, 0));
            var draft = new UiDraft(edit, commands, clock, "assets/Hud.ncmaui"); draft.Update([new(UiEditKind.Replace, d.Root, d.Elements[0] with { Name = "Draft" })]);
            File.WriteAllBytes(Path.Combine(root, "assets/Hud.ncmaui"), UiCodec.Encode(d with { Name = "External" })); Reject(() => draft.Commit(Permissions));
            Check(commands.Read("assets/Hud.ncmaui").Name == "External");
            Check(!edit.State.EditBusy); // Failed confirmation releases the gate without overwriting external data.
        });
        yield return ("UI journal recovery and failure compensation preserve exact documents", () => {
            foreach (string stage in new[] { "journal_created", "backup:0", "published:0" }) {
                string root = Root(); var d = UiDefinition.Create(Guid.NewGuid(), "HUD"); string path = "assets/Hud.ncmaui";
                File.WriteAllBytes(Path.Combine(root, path), UiCodec.Encode(d));
                var scope = new UiWriteScope(new Dictionary<string, Guid> { [path] = d.AssetId }, (_, _) => false, () => true);
                using (var commands = new UiCommands(new(root), scope, new(), where => { if (where == stage) throw new IOException("injected crash"); })) {
                    Guid p = commands.PrepareProposal(path, d with { Name = "After" }, 0);
                    var memento = commands.Prepare(Json(new { proposalId = p, expectedAssetRevision = 0 })); Reject(() => commands.Publish(memento, true));
                }
                using var recovery = new UiCommands(new(root), scope, new()); recovery.Recover(path);
                Check(recovery.Read(path).Name == "HUD" && !File.Exists(Path.Combine(root, path + ".journal")));
            }
        });
        yield return ("UI reducers are atomic, bounded and preserve persistent references", () => {
            var d = UiDefinition.Create(Guid.NewGuid(), "HUD"); var frame = UiElement.Create(Guid.NewGuid(), "Frame", UiKind.Frame, d.Root);
            var text = UiElement.Create(Guid.NewGuid(), "Text", UiKind.Text, frame.Id);
            var result = UiEdits.Apply(d, [new(UiEditKind.Add, frame.Id, frame), new(UiEditKind.Add, text.Id, text)]);
            Check(result.Elements.Length == 3 && d.Elements.Length == 1);
            Check(UiEdits.Apply(result, [new(UiEditKind.RemoveSubtree, frame.Id)]).Elements.Length == 1);
            Reject(() => UiEdits.Apply(result, [new(UiEditKind.Reparent, frame.Id, Parent: text.Id)]));
            Reject(() => UiEdits.Apply(result, [new(UiEditKind.RemoveSubtree, d.Root)]));
            Reject(() => UiEdits.Apply(d, Enumerable.Repeat(new UiEdit(UiEditKind.Add, frame.Id, frame), 257).ToArray()));
            Check(d.Elements.Length == 1);
        });
    }
}
