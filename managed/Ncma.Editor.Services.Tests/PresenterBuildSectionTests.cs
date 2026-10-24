using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Gui;

internal static class PresenterBuildSectionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool condition) { if (!condition) throw new Exception("Presenter Build section assertion failed."); }
    private static void Flag(EditorPresenter view, string name, bool value) =>
        typeof(EditorPresenter).GetField(name, Private)!.SetValue(view, value);
    private static string Packet(EditorPresenter view)
    {
        var actions = new List<string>();
        foreach (DictionaryEntry entry in (IDictionary)typeof(EditorPresenter).GetField("_actions", Private)!.GetValue(view)!)
            actions.Add(entry.Key + ":" + JsonSerializer.Serialize(entry.Value, entry.Value!.GetType()));
        return Convert.ToHexString(MemoryMarshal.AsBytes(view.Items)) + ":" + Convert.ToHexString(view.Text) +
            ":" + string.Join("\n", actions);
    }
    internal static IEnumerable<(string, Action)> Cases(string output)
    {
        foreach (string mode in new[] { "plain", "toolbar", "workspace" }) {
            yield return ("E5-D Build sections repeat/open/close/reopen " + mode, () => {
                using var owner = new EditorSessionOwner("Build sections");
                var workspace = new EditorWorkspace(owner);
                workspace.CreateObject(workspace.Stamp);
                var preferences = new EditorPreferencesStore(Path.Combine(output, "build-sections-" + mode + ".json"));
                using var fbx = new FbxPreviewSession("unopened-native-library");
                using var animation = new ActionPreviewSession("unopened-native-library");
                var view = new EditorPresenter(workspace, null, fbxPreview: fbx, animationPreview: animation,
                    preferences: preferences, toolbarStyle: mode == "toolbar", workspaceStyle: mode == "workspace");
                byte[] document = owner.Document.CaptureBytes();
                int undo = owner.Edit!.State.UndoCount;
                view.Build(1, 1280, 720);
                string closed = Packet(view);
                view.Build(2, 1280, 720); Check(Packet(view) == closed);
                Flag(view, "_showSceneFiles", true);
                Flag(view, "_showPreferences", true);
                Flag(view, "_showAnimation", true); // Missing Current must not enter the preview body.
                view.Build(3, 1280, 720);
                string opened = Packet(view); Check(opened != closed);
                Check(view.Items.ToArray().Any(i => i.WidgetHigh == 11 && i.WidgetLow == 8));
                Check(!view.Items.ToArray().Any(i => i.WidgetHigh == 9 && i.WidgetLow == 2));
                view.Build(4, 1280, 720); Check(Packet(view) == opened);
                Flag(view, "_showSceneFiles", false); Flag(view, "_showPreferences", false); Flag(view, "_showAnimation", false);
                view.Build(5, 1280, 720); Check(Packet(view) == closed);
                Flag(view, "_showSceneFiles", true); Flag(view, "_showPreferences", true); Flag(view, "_showAnimation", true);
                view.Build(6, 1280, 720); Check(Packet(view) == opened);
                Check(document.SequenceEqual(owner.Document.CaptureBytes()) && owner.Edit.State.UndoCount == undo &&
                    owner.Play is null && owner.Endpoint is null && workspace.SnapshotReads == 1);
            });
        }
        yield return ("E5-D hidden preview guards never touch disposed services", () => {
            using var owner = new EditorSessionOwner("Hidden previews");
            var workspace = new EditorWorkspace(owner);
            var preferences = new EditorPreferencesStore(Path.Combine(output, "hidden-previews.json"));
            preferences.Save(0, preferences.Current with { ShowPreviews = false });
            using var fbx = new FbxPreviewSession("unopened-native-library");
            using var animation = new ActionPreviewSession("unopened-native-library");
            fbx.Dispose(); animation.Dispose();
            foreach (bool unified in new[] { false, true }) {
                var view = new EditorPresenter(workspace, null, fbxPreview: fbx, animationPreview: animation,
                    preferences: preferences, workspaceStyle: unified);
                Flag(view, "_showAnimation", true);
                view.Build(1, 1280, 720);
                Check(!view.Items.ToArray().Any(i => i.WidgetHigh is 7 or 9));
            }
            Check(owner.Document.ObjectCount == 0 && owner.Play is null && owner.Endpoint is null);
        });
    }
}
