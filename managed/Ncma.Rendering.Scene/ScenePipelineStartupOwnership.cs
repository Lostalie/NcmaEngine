using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;

namespace Ncma.Rendering.Scene;

public sealed partial class ScenePipelineStartup
{
    private readonly RendererSession _renderer;
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private ScenePipelineSession? _probe;
    private bool _preparing,_rejected,_closing,_disposed;
    // Allocation-free with respect to GPU resources. Hosts store this owner before
    // either preflight; one bounded probe covers post and non-post startup alike.
    public ScenePipelineStartup(RendererSession renderer)
    {ArgumentNullException.ThrowIfNull(renderer);_=renderer.Handle;_renderer=renderer;}
    private void VerifyThread(){if(_owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Pipeline startup owner thread.");}
    public bool HasRetainedProbe {get{VerifyThread();return _probe is not null;}}
    private void VerifyCandidate(){if(_rejected)throw new InvalidOperationException("Startup callback violated probe ownership.");}
    private bool Approved(Func<bool> allowed){bool result=allowed();VerifyCandidate();return result;}
    private void PrepareOwned(Action prepare)
    {
        VerifyThread();ObjectDisposedException.ThrowIf(_disposed,this);
        if(_preparing){_rejected=true;throw new InvalidOperationException("Pipeline startup is nonreentrant.");}
        if(_closing||_probe is not null)throw new InvalidOperationException("Retained startup probe requires explicit close before replacement.");
        _=_renderer.Handle;_preparing=true;_rejected=false;
        try{prepare();VerifyCandidate();}finally{_preparing=false;_rejected=false;}
    }
    private void PrepareProbe(Func<ScenePipelineSession> create,Action<ScenePipelineSession> bind,PostProcessRuntime? post,Func<bool> allowed)
    {
        VerifyCandidate();if(_probe is not null)throw new InvalidOperationException("Only one startup probe is allowed.");
        Exception? preparationFailure=null;
        try {
            void Install(){VerifyCandidate();_probe=create();bind(_probe);VerifyCandidate();}
            if(post is null)Install();else post.ApproveInstallation(Install);
            _renderer.VerifyEnvironmentPreparation(()=>Approved(allowed));VerifyCandidate();
        }catch(Exception error){preparationFailure=error;throw;}
        finally {
            try{_probe?.Dispose();_probe=null;}
            catch(Exception closeFailure){if(preparationFailure is not null)throw new AggregateException("Startup preparation and probe retirement failed; host retains probe.",preparationFailure,closeFailure);throw;}
        }
    }
    public void PreparePost(World world,ISceneContent scene,PostProcessRuntime? runtime)
    {PrepareOwned(()=>PreparePostCore(world,scene,runtime));}
    private void PreparePostCore(World world,ISceneContent scene,PostProcessRuntime? runtime)
    {
        ArgumentNullException.ThrowIfNull(world);ArgumentNullException.ThrowIfNull(scene);
        using var read=SceneEnvironmentState.BeginRead(world,world.Identity);
        var value=ScenePostProcessState.Read(scene);
        if(value!=ScenePostProcessState.Read(world,world.Identity))throw new ArgumentException("Stale post preflight World.");
        if(!value.Enabled)return;
        if(runtime is null)throw new ArgumentException("Enabled post configuration requires a checked runtime.");
        bool skin=scene.Objects.Any(o=>o.Components.Any(c=>c.TypeId==SkinnedMeshData.TypeId));
        if(!skin&&!scene.Objects.Any(o=>o.Components.Any(c=>c.TypeId==StaticMeshData.TypeId)))throw new ArgumentException("Post effects require an explicit 3D scene; pure UI must not load them.");
        bool environment=SceneEnvironmentState.Read(scene).Enabled;
        var prepared=runtime.Prepare(value,16,16,environment,false,skin);
        PrepareProbe(()=>new ScenePipelineSession(_renderer,new Scene3DPipeline(SceneTonePresets.ExposureFromStops(value.ExposureEv),shadows:false),16,16,shaders:prepared.Scene,environmentShaders:prepared.Environment),
            binding=>{if(prepared.Plan is not null)binding.ConfigurePostProcess(prepared.Plan,prepared.Programs,runtime.BindingAllowed);},runtime,runtime.BindingAllowed);
    }
    public void Dispose()
    {
        VerifyThread();if(_disposed)return;
        if(_preparing){_rejected=true;throw new InvalidOperationException("Cannot close during pipeline startup preparation.");}
        _closing=true;_probe?.Dispose();_probe=null;_disposed=true;
    }
}
