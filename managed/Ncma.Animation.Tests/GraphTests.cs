using System.Text;
using System.Text.Json;
using Ncma.Animation;

internal static class GraphTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Animation graph assertion failed."); }
    private static void Reject(Action action, string? code = null)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or JsonException) {
            if (code is not null) Check(e is AnimationGraphValidationException g && g.Code == code); return;
        }
        throw new Exception("Invalid animation graph accepted.");
    }
    private static AnimationGraphLink Link(Guid from, string fromPin, Guid to, string toPin) => new(Guid.NewGuid(), from, fromPin, to, toPin);
    internal static AnimationGraphDefinition Simple()
    {
        var clip = AnimationGraphNode.Create(Guid.NewGuid(), "Idle", AnimationNodeKind.Clip) with { ClipId = Guid.NewGuid(), Loop = true, Speed = 1 };
        var output = AnimationGraphNode.Create(Guid.NewGuid(), "Output", AnimationNodeKind.Output);
        return new(AnimationGraphCodec.CurrentVersion, Guid.NewGuid(), "角色动画", Guid.NewGuid(), Guid.Empty, [], [clip, output], [Link(clip.Id, "pose", output.Id, "pose")], [], []);
    }
    internal static AnimationGraphDefinition Mixed()
    {
        var d = Simple(); var clip = d.Nodes[0] with { Id = Guid.NewGuid(), ClipId = Guid.NewGuid(), Name = "Run" };
        var blend = AnimationGraphNode.Create(Guid.NewGuid(), "Blend", AnimationNodeKind.Blend) with { Weight = .5 };
        var p = new AnimationParameter(Guid.NewGuid(), "Speed", AnimationParameterKind.Float, .75, 0, false);
        var n = AnimationGraphNode.Create(Guid.NewGuid(), "Speed", AnimationNodeKind.Parameter) with { ParameterId = p.Id };
        return d with { Parameters = [p], Nodes = [d.Nodes[0], clip, blend, d.Nodes[1], n], Links = [Link(d.Nodes[0].Id, "pose", blend.Id, "a"),
            Link(clip.Id, "pose", blend.Id, "b"), Link(blend.Id, "pose", d.Nodes[1].Id, "pose"), Link(n.Id, "value", blend.Id, "weight")] };
    }
    internal static AnimationGraphDefinition Machine()
    {
        var d = Simple(); var run = d.Nodes[0] with { Id = Guid.NewGuid(), ClipId = Guid.NewGuid(), Name = "Run" };
        var machine = AnimationGraphNode.Create(Guid.NewGuid(), "Locomotion", AnimationNodeKind.StateMachine);
        var idleState = new AnimationGraphState(Guid.NewGuid(), "Idle", d.Nodes[0].Id);
        var runState = new AnimationGraphState(Guid.NewGuid(), "Run", run.Id);
        var parameter = new AnimationParameter(Guid.NewGuid(), "Moving", AnimationParameterKind.Bool, 0, 0, false);
        return d with { EntryState = idleState.Id, Parameters = [parameter], Nodes = [d.Nodes[0], run, machine, d.Nodes[1]],
            Links = [Link(machine.Id, "pose", d.Nodes[1].Id, "pose")], States = [idleState, runState],
            Transitions = [new(Guid.NewGuid(), idleState.Id, runState.Id, 0, .2, null, [new(parameter.Id, AnimationComparison.Equal, 0, 0, true)]),
                new(Guid.NewGuid(), runState.Id, idleState.Id, 0, .2, 1, [])] };
    }
    public static void Add(List<(string, Action)> cases)
    {
        cases.Add(("M6.1 graph roundtrip, fixed pins, unicode and dependency identity", () => {
            foreach (var d in new[] { Simple(), Mixed(), Machine() }) {
                byte[] bytes = AnimationGraphCodec.Encode(d); var copy = AnimationGraphCodec.Decode(bytes);
                Check(bytes.SequenceEqual(AnimationGraphCodec.Encode(copy)) && copy.AssetId == d.AssetId && copy.Name == d.Name);
                var deps = AnimationGraphValidation.Dependencies(d); Check(deps.Count == d.Nodes.Where(n => n.Kind == AnimationNodeKind.Clip).Select(n => n.ClipId).Distinct().Count() + 1);
                Check(deps.Count(x => x.IsSkeleton) == 1 && deps.Single(x => x.IsSkeleton).Id == d.SkeletonId);
            }
            Check(!typeof(AnimationGraphDocument).Assembly.GetReferencedAssemblies().Any(a => a.Name!.Contains("Native") || a.Name.Contains("Editor") || a.Name.Contains("Python")));
        }));
        cases.Add(("M6.1 canonical collection order and owned document copies", () => {
            var d = Machine(); byte[] bytes = AnimationGraphCodec.Encode(d); var doc = new AnimationGraphDocument(d);
            var reversed = d with { Nodes = d.Nodes.Reverse().ToArray(), States = d.States.Reverse().ToArray(), Transitions = d.Transitions.Reverse().ToArray() };
            Check(bytes.SequenceEqual(AnimationGraphCodec.Encode(reversed)));
            d.Nodes[0] = d.Nodes[0] with { Name = "changed" }; d.Transitions[0].Conditions[0] = d.Transitions[0].Conditions[0] with { BoolValue = false };
            var copy = doc.CopyDefinition(); copy.Nodes[0] = copy.Nodes[0] with { X = 5 }; var leaked = doc.CopyBytes(); leaked[0] = 0;
            Check(doc.AssetId == d.AssetId && doc.CopyBytes().SequenceEqual(bytes));
        }));
        cases.Add(("M6.1 strict JSON fields/version/enums and removed format rejection", () => {
            var d = Simple(); string json = Encoding.UTF8.GetString(AnimationGraphCodec.Encode(d));
            foreach (string bad in new[] { json.Replace("\"version\":2", "\"version\":1"),json.Replace("\"version\":2", "\"version\":3"), json.Replace("\"version\":2,", ""),
                json.Insert(1, "\"version\":2,"), json.Insert(1, "\"worldHandle\":1,"), json.Replace("\"clip\"", "\"Clip\""),
                json.Replace("\"clip\"", "0"), json.Replace("\"clip\"", "\"clip, blend\""), json.Replace("\"clip\"", "\"inverseKinematics\""),
                json.Replace("\"loop\":true,", ""), json.Replace("\"x\":0", "\"x\":0,\"x\":1"), json.Replace("\"name\":", "\"Name\":"),
                "{\"sceneObjects\":[],\"version\":6}", "null" }) Reject(() => AnimationGraphCodec.Decode(Encoding.UTF8.GetBytes(bad)));
            AnimationGraphCodec.RequireExtension("Assets/Walk.ncmaanim"); AnimationGraphCodec.RequireExtension("Assets/Walk.NCMAANIM");
            foreach (string extension in new[] { ".ncscene", ".ncmascene", ".json", ".animgraph", ".ncmaanim.old", "" }) Reject(() => AnimationGraphCodec.RequireExtension("Walk" + extension));
        }));
        cases.Add(("M6.1 finite/text/null/bytes/count budgets fail closed", () => {
            var d = Simple();
            foreach (double value in new[] { double.NaN, double.PositiveInfinity, -65537d }) Reject(() => AnimationGraphCodec.Encode(d with { Nodes = [d.Nodes[0] with { X = value }, d.Nodes[1]] }));
            foreach (string name in new[] { "", " ", new string('a', 257), "x\0", "x\n", "\ud800" }) Reject(() => AnimationGraphCodec.Encode(d with { Name = name }));
            Reject(() => AnimationGraphCodec.Decode(new byte[AnimationGraphCodec.MaxBytes + 1])); Reject(() => AnimationGraphCodec.Decode([0xff, 0xfe]));
            Reject(() => AnimationGraphCodec.Encode(d with { Nodes = [null!, d.Nodes[1]] })); Reject(() => AnimationGraphCodec.Encode(d with { Parameters = null! }));
            Reject(() => AnimationGraphCodec.Encode(d with { Nodes = Enumerable.Range(0, 257).Select(_ => d.Nodes[0] with { Id = Guid.NewGuid() }).ToArray() }), "budget");
            Reject(() => AnimationGraphCodec.Encode(d with { Parameters = Enumerable.Range(0, 65).Select(i => new AnimationParameter(Guid.NewGuid(), "P" + i, AnimationParameterKind.Bool, 0, 0, false)).ToArray() }), "budget");
            Reject(() => AnimationGraphCodec.Encode(d with { Links = new AnimationGraphLink[1025] }), "budget");
            Reject(() => AnimationGraphCodec.Encode(d with { States = new AnimationGraphState[65] }), "budget");
            Reject(() => AnimationGraphCodec.Encode(d with { Transitions = new AnimationTransition[257] }), "budget");
            Reject(() => AnimationGraphCodec.Decode(Encoding.UTF8.GetBytes(new string('[', 17) + "0" + new string(']', 17))));
        }));
        cases.Add(("M6.1 global UUID namespace rejects aliases and retains input", () => {
            var d = Simple(); byte[] before = AnimationGraphCodec.Encode(d);
            Reject(() => AnimationGraphCodec.Encode(d with { AssetId = Guid.Empty }), "identity");
            Reject(() => AnimationGraphCodec.Encode(d with { SkeletonId = d.AssetId }), "identity");
            Reject(() => AnimationGraphCodec.Encode(d with { Nodes = [d.Nodes[0] with { Id = d.AssetId }, d.Nodes[1]] }), "duplicate_identity");
            Reject(() => AnimationGraphCodec.Encode(d with { Links = [d.Links[0] with { Id = d.Nodes[0].Id }] }), "duplicate_identity");
            Reject(() => AnimationGraphCodec.Encode(d with { Nodes = [d.Nodes[0] with { ClipId = d.Nodes[1].Id }, d.Nodes[1]] }), "resource_identity");
            Check(before.SequenceEqual(AnimationGraphCodec.Encode(d)));
        }));
        cases.Add(("M6.1 pins reject missing/wrong/direction/duplicate/dangling inputs", () => {
            var d = Mixed(); var edges = d.Links.ToArray();
            Reject(() => AnimationGraphCodec.Encode(d with { Links = edges.Skip(1).ToArray() }), "missing_input");
            Reject(() => AnimationGraphCodec.Encode(d with { Links = edges.Append(edges[0] with { Id = Guid.NewGuid() }).ToArray() }), "link_type");
            Reject(() => AnimationGraphCodec.Encode(d with { Links = [edges[0] with { From = d.Nodes[4].Id, FromPin = "value" }, .. edges.Skip(1)] }), "link_type");
            Reject(() => AnimationGraphCodec.Encode(d with { Links = [edges[0] with { FromPin = "speed" }, .. edges.Skip(1)] }), "output_pin");
            Reject(() => AnimationGraphCodec.Encode(d with { Links = [edges[0] with { ToPin = "pose" }, .. edges.Skip(1)] }), "input_pin");
            Reject(() => AnimationGraphCodec.Encode(d with { Links = [edges[0] with { From = Guid.NewGuid() }, .. edges.Skip(1)] }), "link_endpoint");
            Reject(() => AnimationGraphCodec.Encode(d with { Links = [edges[0] with { From = edges[0].To }, .. edges.Skip(1)] }), "link_endpoint");
        }));
        cases.Add(("M6.1 DAG cycle/unreachable/output restrictions", () => {
            var d = Simple(); var a = AnimationGraphNode.Create(Guid.NewGuid(), "A", AnimationNodeKind.Blend);
            var b = AnimationGraphNode.Create(Guid.NewGuid(), "B", AnimationNodeKind.Blend);
            var cyclic = d with { Nodes = [.. d.Nodes, a, b], Links = [Link(a.Id, "pose", b.Id, "a"), Link(b.Id, "pose", a.Id, "a"),
                Link(d.Nodes[0].Id, "pose", a.Id, "b"), Link(d.Nodes[0].Id, "pose", b.Id, "b"), Link(b.Id, "pose", d.Nodes[1].Id, "pose")] };
            Reject(() => AnimationGraphCodec.Encode(cyclic), "pose_cycle");
            Reject(() => AnimationGraphCodec.Encode(d with { Nodes = [.. d.Nodes, d.Nodes[0] with { Id = Guid.NewGuid() }] }), "unreachable_node");
            var output = d.Nodes[1] with { Id = Guid.NewGuid() };
            Reject(() => AnimationGraphCodec.Encode(d with { Nodes = [.. d.Nodes, output], Links = [.. d.Links, Link(d.Nodes[0].Id, "pose", output.Id, "pose")] }), "output");
        }));
        cases.Add(("M6.1 state cycles allowed, unreachable/nested/entry invalid", () => {
            var d = Machine(); AnimationGraphValidation.Validate(d);
            Reject(() => AnimationGraphCodec.Encode(d with { EntryState = Guid.Empty }), "state_machine");
            Reject(() => AnimationGraphCodec.Encode(d with { Transitions = [d.Transitions[1]] }), "unreachable_state");
            Reject(() => AnimationGraphCodec.Encode(d with { States = [d.States[0] with { PoseNode = d.Nodes[2].Id }, d.States[1]] }), "state_pose");
            Reject(() => AnimationGraphCodec.Encode(d with { States = [d.States[0], d.States[1] with { Name = d.States[0].Name }] }), "state_pose");
            Reject(() => AnimationGraphCodec.Encode(d with { Transitions = [d.Transitions[0] with { To = d.Transitions[0].From }, d.Transitions[1]] }), "transition");
            Reject(() => AnimationGraphCodec.Encode(d with { Transitions = [.. d.Transitions, d.Transitions[0] with { Id = Guid.NewGuid() }] }), "transition");
        }));
        cases.Add(("M6.1 typed conditions/triggers and neutral fields", () => {
            var d = Machine(); var t = d.Transitions[0]; var p = d.Parameters[0];
            Reject(() => AnimationGraphCodec.Encode(d with { Transitions = [t with { Conditions = [t.Conditions[0] with { Comparison = AnimationComparison.Greater }] }, d.Transitions[1]] }), "condition_type");
            Reject(() => AnimationGraphCodec.Encode(d with { Transitions = [t with { Conditions = [t.Conditions[0], t.Conditions[0]] }, d.Transitions[1]] }), "condition_parameter");
            Reject(() => AnimationGraphCodec.Encode(d with { Transitions = [t with { Conditions = [] }, d.Transitions[1]] }), "transition_condition");
            Reject(() => AnimationGraphCodec.Encode(d with { Transitions = [t with { Conditions = new AnimationCondition[9] }, d.Transitions[1]] }), "transition_condition");
            Reject(() => AnimationGraphCodec.Encode(d with { Transitions = [t with { ExitTime = 1.1 }, d.Transitions[1]] }));
            foreach (var kind in Enum.GetValues<AnimationParameterKind>()) {
                var changed = p with { Kind = kind, BoolDefault = false, FloatDefault = kind == AnimationParameterKind.Float ? .5 : 0, IntDefault = kind == AnimationParameterKind.Int ? 2 : 0 };
                var condition = new AnimationCondition(p.Id, kind == AnimationParameterKind.Trigger ? AnimationComparison.Triggered : AnimationComparison.Equal, 0, 0, false);
                AnimationGraphValidation.Validate(d with { Parameters = [changed], Transitions = [t with { Conditions = [condition] }, d.Transitions[1]] });
            }
            Reject(() => AnimationGraphCodec.Encode(d with { Parameters = [p with { Kind = AnimationParameterKind.Trigger, BoolDefault = true }] }), "parameter_default");
            Reject(() => AnimationGraphCodec.Encode(d with { Nodes = d.Nodes.Select(n => n.Kind == AnimationNodeKind.StateMachine ? n with { ClipId = Guid.NewGuid() } : n).ToArray() }), "node_fields");
            Reject(() => AnimationGraphCodec.Encode(d with { Nodes = d.Nodes.Select(n => n.Kind == AnimationNodeKind.Output ? n with { Loop = true } : n).ToArray() }), "node_fields");
        }));
        cases.Add(("M6.1 dependency depth bounded before stack exhaustion", () => {
            var d = Simple(); var nodes = new List<AnimationGraphNode>(d.Nodes); var links = new List<AnimationGraphLink>(); Guid previous = d.Nodes[0].Id;
            for (int i = 0; i < 62; i++) {
                var blend = AnimationGraphNode.Create(Guid.NewGuid(), "B" + i, AnimationNodeKind.Blend); nodes.Add(blend);
                links.Add(Link(previous, "pose", blend.Id, "a")); links.Add(Link(d.Nodes[0].Id, "pose", blend.Id, "b")); previous = blend.Id;
            }
            links.Add(Link(previous, "pose", d.Nodes[1].Id, "pose")); var boundary = d with { Nodes = nodes.ToArray(), Links = links.ToArray() };
            AnimationGraphValidation.Validate(boundary);
            var extra = AnimationGraphNode.Create(Guid.NewGuid(), "Over", AnimationNodeKind.Blend);
            Reject(() => AnimationGraphCodec.Encode(boundary with { Nodes = [.. boundary.Nodes, extra], Links = [.. boundary.Links.SkipLast(1),
                Link(previous, "pose", extra.Id, "a"), Link(d.Nodes[0].Id, "pose", extra.Id, "b"), Link(extra.Id, "pose", d.Nodes[1].Id, "pose")] }), "pose_depth");
        }));
    }
}
