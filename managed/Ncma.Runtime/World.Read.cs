namespace Ncma.Runtime;

public sealed partial class World
{
    // Bounded derived identities only, never component values or a second World. A membership
    // stamp changes only when committed topology changes; candidates and aborts cannot publish it.
    private Entity[]? _committedEntityOrder;
    private Guid _committedEntityOrderIdentity;
    private ulong _committedEntityOrderMembership;

    private Entity[] CopyEntitiesInCreationOrder()
    {
        if (_committedEntityOrder is null || _committedEntityOrderIdentity != _identity ||
            _committedEntityOrderMembership != _membershipVersion)
        {
            // Build completely before publishing. Creation order is independent of sparse slot
            // reuse, dictionary enumeration, UUID sorting and the candidate's reserved ordinals.
            Entity[] ordered = _objects.Values.OrderBy(e => e.CreationOrdinal).Select(IdentityOf).ToArray();
            _committedEntityOrder = ordered;
            _committedEntityOrderIdentity = _identity;
            _committedEntityOrderMembership = _membershipVersion;
        }
        // Preserve the shipping detached-copy contract even though the return type is read-only.
        // No caller can mutate the cached buffer or invalidate another caller's retained result.
        return _committedEntityOrder.Length == 0 ? [] : (Entity[])_committedEntityOrder.Clone();
    }

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
