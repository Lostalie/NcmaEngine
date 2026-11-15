using System.Diagnostics;
using Ncma.Modules;

namespace Ncma.AI;

// Completion order, NOT dispatch order. Duplicate/busy calls retain distinct invocation ordinals.
// No raw request/response, endpoint, model, credentials or exception object is retained.
public sealed record AiTransportSample(long CompletionSequence, long InvocationSequence,
    ObservationRequest Request, long StartedTimestamp, long CompletedTimestamp, long AdapterElapsedTicks,
    bool OwnedActiveSlot, AiCallDiagnostic Diagnostic);
public sealed record AiTransportObservationReport(ObservationIdentity Identity, long StartedTimestamp,
    long EndedTimestamp, int Capacity, long Invoked, long Completed, long Pending, long Dropped, long BoundaryOverlaps,
    AiTransportSample[] Samples)
{
    public bool CompleteObservedCoverage => Pending == 0 && Dropped == 0 && BoundaryOverlaps == 0;
    public bool PerformanceAccepted => false;
}

// Access serialized by the original adapter gate. Bounded prefix, no worker/callback/file IO.
internal sealed class AiTransportObservations
{
    internal const int MaximumSamples = 4096;
    private readonly ObservationIdentity identity;
    private readonly AiTransportSample[] samples;
    private readonly long started;
    private long invoked, completed, dropped, boundaryOverlaps;
    private int count;
    private bool sealedReport;
    internal AiTransportObservations(ObservationIdentity identity, int capacity)
    {
        if (capacity is < 1 or > MaximumSamples || identity.TimestampFrequency != Stopwatch.Frequency)
            throw new ArgumentException("ai_transport_observation_bounds");
        started = Stopwatch.GetTimestamp();
        // Reuse the original identity contract; no duplicate source/workload hash grammar.
        _ = new ModuleObservationCapture(identity, started, 1);
        this.identity = identity; samples = new AiTransportSample[capacity];
    }
    internal long Begin() => ++invoked;
    internal void Complete(long ordinal, ModuleRequest request, long callStarted, long callEnded,
        bool ownedActiveSlot, AiCallDiagnostic diagnostic)
    {
        if (sealedReport) return; // A sealed pending report is immutable, never retroactively completed.
        completed++;
        if (callStarted < started) boundaryOverlaps++; // Invocation entered before opt-in acquired the original gate.
        if (count == samples.Length) { dropped++; return; }
        samples[count++] = new(completed, ordinal, new(request.SessionId, request.RequestId),
            callStarted, callEnded, callEnded - callStarted, ownedActiveSlot, diagnostic);
    }
    internal AiTransportObservationReport Seal()
    {
        sealedReport = true;
        return new(identity, started, Stopwatch.GetTimestamp(), samples.Length, invoked, completed,
            invoked - completed, dropped, boundaryOverlaps, samples.AsSpan(0, count).ToArray());
    }
}
