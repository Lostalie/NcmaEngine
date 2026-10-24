using System.Diagnostics;
using Ncma.Animation;
using Ncma.Gameplay;
using Ncma.Interop;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Scene.Rendering;
using Ncma.Ui.Rendering;

namespace Ncma.Player.App;

public readonly record struct PlayerGraphicsReview(Guid Token,PlayStatus Play,ulong SceneRevision,
    Guid Assets,ulong RendererGeneration,ulong Frame,bool AbandonActiveFrame);
public sealed record PlayerGraphicsRecoveryRequest(PlayerGraphicsReview Review,bool Retry,RenderWindowFault? Failure,NativeFailureDiagnostic? NativeDiagnostic=null)
{
    public string Message=>$"图形呈现失败。当前游戏状态 {Review.Play.State}，已提交 tick {Review.Play.Tick}。\n"+
        (NativeDiagnostic is {} d?$"设备生命周期首个诊断：操作 {d.Operation}，原生码 {(d.NativeCode is int code?$"0x{unchecked((uint)code):X8}":"无")}，观察帧 {d.Frame}。\n":"")+
        "是否丢弃未完成图形帧并重建 GPU？保留当前游戏状态，不重启脚本。\n选择“否”将退出，不会自动保存。";
}

// Host-local exact approval, not a World command, Agent grant or serializable recovery script.
internal sealed partial class PlayerPresentation
{
    private readonly int _graphicsThread=Environment.CurrentManagedThreadId;
    private bool _graphicsBusy,_graphicsRejected,_graphicsFailed,_terminalClose;
    private PlayerGraphicsReview? _graphicsReview;
    private long _graphicsReviewTime;
    private PlaySession? _boundPlay;
    private IRootMotionPresentation? _rootMotion;
    private SceneAnimatorRuntime? _animators;
    private RuntimeShaderFileSet? _shaderFiles;
    private RuntimePostProcessLibrary? _postLibrary;
    private PostProcessModules? _postModules;
    private RuntimeShadowLibrary? _shadowLibrary;
    private RuntimeUiPackage? _uiPackage;
    private SceneAnimationSession? _retainedAnimation;
    private RenderWindowFault? _firstGraphicsFailure;
    private NativeFailureDiagnostic? _firstNativeFailure;
    private ulong _retiredSubmitted,_retiredErrors,_retiredWarnings;
    internal ulong TotalSubmittedFrames=>checked(_retiredSubmitted+Stats.SubmittedFrames);
    internal ulong TotalValidationErrors=>checked(_retiredErrors+Stats.ValidationErrors);
    internal ulong TotalValidationWarnings=>checked(_retiredWarnings+Stats.ValidationWarnings);
    internal RenderWindowFault? FirstGraphicsFailure=>_firstGraphicsFailure;
    internal NativeFailureDiagnostic? FirstNativeFailure=>_firstNativeFailure;
    private void RememberNativeFailure(Exception? error=null)
    {
        if(_firstNativeFailure is not null)return;
        for(int depth=0;error is not null&&depth<8;depth++,error=error.InnerException)
            if(error is PluginException{NativeDiagnostic:{} diagnostic}){_firstNativeFailure=diagnostic;return;}
        try{_firstNativeFailure=_renderer?.ReadFirstFailure();}catch{ /* Original failure stays authoritative. */ }
    }
    private void VerifyGraphicsAccess()
    {
        if(Environment.CurrentManagedThreadId!=_graphicsThread)throw new InvalidOperationException("Player graphics require owner thread.");
        if(_graphicsBusy){_graphicsRejected=true;throw new InvalidOperationException("Player graphics recovery cannot be reentered.");}
    }
    private void RequireGraphicsReady()
    {
        VerifyGraphicsAccess();ObjectDisposedException.ThrowIf(_disposed,this);
        if(_terminalClose||_graphicsFailed)throw new InvalidOperationException("Player graphics require explicit recovery or shutdown.");
    }
    private PlayerGraphicsReview GraphicsStamp(Guid token,bool abandon)
    {
        var play=_boundPlay??throw new InvalidOperationException("No bound Player session.");
        if(play.State is not (PlayState.Running or PlayState.Paused)||_assets is null)throw new InvalidOperationException("Only healthy bound gameplay permits graphics recovery.");
        var frame=_renderer?.ReadFrameState()??default;
        return new(token,play.Status,play.Scene.Revision,_assets.Assets.Identity,frame.Generation,frame.Frame,abandon);
    }
    internal PlayerGraphicsReview ReviewGraphicsRecovery(bool abandonActiveFrame=false)
    {
        VerifyGraphicsAccess();ObjectDisposedException.ThrowIf(_disposed,this);
        RememberNativeFailure();
        if(_terminalClose)throw new InvalidOperationException("Terminal Player shutdown cannot resume graphics.");
        var frame=_renderer?.ReadFrameState()??default;
        if(frame.Active&&!abandonActiveFrame)throw new InvalidOperationException("Incomplete frame requires explicit abandonment approval.");
        var review=GraphicsStamp(Guid.NewGuid(),abandonActiveFrame);_graphicsReview=review;_graphicsReviewTime=Stopwatch.GetTimestamp();return review;
    }
    internal void RebuildGraphics(PlayerGraphicsReview review)
    {
        VerifyGraphicsAccess();ObjectDisposedException.ThrowIf(_disposed,this);
        if(_terminalClose||_graphicsReview!=review||Stopwatch.GetElapsedTime(_graphicsReviewTime)>TimeSpan.FromSeconds(60)||GraphicsStamp(review.Token,review.AbandonActiveFrame)!=review)
            throw new InvalidOperationException("Stale or unapproved Player graphics recovery.");
        _graphicsReview=null;_graphicsBusy=true;_graphicsRejected=false;_graphicsFailed=true;
        var play=_boundPlay!;
        try {
            ObserveResourceMemory(RendererMemoryCheckpoint.BeforeGraphicsRetirement);
            _uiInput?.Cancel();play.CancelPresentationInput();_focused=false;Array.Clear(_held);Array.Clear(_pressed);Array.Clear(_released);
            _windowSurface?.BeginShutdown();
            if(_renderer is {} old){var frame=old.ReadFrameState();if(frame.Active&&!frame.Failed){
                if(!review.AbandonActiveFrame)throw new InvalidOperationException("Unapproved incomplete frame.");
                old.AbandonFrameForShutdown(frame.Frame);
            }}
            RememberNativeFailure();
            if(_retainedAnimation is null&&_scene is {PreparationPhase:ScenePreparationPhase.Ready})_retainedAnimation=_scene.RetainAnimationForRecovery();
            _scene?.Dispose();_scene=null;
            _environmentPreflight?.Dispose();_environmentPreflight=null;_pipelineStartup?.Dispose();_pipelineStartup=null;
            _ui?.RetireGraphics();_cache?.Dispose();_cache=null;
            _postRuntime?.Dispose();_postRuntime=null;_highShadows=null;
            _pipeline?.Dispose();_pipeline=null;
            if(_renderer is {} retiring){var stats=retiring.Stats;ObserveResourceMemory(RendererMemoryCheckpoint.BeforeRendererClose);retiring.Dispose();
                _retiredSubmitted=checked(_retiredSubmitted+stats.SubmittedFrames);_retiredErrors=checked(_retiredErrors+stats.ValidationErrors);_retiredWarnings=checked(_retiredWarnings+stats.ValidationWarnings);
                _windowSurface?.CompleteShutdown();_renderer=null;
            }
            using(var scope=play.BeginPresentationRecovery()){
                uint width=Math.Clamp(_window!.State.FramebufferWidth,1,8192),height=Math.Clamp(_window.State.FramebufferHeight,1,8192);
                _renderer=new(_loader.Modules.Single(m=>m.Kind==Ncma.Interop.ModuleKind.Renderer),_window,width,height);_windowSurface=new(_renderer);
                bool Approved()=>!_disposed&&!_terminalClose&&!_graphicsRejected;
                _shaderFiles?.InstallSelection(_renderer,Approved);
                if(_shadowLibrary is not null)_highShadows=_shadowLibrary.Prepare(_renderer,_shaderFiles!,Approved);
                if(_postLibrary is not null)_postRuntime=new(_renderer,_shaderFiles!,_postLibrary,_postModules??PostProcessModules.Official(),Approved);
                _pipeline=new(_renderer);_cache=new(_renderer);
                var snapshot=play.Scene.CaptureSnapshot();
                _pipelineStartup=new(_renderer);_pipelineStartup.PreparePost(play.Scene.World,snapshot,_postRuntime);
                _pipelineStartup.Prepare(play.Scene.World,snapshot,pipelineFactory,_postRuntime,Approved,_highShadows);
                _environmentPreflight=new(_renderer,_cache);_environmentPreflight.Prepare(play.Scene.World,_assets!,snapshot,Approved);
                if(_ui is not null)_ui.Prepare(_uiPackage!,_renderer,plugins,new(width,height,1,default),interactive:_uiActions is not null);
                _scene=new(_renderer,_cache,play.Scene.World);
                _scene.Prepare(_assets!,snapshot,pipelineFactory:ScenePipelineStartup.Bind(_renderer,play.Scene.World,pipelineFactory,Approved),
                    poseKernel:_poseKernel,play:play,interpolateTransforms:Ncma.Characters.CharacterComponents.HasPhysics(snapshot),
                    rootMotion:_rootMotion,animators:_animators,environmentPreparationAllowed:Approved,postProcessing:_postRuntime,
                    highShadows:_highShadows,retainedAnimation:_retainedAnimation,deferPlayObserver:true);
                _environmentPreflight.Dispose();_environmentPreflight=null;_pipelineStartup.Dispose();_pipelineStartup=null;
                scope.Validate();
            }
            if(_graphicsRejected||!ReferenceEquals(play,_boundPlay)||play.Status!=review.Play||play.Scene.Revision!=review.SceneRevision||_assets!.Assets.Identity!=review.Assets)
                throw new InvalidOperationException("Player recovery state changed during preparation.");
            _scene.CompleteDeferredAnimationBinding();_uiInput?.Observe();_graphicsFailed=false;
            ObserveResourceMemory(RendererMemoryCheckpoint.AfterGraphicsRebuild);
        }catch(Exception error){RememberNativeFailure(error);throw;}finally{_graphicsBusy=false;}
    }
}
