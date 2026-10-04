using System.Numerics;
using System.Text;
using Ncma.Interop;

namespace Ncma.Physics;

public enum PhysicsSimulationState { Paused, Running, Faulted, Disposed }
public sealed record PhysicsSimulationSettings(PhysicsDimension Dimension, Vector3 Gravity,
    int MaximumBodies = 4096, float FixedSeconds = 1f / 60, uint SubSteps = 4)
{
    public static PhysicsSimulationSettings TwoD => new(PhysicsDimension.Two, new(0, -9.81f, 0));
    public static PhysicsSimulationSettings ThreeD => new(PhysicsDimension.Three, new(0, -9.81f, 0), SubSteps: 1);
}
public readonly record struct PhysicsBodyReference(Guid ServiceId, Guid WorldId, ulong Handle);
public readonly record struct PhysicsVelocity2D(PhysicsBodyReference Body, Vector2 Velocity);
public readonly record struct PhysicsVelocity3D(PhysicsBodyReference Body, Vector3 Velocity);
public readonly record struct PhysicsStepProfile(ulong Samples, int WindowSamples, double MedianMilliseconds, double P95Milliseconds, double MaxMilliseconds);
public sealed record PhysicsWorldInspection(Guid ServiceId, Guid WorldId, PhysicsDimension Dimension,
    PhysicsSimulationState State, float FixedSeconds, int PendingVelocities, ulong PublishedSequence,
    bool SnapshotValid, bool NativeAvailable, PhysicsCounters Native, PhysicsStepProfile Profile);
public sealed record PhysicsServiceInspection(Guid ServiceId, bool Enabled, bool Closing, int Worlds,
    PhysicsCapabilities Capabilities, uint AbiMajor, uint AbiMinor, ulong DroppedDiagnostics);
public sealed record PhysicsServiceDiagnostic(Guid ServiceId, Guid WorldId, ulong Sequence, string Operation,
    string Code, string Message);

// High-level policy stays managed. This is an independent numerical service, NOT a
// Runtime.IWorldSystem: no automatic scene/Play dispatch or GameObject authority.
public sealed class PhysicsService : IDisposable
{
    public const int MaximumWorlds = 16, MaximumDiagnostics = 32;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly PhysicsModuleHost _host;
    private readonly List<PhysicsSimulation> _worlds = new(MaximumWorlds);
    private readonly Queue<PhysicsServiceDiagnostic> _diagnostics = new(MaximumDiagnostics);
    private bool _closing, _disposed;
    private ulong _dropped;
    public Guid ServiceId { get; } = Guid.NewGuid();
    public bool Enabled { get; }
    public PhysicsService(string pluginRoot, bool enabled = true)
    {
        Enabled = enabled;
        _host = new(pluginRoot, enabled);
    }
    internal void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Physics service requires its owner thread.");
    }
    internal void Verify()
    {
        VerifyThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_closing) throw new InvalidOperationException("Physics service is closing; only inspect/dispose allowed.");
    }
    public PhysicsSimulation CreateWorld(PhysicsSimulationSettings settings)
    {
        Verify(); ArgumentNullException.ThrowIfNull(settings);
        if (!Enabled) throw new InvalidOperationException("Physics is disabled.");
        if (_worlds.Count >= MaximumWorlds) throw new InvalidOperationException("Physics service world budget exceeded.");
        if (settings.MaximumBodies is < 1 or > PhysicsWorld.MaximumBatch ||
            !float.IsFinite(settings.FixedSeconds) || settings.FixedSeconds is <= 0 or > .25f ||
            settings.Dimension is not (PhysicsDimension.Two or PhysicsDimension.Three) ||
            settings.Dimension == PhysicsDimension.Two && settings.Gravity.Z != 0 ||
            settings.Dimension == PhysicsDimension.Three && settings.SubSteps != 1)
            throw new ArgumentException("Invalid independent physics simulation settings.");
        Guid id = Guid.NewGuid();
        try
        {
            var simulation = new PhysicsSimulation(this, id, settings, _host.Module!);
            _worlds.Add(simulation);
            return simulation;
        }
        catch (Exception e) { Record(id, 0, "physics.create_world", e); throw; }
    }
    internal void Remove(PhysicsSimulation world) => _worlds.Remove(world);
    internal void Record(Guid world, ulong sequence, string operation, Exception error)
    {
        if (_diagnostics.Count == MaximumDiagnostics) { _diagnostics.Dequeue(); _dropped++; }
        var builder = new StringBuilder(); int bytes = 0;
        foreach (var rune in error.Message.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > 512) break;
            builder.Append(rune.ToString()); bytes += rune.Utf8SequenceLength;
        }
        string code = error is PluginException native ? "native." + native.Result : "managed." + error.GetType().Name;
        _diagnostics.Enqueue(new(ServiceId, world, sequence, operation, code, builder.ToString()));
    }
    public PhysicsServiceInspection Inspect()
    {
        VerifyThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        var module = _host.Module;
        return new(ServiceId, Enabled, _closing, _worlds.Count,
            module is null ? 0 : (PhysicsCapabilities)module.Capabilities, module?.AbiMajor ?? 0, module?.AbiMinor ?? 0, _dropped);
    }
    public PhysicsServiceDiagnostic[] CopyDiagnostics()
    {
        VerifyThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        return _diagnostics.ToArray();
    }
    public void Dispose()
    {
        VerifyThread(); if (_disposed) return;
        _closing = true;
        var failures = new List<Exception>();
        // Failed closes remain owned and leased for explicit retry. No finalizer or force unload.
        for (int i = _worlds.Count - 1; i >= 0; --i)
            try { _worlds[i].Dispose(); } catch (Exception e) { failures.Add(e); }
        if (failures.Count != 0) throw new AggregateException(failures);
        _host.Dispose(); _disposed = true;
    }
}

internal sealed class PhysicsSampleWindow
{
    private readonly double[] _samples = new double[256], _sorted = new double[256];
    private ulong _count;
    private double _maximum;
    public void Add(double milliseconds)
    {
        _samples[(int)(_count++ % 256)] = milliseconds;
        _maximum = Math.Max(_maximum, milliseconds);
    }
    public PhysicsStepProfile Inspect()
    {
        int count = (int)Math.Min(_count, 256);
        if (count == 0) return default;
        Array.Copy(_samples, _sorted, count);
        Array.Sort(_sorted, 0, count);
        return new(_count, count, _sorted[(count - 1) / 2], _sorted[(95 * count + 99) / 100 - 1], _maximum);
    }
}
