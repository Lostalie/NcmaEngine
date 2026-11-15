using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Ncma.Modules;

internal static class ModuleObservationSessionTests
{
    private static readonly ModuleContext Context = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 0, Guid.Empty, 1, null, Guid.Empty, 0);
    private static readonly ModuleOperation Operation = new(Guid.NewGuid(), "1.0", Guid.NewGuid(), "ncma.observe.fixture", Guid.NewGuid(), Guid.NewGuid(), TimeoutMilliseconds: 1000);
    private static ObservationIdentity Identity() => new(Guid.NewGuid(), new('A', 64), new('B', 64), ObservationConfiguration.Release, ObservationVariant.Ai, 1000);
    private static void Check(bool value) { if (!value) throw new Exception("Observed session assertion"); }
    private static void Reject(Action action) {
        try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { Check(!e.Message.Contains("sensitive-marker", StringComparison.Ordinal)); return; }
        throw new Exception("Observed session must reject");
    }
    private static void Until(Func<bool> predicate) {
        var watch = Stopwatch.StartNew(); while (!predicate()) { if (watch.Elapsed > TimeSpan.FromSeconds(3)) throw new TimeoutException("Observed session fixture drain"); Thread.Sleep(1); }
    }
    private sealed class Clock : TimeProvider
    {
        internal long Now; internal int Reads, ThrowOnRead;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() {
            if (Interlocked.Increment(ref Reads) == ThrowOnRead) throw new InvalidOperationException("fixture_clock_read");
            return Interlocked.Read(ref Now);
        }
    }
    private sealed class Adapter : IModuleAdapter
    {
        internal readonly ConcurrentDictionary<Guid, (ModuleRequest Request, TaskCompletionSource<ModuleReply> Source)> Calls = new();
        private volatile bool released;
        public Task<ModuleReply> InvokeAsync(ModuleRequest request, TimeSpan budget, CancellationToken token) {
            var source = new TaskCompletionSource<ModuleReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            Calls[request.RequestId] = (request, source); if (released) Complete(request.RequestId); return source.Task;
        }
        internal void Complete(Guid id) { var pair = Calls[id]; var r = pair.Request; pair.Source.TrySetResult(new(ModuleRequest.ContractVersion, r.RequestId, r.SessionId, r.Epoch, r.Fingerprint, r.Operation.OutputSchema, ModuleReplyStatus.Proposal, new("{}"u8))); }
        internal void Release() { released = true; foreach (var id in Calls.Keys) Complete(id); }
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly Clock Time = new(); internal readonly Adapter Transport = new(); internal readonly ModuleSession Session;
        internal Fixture() { Session = new(new([Operation]), Transport, Context, timeProvider: Time); }
        internal Guid Submit() { var id = Guid.NewGuid(); Check(Session.Submit(id, Operation.Name, "{\"fixture\":\"sensitive-marker\"}"u8) == ModuleAdmission.Accepted); return id; }
        internal void Dispatch(Guid id) { Session.Pump(Context); Until(() => Transport.Calls.ContainsKey(id)); }
        public void Dispose() { Transport.Release(); Session.Stop(); Until(Session.TryClose); }
    }
    private static ModuleObservationReport Sample(int capacity = 4, bool overflow = false) {
        var id = Identity(); var capture = new ModuleObservationCapture(id, 0, capacity);
        var key = new ObservationRequest(Guid.NewGuid(), Guid.NewGuid());
        Check(capture.Record(new(id.RunId, 1, key, 1, ObservationKind.Admitted)));
        if (overflow) Check(!capture.Record(new(id.RunId, 2, key, 2, ObservationKind.Terminal, ObservationOutcome.Failed)));
        else Check(capture.Record(new(id.RunId, 2, key, 2, ObservationKind.Terminal, ObservationOutcome.Failed)));
        return capture.Seal(3);
    }
    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("M8.7-E2B default off and explicit start seal owner boundaries", () => {
            using var f = new Fixture(); Reject(() => f.Session.SealObservation());
            Reject(() => f.Session.BeginObservation(Identity() with { TimestampFrequency = 999 }));
            Task.Run(() => Reject(() => f.Session.BeginObservation(Identity()))).GetAwaiter().GetResult();
            f.Submit(); Check(f.Time.Reads == 1 && f.Transport.Calls.IsEmpty);
            Reject(() => f.Session.BeginObservation(Identity()));
            using var g = new Fixture(); g.Session.BeginObservation(Identity()); Reject(() => g.Session.BeginObservation(Identity()));
            Task.Run(() => Reject(() => g.Session.SealObservation())).GetAwaiter().GetResult();
            var report = g.Session.SealObservation(); Check(report.Scope == ObservationScope.ModuleSession && report.Events.Length == 0);
            Reject(() => g.Session.SealObservation()); Reject(() => g.Session.BeginObservation(Identity()));
        });
        yield return ("M8.7-E2B actual delivery drain and no domain or payload inference", () => {
            using var f = new Fixture(); f.Session.BeginObservation(Identity()); var id = f.Submit();
            Check(f.Session.Submit(id, Operation.Name, "{}"u8) == ModuleAdmission.Duplicate);
            f.Dispatch(id); f.Transport.Complete(id); ModuleOutcome? outcome = null;
            Until(() => f.Session.TryTake(id, Context, out outcome)); Check(outcome!.Status == ModuleOutcomeStatus.NeedsDomainValidation);
            var report = f.Session.SealObservation(); Check(report.Counts == new ObservationCounts(0, 1, 1, 1, 0, 0, 0));
            Check(report.Events.Select(e => e.Kind).SequenceEqual(new[] { ObservationKind.Admitted, ObservationKind.AdmissionRejected, ObservationKind.Dispatched, ObservationKind.Terminal, ObservationKind.Drained }));
            Check(!report.DomainStagesAvailable && report.CommitsPerSecond is null && report.CompleteObservedCoverage);
            var bytes = ModuleObservationEvidence.Encode(report); Check(!Encoding.UTF8.GetString(bytes).Contains("sensitive-marker", StringComparison.Ordinal));
            Check(ModuleObservationEvidence.Decode(bytes).EvidenceSha256 == report.EvidenceSha256);
        });
        yield return ("M8.7-E2B unclaimed proposal timeout observes only final delivered outcome", () => {
            using var f = new Fixture(); f.Session.BeginObservation(Identity()); var id = f.Submit(); f.Dispatch(id); f.Transport.Complete(id);
            Until(() => { f.Session.Pump(Context); return f.Session.ActiveCount == 0; });
            Interlocked.Exchange(ref f.Time.Now, 1001);
            Check(f.Session.TryTake(id, Context, out var outcome) && outcome!.Status == ModuleOutcomeStatus.TimedOut);
            var report = f.Session.SealObservation(); var terminals = report.Events.Where(e => e.Kind == ObservationKind.Terminal).ToArray();
            Check(terminals.Length == 1 && terminals[0].Outcome == ObservationOutcome.TimedOut && report.Counts.Undrained == 0);
        });
        yield return ("M8.7-E2B ignored cancellation keeps drain later than terminal", () => {
            using var f = new Fixture(); f.Session.BeginObservation(Identity()); var id = f.Submit(); f.Dispatch(id);
            Interlocked.Exchange(ref f.Time.Now, 10); Check(f.Session.Cancel(id));
            Check(f.Session.TryTake(id, Context, out var outcome) && outcome!.Status == ModuleOutcomeStatus.Cancelled);
            Check(f.Session.RetainedCount == 1 && f.Session.ActiveCount == 1);
            Interlocked.Exchange(ref f.Time.Now, 50); f.Transport.Complete(id);
            Until(() => { f.Session.Pump(Context); return f.Session.RetainedCount == 0; });
            var report = f.Session.SealObservation();
            Check(report.Events.Count(e => e.Kind == ObservationKind.Terminal) == 1 && report.Events.Last().Kind == ObservationKind.Drained);
            Check(report.Latencies.Single(b => b.Latency == ObservationLatency.CancelToDrain).Ticks.P50 == 40 && report.Counts.Undrained == 0);
        });
        yield return ("M8.7-E2B overflow and observer clock failure cannot change original admission", () => {
            using var f = new Fixture(); f.Session.BeginObservation(Identity(), 1); var id = f.Submit();
            Check(f.Session.Cancel(id)); Check(f.Session.TryTake(id, Context, out var outcome) && outcome!.Status == ModuleOutcomeStatus.Cancelled);
            var report = f.Session.SealObservation(); Check(report.DroppedEvents == 3 && !report.CompleteObservedCoverage && report.Counts.PendingAtEnd == 1);
            using var g = new Fixture(); g.Session.BeginObservation(Identity()); g.Time.ThrowOnRead = g.Time.Reads + 2;
            var other = g.Submit(); Check(g.Session.Cancel(other)); Check(g.Session.TryTake(other, Context, out var original) && original!.Status == ModuleOutcomeStatus.Cancelled);
            var failed = g.Session.SealObservation(); Check(failed.InvalidEvents == 1 && !failed.CompleteObservedCoverage && failed.Events.Length == 0);
            using var h = new Fixture(); h.Session.BeginObservation(Identity()); h.Session.Stop();
            Check(h.Session.Submit(Guid.Empty, Operation.Name, "{}"u8) == ModuleAdmission.Stopped);
            var unidentified = h.Session.SealObservation(); Check(unidentified.InvalidEvents == 1 && !unidentified.CompleteObservedCoverage);
        });
        yield return ("M8.7-E2B sealing pending evidence never stops or resumes business", () => {
            using var f = new Fixture(); f.Session.BeginObservation(Identity()); var id = f.Submit();
            var report = f.Session.SealObservation(); Check(report.Counts.PendingAtEnd == 1 && !report.AllObservedRequestsDrained);
            f.Dispatch(id); f.Transport.Complete(id); Until(() => f.Session.TryTake(id, Context, out _));
            Check(report.Counts.PendingAtEnd == 1 && report.Events.Length == 1 && f.Session.RetainedCount == 0);
            Reject(() => f.Session.BeginObservation(Identity()));
        });
        yield return ("M8.7-E2B bounded canonical codec roundtrips full and incomplete reports", () => {
            foreach (var report in new[] { Sample(), Sample(1, true) }) {
                var bytes = ModuleObservationEvidence.Encode(report); var decoded = ModuleObservationEvidence.Decode(bytes);
                Check(decoded.Events.SequenceEqual(report.Events) && decoded.Counts == report.Counts && decoded.EvidenceSha256 == report.EvidenceSha256);
                Check(ModuleObservationEvidence.Encode(decoded).SequenceEqual(bytes));
            }
            Reject(() => ModuleObservationEvidence.Encode(Sample(), 32));
            Reject(() => ModuleObservationEvidence.Decode(ModuleObservationEvidence.Encode(Sample()), 32));
            Reject(() => ModuleObservationEvidence.Encode(Sample(), ModuleObservationEvidence.MaximumBytes + 1));
        });
        yield return ("M8.7-E2B mutated summaries events scope gaps and hash fail before export", () => {
            var report = Sample();
            Reject(() => ModuleObservationEvidence.Encode(report with { Counts = report.Counts with { Committed = 1 } }));
            Reject(() => ModuleObservationEvidence.Encode(report with { EvidenceSha256 = new('0', 64) }));
            Reject(() => ModuleObservationEvidence.Encode(report with { Scope = ObservationScope.ModuleSession }));
            Reject(() => ModuleObservationEvidence.Encode(report with { DroppedEvents = 1 }));
            var buckets = (ObservationBucket[])report.Latencies.Clone(); buckets[0] = buckets[0] with { Ticks = buckets[0].Ticks with { Missing = 99 } };
            Reject(() => ModuleObservationEvidence.Encode(report with { Latencies = buckets }));
            var rows = (ModuleObservation[])report.Events.Clone(); rows[0] = rows[0] with { Timestamp = 0 };
            Reject(() => ModuleObservationEvidence.Encode(report with { Events = rows }));
            var encoded = ModuleObservationEvidence.Encode(report); report.Events[0] = default;
            Check(ModuleObservationEvidence.Decode(encoded).Events[0].Request != default);
        });
        yield return ("M8.7-E2B duplicate unknown truncated oversized and false accepted JSON rejected", () => {
            string json = Encoding.UTF8.GetString(ModuleObservationEvidence.Encode(Sample()));
            foreach (var bad in new[] { json.Replace("\"Version\":2", "\"Version\":1", StringComparison.Ordinal),
                json.Replace("\"Version\":2", "\"Version\":2,\"Version\":2", StringComparison.Ordinal),
                json.Replace("\"Version\":2", "\"Version\":2,\"Unknown\":1", StringComparison.Ordinal),
                json.Replace("\"PerformanceAccepted\":false", "\"PerformanceAccepted\":true", StringComparison.Ordinal),
                json[..^1], " " + json, "{\"Version\":2,\"Report\":{\"Events\":[" + string.Join(',', Enumerable.Repeat("{}", 65537)) + "]}}",
                "{\"Version\":2,\"Report\":{\"Identity\":{\"SourceSha256\":\"sensitive-marker" + new string('x', 300) + "\"}}}" })
                Reject(() => ModuleObservationEvidence.Decode(Encoding.UTF8.GetBytes(bad)));
        });
        yield return ("M8.7-E2B module scope forbids invented domain and transport stages", () => {
            var id = Identity(); var capture = new ModuleObservationCapture(id, 0, scope: ObservationScope.ModuleSession);
            var key = new ObservationRequest(Guid.NewGuid(), Guid.NewGuid()); Check(capture.Record(new(id.RunId, 1, key, 0, ObservationKind.Admitted)));
            Check(capture.Record(new(id.RunId, 2, key, 0, ObservationKind.Terminal, ObservationOutcome.Proposal)));
            Reject(() => capture.Record(new(id.RunId, 3, key, 0, ObservationKind.ReviewReady)));
            var report = capture.Seal(1); Check(!report.DomainStagesAvailable && report.CommitsPerSecond is null && report.InvalidEvents == 1);
            var decoded = ModuleObservationEvidence.Decode(ModuleObservationEvidence.Encode(report)); Check(decoded.InvalidEvents == 1);
        });
    }
}
