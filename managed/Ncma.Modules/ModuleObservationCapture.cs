using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Ncma.Modules;

// Trusted passive data contract. No IO/timer/process/approval/GC changes.
// Stores a bounded prefix; on overflow refuses ALL later events, never rotates into a false complete run.
public sealed class ModuleObservationCapture
{
    public const int MaximumEvents = 65536;
    public const int MaximumRequests = 4096;
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly ObservationIdentity identity;
    private readonly long started;
    private readonly ModuleObservation[] events;
    private readonly int requestCapacity;
    private readonly ObservationScope scope;
    private readonly Dictionary<ObservationRequest, State> requests = new();
    private int count, carried, admitted, refused;
    private long last, dropped, invalid;
    private bool closed, overflow, opening = true;
    private struct State
    {
        internal long? Admission, Dispatch, Terminal, Review, CommitStart, Commit, Cancel, Drain, Adapter;
        internal ObservationOutcome Outcome;
    }
    public ModuleObservationCapture(ObservationIdentity identity, long startedTimestamp,
        int eventCapacity = 4096, int requestCapacity = MaximumRequests, ObservationScope scope = ObservationScope.SuppliedEvents)
    {
        ArgumentNullException.ThrowIfNull(identity);
        static bool Hash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
        if (identity.RunId == Guid.Empty || !Hash(identity.SourceSha256) || !Hash(identity.WorkloadSha256) ||
            !Enum.IsDefined(identity.Configuration) || !Enum.IsDefined(identity.Variant) || identity.TimestampFrequency <= 0 ||
            startedTimestamp < 0 || eventCapacity is < 1 or > MaximumEvents || requestCapacity is < 1 or > MaximumRequests || !Enum.IsDefined(scope))
            throw new ArgumentException("observation_identity_or_bounds");
        this.identity = identity; started = last = startedTimestamp;
        events = new ModuleObservation[eventCapacity]; this.requestCapacity = requestCapacity;
        this.scope = scope;
    }
    internal void MarkIncomplete() { Verify(); invalid = checked(invalid + 1); }
    private void Verify()
    {
        if (Environment.CurrentManagedThreadId != owner) throw new InvalidOperationException("observation_owner_thread");
        if (closed) throw new InvalidOperationException("observation_sealed");
    }
    public bool Record(in ModuleObservation value)
    {
        Verify();
        if (overflow) { dropped = checked(dropped + 1); return false; }
        try { return RecordChecked(value); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) {
            invalid = checked(invalid + 1); throw; // sticky evidence gap even if caller swallows the exception
        }
    }
    private bool RecordChecked(in ModuleObservation value)
    {
        if (value.RunId != identity.RunId || value.Sequence != count + 1L || value.Request.SessionId == Guid.Empty ||
            value.Request.RequestId == Guid.Empty || value.Timestamp < last || !Enum.IsDefined(value.Kind) || !Enum.IsDefined(value.Outcome))
            throw new ArgumentException("observation_identity_sequence_time");
        bool terminal = value.Kind == ObservationKind.Terminal;
        if (scope == ObservationScope.ModuleSession && value.Kind is ObservationKind.Carried or ObservationKind.ReviewReady or ObservationKind.CommitStarted or ObservationKind.Committed or ObservationKind.Transport)
            throw new ArgumentException("observation_scope");
        if (terminal == (value.Outcome == ObservationOutcome.Pending)) throw new ArgumentException("observation_outcome_shape");
        bool transport = value.Kind == ObservationKind.Transport;
        if (transport ? value.PreparedRequestBytes is null or < 0 || value.ReadResponseBytes is null or < 0 || value.AdapterElapsedTicks is null or < 0
            : value.PreparedRequestBytes is not null || value.ReadResponseBytes is not null || value.AdapterElapsedTicks is not null)
            throw new ArgumentException("observation_transport_shape");
        bool creates = value.Kind is ObservationKind.Carried or ObservationKind.Admitted;
        bool known = requests.TryGetValue(value.Request, out var state);
        if (creates ? known : !known && value.Kind != ObservationKind.AdmissionRejected)
            throw new InvalidOperationException("observation_request_identity");
        switch (value.Kind) {
            case ObservationKind.Carried:
                if (!opening || value.Timestamp != started) throw new InvalidOperationException("observation_carry_boundary");
                break;
            case ObservationKind.Admitted: state.Admission = value.Timestamp; break;
            case ObservationKind.AdmissionRejected: break; // admission attempt, not a second terminal for an accepted request
            case ObservationKind.Dispatched:
                if (state.Dispatch is not null || state.Terminal is not null || state.Cancel is not null) throw new InvalidOperationException("observation_dispatch_order");
                state.Dispatch = value.Timestamp; break;
            case ObservationKind.Terminal:
                if (state.Terminal is not null) throw new InvalidOperationException("observation_duplicate_terminal");
                state.Terminal = value.Timestamp; state.Outcome = value.Outcome; break;
            case ObservationKind.ReviewReady:
                if (state.Outcome != ObservationOutcome.Proposal || state.Review is not null) throw new InvalidOperationException("observation_review_order");
                state.Review = value.Timestamp; break;
            case ObservationKind.CommitStarted:
                if (state.Review is null || state.CommitStart is not null) throw new InvalidOperationException("observation_commit_start_order");
                state.CommitStart = value.Timestamp; break;
            case ObservationKind.Committed:
                if (state.CommitStart is null || state.Commit is not null) throw new InvalidOperationException("observation_commit_order");
                state.Commit = value.Timestamp; break;
            case ObservationKind.CancelRequested:
                if (state.Cancel is not null || state.Terminal is not null) throw new InvalidOperationException("observation_cancel_order");
                state.Cancel = value.Timestamp; break;
            case ObservationKind.Drained:
                if (state.Terminal is null || state.Drain is not null) throw new InvalidOperationException("observation_drain_order");
                state.Drain = value.Timestamp; break;
            case ObservationKind.Transport:
                if (state.Adapter is not null) throw new InvalidOperationException("observation_duplicate_transport");
                state.Adapter = value.AdapterElapsedTicks; break;
        }
        if (count == events.Length || creates && requests.Count == requestCapacity) {
            overflow = true; dropped = checked(dropped + 1); return false;
        }
        // Validation and capacity checks complete before any accepted-state mutation.
        if (value.Kind == ObservationKind.Carried) carried++;
        else { opening = false; if (value.Kind == ObservationKind.Admitted) admitted++; }
        if (value.Kind == ObservationKind.AdmissionRejected) refused++;
        else requests[value.Request] = state;
        events[count++] = value; last = value.Timestamp; return true;
    }
    public ModuleObservationReport Seal(long endedTimestamp)
    {
        Verify();
        if (endedTimestamp < last) { invalid = checked(invalid + 1); throw new ArgumentException("observation_end_time"); }
        var counts = new ObservationCounts(carried, admitted, refused, requests.Values.Count(s => s.Terminal is not null),
            requests.Values.Count(s => s.Terminal is null), requests.Values.Count(s => s.Commit is not null), requests.Values.Count(s => s.Drain is null));
        if (counts.PendingAtStart + counts.Admitted != counts.Terminal + counts.PendingAtEnd) throw new InvalidOperationException("observation_conservation");
        var buckets = new List<ObservationBucket>();
        foreach (var outcome in Enum.GetValues<ObservationOutcome>()) foreach (var metric in Enum.GetValues<ObservationLatency>()) {
            var values = new List<long>(); int missing = 0;
            foreach (var state in requests.Values) {
                if (state.Outcome != outcome) continue;
                if (metric == ObservationLatency.TerminalToReview && outcome != ObservationOutcome.Proposal ||
                    metric == ObservationLatency.Commit && state.CommitStart is null || metric == ObservationLatency.CancelToDrain && state.Cancel is null) continue;
                long? ticks = metric switch {
                    ObservationLatency.Queue => state.Dispatch - state.Admission,
                    ObservationLatency.AdmissionToTerminal => state.Terminal - state.Admission,
                    ObservationLatency.TerminalToReview => state.Review - state.Terminal,
                    ObservationLatency.Commit => state.Commit - state.CommitStart,
                    ObservationLatency.CancelToDrain => state.Drain - state.Cancel,
                    ObservationLatency.Adapter => state.Adapter,
                    _ => throw new InvalidOperationException("observation_metric")
                };
                if (ticks is long known) values.Add(known); else missing++;
            }
            if (values.Count == 0 && missing == 0) continue;
            values.Sort();
            long? Quantile(int percent) => values.Count == 0 ? null : values[(percent * values.Count + 99) / 100 - 1];
            buckets.Add(new(metric, outcome, new(values.Count, missing, Quantile(50), Quantile(95), Quantile(99), Quantile(100))));
        }
        var copied = events.AsSpan(0, count).ToArray();
        string hash = Hash(endedTimestamp, copied);
        var report = new ModuleObservationReport(identity, started, endedTimestamp, events.Length, requestCapacity, dropped, invalid, counts, copied, buckets.ToArray(), hash, scope);
        closed = true; return report; // detached arrays; modifying them cannot change this capture
    }
    private string Hash(long ended, ModuleObservation[] rows)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Number(long value) { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteInt64LittleEndian(bytes, value); hash.AppendData(bytes); }
        void Id(Guid value) { Span<byte> bytes = stackalloc byte[16]; value.TryWriteBytes(bytes); hash.AppendData(bytes); }
        hash.AppendData("Ncma.ModuleObservation.v2"u8); Number((int)scope);
        Id(identity.RunId); hash.AppendData(Encoding.ASCII.GetBytes(identity.SourceSha256)); hash.AppendData(Encoding.ASCII.GetBytes(identity.WorkloadSha256));
        Number((int)identity.Configuration); Number((int)identity.Variant); Number(identity.TimestampFrequency);
        Number(started); Number(ended); Number(events.Length); Number(requestCapacity); Number(dropped); Number(invalid); Number(rows.Length);
        foreach (var row in rows) {
            Id(row.RunId); Number(row.Sequence); Id(row.Request.SessionId); Id(row.Request.RequestId); Number(row.Timestamp);
            Number((int)row.Kind); Number((int)row.Outcome); Number(row.PreparedRequestBytes ?? -1); Number(row.ReadResponseBytes ?? -1); Number(row.AdapterElapsedTicks ?? -1);
        }
        return Convert.ToHexString(hash.GetHashAndReset()); // content identity, NOT a trusted-host attestation
    }

    // Replay a bounded detached snapshot, never serialize caller-mutated derived fields directly.
    public static ModuleObservationReport ValidateReport(ModuleObservationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Events is null || report.Latencies is null || report.Counts is null || report.Events.Length > MaximumEvents ||
            report.Latencies.Length > 42 || report.DroppedEvents < 0 || report.InvalidEvents < 0)
            throw new ArgumentException("observation_report_shape");
        var rows = (ModuleObservation[])report.Events.Clone();
        var buckets = (ObservationBucket[])report.Latencies.Clone();
        var replay = new ModuleObservationCapture(report.Identity, report.StartedTimestamp, report.EventCapacity, report.RequestCapacity, report.Scope);
        foreach (var row in rows) if (!replay.Record(row)) throw new ArgumentException("observation_report_prefix");
        if (report.DroppedEvents != 0 && rows.Length != report.EventCapacity && replay.requests.Count != report.RequestCapacity)
            throw new ArgumentException("observation_report_drop_boundary");
        // Missing events cannot be reconstructed; counts are explicit unverified gap declarations.
        replay.dropped = report.DroppedEvents; replay.invalid = report.InvalidEvents;
        var canonical = replay.Seal(report.EndedTimestamp);
        if (canonical.Counts != report.Counts || !canonical.Latencies.SequenceEqual(buckets) || canonical.EvidenceSha256 != report.EvidenceSha256)
            throw new ArgumentException("observation_report_mismatch");
        return canonical;
    }
}
