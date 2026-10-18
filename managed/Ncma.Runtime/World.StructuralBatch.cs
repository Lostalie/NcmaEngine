namespace Ncma.Runtime;

public sealed partial class World
{
    private bool _preparingStructuralBatch;

    // A bounded candidate fork of THIS step, not another World or another commit owner.
    // The trusted command owner prepares its binding/receipt allocations before invoking install.
    internal Action PrepareStructuralBatch(Action stage, Action<WorldSnapshot> validate)
    {
        VerifyAccess();
        try
        {
            VerifyCommandAccess();
            if (_preparingStructuralBatch || _stepError is not null) throw new InvalidOperationException("Structural preparation is reentrant or the step is poisoned.");
            var priorObjects = _stepObjects!; var priorUuids = _stepUuids!;
            var priorStorage = _stepStorage!; var priorSlots = _stepSlots!;
            bool priorMembership = _stepMembershipChanged;
            var objects = new Dictionary<int, Entry>(priorObjects);
            var uuids = new Dictionary<Guid, int>(priorUuids);
            var storage = priorStorage.Fork();
            var slots = (bool[])priorSlots.Clone();
            bool membership;
            _preparingStructuralBatch = true;
            try
            {
                _stepObjects = objects; _stepUuids = uuids; _stepStorage = storage; _stepSlots = slots;
                stage();
                if (_stepError is not null) throw new InvalidOperationException("Structural batch validation rejected.", _stepError);
                _preparing = true;
                try
                {
                    var snapshot = Capture(_name, objects, storage);
                    _ = SceneJson.EncodeBounded(snapshot, MaxSnapshotBytes);
                    validate(snapshot);
                }
                finally { _preparing = false; }
                if (_stepError is not null) throw new InvalidOperationException("Structural batch validation rejected.", _stepError);
                membership = _stepMembershipChanged;
            }
            finally
            {
                _stepObjects = priorObjects; _stepUuids = priorUuids; _stepStorage = priorStorage; _stepSlots = priorSlots;
                _stepMembershipChanged = priorMembership;
                _preparingStructuralBatch = false;
            }
            // Reserved generations/creation ordinals intentionally are NOT rolled back.
            return () =>
            {
                VerifyCommandAccess();
                if (_stepError is not null || !ReferenceEquals(_stepObjects, priorObjects)) throw new InvalidOperationException("Structural batch candidate is stale or rejected.");
                _stepObjects = objects; _stepUuids = uuids; _stepStorage = storage; _stepSlots = slots;
                _stepMembershipChanged = membership;
            };
        }
        catch (Exception error) { RejectStep(error); throw; }
    }
}
