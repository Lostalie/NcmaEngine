namespace Ncma.Application;

public enum MeasurementConfiguration { Debug, Release }
public enum MeasurementVariant { Default, Ai, Extensions, AiExtensions }
public enum MeasurementHost { Editor, Player, Headless }
// Host-supplied labels, NOT disk freshness, signatures, workload execution or installation approval.
public sealed record ProcessMeasurementIdentity(Guid RunId, string SourceSha256, string CaptureManifestSha256,
    string ProductManifestSha256, string WorkloadSha256, MeasurementConfiguration Configuration,
    MeasurementVariant Variant, MeasurementHost Host)
{
    internal void Validate()
    {
        static bool Hash(string value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
        if (RunId == Guid.Empty || !Hash(SourceSha256) || !Hash(CaptureManifestSha256) || !Hash(ProductManifestSha256) ||
            !Hash(WorkloadSha256) || !Enum.IsDefined(Configuration) || !Enum.IsDefined(Variant) || !Enum.IsDefined(Host))
            throw new ArgumentException("process_measurement_identity");
    }
}
public sealed record ProcessMeasurementReport(int Version, ProcessMeasurementIdentity Identity, ProcessSeriesReport Series,
    bool BoundToLoop, long Iterations, long CompletedIterations, long StoppedIterations, long FaultedIterations, long ObserverFailures)
{
    public bool PerformanceAccepted => false;
    public bool MemoryAccepted => false;
    public string Scope => "trusted host supplied labels, not filesystem freshness or provenance; samples after attempted loop iterations and outside LoopCostCapture; no startup/shutdown/independent background coverage; no business stop or approval authority";
}

// Optional trusted-host observer. It never owns the application, pipeline, callbacks or shutdown.
public sealed class ProcessMeasurement
{
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly ProcessMeasurementIdentity identity;
    private readonly ProcessSeriesCapture series;
    private WeakReference<ApplicationLifetime>? lifetime;
    private WeakReference<IFramePipeline>? pipeline;
    private bool running, sealedReport;
    private long iterations, completed, stopped, faulted, failures;
    public ProcessMeasurement(ProcessMeasurementIdentity identity, TimeSpan interval, TimeSpan duration, int capacity = 4096)
        : this(identity, interval, duration, capacity, TimeProvider.System, ProcessSeriesCounters.ReadCurrent) { }
    internal ProcessMeasurement(ProcessMeasurementIdentity identity, TimeSpan interval, TimeSpan duration, int capacity,
        TimeProvider clock, Func<ProcessSeriesCounters> read)
    {
        ArgumentNullException.ThrowIfNull(identity); identity.Validate(); this.identity = identity;
        series = new(identity.RunId, interval, duration, capacity, clock, read);
    }
    private void Verify()
    { if (Environment.CurrentManagedThreadId != owner || sealedReport) throw new InvalidOperationException("process_measurement_owner_or_closed"); }
    internal void BeginRun(ApplicationLifetime application, IFramePipeline frames)
    {
        Verify();
        if (running || lifetime is not null && (!lifetime.TryGetTarget(out var original) || !ReferenceEquals(original, application) ||
            pipeline is null || !pipeline.TryGetTarget(out var originalFrames) || !ReferenceEquals(originalFrames, frames)))
            throw new InvalidOperationException("process_measurement_loop_identity");
        lifetime ??= new(application); pipeline ??= new(frames); running = true;
    }
    internal void EndRun() => running = false;
    internal void Observe(LoopCostOutcome outcome)
    {
        // Called by the owning lifetime while its original reentry guard is held.
        iterations++;
        switch (outcome) {
            case LoopCostOutcome.Completed: completed++; break;
            case LoopCostOutcome.Stopped: stopped++; break;
            default: faulted++; break;
        }
        if (failures != 0) return;
        try { series.Poll(); }
        catch (Exception e) when (e is not OutOfMemoryException) { failures++; } // No observer exception may replace business failure.
    }
    public ProcessMeasurementReport Seal()
    {
        Verify(); if (running) throw new InvalidOperationException("process_measurement_running");
        var report = series.Seal(); sealedReport = true;
        bool bound = lifetime is not null;
        lifetime = null; pipeline = null; // Observation must not retain an already released host.
        return new(1, identity, report, bound, iterations, completed, stopped, faulted, failures);
    }
}
