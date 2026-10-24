using System.Diagnostics;
using Ncma.Editor.Services;
using Ncma.Interop;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Scene.Rendering;
using Ncma.Gameplay;

namespace Ncma.Editor.App;

internal enum EditorGraphicsPhase { Ready, Retiring, Preparing, Failed }
internal readonly record struct EditorGraphicsReview(Guid Token, Guid Session, Guid DocumentSession, ulong Revision,
    ulong DocumentGeneration, Guid Assets, ulong RendererGeneration, ulong Frame, bool AbandonActiveFrame,
    PlayStatus? Play,ulong PlayRevision,Guid PlayAssets);

// Trusted host operation only. No MCP capability, file command, World replacement or second history.
// The loaded native modules and CPU pins outlive every replacement device and active Play.
internal sealed unsafe partial class CandidatePresentation
{
    private readonly int _graphicsOwner=Environment.CurrentManagedThreadId;
    private bool _graphicsRecoveryBusy,_graphicsRecoveryPoisoned,_graphicsFrameOpen,_graphicsShutdown;
    private EditorGraphicsPhase _graphicsPhase;
    private EditorGraphicsReview? _graphicsReview;
    private long _graphicsReviewTime;
    private string? _graphicsRecoveryFailure;
    private NativeFailureDiagnostic? _graphicsNativeFailure;
    private SceneAnimationSession? _retainedEditAnimation,_retainedPlayAnimation;
    internal EditorGraphicsPhase GraphicsPhase {get{VerifyGraphicsOwner();return _graphicsPhase;}}
    internal string? GraphicsRecoveryFailure {get{VerifyGraphicsOwner();return _graphicsRecoveryFailure;}}
    internal NativeFailureDiagnostic? GraphicsNativeFailure {get{VerifyGraphicsOwner();return _graphicsNativeFailure;}}
    private void RememberGraphicsNativeFailure(Exception? error=null)
    {
        if(_graphicsNativeFailure is not null)return;
        for(int depth=0;error is not null&&depth<8;depth++,error=error.InnerException)
            if(error is PluginException{NativeDiagnostic:{} diagnostic}){_graphicsNativeFailure=diagnostic;return;}
        try{_graphicsNativeFailure=_renderer?.ReadFirstFailure();}catch{ /* Never mask the original failure. */ }
    }
    internal bool CanReviewGraphicsRecovery {get{
        VerifyGraphicsOwner();
        return !_closed&&!_graphicsShutdown&&!cpuOnly&&!ReferencePreview&&!_graphicsFrameOpen&&_editor is not null&&
            RecoveryModeReady()&&_uiWorkspace?.HasDraft!=true&&_animationGraphs?.HasDraft!=true;
    }}
    private bool RecoveryModeReady()=>_editor is {} owner&&!owner.Edit!.State.EditBusy&&
        (owner.Play is {State:PlayState.Running or PlayState.Paused}?owner.Edit.State.Frozen:owner.Play is null&&!owner.Edit.State.Frozen);

    private void VerifyGraphicsOwner()
    {
        if(_graphicsOwner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Editor graphics require owner thread.");
        if(_graphicsRecoveryBusy){_graphicsRecoveryPoisoned=true;throw new InvalidOperationException("Editor graphics recovery cannot be reentered.");}
    }
    private void RequireGraphicsReady()
    {
        VerifyGraphicsOwner();ObjectDisposedException.ThrowIf(_closed,this);
        if(_graphicsShutdown||_graphicsPhase!=EditorGraphicsPhase.Ready)throw new InvalidOperationException("Editor graphics retained; explicit recovery or shutdown required.");
    }
    private RendererSession CurrentGraphicsRenderer(){RequireGraphicsReady();return _renderer??throw new InvalidOperationException("No renderer.");}
    private EditorGraphicsReview GraphicsStamp(Guid token,bool abandon)
    {
        var owner=_editor??throw new InvalidOperationException("Editor not started.");
        if(!RecoveryModeReady()||_uiWorkspace?.HasDraft==true||_animationGraphs?.HasDraft==true)
            throw new InvalidOperationException("Graphics recovery requires idle Edit or healthy active Play, without unfinished drafts.");
        var frame=_renderer?.ReadFrameState()??default;
        return new(token,owner.Edit!.SessionId,owner.Document.SessionId,owner.Document.Revision,
            owner.Edit!.DocumentGeneration,owner.RenderAssets?.Assets.Identity??Guid.Empty,frame.Generation,frame.Frame,abandon,
            owner.Play?.Status,owner.Play?.Scene.Revision??0,owner.PlayRenderAssets?.Assets.Identity??Guid.Empty);
    }
    internal EditorGraphicsReview ReviewGraphicsRecovery(bool abandonActiveFrame=false)
    {
        VerifyGraphicsOwner();ObjectDisposedException.ThrowIf(_closed,this);
        RememberGraphicsNativeFailure();
        if(_graphicsShutdown||cpuOnly||ReferencePreview||_graphicsFrameOpen)throw new InvalidOperationException("Scene graphics recovery requires an off-frame graphical Edit host, not terminal shutdown.");
        var frame=_renderer?.ReadFrameState()??default;
        if(frame.Active&&!abandonActiveFrame)throw new InvalidOperationException("Active incomplete frame requires explicit terminal abandonment approval.");
        var review=GraphicsStamp(Guid.NewGuid(),abandonActiveFrame);
        _graphicsReview=review;_graphicsReviewTime=Stopwatch.GetTimestamp();return review;
    }
    internal void RebuildGraphics(EditorGraphicsReview review)
    {
        VerifyGraphicsOwner();ObjectDisposedException.ThrowIf(_closed,this);
        if(_graphicsShutdown||_graphicsFrameOpen||_graphicsReview!=review||Stopwatch.GetElapsedTime(_graphicsReviewTime)>TimeSpan.FromSeconds(60)||GraphicsStamp(review.Token,review.AbandonActiveFrame)!=review)
            throw new InvalidOperationException("Stale or unapproved graphics recovery.");
        _graphicsReview=null; // single-use even on failure; retry requires a new exact review
        var editState=_editor!.Edit!.State;var play=_editor.Play;
        _graphicsRecoveryBusy=true;_graphicsRecoveryPoisoned=false;
        try {
            var replacementProjection=CreateOrReuseEditProjection();
            using var read=_editor!.Document.ReadOnly();
            _graphicsPhase=EditorGraphicsPhase.Retiring;
            ObserveResourceMemory(RendererMemoryCheckpoint.BeforeGraphicsRetirement);
            _presenter?.CancelInteraction();_presenter?.SetViewportFrame(null,0);
            SuspendSceneHudInput();_pointerWasFocused=false;Array.Clear(_held);Array.Clear(_pressed);Array.Clear(_released);
            play?.CancelPresentationInput();
            _verificationCapture=null;_verificationComposite=null;
            _windowSurface?.BeginShutdown();
            if(_renderer is {} old){var frame=old.ReadFrameState();if(frame.Active&&!frame.Failed){
                if(!review.AbandonActiveFrame)throw new InvalidOperationException("Unapproved incomplete frame.");
                old.AbandonFrameForShutdown(frame.Frame);
            }}
            RememberGraphicsNativeFailure();
            RetireGraphics();
            _graphicsPhase=EditorGraphicsPhase.Preparing;
            using(var playScope=play?.BeginPresentationRecovery()){
                PrepareReplacementGraphics(replacementProjection);playScope?.Validate();
            }
            if(_graphicsRecoveryPoisoned||!ReferenceEquals(_editor.Play,play)||_editor.Play?.Status!=review.Play||(_editor.Play?.Scene.Revision??0)!=review.PlayRevision||(_editor.PlayRenderAssets?.Assets.Identity??Guid.Empty)!=review.PlayAssets||_editor.Edit!.State!=editState||_editor.Document.SessionId!=review.DocumentSession||_editor.Document.Revision!=review.Revision||_editor.Edit.DocumentGeneration!=review.DocumentGeneration||(_editor.RenderAssets?.Assets.Identity??Guid.Empty)!=review.Assets)
                throw new InvalidOperationException("Graphics recovery request changed during preparation.");
            _playScene?.CompleteDeferredAnimationBinding();SynchronizeSceneHud();
            _graphicsPhase=EditorGraphicsPhase.Ready;
            ObserveResourceMemory(RendererMemoryCheckpoint.AfterGraphicsRebuild);
        }catch(Exception error){
            RememberGraphicsNativeFailure(error);
            _graphicsRecoveryFailure??=_graphicsPhase==EditorGraphicsPhase.Retiring?"graphics_retirement_failed":"graphics_preparation_failed";
            _graphicsPhase=EditorGraphicsPhase.Failed;throw;
        }finally{_graphicsRecoveryBusy=false;}
    }
    private void RetireGraphics()
    {
        // Every owner is a field before fallible work. Stop at the first failed release;
        // the same explicit review/retry path drains partial old OR replacement devices.
        if(_uiWorkspace is not null)_presenter?.AttachUi(_uiWorkspace,null);
        if(_animationGraphs is not null)_presenter?.AttachGraphAuthoring(_animationGraphs,null);
        _gui?.Dispose();_gui=null;
        _pendingScene?.Dispose();_pendingScene=null;_pendingEditProjection=null;
        // Undo changes authoring provenance before the next explicit bake. A stale
        // owned animation must be closed normally, not transferred as reusable CPU state.
        // A prior partial close is also no longer readable: resume Dispose directly. Any
        // CPU owner already transferred by this host remains in the retained fields.
        if(_retainedPlayAnimation is null&&_playScene is {} playScene&&
            playScene.PreparationPhase==ScenePreparationPhase.Ready&&
            playScene.WorldId==_editor?.Play?.Scene.World.Identity&&
            playScene.Animation?.AssetPublication==_editor?.PlayRenderAssets?.Assets.Identity)
            _retainedPlayAnimation=playScene.RetainAnimationForRecovery();
        if(_retainedEditAnimation is null&&_editScene is {} editScene&&
            editScene.PreparationPhase==ScenePreparationPhase.Ready&&
            CurrentEditProjection is {} editProjection&&editScene.WorldId==editProjection.Scene.World.Identity&&
            editScene.Animation?.AssetPublication==_editor!.RenderAssets?.Assets.Identity)
            _retainedEditAnimation=editScene.RetainAnimationForRecovery();
        _playScene?.Dispose();_playScene=null;
        _editScene?.Dispose();_editScene=null;
        // Explicit document/asset replacement invalidates those clocks under the existing
        // snapshot contract. Release the obsolete CPU owner after its last GPU borrower.
        if(_retainedEditAnimation is {} editAnimation&&
            (CurrentEditProjection is not {} currentProjection||editAnimation.WorldId!=currentProjection.Scene.World.Identity||editAnimation.AssetPublication!=(_editor!.RenderAssets?.Assets.Identity??Guid.Empty))){
            editAnimation.Dispose();_retainedEditAnimation=null;
        }
        if(_retainedPlayAnimation is {} playAnimation&&
            (playAnimation.WorldId!=_editor?.Play?.Scene.World.Identity||playAnimation.AssetPublication!=(_editor.PlayRenderAssets?.Assets.Identity??Guid.Empty))){
            playAnimation.Dispose();_retainedPlayAnimation=null;
        }
        _environmentStartup?.Dispose();_environmentStartup=null;
        _playEnvironmentStartup?.Dispose();_playEnvironmentStartup=null;
        _pipelineStartup?.Dispose();_pipelineStartup=null;
        _postRuntime?.Dispose();_postRuntime=null;_highShadows=null;
        _graphPreview?.Dispose();_graphPreview=null;
        // Gameplay/UI CPU state is independent from the failing renderer. Do not replace widgets
        // or animation clocks; input cancellation above revokes only transient proposals/focus.
        _playHud?.RetireGraphics();_sceneHud?.RetireGraphics();
        while(_hudRetained.Count!=0){_hudRetained[0].Dispose();_hudRetained.RemoveAt(0);}
        _uiPreview?.Dispose();_uiPreview=null;
        _pendingViewportTarget?.Dispose();_pendingViewportTarget=null;
        _viewportTarget?.Dispose();_viewportTarget=null;
        _sceneCache?.Dispose();_sceneCache=null;
        _renderService?.Dispose();_renderService=null;
        ObserveResourceMemory(RendererMemoryCheckpoint.BeforeRendererClose);
        _renderer?.Dispose();_windowSurface?.CompleteShutdown();_renderer=null;
        // Keep shader/post/shadow/scene/HUD/font CPU pins, numerical kernels, platform,
        // loader, workspace participants, script catalog and the exact sole EditSession.
    }
    private void PrepareReplacementGraphics(EditorSceneProjection projection)
    {
        var state=_window!.State;
        uint width=Math.Clamp(state.FramebufferWidth,1,8192),height=Math.Clamp(state.FramebufferHeight,1,8192);
        _renderer=new(_loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),_window,width,height);
        _windowSurface=new(_renderer);
        _shaderFiles?.InstallSelection(_renderer,()=>!_closed&&!_graphicsRecoveryPoisoned);
        _renderService=new(_renderer);_sceneCache=new(_renderer);
        string font=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"msyh.ttc");
        _gui=new(_loader.Modules.Single(m=>m.Kind==ModuleKind.Gui),_window,_preferences?.Current.FontPath??font,fontSize:_preferences?.Current.FontSize??18);
        _gui.AttachRenderer(_renderer);_gui.SetToolbarIcon(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"NcmaEngine.ico")));
        if(_uiWorkspace is not null){_uiPreview=new(_renderer,plugins,_uiTextResources);_presenter?.AttachUi(_uiWorkspace,_uiPreview);}
        if(_animationGraphs is not null){_graphPreview=new(_renderer,()=>_poseKernel??=new(Path.Combine(plugins,"NcmaAnimationKernel.dll"),Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(plugins,"NcmaAnimationKernel.dll"))))));_presenter?.AttachGraphAuthoring(_animationGraphs,_graphPreview);}
        _editAssets=_playAssets=_playSession=Guid.Empty;_configurationRevision=ulong.MaxValue;_configurationWorld=Guid.Empty;
        _graph=null;_viewWidth=_viewHeight=0;_failedSceneSync=null;_failedEnvironment=null;
        _sceneSynchronizationOk=false;
        projection.RequireCurrent(_editor!);_editProjection=projection;
        var world=projection.Scene.World;var snapshot=projection.Scene.CaptureSnapshot();
        PreflightPost(world,snapshot);
        if(_editor!.RenderAssets is {} assets){_environmentStartup=new(_renderer,_sceneCache);_environmentStartup.Prepare(world,assets,snapshot,()=>!_closed&&!_graphicsRecoveryPoisoned);}
        if(_sceneHudPackage is not null){_sceneHud??=new(_uiTextResources);_sceneHud.Prepare(_sceneHudPackage,_renderer,plugins,new(width,height,1,default),interactive:uiActions is not null);}
        if(_playHud is not null){
            if(!ReferenceEquals(_playHudOwner,_editor.Play)||_sceneHudPackage is null)throw new InvalidOperationException("Retained Play HUD owner changed.");
            _playHud.Prepare(_sceneHudPackage,_renderer,plugins,new(width,height,1,default),interactive:true);
        }
        if(_editor.Play is {} play){
            var playSnapshot=play.Scene.CaptureSnapshot();PreflightPost(play.Scene.World,playSnapshot);
            if(_editor.PlayRenderAssets is {} playAssets){_playEnvironmentStartup=new(_renderer,_sceneCache);_playEnvironmentStartup.Prepare(play.Scene.World,playAssets,playSnapshot,()=>!_closed&&!_graphicsRecoveryPoisoned);}
        }
        _hudReady=true;_hudRecovery=false;_hudObservedDocument=Guid.Empty;_hudObservedRevision=0;
        _renderService.Configure(ClearConfiguration(_configuration),960,680);
        SynchronizeSceneResources();_environmentStartup?.Dispose();_environmentStartup=null;_playEnvironmentStartup?.Dispose();_playEnvironmentStartup=null;
        _sceneSynchronizationOk=true;_scenePreparationError=null;
    }
}
