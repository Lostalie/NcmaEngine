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
public enum ApplicationFramePhase { Clock, Platform, EditorIntents, Agent, BeginPresentation, Input, Gameplay, Present }
public readonly record struct ApplicationFrameFailure(Guid Identity, ulong FrameId, ApplicationFramePhase Phase, string PipelineId);
// Trusted host recovery operation, not a serialized command or permission grant.
// Implementations must retire failed presentation owners and attest readiness before returning.
public interface IApplicationPresentationRecovery
{
    IFramePipeline Pipeline { get; }
    void Restore();
    bool IsReady { get; }
}
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

// Synchronous owner-thread loop. Startup order is dependency order; close in reverse
// and STOP at the first failure so no dependency is released under a retained service.
// BeginPresentation computes current-frame capture before game input.
public sealed class ApplicationLifetime : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly List<IApplicationService> _owned = [];
    private bool _busy,_recovering,_recoveryRejected,_shutdownStarted;
    private IFramePipeline? _failedPipeline;
    public ApplicationState State { get; private set; }
    public ApplicationFailure? Failure { get; private set; }
    public ApplicationFailure? ShutdownFailure { get; private set; }
    public ApplicationFailure? RecoveryFailure { get; private set; }
    public ApplicationFrameFailure? FrameFailure { get; private set; }
    public ulong LastFrameId { get; private set; }
    public uint RecoveryAttempts { get; private set; }
    public bool CanRecoverPresentation {get{Verify();return Recoverable();}}
    public uint ShutdownAttempts { get; private set; }
    public string? RetainedServiceId { get; private set; }
    public int RetainedServiceCount { get { VerifyThread(); return _owned.Count; } }
    private void VerifyThread()
    { if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Application requires its owner thread."); }
    private static string ServiceIdentity(object service)
    { string id=service.GetType().FullName??"service";return id.Length<=256?id:id[..256]; }
    private void Verify()
    {
        VerifyThread();
        if (_busy) { if(_recovering)_recoveryRejected=true;throw new InvalidOperationException("Application callback reentry is forbidden."); }
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
            foreach (var service in startup) { serviceId = ServiceIdentity(service); _owned.Add(service); service.Start(); }
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
    public ulong Run(IFrameClock clock, IFramePipeline pipeline, ulong maxFrames, LoopCostCapture? costs = null, FramePacer? pacer = null)
    {
        Verify();
        if (State != ApplicationState.Running || maxFrames == 0) throw new InvalidOperationException("Running application and positive frame limit required.");
        ArgumentNullException.ThrowIfNull(clock); ArgumentNullException.ThrowIfNull(pipeline);
        if(costs is not null&&costs.FrameRateLimit!=pacer?.FramesPerSecond)throw new ArgumentException("Pacing observation configuration mismatch.");
        _busy = true;
        var phase=ApplicationFramePhase.Clock;
        try
        {
            double previous = clock.Seconds;
            if (!double.IsFinite(previous) || previous < 0) throw new ArgumentException("Invalid clock.");
            ulong count = 0;
            while (count < maxFrames)
            {
                costs?.Begin();
                var outcome=LoopCostOutcome.Faulted;
                ulong observedFrame=0;
                try {
                phase=ApplicationFramePhase.Clock;
                if(pacer is not null){costs?.Phase(LoopCostPhase.Pacing);pacer.Wait();costs?.PacingAdmitted(pacer.LastAdmissionTimestamp!.Value);}
                costs?.Phase(LoopCostPhase.Platform);
                phase=ApplicationFramePhase.Platform;if(!pipeline.PumpPlatform()){outcome=LoopCostOutcome.Stopped;break;}
                costs?.Phase(LoopCostPhase.Control);
                phase=ApplicationFramePhase.Clock;
                double now = clock.Seconds, delta = now - previous;
                if (!double.IsFinite(now) || !double.IsFinite(delta) || delta < 0) throw new ArgumentException("Clock must be finite and monotonic.");
                previous = now;
                var frame = new FrameContext(LastFrameId=checked(LastFrameId+1), delta);count++;
                observedFrame=frame.FrameId;
                costs?.Phase(LoopCostPhase.Intents);
                phase=ApplicationFramePhase.EditorIntents;pipeline.ApplyEditorIntents(frame);
                costs?.Phase(LoopCostPhase.Agent);
                phase=ApplicationFramePhase.Agent;pipeline.PumpAgent(frame);
                costs?.Phase(LoopCostPhase.BeginPresentation);
                phase=ApplicationFramePhase.BeginPresentation;pipeline.BeginPresentation(frame);
                costs?.Phase(LoopCostPhase.Input);
                phase=ApplicationFramePhase.Input;pipeline.SubmitInput(frame);
                costs?.Phase(LoopCostPhase.Simulation);
                phase=ApplicationFramePhase.Gameplay;pipeline.AdvancePlay(frame);
                costs?.Phase(LoopCostPhase.Presentation);
                phase=ApplicationFramePhase.Present;pipeline.Present(frame);
                outcome=LoopCostOutcome.Completed;
                } finally { costs?.End(observedFrame,outcome); }
            }
            return count;
        }
        catch { State = ApplicationState.Failed;
            FrameFailure=new(Guid.NewGuid(),LastFrameId,phase,ServiceIdentity(pipeline));_failedPipeline=pipeline;
            Failure ??= new("application_frame_failed", "frame", ServiceIdentity(pipeline), FrameFailure.Value.Identity, "Application frame failed."); throw; }
        finally { _busy = false; }
    }
    private bool Recoverable()=>State==ApplicationState.Failed&&!_shutdownStarted&&_failedPipeline is not null&&
        _owned.Any(s=>ReferenceEquals(s,_failedPipeline))&&FrameFailure is{Phase:ApplicationFramePhase.Present or ApplicationFramePhase.BeginPresentation};
    public void RecoverPresentation(ApplicationFrameFailure expected,IApplicationPresentationRecovery recovery)
    {
        Verify();ArgumentNullException.ThrowIfNull(recovery);
        if(!Recoverable()||FrameFailure!=expected)throw new InvalidOperationException("Exact retained presentation failure required; shutdown and non-presentation faults cannot resume.");
        RecoveryAttempts=checked(RecoveryAttempts+1);_busy=_recovering=true;_recoveryRejected=false;
        try {
            if(!ReferenceEquals(recovery.Pipeline,_failedPipeline)||_recoveryRejected)throw new InvalidOperationException("Foreign or reentrant presentation recovery.");
            recovery.Restore();
            if(_recoveryRejected||!recovery.IsReady||!ReferenceEquals(recovery.Pipeline,_failedPipeline)||_recoveryRejected)
                throw new InvalidOperationException("Presentation recovery did not establish a nonreentrant ready owner.");
            // No service restart or frame replay. Run establishes a new wall-clock baseline.
            _failedPipeline=null;FrameFailure=null;State=ApplicationState.Running;
        }catch{
            RecoveryFailure??=new("application_presentation_recovery_failed","recovery",expected.PipelineId,expected.Identity,"Presentation recovery failed; explicit review and retry or shutdown required.");
            throw;
        }finally{_busy=_recovering=false;}
    }
    private List<Exception> Release()
    {
        if (_owned.Count == 0) return [];
        ShutdownAttempts=checked(ShutdownAttempts+1);
        while (_owned.Count != 0) {
            var service=_owned[^1];
            try { service.Dispose(); }
            catch (Exception error) {
                RetainedServiceId=ServiceIdentity(service);
                ShutdownFailure??=new("application_shutdown_failed","shutdown",RetainedServiceId,
                    Failure?.Correlation??Guid.NewGuid(),"Application shutdown failed; resources retained for explicit close retry.");
                Failure??=ShutdownFailure;
                // Explicit Add initializer keeps the compiler from introducing
                // a CollectionsMarshal dependency into this headless foundation.
                return new List<Exception>{error}; // Never store callback exceptions as diagnostics.
            }
            _owned.RemoveAt(_owned.Count-1); // Clear ownership only after successful release.
        }
        RetainedServiceId=null;_failedPipeline=null;return [];
    }
    public void Dispose()
    {
        Verify();
        if (State == ApplicationState.Stopped) return;
        _shutdownStarted=true;
        bool failed = State == ApplicationState.Failed;
        State = ApplicationState.Stopping; _busy = true;
        List<Exception> errors;
        try { errors=Release(); } finally { _busy=false; }
        State = failed || errors.Count != 0 ? ApplicationState.Failed : ApplicationState.Stopped;
        if (errors.Count != 0) {
            throw new AggregateException(errors);
        }
    }
}
