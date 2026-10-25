namespace Ncma.Runtime;

public sealed partial class World
{
    private Guid _lastScheduledPublication;
    internal Guid ScheduleAttempt { get { VerifyCommandAccess(); return _stepAttempt; } }
    internal bool WasSchedulePublished(Guid attempt, ulong tick)
    { VerifyAccess(); return !_updating && attempt != Guid.Empty && _lastScheduledPublication == attempt && _tick == tick; }
    internal void VerifyScheduledWrite(Entity entity, Type type)
    {
        VerifyCommandAccess(); var entry = Require(entity);
        if (_stepDirtyComponents!.Contains((entry.Slot, type)) || !_stepUuids!.TryGetValue(entry.PersistentId, out int slot) ||
            _stepObjects![slot].Generation != entry.Generation)
            throw new InvalidOperationException("Scheduled write conflicts with an earlier producer or changed entity.");
    }
    internal void VerifyScheduledStructure(Guid id)
    {
        VerifyCommandAccess();
        if (_uuidIndex.TryGetValue(id, out int slot) && _stepDirtySlots!.Contains(slot))
            throw new InvalidOperationException("Scheduled structure conflicts with an earlier producer.");
    }
    internal void StageScheduledStructure(IReadOnlyList<ScheduleStructure> commands)
    {
        foreach (var c in commands)
            switch (c.Kind)
            {
                case ScheduleStructureKind.Create: StageCreate(c.ObjectId, c.Name!); break;
                case ScheduleStructureKind.Destroy: StageDestroy(c.ObjectId); break;
                case ScheduleStructureKind.Rename: StageRename(c.ObjectId, c.Name!); break;
                case ScheduleStructureKind.Add: StageAdd(c.ObjectId, c.Component!); break;
                case ScheduleStructureKind.Remove: StageRemove(c.ObjectId, c.TypeId!); break;
                default: throw new ArgumentException("Unknown scheduled structure operation.");
            }
    }
}
