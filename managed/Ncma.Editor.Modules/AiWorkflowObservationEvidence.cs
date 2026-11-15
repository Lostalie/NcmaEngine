using System.Text.Json;
using Ncma.AI;
using Ncma.Editor.Services;
using Ncma.Modules;

namespace Ncma.Editor.Modules;

public sealed record WorkflowObservationSummary(int Requests, int JoinedTransportSamples, int UnjoinedTransportSamples,
    int ReviewReady, int ConfirmAttempts, int ReceiptBackedSuccesses, int ChangedReceipts,
    int PendingModuleRequests, int UndrainedModuleRequests, bool CompleteCapturedScope)
{
    public string Scope => "original Confirm calls through workflow after wrapper guards; not all domain commits or pure command time";
    public bool PerformanceAccepted => false;
}

// Bounded numeric evidence only. No reconstruction of omitted transport diagnostics or business authority.
public static partial class AiWorkflowObservationEvidence
{
    public const int Version = 1, MaximumBytes = 64 * 1024 * 1024;
    public sealed record TransportRow(long CompletionSequence, long InvocationSequence, ObservationRequest Request,
        long StartedTimestamp, long CompletedTimestamp, long AdapterElapsedTicks, bool OwnedActiveSlot,
        int? HttpStatus, int PreparedRequestBytes, int ReadResponseBytes, AiTokenUsage? Usage);
    public sealed record TransportData(long StartedTimestamp, long EndedTimestamp, int Capacity, long Invoked,
        long Completed, long Pending, long Dropped, long BoundaryOverlaps, TransportRow[] Samples);
    public sealed record Document(int Version, ObservationIdentity Identity, AiAuthoringIntent Intent,
        long StartedTimestamp, long EndedTimestamp, int RequestCapacity, int EventCapacity,
        long DroppedRequests, long DroppedEvents, long ObservationFailures, WorkflowObservedRequest[] Requests,
        WorkflowObservedEvent[] Events, TransportData Transport, WorkflowObservationSummary Summary);
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition) { if (!condition) throw new ArgumentException("workflow_observation_evidence_mismatch"); }
    private static bool Hash(string? text) => text is { Length: 64 } && text.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    public static WorkflowObservationSummary Inspect(AiWorkflowObservationReport report) => Validate(report).Summary;
    public static byte[] Encode(AiWorkflowObservationReport report, int maximumBytes = MaximumBytes)
    {
        if (maximumBytes is < 1 or > MaximumBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var data = Validate(report);
        using var output = new BoundedOutput(maximumBytes);
        JsonSerializer.Serialize(output, data, Options); return output.Copy();
    }
    public static DiagnosticEvidenceFileReceipt ExportNew(string destination, AiWorkflowObservationReport report, int maximumBytes = MaximumBytes)
        => DiagnosticEvidenceFiles.WriteNew(destination, Encode(report, maximumBytes));

    private static Document Validate(AiWorkflowObservationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var transport = report.Transport;
        Require(transport is not null && transport.Identity == report.Identity && transport.Samples is not null && transport.Samples.Length <= 4096);
        var samples = (AiTransportSample[])transport.Samples.Clone(); var rows = new TransportRow[samples.Length];
        for (int i = 0; i < samples.Length; ++i) {
            var sample = samples[i]; Require(sample is not null && sample.Diagnostic is not null);
            var diagnostic = sample.Diagnostic;
            // Validate source-only values before deliberately omitting them. Reader never fabricates them.
            Require(diagnostic.RequestId == sample.Request.RequestId && double.IsFinite(diagnostic.ElapsedMilliseconds) && diagnostic.ElapsedMilliseconds >= 0);
            rows[i] = new(sample.CompletionSequence, sample.InvocationSequence, sample.Request, sample.StartedTimestamp,
                sample.CompletedTimestamp, sample.AdapterElapsedTicks, sample.OwnedActiveSlot, diagnostic.HttpStatus,
                diagnostic.RequestBytes, diagnostic.ResponseBytes, diagnostic.Usage);
        }
        return ValidateDocument(new(Version, report.Identity, report.Intent, report.StartedTimestamp, report.EndedTimestamp,
            report.RequestCapacity, report.EventCapacity, report.DroppedRequests, report.DroppedEvents, report.ObservationFailures,
            report.Requests, report.Events, new(transport.StartedTimestamp, transport.EndedTimestamp, transport.Capacity, transport.Invoked,
                transport.Completed, transport.Pending, transport.Dropped, transport.BoundaryOverlaps, rows), null!), false);
    }
    private static bool CompleteTransport(TransportData value) => value.Pending == 0 && value.Dropped == 0 && value.BoundaryOverlaps == 0;
    private static Document ValidateDocument(Document report, bool suppliedSummary)
    {
        ArgumentNullException.ThrowIfNull(report);
        Require(report.Version == Version && report.Identity is not null && Enum.IsDefined(report.Intent) && report.StartedTimestamp >= 0 && report.EndedTimestamp >= report.StartedTimestamp &&
            report.RequestCapacity is >= 1 and <= AiWorkflowObservations.MaximumRequests && report.EventCapacity is >= 1 and <= AiWorkflowObservations.MaximumEvents &&
            report.DroppedRequests >= 0 && report.DroppedEvents >= 0 && report.ObservationFailures >= 0 && report.Requests is not null && report.Events is not null && report.Transport is not null);
        _ = new ModuleObservationCapture(report.Identity, report.StartedTimestamp, 1); // Original identity grammar.
        Require(report.Requests.Length <= report.RequestCapacity && report.Events.Length <= report.EventCapacity &&
            (report.DroppedRequests == 0 || report.Requests.Length == report.RequestCapacity) && (report.DroppedEvents == 0 || report.Events.Length == report.EventCapacity));
        var requests = (WorkflowObservedRequest[])report.Requests.Clone(); var events = (WorkflowObservedEvent[])report.Events.Clone();
        var byId = new Dictionary<Guid, WorkflowObservedRequest>(); var keys = new Dictionary<ObservationRequest, Guid>();
        int pending = 0, undrained = 0; bool moduleComplete = true;
        for (int i = 0; i < requests.Length; ++i) {
            var request = requests[i]; Require(request is not null && request.RequestId != Guid.Empty && request.Source is not null);
            DomainModuleBridge.Context(request.Source).Validate(); Require(byId.TryAdd(request.RequestId, request));
            if (request.Module is null) { moduleComplete = false; Require(report.ObservationFailures > 0); continue; }
            var module = ModuleObservationCapture.ValidateReport(request.Module);
            Require(module.Identity == report.Identity && module.Scope == ObservationScope.ModuleSession && module.EventCapacity == AiWorkflowObservations.ModuleEventCapacity &&
                module.StartedTimestamp >= report.StartedTimestamp && module.EndedTimestamp <= report.EndedTimestamp && module.Counts.Admitted <= 1 && module.Counts.PendingAtStart == 0);
            var distinct = module.Events.Select(e => e.Request).Distinct().ToArray();
            Require(distinct.Length <= 1 && (distinct.Length == 0 || distinct[0].RequestId == request.RequestId));
            if (distinct.Length == 1) Require(keys.TryAdd(distinct[0], request.RequestId));
            moduleComplete &= module.CompleteObservedCoverage && module.Counts.Admitted == 1;
            pending += module.Counts.PendingAtEnd; undrained += module.Counts.Undrained;
            requests[i] = request with { Module = module }; byId[request.RequestId] = requests[i];
        }
        var transport = report.Transport;
        Require(transport.StartedTimestamp >= report.StartedTimestamp && transport.EndedTimestamp >= transport.StartedTimestamp &&
            transport.EndedTimestamp <= report.EndedTimestamp && transport.Capacity is >= 1 and <= 4096 && transport.Samples is not null && transport.Samples.Length <= transport.Capacity &&
            transport.Invoked >= 0 && transport.Completed >= 0 && transport.Completed <= transport.Invoked && transport.Pending == transport.Invoked - transport.Completed &&
            transport.Dropped >= 0 && transport.Dropped == transport.Completed - transport.Samples.Length &&
            transport.Samples.Length == Math.Min(transport.Completed, transport.Capacity) && transport.BoundaryOverlaps >= 0 && transport.BoundaryOverlaps <= transport.Completed);
        var samples = (TransportRow[])transport.Samples.Clone(); var rows = new TransportRow[samples.Length];
        var ordinals = new HashSet<long>(); var owned = new HashSet<ObservationRequest>(); long last = transport.StartedTimestamp, overlaps = 0;
        int joined = 0, unjoined = 0;
        for (int i = 0; i < samples.Length; ++i) {
            var sample = samples[i]; Require(sample is not null);
            Require(sample.CompletionSequence == i + 1L && sample.InvocationSequence >= 1 && sample.InvocationSequence <= transport.Invoked && ordinals.Add(sample.InvocationSequence) &&
                sample.Request.RequestId != Guid.Empty && sample.Request.SessionId != Guid.Empty &&
                sample.StartedTimestamp >= 0 && sample.CompletedTimestamp >= sample.StartedTimestamp && sample.CompletedTimestamp >= last && sample.CompletedTimestamp <= transport.EndedTimestamp &&
                sample.AdapterElapsedTicks == sample.CompletedTimestamp - sample.StartedTimestamp && sample.PreparedRequestBytes >= 0 && sample.ReadResponseBytes >= 0 &&
                (sample.HttpStatus is null or >= 100 and <= 599));
            last = sample.CompletedTimestamp; if (sample.StartedTimestamp < transport.StartedTimestamp) overlaps++;
            if (sample.OwnedActiveSlot) Require(owned.Add(sample.Request));
            if (sample.Usage is { } usage) Require(usage.PromptTokens >= 0 && usage.CompletionTokens >= 0 && usage.TotalTokens >= usage.PromptTokens && usage.TotalTokens - usage.PromptTokens == usage.CompletionTokens);
            if (keys.TryGetValue(sample.Request, out var id)) {
                joined++;
                var module = byId[id].Module!;
                // Task.Run can execute before the owner records its handoff event; do not invent an ordering.
                Require(!module.CompleteObservedCoverage || module.Events.Any(e => e.Kind == ObservationKind.Dispatched));
                Require(sample.StartedTimestamp >= module.StartedTimestamp);
            } else { unjoined++; Require(report.DroppedRequests > 0 || report.ObservationFailures > 0 || !moduleComplete); }
            rows[i] = sample;
            // Deliberately omit Code/ExceptionType/model/endpoint and arbitrary strings from transport output.
        }
        Require(transport.BoundaryOverlaps >= overlaps && (transport.Dropped > 0 || transport.BoundaryOverlaps == overlaps));
        if (CompleteTransport(transport) && report.ObservationFailures == 0)
            foreach (var pair in keys) if (byId[pair.Value].Module!.Events.Any(e => e.Kind == ObservationKind.Terminal && e.Outcome == ObservationOutcome.Proposal))
                Require(owned.Contains(pair.Key));
        var ready = new Dictionary<Guid, string>(); var resourceReady = new HashSet<Guid>(); var receipted = new HashSet<Guid>();
        int reviews = 0, attempts = 0, successes = 0, changed = 0; last = report.StartedTimestamp;
        for (int i = 0; i < events.Length; ++i) {
            var entry = events[i];
            Require(entry is not null && entry.Sequence == i + 1L &&
                entry.RequestId != Guid.Empty && Enum.IsDefined(entry.Kind) && Enum.IsDefined(entry.Outcome) && entry.StartedTimestamp >= report.StartedTimestamp &&
                entry.StartedTimestamp >= last && entry.EndedTimestamp >= entry.StartedTimestamp && entry.EndedTimestamp <= report.EndedTimestamp);
            last = entry.EndedTimestamp;
            bool known = byId.TryGetValue(entry.RequestId, out var request);
            Require(known || report.DroppedRequests > 0);
            if (entry.Kind is WorkflowObservedKind.ReviewReady or WorkflowObservedKind.ResourceReviewReady) {
                Require(entry.StartedTimestamp == entry.EndedTimestamp && entry.Outcome == WorkflowObservedOutcome.None && !entry.ReceiptObserved && !entry.Changed && entry.ExecutionRevision == 0 && !entry.RecoveryRequired && !entry.Replayed);
                var module = request?.Module;
                Require(module is null || !module.CompleteObservedCoverage || module.Events.Any(e => e.Kind == ObservationKind.Terminal && e.Outcome == ObservationOutcome.Proposal && e.Timestamp <= entry.StartedTimestamp));
                if (entry.Kind == WorkflowObservedKind.ResourceReviewReady) Require(entry.ReviewFingerprint is null && !ready.ContainsKey(entry.RequestId) && resourceReady.Add(entry.RequestId));
                else { Require(Hash(entry.ReviewFingerprint) && ready.TryAdd(entry.RequestId, entry.ReviewFingerprint!)); reviews++; }
            } else {
                attempts++;
                Require(Hash(entry.ReviewFingerprint) && ready.TryGetValue(entry.RequestId, out var fingerprint) && fingerprint == entry.ReviewFingerprint);
                Require(entry.Kind == WorkflowObservedKind.ConfirmThrew ? entry.Outcome == WorkflowObservedOutcome.Exception && !entry.Changed && !entry.Replayed : entry.Outcome is not WorkflowObservedOutcome.None and not WorkflowObservedOutcome.Exception);
                Require(!entry.RecoveryRequired || entry.ReceiptObserved);
                Require(entry.Kind != WorkflowObservedKind.ConfirmReturned || entry.ReceiptObserved || report.ObservationFailures > 0);
                if (entry.ReceiptObserved) Require(receipted.Add(entry.RequestId));
                if (entry.Outcome == WorkflowObservedOutcome.Ok && entry.ReceiptObserved && !entry.RecoveryRequired && !entry.Replayed) { successes++; if (entry.Changed) changed++; }
            }
        }
        bool complete = report.DroppedRequests == 0 && report.DroppedEvents == 0 && report.ObservationFailures == 0 && moduleComplete &&
            CompleteTransport(transport) && unjoined == 0 && pending == 0 && undrained == 0;
        var summary = new WorkflowObservationSummary(requests.Length, joined, unjoined, reviews, attempts, successes, changed, pending, undrained, complete);
        Require(!suppliedSummary || report.Summary == summary);
        return new(Version, report.Identity, report.Intent, report.StartedTimestamp, report.EndedTimestamp, report.RequestCapacity, report.EventCapacity,
            report.DroppedRequests, report.DroppedEvents, report.ObservationFailures, requests, events,
            new(transport.StartedTimestamp, transport.EndedTimestamp, transport.Capacity, transport.Invoked, transport.Completed, transport.Pending, transport.Dropped, transport.BoundaryOverlaps, rows), summary);
    }
    private sealed class BoundedOutput(int maximum) : Stream
    {
        private readonly MemoryStream data = new();
        internal byte[] Copy() => data.ToArray();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) { if (buffer.Length > maximum - data.Length) throw new ArgumentException("workflow_observation_byte_budget"); data.Write(buffer); }
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => data.Length; public override long Position { get => data.Position; set => throw new NotSupportedException(); }
        public override void Flush() { } public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) data.Dispose(); base.Dispose(disposing); }
    }
}
