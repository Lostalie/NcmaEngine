using System.Security.Cryptography;
using System.Text.Json;

namespace Ncma.Animation;

// Trusted, copied compilation input; not a file codec, resource pin or callback authority.
public sealed record AnimationEventMarker(Guid Id, Guid ClipId, double Time, string Name);
public readonly record struct AnimationGraphEvent(Guid InstanceId, Guid GraphId, AnimationStepContext Context,
    ulong Sequence, Guid StateId, Guid NodeId, Guid MarkerId, Guid ClipId, double UnwrappedTime, string Name);
public enum AnimationEvaluationOutcome { Ready, Prepared, Committed, Aborted, PreparationRejected }
public readonly record struct AnimationDebugParameter(Guid Id, AnimationParameterKind Kind, double Value);
public sealed record AnimationGraphDebugFrame(AnimationGraphFrame Frame, ulong CommittedSequence,
    ulong AttemptSequence, AnimationEvaluationOutcome Outcome, bool SnapshotValid, string EventContentHash,
    IReadOnlyList<AnimationDebugParameter> Parameters, IReadOnlyList<AnimationPoseInstruction> Instructions,
    IReadOnlyList<AnimationGraphEvent> Events, bool RootMotionSupported);

internal static class AnimationEventCompilation
{
    internal const int MaxMarkers = 4096, MaxEvents = 256;
    internal static (AnimationEventMarker[][] Tracks, string Hash) Compile(AnimationProgram.Node[] nodes,
        IReadOnlyList<AnimationEventMarker> input)
    {
        if (input.Count > MaxMarkers) throw new ArgumentException("Animation event marker budget.");
        var clips = nodes.Where(n => n.Kind == AnimationNodeKind.Clip).ToDictionaryByClip();
        var seen = new HashSet<Guid>();
        var owned = new AnimationEventMarker[input.Count];
        for (int i = 0; i < input.Count; i++) {
            var m = input[i] ?? throw new ArgumentException("Event marker required.");
            if (m.Id == Guid.Empty || !seen.Add(m.Id) || !clips.TryGetValue(m.ClipId, out double duration) ||
                !double.IsFinite(m.Time) || m.Time <= 0 || m.Time > duration) throw new ArgumentException("Exact clip and distinct bounded event marker required.");
            AnimationGraphCodec.Text(m.Name, 128); owned[i] = m with { };
        }
        string hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(owned.OrderBy(m => m.Id).ToArray())));
        var byClip = owned.GroupBy(m => m.ClipId).ToDictionary(g => g.Key, g => g.OrderBy(m => m.Time).ThenBy(m => m.Id).ToArray());
        return (nodes.Select(n => n.Kind == AnimationNodeKind.Clip && byClip.TryGetValue(n.Clip, out var track) ? track : Array.Empty<AnimationEventMarker>()).ToArray(), hash);
    }
    private static Dictionary<Guid, double> ToDictionaryByClip(this IEnumerable<AnimationProgram.Node> nodes)
    { var result = new Dictionary<Guid, double>(); foreach (var node in nodes) result.TryAdd(node.Clip, node.Duration); return result; }
}
