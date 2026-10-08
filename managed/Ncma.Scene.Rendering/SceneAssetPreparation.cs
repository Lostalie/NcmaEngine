using Ncma.Assets;
using Ncma.Assets.Runtime;
using Ncma.Runtime;

namespace Ncma.Scene.Rendering;

// Explicit OFF-frame startup/refresh. The prepared lease may be shared by Edit and Play, never a live World.
public sealed class PreparedSceneAssetLease : IDisposable
{
    private RuntimeAssetLease? _lease;
    public PreparedSceneAssets Metadata { get; }
    public IReadOnlyList<RuntimeAssetDiagnostic> Diagnostics => Assets.Diagnostics;
    public RuntimeAssetLease Assets => _lease ?? throw new ObjectDisposedException(nameof(PreparedSceneAssetLease));
    public PreparedSceneAssetLease AcquireLease()
    {
        var lease = Assets.AcquireLease();
        try { return new(lease); } catch { lease.Dispose(); throw; }
    }
    public PreparedSceneAssetLease(RuntimeAssetLease assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        var info = assets.List().Select(asset => asset switch {
            RuntimeMeshAsset mesh => new SceneAssetInfo(mesh.Id, mesh.Kind, mesh.Generation, mesh.ContentHash, mesh.MaterialSlots,
                mesh.SkeletonId, mesh.ModelId, mesh.BoundsMin, mesh.BoundsMax),
            RuntimeMaterialSetAsset set => new SceneAssetInfo(set.Id, set.Kind, set.Generation, set.ContentHash, set.SlotCount),
            // A StaticMesh root is a model manifest, never implicitly its first mesh.
            RuntimeDataAsset model when model.Kind == AssetKind.StaticMesh => null,
            RuntimeDataAsset data => new SceneAssetInfo(data.Id, data.Kind, data.Generation, data.ContentHash, SkeletonId: data.SkeletonId, CharacterId: data.ModelId),
            _ => new SceneAssetInfo(asset.Id, asset.Kind, asset.Generation, asset.ContentHash) }).Where(v => v is not null).Select(v => v!);
        Metadata = new(info); _lease = assets;
    }
    public void Dispose() { _lease?.Dispose(); _lease = null; }
}
public static class SceneAssetPreparation
{
    private static readonly ComponentRegistry Registry = RenderComponentRegistry.CreateRegistry();
    public static AssetRef[] References(SceneDocumentSnapshot scene)
    {
        var refs = new HashSet<AssetRef>();
        foreach (var obj in scene.Objects)
            foreach (var component in obj.Components)
                switch (component.TypeId)
                {
                    case StaticMeshData.TypeId:
                        var mesh = Registry.Decode<StaticMeshData>(component); Add(mesh.MeshId, AssetKind.StaticMesh); Add(mesh.MaterialSetId, AssetKind.MaterialSet); break;
                    case SkinnedMeshData.TypeId:
                        var skin = Registry.Decode<SkinnedMeshData>(component); Add(skin.CharacterId, AssetKind.Character); Add(skin.MeshId, AssetKind.SkinnedMesh); Add(skin.SkeletonId, AssetKind.Skeleton); Add(skin.MaterialSetId, AssetKind.MaterialSet); break;
                    case MaterialOverrideData.TypeId:
                        var material = Registry.Decode<MaterialOverrideData>(component); if (material.MaterialId != Guid.Empty) Add(material.MaterialId, AssetKind.Material); break;
                    case Ncma.Animation.ClipPlaybackData.TypeId:
                        Add(Registry.Decode<Ncma.Animation.ClipPlaybackData>(component).ClipId, AssetKind.Clip); break;
                    case Ncma.Animation.AnimatorData.TypeId:
                        var animator = Registry.Decode<Ncma.Animation.AnimatorData>(component); Add(animator.GraphId, AssetKind.AnimationGraph); Add(animator.SkeletonId, AssetKind.Skeleton); break;
                    case Ncma.Animation.ActionDefinitionData.TypeId:
                        var action=Registry.Decode<Ncma.Animation.ActionDefinitionData>(component);
                        foreach(Guid id in new[]{action.IdleClip,action.RunClip,action.AttackClip,action.DodgeClip})Add(id,AssetKind.Clip);break;
                }
        return refs.OrderBy(r => r.Id.Value).ThenBy(r => r.ExpectedKind).ToArray();
        void Add(Guid id, AssetKind kind) { if (refs.Count >= RuntimeAssetLoader.MaxRequired && !refs.Contains(new(new(id), kind))) throw new ArgumentException("Scene asset reference budget."); refs.Add(new(new(id), kind)); }
    }
    // Explicit off-frame packaging of committed generations. Dynamic roots are host/author supplied,
    // never guessed from script strings. Cold source rebuild and Prefab runtime spawn remain absent.
    public static byte[] CreateRuntimePackage(string root, Guid project, SceneDocumentSnapshot scene, IEnumerable<AssetRef> dynamicRoots)
    {
        ArgumentNullException.ThrowIfNull(dynamicRoots);
        var additional = dynamicRoots.Take(RuntimeAssetLoader.MaxRequired + 1).ToArray();
        if (additional.Length > RuntimeAssetLoader.MaxRequired) throw new ArgumentException("Dynamic asset root budget.");
        using var prepared = RuntimeAssetLoader.Prepare(root, project, References(scene).Concat(additional), true);
        using var lease = prepared.AcquireLease();
        using var view = new PreparedSceneAssetLease(lease.AcquireLease());
        _ = SceneRenderValidation.Inspect(scene, view.Metadata, true);
        ValidateAnimators(scene,lease);
        foreach (var reference in References(scene)) if (reference.ExpectedKind is AssetKind.StaticMesh or AssetKind.SkinnedMesh)
            _ = lease.RequireMesh(reference.Id.Value, reference.ExpectedKind);
        return RuntimeAssetPackage.Encode(lease);
    }
    public static PreparedSceneAssetLease Prepare(string root, Guid project, SceneDocumentSnapshot scene, bool strictMissing, string? package = null)
    {
        using var prepared = package is null ? RuntimeAssetLoader.Prepare(root, project, References(scene), strictMissing) :
            RuntimeAssetPackage.Prepare(root, package, project, References(scene)); // Explicit package never falls back to authoring files.
        var lease = prepared.AcquireLease();
        try
        {
            var result = new PreparedSceneAssetLease(lease);
            _ = SceneRenderValidation.Inspect(scene, result.Metadata, strictMissing);
            ValidateAnimators(scene,lease);
            // A present model-root reference is malformed even in Editor's missing-resource mode.
            foreach (var reference in References(scene)) if (reference.ExpectedKind is AssetKind.StaticMesh or AssetKind.SkinnedMesh &&
                lease.TryResolve(reference.Id.Value, reference.ExpectedKind, out _)) _ = lease.RequireMesh(reference.Id.Value, reference.ExpectedKind);
            return result;
        }
        catch { lease.Dispose(); throw; }
    }
    private static void ValidateAnimators(SceneDocumentSnapshot scene,RuntimeAssetLease lease)
    {
        foreach (var obj in scene.Objects) foreach(var component in obj.Components.Where(c=>c.TypeId==Ncma.Animation.AnimatorData.TypeId)) {
            var binding=Registry.Decode<Ncma.Animation.AnimatorData>(component);
            if (lease.TryResolve(binding.GraphId,AssetKind.AnimationGraph,out var graph) && ((RuntimeAnimationGraphAsset)graph!).CopyDefinition().SkeletonId!=binding.SkeletonId)
                throw new ArgumentException("Animator graph skeleton binding mismatch.");
        }
    }
}
