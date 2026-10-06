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
    public Guid Epoch;
}

public sealed partial class SceneWorld
{
    public const int AccessCapacity = 4096;
    private List<GameplaySignal> _ready = [];
    private readonly List<GameplaySignal> _pendingSignals = [];
    private readonly HashSet<ulong> _consumed = [];
    private ulong _sequence = 1;
    private Guid _signalEpoch = Guid.NewGuid();
    private bool _phase;
    public GameObject FindObject(ObjectUuid uuid)
    {
        Verify();
        return new(this, Runtime.FindObject(ToGuid(uuid.High, uuid.Low)));
    }
    public void ReadTransforms(ReadOnlySpan<ObjectReference> objects, Span<Transform> output)
    {
        Verify();
        if (objects.Length != output.Length || objects.Length > AccessCapacity) throw new ArgumentException("Invalid batch lengths.");
        var values = new Transform[objects.Length];
        for (int i = 0; i < objects.Length; i++) values[i] = FromData(Resolve(objects[i]).Get<Ncma.Runtime.TransformData>());
        values.CopyTo(output);
    }
    public void WriteTransforms(ReadOnlySpan<TransformWrite> writes)
    {
        Verify(); Runtime.VerifyWriteAccess();
        if (writes.Length > AccessCapacity) throw new ArgumentException("Invalid batch length.");
        var targets = new HashSet<ulong>();
        var staged = new (Ncma.Runtime.GameObject, Ncma.Runtime.TransformData)[writes.Length];
        for (int i = 0; i < writes.Length; i++)
        {
            var target = Resolve(writes[i].Object);
            Runtime.VerifyUnownedComponentWrite(target.Reference, typeof(Ncma.Runtime.TransformData));
            if (!targets.Add(target.Reference.Id) || !target.Has<Ncma.Runtime.TransformData>()) throw new ArgumentException("Duplicate target or missing Transform.");
            staged[i] = (target, (Ncma.Runtime.TransformData)Runtime.Components.Validate(ToData(writes[i].Value)));
        }
        foreach (var (target, value) in staged) target.Set(value);
    }
    public void SendSignal(GameObject source, GameObject target, uint code, double value = 0)
    {
        Verify(); Runtime.VerifyWriteAccess();
        _ = Resolve(source.Reference); _ = Resolve(target.Reference);
        if (source.World != this || target.World != this || !double.IsFinite(value)) throw new ArgumentException("Invalid signal.");
        if (_ready.Count + _pendingSignals.Count >= AccessCapacity || _sequence == ulong.MaxValue) throw new InvalidOperationException("Signal capacity exceeded.");
        GameplaySignal signal = new() { Source = source.Reference, Target = target.Reference, Code = code, Value = value, Sequence = _sequence++, Epoch = _signalEpoch };
        (_phase ? _pendingSignals : _ready).Add(signal);
    }
    public int ReceiveSignals(GameObject target, Span<GameplaySignal> output)
    {
        Verify(); Runtime.VerifyWriteAccess(); _ = Resolve(target.Reference);
        if (target.World != this || output.Length > AccessCapacity) throw new ArgumentException("Invalid signal target/buffer.");
        int count = 0;
        for (int i = 0; i < _ready.Count && count < output.Length;)
            if (_ready[i].Target.Id == target.Id && !_consumed.Contains(_ready[i].Sequence))
            {
                output[count++] = _ready[i];
                if (_phase) { _consumed.Add(_ready[i].Sequence); i++; } else _ready.RemoveAt(i);
            }
            else i++;
        return count;
    }
    internal void BeginPhase(bool initialization = false) { Verify(); Runtime.BeginStep(initialization); _phase = true; }
    internal Action PreparePhase(Action<Ncma.Runtime.WorldSnapshot>? inspect = null, bool preflight = false)
    {
        Verify();
        if (!_phase) throw new InvalidOperationException("No active gameplay phase.");
        List<GameplaySignal>? ready = null;
        Action commit = Runtime.PrepareStep(world =>
        {
            var alive = world.Objects.Select(o => o.PersistentId).ToHashSet();
            var ids = Runtime.GetObjects().Where(o => alive.Contains(o.PersistentId)).Select(o => o.Reference.Id).ToHashSet();
            ready = _ready.Where(s => !_consumed.Contains(s.Sequence) && ids.Contains(s.Source.Id) && ids.Contains(s.Target.Id))
                .Concat(_pendingSignals.Where(s => ids.Contains(s.Source.Id) && ids.Contains(s.Target.Id))).ToList();
            if (ready.Count > AccessCapacity) throw new InvalidOperationException("Signal capacity exceeded.");
            inspect?.Invoke(world);
        }, requireAuthorityWrites: !preflight);
        return () => { commit(); _ready = ready!; _pendingSignals.Clear(); _consumed.Clear(); _phase = false; };
    }
    internal void CommitPhase() => PreparePhase()();
    internal void AbortPhase() { Verify(); Runtime.AbortStep(); _pendingSignals.Clear(); _consumed.Clear(); _phase = false; }
    internal void Forget(ulong id) { _ready.RemoveAll(s => s.Source.Id == id || s.Target.Id == id); _pendingSignals.RemoveAll(s => s.Source.Id == id || s.Target.Id == id); }
    internal void Restored() { _ready.Clear(); _pendingSignals.Clear(); _sequence = 1; _signalEpoch = Guid.NewGuid(); }
}
