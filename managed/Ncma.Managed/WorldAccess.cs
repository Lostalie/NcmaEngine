using System.Runtime.InteropServices;
using Ncma.Runtime;

namespace Ncma;

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
    public Entity Object;
    public Transform Value;
    public TransformWrite(GameObject target, Transform value) { Object = target.Entity; Value = value; }
}

[StructLayout(LayoutKind.Sequential)]
public struct GameplaySignal
{
    public Entity Source;
    public Entity Target;
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
        return new(this, Runtime.FindObject(ToGuid(uuid.High, uuid.Low)).Entity);
    }
    public void ReadTransforms(ReadOnlySpan<Entity> objects, Span<Transform> output)
    {
        Verify();
        if (objects.Length != output.Length || objects.Length > AccessCapacity) throw new ArgumentException("Invalid batch lengths.");
        var values = new Transform[objects.Length];
        for (int i = 0; i < objects.Length; i++) values[i] = FromData(Runtime.Get<TransformData>(Resolve(objects[i])));
        values.CopyTo(output);
    }
    public void WriteTransforms(ReadOnlySpan<TransformWrite> writes)
    {
        Verify(); Runtime.VerifyWriteAccess();
        if (writes.Length > AccessCapacity) throw new ArgumentException("Invalid batch length.");
        var targets = new HashSet<Entity>();
        var staged = new (Entity, TransformData)[writes.Length];
        for (int i = 0; i < writes.Length; i++)
        {
            var target = Resolve(writes[i].Object);
            Runtime.VerifyUnownedComponentWrite(target, typeof(TransformData));
            if (!targets.Add(target) || !Runtime.Has<TransformData>(target)) throw new ArgumentException("Duplicate target or missing Transform.");
            staged[i] = (target, Runtime.Components.Validate(ToData(writes[i].Value)));
        }
        foreach (var (target, value) in staged) Runtime.Set(target, value);
    }
    public void SendSignal(GameObject source, GameObject target, uint code, double value = 0)
    {
        Verify(); Runtime.VerifyWriteAccess();
        _ = Resolve(source.Entity); _ = Resolve(target.Entity);
        if (source.World != this || target.World != this || !double.IsFinite(value)) throw new ArgumentException("Invalid signal.");
        if (_ready.Count + _pendingSignals.Count >= AccessCapacity || _sequence == ulong.MaxValue) throw new InvalidOperationException("Signal capacity exceeded.");
        GameplaySignal signal = new() { Source = source.Entity, Target = target.Entity, Code = code, Value = value, Sequence = _sequence++, Epoch = _signalEpoch };
        (_phase ? _pendingSignals : _ready).Add(signal);
    }
    public int ReceiveSignals(GameObject target, Span<GameplaySignal> output)
    {
        Verify(); Runtime.VerifyWriteAccess(); _ = Resolve(target.Entity);
        if (target.World != this || output.Length > AccessCapacity) throw new ArgumentException("Invalid signal target/buffer.");
        int count = 0;
        for (int i = 0; i < _ready.Count && count < output.Length;)
            if (_ready[i].Target == target.Entity && !_consumed.Contains(_ready[i].Sequence))
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
            var entities = Runtime.GetObjects().Where(o => alive.Contains(o.PersistentId)).Select(o => o.Entity).ToHashSet();
            ready = _ready.Where(s => !_consumed.Contains(s.Sequence) && entities.Contains(s.Source) && entities.Contains(s.Target))
                .Concat(_pendingSignals.Where(s => entities.Contains(s.Source) && entities.Contains(s.Target))).ToList();
            if (ready.Count > AccessCapacity) throw new InvalidOperationException("Signal capacity exceeded.");
            inspect?.Invoke(world);
        }, requireAuthorityWrites: !preflight);
        return () => { commit(); _ready = ready!; _pendingSignals.Clear(); _consumed.Clear(); _phase = false; };
    }
    internal void CommitPhase() => PreparePhase()();
    internal void AbortPhase() { Verify(); Runtime.AbortStep(); _pendingSignals.Clear(); _consumed.Clear(); _phase = false; }
    internal void Forget(Entity entity) { _ready.RemoveAll(s => s.Source == entity || s.Target == entity); _pendingSignals.RemoveAll(s => s.Source == entity || s.Target == entity); }
    internal void Restored() { _ready.Clear(); _pendingSignals.Clear(); _sequence = 1; _signalEpoch = Guid.NewGuid(); }
}
