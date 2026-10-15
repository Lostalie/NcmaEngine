using Ncma.Scene.Rendering;

namespace Ncma.Rendering.Scene;

public enum SceneResourceExchangeFailure { None, CandidateRetirement, InstalledRetirement, LeaseRetirement, Closing }
public readonly record struct SceneResourceExchangeStatus(bool RecoveryRequired, bool Closing, bool CandidateRetained,
    bool EnvironmentGpuRetained, bool EnvironmentAssetsRetained, SceneResourceExchangeFailure Failure);

public sealed partial class SceneRenderSession
{
    // At most one uninstalled/retiring bundle. Register ownership BEFORE any
    // fallible binding/approval; no local-only candidate can be lost by cleanup.
    private ScenePipelineSession? _pendingPipeline;
    private RenderAssetLease<GpuEnvironmentResource>? _pendingEnvironmentGpu;
    private PreparedSceneAssetLease? _pendingEnvironmentAssets;
    private bool _resourceRecoveryRequired, _closing, _recovering, _recoveryRejected;
    private SceneResourceExchangeFailure _exchangeFailure;
    public SceneResourceExchangeStatus ResourceExchangeStatus
    { get { _lifetime.VerifyOwner();ObjectDisposedException.ThrowIf(_disposed,this);return new(_resourceRecoveryRequired,_closing,_pendingPipeline is not null,_pendingEnvironmentGpu is not null,_pendingEnvironmentAssets is not null,_exchangeFailure); } }
    private void RequireExchangeReady()
    {
        Verify();
        if(_recovering){_recoveryRejected=true;throw new InvalidOperationException("Resource recovery cannot submit or prepare a scene.");}
        if(_closing||_resourceRecoveryRequired)throw new InvalidOperationException("Scene resource exchange requires explicit recovery or close retry.");
    }
    private void BeginCandidateBundle()
    {
        RequireExchangeReady();
        if(_pendingPipeline is not null||_pendingEnvironmentGpu is not null||_pendingEnvironmentAssets is not null)
            throw new InvalidOperationException("Only one scene candidate bundle is allowed.");
    }
    private void MarkExchangeFailure(SceneResourceExchangeFailure phase)
    { _resourceRecoveryRequired=true;_exchangeFailure=phase;_animationStamp=null;_submittedFrame=0;Costs=default; }
    private void CloseInstalledPipeline()
    {
        try{_pipeline?.Dispose();_pipeline=null;}
        catch{MarkExchangeFailure(SceneResourceExchangeFailure.InstalledRetirement);throw;}
    }
    private void InstallPendingPipeline()
    {
        if(_pendingPipeline is null)throw new InvalidOperationException("Missing complete scene candidate.");
        CloseInstalledPipeline();_pipeline=_pendingPipeline;_pendingPipeline=null;
    }
    private void ReleasePendingResources()
    {
        try{_pendingPipeline?.Dispose();_pendingPipeline=null;}
        catch{MarkExchangeFailure(SceneResourceExchangeFailure.CandidateRetirement);throw;}
        try{
            _pendingEnvironmentGpu?.Dispose();_pendingEnvironmentGpu=null;
            _pendingEnvironmentAssets?.Dispose();_pendingEnvironmentAssets=null;
        }catch{MarkExchangeFailure(SceneResourceExchangeFailure.LeaseRetirement);throw;}
    }
    private void AbandonCandidate(Exception preparationFailure)
    {
        // A failed retirement is not retried implicitly inside another catch.
        if(_resourceRecoveryRequired)return;
        try{ReleasePendingResources();}
        catch(Exception cleanupFailure){throw new AggregateException("Scene preparation and candidate retirement failed; ownership retained.",preparationFailure,cleanupFailure);}
    }
    // Trusted host operation only, no Agent command and no automatic retry. Drain
    // pending and installed GPU pipelines before allowing a fresh view preparation.
    // This does NOT replace a lost device or resume gameplay/a faulted WorldRunner.
    public void RecoverResourceExchange(Func<bool> preparationAllowed)
    {
        Verify();ArgumentNullException.ThrowIfNull(preparationAllowed);
        if(_recovering){_recoveryRejected=true;throw new InvalidOperationException("Nonreentrant resource recovery.");}
        if(_closing||_environmentPreparing)throw new InvalidOperationException("A closing/preparing scene cannot recover rendering.");
        if(!_resourceRecoveryRequired)throw new InvalidOperationException("No failed resource exchange to recover.");
        using var read=SceneEnvironmentState.BeginRead(_world,WorldId);
        _recovering=true;_environmentPreparing=true;_recoveryRejected=false;
        try {
            _renderer.VerifyEnvironmentPreparation(()=>{
                bool allowed=preparationAllowed();
                if(_recoveryRejected)throw new InvalidOperationException("Recovery callback violated scene ownership.");
                return allowed;
            });
            ReleasePendingResources();CloseInstalledPipeline();_cache.Trim();
            _width=0;_height=0;_postReady=false;_shadowReady=false;_submittedFrame=0;_animationStamp=null;Costs=default;
            _resourceRecoveryRequired=false;_exchangeFailure=SceneResourceExchangeFailure.None;
        }finally{_environmentPreparing=false;_recovering=false;_recoveryRejected=false;}
    }
}
