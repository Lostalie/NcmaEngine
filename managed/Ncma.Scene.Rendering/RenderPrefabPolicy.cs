using Ncma.Assets;
using Ncma.Runtime;
using Ncma.Scene.Prefabs;

namespace Ncma.Scene.Rendering;

// Explicit host policy for persistent render authoring fields; never a live GPU/clock lease.
public static class RenderPrefabPolicy
{
    public static PrefabPolicy Create(ComponentRegistry components,Func<BehaviourBindingData,bool>? trustedBinding=null)=>
        new PrefabPolicy(components,trustedBinding,SceneRenderValidation.RequireComposition)
            .AllowComponent(StaticMeshData.TypeId,1,[Asset("/meshId",AssetKind.StaticMesh),Asset("/materialSetId",AssetKind.MaterialSet)])
            .AllowComponent(SkinnedMeshData.TypeId,1,[Asset("/characterId",AssetKind.Character),Asset("/meshId",AssetKind.SkinnedMesh),Asset("/skeletonId",AssetKind.Skeleton),Asset("/materialSetId",AssetKind.MaterialSet)])
            .AllowComponent(Ncma.Animation.ClipPlaybackData.TypeId,1,[Asset("/clipId",AssetKind.Clip)])
            .AllowComponent(CameraData.TypeId,1,[])
            .AllowComponent(DirectionalLightData.TypeId,1,[])
            .AllowComponent(MaterialOverrideData.TypeId,1,[Asset("/materialId",AssetKind.Material,true)]);
    private static PrefabReferenceField Asset(string path,AssetKind kind,bool optional=false)=>new(path,PrefabReferenceRole.Asset,kind.ToString(),optional);
}
