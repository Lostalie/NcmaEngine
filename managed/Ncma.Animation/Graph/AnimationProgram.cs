using System.Security.Cryptography;

namespace Ncma.Animation;

// Copied preparation metadata, not a claim that assets are pinned or native resources exist.
public readonly record struct AnimationClipDescriptor(Guid Id, Guid SkeletonId, ulong Generation, double Duration);
public sealed class AnimationProgram
{
    internal sealed record Node(Guid Id, AnimationNodeKind Kind, int A, int B, int Parameter,
        int ScalarParameter, Guid Clip, double Duration, bool Loop, double Speed, double Weight);
    internal sealed record Condition(int Parameter, AnimationComparison Comparison, double Value);
    internal sealed record Transition(Guid Id, int From, int To, double Duration, double? ExitTime, Condition[] Conditions);
    internal readonly Node[] Nodes;
    internal readonly AnimationParameter[] Parameters;
    internal readonly Guid[] States;
    internal readonly int[] StateRoots, StatePrimaryClips;
    internal readonly Transition[][] Transitions;
    internal readonly Dictionary<Guid, int> ParameterIndices;
    internal readonly int Output, Entry, Machine;
    internal readonly AnimationEventMarker[][] EventTracks;
    public string EventContentHash { get; }
    public const int MaximumEventsPerQuantum = AnimationEventCompilation.MaxEvents;
    public Guid AssetId { get; }
    public Guid SkeletonId { get; }
    public ulong ResourceGeneration { get; }
    public string ContentHash { get; }
    public bool InterruptTransitions {get;}
    public int NodeCount => Nodes.Length;
    public int ParameterCount => Parameters.Length;
    public int StateCount => States.Length;
    public int MaximumPlanInstructions => 3 * NodeCount + 1;
    private AnimationProgram(AnimationGraphDefinition d, IReadOnlyList<AnimationClipDescriptor> clips, ulong generation,
        IReadOnlyList<AnimationEventMarker> events)
    {
        // Off-frame topological compilation, including state and scalar dependencies.
        var byId = d.Nodes.ToDictionary(n => n.Id); var seenNodes = new HashSet<Guid>(); var ordered = new List<AnimationGraphNode>();
        void Order(Guid id) {
            if (!seenNodes.Add(id)) return;
            foreach (var edge in d.Links.Where(l => l.To == id).OrderBy(l => l.ToPin, StringComparer.Ordinal)) Order(edge.From);
            if (byId[id].Kind == AnimationNodeKind.StateMachine) foreach (var state in d.States.OrderBy(s => s.Id)) Order(state.PoseNode);
            ordered.Add(byId[id]);
        }
        Order(d.Nodes.Single(n => n.Kind == AnimationNodeKind.Output).Id); d = d with { Nodes = ordered.ToArray() };
        AssetId = d.AssetId; SkeletonId = d.SkeletonId; ResourceGeneration = generation;InterruptTransitions=d.InterruptTransitions;
        ContentHash = Convert.ToHexString(SHA256.HashData(AnimationGraphCodec.Encode(d)));
        Parameters = d.Parameters.ToArray(); ParameterIndices = Parameters.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var ids = d.Nodes.Select((n, i) => (n.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var clipMap = clips.ToDictionary(c => c.Id); var input = d.Links.ToDictionary(l => (l.To, l.ToPin), l => ids[l.From]);
        int Pin(Guid id, string pin) => input.TryGetValue((id, pin), out int index) ? index : -1;
        int Scalar(Guid id, string pin) { int source = Pin(id, pin); return source < 0 ? -1 : ParameterIndices[d.Nodes[source].ParameterId]; }
        Nodes = d.Nodes.Select(n => new Node(n.Id, n.Kind, Pin(n.Id, n.Kind == AnimationNodeKind.Output ? "pose" : "a"),
            Pin(n.Id, "b"), n.Kind == AnimationNodeKind.Parameter ? ParameterIndices[n.ParameterId] : -1,
            Scalar(n.Id, n.Kind == AnimationNodeKind.Clip ? "speed" : "weight"), n.ClipId,
            n.Kind == AnimationNodeKind.Clip ? clipMap[n.ClipId].Duration : 0, n.Loop, n.Speed, n.Weight)).ToArray();
        Output = Array.FindIndex(Nodes, n => n.Kind == AnimationNodeKind.Output);
        (EventTracks, EventContentHash) = AnimationEventCompilation.Compile(Nodes, events);
        Machine = Array.FindIndex(Nodes, n => n.Kind == AnimationNodeKind.StateMachine);
        States = d.States.Select(s => s.Id).ToArray(); StateRoots = d.States.Select(s => ids[s.PoseNode]).ToArray();
        StatePrimaryClips = StateRoots.Select(Primary).ToArray(); Entry = Array.IndexOf(States, d.EntryState);
        var stateIds = States.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        Transitions = States.Select(id => d.Transitions.Where(t => t.From == id).OrderBy(t => t.Priority).ThenBy(t => t.Id)
            .Select(t => new Transition(t.Id, stateIds[t.From], stateIds[t.To], t.Duration, t.ExitTime,
                t.Conditions.Select(c => new Condition(ParameterIndices[c.ParameterId], c.Comparison, Parameters[ParameterIndices[c.ParameterId]].Kind switch {
                    AnimationParameterKind.Float => c.FloatValue, AnimationParameterKind.Int => c.IntValue, _ => c.BoolValue ? 1 : 0
                })).ToArray())).ToArray()).ToArray();
        int Primary(int node) => Nodes[node].Kind == AnimationNodeKind.Clip ? node : Primary(Nodes[node].A);
    }
    public static AnimationProgram Compile(AnimationGraphDefinition definition, ulong skeletonGeneration,
        IReadOnlyList<AnimationClipDescriptor> clips, IReadOnlyList<AnimationEventMarker>? events = null)
    {
        ArgumentNullException.ThrowIfNull(clips);
        var copy = AnimationGraphCodec.Decode(AnimationGraphCodec.Encode(definition));
        if (skeletonGeneration == 0 || clips.Count > AnimationGraphCodec.MaxNodes) throw new ArgumentException("Bounded committed resource generation required.");
        var required = AnimationGraphValidation.Dependencies(copy).Where(d => !d.IsSkeleton).Select(d => d.Id).ToHashSet();
        var seen = new HashSet<Guid>();
        foreach (var c in clips) {
            if (!required.Contains(c.Id) || !seen.Add(c.Id) || c.SkeletonId != copy.SkeletonId || c.Generation != skeletonGeneration ||
                !double.IsFinite(c.Duration) || c.Duration is < .001 or > 600)
                throw new AnimationGraphValidationException("clip_metadata", c.Id, "Exact same-generation skeleton/clip metadata required.");
        }
        if (!seen.SetEquals(required)) throw new AnimationGraphValidationException("clip_missing", copy.AssetId, "Required clip metadata is missing.");
        if(events is not null&&events.Count>AnimationGraphCodec.MaxEvents)throw new ArgumentException("Event metadata budget.");
        return new(copy, clips.ToArray(), skeletonGeneration, events is null?copy.Events:copy.Events.Concat(events).ToArray());
    }
    public IReadOnlyList<AnimationParameter> DescribeParameters() => Array.AsReadOnly((AnimationParameter[])Parameters.Clone());
}

public sealed record AnimationGraphDiagnostic(string Code, Guid Subject, string Field, string Expected, string Actual);
public static class AnimationGraphDiagnostics
{
    // Sanitized and bounded. No raw exception/filename/stack or model-generated repair code.
    public static IReadOnlyList<AnimationGraphDiagnostic> Validate(AnimationGraphDefinition definition)
    {
        try { AnimationGraphValidation.Validate(definition); return Array.Empty<AnimationGraphDiagnostic>(); }
        catch (AnimationGraphValidationException e) { return Array.AsReadOnly(new[] { new AnimationGraphDiagnostic(e.Code, e.Subject, "graph", "valid_graph_v2", "rejected") }); }
        catch (ArgumentException) { return Array.AsReadOnly(new[] { new AnimationGraphDiagnostic("invalid_data", definition?.AssetId ?? Guid.Empty, "graph", "bounded_valid_data", "rejected") }); }
    }
}
