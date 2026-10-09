using System.Text.Json.Serialization;

namespace Ncma.Animation;

// Persistent authoring DTOs only. Runtime clocks, World/native handles and executable expressions are absent.
public enum AnimationNodeKind { Clip, Blend, Parameter, StateMachine, Output, BlendSpace, LayerOverride, LayerAdditive, CachePose, Slot }
public enum AnimationParameterKind { Float, Int, Bool, Trigger }
public enum AnimationPinType { Pose, Float, Int, Bool, Trigger }
public enum AnimationComparison { Equal, NotEqual, Greater, GreaterOrEqual, Less, LessOrEqual, Triggered }
public sealed record AnimationParameter(
    [property: JsonRequired] Guid Id, [property: JsonRequired] string Name,
    [property: JsonRequired] AnimationParameterKind Kind,
    [property: JsonRequired] double FloatDefault, [property: JsonRequired] int IntDefault,
    [property: JsonRequired] bool BoolDefault);

// One closed shape; irrelevant kind-specific fields MUST remain neutral, not silently ignored.
public sealed record AnimationGraphNode(
    [property: JsonRequired] Guid Id, [property: JsonRequired] string Name,
    [property: JsonRequired] AnimationNodeKind Kind,
    [property: JsonRequired] double X, [property: JsonRequired] double Y,
    [property: JsonRequired] Guid ClipId, [property: JsonRequired] Guid ParameterId,
    [property: JsonRequired] bool Loop, [property: JsonRequired] double Speed,
    [property: JsonRequired] double Weight)
{
    [JsonRequired] public BlendSpaceDefinition? BlendSpace {get;init;}
    [JsonRequired] public AnimationLayerDefinition? Layer {get;init;}
    [JsonRequired] public Guid SlotId {get;init;}
    [JsonRequired] public bool PlayOnStart {get;init;}
    public static AnimationGraphNode Create(Guid id, string name, AnimationNodeKind kind) =>
        new(id, name, kind, 0, 0, Guid.Empty, Guid.Empty, false, 0, 0);
}
public sealed record AnimationGraphLink(
    [property: JsonRequired] Guid Id, [property: JsonRequired] Guid From,
    [property: JsonRequired] string FromPin, [property: JsonRequired] Guid To,
    [property: JsonRequired] string ToPin);
public sealed record AnimationGraphState(
    [property: JsonRequired] Guid Id, [property: JsonRequired] string Name,
    [property: JsonRequired] Guid PoseNode);
public sealed record AnimationCondition(
    [property: JsonRequired] Guid ParameterId, [property: JsonRequired] AnimationComparison Comparison,
    [property: JsonRequired] double FloatValue, [property: JsonRequired] int IntValue,
    [property: JsonRequired] bool BoolValue);
public sealed record AnimationTransition(
    [property: JsonRequired] Guid Id, [property: JsonRequired] Guid From,
    [property: JsonRequired] Guid To, [property: JsonRequired] int Priority,
    [property: JsonRequired] double Duration, [property: JsonRequired] double? ExitTime,
    [property: JsonRequired] AnimationCondition[] Conditions);
public sealed record AnimationGraphDefinition(
    [property: JsonRequired] int Version, [property: JsonRequired] Guid AssetId,
    [property: JsonRequired] string Name, [property: JsonRequired] Guid SkeletonId,
    [property: JsonRequired] Guid EntryState,
    [property: JsonRequired] AnimationParameter[] Parameters,
    [property: JsonRequired] AnimationGraphNode[] Nodes,
    [property: JsonRequired] AnimationGraphLink[] Links,
    [property: JsonRequired] AnimationGraphState[] States,
    [property: JsonRequired] AnimationTransition[] Transitions)
{
    [JsonRequired] public AnimationEventMarker[] Events { get; init; }=[];
    [JsonRequired] public bool InterruptTransitions { get; init; }
    [JsonRequired] public AnimationMontageDefinition? Montage {get;init;}
}

// Immutable, owned publication. Mutable authoring DTOs never become a live runtime program.
public sealed class AnimationGraphDocument
{
    private readonly byte[] _encoded;
    public Guid AssetId { get; }
    public AnimationGraphDocument(AnimationGraphDefinition definition)
    { _encoded = AnimationGraphCodec.Encode(definition); AssetId = definition.AssetId; }
    public AnimationGraphDefinition CopyDefinition() => AnimationGraphCodec.Decode(_encoded);
    public byte[] CopyBytes() => (byte[])_encoded.Clone();
}
