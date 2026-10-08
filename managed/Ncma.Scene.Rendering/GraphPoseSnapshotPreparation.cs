using Ncma.Animation;
using Ncma.Assets;
using Ncma.Assets.Runtime;
namespace Ncma.Scene.Rendering;

public static class GraphPoseSnapshotPreparation
{
    // Off-frame trusted lease adapter. No metadata supplied by a request stands in for NCA ownership.
    public static GraphPoseSnapshotSource Prepare(AnimationProgram program,RuntimeAssetLease assets)
    {
        var graph=(RuntimeAnimationGraphAsset)assets.Require(program.AssetId,AssetKind.AnimationGraph);
        var rig=(RuntimeDataAsset)assets.Require(program.SkeletonId,AssetKind.Skeleton);
        if(graph.ContentHash!=program.ContentHash||rig.Generation!=program.ResourceGeneration)throw new ArgumentException("Exact graph/rig publication for pose cache.");
        var clips=new Dictionary<Guid,ClipPayload>();
        foreach(Guid id in AnimationGraphValidation.ClipIds(graph.CopyDefinition())) {
            var clip=(RuntimeDataAsset)assets.Require(id,AssetKind.Clip);if(clip.SkeletonId!=rig.Id||clip.ModelId!=rig.ModelId||clip.Generation!=rig.Generation)throw new ArgumentException("Exact same-generation pose clip closure.");clips.Add(id,ModelPayloadCodec.DecodeClip(clip.CopyData()));
        }
        return new(program,ModelPayloadCodec.DecodeSkeleton(rig.CopyData()),clips);
    }
}
