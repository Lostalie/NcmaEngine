using System.Text.Json.Serialization;
namespace Ncma.Animation;

public sealed record AnimationLayerDefinition([property:JsonRequired]AnimationBoneMask Mask,
    [property:JsonRequired]Guid ReferenceClip,[property:JsonRequired]double ReferenceTime);
public sealed record AnimationSkeletonDescriptor(Guid Id,string ContentHash,AnimationBoneIdentity[] Bones);
public sealed record AnimationLayerBinding(Guid NodeId,AnimationLayerDefinition Definition,float[] Weights);
