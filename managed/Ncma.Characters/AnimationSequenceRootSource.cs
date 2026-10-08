using Ncma.Animation;
using Ncma.Assets;
using Ncma.Assets.Runtime;
namespace Ncma.Characters;

// Independent numerical intentions only; no Character, World, clock, physics or callback authority.
// The caller retains the exact immutable asset publication for this prepared source's lifetime.
public sealed class AnimationSequenceRootSource:IAnimationSequenceRootSource
{
    private readonly GraphRootMotionRecipe _recipe;
    public Guid GraphId {get;}
    public Guid SkeletonId {get;}
    public string GraphContentHash {get;}
    public ulong ResourceGeneration {get;}
    public int RootBoneIndex {get;}
    public AnimationSequenceRootSource(AnimationProgram program,RuntimeAssetLease assets,int rootBoneIndex=0)
    {
        var graph=(RuntimeAnimationGraphAsset)assets.Require(program.AssetId,AssetKind.AnimationGraph);
        var rig=(RuntimeDataAsset)assets.Require(program.SkeletonId,AssetKind.Skeleton);
        if(graph.ContentHash!=program.ContentHash||rig.Generation!=program.ResourceGeneration)throw new ArgumentException("Exact graph/root publication.");
        GraphId=program.AssetId;SkeletonId=program.SkeletonId;GraphContentHash=program.ContentHash;ResourceGeneration=program.ResourceGeneration;RootBoneIndex=rootBoneIndex;
        var skeleton=ModelPayloadCodec.DecodeSkeleton(rig.CopyData());var tracks=new Dictionary<Guid,RootMotionTrack>();int keys=0;RootMotionTrack? anchor=null;
        foreach(Guid id in AnimationGraphValidation.ClipIds(graph.CopyDefinition())) {
            if(tracks.Count>=128)throw new ArgumentException("Independent root track budget.");
            var clip=(RuntimeDataAsset)assets.Require(id,AssetKind.Clip);if(clip.ModelId!=rig.ModelId||clip.SkeletonId!=rig.Id||clip.Generation!=rig.Generation)throw new ArgumentException("Exact root clip closure.");
            var track=new RootMotionTrack(skeleton,ModelPayloadCodec.DecodeClip(clip.CopyData()),rootBoneIndex);keys=checked(keys+track.KeyCount);if(keys>262144)throw new ArgumentException("Independent root key budget.");
            if(anchor is not null){ReadOnlySpan<System.Numerics.Matrix4x4> a=[anchor.InitialPlanar],b=[track.InitialPlanar];var x=System.Runtime.InteropServices.MemoryMarshal.Cast<System.Numerics.Matrix4x4,float>(a);var y=System.Runtime.InteropServices.MemoryMarshal.Cast<System.Numerics.Matrix4x4,float>(b);for(int n=0;n<x.Length;n++)if(Math.Abs(x[n]-y[n])>1e-5)throw new ArgumentException("Shared root anchor required.");}
            anchor??=track;tracks.Add(id,track);
        }
        _recipe=new(tracks,program.MaximumPlanInstructions);
    }
    public AnimationSequenceRootIntent Evaluate(ReadOnlySpan<AnimationPoseInstruction> plan,int output){var delta=_recipe.Evaluate(plan,output);return new(delta.Translation,delta.Yaw);}
}
