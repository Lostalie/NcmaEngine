namespace Ncma.Application;

public enum ProcessSeriesStop { Sampling, WindowEnded, ExplicitSeal, Capacity, ClockFailure }
public readonly record struct ProcessSeriesSample(int Slot, long StartedTimestamp, long EndedTimestamp,
    long ObserverAllocatedBytes, ProcessSeriesCounters Counters)
{
    public bool ObserverAllocationValid => ObserverAllocatedBytes >= 0;
}
public sealed record ProcessSeriesReport(int Version, Guid RunId, int ProcessId, int ObserverThreadId,
    long TimestampFrequency, long StartedTimestamp, long EndedTimestamp, long IntervalTicks, long DurationTicks,
    int Capacity, int PlannedSlots, int MissingDueSlots, int UnobservedSlots, ProcessSeriesStop Stop,
    ProcessSeriesSample[] Samples)
{
    public bool CompleteCadenceCoverage => Stop == ProcessSeriesStop.WindowEnded && MissingDueSlots == 0 && UnobservedSlots == 0 && Samples.Length == PlannedSlots;
    public bool AllCountersAvailable => Samples.Length > 0 && Samples.All(s => s.Counters is { Failures: ProcessSeriesFailure.None });
    public bool PerformanceAccepted => false;
    public bool MemoryAccepted => false;
    public string Scope => "owner-pumped current-process series, not a timer or independent background sampler; sequential non-atomic reads; CPU is cumulative 100ns, not percent; process allocation is approximate all-thread total; last-GC data may be stale; includes observer/runtime, not native-only/GPU/child-process or leak proof";
}

// Explicit local observation. No sleep, worker, PID selector, file IO, forced GC or business callback.
// One read at most per Poll; never fabricate catch-up samples or rotate the retained prefix.
public sealed class ProcessSeriesCapture
{
    public const int MaximumCapacity = 8192, MaximumPlannedSlots = 36000;
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly Guid run;
    private readonly TimeProvider clock;
    private readonly Func<ProcessSeriesCounters> read;
    private readonly ProcessSeriesSample[] samples;
    private readonly long frequency, started, interval, duration;
    private readonly int planned;
    private long last, ended;
    private int count, nextSlot, missing;
    private ProcessSeriesStop stop;
    private bool busy, sealedReport;
    public ProcessSeriesCapture(Guid runId, TimeSpan interval, TimeSpan duration, int capacity = 4096)
        : this(runId, interval, duration, capacity, TimeProvider.System, ProcessSeriesCounters.ReadCurrent) { }
    // Only named test assemblies can inject deterministic clock/counter reads. No public callback API.
    internal ProcessSeriesCapture(Guid runId, TimeSpan interval, TimeSpan duration, int capacity,
        TimeProvider clock, Func<ProcessSeriesCounters> read)
    {
        ArgumentNullException.ThrowIfNull(clock); ArgumentNullException.ThrowIfNull(read);
        if (runId == Guid.Empty || capacity is < 1 or > MaximumCapacity || interval.Ticks % TimeSpan.TicksPerMillisecond != 0 ||
            duration.Ticks % TimeSpan.TicksPerMillisecond != 0 || interval < TimeSpan.FromMilliseconds(100) || interval > TimeSpan.FromSeconds(60) ||
            duration < interval || duration > TimeSpan.FromHours(1) || clock.TimestampFrequency < 1000)
            throw new ArgumentException("process_series_plan");
        frequency = clock.TimestampFrequency;
        Int128 intervalTicks = ((Int128)frequency * (interval.Ticks / TimeSpan.TicksPerMillisecond) + 999) / 1000;
        Int128 durationTicks = (Int128)frequency * (duration.Ticks / TimeSpan.TicksPerMillisecond) / 1000;
        if (intervalTicks > long.MaxValue || durationTicks > long.MaxValue) throw new ArgumentException("process_series_plan_overflow");
        this.interval = (long)intervalTicks; this.duration = (long)durationTicks;
        planned = checked((int)((durationTicks + intervalTicks - 1) / intervalTicks));
        if (planned is < 1 or > MaximumPlannedSlots) throw new ArgumentException("process_series_slot_budget");
        started = last = ended = clock.GetTimestamp();
        if (started < 0 || durationTicks > long.MaxValue - started) throw new ArgumentException("process_series_clock_range");
        run = runId; this.clock = clock; this.read = read; samples = new ProcessSeriesSample[capacity];
    }
    private void Verify()
    {
        if (Environment.CurrentManagedThreadId != owner || busy || sealedReport) throw new InvalidOperationException("process_series_owner_or_closed");
    }
    private bool Now(out long timestamp)
    {
        timestamp = last;
        try {
            long value = clock.GetTimestamp();
            if (value < last) { stop = ProcessSeriesStop.ClockFailure; ended = last; return false; }
            last = timestamp = value; return true;
        } catch (Exception e) when (e is not OutOfMemoryException) { stop = ProcessSeriesStop.ClockFailure; ended = last; return false; }
    }
    public bool Poll()
    {
        Verify(); if (stop != ProcessSeriesStop.Sampling || !Now(out long now)) return false;
        if (now - started >= duration) { missing += planned - nextSlot; nextSlot = planned; stop = ProcessSeriesStop.WindowEnded; ended = now; return false; }
        int slot = (int)((now - started) / interval);
        if (slot < nextSlot) return false;
        missing += slot - nextSlot; nextSlot = slot;
        if (count == samples.Length) { stop = ProcessSeriesStop.Capacity; ended = now; return false; }
        nextSlot++;
        long allocated = GC.GetAllocatedBytesForCurrentThread(); ProcessSeriesCounters counters;
        busy = true;
        try {
            try { counters = read(); ProcessSeriesCounters.Validate(counters); }
            catch (Exception e) when (e is not OutOfMemoryException) { counters = ProcessSeriesCounters.Unavailable; }
        } finally { busy = false; }
        if (!Now(out long end)) { missing++; return false; } // Due read without valid timing is not a fabricated sample.
        long cost = GC.GetAllocatedBytesForCurrentThread() - allocated;
        samples[count++] = new(slot, now, end, cost, counters); ended = end; return true;
    }
    public ProcessSeriesReport Seal()
    {
        Verify();
        if (stop == ProcessSeriesStop.Sampling && Now(out long now)) {
            int due = now - started >= duration ? planned : (int)((now - started) / interval) + 1;
            missing += due - nextSlot; nextSlot = due;
            stop = now - started >= duration ? ProcessSeriesStop.WindowEnded : ProcessSeriesStop.ExplicitSeal; ended = now;
        }
        sealedReport = true;
        return new(1, run, Environment.ProcessId, owner, frequency, started, ended, interval, duration, samples.Length,
            planned, missing, planned - count - missing, stop, samples.AsSpan(0, count).ToArray());
    }
}
