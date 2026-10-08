using System.Numerics;
namespace Ncma.Animation;

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
