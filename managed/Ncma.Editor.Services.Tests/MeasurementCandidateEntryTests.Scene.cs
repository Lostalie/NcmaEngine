using Ncma.Editor.App;
using Ncma.Editor.Services;
using CandidateProgram = Ncma.Editor.App.Program;

internal static partial class MeasurementCandidateEntryTests
{
    private static IEnumerable<(string, Action)> SceneCases()
    {
        yield return ("M8.7-E2E8 AI candidate closed CLI shape and actual early rejection", () => {
            Check(CandidateProgram.AiSceneMeasurementArguments(["--measure-ai-scene", "r", new('A', 64), "o"]));
            foreach (var args in new[] { new[] { "--measure-ai-scene" }, new[] { "--measure-ai-scene", "r", new string('a', 64), "o" },
                new[] { "--measure-ai-scene", "r", new string('A', 64), "o", "--project" } }) {
                Check(!CandidateProgram.AiSceneMeasurementArguments(args)); Check(CandidateProgram.Main(args) == 2);
            }
#if !NCMA_AI || NCMA_EXTENSIONS
            Check(CandidateProgram.Main(["--measure-ai-scene", "MISSING", new('A', 64), "MISSING"]) == 3);
#endif
        });
        yield return ("M8.7-E2E8 transient presenter closed actions and shortcuts preserve default mode", () => {
            using var owner = new EditorSessionOwner("diagnostic"); var workspace = new EditorWorkspace(owner);
            var normal = new EditorPresenter(workspace, null);
            var view = new EditorPresenter(workspace, null, transientSceneMeasurement: true);
            foreach (string kind in new[] { "new", "open", "save", "browse_open", "browse_save", "start", "reload", "mcp_toggle", "fbx_browse", "workflow_run", "layout_split", "unknown-future-action" })
                Check(normal.MeasurementActionAllowed(kind) && !view.MeasurementActionAllowed(kind));
            foreach (string kind in new[] { "select", "create", "name", "transform", "undo", "redo", "aiui_prepare", "aiui_send", "aiui_confirm", "aiui_diagnostic_send" }) Check(view.MeasurementActionAllowed(kind));
            byte[] before = owner.Document.CaptureBytes(); view.Build(1, 1280, 720);
            foreach (uint key in new uint[] { 78, 79, 83, 82 }) Check(!view.Shortcut(key, true, true, false, true));
            Check(before.SequenceEqual(owner.Document.CaptureBytes()) && owner.Edit!.State.FilePath is null && owner.Play is null);
            // Original enabled and event identity gates remain separate from this mode filter.
            var play = view.Items.ToArray().Single(i => i.WidgetHigh == 2 && i.WidgetLow == 14); Check(play.Enabled == 0);
            view.Apply([new() { WidgetHigh = play.WidgetHigh, WidgetLow = play.WidgetLow, Kind = play.Kind, Phase = 3, Frame = view.Frame.Frame,
                ViewGeneration = view.Frame.ViewGeneration, DocumentGeneration = view.Frame.DocumentGeneration, Revision = view.Frame.Revision }], []);
            Check(owner.Play is null && before.SequenceEqual(owner.Document.CaptureBytes()));
        });
    }
}
