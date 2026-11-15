using System.Text;
using System.Text.Json;
using Ncma.Application;

internal static class ProcessSeriesTests
{
    private static void Check(bool condition) { if (!condition) throw new Exception("Process series assertion"); }
    private static void Reject(Action action) {
        try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; }
        throw new Exception("Process series must reject");
    }
    private sealed class Clock : TimeProvider
    {
        internal long Now; internal bool Fail;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Fail ? throw new InvalidOperationException("SECRET_CLOCK") : Now;
    }
    private static ProcessSeriesCounters Good() => new(100, 1000, 2000, 10, 2, 4000, 3000, 1, 2000, 5000, new(1, 1, 0), new(1, 1, 0), ProcessSeriesFailure.None);
    private static ProcessSeriesCapture Create(Clock clock, Func<ProcessSeriesCounters>? read = null, int capacity = 8, int milliseconds = 3000, int interval = 1000)
        => new(Guid.NewGuid(), TimeSpan.FromMilliseconds(interval), TimeSpan.FromMilliseconds(milliseconds), capacity, clock, read ?? Good);
    private static ProcessSeriesReport Full() {
        var clock = new Clock(); var capture = Create(clock);
        for (int i = 0; i < 3; ++i) { clock.Now = i * 1000; Check(capture.Poll()); }
        clock.Now = 3000; Check(!capture.Poll()); return capture.Seal();
    }
    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("M8.7-E2C process plan bounds and construction never reads counters", () => {
            var clock = new Clock(); int reads = 0; ProcessSeriesCounters Read() { reads++; return Good(); }
            Reject(() => new ProcessSeriesCapture(Guid.Empty, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), 8, clock, Read));
            foreach (int capacity in new[] { 0, 8193 }) Reject(() => Create(clock, Read, capacity));
            foreach (int interval in new[] { 0, 99, 60001 }) Reject(() => Create(clock, Read, interval: interval));
            Reject(() => Create(clock, Read, milliseconds: 3600001)); Reject(() => Create(clock, Read, milliseconds: 999));
            Reject(() => new ProcessSeriesCapture(Guid.NewGuid(), TimeSpan.FromTicks(1000001), TimeSpan.FromSeconds(3), 8, clock, Read));
            _ = Create(clock, Read); Check(reads == 0);
        });
        yield return ("M8.7-E2C exact half-open cadence retains slots and detached canonical bytes", () => {
            var report = Full(); Check(report.PlannedSlots == 3 && report.CompleteCadenceCoverage && report.AllCountersAvailable && report.Samples.Select(s => s.Slot).SequenceEqual(new[] { 0, 1, 2 }));
            Check(!report.MemoryAccepted && !report.PerformanceAccepted);
            var copy = ProcessSeriesEvidence.ValidateReport(report); report.Samples[0] = default;
            Check(copy.Samples[0].Counters == Good()); byte[] bytes = ProcessSeriesEvidence.Encode(copy);
            using var json = JsonDocument.Parse(bytes); Check(json.RootElement.GetProperty("Samples").GetArrayLength() == 3 && !json.RootElement.GetProperty("PerformanceAccepted").GetBoolean());
        });
        yield return ("M8.7-E2C late poll samples once without catch-up and seal declares due gaps", () => {
            var clock = new Clock(); int reads = 0; var capture = Create(clock, () => { reads++; return Good(); }, milliseconds: 5000);
            clock.Now = 2500; Check(capture.Poll() && !capture.Poll() && reads == 1);
            clock.Now = 3500; var report = capture.Seal(); _ = ProcessSeriesEvidence.ValidateReport(report);
            Check(report.Samples.Single().Slot == 2 && report.MissingDueSlots == 3 && report.UnobservedSlots == 1 && !report.CompleteCadenceCoverage);
        });
        yield return ("M8.7-E2C capacity keeps prefix stops reader and never rotates", () => {
            var clock = new Clock(); int reads = 0; var capture = Create(clock, () => { reads++; return Good(); }, 1);
            Check(capture.Poll()); clock.Now = 1000; Check(!capture.Poll()); clock.Now = 2000; Check(!capture.Poll() && reads == 1);
            var report = capture.Seal(); _ = ProcessSeriesEvidence.ValidateReport(report);
            Check(report.Stop == ProcessSeriesStop.Capacity && report.EndedTimestamp == 1000 && report.UnobservedSlots == 2 && !report.CompleteCadenceCoverage);
            Reject(() => capture.Poll()); Reject(() => capture.Seal());
        });
        yield return ("M8.7-E2C backwards failed and mid-read clocks stop without fabricating samples", () => {
            for (int mode = 0; mode < 3; ++mode) {
                var clock = new Clock(); var capture = Create(clock, () => { if (mode == 2) clock.Now = -1; return Good(); });
                if (mode != 2) { Check(capture.Poll()); if (mode == 0) clock.Now = -1; else clock.Fail = true; }
                Check(!capture.Poll()); var report = capture.Seal(); _ = ProcessSeriesEvidence.ValidateReport(report);
                Check(report.Stop == ProcessSeriesStop.ClockFailure && !report.CompleteCadenceCoverage && report.Samples.Length == (mode == 2 ? 0 : 1));
            }
        });
        yield return ("M8.7-E2C counter failure is a missing-value sample not a fake zero or leaked exception", () => {
            var clock = new Clock(); int reads = 0;
            var capture = Create(clock, () => ++reads == 1 ? throw new IOException("SECRET_PROCESS_PATH") : Good(), milliseconds: 2000);
            Check(capture.Poll()); clock.Now = 1000; Check(capture.Poll()); clock.Now = 2000;
            var report = capture.Seal(); Check(report.CompleteCadenceCoverage && !report.AllCountersAvailable && report.Samples[0].Counters.Failures == ProcessSeriesFailure.All);
            Check(report.Samples[0].Counters.CpuTime100ns is null && report.Samples[1].Counters == Good());
            Check(!Encoding.UTF8.GetString(ProcessSeriesEvidence.Encode(report)).Contains("SECRET", StringComparison.Ordinal));
        });
        yield return ("M8.7-E2C slow reader overhead leaves real cadence holes", () => {
            var clock = new Clock(); int reads = 0; var capture = Create(clock, () => { if (++reads == 1) clock.Now = 450; return Good(); }, milliseconds: 1000, interval: 100);
            Check(capture.Poll() && capture.Poll() && !capture.Poll()); clock.Now = 1000;
            var report = capture.Seal(); _ = ProcessSeriesEvidence.ValidateReport(report);
            Check(report.Samples.Select(s => s.Slot).SequenceEqual(new[] { 0, 4 }) && report.Samples[0].EndedTimestamp == 450 && report.MissingDueSlots == 8 && report.UnobservedSlots == 0);
        });
        yield return ("M8.7-E2C owner and read reentry rejected without extra read", () => {
            var clock = new Clock(); ProcessSeriesCapture? capture = null; int reads = 0;
            capture = Create(clock, () => { reads++; Reject(() => capture!.Poll()); Reject(() => capture!.Seal()); return Good(); });
            Task.Run(() => { Reject(() => capture.Poll()); Reject(() => capture.Seal()); }).GetAwaiter().GetResult();
            Check(capture.Poll() && reads == 1); _ = ProcessSeriesEvidence.ValidateReport(capture.Seal());
        });
        yield return ("M8.7-E2C reject counter shape slot timing counts and output budget tampering", () => {
            var report = Full(); var first = report.Samples[0];
            Reject(() => ProcessSeriesEvidence.Encode(report with { MissingDueSlots = 1 }));
            Reject(() => ProcessSeriesEvidence.Encode(report with { Samples = [first, first, report.Samples[2]] }));
            Reject(() => ProcessSeriesEvidence.Encode(report with { Samples = [first with { StartedTimestamp = -1 }, report.Samples[1], report.Samples[2]] }));
            Reject(() => ProcessSeriesEvidence.Encode(report with { Samples = [first with { Counters = first.Counters with { CpuTime100ns = -1 } }, report.Samples[1], report.Samples[2]] }));
            Reject(() => ProcessSeriesEvidence.Encode(report with { Stop = ProcessSeriesStop.Sampling }));
            Reject(() => ProcessSeriesEvidence.Encode(report, 1)); Reject(() => ProcessSeriesEvidence.Encode(report, ProcessSeriesEvidence.MaximumBytes + 1));
            _ = ProcessSeriesEvidence.Encode(report);
        });
        yield return ("M8.7-E2C partial counter groups and GC overlap stay explicit", () => {
            var clock = new Clock(); var counters = Good() with { WorkingSetBytes = null, PrivateBytes = null, Failures = ProcessSeriesFailure.Memory, GcAfter = new(2, 1, 0) };
            var capture = Create(clock, () => counters); Check(capture.Poll()); var report = capture.Seal(); _ = ProcessSeriesEvidence.ValidateReport(report);
            Check(report.Samples[0].Counters.CpuTime100ns == 100 && report.Samples[0].Counters.CollectionObservedDuringRead && !report.AllCountersAvailable);
            var bad = Create(new Clock(), () => Good() with { WorkingSetBytes = -1 }); Check(bad.Poll());
            Check(bad.Seal().Samples.Single().Counters.Failures == ProcessSeriesFailure.All);
        });
        yield return ("M8.7-E2C actual current-process single read no forced GC or leak acceptance", () => {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows counter verification required, not skipped");
            var capture = new ProcessSeriesCapture(Guid.NewGuid(), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));
            Check(capture.Poll()); var report = capture.Seal(); _ = ProcessSeriesEvidence.ValidateReport(report);
            Check(report.ProcessId == Environment.ProcessId && report.ObserverThreadId == Environment.CurrentManagedThreadId && report.Samples.Length == 1 && report.AllCountersAvailable);
            Check(report.Samples[0].Counters.WorkingSetBytes > 0 && report.Samples[0].Counters.Threads > 0 && !report.MemoryAccepted && !report.PerformanceAccepted);
            Check(report.Samples[0].ObserverAllocationValid == (report.Samples[0].ObserverAllocatedBytes >= 0));
        });
    }
}
