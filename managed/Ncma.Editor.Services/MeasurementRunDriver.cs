using System.Runtime.ExceptionServices;
using Ncma.Application;

namespace Ncma.Editor.Services;

public enum MeasurementRunState { Created, Running, Rejected, Closed, Retained }
public enum MeasurementRunEnd { None, Frames, Duration, Requested, Platform, Fault }
public sealed record MeasurementRunStatus(MeasurementRunState State, MeasurementRunEnd End, ulong CompletedFrames,
    bool InputsCheckedBeforeRun, bool InputsCheckedAfterRun, bool ApplicationRetained, bool InputsRetained,
    int CloseAttempts, string? FailureType)
{
    public bool ExecutedProductVerified => false;
    public bool WorkloadApproved => false;
    public bool PerformanceAccepted => false;
}

// Explicit trusted-local operation over an ALREADY STARTED host. No startup, model send/approval,
// scene loader, process launch, recovery, file output or alternate frame loop. RunAndClose takes
// close responsibility only after input/host preflight. A preflight rejection leaves ownership outside.
public sealed class MeasurementRunDriver
{
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly MeasurementInputsPlan plan;
    private readonly MeasurementInputLease inputs;
    private readonly ApplicationRunWindow window;
    private ApplicationLifetime? application;
    private MeasurementRunState state;
    private MeasurementRunEnd end;
    private bool before, after, inputsOwned;
    private ulong frames;
    private int closeAttempts;
    private string? failureType;
    private byte[]? processEvidence;
    public MeasurementRunDriver(MeasurementInputsPlan plan, MeasurementInputLease inputs)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(inputs);
        this.plan = plan; this.inputs = inputs; window = new(plan.DurationMilliseconds);
    }
    private void Owner()
    { if (Environment.CurrentManagedThreadId != owner) throw new InvalidOperationException("measurement_driver_owner"); }
    public MeasurementRunStatus Status
    { get { Owner(); return new(state,end,frames,before,after,application is not null,inputsOwned,closeAttempts,failureType); } }
    // Return a detached, validated report; diagnostics never retry cleanup or return executable authority.
    public ProcessMeasurementReport? ProcessReport
    { get { Owner(); return processEvidence is null ? null : ProcessMeasurementEvidence.DecodeExpected(processEvidence,plan.Identity); } }
    public void RequestStop()
    {
        Owner(); if (state is not (MeasurementRunState.Created or MeasurementRunState.Running)) throw new InvalidOperationException("measurement_driver_not_active");
        window.RequestStop();
    }
    public MeasurementRunStatus RunAndClose(ApplicationLifetime host, IFrameClock clock, IFramePipeline pipeline,
        LoopCostCapture? costs = null, FramePacer? pacer = null)
    {
        Owner(); if (state != MeasurementRunState.Created) throw new InvalidOperationException("measurement_driver_single_use");
        state = MeasurementRunState.Rejected;
        ArgumentNullException.ThrowIfNull(host); ArgumentNullException.ThrowIfNull(clock); ArgumentNullException.ThrowIfNull(pipeline);
        if (host.State != ApplicationState.Running || costs is not null && costs.FrameRateLimit != pacer?.FramesPerSecond)
            throw new InvalidOperationException("measurement_driver_host_not_ready");
        // Original owner AND callback-reentry guard, before acquiring any close responsibility.
        _ = host.CanRecoverPresentation;
        var receipt = inputs.RequireCurrent();
        if (receipt.PlanFingerprint != plan.Fingerprint || receipt.Identity != plan.Identity)
            throw new InvalidOperationException("measurement_driver_plan_mismatch");
        before = true;
        var measurement = new ProcessMeasurement(plan.Identity,TimeSpan.FromMilliseconds(plan.IntervalMilliseconds),
            TimeSpan.FromMilliseconds(plan.DurationMilliseconds),plan.Capacity);
        application = host; inputsOwned = true; state = MeasurementRunState.Running;
        var errors = new List<Exception>();
        try {
            frames = host.Run(clock,pipeline,plan.MaximumFrames,costs,pacer,measurement,window);
            end = window.Stop switch { ApplicationRunStop.Duration => MeasurementRunEnd.Duration,
                ApplicationRunStop.Requested => MeasurementRunEnd.Requested,
                _ => frames == plan.MaximumFrames ? MeasurementRunEnd.Frames : MeasurementRunEnd.Platform };
        } catch (Exception error) { end = MeasurementRunEnd.Fault; errors.Add(error); }
        finally {
            // Seal and revalidation are outside the timed loop and before shutdown, with pins retained.
            // Failed partial frames remain in ProcessReport; CompletedFrames never fabricates success.
            try {
                var report = measurement.Seal();
                frames = checked((ulong)report.CompletedIterations);
                processEvidence = ProcessMeasurementEvidence.Encode(report);
            }
            catch (Exception error) { errors.Add(error); }
            try { inputs.RequireCurrent(); after = true; } catch (Exception error) { errors.Add(error); }
            Close(errors);
        }
        Throw(errors); return Status;
    }
    private void Close(List<Exception> errors)
    {
        closeAttempts++;
        if (application is not null) {
            try { application.Dispose(); application = null; }
            catch (Exception error) { errors.Add(error); }
        }
        // Never release input pins underneath a failed/retained service.
        if (application is null && inputsOwned) {
            try { inputs.Dispose(); inputsOwned = false; } catch (Exception error) { errors.Add(error); }
        }
        state = application is null && !inputsOwned ? MeasurementRunState.Closed : MeasurementRunState.Retained;
    }
    public MeasurementRunStatus RetryClose()
    {
        Owner(); if (state != MeasurementRunState.Retained) throw new InvalidOperationException("measurement_driver_no_retained_close");
        state = MeasurementRunState.Running; // Forbid nested retry from a service close callback.
        var errors = new List<Exception>(); Close(errors); Throw(errors); return Status;
    }
    private void Throw(List<Exception> errors)
    {
        if (errors.Count == 0) return;
        failureType ??= errors[0].GetType().Name; // No raw error messages/paths in diagnostics.
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        throw new AggregateException(errors);
    }
}
