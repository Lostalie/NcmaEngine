using System.Runtime.InteropServices;

namespace Ncma;

[StructLayout(LayoutKind.Sequential)]
public readonly struct ObjectReference
{
    public readonly ulong WorldHigh;
    public readonly ulong WorldLow;
    public readonly ulong Id;
    internal ObjectReference(ulong high, ulong low, ulong id) { WorldHigh = high; WorldLow = low; Id = id; }
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct ObjectUuid
{
    public readonly ulong High;
    public readonly ulong Low;
    public ObjectUuid(ulong high, ulong low) { High = high; Low = low; }
}

[StructLayout(LayoutKind.Sequential)]
public struct TransformWrite
{
    public ObjectReference Object;
    public Transform Value;
    public TransformWrite(GameObject target, Transform value) { Object = target.Reference; Value = value; }
}

[StructLayout(LayoutKind.Sequential)]
public struct GameplaySignal
{
    public ObjectReference Source;
    public ObjectReference Target;
    public uint Code;
    public double Value;
    public ulong Sequence;
}

public sealed partial class SceneWorld
{
    public const int AccessCapacity = 4096;

    public GameObject FindObject(ObjectUuid uuid)
    {
        if (Native.WorldFindObject(Handle, uuid.High, uuid.Low, out ObjectReference reference) == 0)
            throw new InvalidOperationException(Native.GetLastError());
        return new GameObject(this, reference.Id);
    }

    // Keep arrays/Spans for reuse. Only one ABI crossing per batch, no JSON/reflection.
    public unsafe void ReadTransforms(ReadOnlySpan<ObjectReference> objects, Span<Transform> output)
    {
        if (objects.Length != output.Length || objects.Length > AccessCapacity)
            throw new ArgumentException("Batch lengths must match and be <=4096.");
        fixed (ObjectReference* source = objects)
        fixed (Transform* destination = output)
            if (Native.WorldReadTransforms(Handle, source, destination, (uint)objects.Length) == 0)
                throw new InvalidOperationException(Native.GetLastError());
    }

    public unsafe void WriteTransforms(ReadOnlySpan<TransformWrite> writes)
    {
        if (writes.Length > AccessCapacity) throw new ArgumentOutOfRangeException(nameof(writes));
        fixed (TransformWrite* source = writes)
            if (Native.WorldWriteTransforms(Handle, source, (uint)writes.Length) == 0)
                throw new InvalidOperationException(Native.GetLastError());
    }

    public void SendSignal(GameObject source, GameObject target, uint code, double value = 0)
    {
        if (source.World != this || target.World != this)
            throw new ArgumentException("Signal endpoints must belong to this World.");
        GameplaySignal signal = new() { Source = source.Reference, Target = target.Reference, Code = code, Value = value };
        if (Native.WorldSendSignal(Handle, in signal) == 0)
            throw new InvalidOperationException(Native.GetLastError());
    }

    public unsafe int ReceiveSignals(GameObject target, Span<GameplaySignal> output)
    {
        if (target.World != this || output.Length > AccessCapacity)
            throw new ArgumentException("Invalid target or signal buffer size.");
        fixed (GameplaySignal* destination = output)
        {
            if (Native.WorldReceiveSignals(Handle, target.Reference, destination, (uint)output.Length, out uint count) == 0)
                throw new InvalidOperationException(Native.GetLastError());
            return (int)count;
        }
    }
}
