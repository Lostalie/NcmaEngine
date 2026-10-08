using Ncma.Animation;
using Ncma.Runtime;

internal static class GraphEventTests
{
    private static void Check(bool value) { if (!value) throw new Exception("M6.5 event/debug assertion failed."); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; } throw new Exception("Invalid M6.5 input accepted."); }
    private static AnimationProgram Compile(AnimationGraphDefinition d, AnimationEventMarker[] events, double duration = 1) =>
        AnimationProgram.Compile(d, 1, d.Nodes.Where(n => n.Kind == AnimationNodeKind.Clip).Select(n => n.ClipId).Distinct()
            .Select(id => new AnimationClipDescriptor(id, d.SkeletonId, 1, duration)).ToArray(), events);
    private static AnimationGraphInstance Instance(AnimationProgram p) => new(p, new(Guid.NewGuid(), Guid.NewGuid(), 0));
    private static AnimationGraphEvent[] Events(AnimationGraphInstance i)
    { var buffer = new AnimationGraphEvent[AnimationProgram.MaximumEventsPerQuantum]; return buffer.Take(i.CopyCommittedEvents(i.Frame.Context, buffer)).ToArray(); }
    private static void Step(AnimationGraphInstance i, double delta)
    { var token = i.Prepare(i.Frame.Context, delta); i.Commit(token, i.Frame.Context with { Tick = i.Frame.Context.Tick + 1 }); }
    public static void Add(List<(string, Action)> cases)
    {
        cases.Add(("M6.5-A marker ownership, stable separate fingerprint and bounded typed compile", () => {
            var d = GraphTests.Simple(); var marker = new AnimationEventMarker(Guid.NewGuid(), d.Nodes[0].ClipId, .5, "footstep");
            var input = new[] { marker }; var p = Compile(d, input); input[0] = marker with { Name = "changed" };
            var i = Instance(p); Step(i, .5); Check(Events(i).Single().Name == "footstep");
            Check(p.ContentHash == Compile(d, []).ContentHash && p.EventContentHash != Compile(d, []).EventContentHash);
            var second = marker with { Id = Guid.NewGuid(), Time = 1 };
            Check(Compile(d, [marker, second]).EventContentHash == Compile(d, [second, marker]).EventContentHash);
            foreach (var bad in new[] { marker with { Id = Guid.Empty }, marker with { ClipId = Guid.NewGuid() }, marker with { Time = 0 },
                marker with { Time = -1 }, marker with { Time = 1.01 }, marker with { Time = double.NaN }, marker with { Name = "eval\n" }, marker with { Name = "\ud800" } }) Reject(() => Compile(d, [bad]));
            Reject(() => Compile(d, [marker, marker])); Reject(() => Compile(d, [null!]));
            Reject(() => Compile(d, Enumerable.Range(0, 4097).Select(_ => marker with { Id = Guid.NewGuid() }).ToArray()));
            var max = Compile(d, Enumerable.Range(0, 4096).Select(_ => marker with { Id = Guid.NewGuid(), Time = 1 }).ToArray());
            Check(Instance(max).Frame.Context.Tick == 0);
        }));
        cases.Add(("M6.5-A exact half-open interval, multi-cycle endpoints and repeat reads", () => {
            var d = GraphTests.Simple(); var quarter = new AnimationEventMarker(Guid.NewGuid(), d.Nodes[0].ClipId, .25, "quarter");
            var end = quarter with { Id = Guid.NewGuid(), Time = 1, Name = "end" }; var i = Instance(Compile(d, [quarter, end]));
            Check(Events(i).Length == 0); Step(i, .25); var receipt = Events(i).Single(); Check(receipt.MarkerId == quarter.Id && receipt.Context.Tick == 1 && receipt.Sequence == 1);
            Check(Events(i).Single() == receipt); Step(i, .25); Check(Events(i).Length == 0); Step(i, .5); Check(Events(i).Single().MarkerId == end.Id);
            Step(i, 1); var next = Events(i); Check(next.Length == 2 && next[0].UnwrappedTime == 1.25 && next[1].UnwrappedTime == 2);
            Reject(() => i.CopyCommittedEvents(i.Frame.Context with { Tick = 3 }, new AnimationGraphEvent[256]));
            Reject(() => i.CopyCommittedEvents(i.Frame.Context, []));
            var multi = Instance(Compile(d, [quarter with { Time = .125 }, end with { Time = .25 }], .25)); Step(multi, 1);
            Check(Events(multi).Length == 8 && Events(multi).Select(e => e.UnwrappedTime).SequenceEqual(new[] { .125, .25, .375, .5, .625, .75, .875, 1 }));
        }));
        cases.Add(("M6.5-A nonloop endpoint fires once and queries never dispatch", () => {
            var d = GraphTests.Simple(); d = d with { Nodes = [d.Nodes[0] with { Loop = false }, d.Nodes[1]] };
            var i = Instance(Compile(d, [new(Guid.NewGuid(), d.Nodes[0].ClipId, 1, "end")])); Step(i, 1); Check(Events(i).Length == 1);
            _ = i.Frame; _ = i.ReadDebug(i.Frame.Context); Check(Events(i).Length == 1);
            Step(i, 1); Check(Events(i).Length == 0); Step(i, .5); Check(Events(i).Length == 0);
        }));
        cases.Add(("M6.5-A target primary source, reentry and blend weight do not duplicate events", () => {
            var d = GraphTests.Machine(); var idle = new AnimationEventMarker(Guid.NewGuid(), d.Nodes[0].ClipId, .25, "idle");
            var run = idle with { Id = Guid.NewGuid(), ClipId = d.Nodes[1].ClipId, Name = "run" };
            var i = Instance(Compile(d, [idle, run])); i.SetBool(d.Parameters[0].Id, true); Step(i, .5);
            Check(Events(i).Single().MarkerId == run.Id && Events(i)[0].StateId == d.States[1].Id);
            i.SetBool(d.Parameters[0].Id, false); Step(i, .5); Check(Events(i).Single().MarkerId == idle.Id && i.Frame.StateId == d.EntryState);
            i.SetBool(d.Parameters[0].Id, true); Step(i, .5); Check(Events(i).Single().MarkerId == run.Id && Events(i)[0].UnwrappedTime == .25);
            var mix = GraphTests.Mixed(); var a = new AnimationEventMarker(Guid.NewGuid(), mix.Nodes[0].ClipId, .5, "primary"); var b = a with { Id = Guid.NewGuid(), ClipId = mix.Nodes[1].ClipId, Name = "secondary" };
            var j = Instance(Compile(mix, [a, b])); j.SetFloat(mix.Parameters[0].Id, 1); Step(j, .5); Check(Events(j).Single().MarkerId == a.Id);
        }));
        cases.Add(("M6.5-A Prepare/Abort/failed budget preserve clock, trigger and committed receipts", () => {
            var d = GraphTests.Simple(); var marker = new AnimationEventMarker(Guid.NewGuid(), d.Nodes[0].ClipId, .5, "event"); var i = Instance(Compile(d, [marker]));
            var pending = i.Prepare(i.Frame.Context, .5); Check(!i.ReadDebug(i.Frame.Context).SnapshotValid); Reject(() => i.CopyCommittedEvents(i.Frame.Context, new AnimationGraphEvent[256]));
            i.Abort(pending); Check(i.ReadDebug(i.Frame.Context).Outcome == AnimationEvaluationOutcome.Aborted && Events(i).Length == 0);
            Step(i, .5); var original = Events(i).Single(); Reject(() => i.Prepare(i.Frame.Context, double.NaN));
            var failed = i.ReadDebug(i.Frame.Context); Check(!failed.SnapshotValid && failed.Outcome == AnimationEvaluationOutcome.PreparationRejected && failed.Events.Count == 0 && failed.CommittedSequence == original.Sequence);
            Reject(() => i.CopyCommittedEvents(i.Frame.Context, new AnimationGraphEvent[256])); Step(i, .5); Check(i.Frame.Context.Tick == 2 && Events(i).Length == 0);
            var machine = GraphTests.Machine(); Guid trigger = machine.Parameters[0].Id;
            machine = machine with { Parameters = [machine.Parameters[0] with { Kind = AnimationParameterKind.Trigger }], Transitions = [machine.Transitions[0] with { Conditions = [new(trigger, AnimationComparison.Triggered, 0, 0, false)] }, machine.Transitions[1]] };
            var many = Enumerable.Range(0, 257).Select(_ => new AnimationEventMarker(Guid.NewGuid(), machine.Nodes[1].ClipId, .125, "overflow")).ToArray();
            var k = Instance(Compile(machine, many)); k.SetTrigger(trigger); Reject(() => k.Prepare(k.Frame.Context, .125));
            Check(k.Frame.Context.Tick == 0 && k.Frame.StateId == machine.EntryState);
            var token = k.Prepare(k.Frame.Context, .0625); k.Commit(token, k.Frame.Context with { Tick = 1 });
            Check(k.Frame.StateId == machine.States[1].Id && k.CommittedParameter(trigger) == 0);
        }));
        cases.Add(("M6.5-A exact 256 receipts and copied debug/owner/identity", () => {
            var d = GraphTests.Simple(); var markers = Enumerable.Range(0, 256).Select(_ => new AnimationEventMarker(Guid.NewGuid(), d.Nodes[0].ClipId, .5, "event")).ToArray();
            var i = Instance(Compile(d, markers)); Step(i, .5); Check(Events(i).Length == 256);
            var debug = i.ReadDebug(i.Frame.Context); Check(debug.SnapshotValid && debug.CommittedSequence == 1 && !debug.RootMotionSupported);
            Check(debug.Events.Select(e => e.MarkerId).SequenceEqual(markers.OrderBy(m => m.Id).Select(m => m.Id)));
            var old = debug.Frame.Context; Step(i, .5); Check(debug.Frame.Context == old && debug.Events.Count == 256);
            Reject(() => i.ReadDebug(old)); Reject(() => i.ReadDebug(i.Frame.Context with { SessionId = Guid.NewGuid() }));
            Check(Task.Run(() => { try { i.ReadDebug(i.Frame.Context); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult());
        }));
        cases.Add(("M6.5-A 32 isolated instances and zero-allocation committed event warm path", () => {
            var d = GraphTests.Simple(); var p = Compile(d, [new(Guid.NewGuid(), d.Nodes[0].ClipId, .125, "event")], .125);
            var actors = Enumerable.Range(0, 32).Select(_ => Instance(p)).ToArray(); var buffer = new AnimationGraphEvent[256];
            for (int warm = 0; warm < 128; warm++) foreach (var i in actors) { Step(i, .125); Check(i.CopyCommittedEvents(i.Frame.Context, buffer) == 1); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int step = 0; step < 128; step++) foreach (var i in actors) { Step(i, .125); i.CopyCommittedEvents(i.Frame.Context, buffer); }
            Check(GC.GetAllocatedBytesForCurrentThread() == before && actors.Select(i => i.Frame.InstanceId).Distinct().Count() == 32);
            Check(actors.All(i => Events(i).Single().InstanceId == i.Frame.InstanceId && i.Frame.Context.Tick == 256));
        }));
        cases.Add(("M6.5-A real World failure boundary does not publish failed events", () => {
            var d = GraphTests.Simple(); var p = Compile(d, [new(Guid.NewGuid(), d.Nodes[0].ClipId, .125, "event")], .125);
            var w = new World(); var c = new AnimationStepContext(Guid.NewGuid(), w.Identity, 0); var i = new AnimationGraphInstance(p, c);
            AnimationEvaluationToken? token = null; var runner = new WorldRunner(w, .125);
            runner.AddSystem(new SystemStep((world, h) => { token = i.Prepare(c with { Tick = world.Tick }, h); if (world.Tick == 1) throw new InvalidOperationException("injected staged failure"); }));
            runner.AddCommittedObserver(new Observer((world, h) => { i.Commit(token!.Value, c with { Tick = world.Tick }); token = null; }));
            Reject(() => runner.Advance(.25)); i.Abort(token!.Value);
            Check(w.Tick == 1 && runner.IsFaulted && i.Frame.Context.Tick == 1 && Events(i).Single().Context.Tick == 1);
        }));
        cases.Add(("M6.5-A isolated typed sequence, closed assertions, honest no-resource/root evidence", () => {
            var d = GraphTests.Machine(); var p = Compile(d, [new(Guid.NewGuid(), d.Nodes[1].ClipId, .125, "footstep")]);
            var source = Instance(p); var input = new AnimationSequenceCase(.125, 3, [new(1, d.Parameters[0].Id, AnimationParameterKind.Bool, 1)],
                [new(1, AnimationSequenceAssertionKind.State, d.States[1].Id, 0), new(1, AnimationSequenceAssertionKind.EventCount, Guid.Empty, 1),
                 new(2, AnimationSequenceAssertionKind.Parameter, d.Parameters[0].Id, 1), new(3, AnimationSequenceAssertionKind.EventCount, Guid.Empty, 1)]);
            var result = AnimationGraphSequence.Run(p, input); Check(!result.Passed && result.Checks.Count(c => c.Passed) == 3 && result.Checks[3].Code == "assertion_failed");
            Check(!result.ResourcesPrepared && !result.RootMotionSupported && result.GraphContentHash == p.ContentHash && result.EventContentHash == p.EventContentHash);
            Check(result.Timeline.Count == 3 && result.Timeline[0].Frame.Context.Tick == 1 && result.Timeline[2].Sequence == 3 && source.Frame.Context.Tick == 0);
            var second = AnimationGraphSequence.Run(p, input); Check(second.Timeline[0].Frame.InstanceId != result.Timeline[0].Frame.InstanceId);
            Check(second.Timeline[0].Frame.Context.WorldId != result.Timeline[0].Frame.Context.WorldId);
            input.Writes[0] = input.Writes[0] with { Value = 0 }; Check(result.Checks[0].Passed);
        }));
        cases.Add(("M6.5-A sequence request and output budgets reject without authority expansion", () => {
            var d = GraphTests.Machine(); var p = Compile(d, []); var request = new AnimationSequenceCase(.125, 3, [], []);
            foreach (var bad in new[] { request with { Steps = 0 }, request with { Steps = 257 }, request with { FixedDelta = 0 }, request with { FixedDelta = double.NaN },
                request with { Writes = null! }, request with { Assertions = null! }, request with { Writes = [new(1, Guid.NewGuid(), AnimationParameterKind.Bool, 1)] },
                request with { Writes = [new(1, d.Parameters[0].Id, AnimationParameterKind.Float, 1)] }, request with { Writes = [new(1, d.Parameters[0].Id, AnimationParameterKind.Bool, 2)] },
                request with { Writes = [new(0, d.Parameters[0].Id, AnimationParameterKind.Bool, 0)] }, request with { Writes = [new(1, d.Parameters[0].Id, AnimationParameterKind.Bool, 1), new(1, d.Parameters[0].Id, AnimationParameterKind.Bool, 0)] },
                request with { Assertions = [new(1, (AnimationSequenceAssertionKind)99, Guid.Empty, 0)] }, request with { Assertions = [new(1, AnimationSequenceAssertionKind.State, Guid.NewGuid(), 0)] },
                request with { Assertions = [new(1, AnimationSequenceAssertionKind.EventCount, Guid.Empty, .5)] }, request with { Assertions = [new(4, AnimationSequenceAssertionKind.Transition, Guid.Empty, 0)] },
                request with { Assertions = [new(1, AnimationSequenceAssertionKind.Parameter, d.Parameters[0].Id, double.NaN)] }, request with { Writes = new AnimationSequenceWrite[513] },
                request with { Assertions = new AnimationSequenceAssertion[65] } }) Reject(() => AnimationGraphSequence.Run(p, bad));
            var simple = GraphTests.Simple(); var marker = new AnimationEventMarker(Guid.NewGuid(), simple.Nodes[0].ClipId, .125, "event");
            var exact = Compile(simple, Enumerable.Range(0, 32).Select(_ => marker with { Id = Guid.NewGuid() }).ToArray(), .125);
            var max = AnimationGraphSequence.Run(exact, new(.125, 256, [], [])); Check(max.Timeline.Sum(s => s.Events.Count) == 8192 && max.Passed);
            var over = Compile(simple, Enumerable.Range(0, 33).Select(_ => marker with { Id = Guid.NewGuid() }).ToArray(), .125);
            Reject(() => AnimationGraphSequence.Run(over, new(.125, 256, [], [])));
        }));
    }
    private sealed class SystemStep(Action<World, double> step) : IWorldSystem { public void FixedUpdate(World w, double h) => step(w, h); }
    private sealed class Observer(Action<World, double> step) : ICommittedStepObserver { public void StepCommitted(World w, double h) => step(w, h); }
}
