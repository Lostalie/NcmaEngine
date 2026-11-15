namespace Ncma.Application;

public enum ApplicationRunStop { None, Duration, Requested, ClockFailure }

// Trusted owner control, separate from passive measurement. Cooperative between frames, NOT a watchdog.
public sealed class ApplicationRunWindow
{
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly TimeProvider clock;
    private readonly long duration;
    private long started, last;
    private bool used, requested;
    private ApplicationRunStop stop;
    public ApplicationRunStop Stop { get { Owner(); return stop; } }
    public ApplicationRunWindow(int durationMilliseconds) : this(durationMilliseconds, TimeProvider.System) { }
    internal ApplicationRunWindow(int durationMilliseconds, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (durationMilliseconds is < 1 or > 3600000 || clock.TimestampFrequency < 1000)
            throw new ArgumentException("application_run_window_plan");
        Int128 ticks = (Int128)durationMilliseconds * clock.TimestampFrequency / 1000;
        if (ticks > long.MaxValue) throw new ArgumentException("application_run_window_range");
        duration = (long)ticks; this.clock = clock;
    }
    private void Owner()
    { if (Environment.CurrentManagedThreadId != owner) throw new InvalidOperationException("application_run_window_owner"); }
    public void RequestStop() { Owner(); requested = true; }
    internal void BeginRun()
    {
        Owner(); if (used) throw new InvalidOperationException("application_run_window_single_use");
        used = true;
        try { started = last = clock.GetTimestamp(); if (started < 0 || duration > long.MaxValue - started) throw new InvalidOperationException("application_run_window_clock"); }
        catch { stop = ApplicationRunStop.ClockFailure; throw; }
    }
    internal bool Continue()
    {
        Owner(); if (!used) throw new InvalidOperationException("application_run_window_not_started");
        if (stop != ApplicationRunStop.None) return false;
        if (requested) { stop = ApplicationRunStop.Requested; return false; }
        try {
            long now = clock.GetTimestamp();
            if (now < last) throw new InvalidOperationException("application_run_window_clock");
            last = now;
            if (now - started >= duration) { stop = ApplicationRunStop.Duration; return false; }
            return true;
        } catch { stop = ApplicationRunStop.ClockFailure; throw; }
    }
}
