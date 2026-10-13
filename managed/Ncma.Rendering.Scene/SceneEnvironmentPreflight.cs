using Ncma.Assets;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;
namespace Ncma.Rendering.Scene;

// Trusted host startup/composition, BEFORE gameplay. Owns copied asset and native binding
// leases; no World is retained, no rendering, simulation, Agent permission or IO here.
public sealed class SceneEnvironmentPreflight : IDisposable
{
    private readonly RenderResourceCache _cache;
    private ScenePipelineSession? _binding;
    private RenderAssetLease<GpuEnvironmentResource>? _gpu;
    private PreparedSceneAssetLease? _assets;
    private bool _disposed;
    private SceneEnvironmentPreflight(RenderResourceCache cache)=>_cache=cache;
    public static void RequireShaderClosure(RuntimeShaderFileSet? files,SceneDocumentSnapshot scene)
    {
        if(files is null||!scene.Objects.Any(o=>o.Components.Any(c=>c.TypeId is StaticMeshData.TypeId or SkinnedMeshData.TypeId)))return;
        bool skin=scene.Objects.Any(o=>o.Components.Any(c=>c.TypeId==SkinnedMeshData.TypeId));
        var profile=SceneAssetPreparation.HasEnabledEnvironment(scene)?ShaderProfile.SceneEnvironment:ShaderProfile.Scene3D;
        _=files.Get(profile,false,skin);_=files.Get(profile,true,skin);
    }
    public static SceneEnvironmentPreflight Prepare(RendererSession renderer,RenderResourceCache cache,World world,
        PreparedSceneAssetLease assets,SceneDocumentSnapshot scene,Func<bool> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);ArgumentNullException.ThrowIfNull(assets);
        using var read=SceneEnvironmentState.BeginRead(world,world.Identity);
        var value=SceneEnvironmentState.Read(scene);
        if(value!=SceneEnvironmentState.Read(world,world.Identity))throw new ArgumentException("Stale environment preflight snapshot.");
        var result=new SceneEnvironmentPreflight(cache);
        if(!value.Enabled||!scene.Objects.Any(o=>o.Components.Any(c=>c.TypeId is StaticMeshData.TypeId or SkinnedMeshData.TypeId)))return result;
        try {
            var asset=(Ncma.Assets.Runtime.RuntimeEnvironmentAsset)assets.Assets.Require(value.AssetId,AssetKind.Environment);
            if(asset.Generation!=value.Generation||asset.ContentHash!=value.ContentHash)throw new ArgumentException("Exact environment startup generation/hash required.");
            bool skin=scene.Objects.Any(o=>o.Components.Any(c=>c.TypeId==SkinnedMeshData.TypeId));
            var shaders=SceneEnvironmentRuntimeShaders.PrepareDefault(renderer,skin,allowed);
            result._assets=assets.AcquireLease();result._gpu=cache.AcquireEnvironment(asset.Package,allowed);
            // Small, bounded actual GPU binding preflight; no geometry/animation copy or draw.
            result._binding=new(renderer,new Scene3DPipeline(shadows:false),16,16,environmentShaders:shaders.Unshadowed.Scene);
            result._binding.ConfigureEnvironment(result._gpu.Resource,value.ToConfiguration(),allowed);
            return result;
        }catch{result.Dispose();throw;}
    }
    public void Dispose()
    {
        if(_disposed)return;
        _binding?.Dispose();_binding=null;_gpu?.Dispose();_gpu=null;_assets?.Dispose();_assets=null;
        _cache.Trim();_disposed=true;
    }
}
