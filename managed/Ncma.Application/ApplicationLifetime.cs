using System.Diagnostics;
namespace Ncma.Application;

public sealed record ApplicationFailure(string Code, string Phase, string ServiceId, Guid Correlation, string Message);
public enum ApplicationState { Created, Starting, Running, Stopping, Stopped, Failed }
public interface IApplicationService : IDisposable { void Start(); }
public interface IFrameClock { double Seconds { get; } }
public sealed class MonotonicClock : IFrameClock
{
    private readonly long _origin = Stopwatch.GetTimestamp();
    public double Seconds => Stopwatch.GetElapsedTime(_origin).TotalSeconds;
}
public readonly record struct FrameContext(ulong FrameId, double DeltaSeconds);
public interface IFramePipeline
{
    bool PumpPlatform();
    void ApplyEditorIntents(FrameContext frame);
    void PumpAgent(FrameContext frame);
    void BeginPresentation(FrameContext frame);
    void SubmitInput(FrameContext frame);
    void AdvancePlay(FrameContext frame);
    void Present(FrameContext frame);
}

// Synchronous owner-thread loop. BeginPresentation computes current-frame capture before game input.
public sealed class ApplicationLifetime : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly List<IApplicationService> _owned = [];
    private bool _busy;
    public ApplicationState State { get; private set; }
    public ApplicationFailure? Failure { get; private set; }
    private void Verify()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Application requires its owner thread.");
        if (_busy) throw new InvalidOperationException("Application callback reentry is forbidden.");
    }
    public void Start(IReadOnlyList<IApplicationService> services)
    {
        Verify();
        if (State != ApplicationState.Created) throw new InvalidOperationException("Application already started.");
        ArgumentNullException.ThrowIfNull(services);
        if (services.Count > 64) throw new ArgumentException("Application service budget exceeded.");
        var startup = services.ToArray();
        if (startup.Any(s => s is null) || startup.Distinct(ReferenceEqualityComparer.Instance).Count() != startup.Length)
            throw new ArgumentException("Services must be non-null and uniquely owned.");
        _busy = true; State = ApplicationState.Starting;
        string serviceId = "application";
        try
        {
            foreach (var service in startup) { serviceId = service.GetType().FullName ?? "service"; _owned.Add(service); service.Start(); }
            State = ApplicationState.Running;
        }
        catch (Exception startError)
        {
            Failure = new("application_initialize_failed", "initialize", serviceId, Guid.NewGuid(), "Application service initialization failed.");
            var cleanup = Release(); State = ApplicationState.Failed;
            if (cleanup.Count != 0) throw new AggregateException(new[] { startError }.Concat(cleanup));
            throw;
        }
        finally { _busy = false; }
    }
    public ulong Run(IFrameClock clock, IFramePipeline pipeline, ulong maxFrames)
    {
        Verify();
        if (State != ApplicationState.Running || maxFrames == 0) throw new InvalidOperationException("Running application and positive frame limit required.");
        ArgumentNullException.ThrowIfNull(clock); ArgumentNullException.ThrowIfNull(pipeline);
        _busy = true;
        try
        {
            double previous = clock.Seconds;
            if (!double.IsFinite(previous) || previous < 0) throw new ArgumentException("Invalid clock.");
            ulong count = 0;
            while (count < maxFrames && pipeline.PumpPlatform())
            {
                double now = clock.Seconds, delta = now - previous;
                if (!double.IsFinite(now) || !double.IsFinite(delta) || delta < 0) throw new ArgumentException("Clock must be finite and monotonic.");
                previous = now;
                var frame = new FrameContext(++count, delta);
                pipeline.ApplyEditorIntents(frame); pipeline.PumpAgent(frame);
                pipeline.BeginPresentation(frame); pipeline.SubmitInput(frame);
                pipeline.AdvancePlay(frame); pipeline.Present(frame);
            }
            return count;
        }
        catch { State = ApplicationState.Failed;
            Failure = new("application_frame_failed", "frame", pipeline.GetType().FullName ?? "pipeline", Guid.NewGuid(), "Application frame failed."); throw; }
        finally { _busy = false; }
    }
    private List<Exception> Release()
    {
        var errors = new List<Exception>();
        for (int i = _owned.Count - 1; i >= 0; i--)
            try { _owned[i].Dispose(); } catch (Exception e) { errors.Add(e); }
        _owned.Clear(); return errors;
    }
    public void Dispose()
    {
        Verify();
        if (State == ApplicationState.Stopped) return;
        bool failed = State == ApplicationState.Failed;
        State = ApplicationState.Stopping; _busy = true;
        var errors = Release(); _busy = false;
        State = failed || errors.Count != 0 ? ApplicationState.Failed : ApplicationState.Stopped;
        if (errors.Count != 0) {
            Failure = new("application_shutdown_failed", "shutdown", "application", Guid.NewGuid(), "Application shutdown failed; resources may be retained.");
            throw new AggregateException(errors);
        }
    }
}
