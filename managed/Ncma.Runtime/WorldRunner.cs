namespace Ncma.Runtime;

public interface IWorldSystem
{
    void FixedUpdate(World world, double fixedDeltaSeconds);
}

// Serial deterministic registration order. No native/AI/IPC dependency.
public sealed class WorldRunner
{
    private readonly List<IWorldSystem> _systems = new();
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
    }
    public World World { get; }
    public double FixedDeltaSeconds { get; }
    public int MaxStepsPerAdvance { get; }
    public bool IsFaulted { get; private set; }
    public string? LastError { get; private set; }

    public void AddSystem(IWorldSystem system)
    {
        World.VerifyAccess();
        ArgumentNullException.ThrowIfNull(system);
        if (_advancing) throw new InvalidOperationException("Cannot change scheduling during an update.");
        if (_systems.Contains(system)) throw new ArgumentException("System already registered.");
        _systems.Add(system);
    }
    public int Advance(double deltaSeconds)
    {
        World.VerifyAccess();
        if (_advancing || IsFaulted) throw new InvalidOperationException("Runner is updating or faulted.");
        double total = _accumulator + deltaSeconds;
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0 || !double.IsFinite(total) ||
            total > FixedDeltaSeconds * MaxStepsPerAdvance + 1e-12)
            throw new ArgumentException("Advance exceeds the bounded catch-up budget; time is not silently dropped.");
        _accumulator = total;
        int steps = 0;
        _advancing = true;
        try
        {
            while (_accumulator + 1e-12 >= FixedDeltaSeconds)
            {
                World.FixedStep(() => { foreach (var system in _systems) system.FixedUpdate(World, FixedDeltaSeconds); });
                _accumulator = Math.Max(0, _accumulator - FixedDeltaSeconds);
                ++steps;
            }
            return steps;
        }
        catch (Exception exception)
        {
            IsFaulted = true;
            LastError = exception.Message;
            throw;
        }
        finally { _advancing = false; }
    }

    // Explicit recovery: component writes were aborted, but private System state/IO were not.
    public void ResetFault()
    {
        World.VerifyAccess();
        if (_advancing) throw new InvalidOperationException("Cannot reset during an update.");
        _accumulator = 0;
        IsFaulted = false;
        LastError = null;
    }
}
