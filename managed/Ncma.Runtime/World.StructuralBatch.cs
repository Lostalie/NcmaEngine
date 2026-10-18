namespace Ncma.Runtime;

public sealed partial class World
{
    private bool _preparingStructuralBatch;

    // A bounded candidate fork of THIS step, not another World or another commit owner.
    // The trusted command owner prepares its binding/receipt allocations before invoking install.
    internal Action PrepareStructuralBatch(Action stage, Action<PreparedStepView> validate)
    {
        VerifyAccess();
        try
        {
            VerifyCommandAccess();
            if (_preparingStructuralBatch || _stepError is not null) throw new InvalidOperationException("Structural preparation is reentrant or the step is poisoned.");
            var priorObjects = _stepObjects!; var priorUuids = _stepUuids!;
            var priorStorage = _stepStorage!; var priorSlots = _stepSlots!;
            var priorDirty = _stepDirtySlots!; var priorPrepared = _stepPreparedEncoding;
            ulong priorPreparedMutation = _stepPreparedMutation;
            var priorDirtyComponents = _stepDirtyComponents!;
            bool priorMembership = _stepMembershipChanged;
            var objects = new Dictionary<int, Entry>(priorObjects);
            var uuids = new Dictionary<Guid, int>(priorUuids);
            var storage = priorStorage.Fork();
            var slots = (bool[])priorSlots.Clone();
            var dirty = new HashSet<int>(priorDirty);
            var dirtyComponents = new HashSet<(int, Type)>(priorDirtyComponents);
            bool membership;
            _preparingStructuralBatch = true;
            try
            {
                _stepObjects = objects; _stepUuids = uuids; _stepStorage = storage; _stepSlots = slots;
                _stepDirtySlots = dirty; _stepPreparedEncoding = null;
                _stepDirtyComponents = dirtyComponents;
                stage();
                if (_stepError is not null) throw new InvalidOperationException("Structural batch validation rejected.", _stepError);
                _preparing = true;
                try
                {
                    _stepPreparedEncoding = PrepareEncoding(); _stepPreparedMutation = _stepMutation;
                    validate(new PreparedStepView(this));
                }
                finally { _preparing = false; }
                if (_stepError is not null) throw new InvalidOperationException("Structural batch validation rejected.", _stepError);
                membership = _stepMembershipChanged;
            }
            finally
            {
                _stepObjects = priorObjects; _stepUuids = priorUuids; _stepStorage = priorStorage; _stepSlots = priorSlots;
                _stepDirtySlots = priorDirty; _stepPreparedEncoding = priorPrepared;
                _stepPreparedMutation = priorPreparedMutation;
                _stepDirtyComponents = priorDirtyComponents;
                _stepMembershipChanged = priorMembership;
                _preparingStructuralBatch = false;
            }
            // Reserved generations/creation ordinals intentionally are NOT rolled back.
            Guid attempt = _stepAttempt; ulong mutation = _stepMutation, installedMutation = checked(_stepMutation + 1);
            return () =>
            {
                VerifyCommandAccess();
                if (_stepError is not null || _stepAttempt != attempt || _stepMutation != mutation ||
                    !ReferenceEquals(_stepObjects, priorObjects)) throw new InvalidOperationException("Structural batch candidate is stale or rejected.");
                _stepObjects = objects; _stepUuids = uuids; _stepStorage = storage; _stepSlots = slots;
                _stepDirtySlots = dirty; _stepPreparedEncoding = null;
                _stepDirtyComponents = dirtyComponents;
                _stepMutation = installedMutation;
                _stepMembershipChanged = membership;
            };
        }
        catch (Exception error) { RejectStep(error); throw; }
    }
}
