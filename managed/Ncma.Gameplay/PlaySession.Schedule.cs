using Ncma.Runtime;

namespace Ncma.Gameplay;

public sealed partial class PlaySession
{
    private StructuralSubmission? _scheduleSubmission;
    public ScheduleReport? ScheduleStatus { get { Verify(); return _runner.ScheduleStatus; } }
    public ScheduleGraph? ScheduleGraph { get { Verify(); return _runner.ScheduleGraph; } }
    public ScheduleCleanupStatus ScheduleCleanup { get { Verify(); return _runner.ScheduleCleanup; } }
    public bool CollectScheduleResources() { Control(); return _runner.CollectScheduleResources(); }
    public bool CanRecoverScheduleFault {
        get { Verify(); return State == PlayState.Faulted && _coupled is null && _compositionLifetimes.Count == 0 && _runner.CanRecoverScheduleFault; }
    }
    // Explicit local recovery, not replay: keep the committed World, retire the failed graph,
    // invalidate the old session's inputs/grants and remain paused. Private callback/IO state
    // is not rolled back. Native/composition participants keep their existing fail-stop policy.
    public PlayStatus RecoverScheduleFault()
    {
        Control();
        if (!CanRecoverScheduleFault) throw new InvalidOperationException("Only an unchanged precommit schedule fault without coupled lifetimes can recover.");
        _commands.Reset();
        _runner.ResetFault(); _scheduleSubmission = null;
        SessionId = Guid.NewGuid(); _input.Clear(resetSequence: true); _stepInput = null;
        CancelUiInputCore(); ResetSimulationCosts();
        _frameCount = 0; _steps = 0; _dropped = _totalDropped = 0;
        Fault = null; _phase = "control"; _current = null; State = PlayState.Paused;
        return Status;
    }
    // A submission is not an acceptance receipt. Use Commands.GetReceipt for original tokens.
    public StructuralSubmission? LastScheduleSubmission { get { Verify(); return _scheduleSubmission; } }
    public void ConfigureSchedule(ScheduleExecutor schedule)
    {
        Control();
        if (State is not (PlayState.Stopped or PlayState.Paused)) throw new InvalidOperationException("Configure schedule before Start or at an explicit paused boundary.");
        _runner.ConfigureSchedule(schedule, SubmitScheduledStructure); _scheduleSubmission = null;
    }
    public void ClearSchedule()
    {
        Control();
        if (State is not (PlayState.Stopped or PlayState.Paused or PlayState.Faulted)) throw new InvalidOperationException("Pause or Stop before detaching schedule.");
        _runner.ClearSchedule(); _scheduleSubmission = null;
    }
    private void SubmitScheduledStructure(IReadOnlyList<ScheduleStructure> operations)
    {
        var commands = new StructuralCommand[operations.Count];
        var created = new Dictionary<Guid, int>();
        StructuralTarget Target(Guid id) => created.TryGetValue(id, out int index)
            ? StructuralTarget.CreatedAt(index) : StructuralTarget.Existing(_scene.World.FindEntity(id));
        for (int i = 0; i < operations.Count; ++i)
        {
            var op = operations[i];
            commands[i] = op.Kind switch {
                ScheduleStructureKind.Create => new StructuralCommand.Create(op.ObjectId, op.Name!),
                ScheduleStructureKind.Destroy => new StructuralCommand.Destroy(Target(op.ObjectId)),
                ScheduleStructureKind.Rename => new StructuralCommand.Rename(Target(op.ObjectId), op.Name!),
                ScheduleStructureKind.Add => new StructuralCommand.Add(Target(op.ObjectId), op.Component!),
                ScheduleStructureKind.Remove => new StructuralCommand.Remove(Target(op.ObjectId), op.TypeId!),
                _ => throw new ArgumentException("Unknown scheduled structure operation.")
            };
            if (op.Kind == ScheduleStructureKind.Create) created.Add(op.ObjectId, i);
        }
        // Retain E2 batch validation, pending handles, binding metadata, permissions and receipts.
        _scheduleSubmission = _commands.SubmitBatch(commands);
    }
}
