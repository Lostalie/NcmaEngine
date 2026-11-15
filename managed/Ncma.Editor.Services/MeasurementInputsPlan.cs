using Ncma.Application;

namespace Ncma.Editor.Services;

// Explicit trusted-local preparation data. A plan never grants launch/model/approval authority.
public sealed class MeasurementInputsPlan
{
    public ProcessMeasurementIdentity Identity { get; }
    public int DurationMilliseconds { get; }
    public int IntervalMilliseconds { get; }
    public int Capacity { get; }
    public ulong MaximumFrames { get; }
    public string Fingerprint { get; }
    public MeasurementInputsPlan(ProcessMeasurementIdentity identity, int durationMilliseconds, int intervalMilliseconds,
        int capacity, ulong maximumFrames)
    {
        ArgumentNullException.ThrowIfNull(identity);
        foreach (string hash in new[] { identity.SourceSha256, identity.CaptureManifestSha256, identity.ProductManifestSha256, identity.WorkloadSha256 }) WorkflowEvidenceRules.Hash(hash);
        if (identity.RunId == Guid.Empty || !Enum.IsDefined(identity.Configuration) || !Enum.IsDefined(identity.Variant) || identity.Host != MeasurementHost.Editor ||
            intervalMilliseconds is < 100 or > 60000 || durationMilliseconds < intervalMilliseconds || durationMilliseconds > 3600000 ||
            capacity is < 1 or > ProcessSeriesCapture.MaximumCapacity ||
            capacity < (durationMilliseconds + intervalMilliseconds - 1) / intervalMilliseconds || maximumFrames is < 1 or > 1000000)
            throw new ArgumentException("measurement_inputs_plan");
        Identity = identity; DurationMilliseconds = durationMilliseconds; IntervalMilliseconds = intervalMilliseconds;
        Capacity = capacity; MaximumFrames = maximumFrames;
        Fingerprint = WorkflowEvidenceRules.Digest(new { version = 1, identity, durationMilliseconds, intervalMilliseconds, capacity, maximumFrames });
    }
}
public enum MeasurementInputsState { Created, Ready, Faulted, Closed }
public enum MeasurementScenario { EditorIdle, OpenWorldExploration, ControlledFixture }
public sealed record MeasurementInputsReceipt(string PlanFingerprint, ProcessMeasurementIdentity Identity, ulong CheckSequence,
    MeasurementScenario Scenario, int ProductFiles, int WorkloadFiles, long PayloadBytes, string ProductFilesSha256, string WorkloadFilesSha256)
{
    public bool SourceContentsVerified => false;
    public bool ExecutedProductVerified => false;
    public bool LaunchAuthorized => false;
    public bool WorkloadApproved => false;
    public bool PerformanceAccepted => false;
    public string Scope => "declared product/workload bytes and manifests checked while readonly handles retained; exact tree at check points, not exclusion of later additions; capture manifest only, not source files/runtime toolchain/loaded process or approval";
}
