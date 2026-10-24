using System.Text;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Gameplay;
using Ncma.Gui;
using Ncma.Runtime;
using Ncma.Scene;

// Real presenter event routes; prepared only, no test/product execution during E6 substages.
internal static partial class RuntimeChangeReviewTests
{
    private sealed class KeepUi(EditorSessionOwner owner, bool styled = false)
    {
        internal readonly EditorPresenter Presenter = new(new EditorWorkspace(owner), null, workspaceStyle: styled);
        private ulong _frame = 1;
        internal void Build() => Presenter.Build(_frame++, 1280, 720);
        internal string Text() { Build(); return Encoding.UTF8.GetString(Presenter.Text); }
        internal GuiItem Item(ulong high, ulong low) { Build(); return Presenter.Items.ToArray().Single(i => i.WidgetHigh == high && i.WidgetLow == low); }
        internal GuiEvent Event(ulong high, ulong low, double value = 0)
        {
            var item = Item(high, low); var frame = Presenter.Frame;
            return new() { Kind = item.Kind, WidgetHigh = high, WidgetLow = low, Phase = 3, Value = value,
                Frame = frame.Frame, ViewGeneration = frame.ViewGeneration, DocumentGeneration = frame.DocumentGeneration, Revision = frame.Revision };
        }
        internal void Click(ulong high, ulong low, double value = 0) { var e = Event(high, low, value); Presenter.Apply([e], []); }
        internal void Select(Guid id, bool runtime, double value = 1)
        {
            Build(); var bytes = Presenter.Text.ToArray();
            var item = Presenter.Items.ToArray().Single(i => i.WidgetHigh == 64 && i.WidgetLow >= (runtime ? 200ul : 100ul) &&
                i.WidgetLow < (runtime ? 208ul : 108ul) && Encoding.UTF8.GetString(bytes, (int)i.LabelOffset, (int)i.LabelLength).Contains(id.ToString("D"), StringComparison.Ordinal));
            Click(64, item.WidgetLow, value);
        }
        internal void ReadAllDiffPages()
        {
            Click(65, 1);
            for (int page = 0; page < 80; ++page)
            {
                Click(65, 5, 1);
                if (Item(65, 4).Enabled == 0) return;
                Click(65, 4);
            }
            throw new Exception("Bounded complete diff exceeded the UI page limit");
        }
        internal void ConfirmDeletes()
        {
            Build(); var deletes = Presenter.Items.ToArray().Where(i => i.WidgetHigh == 65 && i.WidgetLow >= 100).Select(i => i.WidgetLow).ToArray();
            foreach (ulong low in deletes) Click(65, low, 1);
        }
    }

    private static long AuthorCaptures(SceneDocument document) => (long)typeof(SceneDocument)
        .GetField("_capturedDocumentCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(document)!;

    internal static IEnumerable<(string, Action)> PresenterCases(string repository)
    {
        yield return ("E6 presenter human diff route keeps created deleted and changed objects with original history", () => {
            using var f = new Fixture(repository); var d = f.Owner.Document; byte[] before = d.CaptureBytes(); var state = f.Owner.Edit!.State;
            var world = f.Play.Scene.World; world.Rename(world.FindEntity(f.Changed), "Kept through UI");
            world.Destroy(world.FindEntity(f.Deleted)); world.Set(world.CreateEntity("Created through UI", f.Created), TransformData.Identity);
            var ui = new KeepUi(f.Owner); ui.Click(64, 1); ui.Select(f.Changed, false); ui.Select(f.Deleted, false); ui.Select(f.Created, true); ui.Click(64, 8);
            Check(!ui.Presenter.SceneHudInputAllowed && !ui.Presenter.Shortcut(83, true, false, false, true));
            Check(ui.Text().Contains("审阅指纹") && before.SequenceEqual(d.CaptureBytes()) && f.Owner.Edit.State == state);
            ui.Click(65, 2); Check(ui.Text().Contains("原命令")); ui.ReadAllDiffPages(); ui.ConfirmDeletes();
            ui.Click(65, 6, 1); Check(ui.Item(65, 7).Enabled == 1); ui.Click(65, 7);
            Check(f.Owner.Play is null && f.Owner.Edit.State.UndoCount == state.UndoCount + 1 && d.GetObjectName(f.Changed) == "Kept through UI");
            Check(!d.ContainsObject(f.Deleted) && d.ContainsObject(f.Created));
            byte[] after = d.CaptureBytes(); var workspace = new EditorWorkspace(f.Owner);
            workspace.History(workspace.Stamp, false); Check(before.SequenceEqual(d.CaptureBytes()));
            workspace.History(workspace.Stamp, true); Check(after.SequenceEqual(d.CaptureBytes()));
            Check(f.Owner.Endpoint is null && !f.Owner.Edit.Describe().Any(c => c.Name.Contains("keep_play", StringComparison.Ordinal)));
        });
        yield return ("E6 presenter requires every diff page deletion confirmation and final human checkbox", () => {
            using var f = new Fixture(repository); f.Play.Scene.World.Destroy(f.Play.Scene.World.FindEntity(f.Changed));
            byte[] before = f.Owner.Document.CaptureBytes(); var ui = new KeepUi(f.Owner);
            ui.Click(64, 1); ui.Select(f.Changed, false); ui.Click(64, 8);
            Check(ui.Item(65, 6).Enabled == 0 && ui.Item(65, 7).Enabled == 0);
            ui.Click(65, 7); Check(f.Owner.Play is not null && before.SequenceEqual(f.Owner.Document.CaptureBytes()));
            ui.ReadAllDiffPages(); Check(ui.Item(65, 6).Enabled == 0); ui.ConfirmDeletes(); Check(ui.Item(65, 6).Enabled == 1);
            ui.Click(65, 6, 1); Check(ui.Item(65, 7).Enabled == 1);
            ui.Click(65, 100, 0); Check(ui.Item(65, 7).Enabled == 0 && ui.Item(65, 6).Value == 0);
            ui.Click(65, 7); Check(f.Play.State == PlayState.Paused && before.SequenceEqual(f.Owner.Document.CaptureBytes()));
            ui.Click(64, 2); Check(f.Play.State == PlayState.Paused && before.SequenceEqual(f.Owner.Document.CaptureBytes()));
        });
        yield return ("E6 presenter queued confirm refuses changed runtime and hides stale before-after data", () => {
            using var f = new Fixture(repository); var world = f.Play.Scene.World; world.Rename(world.FindEntity(f.Plain), "Reviewed");
            byte[] before = f.Owner.Document.CaptureBytes(); var ui = new KeepUi(f.Owner); ui.Click(64, 1); ui.Select(f.Plain, false); ui.Click(64, 8);
            ui.ReadAllDiffPages(); ui.Click(65, 6, 1); var queued = ui.Event(65, 7);
            world.Rename(world.FindEntity(f.Plain), "Not reviewed"); ui.Presenter.Apply([queued], []);
            Check(f.Owner.Play is not null && before.SequenceEqual(f.Owner.Document.CaptureBytes()));
            Check(ui.Text().Contains("所有确认失效") && !ui.Presenter.Items.ToArray().Any(i => i.WidgetHigh == 65));
            ui.Click(64, 3); Check(!ui.Presenter.Items.ToArray().Any(i => i.WidgetHigh == 65));
            ui.Select(f.Plain, false); ui.Click(64, 8); Check(ui.Item(65, 7).Enabled == 0);
            f.Owner.StopPlay(); Check(ui.Text().Contains("所有确认失效"));
        });
        yield return ("E6 presenter one UUID has shared selection and selection changes revoke reviewed tickets", () => {
            using var f = new Fixture(repository); f.Play.Scene.World.Rename(f.Play.Scene.World.FindEntity(f.Plain), "Changed");
            var ui = new KeepUi(f.Owner); ui.Click(64, 1); ui.Select(f.Plain, false); ui.Select(f.Plain, true); ui.Click(64, 8);
            Check(f.Changes.IsPreparedCurrent(null) == false); ui.ReadAllDiffPages(); ui.Click(65, 6, 1);
            ui.Select(f.Plain, true, 0); Check(ui.Text().Contains("已选 0/32") && !ui.Presenter.Items.ToArray().Any(i => i.WidgetHigh == 65));
            Check(f.Owner.Play is not null && f.Owner.Document.GetObjectName(f.Plain) == "Plain");
        });
        yield return ("E6 presenter refresh and close do not stop Play and no per-frame full diff capture", () => {
            using var f = new Fixture(repository); var ui = new KeepUi(f.Owner); ui.Click(64, 1);
            f.Play.Scene.World.Rename(f.Play.Scene.World.FindEntity(f.Plain), "Changed"); ui.Click(64, 3);
            ui.Select(f.Plain, false); ui.Click(64, 8); ui.Build(); long captured = AuthorCaptures(f.Owner.Document);
            for (int i = 0; i < 8; ++i) ui.Build();
            Check(captured == AuthorCaptures(f.Owner.Document) && f.Play.State == PlayState.Paused);
            ui.Click(64, 3); Check(!ui.Text().Contains("审阅指纹") && f.Owner.Play is not null);
            ui.Click(64, 2); ui.Build(); Check(!ui.Presenter.Items.ToArray().Any(i => i.WidgetHigh == 65 || i.WidgetHigh == 64 && i.WidgetLow == 2));
            Check(f.Owner.Play is not null && f.Owner.Document.GetObjectName(f.Plain) == "Plain");
        });
        yield return ("E6 styled editor exposes local keep-Play menu without runtime MCP service", () => {
            using var f = new Fixture(repository); var ui = new KeepUi(f.Owner, styled: true);
            // Toolbar menu ID is resolved from the actual displayed item, never a fabricated capability.
            ui.Build(); var bytes = ui.Presenter.Text.ToArray(); var tools = ui.Presenter.Items.ToArray().Single(i =>
                i.Kind == (uint)GuiItemKind.MenuButton && Encoding.UTF8.GetString(bytes, (int)i.LabelOffset, (int)i.LabelLength) == "工具");
            ui.Click(tools.WidgetHigh, tools.WidgetLow); ui.Click(24, 72);
            Check(ui.Text().Contains("保留 Play") && ui.Presenter.Items.ToArray().Any(i => i.WidgetHigh == 64 && i.WidgetLow == 8));
            Check(f.Owner.Endpoint is null && f.Play.State == PlayState.Paused);
        });
    }
}
