using System.Diagnostics;
using Ncma.Interop;

namespace Ncma.Rendering;

public enum RendererMemoryCheckpoint { BeforeStartup, RuntimeReady, BeforeShutdown, AfterShutdown, BeforeGraphicsRetirement, BeforeRendererClose, AfterGraphicsRebuild }
public enum RendererMemoryState { NoRenderer, Available, Unsupported, ReadFailed }
public sealed record RendererMemorySample(ulong Sequence, RendererMemoryCheckpoint Checkpoint, RendererMemoryState State,
    long StartedTimestamp, long EndedTimestamp, long OwnerAllocatedBytes, RendererResourceLedger? Ledger,
    string? FailureType, PluginResult? NativeResult);
public sealed record RendererMemoryReport(long TimestampFrequency, ulong TotalObservations, ulong OmittedObservations, RendererMemorySample[] Samples)
{
    public int Version => 1;
    public bool CompleteRawCoverage => OmittedObservations==0;
    public bool CompleteLifetimeCoverage => false;
    public bool MemoryAccepted => false;
    public string Scope => "explicit bounded host lifecycle/recovery observations, not per-frame; no renderer/unsupported/read failure are not zero; " +
        "AfterShutdown is host-service shutdown, not process exit; omitted suffix never sampled; no retry/GC/GPU waits; " +
        "observer wall/allocation excludes final sample allocation and report copying/serialization; logical domains overlap";
}

// Optional diagnostics only: no dependency from the generic Application layer on rendering.
public sealed class RendererMemoryCapture
{
    public const int Capacity=32;
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private readonly RendererMemorySample[] _samples=new RendererMemorySample[Capacity];
    private int _count;private ulong _observations;
    private void Verify(){if(Environment.CurrentManagedThreadId!=_owner)throw new InvalidOperationException("Renderer memory capture requires owner thread.");}
    public void Observe(RendererMemoryCheckpoint checkpoint,RendererSession? renderer)
    {
        Verify();if(!Enum.IsDefined(checkpoint))throw new ArgumentOutOfRangeException(nameof(checkpoint));
        ulong sequence=checked(++_observations);if(_count==Capacity)return;
        long allocated=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
        RendererResourceLedger? ledger=null;RendererMemoryState state=RendererMemoryState.NoRenderer;
        string? failure=null;PluginResult? result=null;
        if(renderer is not null) {
            try{ledger=renderer.CaptureResourceLedger();state=RendererMemoryState.Available;}
            catch(PluginException error){result=error.Result;failure=nameof(PluginException);state=error.Result is PluginResult.AbiMismatch or PluginResult.UnsupportedFeature?RendererMemoryState.Unsupported:RendererMemoryState.ReadFailed;}
            catch(NotSupportedException error){failure=error.GetType().Name;state=RendererMemoryState.Unsupported;}
            catch(InvalidOperationException error){failure=error.GetType().Name;state=RendererMemoryState.ReadFailed;}
        }
        long end=Stopwatch.GetTimestamp(),bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
        _samples[_count++]=new(sequence,checkpoint,state,start,end,bytes,ledger,failure,result);
    }
    public RendererMemoryReport Capture()
    {
        Verify();var copy=new RendererMemorySample[_count];
        for(int i=0;i<copy.Length;i++){var sample=_samples[i];copy[i]=sample with{Ledger=sample.Ledger is {} ledger?ledger with{Domains=ledger.Domains.ToArray()}:null};}
        return new(Stopwatch.Frequency,_observations,_observations-(ulong)_count,copy);
    }
}
