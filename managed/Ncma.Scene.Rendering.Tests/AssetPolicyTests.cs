using Ncma.Animation;
using Ncma.Assets;
using Ncma.Editor.Services;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static partial class Program
{
    private static void RegisterAssetPolicyTests(List<(string Name, Action Run)> tests)
    {
        tests.Add(("asset policy: inactive visuals preserve metadata without a lease; graphics remains strict", () => {
            var d = Document(); Geometry(d); byte[] before = d.CaptureBytes();
            var scene = d.CaptureSnapshot();
            Check(SceneAssetPreparation.References(scene, SceneAssetPolicy.Simulation).Length == 0);
            Check(SceneAssetPreparation.References(scene).Length == 2);
            var receipt = SceneAssetPreparation.ResolveDependencies(scene, null, SceneAssetPolicy.Simulation);
            Check(!receipt.PresentationRequired && receipt.Resolution == "not-required" && receipt.CopyMembers().Length == 0);
            receipt.RequireCurrent(null);
            Reject(() => SceneAssetPreparation.ResolveDependencies(scene, null));
            Check(before.SequenceEqual(d.CaptureBytes()));
            d.RemoveComponent<TransformData>(d.GetObjectIds()[0]);
            Reject(() => SceneAssetPreparation.ResolveDependencies(d.CaptureSnapshot(), null, SceneAssetPolicy.Simulation));
        }));
        tests.Add(("asset policy: unused visual packages and descriptors are never opened by simulation", () => {
            var f = new AssetFixture(); var d = f.Scene();
            File.WriteAllText(Path.Combine(f.Root, "assets/Model.fbx.ncmeta"), "corrupt visual descriptor");
            using var pins = SceneAssetPreparation.Prepare(f.Root, f.Project, d.CaptureSnapshot(), true,
                "assets/does-not-exist.ncpak", SceneAssetPolicy.Simulation);
            Check(pins.Policy == SceneAssetPolicy.Simulation && pins.Assets.List().Count == 0 && !Locked(f.GenerationPath));
            Check(SceneAssetPreparation.ResolveDependencies(d.CaptureSnapshot(), pins, SceneAssetPolicy.Simulation).Resolution == "not-required");
            AssetReject(() => SceneAssetPreparation.Prepare(f.Root, f.Project, d.CaptureSnapshot(), true));
        }));
        tests.Add(("asset policy: animation remains required without GPU; unrelated visuals are excluded", () => {
            var d = Document(); Geometry(d); var actor = d.CreateObject("Animator"); d.SetComponent(actor, TransformData.Identity);
            var skin = new SkinnedMeshData(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true, true, uint.MaxValue);
            var animator = new AnimatorData(Guid.NewGuid(), skin.SkeletonId);
            d.SetComponent(actor, skin); d.SetComponent(actor, animator);
            var scene = d.CaptureSnapshot();
            var roots = SceneAssetPreparation.References(scene, SceneAssetPolicy.Simulation);
            Check(roots.Length == 4 && roots.Any(r => r.Id.Value == animator.GraphId && r.ExpectedKind == AssetKind.AnimationGraph));
            Check(roots.All(r => r.ExpectedKind is AssetKind.Character or AssetKind.SkinnedMesh or AssetKind.Skeleton or AssetKind.AnimationGraph));
            Reject(() => SceneAssetPreparation.ResolveDependencies(scene, null, SceneAssetPolicy.Simulation));
            var f = new AssetFixture();
            AssetReject(() => SceneAssetPreparation.Prepare(f.Root, f.Project, scene, true, "assets/missing.ncpak", SceneAssetPolicy.Simulation));
        }));
        tests.Add(("asset policy: policy mismatches reject and receipts distinguish no demand from visual validation", () => {
            var f = new AssetFixture(); var d = f.Scene(); var scene = d.CaptureSnapshot();
            using var pins = SceneAssetPreparation.Prepare(f.Root, f.Project, scene, true);
            Reject(() => SceneAssetPreparation.ResolveDependencies(scene, pins, SceneAssetPolicy.Simulation));
            var verified = SceneAssetPreparation.ResolveDependencies(scene, pins);
            Check(verified.PresentationRequired && verified.Resolution == "resolved");
            using var borrowed = pins.AcquireLease(); Check(borrowed.Policy == pins.Policy); verified.RequireCurrent(borrowed);
            var empty = Document().CaptureSnapshot();
            Check(SceneAssetPreparation.ResolveDependencies(empty, null).ContentHash !=
                SceneAssetPreparation.ResolveDependencies(empty, null, SceneAssetPolicy.Simulation).ContentHash);
            Reject(() => SceneAssetPreparation.References(scene, (SceneAssetPolicy)0));
            Reject(() => new EditorSessionOwner("Invalid policy", assetPolicy: (SceneAssetPolicy)99));
        }));
        tests.Add(("asset policy: Editor CPU and graphical startup obey the same strict dependency rules", () => {
            using var cpu = new EditorSessionOwner("CPU", components: global::Ncma.Runtime.CompositionRegistry.Build(global::Ncma.Scene.Rendering.Generated.ComponentModule.Module),
                validateComposition: SceneRenderValidation.RequireComposition, assetPolicy: SceneAssetPolicy.Simulation);
            Geometry(cpu.Document); byte[] before = cpu.Document.CaptureBytes();
            cpu.StartPlay(); Check(cpu.PlayDependencies!.Assets.Policy == SceneAssetPolicy.Simulation && !cpu.PlayDependencies.Assets.PresentationRequired);
            Check(cpu.PlayRenderAssets is null && cpu.Play is not null); cpu.StopPlay();
            Check(before.SequenceEqual(cpu.Document.CaptureBytes()));
            using var graphical = new EditorSessionOwner("Graphical", components: global::Ncma.Runtime.CompositionRegistry.Build(global::Ncma.Scene.Rendering.Generated.ComponentModule.Module),
                validateComposition: SceneRenderValidation.RequireComposition, assetPolicy: SceneAssetPolicy.PresentationAndSimulation);
            Geometry(graphical.Document); var state = graphical.Edit!.State;
            Reject(() => graphical.StartPlay()); Check(graphical.Play is null && graphical.Edit.State == state);
        }));
    }
}
