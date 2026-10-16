using System.Globalization;
using Ncma.Runtime;
namespace Ncma;

// Gameplay services over the sole Runtime.World; never a second component store.
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
        return new(this, Runtime.CreateObject(name).Entity); // Empty container: Transform is optional.
    }
    internal Entity Resolve(Entity entity)
    {
        Verify();
        return Runtime.Resolve(entity);
    }
    public IReadOnlyList<GameObject> GetObjects() { Verify(); return Runtime.GetObjects().Select(o => new GameObject(this, o.Entity)).ToArray(); }
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

public sealed class GameObject : IEquatable<GameObject>
{
    internal GameObject(SceneWorld world, Entity entity)
    {
        World = world; Entity = entity;
    }
    public Entity Entity { get; }
    public ObjectUuid PersistentId { get { World.Verify(); return SceneWorld.ToUuid(World.Runtime.GetUuid(Entity)); } }
    public SceneWorld World { get; }
    public string Name { get { World.Verify(); return World.Runtime.GetName(Entity); } set { World.Verify(); World.Runtime.Rename(Entity, value); } }
    public bool HasTransform { get { World.Verify(); return World.Runtime.Has<TransformData>(Entity); } }
    public Transform LocalTransform
    {
        get { World.Verify(); return SceneWorld.FromData(World.Runtime.Get<TransformData>(Entity)); }
        set { World.Verify(); World.Runtime.Set(Entity, SceneWorld.ToData(value)); }
    }
    public bool Destroy() { World.Verify(); World.Runtime.Destroy(Entity); World.Forget(Entity); return true; }
    public bool Equals(GameObject? other) => other is not null && Entity == other.Entity;
    public override bool Equals(object? other) => other is GameObject value && Equals(value);
    public override int GetHashCode() => Entity.GetHashCode();
    public static bool operator ==(GameObject? left, GameObject? right) => Equals(left, right);
    public static bool operator !=(GameObject? left, GameObject? right) => !Equals(left, right);
}
