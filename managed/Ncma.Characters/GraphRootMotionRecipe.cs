using Ncma.Animation;

namespace Ncma.Characters;
using Vector3 = System.Numerics.Vector3;

// C1 preparation primitive only. Its caller must pin/validate exact NCA generations;
// this class neither owns clocks nor grants World/solver authority.
internal sealed class GraphRootMotionRecipe
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly Dictionary<Guid, RootMotionTrack> _tracks;
    private readonly RootMotionDelta[] _scratch;
    private readonly Dictionary<Guid,AnimationLayerBinding> _layers;

    internal GraphRootMotionRecipe(IReadOnlyDictionary<Guid, RootMotionTrack> tracks, int maximumInstructions,AnimationProgram? program=null)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        if (tracks.Count is < 1 or > 128 || maximumInstructions is < 1 or > AnimationGraphCodec.MaxPlanInstructions)
            throw new ArgumentException("Bounded graph root recipe required.");
        _tracks = new(tracks.Count);
        int keys = 0, root = -1;
        foreach (var pair in tracks) {
            if (pair.Key == Guid.Empty || pair.Value is null) throw new ArgumentException("Exact clip/root track required.");
            if (root >= 0 && pair.Value.RootIndex != root) throw new ArgumentException("One canonical root index required.");
            root = pair.Value.RootIndex;
            keys = checked(keys + pair.Value.KeyCount);
            if (keys > 262144) throw new ArgumentException("Graph root key budget exceeded.");
            _tracks.Add(pair.Key, pair.Value);
        }
        _scratch = new RootMotionDelta[maximumInstructions];
        _layers=program?.CopyLayers().ToDictionary(l=>l.NodeId)??[];
    }

    // Intervals come from the sole prepared graph token, not a second ClipClock.
    // All rows (including unused branches) must succeed before a result is returned.
    internal RootMotionDelta Evaluate(ReadOnlySpan<AnimationPoseInstruction> plan, int output)
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Graph root owner thread required.");
        if (plan.IsEmpty || plan.Length > _scratch.Length || output < 0 || output >= plan.Length)
            throw new ArgumentException("Bounded graph root output required.");
        AnimationPoseRecipe.Validate(plan,output);
        for (int i = 0; i < plan.Length; i++) {
            var row = plan[i];
            if (row.NodeId == Guid.Empty) throw new ArgumentException("Graph root node identity required.");
            RootMotionDelta result;
            switch (row.Operation) {
                case AnimationPoseOperation.Clip:
                    if (row.CacheGeneration!=0 || !_tracks.TryGetValue(row.ClipId, out var track) || row.Duration != track.Duration ||
                        row.SourceA != -1 || row.SourceB != -1 || row.Weight != 0)
                        throw new ArgumentException("Exact prepared root clip/duration required.");
                    result = track.Extract(new(row.Previous, row.Current), row.Loop);
                    break;
                case AnimationPoseOperation.Blend:
                    if (row.CacheGeneration!=0 || row.ClipId != Guid.Empty || row.Duration != 0 || row.Previous != 0 || row.Current != 0 || row.Loop ||
                        row.SourceA < 0 || row.SourceA >= i || row.SourceB < 0 || row.SourceB >= i ||
                        !float.IsFinite(row.Weight) || row.Weight is < 0 or > 1)
                        throw new ArgumentException("Backward bounded graph root blend required.");
                    var a = _scratch[row.SourceA]; var b = _scratch[row.SourceB];
                    // Same shortest-path yaw convention as unit-quaternion pose blending.
                    float yaw = MathF.IEEERemainder(a.Yaw + MathF.IEEERemainder(b.Yaw - a.Yaw, 2 * MathF.PI) * row.Weight, 2 * MathF.PI);
                    result = new(Vector3.Lerp(a.Translation, b.Translation, row.Weight), yaw);
                    break;
                case AnimationPoseOperation.RootSource:
                    if(row.CacheGeneration!=0||row.ClipId!=Guid.Empty||row.Duration!=0||row.Previous!=0||row.Current!=0||row.Loop||row.Weight!=0||row.SourceA<0||row.SourceA>=i||row.SourceB<0||row.SourceB>=i||plan[row.SourceB].Operation!=AnimationPoseOperation.Clip)throw new ArgumentException("Exact backward BlendSpace primary root source required.");
                    result=_scratch[row.SourceB];break;
                case AnimationPoseOperation.LayerOverride:
                case AnimationPoseOperation.LayerAdditive:
                    // Layers cannot acquire movement authority; both overlay/reference rows were fully validated above.
                    if(!_layers.TryGetValue(row.NodeId,out var layer)||row.Operation==AnimationPoseOperation.LayerAdditive&&(plan[row.SourceC].ClipId!=layer.Definition.ReferenceClip||plan[row.SourceC].Current!=layer.Definition.ReferenceTime))throw new ArgumentException("Exact prepared layer for base-only root.");
                    result=_scratch[row.SourceA];break;
                case AnimationPoseOperation.Frozen:
                    if(row.CacheGeneration==0||row.CacheGeneration>9007199254740991UL||row.ClipId!=Guid.Empty||row.Previous!=0||row.Current!=0||row.Duration!=0||row.Loop||row.SourceA!=-1||row.SourceB!=-1||row.Weight!=0)throw new ArgumentException("Exact frozen pose row required.");
                    // Frozen visual source owns no advancing clip interval; only the target contributes root motion.
                    result=default;break;
                default: throw new ArgumentException("Unknown graph root operation.");
            }
            if (!float.IsFinite(result.Translation.LengthSquared()) || result.Translation.LengthSquared() > 1e6f ||
                MathF.Abs(result.Translation.Y) > 1e-6f || !float.IsFinite(result.Yaw) || MathF.Abs(result.Yaw) > MathF.PI)
                throw new ArgumentException("Graph root movement budget exceeded.");
            _scratch[i] = result;
        }
        return _scratch[output];
    }
}
