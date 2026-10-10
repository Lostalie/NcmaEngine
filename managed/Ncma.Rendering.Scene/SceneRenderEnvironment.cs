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
    private bool EnvironmentApproved()=>_environmentPreparing&&!_disposed&&_environmentApproval!();
    private void VerifyPreparedEnvironment()
    {
        SceneEnvironmentState.VerifyBoundary(_world,WorldId);
        if(_environmentObservedRevision!=_world.Revision) {
            var value=SceneEnvironmentState.Read(_world,WorldId);
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
        Verify();ArgumentNullException.ThrowIfNull(assets);ArgumentNullException.ThrowIfNull(preparationAllowed);
        if(_environmentPreparing)throw new InvalidOperationException("Nonreentrant scene environment preparation.");
        using var read=SceneEnvironmentState.BeginRead(_world,WorldId);
        var value=SceneEnvironmentState.Read(_world,WorldId);
        if(!_hasGeometry){_environmentObserved=value;_environmentObservedRevision=_world.Revision;return;}
        var previousApproval=_environmentApproval;_environmentApproval=preparationAllowed;_environmentPreparing=true;
        RenderAssetLease<GpuEnvironmentResource>? gpu=null;PreparedSceneAssetLease? cpu=null;ScenePipelineSession? pipeline=null;
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
                shaders.VerifyFor(_renderer);cpu=assets.AcquireLease();gpu=_cache.AcquireEnvironment(package,EnvironmentApproved);
            }
            if(_pipeline is not null) {
                if(_environmentShaders is null&&shaders is not null) {
                    pipeline=CreateEnvironmentPipeline(shaders,gpu,value,_width,_height,_exposure,_ambient,_shadows);
                    _pipeline.Dispose();
                } else if(_environmentShaders is not null) _pipeline.ConfigureEnvironment(gpu?.Resource,value.ToConfiguration(),EnvironmentApproved);
            }
            // All fallible GPU publication/binding happens above. Lease releases below are managed
            // reference decrements/owned file closes; native retirement remains in explicit cache Trim.
            var oldGpu=_environmentGpu;var oldCpu=_environmentAssets;
            if(pipeline is not null){_pipeline=pipeline;pipeline=null;}
            _environmentGpu=gpu;gpu=null;_environmentAssets=cpu;cpu=null;
            _environmentShaders=shaders;_environment=value;_environmentObserved=value;_environmentObservedRevision=_world.Revision;
            oldGpu?.Dispose();oldCpu?.Dispose();
        } catch { _environmentApproval=previousApproval;pipeline?.Dispose();gpu?.Dispose();cpu?.Dispose();throw; }
        finally{_environmentPreparing=false;}
    }
    private ScenePipelineSession CreateEnvironmentPipeline(SceneEnvironmentRuntimeShaders shaders,RenderAssetLease<GpuEnvironmentResource>? gpu,
        EnvironmentLightingData value,uint width,uint height,float exposure,float ambient,bool shadows)
    {
        shaders.VerifyFor(_renderer);
        var candidate=new ScenePipelineSession(_renderer,_pipelineFactory(exposure,ambient,shadows),width,height,
            environmentShaders:(shadows?shaders.Shadowed:shaders.Unshadowed).Scene);
        try{candidate.ConfigureEnvironment(gpu?.Resource,value.ToConfiguration(),EnvironmentApproved);return candidate;}
        catch{candidate.Dispose();throw;}
    }
    public void PrepareEnvironmentView(uint width,uint height,Guid sceneCamera,SceneCameraView? browserCamera=null,float exposure=1,float ambient=.03f)
    {
        Verify();if(_environmentPreparing)throw new InvalidOperationException("Nonreentrant scene environment preparation.");
        VerifyPreparedEnvironment();if(_environmentShaders is null)return;
        using var read=SceneEnvironmentState.BeginRead(_world,WorldId);_environmentPreparing=true;
        try {
            _renderer.VerifyEnvironmentPreparation(EnvironmentApproved);
            var view=_extractor.Extract(_resources.Metadata,browserCamera is null?sceneCamera:Guid.Empty,width,height,browserCamera,_animation is not null);
            if(view.Camera is not {} camera||view.Geometry.Count==0&&view.ShadowCasters.Count==0)return;
            bool shadows=view.PrimaryLight is {Data.CastShadow:true};
            uint w=Math.Max(1,(uint)(width*camera.Data.ViewportWidth)),h=Math.Max(1,(uint)(height*camera.Data.ViewportHeight));
            if(_pipeline is null||_width!=w||_height!=h||_shadows!=shadows) {
                var candidate=CreateEnvironmentPipeline(_environmentShaders,_environmentGpu,_environment,w,h,exposure,ambient,shadows);
                try{_pipeline?.Dispose();}catch{candidate.Dispose();throw;}
                _pipeline=candidate;_width=w;_height=h;_shadows=shadows;_exposure=exposure;_ambient=ambient;
            } else if(_exposure!=exposure||_ambient!=ambient){
                _pipeline.Configure(_pipelineFactory(exposure,ambient,shadows));_exposure=exposure;_ambient=ambient;
            }
        }finally{_environmentPreparing=false;}
    }
    private void ReleaseEnvironmentLeases()
    { _environmentGpu?.Dispose();_environmentGpu=null;_environmentAssets?.Dispose();_environmentAssets=null; }
}
