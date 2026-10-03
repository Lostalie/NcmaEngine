using Microsoft.Win32.SafeHandles;

namespace Ncma;

public sealed class SceneWorld : IDisposable
{
    private readonly WorldHandle _handle;

    public SceneWorld(string name = "Untitled")
    {
        _handle = new WorldHandle(Native.WorldCreate(name));
        if (_handle.IsInvalid)
            throw new InvalidOperationException(Native.GetLastError());
    }

    internal SceneWorld(nint borrowedHandle)
    {
        if (borrowedHandle == 0) throw new ArgumentException("World handle is null.");
        _handle = new WorldHandle(borrowedHandle, ownsHandle: false);
    }

    public Node CreateNode(string name, Node? parent = null)
    {
        ulong id = Native.WorldCreateNode(Handle, name, parent?.Id ?? 0);
        if (id == 0)
            throw new InvalidOperationException(Native.GetLastError());
        return new Node(this, id);
    }

    public void Dispose() => _handle.Dispose();

    internal nint Handle
    {
        get
        {
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

public sealed class Node
{
    private readonly SceneWorld _world;

    internal Node(SceneWorld world, ulong id)
    {
        _world = world;
        Id = id;
    }

    public ulong Id { get; }

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

    public bool SetParent(Node? parent) => Native.WorldSetParent(_world.Handle, Id, parent?.Id ?? 0) != 0;
    public bool Destroy(bool recursive = true) => Native.WorldDestroyNode(_world.Handle, Id, recursive ? (byte)1 : (byte)0) != 0;
}
