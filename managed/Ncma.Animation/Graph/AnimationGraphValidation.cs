namespace Ncma.Animation;

public sealed class AnimationGraphValidationException(string code, Guid subject, string message) : ArgumentException(message)
{
    public string Code { get; } = code;
    public Guid Subject { get; } = subject;
}
public readonly record struct AnimationGraphDependency(Guid Id, bool IsSkeleton);

public static class AnimationGraphValidation
{
    private static void Require(bool valid, string code, Guid subject, string message)
    { if (!valid) throw new AnimationGraphValidationException(code, subject, message); }
    public static void Validate(AnimationGraphDefinition d)
    {
        ArgumentNullException.ThrowIfNull(d);
        Require(d.Version == AnimationGraphCodec.CurrentVersion && d.AssetId != Guid.Empty && d.SkeletonId != Guid.Empty && d.AssetId != d.SkeletonId,
            "identity", d.AssetId, "Strict graph v3 and distinct graph/skeleton UUIDs required; v1/v2 are removed.");
        AnimationGraphCodec.Text(d.Name, 256);
        Require(d.Parameters is not null && d.Nodes is not null && d.Links is not null && d.States is not null && d.Transitions is not null,
            "collections", d.AssetId, "All graph collections are required.");
        // Explicit checks also establish non-null flow analysis after the contract guard.
        var parameters = d.Parameters ?? throw new ArgumentException("Parameters required.");
        var nodes = d.Nodes ?? throw new ArgumentException("Nodes required.");
        var links = d.Links ?? throw new ArgumentException("Links required.");
        var states = d.States ?? throw new ArgumentException("States required.");
        var transitions = d.Transitions ?? throw new ArgumentException("Transitions required.");
        var events=d.Events??throw new ArgumentException("Events required.");
        Require(parameters.Length <= AnimationGraphCodec.MaxParameters && nodes.Length is >= 2 and <= AnimationGraphCodec.MaxNodes &&
            links.Length <= AnimationGraphCodec.MaxLinks && states.Length <= AnimationGraphCodec.MaxStates && transitions.Length <= AnimationGraphCodec.MaxTransitions && events.Length<=AnimationGraphCodec.MaxEvents,
            "budget", d.AssetId, "Animation graph collection budget exceeded.");
        var identities = new HashSet<Guid> { d.AssetId, d.SkeletonId };
        void Identity(Guid id) => Require(id != Guid.Empty && identities.Add(id), "duplicate_identity", id, "Graph elements require distinct persistent UUIDs.");
        var parameterMap = new Dictionary<Guid, AnimationParameter>(); var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in parameters) {
            if (p is null) throw new ArgumentException("Null parameter."); Identity(p.Id); AnimationGraphCodec.Text(p.Name);
            Require(names.Add(p.Name) && Enum.IsDefined(p.Kind), "parameter", p.Id, "Unique parameter name and supported type required.");
            AnimationGraphCodec.Scalar(p.FloatDefault, -1000000, 1000000);
            Require(p.Kind == AnimationParameterKind.Float || p.FloatDefault == 0, "parameter_default", p.Id, "Unexpected float default.");
            Require(p.Kind == AnimationParameterKind.Int || p.IntDefault == 0, "parameter_default", p.Id, "Unexpected int default.");
            Require(p.Kind == AnimationParameterKind.Bool || !p.BoolDefault, "parameter_default", p.Id, "Triggers start unarmed; irrelevant bool defaults are forbidden.");
            parameterMap.Add(p.Id, p);
        }
        var nodeMap = new Dictionary<Guid, AnimationGraphNode>();int spaces=0;
        foreach (var n in nodes) {
            if (n is null) throw new ArgumentException("Null node."); Identity(n.Id); AnimationGraphCodec.Text(n.Name);
            Require(Enum.IsDefined(n.Kind), "node_kind", n.Id, "Unsupported node kind.");
            AnimationGraphCodec.Scalar(n.X, -65536, 65536); AnimationGraphCodec.Scalar(n.Y, -65536, 65536);
            AnimationGraphCodec.Scalar(n.Speed, 0, 8); AnimationGraphCodec.Scalar(n.Weight, 0, 1);
            Require((n.Kind == AnimationNodeKind.Clip ? n.ClipId != Guid.Empty && n.ClipId != d.SkeletonId : n.ClipId == Guid.Empty) &&
                (n.Kind == AnimationNodeKind.Parameter ? parameterMap.ContainsKey(n.ParameterId) : n.ParameterId == Guid.Empty) &&
                (n.Kind is AnimationNodeKind.Clip or AnimationNodeKind.BlendSpace || !n.Loop && n.Speed == 0) &&
                (n.Kind==AnimationNodeKind.BlendSpace?n.BlendSpace is not null:n.BlendSpace is null) &&
                (n.Kind == AnimationNodeKind.Blend || n.Weight == 0), "node_fields", n.Id, "Missing or irrelevant kind-specific node fields.");
            if(n.BlendSpace is{} space){Require(++spaces<=AnimationGraphCodec.MaxBlendSpaces,"blendspace_budget",n.Id,"At most16 BlendSpace nodes.");_=new BlendSpaceProgram(space);Identity(space.Id);foreach(var sample in space.Samples)Identity(sample.Id);
                foreach(var axis in new[]{space.AxisX,space.AxisY}.Where(a=>a is not null))Require(parameterMap.TryGetValue(axis!.ParameterId,out var parameter)&&parameter.Kind==AnimationParameterKind.Float,"blendspace_axis_type",n.Id,"Float axis parameter required.");}
            nodeMap.Add(n.Id, n);
        }
        var stateMap = new Dictionary<Guid, AnimationGraphState>(); names.Clear();
        foreach (var s in states) {
            if (s is null) throw new ArgumentException("Null state."); Identity(s.Id); AnimationGraphCodec.Text(s.Name);
            Require(names.Add(s.Name) && nodeMap.TryGetValue(s.PoseNode, out var root) && root.Kind is AnimationNodeKind.Clip or AnimationNodeKind.Blend or AnimationNodeKind.BlendSpace,
                "state_pose", s.Id, "State requires a Clip/Blend pose root and unique name; nested machines are not supported.");
            stateMap.Add(s.Id, s);
        }
        var priorities = new HashSet<(Guid, int)>();
        foreach (var t in transitions) {
            if (t is null) throw new ArgumentException("Null transition."); Identity(t.Id);
            Require(t.From != t.To && stateMap.ContainsKey(t.From) && stateMap.ContainsKey(t.To) && t.Priority is >= 0 and <= 255 && priorities.Add((t.From, t.Priority)),
                "transition", t.Id, "Transition endpoints/unique per-source priority are invalid.");
            AnimationGraphCodec.Scalar(t.Duration, 0, 10); if (t.ExitTime.HasValue) AnimationGraphCodec.Scalar(t.ExitTime.Value, 0, 1);
            Require(t.Conditions is not null && t.Conditions.Length <= AnimationGraphCodec.MaxConditions && (t.Conditions.Length != 0 || t.ExitTime.HasValue),
                "transition_condition", t.Id, "Transition needs a bounded conjunction or exit time.");
            var seen = new HashSet<Guid>();
            foreach (var c in t.Conditions ?? throw new ArgumentException("Conditions required.")) {
                if (c is null) throw new ArgumentException("Null condition.");
                Require(seen.Add(c.ParameterId) && parameterMap.ContainsKey(c.ParameterId) && Enum.IsDefined(c.Comparison),
                    "condition_parameter", t.Id, "Unique typed condition parameter required.");
                var p = parameterMap[c.ParameterId]; AnimationGraphCodec.Scalar(c.FloatValue, -1000000, 1000000);
                bool equality = c.Comparison is AnimationComparison.Equal or AnimationComparison.NotEqual;
                bool valid = p.Kind switch {
                    AnimationParameterKind.Float => c.IntValue == 0 && !c.BoolValue && c.Comparison != AnimationComparison.Triggered,
                    AnimationParameterKind.Int => c.FloatValue == 0 && !c.BoolValue && c.Comparison != AnimationComparison.Triggered,
                    AnimationParameterKind.Bool => c.FloatValue == 0 && c.IntValue == 0 && equality,
                    AnimationParameterKind.Trigger => c.FloatValue == 0 && c.IntValue == 0 && !c.BoolValue && c.Comparison == AnimationComparison.Triggered,
                    _ => false
                };
                Require(valid, "condition_type", t.Id, "Condition operand/comparison does not match parameter type.");
            }
        }
        foreach(var e in events){if(e is null)throw new ArgumentException("Null event.");Identity(e.Id);AnimationGraphCodec.Text(e.Name);
            Require(ClipIds(d).Contains(e.ClipId)&&double.IsFinite(e.Time)&&e.Time>0&&e.Time<=600,"event",e.Id,"Graph-owned clip UUID and finite (0,600] event time required; actual duration checked at preparation.");}
        var dependencies = nodeMap.Keys.ToDictionary(id => id, _ => new List<Guid>());
        var occupied = new HashSet<(Guid, string)>();
        foreach (var l in links) {
            if (l is null) throw new ArgumentException("Null link."); Identity(l.Id);
            Require(nodeMap.ContainsKey(l.From) && nodeMap.ContainsKey(l.To) && l.From != l.To, "link_endpoint", l.Id, "Link endpoints are missing or identical.");
            AnimationPinType source = OutputPin(nodeMap[l.From], l.FromPin, parameterMap);
            AnimationPinType target = InputPin(nodeMap[l.To], l.ToPin);
            Require(source == target && occupied.Add((l.To, l.ToPin)), "link_type", l.Id, "Typed input accepts exactly one matching output.");
            dependencies[l.To].Add(l.From);
        }
        foreach (var n in nodes) {
            string[] required = n.Kind switch { AnimationNodeKind.Blend => ["a", "b"], AnimationNodeKind.Output => ["pose"], _ => [] };
            foreach (string pin in required) Require(occupied.Contains((n.Id, pin)), "missing_input", n.Id, "Required pose input is not connected: " + pin);
        }
        var output = nodes.Where(n => n.Kind == AnimationNodeKind.Output).ToArray();
        var machines = nodes.Where(n => n.Kind == AnimationNodeKind.StateMachine).ToArray();
        Require(output.Length == 1, "output", d.AssetId, "Exactly one Output node required.");
        Require(machines.Length <= 1 && (machines.Length == 0 ? states.Length == 0 && transitions.Length == 0 && d.EntryState == Guid.Empty : states.Length > 0 && stateMap.ContainsKey(d.EntryState)),
            "state_machine", d.AssetId, "Supports zero or one machine with an explicit reachable entry state.");
        Require(!d.InterruptTransitions||machines.Length==1,"interruption_policy",d.AssetId,"Interruptions require a state machine.");
        if (machines.Length == 1) {
            dependencies[machines[0].Id].AddRange(states.Select(s => s.PoseNode));
            var reachedStates = new HashSet<Guid> { d.EntryState }; var pending = new Queue<Guid>(); pending.Enqueue(d.EntryState);
            while (pending.TryDequeue(out Guid next)) foreach (var t in transitions.Where(t => t.From == next)) if (reachedStates.Add(t.To)) pending.Enqueue(t.To);
            Require(reachedStates.Count == states.Length, "unreachable_state", d.EntryState, "Every state must be reachable from entry; state cycles are allowed.");
        }
        var colors = new Dictionary<Guid, int>(); var heights = new Dictionary<Guid, int>();
        int Visit(Guid id, int depth) {
            Require(depth <= 64, "pose_depth", id, "Graph dependency depth exceeds 64.");
            if (colors.TryGetValue(id, out int color)) { Require(color == 2, "pose_cycle", id, "Pose/scalar dataflow cannot contain a cycle."); return heights[id]; }
            colors[id] = 1; int height = 1;
            foreach (Guid dependency in dependencies[id]) height = Math.Max(height, 1 + Visit(dependency, depth + 1));
            Require(height <= 64, "pose_depth", id, "Shared DAG path depth exceeds 64.");
            colors[id] = 2; heights[id] = height; return height;
        }
        _ = Visit(output[0].Id, 1);
        Require(colors.Count == nodes.Length, "unreachable_node", d.AssetId, "All nodes must contribute to Output or a reachable state.");
        var clipIds=ClipIds(d);Require(clipIds.Length<=AnimationGraphCodec.MaxClipDependencies,"clip_budget",d.AssetId,"At most128 distinct clip dependencies.");
        foreach(var group in nodes.Where(n=>n.BlendSpace is{SyncGroup:var id}&&id!=Guid.Empty).GroupBy(n=>n.BlendSpace!.SyncGroup)) {
            Require(!identities.Contains(group.Key)&&!clipIds.Contains(group.Key),"blendspace_group_identity",group.Key,"Group UUID cannot alias graph elements or clip resources.");var first=group.First();
            Guid SpeedSource(AnimationGraphNode n){var link=links.SingleOrDefault(l=>l.To==n.Id&&l.ToPin=="speed");return link is null?Guid.Empty:nodeMap[link.From].ParameterId;}
            foreach(var n in group)Require(n.BlendSpace!.CycleSeconds==first.BlendSpace!.CycleSeconds&&n.Loop==first.Loop&&n.Speed==first.Speed&&SpeedSource(n)==SpeedSource(first),"blendspace_group_clock",n.Id,"Same group requires identical cycle/loop/speed binding.");
        }
        foreach (Guid clip in clipIds)
            Require(!identities.Contains(clip), "resource_identity", clip, "External clip UUID cannot alias graph elements or skeleton.");
    }
    private static AnimationPinType OutputPin(AnimationGraphNode node, string pin, Dictionary<Guid, AnimationParameter> parameters)
    {
        if (node.Kind == AnimationNodeKind.Parameter && pin == "value") return parameters[node.ParameterId].Kind switch {
            AnimationParameterKind.Float => AnimationPinType.Float, AnimationParameterKind.Int => AnimationPinType.Int,
            AnimationParameterKind.Bool => AnimationPinType.Bool, AnimationParameterKind.Trigger => AnimationPinType.Trigger,
            _ => throw new ArgumentException("Unknown parameter pin type.")
        };
        if (node.Kind is AnimationNodeKind.Clip or AnimationNodeKind.Blend or AnimationNodeKind.StateMachine or AnimationNodeKind.BlendSpace && pin == "pose") return AnimationPinType.Pose;
        throw new AnimationGraphValidationException("output_pin", node.Id, "Unknown output pin or wrong direction.");
    }
    private static AnimationPinType InputPin(AnimationGraphNode node, string pin)
    {
        if (node.Kind == AnimationNodeKind.Blend && pin is "a" or "b" || node.Kind == AnimationNodeKind.Output && pin == "pose") return AnimationPinType.Pose;
        if (node.Kind == AnimationNodeKind.Blend && pin == "weight" || node.Kind is AnimationNodeKind.Clip or AnimationNodeKind.BlendSpace && pin == "speed") return AnimationPinType.Float;
        throw new AnimationGraphValidationException("input_pin", node.Id, "Unknown input pin or wrong direction.");
    }
    public static IReadOnlyList<AnimationGraphDependency> Dependencies(AnimationGraphDefinition d)
    {
        Validate(d);
        return Array.AsReadOnly(new[] { new AnimationGraphDependency(d.SkeletonId, true) }
            .Concat(ClipIds(d).Select(id=>new AnimationGraphDependency(id,false)))
            .OrderBy(x => x.Id).ToArray());
    }
    public static Guid[] ClipIds(AnimationGraphDefinition d)=>d.Nodes.SelectMany(n=>n.Kind==AnimationNodeKind.Clip?new[]{n.ClipId}:n.BlendSpace?.Samples.Select(s=>s.ClipId)??Enumerable.Empty<Guid>()).Distinct().Order().ToArray();
}
