using System.Diagnostics;
namespace Ncma.Rendering;

public enum CostSampleAttribution { NoValidSample, CurrentSourceFrame, EarlierSourceFrame }
public readonly record struct AttributedGpuCost(GpuWorkTimingSample Sample, CostSampleAttribution Attribution);
// These are selected native counters, not a count of every ABI call or all GPU uploads.
public readonly record struct PresentationCounters(ulong MainSubmissions,ulong Presents,ulong SkinBatches,
    ulong UiDrawSubmissions,ulong UiProductions,ulong PaletteBytes,ulong UiUploadedBytes,
    ulong MeshUploadedBytes,ulong ResourceUploadedBytes,ulong EnvironmentUploadedBytes,
    ulong SceneCopiedBytes,ulong SceneConstantBytes,ulong PostConstantBytes)
{
    internal static PresentationCounters Delta(PresentationCounters a,PresentationCounters b)=>checked(new(
        a.MainSubmissions-b.MainSubmissions,a.Presents-b.Presents,a.SkinBatches-b.SkinBatches,
        a.UiDrawSubmissions-b.UiDrawSubmissions,a.UiProductions-b.UiProductions,a.PaletteBytes-b.PaletteBytes,
        a.UiUploadedBytes-b.UiUploadedBytes,a.MeshUploadedBytes-b.MeshUploadedBytes,
        a.ResourceUploadedBytes-b.ResourceUploadedBytes,a.EnvironmentUploadedBytes-b.EnvironmentUploadedBytes,
        a.SceneCopiedBytes-b.SceneCopiedBytes,a.SceneConstantBytes-b.SceneConstantBytes,a.PostConstantBytes-b.PostConstantBytes));
}
public readonly record struct PresentationCostReport(ulong Generation,ulong HostFrame,bool Completed,
    bool IncludesDiagnosticCapture,double CpuWallMilliseconds,long OwnerThreadAllocatedBytes,
    PresentationCounters? Counters,AttributedGpuCost? Main,AttributedGpuCost? Skin,AttributedGpuCost? Ui,
    AttributedGpuCost? MainResolved=null,AttributedGpuCost? SkinResolved=null,AttributedGpuCost? UiResolved=null)
{
    public int Version=>2;
    public string Scope=>"presentation-call; excludes simulation/startup and observation overhead";
    public string CounterCoverage=>"selected native batch/copy/upload counters; not all ABI calls/uploads; UI draw submissions include offscreen production";
    public string GpuCoverage=>"current query and last resolved sample per domain; bounded copies, not complete history or additive whole-frame GPU time";
    public bool FullPerformanceAccepted=>false;
}

// Explicit opt-in host instrumentation. One copied result, no histories, resources,
// callbacks, synchronization, scene snapshots or live World access.
public sealed class PresentationCostCapture
{
    private readonly int _thread=Environment.CurrentManagedThreadId;
    private readonly RendererSession _renderer;
    private readonly ulong _generation;
    private PresentationCounters _before;
    private ulong _frame;
    private long _time,_allocated;
    private bool _active,_diagnostic;
    private PresentationCostReport? _last;
    public PresentationCostReport? Last {get{Verify();return _last;}}
    public PresentationCostCapture(RendererSession renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);_renderer=renderer;
        var state=renderer.ReadFrameState();if(state.Active||state.Failed)throw new InvalidOperationException("Cost capture requires healthy off-frame initialization.");
        _generation=state.Generation;
        _=ReadCounters();_=renderer.ReadGpuTimingSnapshot(GpuTimingDomain.MainSubmissionBeforePresent);
        _=renderer.ReadGpuTimingSnapshot(GpuTimingDomain.SkinDispatch);_=renderer.ReadGpuTimingSnapshot(GpuTimingDomain.UiProduction);
    }
    private void Verify(){if(_thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Cost capture requires its owner thread.");}
    public void Begin(ulong hostFrame,bool includesDiagnosticCapture=false)
    {
        Verify();if(_active||hostFrame==0||hostFrame<=_frame)throw new InvalidOperationException("Cost capture frame/reentry.");
        var state=_renderer.ReadFrameState();if(state.Generation!=_generation||state.Active||state.Failed||hostFrame<=state.Frame)throw new InvalidOperationException("Cost capture renderer identity/boundary.");
        var before=ReadCounters();_before=before;_frame=hostFrame;_diagnostic=includesDiagnosticCapture;
        _allocated=GC.GetAllocatedBytesForCurrentThread();_time=Stopwatch.GetTimestamp();_active=true;
    }
    public PresentationCostReport End()
    {
        Verify();if(!_active)throw new InvalidOperationException("No cost capture.");
        double elapsed=Stopwatch.GetElapsedTime(_time).TotalMilliseconds;long allocated=GC.GetAllocatedBytesForCurrentThread()-_allocated;
        var state=_renderer.ReadFrameState();if(state.Generation!=_generation||state.Active||state.Failed)throw new InvalidOperationException("Cost capture must finish off-frame on the same healthy renderer.");
        var counters=PresentationCounters.Delta(ReadCounters(),_before);
        var main=_renderer.ReadGpuTimingSnapshot(GpuTimingDomain.MainSubmissionBeforePresent);
        var skin=_renderer.ReadGpuTimingSnapshot(GpuTimingDomain.SkinDispatch);
        var ui=_renderer.ReadGpuTimingSnapshot(GpuTimingDomain.UiProduction);
        var result=new PresentationCostReport(_generation,_frame,true,_diagnostic,elapsed,allocated,counters,
            Attribute(main.Current),Attribute(skin.Current),Attribute(ui.Current),
            Attribute(main.LastResolved),Attribute(skin.LastResolved),Attribute(ui.LastResolved));
        _last=result;_active=false;return result;
    }
    // No native reads on a failure path; never overwrite the primary render exception.
    public void Abort()
    {
        Verify();if(!_active)return;
        _last=new(_generation,_frame,false,_diagnostic,Stopwatch.GetElapsedTime(_time).TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread()-_allocated,null,null,null,null);_active=false;
    }
    private AttributedGpuCost Attribute(GpuWorkTimingSample sample)
    {
        if(sample.Timing.Generation!=_generation||sample.Timing.OriginFrame>_frame)
            throw new InvalidOperationException("Cost sample belongs to another device or future frame.");
        return new(sample,!sample.Timing.IsValid?CostSampleAttribution.NoValidSample:
            sample.Timing.OriginFrame==_frame?CostSampleAttribution.CurrentSourceFrame:CostSampleAttribution.EarlierSourceFrame);
    }
    private PresentationCounters ReadCounters()
    {
        var main=_renderer.Stats;var skin=_renderer.SkinStats;var ui=_renderer.UiStats;var targets=_renderer.UiTargetStats;
        var mesh=_renderer.SceneStats;var resources=_renderer.ResourceStats;var env=_renderer.EnvironmentStats;
        var scene=_renderer.PipelineStats;var post=_renderer.PostStats;
        return new(main.SubmittedFrames,main.Presents,skin.Batches,ui.Submits,targets.Productions,skin.PaletteBytes,
            ui.UploadedBytes,mesh.UploadedBytes,resources.UploadedBytes,env.UploadedBytes,scene.CopiedBytes,scene.ConstantUploadBytes,post.ConstantUploadBytes);
    }
}
