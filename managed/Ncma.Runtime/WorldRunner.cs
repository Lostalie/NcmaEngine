namespace Ncma.Runtime;

public interface IWorldSystem
{
    void FixedUpdate(World world, double fixedDeltaSeconds);
}

// Serial deterministic registration order. No native/AI/IPC dependency.
public sealed class WorldRunner
{
    private List<IWorldSystem> _systems = new();
    private readonly Action<Action> _executeStep;
    private double _accumulator;
    private bool _advancing;
    public WorldRunner(World world, double fixedDeltaSeconds = 1.0 / 60.0, int maxStepsPerAdvance = 8)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!double.IsFinite(fixedDeltaSeconds) || fixedDeltaSeconds < 0.001 || fixedDeltaSeconds > 1 ||
            maxStepsPerAdvance < 1 || maxStepsPerAdvance > 128) throw new ArgumentException("Invalid fixed-step budget.");
        World = world;
        FixedDeltaSeconds = fixedDeltaSeconds;
        MaxStepsPerAdvance = maxStepsPerAdvance;
        _executeStep = world.FixedStep;
    }
    // Trusted gameplay facade may include its mailbox in the same phase. Native hosts cannot call it.
    internal WorldRunner(World world, double fixedDeltaSeconds, int maxStepsPerAdvance, Action<Action> executeStep)
        : this(world, fixedDeltaSeconds, maxStepsPerAdvance) => _executeStep = executeStep;
    public World World { get; }
    public double FixedDeltaSeconds { get; }
    public int MaxStepsPerAdvance { get; }
    public bool IsFaulted { get; private set; }
    public string? LastError { get; private set; }
    public double Accumulator { get { World.VerifyAccess(); return _accumulator; } }
    public double LastDroppedSeconds { get; private set; }
    public int LastStepsExecuted { get; private set; }

    public void AddSystem(IWorldSystem system)
    {
        World.VerifyAccess();
        ArgumentNullException.ThrowIfNull(system);
        if (_advancing || World.IsUpdating) throw new InvalidOperationException("Cannot change scheduling during an update.");
        if (_systems.Contains(system)) throw new ArgumentException("System already registered.");
        _systems.Add(system);
    }
    internal Action PrepareSystems(IEnumerable<IWorldSystem> systems)
    {
        World.VerifyAccess();
        if (_advancing || World.IsUpdating) throw new InvalidOperationException("Runner is updating.");
        var prepared = systems.ToList();
        return () => _systems = prepared;
    }
    // Strict callers retain the original preflight rejection: time is never silently dropped.
    public int Advance(double deltaSeconds) => AdvanceCore(deltaSeconds, false);
    public int AdvanceInteractive(double deltaSeconds) => AdvanceCore(deltaSeconds, true);
    private int AdvanceCore(double deltaSeconds, bool interactive)
    {
        World.VerifyAccess();
        if (_advancing || IsFaulted || World.IsUpdating) throw new InvalidOperationException("Runner is updating or faulted.");
        double accepted = interactive ? Math.Min(deltaSeconds, 0.25) : deltaSeconds;
        double total = _accumulator + accepted;
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0 || !double.IsFinite(total) ||
            (!interactive && total > FixedDeltaSeconds * MaxStepsPerAdvance + 1e-12))
            throw new ArgumentException("Advance exceeds the bounded catch-up budget or contains invalid time.");
        LastDroppedSeconds = deltaSeconds - accepted;
        LastStepsExecuted = 0;
        _accumulator = total;
        _advancing = true;
        try
        {
            while (_accumulator + 1e-12 >= FixedDeltaSeconds && LastStepsExecuted < MaxStepsPerAdvance)
            {
                _executeStep(() => { foreach (var system in _systems) system.FixedUpdate(World, FixedDeltaSeconds); });
                _accumulator = Math.Max(0, _accumulator - FixedDeltaSeconds);
                ++LastStepsExecuted;
            }
            if (interactive && _accumulator + 1e-12 >= FixedDeltaSeconds)
            {
                double debt = Math.Floor((_accumulator + 1e-12) / FixedDeltaSeconds) * FixedDeltaSeconds;
                LastDroppedSeconds += debt;
                _accumulator = Math.Max(0, _accumulator - debt);
            }
            return LastStepsExecuted;
        }
        catch (Exception exception)
        {
            IsFaulted = true;
            LastError = exception.Message;
            throw;
        }
        finally { _advancing = false; }
    }

    internal void ClearAccumulator()
    {
        World.VerifyAccess();
        if (_advancing || World.IsUpdating) throw new InvalidOperationException("Runner is updating.");
        _accumulator = 0;
    }
    // Explicit recovery: component writes were aborted, but private System state/IO were not.
    public void ResetFault()
    {
        ClearAccumulator();
        IsFaulted = false;
        LastError = null;
    }
}
