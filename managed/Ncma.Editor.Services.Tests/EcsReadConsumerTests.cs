using System.Reflection;
using Ncma.Animation;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Runtime;
using Ncma.Scene.Rendering;

internal static class EcsReadConsumerTests
{
    private static void Check(bool value) { if (!value) throw new Exception("E4-D consumer assertion failed."); }
    // Exercise the exact production selector without granting an import ticket or adding a test API.
    private static Guid[] Affected(World world, HashSet<Guid> ids) => (Guid[])typeof(EditorAssetWorkflow)
        .GetMethod("CollectRenderReferences", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [world, ids])!;
    private static Guid[] Oracle(World world, HashSet<Guid> ids) => world.GetEntities().Where(e =>
        world.Has<StaticMeshData>(e) && (ids.Contains(world.Get<StaticMeshData>(e).MeshId) || ids.Contains(world.Get<StaticMeshData>(e).MaterialSetId)) ||
        world.Has<SkinnedMeshData>(e) && (ids.Contains(world.Get<SkinnedMeshData>(e).CharacterId) || ids.Contains(world.Get<SkinnedMeshData>(e).MeshId) || ids.Contains(world.Get<SkinnedMeshData>(e).MaterialSetId)) ||
        world.Has<ClipPlaybackData>(e) && ids.Contains(world.Get<ClipPlaybackData>(e).ClipId)).Select(world.GetUuid).ToArray();
    private static object Field(EditorPresenter view, string name) => typeof(EditorPresenter).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("E4-D optional import reference queries preserve absent registrations", () => {
            var world = new World(); world.CreateEntity("No render types"); Check(Affected(world, [Guid.NewGuid()]).Length == 0);
        });
        yield return ("E4-D mixed import references preserve creation order, conflicts and complete 4096 union", () => {
            var world = new World("References", RenderComponentRegistry.CreateRegistry()); Guid match = Guid.NewGuid(), other = Guid.NewGuid();
            var ids = new HashSet<Guid> { match };
            var first = world.CreateEntity("Skin first", Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
            var second = world.CreateEntity("Static second", Guid.Parse("11111111-1111-1111-1111-111111111111"));
            world.Set(first, new SkinnedMeshData(match, other, other, other, true, true, 1));
            world.Set(second, new StaticMeshData(match, other, true, true, 1));
            world.Set(second, new SkinnedMeshData(other, other, other, match, false, false, 0)); // malformed dual geometry still reported
            world.Set(second, new ClipPlaybackData(match, false, true, 1, 0));
            Check(Affected(world, ids).SequenceEqual(new[] { world.GetUuid(first), world.GetUuid(second) }));
            for (int i = 2; i < World.MaxObjects; i++) {
                var e = world.CreateEntity("Object" + i);
                if (i % 3 == 0) world.Set(e, new StaticMeshData(other, i % 2 == 0 ? match : other, false, false, 0));
                else if (i % 3 == 1) world.Set(e, new SkinnedMeshData(other, match, other, other, true, true, 1));
                else world.Set(e, new ClipPlaybackData(match, false, true, 1, 0));
            }
            Check(Affected(world, ids).SequenceEqual(Oracle(world, ids)));
            var snapshot = world.CaptureSnapshot(); Guid firstId = world.GetUuid(first); world.Destroy(first);
            var reused = world.CreateEntity("Reused", firstId); world.Set(reused, new StaticMeshData(match, other, false, false, 0));
            var current = Affected(world, ids); Check(current.SequenceEqual(Oracle(world, ids)) && current[^1] == firstId);
            world.RestoreSnapshot(snapshot); Check(Affected(world, ids).SequenceEqual(Oracle(world, ids)));
            Check(Affected(world, []).Length == 0);
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
