using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Ncma;
using Ncma.Gameplay;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Editor.Core;

static void Check(bool condition, string message = "Assertion failed")
{ if (!condition) throw new InvalidOperationException(message); }
static void Reject(Action action)
{
    try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or AggregateException or OverflowException) { return; }
    throw new Exception("Invalid request accepted.");
}
static SceneDocument Document(int objects = 1, bool spatial = true)
{
    var doc = new SceneDocument("Play fixture");
    for (int i = 0; i < objects; i++)
    {
        var obj = doc.World.CreateObject("Object " + i, Guid.Parse($"00000000-0000-0000-0000-{i + 1:D12}"));
        if (spatial) obj.Set(TransformData.Identity);
        doc.SetBindings(obj.PersistentId, [new(Guid.Parse($"10000000-0000-0000-0000-{i + 1:D12}"), "Test.Probe", true, [])]);
    }
    return doc;
}
static Ncma.Runtime.GameObject Object(SceneDocument doc) => doc.World.GetObjects()[0];
static double EqualTime(int fps)
{
    var doc = Document(); var probe = new Probe();
    probe.Fixed = delta => {
        var t = probe.GameObject.LocalTransform; t.Position.X += (float)delta;
        probe.GameObject.LocalTransform = t;
    };
    using var play = new PlaySession(doc, FrameTimePolicy.Strict);
    play.Start(_ => probe);
    for (int i = 0; i < fps; i++) play.AdvanceFrame(1.0 / fps);
    Check(play.Status.Tick == 60 && probe.FixedCount == 60 && probe.UpdateCount == fps);
    Check(Math.Abs(probe.Elapsed - 1) < 1e-10);
    return Object(doc).Get<TransformData>().Position.X;
}
[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
static WeakReference[] DisposedPlay()
{
    var doc = Document(); var probe = new Probe(); var play = new PlaySession(doc);
    play.Start(_ => probe); play.AdvanceFrame(1d / 60); play.Stop(); play.Dispose();
    return [new(play), new(probe)];
}
static InputFrame Frame(PlaySession play, ulong sequence, ulong held = 0, ulong pressed = 0, ulong released = 0,
    double x = 0, double y = 0, bool focused = true)
{
    var h = new ulong[8]; var p = new ulong[8]; var r = new ulong[8]; h[0] = held; p[0] = pressed; r[0] = released;
    return new(play.SessionId, sequence, focused, h, p, r, x, y);
}
var cases = new (string Name, Action Run)[]
{
    ("Reload preflight failure preserves old catalog instances and committed Play paused", () => {
        var doc = Document(); var old = new Probe(); using var play = new PlaySession(doc);
        play.Start(_ => old); play.AdvanceFrame(1.0 / 60); var bytes = doc.CaptureBytes(); var epoch = play.SessionId;
        Reject(() => play.Reload(_ => throw new ArgumentException("Candidate invalid")));
        Check(play.State == PlayState.Paused && play.SessionId == epoch && doc.CaptureBytes().SequenceEqual(bytes) && old.Destroyed == 0);
        play.Resume(); play.AdvanceFrame(1.0 / 60); Check(old.FixedCount == 2); play.Stop(); Check(old.Destroyed == 1);
    }),
    ("Reload rebuilds private instances clears commands input and systems without resetting World", () => {
        var doc = Document(); var old = new Probe(); var fresh = new Probe(); int systems = 0; using var play = new PlaySession(doc);
        play.AddSystem(new TestSystem((_, _) => systems++)); play.Start(_ => old); play.AdvanceFrame(1.0 / 60);
        var reference = Object(doc).Reference; var world = doc.World.Identity; var tick = play.Tick; var epoch = play.SessionId;
        play.SubmitInput(Frame(play, 1)); play.SubmitInput(Frame(play, 2, 2, 2));
        play.Reload(_ => fresh); Check(play.State == PlayState.Paused && play.SessionId != epoch && play.Tick == tick);
        Check(Object(doc).Reference == reference && doc.World.Identity == world && old.Destroyed == 1 && fresh.Created == 1 && fresh.FixedCount == 0);
        Check(!play.Input.Pressed(1) && play.Status.Accumulator == 0); play.Resume(); play.AdvanceFrame(1.0 / 60);
        Check(fresh.FixedCount == 1 && systems == 1);
    }),
    ("Reload activation failure is Faulted and does not resurrect old private state", () => {
        var doc = Document(); var old = new Probe(); var fresh = new Probe { Create = () => throw new InvalidOperationException("Activation") };
        using var play = new PlaySession(doc); play.Start(_ => old); var epoch = play.SessionId; var before = doc.CaptureBytes();
        Reject(() => play.Reload(_ => fresh)); Check(play.State == PlayState.Faulted && play.SessionId != epoch && old.Destroyed == 1);
        Check(doc.CaptureBytes().SequenceEqual(before) && play.Tick == 0 && play.Fault!.Phase == "reload_create");
        Reject(() => play.Resume()); play.Stop(); Check(fresh.Destroyed == 1);
    }),
    ("Repeated Start Stop Dispose does not retain engine instance roots", () => {
        var references = Enumerable.Range(0, 64).SelectMany(_ => DisposedPlay()).ToArray();
        for (int pass = 0; pass < 10 && references.Any(r => r.IsAlive); pass++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Thread.Sleep(10); }
        Check(references.All(r => !r.IsAlive), "Disposed Play/Behaviour roots leaked");
    }),
    ("Runtime pending lifecycle is cleaned by Stop without a following step", () => {
        var doc = Document(); var first = new Probe(); var second = new Probe(); using var play = new PlaySession(doc);
        first.Fixed = _ => {
            first.Context.Commands.AttachBehaviour(Object(doc).PersistentId, new(Guid.NewGuid(), "Test.Pending", true, []));
            first.Context.Commands.RemoveBehaviour(Object(doc).PersistentId, Guid.Parse("10000000-0000-0000-0000-000000000001"));
        };
        play.Start(b => b.TypeName == "Test.Pending" ? second : first); play.AdvanceFrame(1.0 / 60); play.Stop();
        Check(first.Disabled == 1 && first.Destroyed == 1 && second.Created == 0);
    }),
    ("Runtime create configure is atomic and keeps surviving handles", () => {
        var doc = Document(); var original = Object(doc); var originalId = original.Reference; var identity = doc.World.Identity;
        var probe = new Probe(); PendingObject? pending = null; using var play = new PlaySession(doc);
        probe.Fixed = _ => {
            if (play.Tick != 0) return;
            pending = probe.Context.Commands.SpawnEmpty("Spawned"); probe.Context.Commands.Rename(pending.ObjectId, "Configured");
            probe.Context.Commands.AddComponent(pending.ObjectId, new("ncma.transform", 1, doc.World.Components.Encode(TransformData.Identity)));
            Reject(() => doc.World.FindObject(pending.ObjectId)); Reject(() => probe.Context.Commands.GetReceipt(pending.Token));
        };
        play.Start(_ => probe); play.AdvanceFrame(1.0 / 60);
        Check(doc.World.Count == 2 && doc.World.Identity == identity && original.Reference == originalId && original.Name == "Object 0");
        Check(doc.World.FindObject(pending!.ObjectId).Name == "Configured" && doc.World.FindObject(pending.ObjectId).Has<TransformData>());
        Check(play.Commands.GetReceipt(pending.Token).Tick == 1 && play.RenderView.Objects.Length == 2);
        var token = pending.Token; play.Stop(); Reject(() => play.Commands.GetReceipt(token));
    }),
    ("Caught payload and component validation errors still poison the whole step", () => {
        foreach (bool command in new[] { true, false }) {
            var doc = Document(); var probe = new Probe(); using var play = new PlaySession(doc);
            probe.Fixed = _ => {
                probe.Context.Commands.SpawnEmpty("Never installed");
                if (command) { try { probe.Context.Commands.AddComponent(Object(doc).PersistentId, new("ncma.transform", 1, default)); } catch (InvalidOperationException) { } }
                else { var t = TransformData.Identity with { Position = new System.Numerics.Vector3(float.NaN, 0, 0) }; Reject(() => Object(doc).Set(t)); }
            };
            play.Start(_ => probe); byte[] before = doc.CaptureBytes(); play.AdvanceFrame(1.0 / 60);
            Check(play.State == PlayState.Faulted && play.Tick == 0 && doc.CaptureBytes().SequenceEqual(before));
            Check(play.Fault!.AttemptTick == 1 && play.Fault.Tick == 0);
        }
    }),
    ("Later invalid runtime command aborts structure components metadata input and receipts", () => {
        var doc = Document(); var probe = new Probe(); PendingObject? pending = null; using var play = new PlaySession(doc);
        var before = doc.CaptureBytes(); var reference = Object(doc).Reference; var revision = doc.Revision;
        probe.Fixed = _ => {
            pending = probe.Context.Commands.SpawnEmpty("Transient");
            var t = probe.GameObject.LocalTransform; t.Position.X = 9; probe.GameObject.LocalTransform = t;
            probe.Context.Commands.AttachBehaviour(pending.ObjectId, new(Guid.NewGuid(), "Test.Probe", true, []));
            Reject(() => probe.Context.Commands.Rename(Guid.NewGuid(), "Bad")); // Even if user catches it, the step is poisoned.
        };
        play.Start(_ => probe); before = doc.CaptureBytes(); revision = doc.Revision;
        play.SubmitInput(Frame(play, 1)); play.SubmitInput(Frame(play, 2, 2, 2)); play.AdvanceFrame(1.0 / 60);
        Check(play.State == PlayState.Faulted && play.Tick == 0 && doc.CaptureBytes().SequenceEqual(before) && doc.Revision == revision);
        Check(Object(doc).Reference == reference && play.Input.Pressed(1)); Reject(() => play.Commands.GetReceipt(pending!.Token));
    }),
    ("Runtime conflict order rejects duplicate create double delete and modifications after delete", () => {
        foreach (var conflict in new[] { "create", "delete", "rename", "add" }) {
            var doc = Document(); var probe = new Probe(); using var play = new PlaySession(doc);
            probe.Fixed = _ => {
                if (conflict == "create") { var id = Guid.NewGuid(); probe.Context.Commands.SpawnEmpty("A", id); probe.Context.Commands.SpawnEmpty("B", id); }
                else { var id = Object(doc).PersistentId; probe.Context.Commands.Destroy(id);
                    if (conflict == "delete") probe.Context.Commands.Destroy(id);
                    else if (conflict == "rename") probe.Context.Commands.Rename(id, "Bad");
                    else probe.Context.Commands.AddComponent(id, new("ncma.transform", 1, doc.World.Components.Encode(TransformData.Identity))); }
            };
            play.Start(_ => probe); var before = doc.CaptureBytes(); play.AdvanceFrame(1.0 / 60);
            Check(play.State == PlayState.Faulted && doc.CaptureBytes().SequenceEqual(before) && play.Tick == 0, conflict);
        }
    }),
    ("Spawn then delete publishes no object binding or lifecycle", () => {
        var doc = Document(); var probe = new Probe(); int factories = 0; PendingObject? pending = null;
        using var play = new PlaySession(doc); probe.Fixed = _ => {
            pending = probe.Context.Commands.SpawnEmpty("Gone");
            probe.Context.Commands.AttachBehaviour(pending.ObjectId, new(Guid.NewGuid(), "Test.Probe", true, []));
            probe.Context.Commands.Destroy(pending.ObjectId);
        };
        play.Start(_ => { factories++; return probe; }); play.AdvanceFrame(1.0 / 60);
        Check(play.Tick == 1 && doc.World.Count == 1 && factories == 1 && play.BehaviourCount == 1);
        Check(play.Commands.GetReceipt(pending!.Token).Disposition == "removed");
    }),
    ("Component write remove ordering rejects Set after remove and Add existing", () => {
        foreach (bool after in new[] { false, true }) {
            var doc = Document(); var probe = new Probe(); using var play = new PlaySession(doc);
            probe.Fixed = _ => {
                if (!after) { var t = probe.GameObject.LocalTransform; t.Position.X = 9; probe.GameObject.LocalTransform = t; }
                probe.Context.Commands.RemoveComponent(Object(doc).PersistentId, "ncma.transform");
                if (after) probe.GameObject.LocalTransform = Transform.Identity;
            };
            play.Start(_ => probe); play.AdvanceFrame(1.0 / 60);
            Check(after ? play.State == PlayState.Faulted && Object(doc).Has<TransformData>() : play.Tick == 1 && !Object(doc).Has<TransformData>());
        }
        var existing = Document(); var p = new Probe(); using var session = new PlaySession(existing);
        p.Fixed = _ => p.Context.Commands.AddComponent(Object(existing).PersistentId, new("ncma.transform", 1, existing.World.Components.Encode(TransformData.Identity)));
        session.Start(_ => p); session.AdvanceFrame(1.0 / 60); Check(session.State == PlayState.Faulted);
    }),
    ("Runtime attach enable disable remove lifecycle waits for safe boundary", () => {
        var doc = Document(); var first = new Probe(); var second = new Probe(); var id = Object(doc).PersistentId; var binding = Guid.NewGuid();
        using var play = new PlaySession(doc);
        first.Fixed = _ => {
            if (play.Tick == 0) first.Context.Commands.AttachBehaviour(id, new(binding, "Test.Other", false, []));
            if (play.Tick == 1) first.Context.Commands.SetBehaviourEnabled(id, binding, true);
            if (play.Tick == 2) first.Context.Commands.SetBehaviourEnabled(id, binding, false);
            if (play.Tick == 3) first.Context.Commands.RemoveBehaviour(id, binding);
        };
        play.Start(b => b.TypeName == "Test.Other" ? second : first);
        play.AdvanceFrame(1.0 / 60); Check(second.Created == 0 && second.FixedCount == 0 && doc.GetBindings(id).Length == 2);
        play.AdvanceFrame(1.0 / 60); Check(second.Created == 1 && second.Enabled == 0 && second.FixedCount == 0);
        play.AdvanceFrame(1.0 / 60); Check(second.Enabled == 1 && second.FixedCount == 1);
        play.AdvanceFrame(1.0 / 60); Check(second.Disabled == 1 && second.FixedCount == 1 && doc.GetBindings(id).Length == 1);
        play.AdvanceFrame(1.0 / 60); Check(second.Destroyed == 1); play.Stop(); Check(second.Disabled == 1 && second.Destroyed == 1);
    }),
    ("Destroyed object cleanup uses tombstone context and surviving handles remain valid", () => {
        var doc = Document(2); var first = new Probe(); var second = new Probe(); var dead = doc.World.GetObjects()[0]; var survivor = doc.World.GetObjects()[1];
        using var play = new PlaySession(doc); first.Fixed = _ => first.Context.Commands.Destroy(dead.PersistentId);
        first.Disable = () => { Check(first.Cleanup!.ObjectId == Guid.Parse("00000000-0000-0000-0000-000000000001")); Reject(() => _ = first.GameObject.Name); };
        play.Start(b => b.Id == Guid.Parse("10000000-0000-0000-0000-000000000001") ? first : second);
        play.AdvanceFrame(1.0 / 60); Check(play.BehaviourCount == 1 && doc.World.Count == 1 && survivor.Name == "Object 1");
        Reject(() => _ = dead.Name); play.AdvanceFrame(1.0 / 60); Check(first.Disabled == 1 && first.Destroyed == 1 && second.FixedCount == 2);
    }),
    ("Runtime factory prepare failure leaves complete state and signal consumption intact", () => {
        var doc = Document(); var first = new Probe(); using var play = new PlaySession(doc);
        first.Fixed = _ => {
            var signal = new GameplaySignal[1]; Check(first.GameObject.World.ReceiveSignals(first.GameObject, signal) == 1);
            var pending = first.Context.Commands.SpawnEmpty("Bad factory");
            first.Context.Commands.AttachBehaviour(pending.ObjectId, new(Guid.NewGuid(), "Test.Missing", true, []));
        };
        play.Start(b => b.TypeName == "Test.Missing" ? throw new ArgumentException("Missing type") : first);
        first.GameObject.World.SendSignal(first.GameObject, first.GameObject, 1); var before = doc.CaptureBytes();
        play.AdvanceFrame(1.0 / 60); Check(play.State == PlayState.Faulted && doc.CaptureBytes().SequenceEqual(before) && play.Tick == 0);
        Check(first.GameObject.World.ReceiveSignals(first.GameObject, new GameplaySignal[1]) == 1);
    }),
    ("Runtime command and receipt capacity is bounded", () => {
        foreach (int count in new[] { 1024, 1025 }) {
            var doc = Document(); var probe = new Probe(); using var play = new PlaySession(doc); CommandToken first = default, last = default;
            probe.Fixed = _ => { for (int i = 0; i < count; i++) { var token = probe.Context.Commands.Rename(Object(doc).PersistentId, "Name " + i); if (i == 0) first = token; last = token; } };
            play.Start(_ => probe); play.AdvanceFrame(1.0 / 60);
            Check(count == 1024 ? play.Tick == 1 && Object(doc).Name == "Name 1023" : play.Tick == 0 && play.State == PlayState.Faulted);
            if (count == 1024) { Check(play.Commands.GetReceipt(first).Tick == 1); var oldToken = first; play.AdvanceFrame(1.0 / 60); Reject(() => play.Commands.GetReceipt(oldToken)); Check(play.Commands.GetReceipt(last).Tick == 2); }
        }
    }),
    ("Input retains both edges without a fixed step and consumes only once", () => {
        var doc = Document(); var seen = new List<InputState>(); var probe = new Probe();
        using var play = new PlaySession(doc); probe.Fixed = _ => seen.Add(probe.Context.Input); play.Start(_ => probe);
        play.SubmitInput(Frame(play, 1));
        play.SubmitInput(Frame(play, 2, held: 2, pressed: 2, x: 3)); play.AdvanceFrame(0);
        play.SubmitInput(Frame(play, 3, released: 2, x: 4)); play.AdvanceFrame(0);
        Check(play.Input.Pressed(1) && play.Input.Released(1) && !play.Input.Held(1) && play.Input.PointerX == 7);
        play.AdvanceFrame(2.0 / 60);
        Check(seen.Count == 2 && seen[0].Pressed(1) && seen[0].Released(1) && seen[0].PointerX == 7);
        Check(!seen[1].Pressed(1) && !seen[1].Released(1) && seen[1].PointerX == 0);
    }),
    ("Input rejects foreign epoch sequence pointer and array shapes without mutation", () => {
        var doc = Document(); using var play = new PlaySession(doc); play.Start(_ => new Probe()); play.SubmitInput(Frame(play, 1));
        Reject(() => play.SubmitInput(Frame(play, 1))); Reject(() => play.SubmitInput(Frame(play, 2) with { SessionId = Guid.NewGuid() }));
        Reject(() => play.SubmitInput(Frame(play, 2, x: double.NaN))); Reject(() => play.SubmitInput(Frame(play, 2) with { Held = [1] }));
        Check(play.Input.FrameSequence == 1);
        play.SubmitInput(Frame(play, 2, x: 1_000_000)); Reject(() => play.SubmitInput(Frame(play, 3, x: 1)));
        Check(play.Input.PointerX == 1_000_000 && play.Input.FrameSequence == 2);
    }),
    ("Focus pause resume step clear transient input and resynchronize held", () => {
        var doc = Document(); var probe = new Probe(); using var play = new PlaySession(doc); play.Start(_ => probe);
        play.SubmitInput(Frame(play, 1)); play.SubmitInput(Frame(play, 2, 2, 2)); play.SubmitInput(Frame(play, 3, focused: false));
        Check(!play.Input.Held(1) && !play.Input.Pressed(1));
        play.SubmitInput(Frame(play, 4, 2, 2)); Check(play.Input.Held(1) && !play.Input.Pressed(1));
        play.Pause(); play.SubmitInput(Frame(play, 5, 2, 2)); Check(play.Input.Held(1) && !play.Input.Pressed(1));
        play.Step(); play.Resume(); Check(!play.Input.Held(1) && !play.Input.Pressed(1));
        play.SubmitInput(Frame(play, 6, 2, 2)); Check(play.Input.Held(1) && !play.Input.Pressed(1));
    }),
    ("Input consumption aborts when a fixed step fails", () => {
        var doc = Document(); var probe = new Probe { Fixed = _ => throw new Exception("Input failure") };
        using var play = new PlaySession(doc); play.Start(_ => probe);
        play.SubmitInput(Frame(play, 1)); play.SubmitInput(Frame(play, 2, 2, 2, x: 5)); play.AdvanceFrame(1.0 / 60);
        Check(play.State == PlayState.Faulted && play.Tick == 0 && play.Input.Pressed(1) && play.Input.PointerX == 5);
    }),
    ("Render view interpolates without writing authoritative World", () => {
        var doc = Document(); var probe = new Probe(); using var play = new PlaySession(doc);
        probe.Fixed = _ => { var t = probe.GameObject.LocalTransform; t.Position.X += 10; t.Scale.X += 2;
            t.RotationY = 1; t.RotationW = 0; probe.GameObject.LocalTransform = t; };
        play.Start(_ => probe); play.AdvanceFrame(1.5 / 60); var revision = doc.Revision;
        var view = play.RenderView; Check(view.Objects.Length == 1 && Math.Abs(view.Objects[0].Transform.Position.X - 5) < 1e-5);
        Check(Math.Abs(view.Objects[0].Transform.Scale.X - 2) < 1e-5 && Math.Abs(view.Objects[0].Transform.Rotation.LengthSquared() - 1) < 1e-5);
        Check(Object(doc).Get<TransformData>().Position.X == 10 && doc.Revision == revision);
        view.Objects[0] = new(Guid.NewGuid(), TransformData.Identity); Check(play.RenderView.Objects[0].ObjectId == Object(doc).PersistentId);
        play.Pause(); Check(play.RenderView.Objects[0].Transform.Position.X == 10); play.Step();
        Check(play.RenderView.Objects[0].Transform.Position.X == 20);
    }),
    ("Render view omits nonspatial objects and clears after Stop", () => {
        var doc = Document(spatial: false); using var play = new PlaySession(doc); play.Start(_ => new Probe());
        Check(play.RenderView.Objects.Length == 0); play.Stop(); Check(play.RenderView.Objects.Length == 0 && !Object(doc).Has<TransformData>());
    }),
    ("Signal receive consumption rolls back and sends have monotonic epoch identity", () => {
        var doc = Document(); var probe = new Probe(); using var play = new PlaySession(doc); play.Start(_ => probe);
        var target = probe.GameObject; target.World.SendSignal(target, target, 1);
        GameplaySignal first = default;
        probe.Fixed = _ => { var values = new GameplaySignal[1]; Check(target.World.ReceiveSignals(target, values) == 1);
            first = values[0]; Check(target.World.ReceiveSignals(target, values) == 0); throw new Exception("Abort consume"); };
        play.AdvanceFrame(1.0 / 60); var remaining = new GameplaySignal[1];
        Check(target.World.ReceiveSignals(target, remaining) == 1 && remaining[0].Sequence == first.Sequence && first.Epoch != Guid.Empty);
        play.Stop(); play.Start(_ => probe); target = probe.GameObject; target.World.SendSignal(target, target, 2);
        Check(target.World.ReceiveSignals(target, remaining) == 1 && remaining[0].Epoch != first.Epoch);
    }),
    ("Pure managed gameplay dependency", () => {
        var assembly = typeof(PlaySession).Assembly;
        Check(!assembly.GetReferencedAssemblies().Any(a => a.Name!.Contains("Python")));
        Check(!assembly.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            .Any(m => m.GetCustomAttribute<DllImportAttribute>() is not null));
    }),
    ("Empty scene still advances fixed ticks", () => {
        var doc = Document(0); using var play = new PlaySession(doc);
        play.Start(_ => throw new Exception("No factory call expected."));
        Check(play.BehaviourCount == 0 && play.Status.Tick == 0);
        Check(play.AdvanceFrame(1.0 / 60).Tick == 1);
        play.Stop(); play.Stop(); Check(play.State == PlayState.Stopped);
    }),
    ("Non spatial Behaviour does not acquire Transform", () => {
        var doc = Document(spatial: false); var probe = new Probe();
        using var play = new PlaySession(doc); play.Start(_ => probe); play.AdvanceFrame(1.0 / 60);
        Check(!Object(doc).Has<TransformData>() && probe.FixedCount == 1);
    }),
    ("Disabled binding created but never updated", () => {
        var doc = Document(); var obj = Object(doc); var b = doc.GetBindings(obj.PersistentId)[0];
        doc.SetBindings(obj.PersistentId, [b with { Enabled = false }]);
        var probe = new Probe(); using var play = new PlaySession(doc); play.Start(_ => probe);
        play.AdvanceFrame(1.0 / 60); play.Stop();
        Check(probe.Created == 1 && probe.Enabled == 0 && probe.FixedCount == 0 && probe.UpdateCount == 0 &&
            probe.Disabled == 0 && probe.Destroyed == 1);
    }),
    ("Binding UUID orders instances deterministically", () => {
        var doc = Document(); var id = Object(doc).PersistentId; var a = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var b = Guid.Parse("20000000-0000-0000-0000-000000000002"); var order = new List<Guid>();
        doc.SetBindings(id, [new(b, "Test.Probe", true, []), new(a, "Test.Probe", true, [])]);
        using var play = new PlaySession(doc);
        play.Start(binding => new Probe { Fixed = _ => order.Add(binding.Id) });
        play.AdvanceFrame(1.0 / 60); Check(order.SequenceEqual(new[] { a, b }));
    }),
    ("Initialization commits writes without Tick", () => {
        var doc = Document(); var probe = new Probe();
        probe.Create = () => { var t = probe.GameObject.LocalTransform; t.Position.X = 7; probe.GameObject.LocalTransform = t; };
        using var play = new PlaySession(doc); play.Start(_ => probe);
        Check(play.Status.Tick == 0 && Object(doc).Get<TransformData>().Position.X == 7);
    }),
    ("Failed initialization aborts writes and cleans attempted lifecycle", () => {
        var doc = Document(); var probe = new Probe();
        probe.Create = () => { var t = probe.GameObject.LocalTransform; t.Position.X = 9; probe.GameObject.LocalTransform = t; throw new InvalidOperationException("Create failure"); };
        using var play = new PlaySession(doc); Reject(() => play.Start(_ => probe));
        Check(play.State == PlayState.Faulted && play.Fault!.Phase == "create");
        Check(Object(doc).Get<TransformData>().Position.X == 0 && doc.World.Tick == 0);
        Check(probe.Created == 1 && probe.Destroyed == 1 && play.BehaviourCount == 0);
    }),
    ("Factory failure does not publish partial instances", () => {
        var doc = Document(2); int attempts = 0; using var play = new PlaySession(doc);
        Reject(() => play.Start(_ => ++attempts == 2 ? throw new ArgumentException("Factory failed") : new Probe()));
        Check(play.State == PlayState.Faulted && play.BehaviourCount == 0 && doc.World.Tick == 0);
    }),
    ("30/60/144 FPS has equal simulation", () => {
        double a = EqualTime(30), b = EqualTime(60), c = EqualTime(144);
        Check(Math.Abs(a - b) < 1e-6 && Math.Abs(b - c) < 1e-6);
    }),
    ("Zero step frames update presentation only", () => {
        var doc = Document(); var probe = new Probe(); using var play = new PlaySession(doc);
        play.Start(_ => probe); play.AdvanceFrame(1.0 / 120);
        Check(play.Status.Tick == 0 && probe.FixedCount == 0 && probe.UpdateCount == 1);
        play.AdvanceFrame(1.0 / 120); Check(play.Status.Tick == 1 && probe.UpdateCount == 2);
    }),
    ("Fixed writes remain committed-read pending-write", () => {
        var doc = Document(); var probe = new Probe(); bool readCommitted = false;
        probe.Fixed = _ => {
            var t = probe.GameObject.LocalTransform; t.Position.X = 11; probe.GameObject.LocalTransform = t;
            readCommitted = probe.GameObject.LocalTransform.Position.X == 0;
        };
        using var play = new PlaySession(doc); play.Start(_ => probe); play.AdvanceFrame(1.0 / 60);
        Check(readCommitted && Object(doc).Get<TransformData>().Position.X == 11);
    }),
    ("OnUpdate World write faults after successful step", () => {
        var doc = Document(); var probe = new Probe();
        probe.Update = _ => { var t = probe.GameObject.LocalTransform; t.Position.X = 99; probe.GameObject.LocalTransform = t; };
        using var play = new PlaySession(doc); play.Start(_ => probe); var result = play.AdvanceFrame(1.0 / 60);
        Check(result.State == PlayState.Faulted && result.Tick == 1 && result.Fault!.Phase == "update");
        Check(Object(doc).Get<TransformData>().Position.X == 0); Reject(() => play.Resume());
    }),
    ("Read-only blocks structure restore and signal mutations", () => {
        var doc = Document(); var probe = new Probe(); var snapshot = doc.World.CaptureSnapshot();
        probe.Update = _ => {
            Reject(() => doc.World.CreateObject("Illegal")); Reject(() => Object(doc).Name = "Illegal");
            Reject(() => Object(doc).Remove<TransformData>()); Reject(() => Object(doc).Destroy());
            Reject(() => doc.World.RestoreSnapshot(snapshot));
            Reject(() => doc.SetBindings(Object(doc).PersistentId, []));
            Reject(() => probe.GameObject.World.Dispose());
            Reject(() => probe.GameObject.World.SendSignal(probe.GameObject, probe.GameObject, 1));
            Reject(() => probe.GameObject.World.ReceiveSignals(probe.GameObject, new GameplaySignal[1]));
        };
        using var play = new PlaySession(doc); play.Start(_ => probe); play.AdvanceFrame(0);
        Check(play.State == PlayState.Running && doc.World.Count == 1 && Object(doc).Name == "Object 0");
    }),
    ("Invalid time rejects without changing status", () => {
        var doc = Document(); using var play = new PlaySession(doc); play.Start(_ => new Probe());
        var before = play.Status;
        foreach (double value in new[] { -1d, double.NaN, double.PositiveInfinity })
            Reject(() => play.AdvanceFrame(value));
        Check(play.Status == before);
    }),
    ("Strict budget rejects before state changes", () => {
        var doc = Document(); using var play = new PlaySession(doc, FrameTimePolicy.Strict); play.Start(_ => new Probe());
        play.AdvanceFrame(1.0 / 120); var before = play.Status; Reject(() => play.AdvanceFrame(0.25));
        Check(play.Status == before);
    }),
    ("Interactive catch-up reports time loss and preserves fraction", () => {
        var doc = Document(); using var play = new PlaySession(doc); play.Start(_ => new Probe());
        var result = play.AdvanceFrame(1.01);
        Check(result.StepsExecuted == 8 && result.Tick == 8 && result.DroppedSeconds > 0.87);
        Check(result.Accumulator >= 0 && result.Accumulator < result.FixedDeltaSeconds);
        Check(result.TotalDroppedSeconds == result.DroppedSeconds);
    }),
    ("Pause resume drops debt and Step skips presentation", () => {
        var doc = Document(); var probe = new Probe(); using var play = new PlaySession(doc); play.Start(_ => probe);
        play.AdvanceFrame(1.0 / 120); play.Pause(); var before = play.Status;
        play.AdvanceFrame(20); Check(play.Status == before);
        play.Step(); Check(play.Status.Tick == 1 && probe.UpdateCount == 1 && play.State == PlayState.Paused);
        play.Resume(); play.AdvanceFrame(0); Check(play.Status.Tick == 1);
        Reject(() => play.Step());
    }),
    ("Fixed failure aborts only failed step and retains prior commits", () => {
        var doc = Document(); var probe = new Probe();
        probe.Fixed = _ => {
            var t = probe.GameObject.LocalTransform; t.Position.X++; probe.GameObject.LocalTransform = t;
            if (probe.FixedCount == 3) throw new Exception("Third step failed");
        };
        using var play = new PlaySession(doc); play.Start(_ => probe); var result = play.AdvanceFrame(4.0 / 60);
        Check(result.State == PlayState.Faulted && result.Tick == 2 && result.StepsExecuted == 2);
        Check(Object(doc).Get<TransformData>().Position.X == 2 && probe.FixedCount == 3);
        Check(result.Fault!.ObjectId == Object(doc).PersistentId && result.Fault.BindingId is not null);
        Reject(() => play.AdvanceFrame(0));
    }),
    ("Single step failure enters Faulted without Tick", () => {
        var doc = Document(); var probe = new Probe { Fixed = _ => throw new Exception("Step failed") };
        using var play = new PlaySession(doc); play.Start(_ => probe); play.Pause();
        Check(play.Step().State == PlayState.Faulted && play.Status.Tick == 0);
    }),
    ("Reentrant controls do not change ownership", () => {
        var doc = Document(); var probe = new Probe(); using var play = new PlaySession(doc);
        probe.Fixed = _ => { Reject(() => play.AdvanceFrame(0)); Reject(() => play.Stop()); Reject(() => play.Dispose()); };
        play.Start(_ => probe); play.AdvanceFrame(1.0 / 60);
        Check(play.State == PlayState.Running && play.Status.Tick == 1);
    }),
    ("Owner thread rejects all foreign control", () => {
        var doc = Document(); using var play = new PlaySession(doc); play.Start(_ => new Probe());
        Task.Run(() => { Reject(() => play.Pause()); Reject(() => play.Stop()); Reject(() => _ = play.Status); }).GetAwaiter().GetResult();
        Check(play.State == PlayState.Running);
    }),
    ("Systems use stable order after Behaviour", () => {
        var doc = Document(); var order = new List<int>(); var probe = new Probe { Fixed = _ => order.Add(0) };
        using var play = new PlaySession(doc);
        play.AddSystem(new TestSystem((_, _) => order.Add(1))); play.AddSystem(new TestSystem((_, _) => order.Add(2)));
        play.Start(_ => probe); Reject(() => play.AddSystem(new TestSystem((_, _) => { })));
        play.AdvanceFrame(1.0 / 60); Check(order.SequenceEqual(new[] { 0, 1, 2 }));
    }),
    ("Initialization factory cannot mutate World", () => {
        var doc = Document(); using var play = new PlaySession(doc);
        Reject(() => play.Start(_ => { doc.World.CreateObject("Illegal"); return new Probe(); }));
        Check(doc.World.Count == 1 && play.State == PlayState.Faulted);
    }),
    ("Stop performs at-most-once cleanup even when callbacks fail", () => {
        var doc = Document(); var probe = new Probe { Disable = () => throw new Exception("Disable failed") };
        var play = new PlaySession(doc); play.Start(_ => probe);
        Reject(() => play.Stop()); Check(play.State == PlayState.Stopped && probe.Disabled == 1 && probe.Destroyed == 1);
        play.Stop(); play.Dispose(); play.Dispose(); Check(probe.Disabled == 1 && probe.Destroyed == 1);
    }),
    ("Play clone preserves complete edit document and history", () => {
        var edit = Document(); var session = new EditSession(edit); byte[] before = edit.CaptureBytes(); var state = session.State;
        var clone = new SceneDocument(); clone.RestoreBytes(before);
        var probe = new Probe();
        probe.Fixed = _ => { var t = probe.GameObject.LocalTransform; t.Position.X = 123; probe.GameObject.LocalTransform = t; };
        using var play = new PlaySession(clone); play.Start(_ => probe); play.AdvanceFrame(1.0 / 60); play.Stop();
        Check(before.SequenceEqual(edit.CaptureBytes()) && session.State == state);
        Check(clone.World.Identity != edit.World.Identity && Object(clone).PersistentId == Object(edit).PersistentId);
    }),
    ("Stopped session and invalid policy reject controls", () => {
        var doc = Document(); using var play = new PlaySession(doc);
        Reject(() => play.AdvanceFrame(0)); Reject(() => play.Pause()); Reject(() => play.Step());
        Reject(() => new PlaySession(doc, (FrameTimePolicy)123));
        play.Start(_ => new Probe()); Reject(() => play.Start(_ => new Probe())); play.Stop();
        play.Start(_ => new Probe()); Check(play.State == PlayState.Running && play.Fault is null);
    }),
    ("Accumulated dropped-time overflow rejects before simulation", () => {
        var doc = Document(); using var play = new PlaySession(doc); play.Start(_ => new Probe());
        play.AdvanceFrame(double.MaxValue); var before = play.Status;
        Reject(() => play.AdvanceFrame(double.MaxValue));
        Check(play.Status == before);
    }),
    ("Faulted frame reports clamp loss in cumulative diagnostics", () => {
        var doc = Document(); var probe = new Probe { Fixed = _ => throw new Exception("Failed") };
        using var play = new PlaySession(doc); play.Start(_ => probe); var status = play.AdvanceFrame(1);
        Check(status.State == PlayState.Faulted && status.StepsExecuted == 0);
        Check(Math.Abs(status.DroppedSeconds - 0.75) < 1e-12 && status.TotalDroppedSeconds == status.DroppedSeconds);
    }),
    ("Frame counter overflow rejects before advancing", () => {
        var doc = Document(); using var play = new PlaySession(doc); play.Start(_ => new Probe());
        typeof(PlaySession).GetField("_frameCount", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(play, ulong.MaxValue);
        var before = play.Status; Reject(() => play.AdvanceFrame(1.0 / 60)); Check(play.Status == before);
    }),
    ("Null Play document is rejected explicitly", () => { Reject(() => new PlaySession(null!)); }),
    ("Fault message remains bounded", () => {
        var doc = Document(); var probe = new Probe { Fixed = _ => throw new Exception(new string('X', 4096)) };
        using var play = new PlaySession(doc); play.Start(_ => probe); play.AdvanceFrame(1.0 / 60);
        Check(play.Fault!.Message.Length == 512);
    }),
};
int passed = 0;
foreach (var test in cases)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); passed++; }
    catch (Exception error) { Console.Error.WriteLine("FAIL " + test.Name + ": " + error); return 1; }
}
Console.WriteLine($"Gameplay tests: {passed}/{cases.Length} passed.");
try { MovementCases.Run(); }
catch(Exception error){Console.Error.WriteLine(error);return 1;}
if (args.Contains("--benchmark"))
{
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { environment = Environment.OSVersion.ToString(), architecture = RuntimeInformation.ProcessArchitecture.ToString(), runtime = RuntimeInformation.FrameworkDescription, cpuCount = Environment.ProcessorCount }));
    foreach (int steps in new[] { 0, 1, 8 })
    foreach (int commands in new[] { 0, 64, 1024 })
    {
        var doc = Document(); var probe = new Probe(); using var play = new PlaySession(doc); var id = Object(doc).PersistentId;
        probe.Fixed = _ => { for (int i = 0; i < commands; i++) probe.Context.Commands.Rename(id, "Profile " + i); };
        play.Start(_ => probe); var times = new List<double>(); var allocations = new List<long>();
        for (int iteration = 0; iteration < 40; iteration++)
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread(); var clock = System.Diagnostics.Stopwatch.StartNew();
            var result = play.AdvanceFrame(steps / 60.0); clock.Stop();
            if (result.State != PlayState.Running) throw new Exception("Benchmark simulation failed.");
            if (iteration < 8) continue;
            times.Add(clock.Elapsed.TotalMilliseconds); allocations.Add(GC.GetAllocatedBytesForCurrentThread() - bytes);
        }
        times.Sort(); allocations.Sort();
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { steps, commandsPerSuccessfulStep = commands, samples = times.Count,
            medianMs = times[times.Count / 2], p95Ms = times[(int)Math.Ceiling(times.Count * 0.95) - 1], maxMs = times[^1],
            medianFrameAllocationBytes = allocations[allocations.Count / 2], maxFrameAllocationBytes = allocations[^1] }));
    }
}
if (args.Contains("--pressure-benchmark")) PressureProfiles.Run();
return 0;

sealed class Probe : Behaviour
{
    public int Created, Enabled, Disabled, Destroyed, FixedCount, UpdateCount;
    public double Elapsed;
    public Action? Create, Disable;
    public Action<double>? Fixed, Update;
    protected override void OnCreate() { Created++; Create?.Invoke(); }
    protected override void OnEnable() { Enabled++; }
    protected override void OnDisable() { Disabled++; Disable?.Invoke(); }
    protected override void OnDestroy() { Destroyed++; }
    protected override void OnFixedUpdate(double delta) { FixedCount++; Elapsed += delta; Fixed?.Invoke(delta); }
    protected override void OnUpdate(double delta) { UpdateCount++; Update?.Invoke(delta); }
}
sealed class TestSystem(Action<World, double> action) : IWorldSystem
{
    public void FixedUpdate(World world, double fixedDeltaSeconds) => action(world, fixedDeltaSeconds);
}
