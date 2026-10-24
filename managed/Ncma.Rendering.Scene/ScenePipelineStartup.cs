using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;

namespace Ncma.Rendering.Scene;

// Trusted C# startup only. Not serialized, dynamically loaded or exposed through an Agent.
// Both the factory and Build callback run at an exact committed, read-only World boundary.
public sealed partial class ScenePipelineStartup : IDisposable
{
    public static Func<float, float, bool, RenderPipeline>? Bind(RendererSession renderer, World world,
        Func<float, float, bool, RenderPipeline>? factory, Func<bool> allowed)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(allowed);
        if (factory is null) return null;
        var binding = new Binding(renderer, world, factory, allowed);
        return binding.Create;
    }

    // Validate both shadow variants with real bounded bindings BEFORE gameplay is loaded.
    // Viewport-sized candidates are still validated again; this is not a code sandbox,
    // future-callback guarantee or permission to call a factory inside a simulation tick.
    public void Prepare(World world, ISceneContent scene,
        Func<float, float, bool, RenderPipeline>? factory, PostProcessRuntime? post, Func<bool> allowed,PreparedShadowLibrary? highShadows=null)
    {ArgumentNullException.ThrowIfNull(allowed);PrepareOwned(()=>PrepareCore(world,scene,factory,post,()=>Approved(allowed),highShadows));}
    private void PrepareCore(World world,ISceneContent scene,Func<float,float,bool,RenderPipeline>? factory,
        PostProcessRuntime? post,Func<bool> allowed,PreparedShadowLibrary? highShadows)
    {
        ArgumentNullException.ThrowIfNull(world);ArgumentNullException.ThrowIfNull(scene);
        var renderer=_renderer;
        using var read = SceneEnvironmentState.BeginRead(world, world.Identity);
        var shadowSelection=SceneShadowState.Read(world,world.Identity);
        if(shadowSelection!=SceneShadowState.Read(scene))throw new ArgumentException("Stale pipeline startup shadow selection.");
        if (factory is null&&shadowSelection is null) return;
        if (!ScenePostProcessPreflight.HasGeometry(scene)) return;
        int settings = new SceneComponentReader<RenderConfiguration>(world).ReadSingle(world.Identity, out var selectedConfig);
        if (settings > 1) throw new ArgumentException("Ambiguous render configuration.");
        var config = settings == 0 ? RenderConfiguration.Default(Guid.Empty) : selectedConfig;
        var value = ScenePostProcessState.Read(world, world.Identity);
        if (value != ScenePostProcessState.Read(scene)) throw new ArgumentException("Stale pipeline startup post settings.");
        bool environment = SceneEnvironmentState.Read(world, world.Identity).Enabled;
        if (environment != SceneEnvironmentState.Read(scene).Enabled) throw new ArgumentException("Stale pipeline startup environment.");
        bool skin = scene.Objects.Any(o => o.Components.Any(c => c.TypeId == SkinnedMeshData.TypeId));
        float exposure = value.Enabled ? config.Exposure * SceneTonePresets.ExposureFromStops(value.ExposureEv) : config.Exposure;
        var bound = Bind(renderer, world, factory, allowed);
        foreach (bool shadows in shadowSelection?.Quality==Ncma.Assets.SceneShadowQuality.Off?new[]{false}:new[] { false, true })
        {
            renderer.VerifyEnvironmentPreparation(allowed);
            var high=shadows&&shadowSelection?.Quality==Ncma.Assets.SceneShadowQuality.High?(highShadows??throw new ArgumentException("High startup requires checked shadow library.")):null;
            var prepared = value.Enabled ? (post ?? throw new ArgumentException("Checked post runtime required."))
                .Prepare(value, 16, 16, environment, shadows, skin,high) : null;
            RegisteredSceneShaders? shaders = prepared?.Scene;
            RegisteredEnvironmentSceneShaders? environmentShaders = prepared?.Environment;
            if (prepared is null)
            {
                if (environment)
                {
                    var pair = SceneEnvironmentRuntimeShaders.PrepareDefault(renderer, skin, allowed);
                    var variant=shadows ? pair.Shadowed : pair.Unshadowed;
                    environmentShaders = high is null?variant.Scene:high.Resolve(variant.Package).Environment;
                }
                else
                {
                    var pair = SceneRuntimeShaders.PrepareDefault(renderer, skin, allowed);
                    var variant=shadows ? pair.Shadowed : pair.Unshadowed;
                    shaders = high is null?variant.Scene:high.Resolve(variant.Package).Scene;
                }
            }
            ScenePipelineSession Create()
            {
                var pipeline=SceneShadowPipelinePolicy.Create(bound,exposure,config.Ambient,shadows,shadowSelection,environment?ShaderProfile.SceneEnvironment:ShaderProfile.Scene3D);
                return new ScenePipelineSession(renderer, pipeline, 16, 16,
                    shaders: shaders, environmentShaders: environmentShaders);
            }
            PrepareProbe(Create,binding=>{if(prepared?.Plan is not null)binding.ConfigurePostProcess(prepared.Plan,prepared.Programs,post!.BindingAllowed);},post,allowed);
        }
    }

    private sealed class Binding(RendererSession renderer, World world,
        Func<float, float, bool, RenderPipeline> factory, Func<bool> allowed)
    {
        private readonly Guid _identity = world.Identity;
        private readonly int _owner = Environment.CurrentManagedThreadId;
        private bool _building;
        internal RenderPipeline Create(float exposure, float ambient, bool shadows) => new BoundPipeline(this, exposure, ambient, shadows);
        private RenderGraph Build(float exposure, float ambient, bool shadows, uint width, uint height)
        {
            if (_owner != Environment.CurrentManagedThreadId || _building)
                throw new InvalidOperationException("Owner-thread, nonreentrant scene pipeline preparation required.");
            using var read = SceneEnvironmentState.BeginRead(world, _identity);
            _building = true;
            try
            {
                renderer.VerifyEnvironmentPreparation(allowed);
                RenderGraph graph;
                renderer.BeginToneOperation();
                try
                {
                    graph = (factory(exposure, ambient, shadows) ?? throw new ArgumentException("Null scene pipeline."))
                        .Build(width, height) ?? throw new ArgumentException("Null scene graph.");
                    var plan = graph.Compile(RenderCapabilities.SceneDx11);
                    if (!plan.RequiresSceneService || plan.Width != width || plan.Height != height ||
                        plan.Resources.Any(r => r.Role == RenderRole.SceneShadow) != shadows ||
                        plan.SceneOperations.Count(p => p.Operation == (uint)RenderOperation.SceneTone) != 1)
                        throw new ArgumentException("Formal scene pipeline requires exact dimensions/shadow variant and one final Tone.");
                }
                finally { renderer.EndToneOperation(); }
                renderer.VerifyEnvironmentPreparation(allowed);
                return graph;
            }
            finally { _building = false; }
        }
        private sealed class BoundPipeline(Binding owner, float exposure, float ambient, bool shadows) : RenderPipeline
        {
            public override RenderGraph Build(uint width, uint height) => owner.Build(exposure, ambient, shadows, width, height);
        }
    }
}
