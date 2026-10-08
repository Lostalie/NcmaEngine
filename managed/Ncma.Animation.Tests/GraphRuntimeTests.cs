using Ncma.Animation;
using Ncma.Runtime;

internal static class GraphRuntimeTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Graph runtime assertion failed."); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; } throw new Exception("Invalid graph quantum accepted."); }
    private static AnimationClipDescriptor[] Clips(AnimationGraphDefinition d, double duration = 1) => d.Nodes.Where(n => n.Kind == AnimationNodeKind.Clip)
        .Select(n => n.ClipId).Distinct().Select(id => new AnimationClipDescriptor(id, d.SkeletonId, 1, duration)).ToArray();
    private static AnimationProgram Compile(AnimationGraphDefinition d) => AnimationProgram.Compile(d, 1, Clips(d));
    private static AnimationStepContext Context() => new(Guid.NewGuid(), Guid.NewGuid(), 0);
    private static AnimationPoseInstruction[] Prepared(AnimationGraphInstance instance, AnimationEvaluationToken token, AnimationProgram p)
    { var output = new AnimationPoseInstruction[p.MaximumPlanInstructions]; return output.Take(instance.CopyPreparedPlan(token, output)).ToArray(); }
    public static void Add(List<(string, Action)> cases)
    {
        cases.Add(("M6.2 immutable compiler resolves exact clip/rig generations and stable diagnostics", () => {
            var d = GraphTests.Simple(); var clips = Clips(d); var p = AnimationProgram.Compile(d, 1, clips);
            clips[0] = clips[0] with { Duration = 500 }; d.Nodes[0] = d.Nodes[0] with { Speed = 8 };
            var c = Context(); var instance = new AnimationGraphInstance(p, c); var token = instance.Prepare(c, .1);
            Check(Prepared(instance, token, p).Single().Current == .1); instance.Abort(token);
            Reject(() => AnimationProgram.Compile(d, 0, Clips(d))); Reject(() => AnimationProgram.Compile(d, 1, []));
            Reject(() => AnimationProgram.Compile(d, 1, [Clips(d)[0] with { SkeletonId = Guid.NewGuid() }]));
            Reject(() => AnimationProgram.Compile(d, 1, [Clips(d)[0] with { Generation = 2 }]));
            Reject(() => AnimationProgram.Compile(d, 1, [Clips(d)[0], Clips(d)[0]]));
            Reject(() => AnimationProgram.Compile(d, 1, [Clips(d)[0] with { Duration = double.NaN }]));
            var diagnostic = AnimationGraphDiagnostics.Validate(d with { AssetId = Guid.Empty }).Single();
            Check(diagnostic.Code == "identity" && diagnostic.Expected == "valid_graph_v4" && diagnostic.Actual == "rejected");
        }));
        cases.Add(("M6.2 pending/committed parameters, scalar blend, abort and token identity", () => {
            var d = GraphTests.Mixed(); var p = Compile(d); var c = Context(); var i = new AnimationGraphInstance(p, c); Guid parameter = d.Parameters[0].Id;
            i.SetFloat(parameter, .2); Check(i.CommittedParameter(parameter) == .75); var t = i.Prepare(c, .1); var plan = Prepared(i, t, p);
            Check(plan.Length == 3 && Math.Abs(plan.Single(x => x.Operation == AnimationPoseOperation.Blend).Weight - .2f) < 1e-7);
            Reject(() => i.SetFloat(parameter, .5)); Reject(() => i.Prepare(c, .1)); Reject(() => i.Commit(t, c));
            Check(i.Frame.Context.Tick == 0); i.Abort(t); Check(i.CommittedParameter(parameter) == .75); Reject(() => i.Abort(t));
            var next = i.Prepare(c, .1); Reject(() => i.CopyPreparedPlan(t, new AnimationPoseInstruction[p.MaximumPlanInstructions]));
            Reject(() => i.CopyPreparedPlan(next, [])); Reject(() => i.Commit(next, c with { WorldId = Guid.NewGuid(), Tick = 1 }));
            i.Commit(next, c with { Tick = 1 }); Check(i.CommittedParameter(parameter) == .2 && i.Frame.Context.Tick == 1);
            Reject(() => i.Commit(next, c with { Tick = 1 })); Reject(() => i.Prepare(c, .1));
            Reject(() => i.SetBool(parameter, true)); Reject(() => i.SetFloat(parameter, double.NaN));
            i.SetFloat(parameter, 2); Reject(() => i.Prepare(c with { Tick = 1 }, .1)); Check(i.Frame.Context.Tick == 1);
            i.SetFloat(parameter, .5); var recover = i.Prepare(c with { Tick = 1 }, .1); i.Abort(recover);
        }));
        cases.Add(("M6.2 trigger consumption only on chosen successful commit and live transition plan", () => {
            var d = GraphTests.Machine(); Guid trigger = d.Parameters[0].Id;
            d = d with { Parameters = [d.Parameters[0] with { Kind = AnimationParameterKind.Trigger }],
                Transitions = [d.Transitions[0] with { Conditions = [new(trigger, AnimationComparison.Triggered, 0, 0, false)] }, d.Transitions[1]] };
            var p = Compile(d); var c = Context(); var instance = new AnimationGraphInstance(p, c); instance.SetTrigger(trigger);
            var t = instance.Prepare(c, .1); var plan = Prepared(instance, t, p);
            Check(plan.Count(x => x.Operation == AnimationPoseOperation.Clip) == 2 && plan[^1].Weight == .5f);
            instance.Abort(t); Check(instance.Frame.StateId == d.EntryState && instance.CommittedParameter(trigger) == 0);
            t = instance.Prepare(c, .1); instance.Commit(t, c = c with { Tick = 1 });
            Check(instance.Frame.StateId == d.States[1].Id && instance.CommittedParameter(trigger) == 0 && instance.Frame.FromStateId == d.EntryState);
            t = instance.Prepare(c, .1); instance.Commit(t, c = c with { Tick = 2 }); Check(instance.Frame.FromStateId == Guid.Empty && instance.Frame.TransitionWeight == 1);
            // A trigger that is not used by any outgoing transition survives commit.
            instance.SetTrigger(trigger); t = instance.Prepare(c, .1); instance.Commit(t, c with { Tick = 3 }); Check(instance.CommittedParameter(trigger) == 1);
        }));
        cases.Add(("M6.2 one transition per quantum, priority and nonloop exit phase", () => {
            var d = GraphTests.Machine(); Guid idle = d.States[0].Id, run = d.States[1].Id;
            d = d with { Nodes = d.Nodes.Select(n => n.Kind == AnimationNodeKind.Clip ? n with { Loop = false } : n).ToArray(),
                Transitions = [d.Transitions[0] with { Duration = 0, Conditions = [], ExitTime = .5 }, d.Transitions[1] with { Duration = 0, ExitTime = 0 }] };
            var p = Compile(d); var c = Context(); var i = new AnimationGraphInstance(p, c);
            for (int step = 0; step < 4; step++) { var t = i.Prepare(c, .1); i.Commit(t, c = c with { Tick = c.Tick + 1 }); Check(i.Frame.StateId == idle); }
            var fifth = i.Prepare(c, .1); i.Commit(fifth, c = c with { Tick = 5 }); Check(i.Frame.StateId == run);
            var sixth = i.Prepare(c, .1); i.Commit(sixth, c with { Tick = 6 }); Check(i.Frame.StateId == idle);
            // Two legal parallel edges, deterministic priority 0 wins even when declaration order is reversed.
            var entry = GraphTests.Machine(); var original = entry.Transitions[0];
            entry = entry with { Transitions = [original with { Id = Guid.NewGuid(), Priority = 1, Duration = .8, Conditions = [], ExitTime = 0 },
                original with { Duration = 0, Conditions = [], ExitTime = 0 }, entry.Transitions[1]] };
            var high = new AnimationGraphInstance(Compile(entry), Context()); var h = high.Prepare(high.Frame.Context, .1); high.Commit(h, high.Frame.Context with { Tick = 1 });
            Check(high.Frame.TransitionId == original.Id && high.Frame.FromStateId == Guid.Empty);
        }));
        cases.Add(("M6.2 loop/ending, exact 32-crossing limit and fail-before-publication", () => {
            var d = GraphTests.Simple(); var p = AnimationProgram.Compile(d, 1, Clips(d, .001)); var c = Context(); var i = new AnimationGraphInstance(p, c);
            var t = i.Prepare(c, .032); i.Commit(t, c = c with { Tick = 1 }); Check(i.Frame.Context.Tick == 1);
            Reject(() => i.Prepare(c, .033)); Check(i.Frame.Context.Tick == 1); Reject(() => i.Prepare(c, 0)); Reject(() => i.Prepare(c, double.NaN));
            var single = d with { Nodes = [d.Nodes[0] with { Loop = false }, d.Nodes[1]] }; var j = new AnimationGraphInstance(Compile(single), Context());
            for (int step = 0; step < 15; step++) { var s = j.Prepare(j.Frame.Context, .1); j.Commit(s, j.Frame.Context with { Tick = j.Frame.Context.Tick + 1 }); }
            var result = new AnimationPoseInstruction[4]; int count = j.CopyCommittedPlan(result); Check(count == 1 && result[0].Current == 1 && result[0].Previous == 1);
        }));
        cases.Add(("M6.2 shared program instances and actual World successful/failing fixed boundaries", () => {
            var p = Compile(GraphTests.Simple()); var context = Context(); var one = new AnimationGraphInstance(p, context); var two = new AnimationGraphInstance(p, context);
            var t = one.Prepare(context, .1); Reject(() => two.Commit(t, context with { Tick = 1 })); one.Commit(t, context with { Tick = 1 }); Check(two.Frame.Context.Tick == 0);
            var world = new World(); var stamp = new AnimationStepContext(Guid.NewGuid(), world.Identity, world.Tick); var instance = new AnimationGraphInstance(p, stamp);
            AnimationEvaluationToken? pending = null; var runner = new WorldRunner(world, .1);
            runner.AddSystem(new GraphSystem((w, h) => { pending = instance.Prepare(stamp with { Tick = w.Tick }, h); if (w.Tick == 1) throw new InvalidOperationException("before successful fixed commit"); }));
            runner.AddCommittedObserver(new GraphObserver((w, h) => { instance.Commit(pending!.Value, stamp with { Tick = w.Tick }); pending = null; }));
            Reject(() => runner.Advance(.2)); if (pending.HasValue) instance.Abort(pending.Value);
            Check(world.Tick == 1 && instance.Frame.Context.Tick == 1 && runner.IsFaulted);
        }));
        cases.Add(("M6.2 30/60/144 render schedules and zero-allocation warm prepare/commit", () => {
            var p = Compile(GraphTests.Mixed());
            foreach (int hz in new[] { 30, 60, 144 }) {
                var i = new AnimationGraphInstance(p, Context()); double debt = 0;
                for (int frame = 0; frame < hz * 2; frame++) { debt += 1d / hz; while (debt + 1e-12 >= 1d / 60) {
                    var t = i.Prepare(i.Frame.Context, 1d / 60); i.Commit(t, i.Frame.Context with { Tick = i.Frame.Context.Tick + 1 }); debt -= 1d / 60;
                } } Check(i.Frame.Context.Tick == 120);
            }
            var runtime = new AnimationGraphInstance(p, Context()); var output = new AnimationPoseInstruction[p.MaximumPlanInstructions];
            for (int warm = 0; warm < 64; warm++) { var t = runtime.Prepare(runtime.Frame.Context, .01); runtime.CopyPreparedPlan(t, output); runtime.Commit(t, runtime.Frame.Context with { Tick = runtime.Frame.Context.Tick + 1 }); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int step = 0; step < 256; step++) { var t = runtime.Prepare(runtime.Frame.Context, .01); runtime.CopyPreparedPlan(t, output); runtime.Commit(t, runtime.Frame.Context with { Tick = runtime.Frame.Context.Tick + 1 }); }
            Check(GC.GetAllocatedBytesForCurrentThread() == before);
            Check(Task.Run(() => { try { _ = runtime.Frame; return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult());
        }));
    }
    private sealed class GraphSystem(Action<World, double> step) : IWorldSystem { public void FixedUpdate(World w, double h) => step(w, h); }
    private sealed class GraphObserver(Action<World, double> step) : ICommittedStepObserver { public void StepCommitted(World w, double h) => step(w, h); }
}
