namespace Ncma.Animation;

public readonly record struct AnimationStepContext(Guid SessionId, Guid WorldId, ulong Tick);
public readonly record struct AnimationEvaluationToken(Guid InstanceId, ulong Sequence, ulong SourceTick);
public enum AnimationPoseOperation { Clip, Blend }
// TRS sampling/mixing recipe only, never native pointers or direct World writes.
public readonly record struct AnimationPoseInstruction(AnimationPoseOperation Operation, Guid NodeId, Guid ClipId,
    double Previous, double Current, double Duration, bool Loop, int SourceA, int SourceB, float Weight);
public readonly record struct AnimationGraphFrame(Guid InstanceId, Guid GraphId, AnimationStepContext Context,
    Guid StateId, Guid FromStateId, Guid TransitionId, float TransitionWeight, int Output, int InstructionCount);

// Trusted host commits this numerical-free instance only AFTER the corresponding World step succeeds.
// This stamp API is not World authority: production host/asset/Character composition is M6.3.
public sealed class AnimationGraphInstance
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly AnimationProgram _program;
    private readonly Guid _identity = Guid.NewGuid();
    private AnimationStepContext _context;
    private readonly double[] _requested, _committedParameters, _times, _candidateTimes;
    private readonly int[] _slots;
    private readonly bool[] _consume;
    private readonly AnimationPoseInstruction[] _plan, _committedPlan;
    private int _state, _from = -1, _nextState, _nextFrom, _count, _committedCount, _output, _committedOutput;
    private double _elapsed, _duration, _nextElapsed, _nextDuration, _delta;
    private Guid _transition, _nextTransition;
    private ulong _sequence;
    private bool _prepared;
    private readonly AnimationGraphEvent[] _events = new AnimationGraphEvent[AnimationProgram.MaximumEventsPerQuantum],
        _committedEvents = new AnimationGraphEvent[AnimationProgram.MaximumEventsPerQuantum];
    private int _eventCount, _committedEventCount;
    private ulong _committedSequence;
    private AnimationEvaluationOutcome _outcome;
    public AnimationGraphInstance(AnimationProgram program, AnimationStepContext context)
    {
        _program = program ?? throw new ArgumentNullException(nameof(program)); RequireContext(context);
        _context = context; _state = program.Entry;
        _requested = new double[program.ParameterCount]; _committedParameters = new double[_requested.Length]; _consume = new bool[_requested.Length];
        for (int i = 0; i < _requested.Length; i++) _requested[i] = program.Parameters[i].Kind switch {
            AnimationParameterKind.Float => program.Parameters[i].FloatDefault, AnimationParameterKind.Int => program.Parameters[i].IntDefault,
            _ => program.Parameters[i].BoolDefault ? 1 : 0
        };
        _requested.CopyTo(_committedParameters, 0);
        _times = new double[(program.StateCount + 1) * program.NodeCount]; _candidateTimes = new double[_times.Length]; _slots = new int[_times.Length];
        _plan = new AnimationPoseInstruction[program.MaximumPlanInstructions]; _committedPlan = new AnimationPoseInstruction[_plan.Length];
        Build(0, false); PublishPlan();
    }
    private void Verify() { if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Animation graph owner thread required."); }
    private static void RequireContext(AnimationStepContext context)
    { if (context.SessionId == Guid.Empty || context.WorldId == Guid.Empty) throw new ArgumentException("Exact session/world identity required."); }
    private void CheckContext(AnimationStepContext context)
    { RequireContext(context); if (context != _context) throw new InvalidOperationException("Animation graph committed context mismatch."); }
    private int Parameter(Guid id, AnimationParameterKind kind)
    {
        Verify(); if (_prepared) throw new InvalidOperationException("Parameters cannot change during a prepared quantum.");
        if (!_program.ParameterIndices.TryGetValue(id, out int i) || _program.Parameters[i].Kind != kind) throw new ArgumentException("Typed graph parameter required."); return i;
    }
    public void SetFloat(Guid id, double value) { int i = Parameter(id, AnimationParameterKind.Float); AnimationGraphCodec.Scalar(value, -1000000, 1000000); _requested[i] = value; }
    public void SetInt(Guid id, int value) => _requested[Parameter(id, AnimationParameterKind.Int)] = value;
    public void SetBool(Guid id, bool value) => _requested[Parameter(id, AnimationParameterKind.Bool)] = value ? 1 : 0;
    public void SetTrigger(Guid id) => _requested[Parameter(id, AnimationParameterKind.Trigger)] = 1;
    public void ClearTrigger(Guid id) => _requested[Parameter(id, AnimationParameterKind.Trigger)] = 0;
    public double CommittedParameter(Guid id)
    { Verify(); return _program.ParameterIndices.TryGetValue(id, out int i) ? _committedParameters[i] : throw new ArgumentException("Unknown parameter."); }
    public AnimationEvaluationToken Prepare(AnimationStepContext context, double fixedDelta)
    {
        Verify(); CheckContext(context);
        if (_prepared || context.Tick == ulong.MaxValue || _sequence == ulong.MaxValue) throw new InvalidOperationException("One bounded quantum may be prepared.");
        _sequence++;
        try { AnimationGraphCodec.Scalar(fixedDelta, .001, 1); Build(fixedDelta, true); CollectEvents(); }
        catch { _outcome = AnimationEvaluationOutcome.PreparationRejected; throw; }
        _prepared = true; _outcome = AnimationEvaluationOutcome.Prepared; return new(_identity, _sequence, _context.Tick);
    }
    private void Token(AnimationEvaluationToken token)
    { Verify(); if (!_prepared || token.InstanceId != _identity || token.Sequence != _sequence || token.SourceTick != _context.Tick) throw new InvalidOperationException("Stale/foreign prepared graph quantum."); }
    public void Abort(AnimationEvaluationToken token) { Token(token); _prepared = false; _outcome = AnimationEvaluationOutcome.Aborted; }
    public void Commit(AnimationEvaluationToken token, AnimationStepContext committed)
    {
        Token(token); RequireContext(committed);
        if (committed.SessionId != _context.SessionId || committed.WorldId != _context.WorldId || committed.Tick != _context.Tick + 1)
            throw new InvalidOperationException("Commit must match the next successful host fixed step.");
        _candidateTimes.CopyTo(_times, 0); _requested.CopyTo(_committedParameters, 0);
        for (int i = 0; i < _consume.Length; i++) if (_consume[i]) _requested[i] = _committedParameters[i] = 0;
        _state = _nextState; _from = _nextFrom; _elapsed = _nextElapsed; _duration = _nextDuration; _transition = _nextTransition;
        _context = committed; PublishPlan();
        Array.Copy(_events, _committedEvents, _eventCount); _committedEventCount = _eventCount;
        _committedSequence = token.Sequence; _prepared = false; _outcome = AnimationEvaluationOutcome.Committed;
    }
    private void PublishPlan() { Array.Copy(_plan, _committedPlan, _count); _committedCount = _count; _committedOutput = _output; }
    public int CopyPreparedPlan(AnimationEvaluationToken token, Span<AnimationPoseInstruction> destination)
    { Token(token); if (destination.Length < _count) throw new ArgumentException("Graph plan output capacity."); _plan.AsSpan(0, _count).CopyTo(destination); return _count; }
    public AnimationGraphFrame PreparedFrame(AnimationEvaluationToken token)
    {
        Token(token); return new(_identity, _program.AssetId, _context with { Tick = _context.Tick + 1 },
            _nextState < 0 ? Guid.Empty : _program.States[_nextState], _nextFrom < 0 ? Guid.Empty : _program.States[_nextFrom],
            _nextTransition, _nextFrom < 0 ? 1 : (float)Math.Min(1, _nextElapsed / _nextDuration), _output, _count);
    }
    public int CopyCommittedPlan(Span<AnimationPoseInstruction> destination)
    { Verify(); if (destination.Length < _committedCount) throw new ArgumentException("Graph plan output capacity."); _committedPlan.AsSpan(0, _committedCount).CopyTo(destination); return _committedCount; }
    // Repeated reads copy the same committed receipts; they never dispatch events or consume them.
    public int CopyCommittedEvents(AnimationStepContext context, Span<AnimationGraphEvent> destination)
    {
        Verify(); CheckContext(context);
        if (_prepared || _outcome == AnimationEvaluationOutcome.PreparationRejected) throw new InvalidOperationException("Graph event snapshot not valid.");
        if (destination.Length < _committedEventCount) throw new ArgumentException("Graph event output capacity.");
        _committedEvents.AsSpan(0, _committedEventCount).CopyTo(destination); return _committedEventCount;
    }
    // Off-frame copied diagnostics, not live World authority. Failed preparation preserves but labels historical data.
    public AnimationGraphDebugFrame ReadDebug(AnimationStepContext context)
    {
        Verify(); CheckContext(context);
        var parameters = new AnimationDebugParameter[_committedParameters.Length];
        for (int i = 0; i < parameters.Length; i++) parameters[i] = new(_program.Parameters[i].Id, _program.Parameters[i].Kind, _committedParameters[i]);
        bool valid = !_prepared && _outcome != AnimationEvaluationOutcome.PreparationRejected;
        return new(Frame, _committedSequence, _sequence, _outcome, valid, _program.EventContentHash,
            Array.AsReadOnly(parameters), Array.AsReadOnly(_committedPlan.AsSpan(0, _committedCount).ToArray()),
            Array.AsReadOnly(valid ? _committedEvents.AsSpan(0, _committedEventCount).ToArray() : Array.Empty<AnimationGraphEvent>()), false);
    }
    public AnimationGraphFrame Frame {
        get { Verify(); return new(_identity, _program.AssetId, _context, _state < 0 ? Guid.Empty : _program.States[_state],
            _from < 0 ? Guid.Empty : _program.States[_from], _transition, _from < 0 ? 1 : (float)Math.Min(1, _elapsed / _duration), _committedOutput, _committedCount); }
    }
    private double Speed(AnimationProgram.Node node)
    { double speed = node.ScalarParameter < 0 ? node.Speed : _requested[node.ScalarParameter]; AnimationGraphCodec.Scalar(speed, 0, 8); return speed; }
    private static double Next(double previous, double duration, bool loop, double delta)
    {
        double current = previous + delta;
        if (!double.IsFinite(current) || delta > 0 && current <= previous) throw new ArgumentException("Graph clock overflow/precision loss.");
        if (loop && Math.Floor(current / duration) - Math.Floor(previous / duration) > 32) throw new ArgumentException("Graph quantum exceeds 32 clip crossings.");
        return loop ? current : Math.Min(duration, current);
    }
    private bool Exit(AnimationProgram.Transition transition)
    {
        if (!transition.ExitTime.HasValue) return true;
        int primary = _program.StatePrimaryClips[_state]; var node = _program.Nodes[primary];
        double previous = _times[(_state + 1) * _program.NodeCount + primary];
        double current = Next(previous, node.Duration, node.Loop, _delta * Speed(node)); double exit = transition.ExitTime.Value;
        if (!node.Loop) return current / node.Duration >= exit;
        if (exit == 0 && previous == 0) return true;
        return Math.Floor(current / node.Duration - exit) > Math.Floor(previous / node.Duration - exit);
    }
    private bool Conditions(AnimationProgram.Transition transition)
    {
        foreach (var condition in transition.Conditions) {
            double value = _requested[condition.Parameter]; bool match = condition.Comparison switch {
                AnimationComparison.Equal => value == condition.Value, AnimationComparison.NotEqual => value != condition.Value,
                AnimationComparison.Greater => value > condition.Value, AnimationComparison.GreaterOrEqual => value >= condition.Value,
                AnimationComparison.Less => value < condition.Value, AnimationComparison.LessOrEqual => value <= condition.Value,
                AnimationComparison.Triggered => value == 1, _ => false
            }; if (!match) return false;
        } return true;
    }
    private void Build(double delta, bool transitions)
    {
        _delta = delta; _count = 0; Array.Fill(_slots, -1); Array.Clear(_consume); _times.CopyTo(_candidateTimes, 0);
        _nextState = _state; _nextFrom = _from; _nextElapsed = _elapsed; _nextDuration = _duration; _nextTransition = _transition;
        // At most one selection per quantum. A completed transition becomes eligible on the next quantum.
        if (transitions && _state >= 0 && _from < 0) foreach (var t in _program.Transitions[_state]) {
            if (!Conditions(t) || !Exit(t)) continue;
            _nextState = t.To; _nextFrom = t.Duration > 0 ? _state : -1; _nextElapsed = 0; _nextDuration = t.Duration; _nextTransition = t.Id;
            Array.Clear(_candidateTimes, (t.To + 1) * _program.NodeCount, _program.NodeCount);
            foreach (var c in t.Conditions) if (c.Comparison == AnimationComparison.Triggered) _consume[c.Parameter] = true;
            break;
        }
        if (_nextFrom >= 0) { _nextElapsed = Math.Min(_nextDuration, _nextElapsed + delta); }
        _output = Evaluate(_program.Output, 0);
        if (_nextFrom >= 0 && _nextElapsed >= _nextDuration) _nextFrom = -1;
    }
    private int Add(AnimationPoseInstruction instruction)
    { if (_count == _plan.Length) throw new ArgumentException("Bounded graph plan exceeded."); _plan[_count] = instruction; return _count++; }
    private void CollectEvents()
    {
        _eventCount = 0;
        int primary = _nextState < 0 ? Primary(_program.Output) : _program.StatePrimaryClips[_nextState];
        int context = _nextState < 0 ? 0 : _nextState + 1;
        int slot = _slots[context * _program.NodeCount + primary];
        if (slot < 0) throw new InvalidOperationException("Target event source missing from prepared plan.");
        var clip = _plan[slot]; var markers = _program.EventTracks[primary];
        if (markers.Length == 0 || clip.Current <= clip.Previous) return;
        double first = clip.Loop ? Math.Floor(clip.Previous / clip.Duration) : 0;
        double last = clip.Loop ? Math.Floor(clip.Current / clip.Duration) : 0;
        // Integer cycles must remain exactly representable; no cycle counter wrapping or non-progressing loop.
        if (last > 9007199254740991d || last - first > 32) throw new ArgumentException("Event cycle precision/boundary budget.");
        for (int cycle = 0; cycle <= last - first; cycle++) foreach (var marker in markers) {
            double at = (first + cycle) * clip.Duration + marker.Time;
            if (at <= clip.Previous || at > clip.Current) continue;
            if (_eventCount == _events.Length) throw new ArgumentException("Animation event quantum budget.");
            _events[_eventCount++] = new(_identity, _program.AssetId, _context with { Tick = _context.Tick + 1 }, _sequence,
                _nextState < 0 ? Guid.Empty : _program.States[_nextState], clip.NodeId, marker.Id, marker.ClipId, at, marker.Name);
        }
        int Primary(int index) => _program.Nodes[index].Kind == AnimationNodeKind.Clip ? index : Primary(_program.Nodes[index].A);
    }
    private int Evaluate(int index, int context)
    {
        int at = context * _program.NodeCount + index; if (_slots[at] >= 0) return _slots[at];
        var node = _program.Nodes[index]; int result;
        switch (node.Kind) {
            case AnimationNodeKind.Output: result = Evaluate(node.A, context); break;
            case AnimationNodeKind.Clip:
                double previous = _candidateTimes[at]; double current = Next(previous, node.Duration, node.Loop, _delta * Speed(node));
                _candidateTimes[at] = current; result = Add(new(AnimationPoseOperation.Clip, node.Id, node.Clip, previous, current, node.Duration, node.Loop, -1, -1, 0)); break;
            case AnimationNodeKind.Blend:
                double weight = node.ScalarParameter < 0 ? node.Weight : _requested[node.ScalarParameter]; AnimationGraphCodec.Scalar(weight, 0, 1);
                int a = Evaluate(node.A, context), b = Evaluate(node.B, context);
                result = Add(new(AnimationPoseOperation.Blend, node.Id, Guid.Empty, 0, 0, 0, false, a, b, (float)weight)); break;
            case AnimationNodeKind.StateMachine:
                int target = Evaluate(_program.StateRoots[_nextState], _nextState + 1);
                if (_nextFrom < 0) result = target;
                else { int source = Evaluate(_program.StateRoots[_nextFrom], _nextFrom + 1); result = Add(new(AnimationPoseOperation.Blend, node.Id,
                    Guid.Empty, 0, 0, 0, false, source, target, (float)(_nextElapsed / _nextDuration))); } break;
            default: throw new InvalidOperationException("Scalar parameter nodes cannot be pose instructions.");
        }
        _slots[at] = result; return result;
    }
}
