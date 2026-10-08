namespace Ncma.Animation;

// Closed, typed checks; no executable assertions, clocks supplied by an Agent, files or live Play.
public enum AnimationSequenceAssertionKind { State, Transition, EventCount, Parameter }
public sealed record AnimationSequenceWrite(int Step, Guid ParameterId, AnimationParameterKind Kind, double Value);
public sealed record AnimationSequenceAssertion(int Step, AnimationSequenceAssertionKind Kind, Guid SubjectId, double Value);
public sealed record AnimationSequenceCase(double FixedDelta, int Steps, AnimationSequenceWrite[] Writes, AnimationSequenceAssertion[] Assertions);
public sealed record AnimationSequenceStep(AnimationGraphFrame Frame, ulong Sequence, IReadOnlyList<AnimationGraphEvent> Events);
public sealed record AnimationSequenceCheck(int Index, int Step, bool Passed, string Code);
public sealed record AnimationSequenceResult(Guid GraphId, string GraphContentHash, string EventContentHash,
    ulong MetadataGeneration, bool ResourcesPrepared, bool RootMotionSupported,
    IReadOnlyList<AnimationSequenceStep> Timeline, IReadOnlyList<AnimationSequenceCheck> Checks, bool Passed);

public static class AnimationGraphSequence
{
    public const int MaxSteps = 256, MaxWrites = 512, MaxAssertions = 64, MaxOutputEvents = 8192;
    // Off-frame trusted entry. Compiled metadata is NOT evidence of resource pins or numerical/GPU execution.
    public static AnimationSequenceResult Run(AnimationProgram program, AnimationSequenceCase input)
    {
        ArgumentNullException.ThrowIfNull(program); ArgumentNullException.ThrowIfNull(input);
        AnimationGraphCodec.Scalar(input.FixedDelta, .001, 1);
        if (input.Steps is < 1 or > MaxSteps || input.Writes is null || input.Assertions is null ||
            input.Writes.Length > MaxWrites || input.Assertions.Length > MaxAssertions) throw new ArgumentException("Animation sequence budget.");
        // Own and validate the complete request before executing any quantum.
        var writes = input.Writes.ToArray(); var assertions = input.Assertions.ToArray();
        var unique = new HashSet<(int, Guid)>();
        foreach (var write in writes) {
            if (write is null || write.Step < 1 || write.Step > input.Steps || !unique.Add((write.Step, write.ParameterId)) ||
                !program.ParameterIndices.TryGetValue(write.ParameterId, out int index) || program.Parameters[index].Kind != write.Kind)
                throw new ArgumentException("Exact typed sequence parameter/step required.");
            Value(write.Kind, write.Value);
        }
        foreach (var assertion in assertions) {
            if (assertion is null || assertion.Step < 1 || assertion.Step > input.Steps || !Enum.IsDefined(assertion.Kind))
                throw new ArgumentException("Closed animation assertion/step required.");
            switch (assertion.Kind) {
                case AnimationSequenceAssertionKind.State:
                    if (assertion.Value != 0 || !program.States.Contains(assertion.SubjectId)) throw new ArgumentException("Exact state assertion."); break;
                case AnimationSequenceAssertionKind.Transition:
                    if (assertion.Value != 0 || assertion.SubjectId != Guid.Empty && !program.Transitions.Any(ts => ts.Any(t => t.Id == assertion.SubjectId))) throw new ArgumentException("Exact transition assertion."); break;
                case AnimationSequenceAssertionKind.EventCount:
                    if (assertion.SubjectId != Guid.Empty && !program.EventTracks.Any(ms => ms.Any(m => m.Id == assertion.SubjectId)) ||
                        !double.IsFinite(assertion.Value) || assertion.Value != Math.Truncate(assertion.Value) || assertion.Value is < 0 or > AnimationProgram.MaximumEventsPerQuantum)
                        throw new ArgumentException("Bounded event assertion."); break;
                case AnimationSequenceAssertionKind.Parameter:
                    if (!program.ParameterIndices.TryGetValue(assertion.SubjectId, out int index)) throw new ArgumentException("Exact parameter assertion.");
                    Value(program.Parameters[index].Kind, assertion.Value); break;
            }
        }
        var byStep = writes.GroupBy(w => w.Step).ToDictionary(g => g.Key, g => g.OrderBy(w => w.ParameterId).ToArray());
        var context = new AnimationStepContext(Guid.NewGuid(), Guid.NewGuid(), 0);
        var instance = new AnimationGraphInstance(program, context); var timeline = new AnimationSequenceStep[input.Steps];
        var checks = new AnimationSequenceCheck[assertions.Length]; var events = new AnimationGraphEvent[AnimationProgram.MaximumEventsPerQuantum]; int total = 0;
        for (int step = 1; step <= input.Steps; step++) {
            if (byStep.TryGetValue(step, out var changes)) foreach (var write in changes) Apply(instance, write);
            var token = instance.Prepare(context, input.FixedDelta);
            instance.Commit(token, context = context with { Tick = context.Tick + 1 });
            int count = instance.CopyCommittedEvents(context, events); total = checked(total + count);
            if (total > MaxOutputEvents) throw new ArgumentException("Animation sequence output budget.");
            timeline[step - 1] = new(instance.Frame, token.Sequence, Array.AsReadOnly(events.AsSpan(0, count).ToArray()));
            for (int at = 0; at < assertions.Length; at++) {
                var assertion = assertions[at]; if (assertion.Step != step) continue;
                bool passed = assertion.Kind switch {
                    AnimationSequenceAssertionKind.State => instance.Frame.StateId == assertion.SubjectId,
                    AnimationSequenceAssertionKind.Transition => instance.Frame.TransitionId == assertion.SubjectId,
                    AnimationSequenceAssertionKind.EventCount => timeline[step - 1].Events.Count(e => assertion.SubjectId == Guid.Empty || e.MarkerId == assertion.SubjectId) == assertion.Value,
                    AnimationSequenceAssertionKind.Parameter => instance.CommittedParameter(assertion.SubjectId) == assertion.Value,
                    _ => throw new ArgumentException("Closed assertion required.")
                };
                checks[at] = new(at, step, passed, passed ? "assertion_passed" : "assertion_failed");
            }
        }
        return new(program.AssetId, program.ContentHash, program.EventContentHash, program.ResourceGeneration, false, false,
            Array.AsReadOnly(timeline), Array.AsReadOnly(checks), checks.All(c => c.Passed));
    }
    private static void Value(AnimationParameterKind kind, double value)
    {
        switch (kind) {
            case AnimationParameterKind.Float: AnimationGraphCodec.Scalar(value, -1000000, 1000000); break;
            case AnimationParameterKind.Int: AnimationGraphCodec.Scalar(value, int.MinValue, int.MaxValue); if (Math.Truncate(value) != value) throw new ArgumentException("Integral sequence value."); break;
            case AnimationParameterKind.Bool: case AnimationParameterKind.Trigger: if (value is not (0 or 1)) throw new ArgumentException("Boolean sequence value."); break;
            default: throw new ArgumentException("Closed sequence parameter type.");
        }
    }
    private static void Apply(AnimationGraphInstance instance, AnimationSequenceWrite write)
    {
        switch (write.Kind) {
            case AnimationParameterKind.Float: instance.SetFloat(write.ParameterId, write.Value); break;
            case AnimationParameterKind.Int: instance.SetInt(write.ParameterId, (int)write.Value); break;
            case AnimationParameterKind.Bool: instance.SetBool(write.ParameterId, write.Value == 1); break;
            case AnimationParameterKind.Trigger: if (write.Value == 1) instance.SetTrigger(write.ParameterId); else instance.ClearTrigger(write.ParameterId); break;
            default: throw new ArgumentException("Closed sequence write required.");
        }
    }
}
