using System.Numerics;
using System.Runtime.CompilerServices;
using Ncma.Interop;

namespace Ncma.Physics;

// Dense buffers are solver resources only, never a duplicate GameObject/component store.
// Creation preallocates managed tracking; hot Step/Read/flush reuse all buffers.
public sealed class PhysicsSimulation : IDisposable
{
    private readonly PhysicsService _owner;
    private readonly PhysicsWorld _native;
    private readonly PhysicsSimulationSettings _settings;
    private readonly Dictionary<ulong, int> _indices;
    private readonly ulong[] _handles, _scratch, _validation;
    private readonly bool[] _dynamic, _pending;
    private readonly Box2D[] _boxes2;
    private readonly Box3D[] _boxes3;
    private readonly BodyState2D[] _states2;
    private readonly BodyState3D[] _states3;
    private readonly Velocity2D[] _velocities2, _flush2;
    private readonly Velocity3D[] _velocities3, _flush3;
    private readonly PhysicsSampleWindow _profile = new();
    private int _count, _pendingCount;
    private ulong _sequence;
    private bool _busy;
    private PhysicsCounters _lastCounters;
    private PhysicsSimulationState _state = PhysicsSimulationState.Paused;
    public Guid WorldId { get; }
    public PhysicsDimension Dimension => _settings.Dimension;
    public PhysicsSimulationState State { get { Verify(false); return _state; } }
    public int BodyCount { get { Verify(false); return _count; } }
    internal PhysicsSimulation(PhysicsService owner, Guid id, PhysicsSimulationSettings settings, PluginModule module)
    {
        _owner = owner; WorldId = id; _settings = settings;
        int max = settings.MaximumBodies;
        // Allocate before native creation, so managed buffer allocation failure leaks no world.
        _indices = new(max); _handles = new ulong[max]; _scratch = new ulong[max]; _validation = new ulong[max];
        _dynamic = new bool[max]; _pending = new bool[max];
        _boxes2 = Dimension == PhysicsDimension.Two ? new Box2D[max] : [];
        _boxes3 = Dimension == PhysicsDimension.Three ? new Box3D[max] : [];
        _states2 = Dimension == PhysicsDimension.Two ? new BodyState2D[max] : [];
        _states3 = Dimension == PhysicsDimension.Three ? new BodyState3D[max] : [];
        _velocities2 = Dimension == PhysicsDimension.Two ? new Velocity2D[max] : [];
        _velocities3 = Dimension == PhysicsDimension.Three ? new Velocity3D[max] : [];
        _flush2 = Dimension == PhysicsDimension.Two ? new Velocity2D[max] : [];
        _flush3 = Dimension == PhysicsDimension.Three ? new Velocity3D[max] : [];
        _native = Dimension == PhysicsDimension.Two
            ? PhysicsWorld.Create2D(module, new(settings.Gravity.X, settings.Gravity.Y), (uint)max, settings.SubSteps)
            : PhysicsWorld.Create3D(module, settings.Gravity, (uint)max);
    }
    private void Verify(bool ready = true)
    {
        _owner.Verify();
        ObjectDisposedException.ThrowIf(_state == PhysicsSimulationState.Disposed, this);
        if (_busy) throw new InvalidOperationException("Physics simulation reentry forbidden.");
        if (ready && _state == PhysicsSimulationState.Faulted) throw new InvalidOperationException("Physics simulation faulted; dispose and recreate.");
    }
    private void VerifyBatch(PhysicsDimension dimension, int count)
    {
        Verify();
        if (dimension != Dimension || count > _handles.Length) throw new ArgumentException("Wrong physics dimension or batch budget.");
    }
    private int Slot(PhysicsBodyReference body)
    {
        if (body.ServiceId != _owner.ServiceId || body.WorldId != WorldId || !_indices.TryGetValue(body.Handle, out int slot))
            throw new ArgumentException("Foreign or stale physics body reference.");
        return slot;
    }
    private void Unique(int count)
    {
        Array.Sort(_validation, 0, count);
        for (int i = 1; i < count; i++)
            if (_validation[i] == _validation[i - 1]) throw new ArgumentException("Duplicate physics body target.");
    }
    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && Math.Abs(value.X) <= 100000 && Math.Abs(value.Y) <= 100000;
    private static bool Finite(Vector3 value) => Finite(new Vector2(value.X, value.Y)) && float.IsFinite(value.Z) && Math.Abs(value.Z) <= 100000;
    private PhysicsBodyReference Reference(ulong handle) => new(_owner.ServiceId, WorldId, handle);
    private void CreationBudget(int count, int capacity)
    {
        if (count > _handles.Length - _count || capacity < count) throw new ArgumentException("Body/output capacity exceeded.");
    }
    public void CreateBoxes2D(ReadOnlySpan<Box2D> boxes, Span<PhysicsBodyReference> output)
    {
        VerifyBatch(PhysicsDimension.Two, boxes.Length); CreationBudget(boxes.Length, output.Length);
        boxes.CopyTo(_boxes2);
        try { _native.CreateBoxes2D(_boxes2.AsSpan(0, boxes.Length), _scratch.AsSpan(0, boxes.Length)); }
        catch (Exception e) { NativeFailure("physics.create_boxes_2d", e, false); throw; }
        for (int i = 0; i < boxes.Length; i++)
        {
            int slot = _count++; ulong handle = _scratch[i]; var box = _boxes2[i];
            _handles[slot] = handle; _indices.Add(handle, slot); _dynamic[slot] = box.Dynamic != 0;
            _states2[slot] = new() { Body = handle, Correlation = box.Correlation, Sequence = _sequence, Position = box.Position };
            output[i] = Reference(handle);
        }
    }
    public void CreateBoxes3D(ReadOnlySpan<Box3D> boxes, Span<PhysicsBodyReference> output)
    {
        VerifyBatch(PhysicsDimension.Three, boxes.Length); CreationBudget(boxes.Length, output.Length);
        boxes.CopyTo(_boxes3);
        try { _native.CreateBoxes3D(_boxes3.AsSpan(0, boxes.Length), _scratch.AsSpan(0, boxes.Length)); }
        catch (Exception e) { NativeFailure("physics.create_boxes_3d", e, false); throw; }
        for (int i = 0; i < boxes.Length; i++)
        {
            int slot = _count++; ulong handle = _scratch[i]; var box = _boxes3[i];
            _handles[slot] = handle; _indices.Add(handle, slot); _dynamic[slot] = box.Dynamic != 0;
            _states3[slot] = new() { Body = handle, Correlation = box.Correlation, Sequence = _sequence, Position = box.Position, Rotation = Quaternion.Identity };
            output[i] = Reference(handle);
        }
    }
    public void StageVelocities2D(ReadOnlySpan<PhysicsVelocity2D> values)
    {
        VerifyBatch(PhysicsDimension.Two, values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            int slot = Slot(values[i].Body);
            if (!_dynamic[slot] || !Finite(values[i].Velocity)) throw new ArgumentException("Invalid dynamic body velocity.");
            _validation[i] = values[i].Body.Handle;
        }
        Unique(values.Length);
        foreach (var value in values)
        {
            int slot = Slot(value.Body);
            if (!_pending[slot]) { _pending[slot] = true; _pendingCount++; }
            _velocities2[slot] = new() { Body = value.Body.Handle, Velocity = value.Velocity };
        }
    }
    public void StageVelocities3D(ReadOnlySpan<PhysicsVelocity3D> values)
    {
        VerifyBatch(PhysicsDimension.Three, values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            int slot = Slot(values[i].Body);
            if (!_dynamic[slot] || !Finite(values[i].Velocity)) throw new ArgumentException("Invalid dynamic body velocity.");
            _validation[i] = values[i].Body.Handle;
        }
        Unique(values.Length);
        foreach (var value in values)
        {
            int slot = Slot(value.Body);
            if (!_pending[slot]) { _pending[slot] = true; _pendingCount++; }
            _velocities3[slot] = new() { Body = value.Body.Handle, Velocity = value.Velocity };
        }
    }
    public void CancelPendingVelocities() { Verify(); Array.Clear(_pending); _pendingCount = 0; }
    public void Pause() { Verify(); _state = PhysicsSimulationState.Paused; }
    public void Resume() { Verify(); _state = PhysicsSimulationState.Running; }
    public ulong Step()
    {
        Verify(); if (_state != PhysicsSimulationState.Running) throw new InvalidOperationException("Resume before a running physics step.");
        return ExecuteStep();
    }
    public ulong StepOnce()
    {
        Verify(); if (_state != PhysicsSimulationState.Paused) throw new InvalidOperationException("Single step requires a paused physics simulation.");
        return ExecuteStep();
    }
    // This bounded numerical hot path should enter optimized code directly instead of
    // transitioning to OSR in the middle of its first large compaction batches.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private ulong ExecuteStep()
    {
        _busy = true;
        try
        {
            int count = 0;
            for (int i = 0; i < _count; i++) if (_pending[i])
            {
                if (Dimension == PhysicsDimension.Two) _flush2[count] = _velocities2[i]; else _flush3[count] = _velocities3[i];
                count++;
            }
            if (count != 0)
            {
                if (Dimension == PhysicsDimension.Two) _native.SetVelocities2D(_flush2.AsSpan(0, count));
                else _native.SetVelocities3D(_flush3.AsSpan(0, count));
                Array.Clear(_pending); _pendingCount = 0;
            }
            ulong sequence = _native.Step(_settings.FixedSeconds);
            if (Dimension == PhysicsDimension.Two) _native.ReadBodyStates2D(sequence, _handles.AsSpan(0, _count), _states2.AsSpan(0, _count));
            else _native.ReadBodyStates3D(sequence, _handles.AsSpan(0, _count), _states3.AsSpan(0, _count));
            var counters = _native.NativeCounters;
            if (counters.State != 1 || counters.Sequence != sequence) throw new InvalidOperationException("Physics step counter mismatch.");
            _lastCounters = counters;
            _profile.Add(counters.LastStepMilliseconds);
            _sequence = sequence; return sequence;
        }
        catch (Exception e) { NativeFailure("physics.step", e, true); throw; }
        finally { _busy = false; }
    }
    private void NativeFailure(string operation, Exception error, bool fault)
    {
        if (fault || error is PluginException { Result: PluginResult.InternalError }) _state = PhysicsSimulationState.Faulted;
        _owner.Record(WorldId, _sequence, operation, error);
    }
    public void DestroyBodies(ReadOnlySpan<PhysicsBodyReference> bodies)
    {
        VerifyBatch(Dimension, bodies.Length);
        for (int i = 0; i < bodies.Length; i++) { _ = Slot(bodies[i]); _scratch[i] = _validation[i] = bodies[i].Handle; }
        Unique(bodies.Length);
        try { _native.DestroyBodies(_scratch.AsSpan(0, bodies.Length)); }
        catch (Exception e) { NativeFailure("physics.destroy_bodies", e, true); throw; }
        for (int i = 0; i < bodies.Length; i++)
        {
            ulong handle = _scratch[i]; int slot = _indices[handle]; int last = --_count;
            if (_pending[slot]) _pendingCount--;
            _indices.Remove(handle);
            if (slot != last)
            {
                _handles[slot] = _handles[last]; _indices[_handles[slot]] = slot;
                _dynamic[slot] = _dynamic[last]; _pending[slot] = _pending[last];
                if (Dimension == PhysicsDimension.Two) { _states2[slot] = _states2[last]; _velocities2[slot] = _velocities2[last]; }
                else { _states3[slot] = _states3[last]; _velocities3[slot] = _velocities3[last]; }
            }
            _handles[last] = 0; _pending[last] = false; _dynamic[last] = false;
        }
    }
    private int Page(int offset, int capacity)
    {
        Verify(); if (offset < 0 || offset > _count || capacity > PhysicsWorld.MaximumBatch) throw new ArgumentException("Invalid physics snapshot page.");
        return Math.Min(capacity, _count - offset);
    }
    public int CopyBodies(Span<PhysicsBodyReference> output, int offset = 0)
    {
        int count = Page(offset, output.Length);
        for (int i = 0; i < count; i++) output[i] = Reference(_handles[offset + i]);
        return count;
    }
    public int CopyStates2D(Span<BodyState2D> output, int offset = 0)
    {
        VerifyBatch(PhysicsDimension.Two, output.Length); int count = Page(offset, output.Length);
        _states2.AsSpan(offset, count).CopyTo(output); return count;
    }
    public int CopyStates3D(Span<BodyState3D> output, int offset = 0)
    {
        VerifyBatch(PhysicsDimension.Three, output.Length); int count = Page(offset, output.Length);
        _states3.AsSpan(offset, count).CopyTo(output); return count;
    }
    public PhysicsWorldInspection Inspect()
    {
        _owner.VerifyThread(); ObjectDisposedException.ThrowIf(_state == PhysicsSimulationState.Disposed, this);
        if (_busy) throw new InvalidOperationException("Physics simulation reentry forbidden.");
        bool available = true;
        try
        {
            _lastCounters = _native.NativeCounters;
            if (_lastCounters.State == 2 || _lastCounters.Sequence != _sequence || _lastCounters.LiveBodies != (ulong)_count)
            {
                if (_state != PhysicsSimulationState.Faulted)
                    NativeFailure("physics.inspect", new InvalidOperationException("Native physics fault/state mismatch; published snapshot invalidated."), true);
                _state = PhysicsSimulationState.Faulted;
            }
        }
        catch (Exception e) { available = false; NativeFailure("physics.inspect", e, true); }
        return new(_owner.ServiceId, WorldId, Dimension, _state, _settings.FixedSeconds,
            _pendingCount, _sequence, _state != PhysicsSimulationState.Faulted, available, _lastCounters, _profile.Inspect());
    }
    public void Dispose()
    {
        _owner.VerifyThread(); if (_state == PhysicsSimulationState.Disposed) return;
        if (_busy) throw new InvalidOperationException("Cannot dispose an executing physics simulation.");
        try { _native.Dispose(); }
        catch (Exception e) { NativeFailure("physics.destroy_world", e, true); throw; }
        _state = PhysicsSimulationState.Disposed; _pendingCount = _count = 0;
        _indices.Clear(); Array.Clear(_handles); Array.Clear(_pending);
        Array.Clear(_states2); Array.Clear(_states3); _owner.Remove(this);
    }
}
