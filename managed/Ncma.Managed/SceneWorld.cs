using System.Globalization;
using Ncma.Runtime;
namespace Ncma;

// Compatibility gameplay facade. Storage, validation and handles belong to C# Runtime.World.
public sealed partial class SceneWorld : IDisposable
{
    internal World Runtime { get; }
    private bool _disposed;
    public SceneWorld(string name = "Untitled") => Runtime = new World(name);
    internal SceneWorld(World runtime) => Runtime = runtime;
    internal void Verify() { Runtime.VerifyAccess(); ObjectDisposedException.ThrowIf(_disposed, this); }
    public GameObject CreateObject(string name)
    {
        Verify();
        return new(this, Runtime.CreateObject(name)); // Empty container: Transform is optional.
    }
    internal Ncma.Runtime.GameObject Resolve(ObjectReference reference)
    {
        Verify();
        return Runtime.Resolve(new(ToGuid(reference.WorldHigh, reference.WorldLow), reference.Id));
    }
    internal GameObject FindId(ulong id)
    {
        Verify();
        return new(this, Runtime.Resolve(new(Runtime.Identity, id)));
    }
    public IReadOnlyList<GameObject> GetObjects() { Verify(); return Runtime.GetObjects().Select(o => new GameObject(this, o)).ToArray(); }
    public void Dispose()
    {
        Runtime.VerifyAccess(); if (_disposed) return;
        Runtime.VerifyWriteAccess();
        if (_phase || Runtime.IsUpdating) throw new InvalidOperationException("Cannot dispose a World during gameplay.");
        _disposed = true;
    }
    internal bool IsInPhase => _phase;
    internal static Guid ToGuid(ulong high, ulong low) => Guid.ParseExact(high.ToString("x16") + low.ToString("x16"), "N");
    internal static ObjectUuid ToUuid(Guid value)
    {
        string hex = value.ToString("N");
        return new(ulong.Parse(hex[..16], NumberStyles.HexNumber), ulong.Parse(hex[16..], NumberStyles.HexNumber));
    }
    internal static TransformData ToData(Transform value) => new(
        new(value.Position.X, value.Position.Y, value.Position.Z),
        new(value.RotationX, value.RotationY, value.RotationZ, value.RotationW),
        new(value.Scale.X, value.Scale.Y, value.Scale.Z));
    internal static Transform FromData(TransformData value) => new()
    {
        Position = new(value.Position.X, value.Position.Y, value.Position.Z),
        RotationX = value.Rotation.X, RotationY = value.Rotation.Y, RotationZ = value.Rotation.Z, RotationW = value.Rotation.W,
        Scale = new(value.Scale.X, value.Scale.Y, value.Scale.Z)
    };
}

public sealed class GameObject
{
    private readonly Ncma.Runtime.GameObject _object;
    internal GameObject(SceneWorld world, Ncma.Runtime.GameObject value)
    {
        World = world; _object = value;
        ObjectUuid uuid = SceneWorld.ToUuid(value.Reference.WorldId);
        Reference = new(uuid.High, uuid.Low, value.Reference.Id);
    }
    public ulong Id => Reference.Id;
    public ObjectReference Reference { get; }
    public ObjectUuid PersistentId { get { World.Verify(); return SceneWorld.ToUuid(_object.PersistentId); } }
    public SceneWorld World { get; }
    public string Name { get { World.Verify(); return _object.Name; } set { World.Verify(); _object.Name = value; } }
    public bool HasTransform { get { World.Verify(); return _object.Has<TransformData>(); } }
    public Transform LocalTransform
    {
        get { World.Verify(); return SceneWorld.FromData(_object.Get<TransformData>()); }
        set { World.Verify(); _object.Set(SceneWorld.ToData(value)); }
    }
    public bool Destroy() { World.Verify(); _object.Destroy(); World.Forget(Id); return true; }
}
