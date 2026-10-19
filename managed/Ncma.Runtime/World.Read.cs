namespace Ncma.Runtime;

public sealed partial class World
{
    // Encoding proof only; does not validate schema, grant writes, or replace the full document budget.
    // Scene combines this with its successful full codec read and exact identity/revision stamps.
    internal bool HasStableReadEncoding
    {
        get { VerifyCommittedRead(); return _storage.HasStableReadEncoding(Components); }
    }

    internal ObjectSnapshot CaptureReadObject(Entity entity)
    {
        VerifyCommittedRead();
        var entry = Require(entity);
        return new(entry.PersistentId, entry.Name, _storage.Capture(entry.Slot, Components));
    }

    private void VerifyCommittedRead()
    {
        VerifyAccess();
        if (!IsCommittedBoundary || _readOnlyDepth == 0)
            throw new InvalidOperationException("Object encoding requires a committed read scope.");
    }
}
