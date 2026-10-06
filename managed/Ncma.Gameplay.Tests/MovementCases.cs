using System.Numerics;
using Vector3 = System.Numerics.Vector3;
using Ncma;
using Ncma.Gameplay;
using Ncma.Movement;
using Ncma.Runtime;
using Ncma.Scene;

internal static class MovementCases
{
    private static void Check(bool value, string message = "Movement assertion failed")
    { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or AggregateException) { return; }
        throw new InvalidOperationException("Invalid movement operation accepted.");
    }

    internal static void Run()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("Movement authority permits one validated publication and copied committed reads", () => {
                using var f = new Fixture(); f.Start();
                f.Probe.Fixed = _ => { f.Submit(Vector3.UnitX); Check(f.Target.Get<TransformData>().Position == Vector3.Zero); };
                var state = f.Play.AdvanceFixedStep();
                Check(state.State == PlayState.Running && state.Tick == 1 && f.Adapter.Steps == 1);
                Check(f.Target.Get<TransformData>().Position == Vector3.UnitX && f.Move.Status.CommittedSequence == 1);
                var copy = f.Move.ReadCommitted(); copy[0] = default;
                Check(f.Move.ReadCommitted()[0].Transform.Position == Vector3.UnitX);
                Check(f.Move.Status.SnapshotValid && !f.Move.Status.NumericalExecutionStarted);
            }),
            ("Caught SDK Behaviour writes poison candidate before any numerical call", () => {
                using var f = new Fixture(); f.Start();
                var before = f.Document.CaptureBytes();
                f.Probe.Fixed = _ => { Reject(() => f.Probe.GameObject.LocalTransform = Transform.Identity); f.Submit(Vector3.UnitX); };
                f.Play.AdvanceFixedStep(); f.CheckPreExecutionFailure(before);
            }),
            ("Caught System writes cannot impersonate movement authority", () => {
                using var f = new Fixture();
                f.Play.AddSystem(new TestSystem((_, _) => Reject(() => f.Target.Set(TransformData.Identity))));
                f.Start(); var before = f.Document.CaptureBytes(); f.Play.AdvanceFixedStep(); f.CheckPreExecutionFailure(before);
            }),
            ("Batch authority preflight prevents an unbound prefix from being installed", () => {
                using var f = new Fixture(); f.Start(); var before = f.Document.CaptureBytes();
                var sdkOther = f.Play.Facade.GetObjects().Single(o => o.Name == "Other");
                Reject(() => f.Play.Facade.WriteTransforms([
                    new(sdkOther, Transform.Identity with { Position = new Ncma.Vector3(9, 0, 0) }),
                    new(f.Probe.GameObject, Transform.Identity)]));
                Check(f.Document.CaptureBytes().SequenceEqual(before));
                f.Probe.Fixed = _ => Reject(() => f.Play.Facade.WriteTransforms([new(f.Probe.GameObject, Transform.Identity)]));
                f.Play.AdvanceFixedStep(); f.CheckPreExecutionFailure(before);
            }),
            ("Bound structure is rejected in command and direct paths even if caught", () => {
                foreach (int mode in Enumerable.Range(0, 5))
                {
                    using var f = new Fixture(); f.Start(); var before = f.Document.CaptureBytes();
                    f.Probe.Fixed = _ => Reject(() => {
                        switch (mode)
                        {
                            case 0: f.Play.Commands.Destroy(f.Target.PersistentId); break;
                            case 1: f.Play.Commands.RemoveComponent(f.Target.PersistentId, "ncma.transform"); break;
                            case 2: f.Play.Commands.AddComponent(f.Target.PersistentId, new("ncma.transform", 1, f.Document.World.Components.Encode(TransformData.Identity))); break;
                            case 3: f.Target.Destroy(); break;
                            case 4: f.Target.Remove<TransformData>(); break;
                        }
                    });
                    f.Play.AdvanceFixedStep(); f.CheckPreExecutionFailure(before);
                }
            }),
            ("Outside-step Transform, structure and restore stay locked until Stop", () => {
                using var f = new Fixture(); f.Start(); var bytes = f.Document.CaptureBytes();
                Reject(() => f.Target.Set(TransformData.Identity)); Reject(() => f.Target.Destroy());
                Reject(() => f.Target.Remove<TransformData>()); Reject(() => f.Document.RestoreBytes(bytes));
                f.Play.Stop(); f.Target.Set(TransformData.Identity); f.Document.RestoreBytes(bytes);
            }),
            ("Unbound writes and structural commands preserve their original transaction", () => {
                using var f = new Fixture(); f.Start();
                f.Probe.Fixed = _ => {
                    f.Other.Set(TransformData.Identity with { Position = Vector3.UnitZ });
                    var created = f.Play.Commands.SpawnEmpty("Unbound spawn");
                    f.Play.Commands.AddComponent(created.ObjectId, new("ncma.transform", 1, f.Document.World.Components.Encode(TransformData.Identity)));
                    f.Play.Commands.Rename(f.Target.PersistentId, "Renamed bound object");
                };
                f.Play.AdvanceFixedStep(); Check(f.Play.State == PlayState.Running && f.Document.World.Count == 3);
                Check(f.Other.Get<TransformData>().Position == Vector3.UnitZ && f.Target.Name == "Renamed bound object");
            }),
            ("Initialization cannot write reserved Transform and never steps numerics", () => {
                using var f = new Fixture(); f.Probe.Create = () => Reject(() => f.Probe.GameObject.LocalTransform = Transform.Identity);
                Reject(f.Start); Check(f.Play.State == PlayState.Faulted && f.Adapter.Steps == 0 && f.Play.Tick == 0);
            }),
            ("Old tick, revision, world, session and numerical sequence intents poison step", () => {
                foreach (int mode in Enumerable.Range(0, 5))
                {
                    using var f = new Fixture(); f.Start(); var before = f.Document.CaptureBytes();
                    f.Probe.Fixed = _ => {
                        var stamp = f.Move.CurrentStep;
                        stamp = mode switch { 0 => stamp with { FromTick = stamp.FromTick + 1 },
                            1 => stamp with { Revision = stamp.Revision + 1 }, 2 => stamp with { WorldId = Guid.NewGuid() },
                            3 => stamp with { SessionId = Guid.NewGuid() }, _ => stamp with { Sequence = stamp.Sequence + 1 } };
                        Reject(() => f.Move.Submit(new(stamp, f.Target.PersistentId, Vector3.UnitX, 0)));
                    };
                    f.Play.AdvanceFixedStep(); f.CheckPreExecutionFailure(before);
                }
            }),
            ("Duplicate foreign and nonfinite intents are bounded and fail closed", () => {
                foreach (int mode in Enumerable.Range(0, 6))
                {
                    using var f = new Fixture(); f.Start(); var before = f.Document.CaptureBytes();
                    f.Probe.Fixed = _ => {
                        if (mode == 0) f.Submit(Vector3.UnitX);
                        Reject(() => f.Move.Submit(new(f.Move.CurrentStep, mode == 1 ? f.Other.PersistentId : f.Target.PersistentId,
                            mode switch { 2 => new(float.NaN, 0, 0), 3 => new(1001, 0, 0), _ => Vector3.UnitX },
                            mode switch { 4 => float.PositiveInfinity, 5 => 4, _ => 0 })));
                    };
                    f.Play.AdvanceFixedStep(); f.CheckPreExecutionFailure(before);
                }
            }),
            ("Movement submission is forbidden outside simulation and in readonly presentation", () => {
                using var f = new Fixture(PlayAdvanceMode.Frames); f.Start();
                Reject(() => _ = f.Move.CurrentStep); Reject(() => f.Move.Submit(default));
                f.Probe.Update = _ => Reject(() => f.Move.Submit(default));
                f.Play.AdvanceFrame(1d / 60); Check(f.Play.State == PlayState.Running && f.Adapter.Steps == 1);
            }),
            ("Gameplay error aborts input, signal sends and receives before numerics", () => {
                using var f = new Fixture(); f.Start(); f.SeedInputAndSignal(); var before = f.Document.CaptureBytes();
                f.Probe.Fixed = _ => { f.StageSignals(); throw new InvalidOperationException("gameplay fault"); };
                f.Play.AdvanceFixedStep(); f.CheckPreExecutionFailure(before); f.CheckUnconsumedInputAndSignal();
            }),
            ("Numerical preflight error does not advance numerical state", () => {
                using var f = new Fixture(); f.Adapter.FailPreflight = true; f.Start(); var before = f.Document.CaptureBytes();
                f.Play.AdvanceFixedStep(); f.CheckPreExecutionFailure(before);
                Check(f.Play.Fault!.Phase == "coupled_preflight");
            }),
            ("Caught preflight callback write poisons candidate before Execute", () => {
                using var f = new Fixture(); f.Start(); var before = f.Document.CaptureBytes();
                f.Adapter.PreflightAction = () => Reject(() => f.Other.Set(TransformData.Identity));
                f.Play.AdvanceFixedStep(); f.CheckPreExecutionFailure(before);
            }),
            ("Partial numerical failure invalidates coupled snapshot without lying about rollback", () => {
                using var f = new Fixture(); f.Start(); f.SeedInputAndSignal(); var before = f.Document.CaptureBytes();
                f.Adapter.FailExecute = true; f.Probe.Fixed = _ => { f.StageSignals(); f.Submit(Vector3.UnitX); };
                f.Play.AdvanceFixedStep(); f.CheckPostExecutionFailure(before); f.CheckUnconsumedInputAndSignal();
                Check(f.Adapter.Position == Vector3.UnitX && f.Target.Get<TransformData>().Position == Vector3.Zero);
                Reject(() => f.Play.Resume()); Reject(() => f.Play.AdvanceFixedStep()); Reject(() => f.Move.ReadCommitted());
            }),
            ("Malformed stamp/count/target/finite/scale/quaternion results reject the whole candidate", () => {
                foreach (int mode in Enumerable.Range(0, 7))
                {
                    using var f = new Fixture(); f.Start(); var before = f.Document.CaptureBytes(); f.Adapter.BadResult = mode;
                    f.Probe.Fixed = _ => f.Submit(Vector3.UnitX);
                    f.Play.AdvanceFixedStep(); f.CheckPostExecutionFailure(before);
                }
            }),
            ("Caught numerical callback write causes post-execution fail-stop", () => {
                using var f = new Fixture(); f.Start(); var before = f.Document.CaptureBytes();
                f.Adapter.ExecuteAction = () => Reject(() => f.Other.Set(TransformData.Identity));
                f.Play.AdvanceFixedStep(); f.CheckPostExecutionFailure(before);
            }),
            ("Final managed prepare may fail after numerical execution and retains prior World", () => {
                using var f = new Fixture(); f.Start(); f.SeedInputAndSignal(); var before = f.Document.CaptureBytes();
                f.Probe.Fixed = _ => {
                    f.StageSignals(); f.Submit(Vector3.UnitX);
                    var pending = f.Play.Commands.SpawnEmpty("Factory fails after numeric boundary");
                    f.Play.Commands.AttachBehaviour(pending.ObjectId, new(Guid.NewGuid(), "Test.Missing", true, []));
                };
                f.Play.AdvanceFixedStep(); f.CheckPostExecutionFailure(before); f.CheckUnconsumedInputAndSignal();
                Check(f.Play.Fault!.Phase == "prepare" && f.Document.World.Count == 2);
            }),
            ("A later failed quantum preserves earlier committed World and sequence", () => {
                using var f = new Fixture(); f.Start(); f.Probe.Fixed = _ => f.Submit(Vector3.UnitX);
                f.Play.AdvanceFixedStep(); var before = f.Document.CaptureBytes();
                f.Adapter.FailExecute = true; f.Play.AdvanceFixedStep();
                Check(f.Play.State == PlayState.Faulted && f.Play.Tick == 1 && f.Move.Status.CommittedSequence == 1);
                Check(f.Document.CaptureBytes().SequenceEqual(before) && f.Adapter.Steps == 2 && !f.Move.Status.SnapshotValid);
            }),
            ("Committed observer fault never rolls back a successful quantum", () => {
                using var f = new Fixture(); f.Play.AddCommittedObserver(new FaultObserver()); f.Start(); f.Probe.Fixed = _ => f.Submit(Vector3.UnitX);
                f.Play.AdvanceFixedStep(); Check(f.Play.State == PlayState.Faulted && f.Play.Tick == 1);
                Check(f.Move.Status.SnapshotValid && f.Move.Status.CommittedTick == 1 && f.Move.Status.CommittedSequence == 1);
                Check(f.Move.ReadCommitted()[0].Transform.Position == Vector3.UnitX && f.Adapter.Steps == 1);
            }),
            ("Pause single Step and render-only frames never perform extra numerics", () => {
                using var f = new Fixture(PlayAdvanceMode.Frames); f.Start();
                f.Play.AdvanceFrame(0); Check(f.Adapter.Steps == 0);
                f.Play.AdvanceFrame(1d / 120); Check(f.Adapter.Steps == 0);
                f.Play.Pause(); f.Play.AdvanceFrame(1); Check(f.Adapter.Steps == 0);
                f.Play.Step(); Check(f.Adapter.Steps == 1 && f.Play.State == PlayState.Paused);
                f.Play.Resume(); f.Play.AdvanceFrame(1d / 60); Check(f.Adapter.Steps == 2);
            }),
            ("Coupled Reload closes adapter and rebuilds startup with fresh world/session identity", () => {
                using var f = new Fixture(); f.Start(); var world = f.Document.World.Identity; var epoch = f.Play.SessionId;
                f.Probe.Fixed = _ => f.Submit(Vector3.UnitX); f.Play.AdvanceFixedStep(); var old = f.Adapter;
                var freshProbe = new Probe(); f.Play.Reload(_ => freshProbe);
                Check(f.Play.State == PlayState.Paused && f.Play.SessionId != epoch && f.Document.World.Identity != world);
                Check(old.Closed && f.Adapters.Count == 2 && f.Adapter.Steps == 0 && f.Play.Tick == 1);
                Check(f.Document.World.FindObject(f.TargetId).Get<TransformData>().Position == Vector3.Zero);
                Check(f.Move.Status.CommittedSequence == 0 && f.Move.Status.SnapshotValid);
                Reject(() => _ = f.Target.Name); f.Play.Step(); Check(f.Play.Tick == 2 && f.Adapter.Steps == 1);
            }),
            ("Fault recovery needs Stop and rebuilt startup, not Start on mutated identity", () => {
                using var f = new Fixture(); byte[] startup = f.Document.CaptureBytes(); f.Start(); var oldSession = f.Play.SessionId;
                f.Adapter.FailExecute = true; f.Play.AdvanceFixedStep(); f.Play.Stop();
                Reject(f.Start); Check(f.Play.State == PlayState.Faulted); f.Play.Stop();
                f.Document.RestoreBytes(startup); f.Start(); Check(f.Play.SessionId != oldSession && f.Adapter.Steps == 0);
                f.Play.AdvanceFixedStep(); Check(f.Play.State == PlayState.Running && f.Move.Status.SnapshotValid);
            }),
            ("Close failure retains ownership and authority; retry Stop releases them", () => {
                using var f = new Fixture(); f.Start(); f.Adapter.FailClose = true;
                Reject(() => f.Play.Dispose()); Check(f.Play.State == PlayState.Faulted && f.Move.Status.ResourcesOwned);
                Check(!f.Move.Status.SnapshotValid); Reject(() => f.Target.Set(TransformData.Identity)); Reject(() => f.Move.Dispose());
                f.Adapter.FailClose = false; f.Play.Stop(); Check(!f.Move.Status.ResourcesOwned && f.Adapter.Closed);
                f.Target.Set(TransformData.Identity); Check(f.Probe.Destroyed == 1);
            }),
            ("Coupled Reload never starts a new adapter if close fails", () => {
                using var f = new Fixture(); f.Start(); var world = f.Document.World.Identity;
                f.Adapter.FailClose = true; Reject(() => f.Play.Reload(_ => new Probe()));
                Check(f.Play.State == PlayState.Faulted && f.Adapters.Count == 1 && f.Document.World.Identity == world);
                f.Adapter.FailClose = false; f.Play.Stop();
            }),
            ("Failed initialization retains adapter until explicit Stop", () => {
                using var f = new Fixture(); f.Adapter.FailInitialize = true;
                Reject(f.Start); Check(f.Play.State == PlayState.Faulted && f.Move.Status.ResourcesOwned && f.Adapter.Steps == 0);
                f.Play.Stop(); Check(f.Adapter.Closed && !f.Move.Status.ResourcesOwned);
            }),
            ("Runtime authority is neither serialized nor copied to an isolated edit clone", () => {
                using var f = new Fixture(); f.Start(); var bytes = f.Document.CaptureBytes();
                var clone = new SceneDocument(); clone.RestoreBytes(bytes); clone.World.FindObject(f.TargetId).Set(TransformData.Identity);
                Check(!System.Text.Encoding.UTF8.GetString(bytes).Contains("authority", StringComparison.OrdinalIgnoreCase));
            }),
            ("An independent WorldRunner cannot commit while skipping reserved component publication", () => {
                using var f = new Fixture(); f.Start(); var before = f.Document.CaptureBytes();
                var runner = new WorldRunner(f.Document.World); Reject(() => runner.Advance(1d / 60));
                Check(f.Document.CaptureBytes().SequenceEqual(before) && f.Play.Tick == 0 && f.Adapter.Steps == 0);
                f.Play.AdvanceFixedStep(); Check(f.Play.State == PlayState.Running && f.Play.Tick == 1);
            }),
            ("A malformed final result in a multi-target batch publishes no prefix", () => {
                using var f = new Fixture(bothTargets: true); f.Start(); var before = f.Document.CaptureBytes();
                f.Adapter.BadResult = 8; f.Probe.Fixed = _ => f.Submit(Vector3.UnitX);
                f.Play.AdvanceFixedStep(); f.CheckPostExecutionFailure(before);
                Check(f.Target.Get<TransformData>().Position == Vector3.Zero && f.Other.Get<TransformData>().Position == Vector3.Zero);
            }),
            ("Successful movement commits signal and input consumption exactly once", () => {
                using var f = new Fixture(); f.Start(); f.SeedInputAndSignal();
                f.Probe.Fixed = _ => { f.StageSignals(); f.Submit(Vector3.UnitX); };
                f.Play.AdvanceFixedStep(); Check(!f.Play.Input.Pressed(1) && f.Move.Status.SnapshotValid);
                var signals = new GameplaySignal[4];
                Check(f.Probe.GameObject.World.ReceiveSignals(f.Probe.GameObject, signals) == 1 && signals[0].Code == 42);
            }),
            ("Owner-thread and reentrant calls are rejected without native advancement", () => {
                using var f = new Fixture(); f.Start();
                Check(Task.Run(() => { try { _ = f.Move.Status; return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult());
                f.Probe.Fixed = _ => { Reject(() => f.Play.Stop()); Reject(() => f.Play.Reload(_ => new Probe())); };
                f.Play.AdvanceFixedStep(); Check(f.Play.State == PlayState.Running && f.Adapter.Steps == 1);
            }),
            ("Numerical quaternion normalization matches the exact committed World value", () => {
                using var f = new Fixture(); f.Start(); f.Adapter.BadResult = 9;
                f.Play.AdvanceFixedStep(); Check(f.Play.State == PlayState.Running);
                Check(f.Move.ReadCommitted()[0].Transform == f.Target.Get<TransformData>());
                Check(f.Move.ReadCommitted()[0].Transform.Rotation == Quaternion.Identity);
            }),
            ("A custom registered normalizer cannot silently desynchronize numerical and World values", () => {
                bool change = false;
                var registry = new ComponentRegistry();
                var schema = ComponentRegistry.CreateDefault().Describe().Single(d => d.TypeId == "ncma.transform").Schema.GetRawText();
                registry.Register<TransformData>("ncma.transform", 1, schema,
                    v => TransformData.Validate(change ? v with { Position = v.Position + Vector3.UnitX } : v));
                using var f = new Fixture(components: registry); f.Start(); var before = f.Document.CaptureBytes();
                f.Adapter.ExecuteAction = () => change = true;
                f.Play.AdvanceFixedStep(); f.CheckPostExecutionFailure(before);
            }),
            ("32 create step close cycles release numerical ownership and write claims", () => {
                for (int i = 0; i < 32; i++)
                {
                    using var f = new Fixture(); f.Start(); f.Play.AdvanceFixedStep(); f.Play.Stop();
                    Check(f.Adapter.Closed && !f.Move.Status.ResourcesOwned); f.Target.Remove<TransformData>();
                }
            }),
            ("Duplicate missing overbudget targets and a second coordinator fail before native resources", () => {
                using var f = new Fixture();
                Reject(() => new MovementCoordinator(f.Play, [f.TargetId], () => new FakeNumerics()));
                using var other = new PlaySession(f.Document);
                Reject(() => new MovementCoordinator(other, [f.TargetId, f.TargetId], () => new FakeNumerics()));
                Reject(() => new MovementCoordinator(other, [Guid.NewGuid()], () => new FakeNumerics()));
                Reject(() => new MovementCoordinator(other, new Guid[MovementCoordinator.MaxTargets + 1], () => new FakeNumerics()));
            }),
        };
        foreach (var test in cases)
        {
            try { test.Run(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { throw new InvalidOperationException("FAIL " + test.Name, error); }
        }
        Console.WriteLine($"Movement tests: {cases.Length}/{cases.Length} passed (deterministic fake; NOT Jolt character acceptance).");
    }

    private sealed class FaultObserver : ICommittedStepObserver
    { public void StepCommitted(World world, double fixedDeltaSeconds) => throw new InvalidOperationException("observer fault"); }

    private sealed class Fixture : IDisposable
    {
        public readonly SceneDocument Document;
        public readonly Ncma.Runtime.GameObject Target, Other;
        public Guid TargetId => _targetId;
        private readonly Guid _targetId;
        public readonly Probe Probe = new();
        public readonly PlaySession Play;
        public readonly MovementCoordinator Move;
        public readonly List<FakeNumerics> Adapters = [];
        private readonly FakeNumerics _first = new();
        public FakeNumerics Adapter => Adapters.Count == 0 ? _first : Adapters[^1];
        public Fixture(PlayAdvanceMode mode = PlayAdvanceMode.FixedSteps, bool bothTargets = false, ComponentRegistry? components = null)
        {
            Document = new("Movement fixture", components);
            Target = Document.World.CreateObject("Target"); Target.Set(TransformData.Identity); _targetId = Target.PersistentId;
            Other = Document.World.CreateObject("Other"); Other.Set(TransformData.Identity);
            Document.SetBindings(TargetId, [new(Guid.NewGuid(), "Test.Probe", true, [])]);
            Play = new(Document, FrameTimePolicy.Strict, advanceMode: mode);
            Move = new(Play, bothTargets ? [TargetId, Other.PersistentId] : [TargetId], () => { var adapter = Adapters.Count == 0 ? _first : new FakeNumerics(); Adapters.Add(adapter); return adapter; });
        }
        public void Start() => Play.Start(b => b.TypeName == "Test.Missing" ? throw new ArgumentException("missing behaviour") : Probe);
        public void Submit(Vector3 delta) => Move.Submit(new(Move.CurrentStep, TargetId, delta, 0));
        public void SeedInputAndSignal()
        {
            Play.SubmitInput(new(Play.SessionId, 1, true, new ulong[8], new ulong[8], new ulong[8], 0, 0));
            var pressed = new ulong[8]; pressed[0] = 2;
            Play.SubmitInput(new(Play.SessionId, 2, true, pressed, pressed, new ulong[8], 0, 0));
            Probe.GameObject.World.SendSignal(Probe.GameObject, Probe.GameObject, 41);
        }
        public void StageSignals()
        {
            Check(Play.Input.Pressed(1));
            var signals = new GameplaySignal[1]; Check(Probe.GameObject.World.ReceiveSignals(Probe.GameObject, signals) == 1);
            Probe.GameObject.World.SendSignal(Probe.GameObject, Probe.GameObject, 42);
        }
        public void CheckUnconsumedInputAndSignal()
        {
            Check(Play.Input.Pressed(1));
            var signals = new GameplaySignal[4]; Check(Probe.GameObject.World.ReceiveSignals(Probe.GameObject, signals) == 1 && signals[0].Code == 41);
        }
        public void CheckPreExecutionFailure(byte[] before)
        {
            Check(Play.State == PlayState.Faulted && Play.Tick == 0 && Adapter.Steps == 0);
            Check(Document.CaptureBytes().SequenceEqual(before) && Move.Status.SnapshotValid && Move.Status.Faulted);
        }
        public void CheckPostExecutionFailure(byte[] before)
        {
            Check(Play.State == PlayState.Faulted && Play.Tick == 0 && Adapter.Steps == 1);
            Check(Document.CaptureBytes().SequenceEqual(before) && !Move.Status.SnapshotValid && Move.Status.NumericalExecutionStarted);
        }
        public void Dispose() { Adapter.FailClose = false; Play.Stop(); Move.Dispose(); Play.Dispose(); }
    }

    private sealed class FakeNumerics : INumericMovementAdapter
    {
        public int Steps;
        public Vector3 Position;
        public bool FailInitialize, FailPreflight, FailExecute, FailClose, Closed;
        public int BadResult = -1;
        public Action? PreflightAction, ExecuteAction;
        public void Initialize(Guid sessionId, Guid worldId, ReadOnlySpan<NumericMovementInput> startup)
        { Position = startup[0].Start.Position; if (FailInitialize) throw new InvalidOperationException("initialize fault"); }
        public void Preflight(MovementStepStamp stamp, double fixedDeltaSeconds, ReadOnlySpan<NumericMovementInput> inputs)
        { PreflightAction?.Invoke(); if (FailPreflight) throw new InvalidOperationException("preflight fault"); }
        public NumericMovementReceipt Execute(MovementStepStamp stamp, double fixedDeltaSeconds,
            ReadOnlySpan<NumericMovementInput> inputs, Span<NumericMovementResult> results)
        {
            Steps++; Position += inputs[0].Displacement;
            if (FailExecute) throw new InvalidOperationException("numerical fault after mutation");
            ExecuteAction?.Invoke();
            for (int i = 0; i < inputs.Length; i++) results[i] = new(inputs[i].ObjectId, inputs[i].Start with { Position = inputs[i].Start.Position + inputs[i].Displacement });
            if (BadResult == 2) results[0] = results[0] with { ObjectId = Guid.NewGuid() };
            if (BadResult == 3) results[0] = results[0] with { Transform = results[0].Transform with { Position = new(float.NaN, 0, 0) } };
            if (BadResult == 4) results[0] = results[0] with { Transform = results[0].Transform with { Scale = Vector3.One * 2 } };
            if (BadResult == 5) results[0] = results[0] with { Transform = results[0].Transform with { Rotation = new(0, 0, 0, 2) } };
            if (BadResult == 6) results[0] = results[0] with { Transform = results[0].Transform with { Position = new(1001, 0, 0) } };
            if (BadResult == 8) results[^1] = results[^1] with { ObjectId = Guid.NewGuid() };
            if (BadResult == 9) results[0] = results[0] with { Transform = results[0].Transform with { Rotation = new(0, 0, 0, 1.000002f) } };
            return new(BadResult == 0 ? stamp with { Sequence = stamp.Sequence + 1 } : stamp, BadResult == 1 ? 0 : inputs.Length);
        }
        public void Dispose() { if (FailClose) throw new InvalidOperationException("close fault"); Closed = true; }
    }
}
