using V3 = System.Numerics.Vector3;
using Ncma.Gameplay;
using Ncma.Runtime;

namespace Ncma.Movement;

// C# owns identity, intentions, publication and cross-domain failure policy; adapters own numerics only.
// Registration is trusted host composition, not a sandbox against arbitrary C# reflection/native code.
public sealed class MovementCoordinator : IDisposable, ICoupledStepParticipant
{
    public const int MaxTargets = World.MaxObjects;
    public const float MaxDisplacementPerStep = 1000;
    public const float MaxPositionMagnitude = 1_000_000;
    private readonly PlaySession _play;
    private World World => _play.Document.World;
    private readonly Guid[] _targets;
    private readonly Dictionary<Guid, int> _index;
    private readonly Func<INumericMovementAdapter> _factory;
    private readonly byte[] _startup;
    private readonly NumericMovementInput[] _inputs;
    private readonly NumericMovementResult[] _results, _committed;
    private readonly bool[] _submitted;
    private World.ComponentAuthority? _authority;
    private INumericMovementAdapter? _adapter;
    private readonly List<Func<World.ComponentAuthority>> _frozenFactories = [];
    private readonly List<World.ComponentAuthority> _frozenOwned = [];
    // Trusted startup-only configuration reservation; no proof is exposed to gameplay.
    // Configurations are immutable during Play, rather than silently disagreeing with solver shapes.
    public void FreezeConfiguration<T>(ReadOnlySpan<Guid> objects, bool freezeMembership = true) where T : struct, IComponent
    {
        Verify();
        if (_play.State != PlayState.Stopped || objects.Length > MaxTargets)
            throw new ArgumentException("Freeze a bounded configuration set before Start.");
        Guid[] ids = objects.ToArray();
        foreach (Guid id in ids) if (!World.FindObject(id).Has<T>()) throw new ArgumentException("Missing frozen configuration.");
        _frozenFactories.Add(() => World.ClaimComponents<T>(ids, publishRequired: false, freezeMembership: freezeMembership));
    }
    private MovementStepStamp _stamp;
    private Guid _lastWorldId, _sessionId, _worldId;
    private ulong _sequence, _committedTick, _attemptTick;
    private bool _collecting, _executed, _snapshotValid, _faulted, _disposed;

    public MovementCoordinator(PlaySession play, ReadOnlySpan<Guid> targets, Func<INumericMovementAdapter> factory)
    {
        ArgumentNullException.ThrowIfNull(play); ArgumentNullException.ThrowIfNull(factory);
        play.Document.World.VerifyAccess();
        if (play.State != PlayState.Stopped || targets.IsEmpty || targets.Length > MaxTargets)
            throw new ArgumentException("Movement composition requires stopped Play and a bounded nonempty target set.");
        _play = play; _factory = factory; _targets = targets.ToArray(); _index = [];
        for (int i = 0; i < _targets.Length; i++)
            if (!_index.TryAdd(_targets[i], i) || !World.FindObject(_targets[i]).Has<TransformData>())
                throw new ArgumentException("Duplicate or missing movement target.");
        _inputs = new NumericMovementInput[_targets.Length]; _results = new NumericMovementResult[_targets.Length];
        _committed = new NumericMovementResult[_targets.Length]; _submitted = new bool[_targets.Length];
        _startup = play.Document.CaptureBytes();
        play.AttachCoupledParticipant(this);
    }

    public CoupledMovementStatus Status
    {
        get { Verify(); return new(_sessionId, _worldId, _committedTick, _sequence, _attemptTick,
            _executed, SnapshotMatches, _adapter is not null || _authority is not null, _faulted); }
    }

    public MovementStepStamp CurrentStep
    {
        get { Verify(); if (!_collecting || !World.IsUpdating) throw new InvalidOperationException("No collecting movement step."); return _stamp; }
    }

    public void Submit(MovementIntent intent)
    {
        Verify();
        try
        {
            World.VerifyWriteAccess();
            if (!_collecting || !World.IsUpdating || _faulted || intent.Stamp != _stamp ||
                !_index.TryGetValue(intent.ObjectId, out int index) || _submitted[index])
                throw new ArgumentException("Stale, foreign, duplicate or out-of-phase movement intention.");
            if (!Finite(intent.Displacement) || intent.Displacement.LengthSquared() > MaxDisplacementPerStep * MaxDisplacementPerStep ||
                !float.IsFinite(intent.YawRadians) || MathF.Abs(intent.YawRadians) > MathF.PI)
                throw new ArgumentException("Movement intention exceeds finite displacement/yaw limits.");
            _inputs[index] = _inputs[index] with { Displacement = intent.Displacement, YawRadians = intent.YawRadians };
            _submitted[index] = true;
        }
        catch (Exception error) { World.RejectStep(error); throw; }
    }

    public NumericMovementResult[] ReadCommitted()
    {
        Verify();
        if (!SnapshotMatches) throw new InvalidOperationException("Coupled numerical snapshot is invalid or stopped.");
        return (NumericMovementResult[])_committed.Clone();
    }
    public void VerifyControlBoundary()
    {
        Verify();World.VerifyWriteAccess();
        if(World.IsUpdating || _collecting || _faulted || !SnapshotMatches || _play.State is not (PlayState.Running or PlayState.Paused)) {
            var error=new InvalidOperationException("Movement control requires a synchronized owner-thread safe boundary.");
            World.RejectStep(error);throw error;
        }
    }

    void ICoupledStepParticipant.Start()
    {
        Verify();
        if (_adapter is not null || _authority is not null || World.Identity == _lastWorldId ||
            !_play.Document.CaptureBytes().AsSpan().SequenceEqual(_startup))
            throw new InvalidOperationException("Rebuild from the frozen startup document before reusing coupled Play.");
        FillInputs();
        _authority = World.ClaimComponents<TransformData>(_targets);
        foreach (var claim in _frozenFactories) _frozenOwned.Add(claim());
        _worldId = World.Identity; _lastWorldId = _worldId; _sessionId = _play.SessionId;
        _sequence = 0; _committedTick = World.Tick; _attemptTick = World.Tick;
        _collecting = _executed = _snapshotValid = _faulted = false;
        // Retain the adapter/authority after initialization failure until Stop succeeds.
        using (World.ReadOnly())
        {
            _adapter = _factory() ?? throw new ArgumentException("Null numerical adapter.");
            _adapter.Initialize(_sessionId, _worldId, _inputs);
        }
        for (int i = 0; i < _targets.Length; i++) _committed[i] = new(_targets[i], _inputs[i].Start);
        _snapshotValid = true;
    }

    void ICoupledStepParticipant.BeginStep()
    {
        Verify();
        if (_adapter is null || _authority is null || _faulted || !_snapshotValid || !World.IsUpdating ||
            World.Identity != _worldId || _play.SessionId != _sessionId || World.Tick != _committedTick)
            throw new InvalidOperationException("Coupled session identity/tick is not synchronized.");
        _stamp = new(_sessionId, _worldId, World.Tick, World.Revision, checked(_sequence + 1));
        _attemptTick = checked(World.Tick + 1);
        FillInputs(); Array.Clear(_submitted); Array.Clear(_results);
        _collecting = true; _executed = false;
    }

    void ICoupledStepParticipant.Preflight()
    {
        VerifyStamp(); _collecting = false;
        _adapter!.Preflight(_stamp, _play.FixedDeltaSeconds, _inputs);
    }

    void ICoupledStepParticipant.ExecuteAndStage()
    {
        VerifyStamp();
        _executed = true; _snapshotValid = false; // Set BEFORE the first possibly irreversible call.
        NumericMovementReceipt receipt;
        using (World.PreparationReadOnly()) receipt = _adapter!.Execute(_stamp, _play.FixedDeltaSeconds, _inputs, _results);
        if (receipt.Stamp != _stamp || receipt.Count != _targets.Length) throw new ArgumentException("Invalid numerical result stamp/count.");
        // Validate every result before publishing any owned component write.
        for (int i = 0; i < _results.Length; i++)
        {
            var result = _results[i];
            result = result with { Transform = ValidateTransform(result.Transform) };
            if (result.ObjectId != _targets[i] || result.Transform.Scale != _inputs[i].Start.Scale ||
                V3.DistanceSquared(result.Transform.Position, _inputs[i].Start.Position) > MaxDisplacementPerStep * MaxDisplacementPerStep)
                throw new ArgumentException("Numerical results violate exact target order, fixed scale or movement bounds.");
            _results[i] = result;
        }
        for (int i = 0; i < _results.Length; i++)
            if (World.WriteOwned(_authority!, _results[i].ObjectId, _results[i].Transform) != _results[i].Transform)
                throw new InvalidOperationException("Registered component validation changed the coupled numerical result.");
    }

    void ICoupledStepParticipant.Committed()
    {
        Verify();
        if (!_executed || World.IsUpdating || World.Tick != _attemptTick) throw new InvalidOperationException("Invalid coupled commit boundary.");
        _results.CopyTo(_committed, 0); _sequence = _stamp.Sequence; _committedTick = World.Tick;
        _snapshotValid = true; _executed = _collecting = false;
    }

    void ICoupledStepParticipant.Faulted()
    {
        // No callback/throw here: preserve the original Play fault, including committed observer failures.
        _faulted = true; _collecting = false;
        if (_executed) _snapshotValid = false;
    }

    void ICoupledStepParticipant.Stop()
    {
        Verify(); _collecting = false; _snapshotValid = false;
        // Failed close retains BOTH numerical ownership and write authority for explicit retry.
        using (World.ReadOnly()) _adapter?.Dispose();
        _adapter = null;
        foreach (var claim in _frozenOwned) claim.Dispose();
        _frozenOwned.Clear();
        _authority?.Dispose(); _authority = null;
        _executed = false;
        Array.Clear(_inputs); Array.Clear(_results); Array.Clear(_committed); Array.Clear(_submitted);
    }

    void ICoupledStepParticipant.RebuildStartup()
    {
        Verify();
        if (_adapter is not null || _authority is not null || _play.State != PlayState.Stopped)
            throw new InvalidOperationException("Release coupled resources before rebuilding startup.");
        // Scene restore creates a fresh World identity. Tick chronology is retained, never rolled back.
        _play.Document.RestoreBytes(_startup);
    }

    private void FillInputs()
    {
        for (int i = 0; i < _targets.Length; i++)
        {
            TransformData start = World.FindObject(_targets[i]).Get<TransformData>(); ValidateTransform(start);
            _inputs[i] = new(_targets[i], start, V3.Zero, 0);
        }
    }

    private void VerifyStamp()
    {
        Verify();
        if (_adapter is null || _authority is null || _faulted || !World.IsUpdating || World.Identity != _stamp.WorldId ||
            _play.SessionId != _stamp.SessionId || World.Tick != _stamp.FromTick || World.Revision != _stamp.Revision)
            throw new InvalidOperationException("Stale coupled step stamp.");
    }

    private static bool Finite(V3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static TransformData ValidateTransform(TransformData value)
    {
        _ = TransformData.Validate(value);
        if (!Finite(value.Position) || MathF.Abs(value.Position.X) > MaxPositionMagnitude ||
            MathF.Abs(value.Position.Y) > MaxPositionMagnitude || MathF.Abs(value.Position.Z) > MaxPositionMagnitude ||
            value.Scale.X <= 0 || value.Scale.Y <= 0 || value.Scale.Z <= 0 || MathF.Abs(value.Rotation.LengthSquared() - 1) > 1e-5f)
            throw new ArgumentException("Unsupported numerical transform: position, positive scale or unit rotation limits.");
        return TransformData.Validate(value);
    }

    private void Verify() { World.VerifyAccess(); ObjectDisposedException.ThrowIf(_disposed, this); }
    private bool SnapshotMatches => _snapshotValid && World.Identity == _worldId && World.Tick == _committedTick && _play.SessionId == _sessionId;
    public void Dispose()
    {
        World.VerifyAccess(); if (_disposed) return;
        if (_adapter is not null || _authority is not null || _play.State != PlayState.Stopped)
            throw new InvalidOperationException("Stop Play successfully before releasing its movement coordinator.");
        _play.DetachCoupledParticipant(this); _disposed = true;
    }
}
