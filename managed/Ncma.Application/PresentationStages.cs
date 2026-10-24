using System.Diagnostics;
namespace Ncma.Application;

public enum PresentationCall { BeginPresentation, Present }
public enum PresentationStage {
    Control, Observer, Surface, GuiBegin, GuiBuild, ViewPreparation, GuiDraw,
    SceneSubmission, GuiGpu, DiagnosticCapture, NativePresent, Cleanup, Diagnostics
}
public readonly record struct PresentationStageCost(PresentationStage Stage,int Visits,long WallTicks,long OwnerAllocatedBytes);
public readonly record struct PresentationCallCost(ulong Sequence,ulong FrameHint,PresentationCall Call,
    bool Completed,bool DiagnosticCaptureRequested,long StartedTimestamp,long EndedTimestamp,long OwnerAllocatedBytes,
    PresentationStageCost[] Stages);
public sealed record PresentationStageReport(int Version,long TimestampFrequency,int Capacity,ulong TotalCalls,
    ulong OmittedCalls,PresentationCallCost[] Calls)
{
    public bool CompleteRawCoverage=>OmittedCalls==0;
    public bool PerformanceAccepted=>false;
    public string Scope=>"host presentation call wall/owner allocation; sequential stages including nested work/observation; not GPU time or upload bytes; excludes call-entry JIT/readiness guards/startup/shutdown";
    public string Identity=>"sequence is unique; frame hint may repeat after suspend/fault/recovery, and is not a GPU source token";
}

// Owner-thread, explicit opt-in, bounded FIRST prefix. No renderer/World access,
// callbacks, file IO or resource ownership. Capture copies; it never drains rows.
public sealed class PresentationStageCapture
{
    public const int MaximumCalls=64;
    private const int StageCount=13;
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private readonly PresentationCallCost[] _headers;
    private readonly PresentationStageCost[] _costs;
    private readonly PresentationStageCost[] _working=new PresentationStageCost[StageCount];
    private ulong _total,_frame;
    private int _count;
    private bool _active,_diagnostic;
    private PresentationCall _call;
    private PresentationStage _phase;
    private long _start,_allocated,_phaseTime,_phaseAllocated;
    public PresentationStageCapture(int capacity=MaximumCalls)
    {
        if(capacity is <1 or >MaximumCalls)throw new ArgumentOutOfRangeException(nameof(capacity));
        _headers=new PresentationCallCost[capacity];_costs=new PresentationStageCost[capacity*StageCount];
    }
    private void Verify()
    {
        if(Environment.CurrentManagedThreadId!=_owner)throw new InvalidOperationException("Presentation stages require owner thread.");
    }
    public void Begin(ulong frameHint,PresentationCall call,bool diagnosticCaptureRequested=false)
    {
        Verify();
        if(_active||frameHint==0||!Enum.IsDefined(call))throw new InvalidOperationException("Invalid presentation stage boundary.");
        Array.Clear(_working);
        _call=call;_frame=frameHint;_diagnostic=diagnosticCaptureRequested;_phase=PresentationStage.Control;
        _working[0]=new(_phase,1,0,0);
        _allocated=_phaseAllocated=GC.GetAllocatedBytesForCurrentThread();
        _start=_phaseTime=Stopwatch.GetTimestamp();_active=true;
    }
    private void Flush(long now,long allocated)
    {
        int index=(int)_phase;var previous=_working[index];
        _working[index]=previous with{WallTicks=checked(previous.WallTicks+now-_phaseTime),
            OwnerAllocatedBytes=checked(previous.OwnerAllocatedBytes+allocated-_phaseAllocated)};
        _phaseTime=now;_phaseAllocated=allocated;
    }
    public void Phase(PresentationStage stage)
    {
        Verify();
        if(!_active||!Enum.IsDefined(stage))throw new InvalidOperationException("Invalid presentation stage.");
        Flush(Stopwatch.GetTimestamp(),GC.GetAllocatedBytesForCurrentThread());
        _phase=stage;int index=(int)stage;var previous=_working[index];
        _working[index]=previous with{Stage=stage,Visits=checked(previous.Visits+1)};
    }
    public void End(bool completed)
    {
        Verify();if(!_active)throw new InvalidOperationException("No presentation stage call.");
        long now=Stopwatch.GetTimestamp(),allocated=GC.GetAllocatedBytesForCurrentThread();
        Flush(now,allocated);_total=checked(_total+1);
        if(_count<_headers.Length){
            for(int i=0;i<StageCount;i++)_costs[_count*StageCount+i]=_working[i] with{Stage=(PresentationStage)i};
            _headers[_count++]=new(_total,_frame,_call,completed,_diagnostic,_start,now,allocated-_allocated,[]);
        }
        _active=false;
    }
    public PresentationStageReport Capture()
    {
        Verify();if(_active)throw new InvalidOperationException("Cannot capture an active presentation call.");
        var rows=new PresentationCallCost[_count];
        for(int i=0;i<_count;i++)rows[i]=_headers[i] with{Stages=_costs.AsSpan(i*StageCount,StageCount).ToArray()};
        return new(1,Stopwatch.Frequency,_headers.Length,_total,_total-(ulong)_count,rows);
    }
}
