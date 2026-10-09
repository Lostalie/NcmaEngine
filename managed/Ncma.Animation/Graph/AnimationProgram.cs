using System.Security.Cryptography;

namespace Ncma.Animation;

// Copied preparation metadata, not a claim that assets are pinned or native resources exist.
public readonly record struct AnimationClipDescriptor(Guid Id, Guid SkeletonId, ulong Generation, double Duration);
public sealed class AnimationProgram
{
    internal sealed record Node(Guid Id, AnimationNodeKind Kind, int A, int B, int Parameter,
        int ScalarParameter, Guid Clip, double Duration, bool Loop, double Speed, double Weight,Space? Space=null,AnimationLayerDefinition? Layer=null,Guid SlotId=default,bool PlayOnStart=false);
    internal sealed record Space(BlendSpaceProgram Weights,BlendSpaceSample[] Samples,double[] Durations,int X,int Y,int PhaseLeader);
    internal sealed record Condition(int Parameter, AnimationComparison Comparison, double Value);
    internal sealed record Transition(Guid Id, int From, int To, double Duration, double? ExitTime, Condition[] Conditions);
    internal readonly Node[] Nodes;
    internal readonly AnimationParameter[] Parameters;
    internal readonly Guid[] States;
    internal readonly int[] StateRoots, StatePrimarySources;
    internal readonly Transition[][] Transitions;
    internal readonly Dictionary<Guid, int> ParameterIndices;
    private readonly Dictionary<Guid,AnimationLayerBinding> _layers=[];
    private readonly Dictionary<Guid,double> _clipDurations;
    private readonly AnimationCacheLifetime[] _cacheLifetimes;
    private readonly Dictionary<Guid,Guid> _slotNodes=[];
    private readonly Dictionary<Guid,AnimationMontageSection[]> _slotSections=[];
    public AnimationMontageProgram? Montage {get;}
    internal readonly int Output, Entry, Machine;
    internal readonly Dictionary<Guid,AnimationEventMarker[]> EventTracks;
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
    public int MaximumPlanInstructions => 3 * NodeCount + 1+10*Nodes.Count(n=>n.Space is not null)+_layers.Count;
    public bool HasLayers=>_layers.Count>0;
    private AnimationProgram(AnimationGraphDefinition d, IReadOnlyList<AnimationClipDescriptor> clips, ulong generation,
        IReadOnlyList<AnimationEventMarker> events,AnimationSkeletonDescriptor? skeleton)
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
        var clipMap = clips.ToDictionary(c => c.Id);_clipDurations=clips.ToDictionary(c=>c.Id,c=>c.Duration); var input = d.Links.ToDictionary(l => (l.To, l.ToPin), l => ids[l.From]);
        if(d.Montage is{} montage){var required=montage.Sections.Select(s=>s.ClipId).ToHashSet();Montage=new(montage,generation,clips.Where(c=>required.Contains(c.Id)).ToArray());foreach(var n in d.Nodes.Where(n=>n.Kind==AnimationNodeKind.Slot))_slotNodes.Add(n.Id,n.SlotId);foreach(var group in montage.Sections.GroupBy(s=>s.SlotId))_slotSections.Add(group.Key,group.ToArray());}
        foreach(var n in d.Nodes.Where(n=>n.Layer is not null)){
            var layer=n.Layer!;if(skeleton is null)throw new ArgumentException("Exact skeleton layout metadata required for layers.");
            var mask=new AnimationBoneMaskProgram(layer.Mask,skeleton.Id,skeleton.ContentHash,skeleton.Bones);var weights=new float[mask.BoneCount];mask.CopyWeights(weights);
            if(layer.ReferenceClip!=Guid.Empty&&layer.ReferenceTime>clipMap[layer.ReferenceClip].Duration)throw new ArgumentException("Reference time exceeds actual pinned Clip duration.");
            _layers.Add(n.Id,new(n.Id,layer with{Mask=mask.CopyDefinition()},weights));
        }
        int Pin(Guid id, string pin) => input.TryGetValue((id, pin), out int index) ? index : -1;
        int Scalar(Guid id, string pin) { int source = Pin(id, pin); return source < 0 ? -1 : ParameterIndices[d.Nodes[source].ParameterId]; }
        Space? PrepareSpace(AnimationGraphNode n){if(n.BlendSpace is not{} s)return null;var weights=new BlendSpaceProgram(s);var samples=weights.CopyDefinition().Samples;
            int leader=s.SyncGroup==Guid.Empty?ids[n.Id]:d.Nodes.Where(other=>other.BlendSpace?.SyncGroup==s.SyncGroup).Select(other=>other.Id).Order().Select(id=>ids[id]).First();
            return new(weights,samples,samples.Select(sample=>clipMap[sample.ClipId].Duration).ToArray(),ParameterIndices[s.AxisX.ParameterId],s.AxisY is{} y?ParameterIndices[y.ParameterId]:-1,leader);}
        Nodes = d.Nodes.Select(n => new Node(n.Id, n.Kind, Pin(n.Id, n.Kind is AnimationNodeKind.Output or AnimationNodeKind.CachePose or AnimationNodeKind.Slot ? "pose" : "a"),
            Pin(n.Id, "b"), n.Kind == AnimationNodeKind.Parameter ? ParameterIndices[n.ParameterId] : -1,
            Scalar(n.Id, n.Kind is AnimationNodeKind.Clip or AnimationNodeKind.BlendSpace ? "speed" : "weight"), n.ClipId,
            n.Kind == AnimationNodeKind.Clip ? clipMap[n.ClipId].Duration : n.BlendSpace?.CycleSeconds??0, n.Loop, n.Speed, n.Weight,PrepareSpace(n),n.Layer,n.SlotId,n.PlayOnStart)).ToArray();
        // Precompiled lifetime of named cache aliases in the static DAG; no cross-state/instance cache storage.
        _cacheLifetimes=d.Nodes.Where(n=>n.Kind==AnimationNodeKind.CachePose).Select(n=>{
            var consumers=d.Links.Where(l=>l.From==n.Id).Select(l=>ids[l.To]).Concat(d.States.Where(s=>s.PoseNode==n.Id).Select(_=>ids[d.Nodes.Single(v=>v.Kind==AnimationNodeKind.StateMachine).Id])).ToArray();
            return new AnimationCacheLifetime(n.Id,ids[n.Id],consumers.Max(),consumers.Length);
        }).ToArray();
        Output = Array.FindIndex(Nodes, n => n.Kind == AnimationNodeKind.Output);
        (EventTracks, EventContentHash) = AnimationEventCompilation.Compile(clips, events);
        Machine = Array.FindIndex(Nodes, n => n.Kind == AnimationNodeKind.StateMachine);
        States = d.States.Select(s => s.Id).ToArray(); StateRoots = d.States.Select(s => ids[s.PoseNode]).ToArray();
        StatePrimarySources = StateRoots.Select(Primary).ToArray(); Entry = Array.IndexOf(States, d.EntryState);
        var stateIds = States.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        Transitions = States.Select(id => d.Transitions.Where(t => t.From == id).OrderBy(t => t.Priority).ThenBy(t => t.Id)
            .Select(t => new Transition(t.Id, stateIds[t.From], stateIds[t.To], t.Duration, t.ExitTime,
                t.Conditions.Select(c => new Condition(ParameterIndices[c.ParameterId], c.Comparison, Parameters[ParameterIndices[c.ParameterId]].Kind switch {
                    AnimationParameterKind.Float => c.FloatValue, AnimationParameterKind.Int => c.IntValue, _ => c.BoolValue ? 1 : 0
                })).ToArray())).ToArray()).ToArray();
        int Primary(int node) => Nodes[node].Kind is AnimationNodeKind.Clip or AnimationNodeKind.BlendSpace ? node : Primary(Nodes[node].A);
    }
    public static AnimationProgram Compile(AnimationGraphDefinition definition, ulong skeletonGeneration,
        IReadOnlyList<AnimationClipDescriptor> clips, IReadOnlyList<AnimationEventMarker>? events = null,AnimationSkeletonDescriptor? skeleton=null)
    {
        ArgumentNullException.ThrowIfNull(clips);
        var copy = AnimationGraphCodec.Decode(AnimationGraphCodec.Encode(definition));
        if (skeletonGeneration == 0 || clips.Count > AnimationGraphCodec.MaxClipDependencies) throw new ArgumentException("Bounded committed resource generation required.");
        var required = AnimationGraphValidation.Dependencies(copy).Where(d => !d.IsSkeleton).Select(d => d.Id).ToHashSet();
        var seen = new HashSet<Guid>();
        foreach (var c in clips) {
            if (!required.Contains(c.Id) || !seen.Add(c.Id) || c.SkeletonId != copy.SkeletonId || c.Generation != skeletonGeneration ||
                !double.IsFinite(c.Duration) || c.Duration is < .001 or > 600)
                throw new AnimationGraphValidationException("clip_metadata", c.Id, "Exact same-generation skeleton/clip metadata required.");
        }
        if (!seen.SetEquals(required)) throw new AnimationGraphValidationException("clip_missing", copy.AssetId, "Required clip metadata is missing.");
        if(events is not null&&events.Count>AnimationGraphCodec.MaxEvents)throw new ArgumentException("Event metadata budget.");
        return new(copy, clips.ToArray(), skeletonGeneration, events is null?copy.Events:copy.Events.Concat(events).ToArray(),skeleton);
    }
    public IReadOnlyList<AnimationParameter> DescribeParameters() => Array.AsReadOnly((AnimationParameter[])Parameters.Clone());
    public BlendSpaceDefinition[] CopyBlendSpaces()=>Nodes.Where(n=>n.Space is not null).Select(n=>n.Space!.Weights.CopyDefinition()).ToArray();
    public (Guid NodeId,BlendSpaceDefinition Definition)[] CopyBlendSpaceNodes()=>Nodes.Where(n=>n.Space is not null).Select(n=>(n.Id,n.Space!.Weights.CopyDefinition())).ToArray();
    public AnimationLayerBinding[] CopyLayers()=>_layers.Values.OrderBy(l=>l.NodeId).Select(l=>l with{Definition=l.Definition with{Mask=l.Definition.Mask with{Bones=l.Definition.Mask.Bones.ToArray()}},Weights=l.Weights.ToArray()}).ToArray();
    public double ClipDuration(Guid id)=>_clipDurations.TryGetValue(id,out var duration)?duration:throw new ArgumentException("Exact bound Clip required.");
    public AnimationCacheLifetime[] CopyCacheLifetimes()=>_cacheLifetimes.ToArray();
    public Guid SlotNodeId(Guid slot){foreach(var pair in _slotNodes)if(pair.Value==slot)return pair.Key;throw new ArgumentException("Exact compiled Slot required.");}
    public void ValidateSlotPose(AnimationPoseInstruction row,AnimationPoseInstruction sample)
    {
        if(row.Operation!=AnimationPoseOperation.Slot||!_slotNodes.TryGetValue(row.NodeId,out var slot)||slot!=row.SlotId||sample.Operation!=AnimationPoseOperation.Clip||sample.NodeId!=row.NodeId||sample.Previous!=sample.Current||sample.Loop||sample.Duration!=ClipDuration(sample.ClipId))throw new ArgumentException("Exact bound Slot/static sample required.");
        foreach(var section in _slotSections[slot])if(section.ClipId==sample.ClipId&&sample.Current>=section.Start&&sample.Current<=section.End)return;
        throw new ArgumentException("Slot sample must belong to an authored Section interval.");
    }
}

public sealed record AnimationGraphDiagnostic(string Code, Guid Subject, string Field, string Expected, string Actual);
public static class AnimationGraphDiagnostics
{
    // Sanitized and bounded. No raw exception/filename/stack or model-generated repair code.
    public static IReadOnlyList<AnimationGraphDiagnostic> Validate(AnimationGraphDefinition definition)
    {
        try { AnimationGraphValidation.Validate(definition); return Array.Empty<AnimationGraphDiagnostic>(); }
        catch (AnimationGraphValidationException e) { return Array.AsReadOnly(new[] { new AnimationGraphDiagnostic(e.Code, e.Subject, "graph", "valid_graph_v5", "rejected") }); }
        catch (ArgumentException) { return Array.AsReadOnly(new[] { new AnimationGraphDiagnostic("invalid_data", definition?.AssetId ?? Guid.Empty, "graph", "bounded_valid_data", "rejected") }); }
    }
}
