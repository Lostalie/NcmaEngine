using Ncma.Runtime;

namespace Ncma.Scene;

public sealed partial class SceneDocument
{
    private Guid _readIdentity, _readQueryIdentity;
    private ulong _readWorldRevision, _readDocumentRevision;
    private bool _readProof, _reading;
    private EntityQuery? _readQuery;

    // Existing inspection endpoints only. Outputs own their arrays/JSON; no Entity escapes.
    internal SceneDocumentSnapshot ReadPage(int offset, int limit, out int totalCount)
    {
        VerifyReadBoundary();
        if (offset < 0 || limit is < 1 or > 128) throw new ArgumentException("Invalid pagination.");
        using var read = World.ReadOnly();
        var cold = EnsureReadProof();
        totalCount = cold?.Objects.Length ?? World.Count;
        if (cold is not null) return cold with { Objects = cold.Objects.Skip(offset).Take(limit).ToArray() };
        if (_readQuery is null || _readQueryIdentity != World.Identity)
        { _readQuery = World.CreateQuery([], []); _readQueryIdentity = World.Identity; }
        int count = Math.Min(limit, Math.Max(0, totalCount - offset));
        var objects = new SceneObjectData[count];
        if (count != 0)
        {
            int skip = offset, next = 0;
            foreach (var entity in _readQuery)
            {
                if (skip > 0) { --skip; continue; }
                objects[next++] = CopyReadObject(entity);
                if (next == count) break;
            }
        }
        // Preserve the full codec's names/JSON/Export normalization, including ill-formed UTF-16.
        return SceneDocumentCodec.Copy(new(1, World.Name, objects));
    }

    internal SceneObjectData? ReadObject(Guid id)
    {
        VerifyReadBoundary();
        using var read = World.ReadOnly();
        var cold = EnsureReadProof(); // Even missing objects must not hide invalid data elsewhere.
        if (cold is not null) return cold.Objects.FirstOrDefault(o => o.Id == id);
        if (!World.TryFindEntity(id, out var entity)) return null;
        return SceneDocumentCodec.Copy(new(1, World.Name, [CopyReadObject(entity)])).Objects[0];
    }

    private void VerifyReadBoundary()
    {
        VerifyAccess();
        if (_preparing || _reading || !World.IsCommittedBoundary)
            throw new InvalidOperationException("Inspection requires a committed safe boundary.");
        ObserveWorld();
    }

    private SceneDocumentSnapshot? EnsureReadProof()
    {
        if (_readProof && _readIdentity == World.Identity && _readWorldRevision == World.Revision &&
            _readDocumentRevision == _revision) return null;
        _readProof = false;
        // Reading is NOT ValidateAuthoring: keep data readable for diagnosis even if schema or
        // authoring composition validation would reject it. All existing write guards stay intact.
        SceneDocumentSnapshot snapshot;
        _reading = true;
        try { snapshot = CaptureSnapshot(); }
        finally { _reading = false; }
        if (World.HasStableReadEncoding)
        {
            _readIdentity = World.Identity; _readWorldRevision = World.Revision;
            _readDocumentRevision = _revision; _readProof = true;
        }
        return snapshot; // No complete document or component values retained by this cache.
    }

    private SceneObjectData CopyReadObject(Entity entity)
    {
        var item = World.CaptureReadObject(entity);
        return new(item.PersistentId, item.Name, item.Components, _bindings.GetValueOrDefault(item.PersistentId, []));
    }
}
