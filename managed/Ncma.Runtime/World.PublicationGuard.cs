namespace Ncma.Runtime;

public sealed partial class World
{
    // A metadata publisher must follow THIS prepared ECS commit, not any commit which happens
    // to reach the same revision after an aborted attempt. No component storage escapes.
    internal Action PreparePublicationGuard(PreparedStepView view)
    {
        VerifyAccess(); view.VerifyCurrent();
        if (!ReferenceEquals(view.Owner, this)) throw new InvalidOperationException("Foreign prepared publication.");
        var objects = _stepObjects;
        var encoded = _stepPreparedEncoding;
        var identity = _identity;
        ulong revision = checked(_revision + 1), mutation = _stepMutation;
        return () =>
        {
            VerifyAccess(); VerifyWriteAccess();
            if (_updating || _preparing || _identity != identity || _revision != revision ||
                _stepMutation != mutation || !ReferenceEquals(_objects, objects) ||
                !ReferenceEquals(_committedEncoding, encoded) || _encodingRevision != revision)
                throw new InvalidOperationException("The matching prepared ECS candidate was not published.");
        };
    }
}
