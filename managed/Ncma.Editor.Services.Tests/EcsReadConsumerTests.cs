using System.Reflection;
using Ncma.Animation;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static class EcsReadConsumerTests
{
    private static void Check(bool value) { if (!value) throw new Exception("E4-D consumer assertion failed."); }
    // Exercise the exact production selector without granting an import ticket or adding a test API.
    private static Guid[] Affected(SceneDocument document, HashSet<Guid> ids) => (Guid[])typeof(EditorAssetWorkflow)
        .GetMethod("CollectRenderReferences", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [document, ids])!;
    // Independent snapshot oracle: not the selector's typed UUID read path.
    private static Guid[] Oracle(SceneDocument document, HashSet<Guid> ids) =>
        document.CaptureSnapshot().Objects.Where(o => o.Components.Any(c => c.TypeId switch {
            StaticMeshData.TypeId => Static(document.Components.Decode<StaticMeshData>(c), ids),
            SkinnedMeshData.TypeId => Skin(document.Components.Decode<SkinnedMeshData>(c), ids),
            ClipPlaybackData.TypeId => ids.Contains(document.Components.Decode<ClipPlaybackData>(c).ClipId),
            _ => false
        })).Select(o => o.Id).ToArray();
    private static bool Static(StaticMeshData value, HashSet<Guid> ids) =>
        ids.Contains(value.MeshId) || ids.Contains(value.MaterialSetId);
    private static bool Skin(SkinnedMeshData value, HashSet<Guid> ids) =>
        ids.Contains(value.CharacterId) || ids.Contains(value.MeshId) || ids.Contains(value.MaterialSetId);
    private static object Field(EditorPresenter view, string name) => typeof(EditorPresenter).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("E6-A optional authoring import references preserve absent registrations", () => {
            var document = new SceneDocument("Optional"); document.CreateObject("No render types");
            Check(Affected(document, [Guid.NewGuid()]).Length == 0);
        });
        yield return ("E6-A authoring references preserve creation order, conflicts and complete 4096 union", () => {
            var document = new SceneDocument("References", RenderComponentRegistry.CreateRegistry());
            Guid match = Guid.NewGuid(), other = Guid.NewGuid();
            var ids = new HashSet<Guid> { match };
            Guid first = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), second = Guid.Parse("11111111-1111-1111-1111-111111111111");
            ComponentSnapshot Component<T>(string type, T value) where T : struct, IComponent =>
                new(type, 1, document.Components.Encode(value));
            var objects = new List<SceneObjectData> {
                new(first, "Skin first", [Component(SkinnedMeshData.TypeId, new SkinnedMeshData(match, other, other, other, true, true, 1))], []),
                new(second, "Static second", [
                    Component(StaticMeshData.TypeId, new StaticMeshData(match, other, true, true, 1)),
                    Component(SkinnedMeshData.TypeId, new SkinnedMeshData(other, other, other, match, false, false, 0)),
                    Component(ClipPlaybackData.TypeId, new ClipPlaybackData(match, false, true, 1, 0))], [])
            }; // Malformed dual geometry / missing Transform still reported, not render validation.
            document.RestoreSnapshot(document.CaptureSnapshot() with { Objects = objects.ToArray() });
            Check(Affected(document, ids).SequenceEqual(new[] { first, second }));
            for (int i = 2; i < World.MaxObjects; i++) {
                ComponentSnapshot value = (i % 3) switch {
                    0 => Component(StaticMeshData.TypeId, new StaticMeshData(other, i % 2 == 0 ? match : other, false, false, 0)),
                    1 => Component(SkinnedMeshData.TypeId, new SkinnedMeshData(other, match, other, other, true, true, 1)),
                    _ => Component(ClipPlaybackData.TypeId, new ClipPlaybackData(match, false, true, 1, 0))
                };
                objects.Add(new(Guid.NewGuid(), "Object" + i, [value], []));
            }
            document.RestoreSnapshot(document.CaptureSnapshot() with { Objects = objects.ToArray() });
            var snapshot = document.CaptureSnapshot(); var bytes = document.CaptureBytes(); ulong revision = document.Revision;
            Check(Affected(document, ids).SequenceEqual(Oracle(document, ids)));
            Check(document.Revision == revision && bytes.SequenceEqual(document.CaptureBytes()));
            document.DeleteObject(first); var reused = document.CreateObject("Reused", first);
            document.SetComponent(reused, new StaticMeshData(match, other, false, false, 0));
            var current = Affected(document, ids); Check(current.SequenceEqual(Oracle(document, ids)) && current[^1] == first);
            document.RestoreSnapshot(snapshot); Check(Affected(document, ids).SequenceEqual(Oracle(document, ids)));
            Check(Affected(document, []).Length == 0);
        });
        yield return ("E4-D Presenter row identities survive value/selection changes and prune deleted UUIDs", () => {
            using var owner = new EditorSessionOwner("Rows"); var workspace = new EditorWorkspace(owner);
            var a = workspace.CreateObject(workspace.Stamp); var b = workspace.CreateObject(workspace.Stamp); var view = new EditorPresenter(workspace, null);
            view.Build(1, 1280, 720); var rows = (Dictionary<Guid, ulong>)Field(view, "_rowIds"); ulong aRow = rows[a], bRow = rows[b];
            workspace.Select(workspace.Stamp, a); view.Build(2, 1280, 720); Check(rows[a] == aRow && rows[b] == bRow);
            workspace.Transaction(workspace.Stamp, "Rename", [new { op = "rename", objectId = a, name = "Renamed" }]); view.Build(3, 1280, 720);
            Check(rows[a] == aRow && rows[b] == bRow);
            typeof(EditorPresenter).GetField("_componentOffset", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(view, 9);
            workspace.Select(workspace.Stamp, b); view.Build(4, 1280, 720); Check((int)Field(view, "_componentOffset") == 0);
            workspace.Delete(workspace.Stamp, a, a); view.Build(5, 1280, 720); Check(!rows.ContainsKey(a) && rows[b] == bRow);
            workspace.History(workspace.Stamp, false); view.Build(6, 1280, 720); Check(rows.ContainsKey(a) && rows[a] != aRow && rows[b] == bRow);
            workspace.New(workspace.Stamp, true); view.Build(7, 1280, 720); Check(rows.Count == 0);
            workspace.History(workspace.Stamp, false); view.Build(8, 1280, 720); Check(rows.ContainsKey(a) && rows.ContainsKey(b));
        });
    }
}
