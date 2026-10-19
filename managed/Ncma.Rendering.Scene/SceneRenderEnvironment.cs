using Ncma.Assets;
using Ncma.Assets.Runtime;
using Ncma.Scene.Rendering;
namespace Ncma.Rendering.Scene;

public sealed partial class SceneRenderSession
{
    private bool _hasGeometry,_hasSkin,_environmentPreparing;
    private Func<bool>? _environmentApproval;
    private SceneEnvironmentRuntimeShaders? _environmentShaders;
    private RenderAssetLease<GpuEnvironmentResource>? _environmentGpu;
    private PreparedSceneAssetLease? _environmentAssets;
    private EnvironmentLightingData _environment=EnvironmentLightingData.Off,_environmentObserved=EnvironmentLightingData.Off;
    private ulong _environmentObservedRevision;
    public EnvironmentLightingData PreparedEnvironment { get { Verify();return _environment; } }
    public SceneEnvironmentRuntimeShaders? PreparedEnvironmentShaders { get { Verify();return _environmentShaders; } }
    private bool EnvironmentApproved()
    { bool result=_environmentPreparing&&!_disposed&&_environmentApproval!();_lifetime.CheckCallback();return result; }
    private void VerifyPreparedEnvironment()
    {
        SceneEnvironmentState.VerifyBoundary(_world,WorldId);
        if(_environmentObservedRevision!=_world.Revision) {
            var value=SceneEnvironmentState.Read(_environmentReader,WorldId);
            _environmentObserved=value;_environmentObservedRevision=_world.Revision;
        }
        if(_hasGeometry&&_environmentObserved!=_environment)
            throw new InvalidOperationException("Environment configuration requires explicit off-frame refresh.");
    }
    private static EnvironmentPackage RequireEnvironment(PreparedSceneAssetLease assets,EnvironmentLightingData value)
    {
        var asset=(RuntimeEnvironmentAsset)assets.Assets.Require(value.AssetId,AssetKind.Environment);
        if(asset.Generation!=value.Generation||asset.ContentHash!=value.ContentHash)
            throw new ArgumentException("Exact prepared environment identity/generation/hash required.");
        return asset.Package;
    }
    private void InitializeEnvironment(PreparedSceneAssetLease assets,EnvironmentLightingData value,SceneEnvironmentRuntimeShaders? shaders,Func<bool> approval)
    {
        using var read=SceneEnvironmentState.BeginRead(_world,WorldId);_environmentPreparing=true;_environmentApproval=approval;
        try {
            _renderer.VerifyEnvironmentPreparation(EnvironmentApproved);
            var candidate=shaders??SceneEnvironmentRuntimeShaders.PrepareDefault(_renderer,_hasSkin,EnvironmentApproved);
            if(candidate.Unshadowed.Package.Skinning!=_hasSkin)throw new ArgumentException("Environment shader skin closure mismatch.");
            candidate.VerifyFor(_renderer);
            if(value.Enabled){var package=RequireEnvironment(assets,value);_environmentAssets=assets.AcquireLease();_environmentGpu=_cache.AcquireEnvironment(package,EnvironmentApproved);}
            _environmentShaders=candidate;_environment=value;
        }finally{_environmentPreparing=false;}
    }
    // Exact committed values/owned generations only; caller must be the trusted host outside
    // simulation and renderer frame. No file reads, Cook, World mutation or Agent grants here.
    public void RefreshEnvironment(PreparedSceneAssetLease assets,Func<bool> preparationAllowed)
    {
        RequireExchangeReady();ArgumentNullException.ThrowIfNull(assets);ArgumentNullException.ThrowIfNull(preparationAllowed);
        if(_environmentPreparing)throw new InvalidOperationException("Nonreentrant scene environment preparation.");
        using var read=SceneEnvironmentState.BeginRead(_world,WorldId);
        var value=SceneEnvironmentState.Read(_environmentReader,WorldId);
        if(!_hasGeometry){_environmentObserved=value;_environmentObservedRevision=_world.Revision;return;}
        BeginCandidateBundle();var previousApproval=_environmentApproval;_environmentApproval=preparationAllowed;_environmentPreparing=true;bool committed=false;
        try {
            _renderer.VerifyEnvironmentPreparation(EnvironmentApproved);
            if(value==_environment && (!value.Enabled||_environmentAssets?.Assets.Identity==assets.Assets.Identity)) {
                _environmentObserved=value;_environmentObservedRevision=_world.Revision;return;
            }
            var shaders=_environmentShaders;
            if(value.Enabled) {
                var package=RequireEnvironment(assets,value);
                shaders??=SceneEnvironmentRuntimeShaders.PrepareDefault(_renderer,_hasSkin,EnvironmentApproved);
                if(_hasSkin&&_shaders is not null&&!_shaders.Unshadowed.Package.CopyBytecode(RuntimeShaderRole.SkinCompute)
                    .SequenceEqual(shaders.Unshadowed.Package.CopyBytecode(RuntimeShaderRole.SkinCompute)))
                    throw new ArgumentException("Environment promotion must retain the existing shared skin kernel.");
                shaders.VerifyFor(_renderer);_pendingEnvironmentAssets=assets.AcquireLease();_pendingEnvironmentGpu=_cache.AcquireEnvironment(package,EnvironmentApproved);
            }
            if(_pipeline is not null&&_postRuntime is null) {
                if(_environmentShaders is null&&shaders is not null) {
                    CreateEnvironmentPipeline(shaders,_pendingEnvironmentGpu,value,_width,_height,_exposure,_ambient,_shadows,_shadowPrepared);
                    InstallPendingPipeline();
                } else if(_environmentShaders is not null) _pipeline.ConfigureEnvironment(_pendingEnvironmentGpu?.Resource,value.ToConfiguration(),EnvironmentApproved);
            }
            // All fallible GPU publication/binding happens above. Lease releases below are managed
            // reference decrements/owned file closes; native retirement remains in explicit cache Trim.
            var oldGpu=_environmentGpu;var oldCpu=_environmentAssets;
            _environmentGpu=_pendingEnvironmentGpu;_pendingEnvironmentGpu=oldGpu;
            _environmentAssets=_pendingEnvironmentAssets;_pendingEnvironmentAssets=oldCpu;
            _environmentShaders=shaders;_environment=value;_environmentObserved=value;_environmentObservedRevision=_world.Revision;
            committed=true;ReleasePendingResources();
        } catch(Exception failure) { if(!committed)_environmentApproval=previousApproval;AbandonCandidate(failure);throw; }
        finally{_environmentPreparing=false;}
    }
    private void CreateEnvironmentPipeline(SceneEnvironmentRuntimeShaders shaders,RenderAssetLease<GpuEnvironmentResource>? gpu,
        EnvironmentLightingData value,uint width,uint height,float exposure,float ambient,bool shadows,SceneShadowData? selection)
    {
        shaders.VerifyFor(_renderer);
        var programs=SelectShadowPrograms(null,shadows?shaders.Shadowed:shaders.Unshadowed,shadows,selection);
        if(_pendingPipeline is not null)throw new InvalidOperationException("A pipeline candidate is already retained.");
        _pendingPipeline=new ScenePipelineSession(_renderer,CreateSelectedPipeline(exposure,ambient,shadows,selection,ShaderProfile.SceneEnvironment),width,height,
            environmentShaders:programs.Environment);
        _pendingPipeline.ConfigureEnvironment(gpu?.Resource,value.ToConfiguration(),EnvironmentApproved);
    }
    public void PrepareEnvironmentView(uint width,uint height,Guid sceneCamera,SceneCameraView? browserCamera=null,float exposure=1,float ambient=.03f)
    {
        RequireExchangeReady();if(_environmentPreparing)throw new InvalidOperationException("Nonreentrant scene environment preparation.");
        VerifyPreparedEnvironment();var selection=ObserveShadowSelection();if(_postRuntime is not null){PreparePostView(width,height,sceneCamera,browserCamera,exposure,ambient);return;}
        if(_environmentShaders is null&&_shaders is null)return;
        // Preserve the original lazy default path when no scene policy is selected.
        // An explicit->absent transition still prepares once to retire its old graph.
        // Do not extract an extra view/allocate read scopes for ordinary legacy callers.
        if(_environmentShaders is null&&!_hasCustomPipeline&&selection is null&&_shadowPrepared is null)return;
        using var read=SceneEnvironmentState.BeginRead(_world,WorldId);_environmentPreparing=true;
        try {
            _renderer.VerifyEnvironmentPreparation(EnvironmentApproved);
            var view=_extractor.Extract(_resources.Metadata,browserCamera is null?sceneCamera:Guid.Empty,width,height,browserCamera,_animation is not null);
            if(view.Camera is not {} camera||view.Geometry.Count==0&&view.ShadowCasters.Count==0)return;
            bool shadows=SceneShadowPipelinePolicy.EffectiveShadows(selection,view.PrimaryLight is {Data.CastShadow:true});
            uint w=Math.Max(1,(uint)(width*camera.Data.ViewportWidth)),h=Math.Max(1,(uint)(height*camera.Data.ViewportHeight));
            if(_pipeline is null||_width!=w||_height!=h||_shadows!=shadows||_shadowPrepared!=selection) {
                BeginCandidateBundle();
                try {
                    if(_environmentShaders is not null)CreateEnvironmentPipeline(_environmentShaders,_environmentGpu,_environment,w,h,exposure,ambient,shadows,selection);
                    else _pendingPipeline=new ScenePipelineSession(_renderer,CreateSelectedPipeline(exposure,ambient,shadows,selection,ShaderProfile.Scene3D),w,h,
                        shaders:SelectShadowPrograms(shadows?_shaders!.Shadowed:_shaders!.Unshadowed,null,shadows,selection).Scene);
                    _renderer.VerifyEnvironmentPreparation(EnvironmentApproved);InstallPendingPipeline();
                }catch(Exception failure){AbandonCandidate(failure);throw;}
                _width=w;_height=h;_shadows=shadows;_exposure=exposure;_ambient=ambient;
            } else if(_exposure!=exposure||_ambient!=ambient){
                _pipeline.Configure(CreateSelectedPipeline(exposure,ambient,shadows,selection,_environmentShaders is null?ShaderProfile.Scene3D:ShaderProfile.SceneEnvironment));_exposure=exposure;_ambient=ambient;
            }
            CommitShadowSelection(selection);
        }finally{_environmentPreparing=false;}
    }
    private void ReleaseEnvironmentLeases()
    { _environmentGpu?.Dispose();_environmentGpu=null;_environmentAssets?.Dispose();_environmentAssets=null; }
}
