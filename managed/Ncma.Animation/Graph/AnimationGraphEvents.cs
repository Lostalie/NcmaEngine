using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ncma.Animation;

// Copied authored data; marker text never grants callback, damage or resource authority.
public sealed record AnimationEventMarker([property:JsonRequired] Guid Id,[property:JsonRequired] Guid ClipId,
    [property:JsonRequired] double Time,[property:JsonRequired] string Name);
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
    internal static (Dictionary<Guid,AnimationEventMarker[]> Tracks, string Hash) Compile(IReadOnlyList<AnimationClipDescriptor> descriptors,
        IReadOnlyList<AnimationEventMarker> input)
    {
        if (input.Count > MaxMarkers) throw new ArgumentException("Animation event marker budget.");
        var clips = descriptors.ToDictionary(c=>c.Id,c=>c.Duration);
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
        foreach(Guid id in clips.Keys)byClip.TryAdd(id,[]);return (byClip,hash);
    }
}
