using Ncma;
using Ncma.Gameplay;
using Ncma.Scene;

internal static class GameplayUiCases
{
    private static void Check(bool value, string message = "Gameplay UI assertion failed")
    { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; }
        throw new Exception("Invalid UI input accepted.");
    }
    private static GameplayUiAction Action(ulong sequence = 1) =>
        new(sequence, Guid.Parse("785b512d-d59f-48b3-9d5c-5fd4f9ca8a41"), "hud.activate", GameplayUiActionKind.Activate);
    private sealed class Fixture : IDisposable
    {
        internal readonly RuntimeSceneInstance Scene;
        internal readonly PlaySession Play;
        internal readonly UiProbe Probe = new();
        internal Fixture(bool start = true)
        {
            var source = new SceneDocument("UI input fixture");
            var obj = source.CreateObject("Before");
            source.SetBindings(obj, [new(Guid.NewGuid(), "Test.UiProbe", true, [])]);
            Scene = source.BakeRuntime(source.Revision).Image.Instantiate(source.Components);
            Play = new(Scene, FrameTimePolicy.Strict);
            Play.RegisterUiAction("hud.activate", GameplayUiActionKind.Activate);
            Play.RegisterUiAction("hud.value", GameplayUiActionKind.ValueChanged);
            Play.RegisterUiAction("hud.text", GameplayUiActionKind.TextChanged);
            if (start) Play.Start(_ => Probe);
        }
        internal GameplayUiStamp Bind() => Play.BindUiInput(Guid.NewGuid(), 1);
        internal void Step() => Play.AdvanceFrame(1d / 60);
        public void Dispose() => Play.Dispose();
    }
    private sealed class UiProbe : Behaviour
    {
        internal Action? Fixed;
        internal GameplayUiInput? Last;
        internal readonly List<int> Counts = [];
        internal bool OutsideEmpty = true;
        protected override void OnCreate() => OutsideEmpty &= Context.UiInput.Count == 0;
        protected override void OnFixedUpdate(double delta)
        { Last = Context.UiInput; Counts.Add(Last.Count); Fixed?.Invoke(); }
        protected override void OnUpdate(double delta) => OutsideEmpty &= Context.UiInput.Count == 0;
        protected override void OnDisable() => OutsideEmpty &= Context.UiInput.Count == 0;
    }
    internal static void Run()
    {
        var cases = new (string Name, Action Run)[] {
            ("Startup exact registrations, limits and immutable catalog", () => {
                using var f = new Fixture(false);
                foreach (var name in new[] { "", "has space", "代码", "x/y", new string('a', 97) })
                    Reject(() => f.Play.RegisterUiAction(name, GameplayUiActionKind.Activate));
                Reject(() => f.Play.RegisterUiAction(null!, GameplayUiActionKind.Activate));
                Reject(() => f.Play.RegisterUiAction("hud.activate", GameplayUiActionKind.Activate));
                Reject(() => f.Play.RegisterUiAction("new", (GameplayUiActionKind)999));
                Check(f.Play.AcceptsUiAction("hud.activate", GameplayUiActionKind.Activate));
                Check(!f.Play.AcceptsUiAction("HUD.activate", GameplayUiActionKind.Activate));
                Check(!f.Play.AcceptsUiAction("hud.activate", GameplayUiActionKind.TextChanged));
                for (int i = 3; i < PlaySession.MaxUiActions; i++) f.Play.RegisterUiAction("route." + i, GameplayUiActionKind.Activate);
                Reject(() => f.Play.RegisterUiAction("overflow", GameplayUiActionKind.Activate));
                f.Play.Start(_ => f.Probe); Reject(() => f.Play.RegisterUiAction("late", GameplayUiActionKind.Activate));
                f.Play.Stop(); Reject(() => f.Play.RegisterUiAction("restart", GameplayUiActionKind.Activate));
            }),
            ("No UI is opt-in and empty phases do not allocate input snapshots", () => {
                using var f = new Fixture();
                Reject(() => _ = f.Play.CurrentUiInputStamp);
                Reject(() => f.Play.SubmitUiActions(default, [Action()]));
                var empty = f.Play.UiInput;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 64; i++) Check(ReferenceEquals(empty, f.Play.UiInput));
                Check(GC.GetAllocatedBytesForCurrentThread() == before);
                f.Step(); Check(f.Probe.Last!.Count == 0 && f.Probe.OutsideEmpty);
            }),
            ("Copied proposals are fixed-step-only and gameplay uses existing command commit", () => {
                using var f = new Fixture(); var stamp = f.Bind(); var action = Action();
                var source = new[] { action }; var bytes = f.Scene.CaptureCheckpoint().CopyBytes();
                f.Play.SubmitUiActions(stamp, source); source[0] = Action(999);
                Check(f.Scene.CaptureCheckpoint().CopyBytes().SequenceEqual(bytes) && f.Play.UiInput.Count == 0);
                f.Probe.Fixed = () => {
                    Check(f.Probe.Context.UiInput[0] == action && f.Probe.Context.UiInput.Stamp == stamp);
                    f.Probe.Context.Commands.Rename(f.Scene.World.GetUuid(f.Scene.World.GetEntities()[0]), "Accepted");
                    Check(f.Scene.World.GetName(f.Scene.World.GetEntities()[0]) == "Before");
                };
                f.Step(); var retained = f.Probe.Last!;
                Check(f.Play.Tick == 1 && f.Scene.World.GetName(f.Scene.World.GetEntities()[0]) == "Accepted" && retained.Count == 1);
                f.Probe.Fixed = null; f.Step(); Check(f.Probe.Last!.Count == 0 && retained[0] == action);
                f.Play.Stop(); Check(f.Probe.OutsideEmpty && f.Play.UiInput.Count == 0 && retained.Stamp == stamp);
            }),
            ("Repeated render frames retain input, catch-up steps consume only once", () => {
                using var f = new Fixture(); var stamp = f.Bind(); f.Play.SubmitUiActions(stamp, [Action()]);
                f.Play.AdvanceFrame(0); f.Play.AdvanceFrame(0); Check(f.Probe.Counts.Count == 0);
                f.Play.AdvanceFrame(3d / 60); Check(f.Probe.Counts.SequenceEqual(new[] { 1, 0, 0 }));
                Reject(() => f.Play.SubmitUiActions(stamp, [Action(2)]));
                var next = f.Play.CurrentUiInputStamp; Check(next.Tick == 3 && next.Admission == stamp.Admission);
                Reject(() => f.Play.SubmitUiActions(next, [Action()]));
                f.Play.SubmitUiActions(next, [Action(2)]); f.Step(); Check(f.Probe.Last!.Count == 1);
            }),
            ("Entire malformed batch rejects without partial queue or sequence advance", () => {
                using var f = new Fixture(); var stamp = f.Bind();
                var invalid = new[] {
                    Action(2) with { Action = "unknown" }, Action(2) with { Action = "HUD.activate" },
                    Action(2) with { Action = null! }, Action(2) with { ElementId = Guid.Empty },
                    Action(2) with { Sequence = 0 }, Action(2) with { Sequence = 1 },
                    Action(2) with { Kind = GameplayUiActionKind.TextChanged }, Action(2) with { Kind = (GameplayUiActionKind)999 },
                    Action(2) with { Value = float.NaN }, Action(2) with { Value = float.PositiveInfinity },
                    Action(2) with { Value = .5f }, Action(2) with { Text = "unexpected" }, Action(2) with { Text = null! }
                };
                foreach (var bad in invalid) Reject(() => f.Play.SubmitUiActions(stamp, [Action(), bad]));
                f.Play.SubmitUiActions(stamp, [Action(), Action(2)]); f.Step(); Check(f.Probe.Last!.Count == 2);
            }),
            ("All stamp dimensions reject cross-world/session/generation/revision/tick replay", () => {
                using var f = new Fixture(); var stamp = f.Bind();
                foreach (var bad in new[] {
                    stamp with { SessionId = Guid.NewGuid() }, stamp with { WorldId = Guid.NewGuid() },
                    stamp with { SceneRevision = stamp.SceneRevision + 1 }, stamp with { Tick = stamp.Tick + 1 },
                    stamp with { InstanceId = Guid.NewGuid() }, stamp with { Generation = stamp.Generation + 1 },
                    stamp with { Admission = Guid.NewGuid() }, default
                }) Reject(() => f.Play.SubmitUiActions(bad, [Action()]));
                using var other = new Fixture(); other.Bind(); Reject(() => other.Play.SubmitUiActions(stamp, [Action()]));
                f.Play.SubmitUiActions(stamp, [Action()]); f.Step(); Check(f.Probe.Last!.Count == 1);
            }),
            ("Queue cap is aggregate across batches and rejects atomically", () => {
                using var f = new Fixture(); var stamp = f.Bind();
                var full = Enumerable.Range(1, PlaySession.MaxUiActions).Select(i => Action((ulong)i)).ToArray();
                Reject(() => f.Play.SubmitUiActions(stamp, full.Append(Action(129)).ToArray()));
                f.Play.SubmitUiActions(stamp, full.AsSpan(0, 64)); f.Play.SubmitUiActions(stamp, full.AsSpan(64));
                Reject(() => f.Play.SubmitUiActions(stamp, [Action(129)]));
                f.Step(); Check(f.Probe.Last!.Count == 128 && f.Probe.Last[127].Sequence == 128);
                f.Play.SubmitUiActions(f.Play.CurrentUiInputStamp, [Action(129)]); f.Step(); Check(f.Probe.Last!.Count == 1);
            }),
            ("Text values are bounded Unicode and aggregate text capacity is enforced", () => {
                using var f = new Fixture(); var stamp = f.Bind();
                GameplayUiAction Text(ulong seq, string text) => Action(seq) with {
                    Action = "hud.text", Kind = GameplayUiActionKind.TextChanged, Text = text };
                foreach (var bad in new[] { "\ud800", "\udc00", "\ud800x", "x\0y", new string('x', 16385) })
                    Reject(() => f.Play.SubmitUiActions(stamp, [Text(1, bad)]));
                string max = new('x', PlaySession.MaxUiTextLength);
                f.Play.SubmitUiActions(stamp, [Text(1, max), Text(2, max), Text(3, max), Text(4, max)]);
                Reject(() => f.Play.SubmitUiActions(stamp, [Text(5, "x")]));
                f.Step(); Check(f.Probe.Last!.Count == 4);
                const string unicode = "你好👩‍💻e\u0301\n";
                f.Play.SubmitUiActions(f.Play.CurrentUiInputStamp, [Text(5, unicode)]); f.Step(); Check(f.Probe.Last![0].Text == unicode);
            }),
            ("Value changes require finite normalized scalar and exact registered kind", () => {
                using var f = new Fixture(); var stamp = f.Bind();
                var value = Action() with { Action = "hud.value", Kind = GameplayUiActionKind.ValueChanged, Value = .75f };
                foreach (float bad in new[] { -.1f, 1.1f, float.NaN, float.NegativeInfinity })
                    Reject(() => f.Play.SubmitUiActions(stamp, [value with { Value = bad }]));
                Reject(() => f.Play.SubmitUiActions(stamp, [value with { Text = "x" }]));
                f.Play.SubmitUiActions(stamp, [value]); f.Step(); Check(f.Probe.Last![0] == value);
            }),
            ("Explicit cancellation rotates admission even for the same instance/generation", () => {
                using var f = new Fixture(); var stamp = f.Bind(); f.Play.SubmitUiActions(stamp, [Action()]);
                f.Play.CancelUiInput(); Reject(() => f.Play.SubmitUiActions(stamp, [Action(2)]));
                var rebound = f.Play.BindUiInput(stamp.InstanceId, stamp.Generation);
                Check(rebound.Admission != stamp.Admission); Reject(() => f.Play.SubmitUiActions(stamp, [Action(2)]));
                f.Step(); Check(f.Probe.Last!.Count == 0);
                f.Play.SubmitUiActions(f.Play.CurrentUiInputStamp, [Action()]); f.Step(); Check(f.Probe.Last!.Count == 1);
            }),
            ("Invalid binding is atomic; a valid new generation drops old pending actions", () => {
                using var f = new Fixture(); var stamp = f.Bind(); f.Play.SubmitUiActions(stamp, [Action()]);
                Reject(() => f.Play.BindUiInput(Guid.Empty, 1)); Reject(() => f.Play.BindUiInput(Guid.NewGuid(), 0));
                Check(f.Play.CurrentUiInputStamp == stamp); f.Step(); Check(f.Probe.Last!.Count == 1);
                var current = f.Play.CurrentUiInputStamp; f.Play.SubmitUiActions(current, [Action(2)]);
                f.Play.BindUiInput(stamp.InstanceId, 2); Reject(() => f.Play.SubmitUiActions(current, [Action(3)]));
                f.Step(); Check(f.Probe.Last!.Count == 0);
            }),
            ("Pause, single-step and resume never resurrect queued HUD input", () => {
                using var f = new Fixture(); var stamp = f.Bind(); f.Play.SubmitUiActions(stamp, [Action()]); f.Play.Pause();
                Reject(() => f.Play.SubmitUiActions(stamp, [Action(2)])); Reject(() => f.Play.BindUiInput(Guid.NewGuid(), 1));
                f.Play.Step(); Check(f.Probe.Last!.Count == 0); f.Play.Resume();
                Reject(() => f.Play.SubmitUiActions(stamp, [Action(2)]));
                f.Play.SubmitUiActions(f.Bind(), [Action()]); f.Step(); Check(f.Probe.Last!.Count == 1);
            }),
            ("Reload failure and successful reload both revoke old UI observations", () => {
                using var f = new Fixture(); var stamp = f.Bind(); f.Play.SubmitUiActions(stamp, [Action()]);
                Reject(() => f.Play.Reload(_ => throw new ArgumentException("preflight")));
                Check(f.Play.State == PlayState.Paused); f.Play.Resume(); Reject(() => f.Play.SubmitUiActions(stamp, [Action(2)]));
                stamp = f.Bind(); f.Play.SubmitUiActions(stamp, [Action()]); var fresh = new UiProbe(); f.Play.Reload(_ => fresh);
                f.Play.Resume(); Reject(() => f.Play.SubmitUiActions(stamp, [Action(2)])); f.Step(); Check(fresh.Last!.Count == 0);
            }),
            ("World or metadata revision change drops queued observations before fixed update", () => {
                foreach (bool metadata in new[] { false, true }) {
                    using var f = new Fixture(); var stamp = f.Bind(); f.Play.SubmitUiActions(stamp, [Action()]);
                    global::Ncma.Runtime.Entity obj = f.Scene.World.GetEntities()[0];
                    if (metadata) {
                        var id = f.Scene.World.GetUuid(obj); var world = f.Scene.World.Identity;
                        RuntimeSceneTestTransactions.CommitMetadata(f.Scene, bindings => bindings[id] = [bindings[id][0] with {
                            Exports = [new("RevisionSignal", ExportKind.Double, 1)] }]);
                        Check(f.Scene.World.Identity == world && f.Scene.World.GetName(obj) == "Before" &&
                            f.Scene.GetBindings(id)[0].Exports[0].Name == "RevisionSignal");
                    }
                    else f.Scene.World.Rename(obj, "External");
                    Check(f.Play.CurrentUiInputStamp.SceneRevision != stamp.SceneRevision);
                    Reject(() => f.Play.SubmitUiActions(stamp, [Action(2)]));
                    f.Step(); Check(f.Probe.Last!.Count == 0); Reject(() => _ = f.Play.CurrentUiInputStamp);
                }
            }),
            ("Caught reentry cannot submit, rebind, cancel or register during fixed update", () => {
                using var f = new Fixture(); var stamp = f.Bind(); f.Play.SubmitUiActions(stamp, [Action()]);
                f.Probe.Fixed = () => {
                    Reject(() => f.Play.SubmitUiActions(stamp, [Action(2)])); Reject(() => f.Play.CancelUiInput());
                    Reject(() => f.Bind()); Reject(() => f.Play.RegisterUiAction("reentry", GameplayUiActionKind.Activate));
                    Reject(() => _ = f.Play.CurrentUiInputStamp); Check(f.Probe.Context.UiInput.Count == 1);
                };
                f.Step(); Check(f.Play.State == PlayState.Running && f.Play.Tick == 1);
            }),
            ("Owner thread and disposal guards also apply to immutable input access", () => {
                var f = new Fixture(); var stamp = f.Bind();
                Task.Run(() => {
                    Reject(() => f.Play.SubmitUiActions(stamp, [Action()])); Reject(() => f.Play.CancelUiInput());
                    Reject(() => f.Bind()); Reject(() => _ = f.Play.UiInput); Reject(() => _ = f.Play.CurrentUiInputStamp);
                    Reject(() => f.Play.AcceptsUiAction("hud.activate", GameplayUiActionKind.Activate));
                }).GetAwaiter().GetResult();
                f.Dispose(); Reject(() => _ = f.Play.UiInput); Reject(() => f.Bind());
            }),
            ("Fault aborts scene commands, revokes UI input and requires explicit new Play", () => {
                using var f = new Fixture(); var stamp = f.Bind(); var bytes = f.Scene.CaptureCheckpoint().CopyBytes();
                f.Play.SubmitUiActions(stamp, [Action()]);
                f.Probe.Fixed = () => {
                    Check(f.Probe.Context.UiInput.Count == 1);
                    f.Probe.Context.Commands.Rename(f.Scene.World.GetUuid(f.Scene.World.GetEntities()[0]), "Must not commit");
                    throw new InvalidOperationException("gameplay failure");
                };
                f.Step(); Check(f.Play.State == PlayState.Faulted && f.Play.Tick == 0 && f.Scene.CaptureCheckpoint().CopyBytes().SequenceEqual(bytes));
                Check(f.Play.UiInput.Count == 0); Reject(() => f.Play.SubmitUiActions(stamp, [Action(2)]));
                f.Play.Stop(); f.Probe.Fixed = null; f.Play.Start(_ => f.Probe);
                Reject(() => f.Play.SubmitUiActions(stamp, [Action(2)])); f.Step(); Check(f.Probe.Last!.Count == 0);
            }),
            ("Headless fixed-step sessions use identical UI input consumption without OnUpdate", () => {
                var source = new SceneDocument();
                var scene = source.BakeRuntime(source.Revision).Image.Instantiate(source.Components);
                using var play = new PlaySession(scene, FrameTimePolicy.Strict, advanceMode: PlayAdvanceMode.FixedSteps);
                play.RegisterUiAction("hud.activate", GameplayUiActionKind.Activate); play.Start(_ => throw new Exception("empty"));
                var stamp = play.BindUiInput(Guid.NewGuid(), 1); play.SubmitUiActions(stamp, [Action()]);
                Check(play.AdvanceFixedStep().Tick == 1); Reject(() => play.SubmitUiActions(stamp, [Action(2)]));
                play.SubmitUiActions(play.CurrentUiInputStamp, [Action(2)]); Check(play.AdvanceFixedStep().Tick == 2);
            })
        };
        foreach (var test in cases) {
            try { test.Run(); Console.WriteLine("PASS gameplay UI: " + test.Name); }
            catch (Exception e) { throw new InvalidOperationException("FAIL gameplay UI: " + test.Name, e); }
        }
        Console.WriteLine($"Gameplay UI tests: {cases.Length}/{cases.Length} groups passed.");
    }
}
