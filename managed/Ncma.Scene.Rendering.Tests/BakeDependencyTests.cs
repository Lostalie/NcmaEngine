using Ncma.Assets;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static partial class Program
{
    private static void RegisterBakeDependencyTests(List<(string Name, Action Run)> tests)
    {
        tests.Add(("E6 baked asset closure binds exact held generation and owns copied output", () => {
            var f = new AssetFixture(); var d = f.Scene(); var image = d.BakeRuntime(d.Revision).Image;
            using var pins = SceneAssetPreparation.Prepare(f.Root, f.Project, image.CopySnapshot(), true);
            var a = SceneAssetPreparation.ResolveDependencies(image.CopySnapshot(), pins);
            using var borrowed = pins.AcquireLease(); var b = SceneAssetPreparation.ResolveDependencies(image.CopySnapshot(), borrowed);
            Check(a.ContentHash == b.ContentHash && a.LeaseIdentity == b.LeaseIdentity && a.ProjectId == f.Project);
            Check(a.CopyMembers().Length == 3 && a.CopyMembers().All(m => m.Generation == 1));
            Check(a.CopyRoots().Any(r => r.Id.Value == f.Mesh && r.ExpectedKind == AssetKind.StaticMesh));
            var copy = a.CopyMembers(); copy[0] = copy[0] with { ContentHash = new string('0',64) };
            Check(a.CopyMembers()[0].ContentHash != copy[0].ContentHash);
            using var replacement = SceneAssetPreparation.Prepare(f.Root, f.Project, image.CopySnapshot(), true);
            AssetReject(() => a.RequireCurrent(replacement)); a.RequireCurrent(borrowed);
            pins.Dispose(); a.RequireCurrent(borrowed); borrowed.Dispose(); AssetReject(() => a.RequireCurrent(borrowed));
        }));
        tests.Add(("E6 lax missing assets cannot pass runtime closure and rejection releases no foreign lease", () => {
            var f = new AssetFixture(); var d = f.Scene();
            var meshObject = d.GetObjectIds().First(id => d.HasComponent<StaticMeshData>(id));
            d.SetComponent(meshObject, d.GetComponent<StaticMeshData>(meshObject) with { MeshId = Guid.NewGuid() });
            using var pins = SceneAssetPreparation.Prepare(f.Root, f.Project, d.CaptureSnapshot(), false);
            var identity = pins.Assets.Identity;
            AssetReject(() => SceneAssetPreparation.ResolveDependencies(d.CaptureSnapshot(), pins));
            Check(pins.Assets.Identity == identity);
        }));
        tests.Add(("E6 asset closure changes with an explicit new immutable generation", () => {
            var f = new AssetFixture(); var d = f.Scene();
            string firstHash;
            using (var pins = SceneAssetPreparation.Prepare(f.Root, f.Project, d.CaptureSnapshot(), true))
                firstHash = SceneAssetPreparation.ResolveDependencies(d.CaptureSnapshot(), pins).ContentHash;
            f.WriteModel(2, 2);
            using var next = SceneAssetPreparation.Prepare(f.Root, f.Project, d.CaptureSnapshot(), true);
            var closure = SceneAssetPreparation.ResolveDependencies(d.CaptureSnapshot(), next);
            Check(firstHash != closure.ContentHash && closure.CopyMembers().All(m => m.Generation == 2));
        }));
    }
}
