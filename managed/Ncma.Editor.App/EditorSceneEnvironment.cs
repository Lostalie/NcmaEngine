using Ncma.Assets;
using Ncma.Assets.Runtime;
using Ncma.Gameplay;
using Ncma.Interop;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Scene;
using Ncma.Scene.Rendering;
namespace Ncma.Editor.App;

internal sealed unsafe partial class CandidatePresentation
{
    private SceneEnvironmentPreflight? _environmentStartup;
    private SceneEnvironmentPreflight? _playEnvironmentStartup;
    private bool _sceneSynchronizationOk;
    private (Guid World,ulong Revision,Guid Assets,Guid Play,Guid PlayAssets,Guid PlayWorld)? _failedSceneSync;
    private (SceneRenderSession Scene,EnvironmentLightingData Environment,Guid Assets,ulong Revision,uint Width,uint Height,float Exposure,float Ambient)? _failedEnvironment;
    private string? _scenePreparationError;
    private ulong? _failedEnvironmentCatalog;
    private void SynchronizeEnvironmentCatalog()
    {
        if(_editor?.Environments is not {} environments||_editor.Assets is not {} assets||_failedEnvironmentCatalog==assets.Clock.Revision)return;
        try { environments.Synchronize();_failedEnvironmentCatalog=null; }
        catch(Exception error)when(PreparationError(error)) {
            _failedEnvironmentCatalog=assets.Clock.Revision;environments.RevokeRead();
            _log!.Write("error","environment.catalog",error.Message,_correlation);
        }
    }
    private Action<byte[],uint,uint>? _verificationCapture;
    private IDisposable? ComposePreparedPlay(PlaySession play,PreparedSceneAssetLease? assets)
    {
        RequireGraphicsReady();
        if(_playEnvironmentStartup is not null)throw new InvalidOperationException("A retained Play startup preflight must close before another Play attempt.");
        PreparePlayHud(play);
        PreflightPost(play.Document.World,play.Document.CaptureSnapshot());
        if(_renderer is not null&&assets is not null) {
            _playEnvironmentStartup=new(_renderer,_sceneCache!);
            try{_playEnvironmentStartup.Prepare(play.Document.World,assets,play.Document.CaptureSnapshot(),()=>!_closed);}
            catch(Exception preparationFailure){
                try{_playEnvironmentStartup.Dispose();_playEnvironmentStartup=null;}
                catch(Exception closeFailure){throw new AggregateException("Play environment preparation and close failed; host retains preflight.",preparationFailure,closeFailure);}
                throw;
            }
            // Complete and retire the GPU-only probe BEFORE creating gameplay/solver state.
            _playEnvironmentStartup.Dispose();_playEnvironmentStartup=null;
        }
        _sceneRuntime=Ncma.Characters.ScenePlayRuntime.Compose(play,_physics??throw new InvalidOperationException("Physics service not initialized."),assets);_characterRuntime=_sceneRuntime.Characters;return _sceneRuntime;
    }
    private PreparedSceneAssetLease PrepareCurrentEnvironmentAssets(PreparedSceneAssetLease assets,SceneDocumentSnapshot snapshot)
    {
        var value=SceneEnvironmentState.Read(snapshot);if(!value.Enabled)return assets;
        if(assets.Assets.TryResolve(value.AssetId,AssetKind.Environment,out var found)&&found is RuntimeEnvironmentAsset env&&env.Generation==value.Generation&&env.ContentHash==value.ContentHash)return assets;
        // Only an explicit committed scene/resource change reaches here, before simulation/GUI GPU.
        _editor!.RefreshRenderAssets();return _editor.RenderAssets!;
    }
    private void ScenePreparationFailed(Exception error)
    {
        string message=error.GetType().Name+": "+error.Message;
        if(_scenePreparationError!=message)_log!.Write("error","render.scene_prepare",message,_correlation);
        _scenePreparationError=message;
    }
    private static bool PreparationError(Exception error)=>error is ArgumentException or InvalidOperationException or IOException or NotSupportedException or PluginException or RenderGraphException;
    private (Guid World,ulong Revision,Guid Assets,Guid Play,Guid PlayAssets,Guid PlayWorld) SceneSyncKey()=>
        (_editor!.Document.World.Identity,_editor.Document.Revision,_editor.RenderAssets?.Assets.Identity??Guid.Empty,
         _editor.Play?.SessionId??Guid.Empty,_editor.PlayRenderAssets?.Assets.Identity??Guid.Empty,_editor.Play?.Document.World.Identity??Guid.Empty);
    private void SynchronizeSceneResourcesChecked()
    {
        if(_renderer is null||_editor is null)return;
        var key=SceneSyncKey();if(!_sceneSynchronizationOk&&_failedSceneSync==key)return;
        try{
            // A new committed scene/resource request is the explicit recovery boundary.
            // Never retry on an unchanged failing stamp or overwrite a retained owner.
            if(_pendingScene is not null&&_failedSceneSync is {} failed&&failed!=key) {
                using var read=SceneEnvironmentState.BeginRead(_editor.Document.World,key.World);
                var frameState=_renderer.ReadFrameState();
                if(frameState.Active||frameState.Failed||_closed||SceneSyncKey()!=key)
                    throw new InvalidOperationException("Candidate retirement requires an unchanged request and a healthy off-frame renderer.");
                _pendingScene.Dispose();_pendingScene=null;
                if(SceneSyncKey()!=key)throw new InvalidOperationException("Scene changed during candidate retirement.");
            }
            SynchronizeSceneResources();_sceneSynchronizationOk=true;_failedSceneSync=null;
        }
        catch(Exception error)when(PreparationError(error)){_failedSceneSync=SceneSyncKey();_sceneSynchronizationOk=false;ScenePreparationFailed(error);}
    }
    private bool PrepareSceneEnvironmentView(SceneRenderSession scene,uint width,uint height,Guid camera,SceneCameraView? browser,RenderConfiguration config)
    {
        var assets=_editor!.Play is null?_editor.RenderAssets:_editor.PlayRenderAssets;
        if(assets is null)return false;
        try {
            var world=_editor.Play?.Document.World??_editor.Document.World;
            var value=SceneEnvironmentState.Read(world,scene.WorldId);
            var request=(scene,value,assets.Assets.Identity,world.Revision,width,height,config.Exposure,config.Ambient);
            if(_failedEnvironment==request)return false;
            if(scene.PreparedEnvironment!=value)scene.RefreshEnvironment(assets,()=>!_closed);
            scene.PrepareEnvironmentView(width,height,camera,browser,config.Exposure,config.Ambient);
            _failedEnvironment=null;_scenePreparationError=null;return true;
        }catch(Exception error)when(PreparationError(error)){
            ScenePreparationFailed(error);
            // Preserve installed resources but never submit/pick old pixels as a new World.
            try{var world=_editor.Play?.Document.World??_editor.Document.World;_failedEnvironment=(scene,SceneEnvironmentState.Read(world,scene.WorldId),assets.Assets.Identity,world.Revision,width,height,config.Exposure,config.Ambient);}catch(Exception e)when(e is ArgumentException or InvalidOperationException){_failedEnvironment=null;}
            return false;
        }
    }
    // Internal trusted automatic harness only; no CLI, IPC/MCP or human permission shortcut.
    internal Ncma.Editor.Services.EditorSessionOwner VerificationEditor=>_editor??throw new InvalidOperationException("Editor not started.");
    internal (uint Width,uint Height) VerificationViewport=>_viewportTarget is {} t?(t.Width,t.Height):throw new InvalidOperationException("Viewport not prepared.");
    internal void CaptureNextViewportForVerification(Action<byte[],uint,uint> capture)
    {ArgumentNullException.ThrowIfNull(capture);if(_verificationCapture is not null)throw new InvalidOperationException("Capture already pending.");_verificationCapture=capture;}
    private void CaptureVerificationViewport()
    {
        if(_verificationCapture is null)return;var target=_viewportTarget??throw new InvalidOperationException("Viewport not prepared.");
        var pixels=new byte[checked((int)(target.Width*target.Height*4))];_renderer!.CaptureTarget(target,pixels);
        var callback=_verificationCapture;_verificationCapture=null;callback(pixels,target.Width,target.Height);
    }
    internal RendererStats VerificationStats=>_renderer!.Stats;
    internal EditorPresenter VerificationPresenter=>_presenter??throw new InvalidOperationException("Presenter not started.");
}
