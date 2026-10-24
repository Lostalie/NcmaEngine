using System.Diagnostics;

namespace Ncma.Gameplay;

public enum SimulationAdvanceKind { Frame, FixedStep, PausedStep }
public readonly record struct SimulationCostReport(Guid SessionId, Guid WorldId, ulong AdvanceSequence,
    ulong SourceFrameCount, SimulationAdvanceKind Kind, PlayState State, ulong TickBefore, ulong TickAfter,
    bool Completed, bool IncludesUpdate, double RequestedDeltaSeconds, double CpuWallMilliseconds,
    long OwnerThreadAllocatedBytes)
{
    public int Version => 1;
    public string Scope => "one Play advance call; includes its fixed quanta and optional OnUpdate; excludes input preparation/render/startup and observer setup";
    public ulong CommittedSteps => TickAfter >= TickBefore ? TickAfter - TickBefore : 0;
    public bool FullPerformanceAccepted => false;
}

public sealed partial class PlaySession
{
    private bool _simulationCostsEnabled;
    private bool _simulationUpdateEntered;
    private ulong _simulationCostSequence;
    private SimulationCostReport? _lastSimulationCosts;
    public SimulationCostReport? LastSimulationCosts { get { Verify(); return _lastSimulationCosts; } }

    // Explicit trusted host opt-in before Start. No remote capability, callback or extra runner.
    public void EnableSimulationCosts()
    {
        Control();
        if (State != PlayState.Stopped) throw new InvalidOperationException("Enable simulation costs before Start.");
        _simulationCostsEnabled = true;
    }

    private void ResetSimulationCosts() { _lastSimulationCosts = null; _simulationCostSequence = 0; }
    private SimulationCostScope BeginSimulationCosts(SimulationAdvanceKind kind, double delta) =>
        _simulationCostsEnabled ? new(this, kind, delta) : default;

    // Value scope: no delegate/boxing/list, no clock or allocation reads on the disabled path.
    private readonly struct SimulationCostScope : IDisposable
    {
        private readonly PlaySession? _owner;
        private readonly Guid _session, _world;
        private readonly ulong _tick, _sequence;
        private readonly SimulationAdvanceKind _kind;
        private readonly double _delta;
        private readonly long _time, _allocated;
        internal SimulationCostScope(PlaySession owner, SimulationAdvanceKind kind, double delta)
        {
            _owner = owner; _session = owner.SessionId; _world = owner._scene.World.Identity;
            _tick = owner._scene.World.Tick; _sequence = checked(++owner._simulationCostSequence);
            _kind = kind; _delta = delta; owner._simulationUpdateEntered = false;
            _allocated = GC.GetAllocatedBytesForCurrentThread(); _time = Stopwatch.GetTimestamp();
        }
        public void Dispose()
        {
            if (_owner is not { } owner) return;
            double elapsed = Stopwatch.GetElapsedTime(_time).TotalMilliseconds;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - _allocated;
            ulong tick = owner._scene.World.Tick;
            bool completed = owner.SessionId == _session && owner._scene.World.Identity == _world && tick >= _tick &&
                owner.State is PlayState.Running or PlayState.Paused;
            owner._lastSimulationCosts = new(_session, _world, _sequence, owner._frameCount, _kind, owner.State, _tick,
                tick, completed, owner._simulationUpdateEntered, _delta, elapsed, allocated);
        }
    }
}
