using System.Diagnostics;

namespace Ncma.Application;

public enum LoopCostOutcome { Completed, Stopped, Faulted, Recovered }
public enum LoopCostPhase { Control, Platform, Intents, Agent, BeginPresentation, Input, Simulation, Presentation, Recovery, Pacing }
public readonly record struct LoopPhaseTicks(long Control, long Platform, long Intents, long Agent,
    long BeginPresentation, long Input, long Simulation, long Presentation, long Recovery, long Pacing)
{
    internal LoopPhaseTicks Add(LoopCostPhase phase, long ticks) => phase switch {
        LoopCostPhase.Control => this with { Control=Control+ticks },
        LoopCostPhase.Platform => this with { Platform=Platform+ticks },
        LoopCostPhase.Intents => this with { Intents=Intents+ticks },
        LoopCostPhase.Agent => this with { Agent=Agent+ticks },
        LoopCostPhase.BeginPresentation => this with { BeginPresentation=BeginPresentation+ticks },
        LoopCostPhase.Input => this with { Input=Input+ticks },
        LoopCostPhase.Simulation => this with { Simulation=Simulation+ticks },
        LoopCostPhase.Presentation => this with { Presentation=Presentation+ticks },
        LoopCostPhase.Recovery => this with { Recovery=Recovery+ticks },
        LoopCostPhase.Pacing => this with { Pacing=Pacing+ticks },
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };
}
public readonly record struct LoopCostSample(ulong Sequence, ulong FrameId, LoopCostOutcome Outcome,
    long StartedTimestamp, long EndedTimestamp, long OwnerAllocatedBytes, LoopPhaseTicks Phases,
    long? PresentReturnTimestamp, long? PresentReturnIntervalTicks, long? PacingAdmissionTimestamp);
public sealed record LoopCostReport(int Version, long TimestampFrequency, int Capacity, ulong TotalIterations,
    ulong OmittedIterations, ulong PresentReturns, ulong PresentIntervals, long MaximumPresentIntervalTicks,
    LoopCostSample[] Samples, int? FrameRateLimit)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public LoopStreamReport? Stream { get; init; }
    public string Scope => "owner-loop-wall-including-observation; present-return-not-display; startup/shutdown excluded";
    public bool TargetPerformanceAccepted => false;
    public bool CompleteRawCoverage => OmittedIterations == 0;
}

// Local, default-off diagnostics. Never a frame clock, pacing policy, GPU wait or Agent capability.
// Retain the FIRST bounded prefix, expose all omissions, and keep interval counters across skipped
// iterations, failures and recovery. No percentile/FPS claim is made for incomplete coverage.
public sealed class LoopCostCapture
{
    public const int MaximumCapacity = 512;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly LoopCostSample[] _samples;
    private readonly LoopCostStream? _stream;
    private int _count;
    private bool _active;
    private ulong _total, _presents, _intervals;
    private long _start, _allocation, _phaseStart, _maximumInterval;
    private long? _lastPresent, _present, _interval, _pacingAdmission;
    private LoopCostPhase _phase;
    private LoopPhaseTicks _phases;
    public int? FrameRateLimit { get; }
    public LoopCostCapture(int capacity = MaximumCapacity, int? frameRateLimit = null, LoopCostStream? stream = null)
    {
        if (capacity is < 1 or > MaximumCapacity) throw new ArgumentOutOfRangeException(nameof(capacity));
        if(frameRateLimit is int rate)FramePacer.ValidateRate(rate);
        FrameRateLimit=frameRateLimit;
        _samples=new LoopCostSample[capacity];
        stream?.Attach(frameRateLimit); _stream=stream;
    }
    private void Verify()
    {
        if(Environment.CurrentManagedThreadId!=_owner)throw new InvalidOperationException("Loop costs require the owner thread.");
    }
    public void Begin()
    {
        Verify();
        if(_active)throw new InvalidOperationException("Loop observation already active.");
        _allocation=GC.GetAllocatedBytesForCurrentThread();
        _start=_phaseStart=Stopwatch.GetTimestamp();_phase=LoopCostPhase.Control;
        _phases=default;_present=_interval=_pacingAdmission=null;_active=true;
    }
    public void Phase(LoopCostPhase phase)
    {
        Verify();
        if(!_active)throw new InvalidOperationException("No active loop observation.");
        if(!Enum.IsDefined(phase))throw new ArgumentOutOfRangeException(nameof(phase));
        long now=Stopwatch.GetTimestamp();
        _phases=_phases.Add(_phase,now-_phaseStart);_phaseStart=now;_phase=phase;
    }
    // Called ONLY immediately after a successful native Present return, including a clear-only
    // present followed by a host fault. Suspended/Null/failed Present calls do not fabricate samples.
    public void PresentReturned()
    {
        Verify();
        if(!_active||_present is not null)throw new InvalidOperationException("One successful Present per loop observation required.");
        long now=Stopwatch.GetTimestamp();_present=now;_presents++;
        if(_lastPresent is long prior) {
            _interval=now-prior;_intervals++;_maximumInterval=Math.Max(_maximumInterval,_interval.Value);
        }
        _lastPresent=now;
    }
    public void PacingAdmitted(long timestamp)
    {
        Verify();
        if(!_active||FrameRateLimit is null||_pacingAdmission is not null||
            timestamp<_start||timestamp>Stopwatch.GetTimestamp())
            throw new InvalidOperationException("Exact current-loop pacing admission required.");
        _pacingAdmission=timestamp;
    }
    public void End(ulong frameId, LoopCostOutcome outcome)
    {
        Verify();
        if(!_active)throw new InvalidOperationException("No active loop observation.");
        if(!Enum.IsDefined(outcome))throw new ArgumentOutOfRangeException(nameof(outcome));
        long end=Stopwatch.GetTimestamp(), allocated=GC.GetAllocatedBytesForCurrentThread()-_allocation;
        _phases=_phases.Add(_phase,end-_phaseStart);_total++;
        var sample=new LoopCostSample(_total,frameId,outcome,_start,end,allocated,_phases,_present,_interval,_pacingAdmission);
        if(_count<_samples.Length)_samples[_count++]=sample;
        _active=false;
        _stream?.Publish(sample);
    }
    public LoopCostReport Capture()
    {
        Verify();
        if(_active)throw new InvalidOperationException("Cannot snapshot an active loop.");
        return new(2,Stopwatch.Frequency,_samples.Length,_total,_total-(ulong)_count,
            _presents,_intervals,_maximumInterval,_samples.AsSpan(0,_count).ToArray(),FrameRateLimit) { Stream=_stream?.Snapshot() };
    }
}
