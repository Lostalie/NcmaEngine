using Ncma.Runtime;
using Ncma.Scene;

namespace Ncma.Gameplay;

public enum PlayState : uint { Stopped, Starting, Running, Paused, Faulted, Stopping }
public enum FrameTimePolicy { Strict, Interactive }
public enum PlayAdvanceMode { Frames, FixedSteps }
public sealed record PlayFault(string Code, string Phase, Guid SessionId, Guid WorldId, ulong Tick,
    Guid? ObjectId, Guid? BindingId, string? TypeName, string Message, ulong AttemptTick);
public sealed record PlayStatus(Guid SessionId, Guid WorldId, PlayState State, ulong FrameCount, ulong Tick,
    double FixedDeltaSeconds, double SimulationSeconds, double Accumulator, double InterpolationAlpha,
    int StepsExecuted, double DroppedSeconds, double TotalDroppedSeconds, PlayFault? Fault);

// Owns lifecycle and the sole runner. Editor supplies a clone; Player owns its runtime document directly.
public sealed class PlaySession : IDisposable, IGameplayContext
{
    private sealed class Instance(Guid objectId, BehaviourBindingData binding, Behaviour behaviour)
    {
        public readonly Guid ObjectId = objectId;
        public BehaviourBindingData Binding = binding;
        public readonly Behaviour Behaviour = behaviour;
        public bool Created, Activated;
    }
    private sealed class Dispatcher(PlaySession session) : IWorldSystem
    {
        public void FixedUpdate(World world, double delta)
        {
            foreach (var item in session._instances)
                if (item.Activated && item.Binding.Enabled) session.Invoke(item, "fixed_update", () => item.Behaviour.DispatchFixedUpdate(delta));
        }
    }
    private readonly SceneDocument _document;
    private readonly SceneWorld _facade;
    private readonly WorldRunner _runner;
    private readonly FrameTimePolicy _policy;
    private readonly PlayAdvanceMode _advanceMode;
    private bool _ownsFacade;
    private Instance[] _instances = [];
    private Instance[] _retired = [];
    private Func<BehaviourBindingData, Behaviour>? _factory;
    private readonly RuntimeCommandBuffer _commands;
    public IRuntimeCommands Commands { get { Verify(); return _commands; } }
    private readonly InputBuffer _input = new();
    private readonly RenderBuffer _render = new();
    private InputState? _stepInput;
    public ulong Tick { get { Verify(); return _document.World.Tick; } }
    // Scalar presentation reads avoid allocating a complete diagnostic status for every character frame.
    public double FixedDeltaSeconds { get { Verify(); return _runner.FixedDeltaSeconds; } }
    public double InterpolationAlpha { get { Verify(); return State==PlayState.Running ? Math.Clamp(_runner.Accumulator/_runner.FixedDeltaSeconds,0,1) : 0; } }
    public InputState Input { get { Verify(); return _stepInput ?? _input.Read(); } }
    public RenderFrameView RenderView { get { Verify(); return _render.Read(Status); } }
    public void SubmitInput(InputFrame frame)
    {
        Control();
        if (State is not (PlayState.Running or PlayState.Paused)) throw new InvalidOperationException("Input requires active Play.");
        _input.Submit(frame, SessionId, State == PlayState.Running);
    }
    private bool _busy, _disposed;
    private ulong _frameCount;
    private int _steps;
    private double _dropped, _totalDropped;
    private string _phase = "control";
    private ulong _attemptTick;
    internal bool MeasureSteps { get; set; }
    internal StepProfile LastStepProfile { get; private set; }
    private Instance? _current;
    public PlaySession(SceneDocument document, FrameTimePolicy policy = FrameTimePolicy.Interactive,
        double fixedDeltaSeconds = 1.0 / 60.0, int maxStepsPerFrame = 8, PlayAdvanceMode advanceMode = PlayAdvanceMode.Frames)
        : this(document, new SceneWorld((document ?? throw new ArgumentNullException(nameof(document))).World), policy, fixedDeltaSeconds, maxStepsPerFrame, advanceMode) { _ownsFacade = true; }
    internal PlaySession(SceneDocument document, SceneWorld facade, FrameTimePolicy policy = FrameTimePolicy.Interactive,
        double fixedDeltaSeconds = 1.0 / 60.0, int maxStepsPerFrame = 8, PlayAdvanceMode advanceMode = PlayAdvanceMode.Frames)
    {
        document.VerifyAccess();
        if (document.World != facade.Runtime || !Enum.IsDefined(policy) || !Enum.IsDefined(advanceMode) ||
            advanceMode == PlayAdvanceMode.FixedSteps && policy != FrameTimePolicy.Strict) throw new ArgumentException("Invalid Play context.");
        _document = document; _facade = facade; _policy = policy; _advanceMode = advanceMode; _commands = new(this);
        _runner = new(document.World, fixedDeltaSeconds, maxStepsPerFrame, RunStep);
        _runner.AddSystem(new Dispatcher(this));
    }
    public Guid SessionId { get; private set; } = Guid.NewGuid();
    public PlayState State { get; private set; }
    public PlayFault? Fault { get; private set; }
    public SceneDocument Document => _document;
    internal SceneWorld Facade => _facade;
    public int BehaviourCount { get { Verify(); return _instances.Length; } }
    public PlayStatus Status
    {
        get
        {
            Verify();
            double accumulator = _runner.Accumulator;
            double alpha = State == PlayState.Running ? Math.Clamp(accumulator / _runner.FixedDeltaSeconds, 0, 1) : 0;
            return new(SessionId, _document.World.Identity, State, _frameCount, _document.World.Tick,
                _runner.FixedDeltaSeconds, _document.World.Tick * _runner.FixedDeltaSeconds,
                accumulator, alpha, _steps, _dropped, _totalDropped, Fault);
        }
    }
    public void AddSystem(IWorldSystem system)
    {
        Control();
        if (State != PlayState.Stopped) throw new InvalidOperationException("Register Systems before Start.");
        _runner.AddSystem(system);
    }
    public void AddCommittedObserver(ICommittedStepObserver observer)
    { Control();if(State==PlayState.Faulted)throw new InvalidOperationException("Recover Play before attaching an observer.");_runner.AddCommittedObserver(observer); }
    public void RemoveCommittedObserver(ICommittedStepObserver observer)
    { _document.VerifyAccess();if(_disposed)return;Control();_runner.RemoveCommittedObserver(observer); }
    public void Start(Func<BehaviourBindingData, Behaviour> createBehaviour)
    {
        Control(); ArgumentNullException.ThrowIfNull(createBehaviour);
        if (State != PlayState.Stopped) throw new InvalidOperationException("Play session is already started.");
        _attemptTick = Tick;
        _busy = true; State = PlayState.Starting; SessionId = Guid.NewGuid(); Fault = null;
        _input.Clear(resetSequence: true); _facade.SetContext(this); _factory = createBehaviour;
        _phase = "prepare"; _current = null;
        try
        {
            var snapshot = _document.CaptureSnapshot();
            var prepared = new List<Instance>();
            // Constructors/Export setters cannot mutate the live Play World.
            using (_document.World.ReadOnly())
                foreach (var obj in snapshot.Objects.OrderBy(o => o.Id))
                    foreach (var binding in obj.Behaviours.OrderBy(b => b.Id))
                    {
                        var behaviour = createBehaviour(binding) ?? throw new ArgumentException("Null Behaviour.");
                        behaviour.GameObject = new(_facade, _document.World.FindObject(obj.Id));
                        prepared.Add(new(obj.Id, binding, behaviour));
                    }
            _instances = prepared.ToArray();
            _facade.BeginPhase(initialization: true);
            foreach (var item in _instances)
            {
                item.Created = true;
                Invoke(item, "create", item.Behaviour.DispatchCreate);
                if (!item.Binding.Enabled) continue;
                item.Activated = true;
                Invoke(item, "enable", item.Behaviour.DispatchEnable);
            }
            Action? render = null;
            Action commit = _facade.PreparePhase(world => render = _render.Prepare(world, reset: true));
            commit(); render!(); // Initialization is committed, but is not a simulated Tick.
            State = PlayState.Running; _phase = "control"; _current = null;
        }
        catch (Exception error)
        {
            if (_facade.IsInPhase) _facade.AbortPhase();
            SetFault(error);
            _ = Cleanup();
            throw;
        }
        finally { _busy = false; }
    }
    public PlayStatus AdvanceFrame(double deltaSeconds)
    {
        Control();
        if (_advanceMode != PlayAdvanceMode.Frames) throw new InvalidOperationException("Fixed-step sessions cannot advance frames.");
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0) throw new ArgumentException("Invalid frame delta.");
        if (State == PlayState.Faulted) throw new InvalidOperationException("Stop/Restart the faulted Play session.");
        if (State is not (PlayState.Running or PlayState.Paused)) throw new InvalidOperationException("No active Play session.");
        if (State == PlayState.Paused) return Status;
        // Strict preflight must reject without changing counters, accumulator or diagnostics.
        if (_policy == FrameTimePolicy.Strict &&
            (!double.IsFinite(_runner.Accumulator + deltaSeconds) ||
             _runner.Accumulator + deltaSeconds > _runner.FixedDeltaSeconds * _runner.MaxStepsPerAdvance + 1e-12))
            throw new ArgumentException("Frame exceeds strict fixed-step budget.");
        if (_policy == FrameTimePolicy.Interactive &&
            !double.IsFinite(_totalDropped + deltaSeconds + _runner.Accumulator))
            throw new ArgumentException("Dropped-time counter would overflow.");
        ulong frame = checked(_frameCount + 1);
        _attemptTick = Tick;
        _busy = true; _steps = 0; _dropped = 0; _phase = "fixed_update"; _current = null;
        ulong tick = _document.World.Tick;
        bool timeRecorded = false;
        try
        {
            _frameCount = frame;
            _steps = _policy == FrameTimePolicy.Strict ? _runner.Advance(deltaSeconds) : _runner.AdvanceInteractive(deltaSeconds);
            _dropped = _runner.LastDroppedSeconds;
            AccumulateDropped(); timeRecorded = true;
            using (_document.World.ReadOnly())
                foreach (var item in _instances)
                    if (item.Activated && item.Binding.Enabled) Invoke(item, "update", () => item.Behaviour.DispatchUpdate(_policy == FrameTimePolicy.Interactive ? Math.Min(deltaSeconds, 0.25) : deltaSeconds));
            _phase = "control"; _current = null;
        }
        catch (Exception error)
        {
            _steps = checked((int)(_document.World.Tick - tick));
            _dropped = _runner.LastDroppedSeconds;
            if (!timeRecorded) AccumulateDropped();
            SetFault(error);
        }
        finally { _busy = false; }
        return Status;
    }
    // Headless uses the exact same transactional runner, without OnUpdate or wall-clock debt.
    public PlayStatus AdvanceFixedStep()
    {
        Control();
        if (_advanceMode != PlayAdvanceMode.FixedSteps || State != PlayState.Running)
            throw new InvalidOperationException("Running fixed-step session required.");
        _busy = true; _steps = 0; _dropped = 0; _phase = "fixed_update"; _current = null; _attemptTick = Tick;
        try { _runner.ClearAccumulator(); _steps = _runner.Advance(_runner.FixedDeltaSeconds); _phase = "control"; }
        catch (Exception error) { _steps = _runner.LastStepsExecuted; SetFault(error); }
        finally { _busy = false; }
        return Status;
    }
    public PlayStatus Pause()
    {
        Control();
        if (State is not (PlayState.Running or PlayState.Paused)) throw new InvalidOperationException("Pause requires active Play.");
        _runner.ClearAccumulator(); _input.Clear(); State = PlayState.Paused; _steps = 0; _dropped = 0;
        return Status;
    }
    public PlayStatus Resume()
    {
        Control();
        if (State != PlayState.Paused) throw new InvalidOperationException("Resume requires Paused.");
        _runner.ClearAccumulator(); _input.Clear(); State = PlayState.Running; _steps = 0; _dropped = 0;
        return Status;
    }
    public PlayStatus Step()
    {
        Control();
        if (State != PlayState.Paused) throw new InvalidOperationException("Step requires Paused.");
        _busy = true; _phase = "fixed_update"; _current = null; _steps = 0; _dropped = 0;
        try { _runner.ClearAccumulator(); _steps = _runner.Advance(_runner.FixedDeltaSeconds); }
        catch (Exception error) { _steps = _runner.LastStepsExecuted; SetFault(error); }
        finally { _busy = false; }
        return Status;
    }
    // Preflight failure leaves the old instances paused. Past cleanup, activation failure is irreversible and Faulted.
    public PlayStatus Reload(Func<BehaviourBindingData, Behaviour> createBehaviour)
    {
        Control(); ArgumentNullException.ThrowIfNull(createBehaviour);
        if (State is not (PlayState.Running or PlayState.Paused)) throw new InvalidOperationException("Reload requires active Play.");
        Pause();
        var prepared = new List<Instance>();
        using (_document.World.ReadOnly())
            foreach (var obj in _document.CaptureSnapshot().Objects.OrderBy(o => o.Id))
                foreach (var binding in obj.Behaviours.OrderBy(b => b.Id))
                {
                    var behaviour = createBehaviour(binding) ?? throw new ArgumentException("Null Behaviour.");
                    behaviour.GameObject = new(_facade, _document.World.FindObject(obj.Id));
                    prepared.Add(new(obj.Id, binding, behaviour));
                }
        var instances = prepared.ToArray();
        Action systems = _runner.PrepareSystems([new Dispatcher(this)]);
        _attemptTick = Tick;
        _busy = true; _phase = "reload_cleanup"; _current = null;
        try
        {
            var errors = Cleanup();
            if (errors.Length != 0) throw new AggregateException("Reload cleanup failed.", errors);
            _instances = instances; _factory = createBehaviour; SessionId = Guid.NewGuid();
            systems(); _input.Clear(resetSequence: true); _facade.Restored(); _runner.ResetFault(); _frameCount = 0;
            _facade.BeginPhase(initialization: true);
            foreach (var item in _instances)
            {
                item.Created = true; Invoke(item, "reload_create", item.Behaviour.DispatchCreate);
                if (item.Binding.Enabled) { item.Activated = true; Invoke(item, "reload_enable", item.Behaviour.DispatchEnable); }
            }
            Action? render = null;
            Action commit = _facade.PreparePhase(world => render = _render.Prepare(world, reset: true));
            commit(); render!(); State = PlayState.Paused; Fault = null; _phase = "control"; _current = null;
            return Status;
        }
        catch (Exception error)
        {
            if (_facade.IsInPhase) _facade.AbortPhase();
            SetFault(error); throw;
        }
        finally { _busy = false; }
    }
    public void Stop()
    {
        Control();
        if (State == PlayState.Stopped) return;
        _busy = true; State = PlayState.Stopping;
        Exception[] errors;
        try
        {
            if (_facade.IsInPhase) _facade.AbortPhase();
            errors = Cleanup();
            _facade.Restored(); _facade.SetContext(null); _input.Clear(resetSequence: true); _render.Clear(); _stepInput = null;
            _runner.ResetFault();
            _frameCount = 0; _steps = 0; _dropped = _totalDropped = 0;
            _phase = "control"; _current = null; State = PlayState.Stopped;
        }
        finally { _busy = false; }
        if (errors.Length != 0) throw new AggregateException("Play cleanup failed; session is stopped.", errors);
    }
    private Exception[] Cleanup()
    {
        var errors = new List<Exception>();
        using (_document.World.ReadOnly())
            foreach (var item in _instances.Concat(_retired))
            {
                item.Behaviour.Cleanup = new(item.ObjectId, item.Binding.Id, item.Binding.TypeName);
                bool activated = item.Activated; item.Activated = false;
                if (activated) try { item.Behaviour.DispatchDisable(); } catch (Exception error) { errors.Add(error); }
                bool created = item.Created; item.Created = false;
                if (created) try { item.Behaviour.DispatchDestroy(); } catch (Exception error) { errors.Add(error); }
            }
        _instances = []; _retired = []; _factory = null; _commands.Reset();
        return errors.ToArray();
    }
    private void RunStep(Action update)
    {
        long Stamp() => MeasureSteps ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        long started = Stamp(); long callbacksAt = 0, inputAt = 0, worldAt = 0, installAt = 0;
        long metadataTime = 0, instanceTime = 0, receiptsTime = 0, renderTime = 0;
        _attemptTick = Tick == ulong.MaxValue ? Tick : Tick + 1;
        _phase = "lifecycle_cleanup"; _current = null;
        CleanupRetired();
        _commands.Begin(_document.CaptureSnapshot());
        _stepInput = _input.Read();
        _facade.BeginPhase();
        long setupAt = Stamp();
        try
        {
            foreach (var item in _instances)
            {
                if (!item.Created) { item.Created = true; Invoke(item, "create", item.Behaviour.DispatchCreate); }
                if (item.Binding.Enabled && !item.Activated) { item.Activated = true; Invoke(item, "enable", item.Behaviour.DispatchEnable); }
            }
            update(); callbacksAt = Stamp();
            _phase = "prepare"; _current = null;
            Action consume = _input.PrepareConsume(); inputAt = Stamp();
            Action? render = null, metadata = null, instances = null, receipts = null;
            Action commit = _facade.PreparePhase(world =>
            {
                long a = Stamp(); metadata = _document.PrepareRuntimeStep(world, _commands.Bindings); long b = Stamp();
                instances = PrepareInstances(world, _commands.Bindings); long c = Stamp();
                receipts = _commands.PrepareReceipts(world); long d = Stamp();
                render = _render.Prepare(world); long e = Stamp();
                metadataTime = b - a; instanceTime = c - b; receiptsTime = d - c; renderTime = e - d;
            });
            worldAt = Stamp();
            commit(); metadata!(); instances!(); receipts!(); consume(); render!(); installAt = Stamp();
            if (MeasureSteps) LastStepProfile = new(setupAt - started, callbacksAt - setupAt, inputAt - callbacksAt,
                Math.Max(0, worldAt - inputAt - metadataTime - instanceTime - receiptsTime - renderTime),
                metadataTime, instanceTime, receiptsTime, renderTime, installAt - worldAt);
        }
        finally { if (_facade.IsInPhase) _facade.AbortPhase(); _commands.Abort(); _stepInput = null; }
    }
    private Action PrepareInstances(WorldSnapshot world, Dictionary<Guid, BehaviourBindingData[]> bindings)
    {
        var prepared = new List<Instance>();
        var changes = new List<(Instance Instance, BehaviourBindingData Binding)>();
        var existing = _instances.ToDictionary(i => (i.ObjectId, i.Binding.Id));
        using (_document.World.PreparationReadOnly())
            foreach (var obj in world.Objects.OrderBy(o => o.PersistentId))
                foreach (var binding in bindings.GetValueOrDefault(obj.PersistentId, []).OrderBy(b => b.Id))
                {
                    if (existing.Remove((obj.PersistentId, binding.Id), out var item) && item.Binding.TypeName == binding.TypeName)
                    { prepared.Add(item); changes.Add((item, binding)); }
                    else
                    {
                        var behaviour = _factory!(binding) ?? throw new ArgumentException("Null Behaviour.");
                        behaviour.GameObject = new(_facade, new Ncma.Runtime.GameObject(_document.World, _document.World.CandidateReference(obj.PersistentId)));
                        prepared.Add(new(obj.PersistentId, binding, behaviour));
                    }
                }
        var instances = prepared.ToArray();
        var retired = _retired.Concat(existing.Values).ToArray();
        return () => { foreach (var (instance, binding) in changes) instance.Binding = binding; _instances = instances; _retired = retired; };
    }
    private void CleanupRetired()
    {
        var retiring = _retired; _retired = [];
        var errors = new List<Exception>();
        using (_document.World.ReadOnly())
        {
            foreach (var item in retiring)
            {
                item.Behaviour.Cleanup = new(item.ObjectId, item.Binding.Id, item.Binding.TypeName);
                bool active = item.Activated; item.Activated = false;
                if (active) try { Invoke(item, "disable", item.Behaviour.DispatchDisable); } catch (Exception error) { errors.Add(error); }
                bool created = item.Created; item.Created = false;
                if (created) try { Invoke(item, "destroy", item.Behaviour.DispatchDestroy); } catch (Exception error) { errors.Add(error); }
            }
            foreach (var item in _instances)
                if (item.Activated && !item.Binding.Enabled)
                {
                    item.Activated = false; item.Behaviour.Cleanup = new(item.ObjectId, item.Binding.Id, item.Binding.TypeName);
                    try { Invoke(item, "disable", item.Behaviour.DispatchDisable); } catch (Exception error) { errors.Add(error); }
                }
        }
        if (errors.Count > 0) throw new AggregateException("Lifecycle cleanup failed.", errors);
    }
    private void Invoke(Instance item, string phase, Action callback)
    {
        _current = item; _phase = phase; callback(); _current = null;
    }
    private void SetFault(Exception error)
    {
        State = PlayState.Faulted;
        string message = error.GetBaseException().Message;
        if (message.Length > 512) message = message[..512];
        Fault = new("gameplay_callback_failed", _phase, SessionId, _document.World.Identity,
            _document.World.Tick, _current?.ObjectId, _current?.Binding.Id, _current?.Binding.TypeName, message, _attemptTick);
        _current = null;
    }
    private void AccumulateDropped()
    {
        double total = _totalDropped + _dropped;
        if (!double.IsFinite(total)) throw new OverflowException("Dropped-time counter overflow.");
        _totalDropped = total;
    }
    private void Verify() { _document.VerifyAccess(); ObjectDisposedException.ThrowIf(_disposed, this); }
    private void Control()
    {
        Verify();
        if (_busy || _document.World.IsUpdating) throw new InvalidOperationException("Reentrant Play control is forbidden.");
    }
    public void Dispose()
    {
        if (_disposed) { _document.VerifyAccess(); return; }
        Control();
        try { Stop(); }
        finally { if (_ownsFacade && State == PlayState.Stopped) _facade.Dispose(); _disposed = true; }
    }
}
