using System.Numerics;
using System.Diagnostics;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;
namespace Ncma.Rendering.Scene;
using Vector3 = System.Numerics.Vector3;

public readonly record struct SceneRenderCosts(double ExtractionMilliseconds, double EncodeMilliseconds, int GeometryDraws, int ShadowDraws, bool GpuSubmitted);
public readonly record struct AnimationPresentationStamp(Guid WorldId,Guid PublicationId,ulong Tick,ulong RendererFrame,ulong PoseGeneration,int GeometryDraws,int ShadowDraws);
// Application owns shared cache and independent Edit/Play sessions. No reference resources are created.
public sealed class SceneRenderSession : IDisposable
{
    private readonly RendererSession _renderer;
    private readonly RenderResourceCache _cache;
    private readonly RenderSceneExtractor _extractor;
    private readonly SceneGpuResources _resources;
    private readonly SceneAnimationSession? _animation;
    private readonly Ncma.Gameplay.PlaySession? _play;
    private readonly bool _interpolateTransforms;
    private readonly World _world;
    private ulong _submittedFrame,_submittedPose;
    private readonly Guid _publication;
    private AnimationPresentationStamp? _animationStamp;
    // Copied observation of a successful submission, not a native GPU-time sample or a new render operation.
    public AnimationPresentationStamp? ReadAnimationPresentation()
    {Verify();return _animationStamp is {} stamp&&stamp.WorldId==_world.Identity&&stamp.Tick==_world.Tick&&_play is not {State:Ncma.Gameplay.PlayState.Faulted} ? stamp:null;}
    public Guid WorldId { get; }
    public ulong PreparedRevision { get; }
    public SceneAnimationSession? Animation => _animation;
    public ulong SkinBackpressureFrames { get; private set; }
    public double SkinAbiMilliseconds { get; private set; }
    private readonly Func<float, float, bool, RenderPipeline> _pipelineFactory;
    private ScenePipelineSession? _pipeline;
    private readonly SceneGpuDraw[] _geometry = new SceneGpuDraw[4096], _casters = new SceneGpuDraw[4096];
    private readonly List<SceneRenderDiagnostic> _diagnostics = new(8192);
    private readonly IReadOnlyList<SceneRenderDiagnostic> _readDiagnostics;
    private uint _width, _height;
    private float _exposure, _ambient;
    private bool _shadows, _disposed;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    public IReadOnlyList<SceneRenderDiagnostic> Diagnostics => _readDiagnostics;
    public SceneRenderCosts Costs { get; private set; }
    public RenderSceneView? View { get; private set; }
    public CompiledRenderGraph? Plan { get { Verify(); return _pipeline?.Plan; } }
    // Trusted diagnostic only; no normal frame readback and no Agent capability grants this operation.
    public void CaptureCharacterVertices(Guid objectId,Span<byte> output)
    { Verify();_resources.CaptureCharacterVertices(_renderer,objectId,output); }
    public SceneRenderSession(RendererSession renderer, RenderResourceCache cache, World world, PreparedSceneAssetLease assets, SceneDocumentSnapshot startup,
        Func<float, float, bool, RenderPipeline>? pipelineFactory = null, Ncma.Animation.Native.PoseKernel? poseKernel = null, Ncma.Gameplay.PlaySession? play = null, bool interpolateTransforms = false,
        Ncma.Animation.IRootMotionPresentation? rootMotion=null,SceneAnimatorRuntime? animators=null)
    {
        _renderer = renderer; _cache = cache; _world=world;_extractor = new(world); _readDiagnostics = _diagnostics.AsReadOnly();
        _pipelineFactory = pipelineFactory ?? ((exposure, ambient, shadows) => new Scene3DPipeline(exposure, ambient, shadows: shadows));
        WorldId = world.Identity; PreparedRevision = world.Revision; _play = play;
        _publication=assets.Assets.Identity;
        _interpolateTransforms=interpolateTransforms;
        try {
            if(startup.Objects.Any(o=>o.Components.Any(c=>c.TypeId==SkinnedMeshData.TypeId))) {
                if(poseKernel is null)throw new ArgumentException("Skinned scenes require the trusted numerical pose plugin.");
                _animation = new(world,assets,startup,poseKernel,rootMotion,animators);
            }
            _resources = SceneGpuResources.Prepare(cache, assets, startup,renderer,_animation);
            if(play is not null)_animation?.Attach(play);
        } catch { _resources?.Dispose(); _animation?.Dispose(); cache.Trim(); throw; }
    }
    public bool Submit(ulong frame, uint width, uint height, Guid sceneCamera, SceneCameraView? browserCamera = null,
        float exposure = 1, float ambient = .03f, SceneShadowSettings? shadow = null, GpuViewTarget? target = null, Vector2 origin = default, Vector4 clear = default)
    {
        Verify(); _animationStamp=null;long time = Stopwatch.GetTimestamp();
        IReadOnlyDictionary<Guid,TransformData>? transforms=null;
        if(_interpolateTransforms && _play is not null) {
            if(_play.Document.World.Identity!=WorldId || _play.State==Ncma.Gameplay.PlayState.Faulted)throw new InvalidOperationException("Invalid coupled presentation snapshot.");
            transforms=_play.RenderView.Objects.ToDictionary(o=>o.ObjectId,o=>o.Transform);
        }
        var view = _extractor.Extract(_resources.Metadata, browserCamera is null ? sceneCamera : Guid.Empty, width, height, browserCamera,_animation is not null,transforms); View = view;
        double extraction = Stopwatch.GetElapsedTime(time).TotalMilliseconds; _diagnostics.Clear();
        for(int i=0;i<view.Diagnostics.Count;i++) _diagnostics.Add(view.Diagnostics[i]);
        for(int i=0;i<_resources.Diagnostics.Count;i++) _diagnostics.Add(_resources.Diagnostics[i]);
        if(_animation is not null)for(int i=0;i<_animation.Diagnostics.Count;i++)_diagnostics.Add(_animation.Diagnostics[i]);
        if (view.Camera is not { } camera) { Costs = new(extraction, 0, 0, 0, false); return false; }
        // Pure empty/2D scenes have no pipeline/shadow/HDR initialization. Application may clear via its lightweight path.
        if (view.Geometry.Count == 0 && view.ShadowCasters.Count == 0) { Costs = new(extraction, 0, 0, 0, false); return false; }
        time = Stopwatch.GetTimestamp(); bool shadows = view.PrimaryLight is { Data.CastShadow: true };
        Vector3 toLight = view.PrimaryLight is { } light ? -light.Direction : Vector3.UnitY;
        var settings=shadow??new SceneShadowSettings();
        var lightVP = ShadowVolume.Create(camera, toLight,settings.Distance);
        int geo = _resources.Encode(view.Geometry, camera.ViewProjection, null, _geometry, _diagnostics, view.PrimaryLight?.Data.LayerMask??uint.MaxValue);
        int casters = shadows ? _resources.Encode(view.ShadowCasters, camera.ViewProjection, lightVP, _casters, _diagnostics) : 0;
        if (geo == 0 && casters == 0) { Costs = new(extraction, Stopwatch.GetElapsedTime(time).TotalMilliseconds, 0, 0, false); return false; }
        uint w = Math.Max(1, (uint)(width * camera.Data.ViewportWidth)), h = Math.Max(1, (uint)(height * camera.Data.ViewportHeight));
        if (_pipeline is null || _width != w || _height != h || _shadows != shadows)
        {
            var candidate = new ScenePipelineSession(_renderer, _pipelineFactory(exposure, ambient, shadows), w, h);
            try { _pipeline?.Dispose(); } catch { candidate.Dispose(); throw; }
            _pipeline = candidate; _width = w; _height = h; _exposure = exposure; _ambient = ambient; _shadows = shadows;
        }
        else if (_exposure != exposure || _ambient != ambient) {
            _pipeline.Configure(_pipelineFactory(exposure,ambient,shadows)); _exposure=exposure; _ambient=ambient;
        }
        Vector4 color = view.PrimaryLight is { } l ? new(l.Data.Red, l.Data.Green, l.Data.Blue, l.Data.Intensity) : new(1, 1, 1, 0);
        double encoding = Stopwatch.GetElapsedTime(time).TotalMilliseconds;
        if(_animation is not null) {
            _animation.Evaluate((float)(_play?.InterpolationAlpha ?? 1),_play is not { State:Ncma.Gameplay.PlayState.Running });
            long skinStarted=Stopwatch.GetTimestamp();bool updated=_resources.UpdateAnimation(_renderer,_animation,frame);
            SkinAbiMilliseconds=Stopwatch.GetElapsedTime(skinStarted).TotalMilliseconds;
            if(!updated) {
                SkinBackpressureFrames++; Costs = new(extraction,encoding,geo,casters,false);return false;
            }
        }
        _pipeline.Submit(frame, _geometry.AsSpan(0, geo), _casters.AsSpan(0, casters), new(camera.Position, toLight, color), lightVP,
            settings, target, new(origin.X + width * camera.Data.ViewportX, origin.Y + height * camera.Data.ViewportY, w, h), clear);
        _submittedFrame=frame;_submittedPose=_animation?.Costs.PoseGeneration??0;
        if(_animation is not null)_animationStamp=new(WorldId,_publication,_world.Tick,frame,_submittedPose,geo,casters);
        Costs = new(extraction, encoding, geo, casters, true); return true;
    }
    // User click only. No GPU readback or per-frame CPU skinning. Animated bounds use the
    // exact submitted pose's conservative influence union, never stale bind-pose bounds.
    public Guid? Pick(ulong frame,Guid viewIdentity,float u,float v)
    {
        Verify();var view=View??throw new InvalidOperationException("pick_view_missing");
        if(_submittedFrame!=frame||view.FrameIdentity!=viewIdentity||_world.Identity!=view.WorldId||_world.Revision!=view.Revision||!Costs.GpuSubmitted)throw new InvalidOperationException("stale_pick_frame");
        var camera=view.Camera??throw new InvalidOperationException("pick_camera_missing");
        if(!float.IsFinite(u)||!float.IsFinite(v)||u is <0 or >1||v is <0 or >1)throw new ArgumentException("Pick range.");
        if(u<camera.Data.ViewportX||u>camera.Data.ViewportX+camera.Data.ViewportWidth||v<camera.Data.ViewportY||v>camera.Data.ViewportY+camera.Data.ViewportHeight)return null;
        var ray=BoundsPicking.Ray(camera,u,v);Guid? best=null;float nearest=float.PositiveInfinity;
        foreach(var item in view.Geometry){MeshBounds bounds;Matrix4x4 model=item.Model;
            if(item.Mesh.Kind==Ncma.Assets.AssetKind.SkinnedMesh){if(_animation is null||!_animation.TryBounds(item.ObjectId,_submittedPose,model,out bounds))continue;model=Matrix4x4.Identity;}
            else bounds=new(item.Mesh.BoundsMin,item.Mesh.BoundsMax);
            if(BoundsPicking.Hit(bounds.Min,bounds.Max,model,ray.Origin,ray.Direction,out float distance)&&(distance<nearest||distance==nearest&&(best is null||item.ObjectId.CompareTo(best.Value)<0))){best=item.ObjectId;nearest=distance;}}
        return best;
    }
    private void Verify() { ObjectDisposedException.ThrowIf(_disposed, this); if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Scene render session requires owner thread."); }
    public void Dispose() { if (_disposed) return; Verify(); _pipeline?.Dispose(); _pipeline = null; _resources.Dispose(); _animation?.Dispose(); _cache.Trim(); _disposed = true; }
}
