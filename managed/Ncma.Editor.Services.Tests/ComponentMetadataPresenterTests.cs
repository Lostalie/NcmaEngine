using System.Text;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Runtime;

internal static unsafe class ComponentMetadataPresenterTests
{
    private static void Check(bool value) { if (!value) throw new Exception("E6-B3 metadata presenter assertion"); }
    private static string Label(EditorPresenter view, GuiItem item) => Encoding.UTF8.GetString(view.Text.Slice((int)item.LabelOffset, (int)item.LabelLength));
    private static GuiItem Item(EditorPresenter view, string label) => view.Items.ToArray().Single(i => Label(view, i) == label);
    private static GuiEvent Event(EditorPresenter view, GuiItem item, uint phase = 3, double value = 0) => new() {
        Kind = item.Kind, WidgetHigh = item.WidgetHigh, WidgetLow = item.WidgetLow, Phase = phase, Value = value,
        Frame = view.Frame.Frame, ViewGeneration = view.Frame.ViewGeneration, DocumentGeneration = view.Frame.DocumentGeneration, Revision = view.Frame.Revision
    };
    private static ComponentRegistry Registry(bool swap = false)
    {
        var registry = new ComponentRegistry(); var fields = TransformData.Generated.DescribeFields();
        if (swap) fields = fields.Select(f => f.FieldId == "position.x" ? f with { FieldId = "position.y" } : f.FieldId == "position.y" ? f with { FieldId = "position.x" } : f).ToArray();
        registry.Register("ncma.transform", 1, global::Ncma.Runtime.CompositionRegistry.Build().Describe("ncma.transform").Schema.GetRawText(), TransformData.Validate,
            fields: fields, defaultFactory: () => TransformData.Identity with { Position = new(7, 8, 9) });
        return registry;
    }
    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("E6-B3 actual presenter uses declared serialized default and original shared Undo", () => {
            using var owner = new EditorSessionOwner("Declared default", components: Registry()); var workspace = new EditorWorkspace(owner);
            var id = workspace.CreateObject(workspace.Stamp); var view = new EditorPresenter(workspace, null); view.Build(1, 1280, 720);
            int undo = owner.Edit!.State.UndoCount;
            view.Apply([Event(view, Item(view, "Add Transform"))], []);
            Check(owner.Document.GetComponent<TransformData>(id).Position.X == 7 && owner.Edit.State.UndoCount == undo + 1);
            workspace.History(workspace.Stamp, false); Check(!owner.Document.HasComponent<TransformData>(id));
            workspace.History(workspace.Stamp, true); Check(owner.Document.GetComponent<TransformData>(id).Position.Y == 8);
            view.Build(2, 1280, 720); Check(Item(view, "Position X").Value == 7);
            byte[] before = owner.Document.CaptureBytes(); view.Build(3, 1280, 720);
            Check(before.SequenceEqual(owner.Document.CaptureBytes()) && owner.Play is null && owner.Endpoint is null);
        });
        yield return ("E6-B3 field-ID presenter edits follow metadata path not old index and retain stale/frozen guards", () => {
            using var owner = new EditorSessionOwner("Declared paths", components: Registry(true)); var workspace = new EditorWorkspace(owner);
            var id = workspace.CreateObject(workspace.Stamp); var view = new EditorPresenter(workspace, null); view.Build(1, 1280, 720);
            view.Apply([Event(view, Item(view, "Add Transform"))], []); view.Build(2, 1280, 720);
            var item = Item(view, "Position Y"); Check(item.Value == 8); int undo = owner.Edit!.State.UndoCount;
            var stale = Event(view, item, 3, 99);
            view.Apply([Event(view, item, 1), Event(view, item, 3, 42)], []);
            var value = owner.Document.GetComponent<TransformData>(id);
            Check(value.Position.X == 7 && value.Position.Y == 42 && owner.Edit.State.UndoCount == undo + 1);
            view.Build(3, 1280, 720); var before = owner.Document.CaptureBytes(); view.Apply([stale], []);
            Check(before.SequenceEqual(owner.Document.CaptureBytes()));
            owner.Edit.SetFrozen(true); view.Build(4, 1280, 720); Check(Item(view, "Position Y").Enabled == 0); owner.Edit.SetFrozen(false);
            workspace.History(workspace.Stamp, false); Check(owner.Document.GetComponent<TransformData>(id).Position.Y == 8);
        });
    }
}
