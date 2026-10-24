using System.Diagnostics;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Ncma.Application.Tests")]

namespace Ncma.Application;

internal interface IFramePacingClock
{
    long Frequency { get; }
    long Timestamp { get; }
    void WaitSlice(long remainingTicks);
}

// Owner-loop admission cap, NOT simulation time or physical display synchronization.
// Late work never earns a catch-up burst: every admission starts a fresh minimum interval.
public sealed class FramePacer
{
    private sealed class SystemClock(Action<uint>? precisionWait=null) : IFramePacingClock
    {
        public long Frequency => Stopwatch.Frequency;
        public long Timestamp => Stopwatch.GetTimestamp();
        public void WaitSlice(long remainingTicks)
        {
            double milliseconds=1000d*remainingTicks/Frequency;
            if(milliseconds>1) {
                if(precisionWait is not null)precisionWait((uint)Math.Clamp(Math.Floor((milliseconds-1)*1000),1,8000));
                else Thread.Sleep((int)Math.Min(8, Math.Max(1, Math.Floor(milliseconds-1))));
            }
            else Thread.SpinWait(64);
        }
    }
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private readonly IFramePacingClock _clock;
    private long? _lastAdmission, _lastObserved;
    private bool _busy, _reentered;
    public int FramesPerSecond { get; }
    public long MinimumIntervalTicks { get; }
    public long? LastAdmissionTimestamp
    {
        get {
            if(Environment.CurrentManagedThreadId!=_owner)throw new InvalidOperationException("Pacing requires its owner thread.");
            return _lastAdmission;
        }
    }
    public FramePacer(int framesPerSecond) : this(framesPerSecond,new SystemClock()) {}
    // The host owns the waiter lifetime. No platform DLL reference in Application;
    // default standalone clock remains portable and makes no precision guarantee.
    public FramePacer(int framesPerSecond,Action<uint> precisionWait)
        : this(framesPerSecond,new SystemClock(precisionWait??throw new ArgumentNullException(nameof(precisionWait)))) {}
    internal FramePacer(int framesPerSecond,IFramePacingClock clock)
    {
        ValidateRate(framesPerSecond);
        ArgumentNullException.ThrowIfNull(clock);
        if(clock.Frequency<=0)throw new ArgumentException("Positive pacing clock frequency required.");
        FramesPerSecond=framesPerSecond;_clock=clock;
        MinimumIntervalTicks=1+(clock.Frequency-1)/framesPerSecond;
    }
    public static void ValidateRate(int framesPerSecond)
    {
        if(framesPerSecond is <1 or >240)throw new ArgumentOutOfRangeException(nameof(framesPerSecond),"Frame cap must be 1..240.");
    }
    private long ReadTime()
    {
        long now=_clock.Timestamp;
        if(now<0||_lastObserved is long previous&&now<previous)throw new InvalidOperationException("Pacing clock must be monotonic.");
        _lastObserved=now;return now;
    }
    public bool Wait(Func<bool>? shouldStop=null)
    {
        if(Environment.CurrentManagedThreadId!=_owner)throw new InvalidOperationException("Pacing requires its owner thread.");
        if(_busy){_reentered=true;throw new InvalidOperationException("Pacing reentry forbidden.");}
        _busy=true;_reentered=false;
        try {
            while(true) {
                bool stop=shouldStop?.Invoke()==true;
                if(_reentered)throw new InvalidOperationException("Pacing callback reentered.");
                if(stop)return false;
                long now=ReadTime();
                if(_lastAdmission is not long last||now-last>=MinimumIntervalTicks) {
                    _lastAdmission=now;return true;
                }
                _clock.WaitSlice(MinimumIntervalTicks-(now-last));
            }
        } finally {_busy=false;}
    }
}
