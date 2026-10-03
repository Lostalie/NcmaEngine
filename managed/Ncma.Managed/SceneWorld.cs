using Microsoft.Win32.SafeHandles;

namespace Ncma;

public sealed partial class SceneWorld : IDisposable
{
    private readonly WorldHandle _handle;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;

    public SceneWorld(string name = "Untitled")
    {
        if (Native.GetAbiVersion() != 1 || Native.GetGameObjectApiVersion() != 4 || Native.GetWorldAccessApiVersion() != 1)
            throw new InvalidOperationException("Native GameObject API mismatch; rebuild NcmaEngine.");
        _handle = new WorldHandle(Native.WorldCreate(name));
        if (_handle.IsInvalid)
            throw new InvalidOperationException(Native.GetLastError());
    }

    internal SceneWorld(nint borrowedHandle)
    {
        if (borrowedHandle == 0) throw new ArgumentException("World handle is null.");
        _handle = new WorldHandle(borrowedHandle, ownsHandle: false);
    }

    public GameObject CreateObject(string name)
    {
        ulong id = Native.WorldCreateObject(Handle, name, 0);
        if (id == 0)
            throw new InvalidOperationException(Native.GetLastError());
        return new GameObject(this, id);
    }

    public void Dispose()
    {
        VerifyThread();
        _handle.Dispose();
    }

    private void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("World operations must run on the play-session thread.");
    }

    internal nint Handle
    {
        get
        {
            VerifyThread();
            ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
            return _handle.DangerousGetHandle();
        }
    }

    private sealed class WorldHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal WorldHandle(nint value, bool ownsHandle = true) : base(ownsHandle) => SetHandle(value);

        protected override bool ReleaseHandle()
        {
            Native.WorldDestroy(handle);
            return true;
        }
    }
}

public sealed class GameObject
{
    private readonly SceneWorld _world;

    internal GameObject(SceneWorld world, ulong id)
    {
        _world = world;
        Id = id;
        if (Native.WorldGetObjectReference(world.Handle, id, out ObjectReference reference) == 0)
            throw new InvalidOperationException(Native.GetLastError());
        Reference = reference;
    }

    public ulong Id { get; }
    public ObjectReference Reference { get; }

    public ObjectUuid PersistentId
    {
        get
        {
            if (Native.WorldGetObjectUuid(_world.Handle, Reference, out ulong high, out ulong low) == 0)
                throw new InvalidOperationException(Native.GetLastError());
            return new ObjectUuid(high, low);
        }
    }

    public SceneWorld World => _world;

    public Transform LocalTransform
    {
        get
        {
            if (Native.WorldGetLocalTransform(_world.Handle, Id, out Transform value) == 0)
                throw new InvalidOperationException(Native.GetLastError());
            return value;
        }
        set
        {
            if (Native.WorldSetLocalTransform(_world.Handle, Id, in value) == 0)
                throw new InvalidOperationException(Native.GetLastError());
        }
    }

    public bool Destroy() => Native.WorldDestroyObject(_world.Handle, Id, 0) != 0;
}
