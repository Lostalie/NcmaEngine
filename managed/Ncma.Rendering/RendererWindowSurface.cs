using Ncma.Interop;
using Ncma.Platform;

namespace Ncma.Rendering;

public enum RenderWindowPhase { Ready, Suspended, Faulted, Closing, Closed }
public readonly record struct RenderWindowFault(ulong RendererGeneration, ulong Frame, PluginResult? NativeResult,
    string PluginId, string Operation, string Code, NativeFailureDiagnostic? FirstNativeFailure=null);
public readonly record struct RenderWindowStatus(RenderWindowPhase Phase, ulong RendererGeneration,
    uint Width, uint Height, uint RequestedWidth, uint RequestedHeight, bool RequiresNewGeneration, RenderWindowFault? Failure);

// Shared C# host policy; owns no window, World or GPU resource. A zero/minimized
// observation suspends presentation without resizing to zero or retiring resources.
// Recovery is an explicit trusted owner action, never a render-loop retry or Agent grant.
public sealed class RendererWindowSurface
{
    private readonly int _thread=Environment.CurrentManagedThreadId;
    private readonly RendererSession _renderer;
    private readonly ulong _generation;
    private RenderWindowPhase _phase;
    private uint _width,_height,_requestedWidth,_requestedHeight;
    private ulong _frame;
    private bool _requiresNewGeneration;
    private RenderWindowFault? _failure;

    public RendererWindowSurface(RendererSession renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);_renderer=renderer;
        var frame=renderer.ReadFrameState();var stats=renderer.Stats;
        if(frame.Active||frame.Failed)throw new InvalidOperationException("Window surface requires a healthy off-frame renderer.");
        _generation=frame.Generation;_frame=frame.Frame;_width=_requestedWidth=stats.Width;_height=_requestedHeight=stats.Height;
    }
    public RenderWindowStatus Status {get{VerifyThread();return new(_phase,_generation,_width,_height,_requestedWidth,_requestedHeight,_requiresNewGeneration,_failure);}}
    public static bool CanPresent(WindowState state)=>state.Minimized==0&&state.FramebufferWidth>0&&state.FramebufferHeight>0;
    private void VerifyThread(){if(_thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Window surface requires its owner thread.");}
    private void VerifyOpen(){VerifyThread();if(_phase is RenderWindowPhase.Closing or RenderWindowPhase.Closed)throw new ObjectDisposedException(nameof(RendererWindowSurface));}

    public bool Prepare(WindowState state)
    {
        VerifyOpen();
        if(_phase==RenderWindowPhase.Faulted)throw new InvalidOperationException("Window surface fault retained; explicit recovery or complete shutdown is required.");
        return PrepareCore(state);
    }
    public bool Recover(WindowState state)
    {
        VerifyOpen();
        if(_phase!=RenderWindowPhase.Faulted)throw new InvalidOperationException("No failed window surface to recover.");
        if(_requiresNewGeneration)throw new InvalidOperationException("A failed renderer requires a new generation, not surface retry.");
        // Read and verify the exact original device. A stopped/lost renderer must
        // be closed and a NEW host-owned generation built from approved CPU assets.
        // This operation cannot turn a failed renderer back into a healthy one.
        return PrepareCore(state);
    }
    private bool PrepareCore(WindowState state)
    {
        _requestedWidth=state.FramebufferWidth;_requestedHeight=state.FramebufferHeight;
        try {
            var frame=_renderer.ReadFrameState();_frame=frame.Frame;
            if(frame.Generation!=_generation||frame.Failed){_requiresNewGeneration=true;throw new InvalidOperationException("Renderer generation is no longer usable.");}
            if(frame.Active)throw new InvalidOperationException("Window surface preparation requires an off-frame boundary.");
            if(!CanPresent(state)){_phase=RenderWindowPhase.Suspended;return false;}
            _renderer.Resize(state.FramebufferWidth,state.FramebufferHeight);
            _width=state.FramebufferWidth;_height=state.FramebufferHeight;_phase=RenderWindowPhase.Ready;return true;
        }catch(Exception error){RecordFailure(error);throw;}
    }
    public void RecordFailure(Exception error)
    {
        VerifyThread();ArgumentNullException.ThrowIfNull(error);
        if(_phase is RenderWindowPhase.Closing or RenderWindowPhase.Closed)return;
        if(_phase==RenderWindowPhase.Faulted)return; // Preserve the first failure, not a later close/retry exception.
        PluginException? native=error as PluginException;Exception cause=error;
        for(int depth=0;native is null&&depth<8&&cause.InnerException is {} inner;depth++){cause=inner;native=cause as PluginException;}
        // Bounded value-only diagnostic. Never retain exception/script objects,
        // stack traces, arbitrary callback messages or potentially private paths.
        static string Bounded(string value)=>value.Length<=128?value:value[..128];
        _failure=new(_generation,_frame,native?.Result,native is null?"":Bounded(native.PluginId),
            native is null?"presentation":Bounded(native.Phase),native is null?"host_presentation_failed":"native_presentation_failed");
        _requiresNewGeneration|=native?.Result==PluginResult.DeviceLost;
        try{var frame=_renderer.ReadFrameState();_frame=frame.Frame;_requiresNewGeneration|=frame.Failed;_failure=_failure.Value with{Frame=_frame};}catch{ /* original failure remains authoritative */ }
        try{_failure=_failure.Value with{FirstNativeFailure=_renderer.ReadFirstFailure()};}catch{ /* Diagnostics never replace the primary failure. */ }
        _phase=RenderWindowPhase.Faulted;
    }
    public void BeginShutdown()
    {
        VerifyThread();if(_phase!=RenderWindowPhase.Closed)_phase=RenderWindowPhase.Closing;
    }
    public void CompleteShutdown()
    {
        VerifyThread();if(_phase==RenderWindowPhase.Closed)return;
        if(!_renderer.IsClosed)throw new InvalidOperationException("Close the renderer and its resource owners before completing surface shutdown.");
        _phase=RenderWindowPhase.Closed;
    }
}
