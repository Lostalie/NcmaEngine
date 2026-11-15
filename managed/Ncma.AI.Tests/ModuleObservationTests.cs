using Ncma.Modules;

internal static class ModuleObservationTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Observation contract assertion"); }
    private static void Reject(Action action) {
        try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; }
        throw new Exception("Observation must reject");
    }
    private static readonly ObservationIdentity Identity = new(new("11111111-1111-1111-1111-111111111111"), new('A', 64), new('B', 64),
        ObservationConfiguration.Release, ObservationVariant.AiExtensions, 1000);
    private static ObservationRequest Key(int number = 1) => new(new("22222222-2222-2222-2222-222222222222"), new(number, 0, 0, new byte[8]));
    private sealed class Fixture(int events = 4096, int requests = 4096)
    {
        internal readonly ModuleObservationCapture Capture = new(Identity, 0, events, requests);
        private long sequence;
        internal ModuleObservation Next(ObservationKind kind, long time, int key = 1, ObservationOutcome outcome = ObservationOutcome.Pending) =>
            new(Identity.RunId, sequence + 1, Key(key), time, kind, outcome);
        internal void Add(ObservationKind kind, long time, int key = 1, ObservationOutcome outcome = ObservationOutcome.Pending) {
            Check(Capture.Record(Next(kind, time, key, outcome))); sequence++;
        }
        internal void Transport(long time, long elapsed, int prepared, int read) {
            Check(Capture.Record(Next(ObservationKind.Transport, time) with { AdapterElapsedTicks = elapsed, PreparedRequestBytes = prepared, ReadResponseBytes = read })); sequence++;
        }
    }
    private static ObservationQuantiles Bucket(ModuleObservationReport report, ObservationLatency latency, ObservationOutcome outcome) =>
        report.Latencies.Single(b => b.Latency == latency && b.Outcome == outcome).Ticks;
    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("M8.7-E2 empty and missing samples never manufacture zero latency", () => {
            var empty = new Fixture().Capture.Seal(0);
            Check(empty.Events.Length == 0 && empty.Latencies.Length == 0 && empty.TerminalPerSecond is null && !empty.PerformanceAccepted);
            var f = new Fixture(); f.Add(ObservationKind.Admitted, 1); var report = f.Capture.Seal(1000);
            var missing = Bucket(report, ObservationLatency.AdmissionToTerminal, ObservationOutcome.Pending);
            Check(missing.Samples == 0 && missing.Missing == 1 && missing.P50 is null && missing.P95 is null && missing.P99 is null && missing.Maximum is null);
            Check(report.Counts.PendingAtEnd == 1 && report.Counts.Undrained == 1 && !report.AllObservedRequestsDrained && report.TerminalPerSecond == 0);
        });
        yield return ("M8.7-E2 exact queue adapter review commit and body byte semantics", () => {
            var f = new Fixture(); f.Add(ObservationKind.Admitted, 10); f.Add(ObservationKind.Dispatched, 20);
            f.Transport(30, 7, 42, 0); f.Add(ObservationKind.Terminal, 40, outcome: ObservationOutcome.Proposal);
            f.Add(ObservationKind.ReviewReady, 50); f.Add(ObservationKind.CommitStarted, 500); f.Add(ObservationKind.Committed, 550); f.Add(ObservationKind.Drained, 600);
            var r = f.Capture.Seal(1000);
            Check(r.Counts == new ObservationCounts(0, 1, 0, 1, 0, 1, 0) && r.TerminalPerSecond == 1 && r.CommitsPerSecond == 1);
            Check(Bucket(r, ObservationLatency.Queue, ObservationOutcome.Proposal).P50 == 10);
            Check(Bucket(r, ObservationLatency.AdmissionToTerminal, ObservationOutcome.Proposal).P50 == 30);
            Check(Bucket(r, ObservationLatency.Adapter, ObservationOutcome.Proposal).P50 == 7);
            Check(Bucket(r, ObservationLatency.TerminalToReview, ObservationOutcome.Proposal).P50 == 10);
            Check(Bucket(r, ObservationLatency.Commit, ObservationOutcome.Proposal).P50 == 50);
            Check(r.Events[2].PreparedRequestBytes == 42 && r.Events[2].ReadResponseBytes == 0 && r.Events[0].ReadResponseBytes is null);
            Check(r.CompleteObservedCoverage && r.AllObservedRequestsDrained && !r.PerformanceAccepted);
        });
        yield return ("M8.7-E2 carry and admission refusals preserve window conservation", () => {
            var f = new Fixture(); f.Add(ObservationKind.Carried, 0); f.Add(ObservationKind.Admitted, 10, 2);
            f.Add(ObservationKind.AdmissionRejected, 11, 2); f.Add(ObservationKind.AdmissionRejected, 12, 3);
            f.Add(ObservationKind.Terminal, 20, outcome: ObservationOutcome.Failed);
            var r = f.Capture.Seal(100);
            Check(r.Counts == new ObservationCounts(1, 1, 2, 1, 1, 0, 2));
            Check(r.Counts.PendingAtStart + r.Counts.Admitted == r.Counts.Terminal + r.Counts.PendingAtEnd);
            Check(Bucket(r, ObservationLatency.AdmissionToTerminal, ObservationOutcome.Failed).Samples == 0);
            Check(Bucket(r, ObservationLatency.AdmissionToTerminal, ObservationOutcome.Failed).Missing == 1);
        });
        yield return ("M8.7-E2 nearest rank exact 100 nonzero controls and outcome isolation", () => {
            var f = new Fixture();
            for (int i = 1; i <= 100; i++) { f.Add(ObservationKind.Admitted, i * 1000, i); f.Add(ObservationKind.Terminal, i * 1000 + i, i, ObservationOutcome.Proposal); }
            f.Add(ObservationKind.Admitted, 101000, 101); f.Add(ObservationKind.Terminal, 101999, 101, ObservationOutcome.TimedOut);
            var r = f.Capture.Seal(102000); var q = Bucket(r, ObservationLatency.AdmissionToTerminal, ObservationOutcome.Proposal);
            Check(q == new ObservationQuantiles(100, 0, 50, 95, 99, 100) && !q.SparseTail);
            var timeout = Bucket(r, ObservationLatency.AdmissionToTerminal, ObservationOutcome.TimedOut);
            Check(timeout.Samples == 1 && timeout.P99 == 999 && timeout.SparseTail && r.Counts.Committed == 0);
        });
        yield return ("M8.7-E2 malformed identity enum sequence time and byte shapes are sticky gaps", () => {
            var valid = new ModuleObservation(Identity.RunId, 1, Key(), 1, ObservationKind.Admitted);
            foreach (var bad in new[] { valid with { RunId = Guid.NewGuid() }, valid with { Sequence = 2 }, valid with { Request = default },
                valid with { Timestamp = -1 }, valid with { Kind = (ObservationKind)999 }, valid with { Outcome = (ObservationOutcome)999 },
                valid with { Outcome = ObservationOutcome.Failed }, valid with { PreparedRequestBytes = 0 },
                valid with { Kind = ObservationKind.Terminal }, valid with { Kind = ObservationKind.Transport, PreparedRequestBytes = -1, ReadResponseBytes = 0, AdapterElapsedTicks = 1 } }) {
                var f = new Fixture(); Reject(() => f.Capture.Record(bad)); f.Add(ObservationKind.Admitted, 1);
                var r = f.Capture.Seal(2); Check(r.InvalidEvents == 1 && r.Events.Length == 1 && !r.CompleteObservedCoverage && r.TerminalPerSecond is null);
            }
        });
        yield return ("M8.7-E2 illegal and duplicate transitions never double count", () => {
            var f = new Fixture(); f.Add(ObservationKind.Admitted, 0);
            foreach (var kind in new[] { ObservationKind.Admitted, ObservationKind.Drained, ObservationKind.ReviewReady, ObservationKind.CommitStarted, ObservationKind.Committed })
                Reject(() => f.Capture.Record(f.Next(kind, 1)));
            f.Add(ObservationKind.Terminal, 2, outcome: ObservationOutcome.Proposal);
            Reject(() => f.Capture.Record(f.Next(ObservationKind.Terminal, 3, outcome: ObservationOutcome.Failed)));
            f.Add(ObservationKind.ReviewReady, 3); f.Add(ObservationKind.CommitStarted, 4); f.Add(ObservationKind.Committed, 5);
            Reject(() => f.Capture.Record(f.Next(ObservationKind.Committed, 6)));
            var r = f.Capture.Seal(7); Check(r.Counts.Terminal == 1 && r.Counts.Committed == 1 && r.InvalidEvents == 7);
        });
        yield return ("M8.7-E2 event and request bounds preserve prefix and report all later drops", () => {
            foreach (bool eventLimit in new[] { true, false }) {
                var f = new Fixture(eventLimit ? 1 : 16, eventLimit ? 16 : 1); f.Add(ObservationKind.Admitted, 0);
                Check(!f.Capture.Record(f.Next(ObservationKind.Admitted, 1, 2)));
                Check(!f.Capture.Record(f.Next(ObservationKind.Terminal, 2, outcome: ObservationOutcome.Failed)));
                var r = f.Capture.Seal(3); Check(r.Events.Length == 1 && r.DroppedEvents == 2 && r.InvalidEvents == 0 && r.Counts.PendingAtEnd == 1);
                Check(!r.CompleteObservedCoverage && r.TerminalPerSecond is null && r.CommitsPerSecond is null);
            }
        });
        yield return ("M8.7-E2 cancel result and actual drain remain distinct", () => {
            var f = new Fixture(); f.Add(ObservationKind.Admitted, 0); f.Add(ObservationKind.Dispatched, 5);
            f.Add(ObservationKind.CancelRequested, 20); f.Add(ObservationKind.Terminal, 30, outcome: ObservationOutcome.Cancelled);
            f.Transport(90, 85, 20, 31); f.Add(ObservationKind.Drained, 100); var r = f.Capture.Seal(100);
            Check(Bucket(r, ObservationLatency.CancelToDrain, ObservationOutcome.Cancelled).P50 == 80);
            Check(Bucket(r, ObservationLatency.AdmissionToTerminal, ObservationOutcome.Cancelled).P50 == 30);
            Check(r.Counts.Committed == 0 && r.Counts.Undrained == 0 && r.Events[4].ReadResponseBytes == 31);
        });
        yield return ("M8.7-E2 owner thread seal and input bounds remain closed", () => {
            Reject(() => new ModuleObservationCapture(Identity with { RunId = Guid.Empty }, 0));
            Reject(() => new ModuleObservationCapture(Identity with { SourceSha256 = "key-not-a-hash" }, 0));
            Reject(() => new ModuleObservationCapture(Identity with { TimestampFrequency = 0 }, 0));
            Reject(() => new ModuleObservationCapture(Identity, 0, ModuleObservationCapture.MaximumEvents + 1));
            Reject(() => new ModuleObservationCapture(Identity, 0, requestCapacity: ModuleObservationCapture.MaximumRequests + 1));
            var f = new Fixture(); Task.Run(() => Reject(() => f.Capture.Record(f.Next(ObservationKind.Admitted, 1)))).GetAwaiter().GetResult();
            f.Add(ObservationKind.Admitted, 2); Reject(() => f.Capture.Seal(1)); var r = f.Capture.Seal(3);
            Check(r.InvalidEvents == 1 && r.Counts.Admitted == 1);
            Reject(() => f.Capture.Record(f.Next(ObservationKind.Drained, 3))); Reject(() => f.Capture.Seal(3));
        });
        yield return ("M8.7-E2 canonical evidence hash binds run variant config source window and raw events", () => {
            static ModuleObservationReport Make(ObservationIdentity id, long end = 10, long admitted = 0) {
                var c = new ModuleObservationCapture(id, 0); Check(c.Record(new(id.RunId, 1, Key(), admitted, ObservationKind.Admitted))); return c.Seal(end);
            }
            var a = Make(Identity); var b = Make(Identity); Check(a.EvidenceSha256 == b.EvidenceSha256 && a.EvidenceSha256.Length == 64);
            foreach (var other in new[] { Identity with { RunId = Guid.NewGuid() }, Identity with { Configuration = ObservationConfiguration.Debug },
                Identity with { Variant = ObservationVariant.Default }, Identity with { SourceSha256 = new('C', 64) },
                Identity with { WorkloadSha256 = new('D', 64) }, Identity with { TimestampFrequency = 2000 } })
                Check(Make(other).EvidenceSha256 != a.EvidenceSha256);
            Check(Make(Identity, end: 11).EvidenceSha256 != a.EvidenceSha256 && Make(Identity, admitted: 1).EvidenceSha256 != a.EvidenceSha256);
            a.Events[0] = default; Check(b.Events[0].Request == Key()); // detached, not an authenticated report object
        });
        yield return ("M8.7-E2 session identity and opening carry do not alias requests", () => {
            var f = new Fixture(); f.Add(ObservationKind.Carried, 0);
            var other = f.Next(ObservationKind.Admitted, 1) with { Request = Key() with { SessionId = Guid.NewGuid() } };
            Check(f.Capture.Record(other));
            Reject(() => f.Capture.Record(new(Identity.RunId, 3, Key(2), 1, ObservationKind.Carried)));
            var r = f.Capture.Seal(2); Check(r.Counts.Admitted == 1 && r.Counts.PendingAtStart == 1 && r.Counts.PendingAtEnd == 2 && r.InvalidEvents == 1);
        });
    }
}
