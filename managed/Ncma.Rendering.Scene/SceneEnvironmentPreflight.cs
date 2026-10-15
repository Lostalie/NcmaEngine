using Ncma.Assets;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;
namespace Ncma.Rendering.Scene;

public enum EnvironmentPreflightPhase { Empty, Preparing, Ready, Failed, Closing, Closed }
public readonly record struct EnvironmentPreflightStatus(EnvironmentPreflightPhase Phase,bool BindingRetained,bool GpuRetained,bool AssetsRetained);

// Trusted host startup/composition, BEFORE gameplay. Owns copied asset and native binding
// leases; no World is retained, no rendering, simulation, Agent permission or IO here.
public sealed class SceneEnvironmentPreflight : IDisposable
{
    private readonly RenderResourceCache _cache;
    private readonly RendererSession _renderer;
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private ScenePipelineSession? _binding;
    private RenderAssetLease<GpuEnvironmentResource>? _gpu;
    private PreparedSceneAssetLease? _assets;
    private EnvironmentPreflightPhase _phase;
    private bool _rejected;
    // No GPU/asset allocation in this constructor: the host stores this owner
    // BEFORE calling Prepare. Failed preparation never hides its resource owner.
    public SceneEnvironmentPreflight(RendererSession renderer,RenderResourceCache cache)
    {
        ArgumentNullException.ThrowIfNull(renderer);ArgumentNullException.ThrowIfNull(cache);
        cache.VerifyFor(renderer);_renderer=renderer;_cache=cache;
    }
    private void VerifyThread(){if(_owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Environment preflight owner thread.");}
    public EnvironmentPreflightStatus Status
    {get{VerifyThread();return new(_phase,_binding is not null,_gpu is not null,_assets is not null);}}
    public static void RequireShaderClosure(RuntimeShaderFileSet? files,SceneDocumentSnapshot scene)
    {
        if(files is null||!scene.Objects.Any(o=>o.Components.Any(c=>c.TypeId is StaticMeshData.TypeId or SkinnedMeshData.TypeId)))return;
        bool skin=scene.Objects.Any(o=>o.Components.Any(c=>c.TypeId==SkinnedMeshData.TypeId));
        var profile=SceneAssetPreparation.HasEnabledEnvironment(scene)?ShaderProfile.SceneEnvironment:ShaderProfile.Scene3D;
        _=files.Get(profile,false,skin);_=files.Get(profile,true,skin);
    }
    public void Prepare(World world,
        PreparedSceneAssetLease assets,SceneDocumentSnapshot scene,Func<bool> allowed)
    {
        VerifyThread();
        if(_phase==EnvironmentPreflightPhase.Preparing){_rejected=true;throw new InvalidOperationException("Environment preflight is nonreentrant.");}
        if(_phase!=EnvironmentPreflightPhase.Empty)throw new InvalidOperationException("Environment preflight is single-use; close before replacing it.");
        ArgumentNullException.ThrowIfNull(allowed);ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(world);ArgumentNullException.ThrowIfNull(scene);
        _cache.VerifyFor(_renderer);
        using var read=SceneEnvironmentState.BeginRead(world,world.Identity);
        var value=SceneEnvironmentState.Read(scene);
        if(value!=SceneEnvironmentState.Read(world,world.Identity))throw new ArgumentException("Stale environment preflight snapshot.");
        // Off / pure-2D startup must not even query the optional environment GPU API.
        if(!value.Enabled||!scene.Objects.Any(o=>o.Components.Any(c=>c.TypeId is StaticMeshData.TypeId or SkinnedMeshData.TypeId))){_phase=EnvironmentPreflightPhase.Ready;return;}
        _renderer.VerifyEnvironmentPreparation(()=>true);
        _phase=EnvironmentPreflightPhase.Preparing;_rejected=false;
        bool Approved(){bool result=allowed();if(_rejected)throw new InvalidOperationException("Environment preflight callback violated ownership.");return result;}
        try {
            _renderer.VerifyEnvironmentPreparation(Approved);
            var asset=(Ncma.Assets.Runtime.RuntimeEnvironmentAsset)assets.Assets.Require(value.AssetId,AssetKind.Environment);
            if(asset.Generation!=value.Generation||asset.ContentHash!=value.ContentHash)throw new ArgumentException("Exact environment startup generation/hash required.");
            bool skin=scene.Objects.Any(o=>o.Components.Any(c=>c.TypeId==SkinnedMeshData.TypeId));
            var shaders=SceneEnvironmentRuntimeShaders.PrepareDefault(_renderer,skin,Approved);
            _assets=assets.AcquireLease();_gpu=_cache.AcquireEnvironment(asset.Package,Approved);
            // Small, bounded actual GPU binding preflight; no geometry/animation copy or draw.
            _binding=new(_renderer,new Scene3DPipeline(shadows:false),16,16,environmentShaders:shaders.Unshadowed.Scene);
            _binding.ConfigureEnvironment(_gpu.Resource,value.ToConfiguration(),Approved);
            _renderer.VerifyEnvironmentPreparation(Approved);_phase=EnvironmentPreflightPhase.Ready;
        }catch{_phase=EnvironmentPreflightPhase.Failed;throw;}
        finally{_rejected=false;}
    }
    public void Dispose()
    {
        VerifyThread();if(_phase==EnvironmentPreflightPhase.Closed)return;
        if(_phase==EnvironmentPreflightPhase.Preparing){_rejected=true;throw new InvalidOperationException("Cannot close during environment preflight.");}
        _phase=EnvironmentPreflightPhase.Closing;
        _binding?.Dispose();_binding=null;_gpu?.Dispose();_gpu=null;_assets?.Dispose();_assets=null;
        _cache.Trim();_phase=EnvironmentPreflightPhase.Closed;
    }
}
