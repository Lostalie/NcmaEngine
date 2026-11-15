using System.Diagnostics;
using Ncma.AI;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Modules;

namespace Ncma.Editor.Modules;

public enum WorkflowObservedKind { ResourceReviewReady, ReviewReady, ConfirmReturned, ConfirmThrew }
public enum WorkflowObservedOutcome { None, Ok, Error, Conflict, Denied, Other, Exception }
public sealed record WorkflowObservedEvent(long Sequence, Guid RequestId, WorkflowObservedKind Kind,
    long StartedTimestamp, long EndedTimestamp, string? ReviewFingerprint, WorkflowObservedOutcome Outcome,
    bool ReceiptObserved, bool Changed, ulong ExecutionRevision, bool RecoveryRequired, bool Replayed);
public sealed record WorkflowObservedRequest(Guid RequestId, DomainProposalSource Source, ModuleObservationReport? Module);
public sealed record AiWorkflowObservationReport(ObservationIdentity Identity, AiAuthoringIntent Intent,
    long StartedTimestamp, long EndedTimestamp, int RequestCapacity, int EventCapacity,
    long DroppedRequests, long DroppedEvents, long ObservationFailures,
    WorkflowObservedRequest[] Requests, WorkflowObservedEvent[] Events, AiTransportObservationReport Transport)
{
    // Other callers can invoke the original domain service directly. NEVER imply global commit coverage.
    public bool WorkflowConfirmCallsOnly => true;
    public bool PerformanceAccepted => false;
}

// Optional trusted composition, no model data/approval callback. All access is original owner-thread.
internal sealed class AiWorkflowObservations
{
    internal const int MaximumRequests = 256, MaximumEvents = 4096, ModuleEventCapacity = 32;
    private readonly ObservationIdentity identity;
    private readonly AiAuthoringIntent intent;
    private readonly long started;
    private readonly State[] requests;
    private readonly WorkflowObservedEvent[] events;
    private int requestCount, eventCount;
    private long droppedRequests, droppedEvents, failures;
    private bool closed;
    private sealed class State(Guid id, DomainProposalSource source, ModuleSession session)
    {
        internal readonly Guid Id = id;
        internal readonly DomainProposalSource Source = source;
        internal ModuleSession? Session = session;
        internal bool Observed;
        internal ModuleObservationReport? Report;
    }
    internal AiWorkflowObservations(ObservationIdentity identity, AiAuthoringIntent intent, int requestCapacity, int eventCapacity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.TimestampFrequency != Stopwatch.Frequency || requestCapacity is < 1 or > MaximumRequests ||
            eventCapacity is < 1 or > MaximumEvents) throw new ArgumentException("workflow_observation_bounds");
        started = Stopwatch.GetTimestamp(); _ = new ModuleObservationCapture(identity, started, 1);
        this.identity = identity; this.intent = intent; requests = new State[requestCapacity]; events = new WorkflowObservedEvent[eventCapacity];
    }
    // Any observation failure is a sticky evidence gap, not a business error or automatic retry.
    private void Observe(Action action)
    {
        if (closed) return;
        try { action(); } catch (Exception error) when (error is not OutOfMemoryException) { failures++; }
    }
    internal void Attach(ModuleSession session, ApprovedDomainRequest request) => Observe(() => {
        if (requestCount == requests.Length) { droppedRequests++; return; }
        var state = new State(request.RequestId, request.Source, session); requests[requestCount++] = state;
        session.BeginObservation(identity, ModuleEventCapacity); state.Observed = true;
    });
    internal void Detach(ModuleSession session) => Observe(() => {
        foreach (var state in requests.AsSpan(0, requestCount)) if (ReferenceEquals(state.Session, session)) {
            try { if (state.Observed) state.Report = session.SealObservation(); }
            finally { state.Session = null; }
            return;
        }
    });
    private void Add(Guid request, WorkflowObservedKind kind, long start, long end, string? fingerprint,
        WorkflowObservedOutcome outcome = WorkflowObservedOutcome.None, bool receipt = false, bool changed = false,
        ulong revision = 0, bool recovery = false, bool replayed = false)
    {
        if (eventCount == events.Length) { droppedEvents++; return; }
        events[eventCount] = new(eventCount + 1L, request, kind, start, end, fingerprint, outcome, receipt, changed, revision, recovery, replayed);
        eventCount++;
    }
    internal void Review(Guid request, DomainProposalReview? review) => Observe(() => {
        long now = Stopwatch.GetTimestamp();
        Add(request, review is null ? WorkflowObservedKind.ResourceReviewReady : WorkflowObservedKind.ReviewReady, now, now, review?.Fingerprint);
    });
    internal void Confirm(DomainProposalReview review, long start, CapabilityResult? result, EditorDomainProposals domains) => Observe(() => {
        // This bounds the actual Confirm CALL, not pure command time; failed human checks are separate attempts.
        long end = Stopwatch.GetTimestamp(); DomainProposalReceipt? receipt = null;
        try { receipt = domains.Receipts.SingleOrDefault(r => r.ProposalId == review.ProposalId && r.Fingerprint == review.Fingerprint); }
        catch (Exception error) when (error is not OutOfMemoryException) { failures++; }
        bool matches = receipt is not null && (result is null ? receipt.Status == "error" :
            result.RequestId == review.ProposalId && receipt.Status == result.Status && receipt.Code == result.Code && receipt.Changed == result.Changed && receipt.ExecutionRevision == result.ExecutionRevision);
        if (result is not null && !matches) failures++;
        var outcome = result is null ? WorkflowObservedOutcome.Exception : result.Status switch {
            "ok" => WorkflowObservedOutcome.Ok, "error" => WorkflowObservedOutcome.Error, "conflict" => WorkflowObservedOutcome.Conflict,
            "denied" => WorkflowObservedOutcome.Denied, _ => WorkflowObservedOutcome.Other };
        Add(review.ProposalId, result is null ? WorkflowObservedKind.ConfirmThrew : WorkflowObservedKind.ConfirmReturned,
            start, end, review.Fingerprint, outcome, matches, result?.Changed ?? false,
            result?.ExecutionRevision ?? receipt?.ExecutionRevision ?? 0, matches && receipt!.RecoveryRequired, result?.Replayed ?? false);
    });
    internal AiWorkflowObservationReport Seal(AiTransportObservationReport transport)
    {
        // Snapshot active pending sessions WITHOUT Stop/Pump/Cancel. Late completions cannot change this report.
        for (int i = 0; i < requestCount; ++i) if (requests[i].Session is { } session) Detach(session);
        closed = true;
        return new(identity, intent, started, Stopwatch.GetTimestamp(), requests.Length, events.Length,
            droppedRequests, droppedEvents, failures,
            requests.AsSpan(0, requestCount).ToArray().Select(s => new WorkflowObservedRequest(s.Id, s.Source, s.Report)).ToArray(),
            events.AsSpan(0, eventCount).ToArray(), transport);
    }
}
