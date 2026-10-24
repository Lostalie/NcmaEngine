using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Gui;

internal static class PresenterPanelTests
{
    private static readonly (string Flag, string Method, ulong Panel)[] Panels = [
        ("_showCharacters", "BuildCharacterDebug", 6),
        ("_showGraphReads", "BuildGraphReads", 301),
        ("_showAnimationRuntime", "BuildAnimationRuntime", 411),
        ("_showWorkflows", "BuildWorkflows", 413)];
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool condition) { if (!condition) throw new Exception("Presenter panel boundary assertion failed."); }
    private static void Show(EditorPresenter view, string flag, bool value) =>
        typeof(EditorPresenter).GetField(flag, Private)!.SetValue(view, value);
    private static string Packet(EditorPresenter view) =>
        Convert.ToHexString(MemoryMarshal.AsBytes(view.Items)) + ":" + Convert.ToHexString(view.Text);
    private static string Actions(EditorPresenter view)
    {
        var rows = new List<string>();
        var map = (IDictionary)typeof(EditorPresenter).GetField("_actions", Private)!.GetValue(view)!;
        foreach (DictionaryEntry e in map)
            rows.Add(e.Key + ":" + JsonSerializer.Serialize(e.Value, e.Value!.GetType()));
        return string.Join("\n", rows);
    }
    internal static IEnumerable<(string, Action)> Cases(string root)
    {
        yield return ("E5-D closed panel guards and missing services", () => {
            using var owner = new EditorSessionOwner("Panel guards", activateEditor: false);
            var view = new EditorPresenter(new EditorWorkspace(owner), null);
            foreach (var panel in Panels) {
                Show(view, panel.Flag, true);
                object[] args = [1280f, 720f, 123UL];
                typeof(EditorPresenter).GetMethod(panel.Method, Private)!.Invoke(view, args);
                Check((ulong)args[2] == 123 && view.Items.IsEmpty && view.Text.IsEmpty);
                Show(view, panel.Flag, false);
            }
            owner.Dispose();
            // Closed workflows must not read Owner.Workflows (which verifies owner lifetime).
            foreach (var panel in Panels) {
                object[] args = [1280f, 720f, 123UL];
                typeof(EditorPresenter).GetMethod(panel.Method, Private)!.Invoke(view, args);
                Check((ulong)args[2] == 123 && view.Items.IsEmpty && view.Text.IsEmpty);
            }
        });
        foreach (var panel in Panels) {
            yield return ("E5-D first open/close/reopen " + panel.Method, () => {
                using var owner = new EditorSessionOwner("Panel lifecycle");
                var workspace = new EditorWorkspace(owner);
                var view = new EditorPresenter(workspace, null, workspaceStyle: true);
                view.AttachCharacters(new(owner, () => null));
                view.AttachGraphReads(new(workspace, root));
                view.AttachAnimationRuntime(new(owner, () => null));
                byte[] document = owner.Document.CaptureBytes();
                view.Build(1, 1280, 720);
                string closed = Packet(view), closedActions = Actions(view);
                Show(view, panel.Flag, true);
                view.Build(2, 1280, 720);
                Check(view.Items.ToArray().Any(i => i.Kind == (uint)GuiItemKind.PanelBegin && i.WidgetHigh == 1 && i.WidgetLow == panel.Panel));
                string opened = Packet(view), openedActions = Actions(view);
                Check(opened != closed);
                view.Build(3, 1280, 720);
                Check(Packet(view) == opened && Actions(view) == openedActions);
                // Exercise actual stamped close events where the panel has a close control.
                if (panel.Panel is 301 or 413) {
                    ulong domain = panel.Panel == 301 ? 40UL : 61UL;
                    ulong id = panel.Panel == 301 ? 8UL : 9UL;
                    var item = view.Items.ToArray().Single(i => i.WidgetHigh == domain && i.WidgetLow == id);
                    var frame = view.Frame;
                    view.Apply([new GuiEvent { Kind = item.Kind, WidgetHigh = domain, WidgetLow = id, Phase = 3,
                        Frame = frame.Frame, ViewGeneration = frame.ViewGeneration,
                        DocumentGeneration = frame.DocumentGeneration, Revision = frame.Revision }], []);
                    Check(!(bool)typeof(EditorPresenter).GetField(panel.Flag, Private)!.GetValue(view)!);
                } else Show(view, panel.Flag, false);
                view.Build(4, 1280, 720);
                Check(Packet(view) == closed && Actions(view) == closedActions);
                Show(view, panel.Flag, true);
                view.Build(5, 1280, 720);
                Check(Packet(view) == opened && Actions(view) == openedActions);
                Check(owner.Document.CaptureBytes().SequenceEqual(document) && owner.Play is null &&
                    owner.Endpoint is null && owner.Edit!.State.UndoCount == 0 && workspace.SnapshotReads == 1);
            });
        }
    }
}
