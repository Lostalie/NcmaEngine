namespace Ncma.Modules;

public enum ObservationConfiguration { Debug, Release }
public enum ObservationVariant { Default, Ai, Extensions, AiExtensions, LocalWorker }
public enum ObservationScope { SuppliedEvents, ModuleSession }
public sealed record ObservationIdentity(Guid RunId, string SourceSha256, string WorkloadSha256,
    ObservationConfiguration Configuration, ObservationVariant Variant, long TimestampFrequency);
public readonly record struct ObservationRequest(Guid SessionId, Guid RequestId);
public enum ObservationKind { Carried, Admitted, AdmissionRejected, Dispatched, Terminal, ReviewReady, CommitStarted, Committed, CancelRequested, Drained, Transport }
public enum ObservationOutcome { Pending, Proposal, Rejected, Failed, Cancelled, TimedOut, Stale }
public enum ObservationLatency { Queue, AdmissionToTerminal, TerminalToReview, Commit, CancelToDrain, Adapter }

// Numeric detached evidence ONLY. No prompt/body/key/path/code/message, callback, clock or authority.
// Timestamp means the caller's owner-ordered observation, not an inferred remote/server clock.
public readonly record struct ModuleObservation(Guid RunId, long Sequence, ObservationRequest Request,
    long Timestamp, ObservationKind Kind, ObservationOutcome Outcome = ObservationOutcome.Pending,
    int? PreparedRequestBytes = null, int? ReadResponseBytes = null, long? AdapterElapsedTicks = null);
public sealed record ObservationQuantiles(int Samples, int Missing, long? P50, long? P95, long? P99, long? Maximum)
{
    public bool SparseTail => Samples is > 0 and < 100;
}
public sealed record ObservationBucket(ObservationLatency Latency, ObservationOutcome Outcome, ObservationQuantiles Ticks);
public sealed record ObservationCounts(int PendingAtStart, int Admitted, int AdmissionRejected,
    int Terminal, int PendingAtEnd, int Committed, int Undrained);
public sealed record ModuleObservationReport(ObservationIdentity Identity, long StartedTimestamp, long EndedTimestamp,
    int EventCapacity, int RequestCapacity, long DroppedEvents, long InvalidEvents, ObservationCounts Counts,
    ModuleObservation[] Events, ObservationBucket[] Latencies, string EvidenceSha256, ObservationScope Scope)
{
    // Complete supplied observation sequence, NOT proof that the host emitted every real event.
    public bool CompleteObservedCoverage => DroppedEvents == 0 && InvalidEvents == 0;
    public bool AllObservedRequestsDrained => Counts.Undrained == 0;
    public double? TerminalPerSecond => CompleteObservedCoverage && EndedTimestamp > StartedTimestamp
        ? Counts.Terminal * (double)Identity.TimestampFrequency / (EndedTimestamp - StartedTimestamp) : null;
    public bool DomainStagesAvailable => Scope == ObservationScope.SuppliedEvents;
    public double? CommitsPerSecond => DomainStagesAvailable && CompleteObservedCoverage && EndedTimestamp > StartedTimestamp
        ? Counts.Committed * (double)Identity.TimestampFrequency / (EndedTimestamp - StartedTimestamp) : null;
    public bool PerformanceAccepted => false;
}
