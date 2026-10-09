using System.Numerics;
namespace Ncma.Animation;
using Vector3=System.Numerics.Vector3;

// Trusted pinned numeric provider only. No World, files, callbacks/events or clock ownership.
public readonly record struct AnimationLocalTransform(Vector3 Position,Quaternion Rotation,float Scale);
public interface IAnimationPoseSnapshotSource
{
    Guid GraphId {get;}
    Guid SkeletonId {get;}
    string GraphContentHash {get;}
    ulong ResourceGeneration {get;}
    int BoneCount {get;}
    void Evaluate(ReadOnlySpan<AnimationPoseInstruction> plan,int output,ReadOnlySpan<AnimationLocalTransform> frozen,ulong frozenGeneration,Span<AnimationLocalTransform> destination);
}
public readonly record struct AnimationSequenceRootIntent(Vector3 Translation,float Yaw);
public interface IAnimationSequenceRootSource
{
    Guid GraphId {get;}
    Guid SkeletonId {get;}
    string GraphContentHash {get;}
    ulong ResourceGeneration {get;}
    int RootBoneIndex {get;}
    AnimationSequenceRootIntent Evaluate(ReadOnlySpan<AnimationPoseInstruction> plan,int output,ReadOnlySpan<MontageInterval> intervals=default,double fixedDelta=1d/60);
}
