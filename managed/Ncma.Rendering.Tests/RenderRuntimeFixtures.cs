using Ncma.Scene;
using Ncma.Editor.App;

internal static unsafe partial class Program
{
    // Observe the actual host-owned derivation, including a deliberately stale source.
    // No replacement projection or compatibility World is manufactured by this accessor.
    private static EditorSceneProjection OwnedEditProjection(CandidatePresentation host) =>
        EnvironmentProductEvidence.Field<EditorSceneProjection>(host,"_editProjection");

    // Explicit cold authoring-to-runtime boundary. No live document World or write-back.
    private static RuntimeSceneInstance BakeRenderScene(SceneDocument source) =>
        source.BakeRuntime(source.Revision).Image.Instantiate(source.Components);
}
