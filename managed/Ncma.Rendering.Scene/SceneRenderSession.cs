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
public sealed partial class SceneRenderSession : IDisposable
{
    private readonly RendererSession _renderer;
    private readonly RenderResourceCache _cache;
    private readonly RenderSceneExtractor _extractor;
    private readonly SceneGpuResources _resources = new();
    private readonly ScenePreparationLifetime _lifetime = new();
    public ScenePreparationPhase PreparationPhase => _lifetime.Phase;
    private SceneAnimationSession? _animation;
    private IDisposable? _animationBorrow;
    private bool _pendingAnimationBinding;
    private Ncma.Gameplay.PlaySession? _play;
    private bool _interpolateTransforms;
    private SceneRuntimeShaders? _shaders;
    public SceneRuntimeShaders? PreparedShaders => _shaders;
    private readonly World _world;
    private ulong _submittedFrame,_submittedPose;
    private Guid _publication;
    private AnimationPresentationStamp? _animationStamp;
    // Copied observation of a successful submission, not a native GPU-time sample or a new render operation.
    public AnimationPresentationStamp? ReadAnimationPresentation()
    {Verify();return !_resourceRecoveryRequired&&!_closing&&_animationStamp is {} stamp&&stamp.WorldId==_world.Identity&&stamp.Tick==_world.Tick&&_play is not {State:Ncma.Gameplay.PlayState.Faulted} ? stamp:null;}
    public Guid WorldId { get; }
    public ulong PreparedRevision { get; private set; }
    public SceneAnimationSession? Animation { get { Verify(); return _animation; } }
    public ulong SkinBackpressureFrames { get; private set; }
    public double SkinAbiMilliseconds { get; private set; }
    private Func<float, float, bool, RenderPipeline> _pipelineFactory = (exposure, ambient, shadows) => new Scene3DPipeline(exposure, ambient, shadows: shadows);
    private bool _hasCustomPipeline;
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
    public SceneRenderSession(RendererSession renderer, RenderResourceCache cache, World world)
    {
        ArgumentNullException.ThrowIfNull(renderer); ArgumentNullException.ThrowIfNull(cache); ArgumentNullException.ThrowIfNull(world);
        cache.VerifyFor(renderer);
        _renderer = renderer; _cache = cache; _world = world; _extractor = new(world);
        _readDiagnostics = _diagnostics.AsReadOnly(); WorldId = world.Identity;
    }
    public void Prepare(PreparedSceneAssetLease assets, SceneDocumentSnapshot startup,
        Func<float, float, bool, RenderPipeline>? pipelineFactory = null, Ncma.Animation.Native.PoseKernel? poseKernel = null, Ncma.Gameplay.PlaySession? play = null, bool interpolateTransforms = false,
        Ncma.Animation.IRootMotionPresentation? rootMotion=null,SceneAnimatorRuntime? animators=null,bool previewInterruptions=false,SceneRuntimeShaders? shaders=null,
        SceneEnvironmentRuntimeShaders? environmentShaders=null,Func<bool>? environmentPreparationAllowed=null,PostProcessRuntime? postProcessing=null,
        PreparedShadowLibrary? highShadows=null,SceneAnimationSession? retainedAnimation=null,bool deferPlayObserver=false)
    {
        _lifetime.Begin();
        try {
        ArgumentNullException.ThrowIfNull(assets); ArgumentNullException.ThrowIfNull(startup);
        var renderer = _renderer; var cache = _cache; var world = _world;
        cache.VerifyFor(renderer);
        using var startupRead = SceneEnvironmentState.BeginRead(world, WorldId);
        _pipelineFactory = pipelineFactory ?? ((exposure, ambient, shadows) => new Scene3DPipeline(exposure, ambient, shadows: shadows));
        _hasCustomPipeline = pipelineFactory is not null;
        _environmentApproval = environmentPreparationAllowed ?? (()=>!_disposed);
        PreparedRevision = world.Revision; _play = play;
        _publication=assets.Assets.Identity;
        _interpolateTransforms=interpolateTransforms;
            bool hasSkin=startup.Objects.Any(o=>o.Components.Any(c=>c.TypeId==SkinnedMeshData.TypeId));
            if(!hasSkin&&retainedAnimation is not null)throw new ArgumentException("A scene without skin cannot adopt retained animation.");
            bool hasGeometry=hasSkin||startup.Objects.Any(o=>o.Components.Any(c=>c.TypeId==StaticMeshData.TypeId));
            _hasGeometry=hasGeometry;_hasSkin=hasSkin;
            InitializeShadows(startup,highShadows);
            InitializePostProcess(postProcessing,ScenePostProcessState.Read(startup));
            var environment=SceneEnvironmentState.Read(startup);
            if(environment!=SceneEnvironmentState.Read(world,WorldId))throw new ArgumentException("Stale startup environment configuration.");
            _environmentObservedRevision=world.Revision;_environmentObserved=environment;
            if(hasGeometry) {
                if(environment.Enabled||environmentShaders is not null) {
                    if(shaders is not null)throw new ArgumentException("Choose one exact scene shader profile.");
                    InitializeEnvironment(assets,environment,environmentShaders,environmentPreparationAllowed??throw new ArgumentException("Explicit trusted environment preparation approval required."));
                } else {
                    _shaders=shaders??SceneRuntimeShaders.PrepareDefault(renderer,hasSkin,()=>!_disposed);
                    if(_shaders.Unshadowed.Package.Skinning!=hasSkin)throw new ArgumentException("Scene shader skin closure mismatch.");
                    _shaders.Unshadowed.VerifyFor(renderer);_shaders.Shadowed.VerifyFor(renderer);
                }
            } else if(shaders is not null||environmentShaders is not null)throw new ArgumentException("Empty scene must not prepare scene shaders.");
            if(startup.Objects.Any(o=>o.Components.Any(c=>c.TypeId==SkinnedMeshData.TypeId))) {
                if(poseKernel is null)throw new ArgumentException("Skinned scenes require the trusted numerical pose plugin.");
                if(retainedAnimation is null){
                    _animation = new(world,poseKernel);
                    _animation.Prepare(assets,startup,rootMotion,animators,previewInterruptions);
                }else{
                    _animationBorrow=retainedAnimation.BorrowForPresentation(world,assets.Assets.Identity,poseKernel,play,rootMotion,animators);
                    _animation=retainedAnimation;
                }
            }
            if(_environmentShaders is not null) {
                using var read=SceneEnvironmentState.BeginRead(world,WorldId);_environmentPreparing=true;
                try{_renderer.VerifyEnvironmentPreparation(EnvironmentApproved);
                    _resources.Prepare(cache,assets,startup,renderer,_animation,_environmentShaders.Unshadowed.Skin);
                }finally{_environmentPreparing=false;}
            }else _resources.Prepare(cache, assets, startup,renderer,_animation,_shaders?.Unshadowed.Skin);
            if(play is not null&&retainedAnimation is null&&_animation is not null){
                if(deferPlayObserver)_pendingAnimationBinding=true;else _animation.Attach(play);
            }
            _lifetime.Complete();
        } catch { _lifetime.Fail(); throw; }
    }
    public bool Submit(ulong frame, uint width, uint height, Guid sceneCamera, SceneCameraView? browserCamera = null,
        float exposure = 1, float ambient = .03f, SceneShadowSettings? shadow = null, GpuViewTarget? target = null, Vector2 origin = default, Vector4 clear = default)
    {
        RequireExchangeReady();if(_environmentPreparing||_pendingAnimationBinding)throw new InvalidOperationException("Cannot submit before presentation preparation/binding completes.");VerifyPreparedEnvironment();VerifyPreparedPostProcess();ObserveShadowSelection(); _animationStamp=null;long time = Stopwatch.GetTimestamp();
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
        VerifyPreparedShadows();
        time = Stopwatch.GetTimestamp(); bool shadows = SceneShadowPipelinePolicy.EffectiveShadows(_shadowPrepared,view.PrimaryLight is { Data.CastShadow: true });
        Vector3 toLight = view.PrimaryLight is { } light ? -light.Direction : Vector3.UnitY;
        var settings=SceneShadowPipelinePolicy.Settings(_shadowPrepared,shadow);
        var lightVP = ShadowVolume.Create(camera, toLight,settings.Distance);
        int geo = _resources.Encode(view.Geometry, camera.ViewProjection, null, _geometry, _diagnostics, view.PrimaryLight?.Data.LayerMask??uint.MaxValue);
        int casters = shadows ? _resources.Encode(view.ShadowCasters, camera.ViewProjection, lightVP, _casters, _diagnostics) : 0;
        if (geo == 0 && casters == 0) { Costs = new(extraction, Stopwatch.GetElapsedTime(time).TotalMilliseconds, 0, 0, false); return false; }
        uint w = Math.Max(1, (uint)(width * camera.Data.ViewportWidth)), h = Math.Max(1, (uint)(height * camera.Data.ViewportHeight));
        if(_postRuntime is not null) {
            if(!_postReady||_postViewEmpty||_pipeline is null||_width!=w||_height!=h||_shadows!=shadows||_postInputExposure!=exposure||_ambient!=ambient)
                throw new InvalidOperationException("Post view requires explicit off-frame preparation.");
        }
        else if(_environmentShaders is not null||_shadowPrepared is not null) {
            if(_pipeline is null||_width!=w||_height!=h||_shadows!=shadows||_exposure!=exposure||_ambient!=ambient)
                throw new InvalidOperationException("Environment view requires explicit off-frame preparation.");
        }
        else if (_pipeline is null || _width != w || _height != h || _shadows != shadows)
        {
            var programs=(shadows?_shaders?.Shadowed:_shaders?.Unshadowed)?.Scene??throw new InvalidOperationException("Scene shaders were not prepared.");
            BeginCandidateBundle();
            using var pipelineRead=SceneEnvironmentState.BeginRead(_world,WorldId);_environmentPreparing=true;
            try { _pendingPipeline = new ScenePipelineSession(_renderer, CreateSelectedPipeline(exposure, ambient, shadows,_shadowPrepared,ShaderProfile.Scene3D), w, h,shaders:programs);InstallPendingPipeline(); }
            catch(Exception failure){AbandonCandidate(failure);throw;}
            finally{_environmentPreparing=false;}
            _width = w; _height = h; _exposure = exposure; _ambient = ambient; _shadows = shadows;
            CommitShadowSelection(_shadowPrepared);
        }
        else if (_exposure != exposure || _ambient != ambient) {
            _pipeline.Configure(CreateSelectedPipeline(exposure,ambient,shadows,_shadowPrepared,ShaderProfile.Scene3D)); _exposure=exposure; _ambient=ambient;
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
        (_pipeline??throw new InvalidOperationException("Missing installed scene pipeline.")).Submit(frame, _geometry.AsSpan(0, geo), _casters.AsSpan(0, casters), new(camera.Position, toLight, color), lightVP,
            settings, target, new(origin.X + width * camera.Data.ViewportX, origin.Y + height * camera.Data.ViewportY, w, h), clear);
        _submittedFrame=frame;_submittedPose=_animation?.Costs.PoseGeneration??0;
        if(_animation is not null)_animationStamp=new(WorldId,_publication,_world.Tick,frame,_submittedPose,geo,casters);
        Costs = new(extraction, encoding, geo, casters, true); return true;
    }
    // User click only. No GPU readback or per-frame CPU skinning. Animated bounds use the
    // exact submitted pose's conservative influence union, never stale bind-pose bounds.
    public Guid? Pick(ulong frame,Guid viewIdentity,float u,float v)
    {
        RequireExchangeReady();var view=View??throw new InvalidOperationException("pick_view_missing");
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
    private void Verify() { ObjectDisposedException.ThrowIf(_disposed, this); if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Scene render session requires owner thread."); _lifetime.RequireReady(); }
    // Explicit ownership transfer before retiring a device. The old scene remains a borrower
    // until successfully closed, so the host cannot release live numerical state prematurely.
    public SceneAnimationSession? RetainAnimationForRecovery()
    {
        Verify();var frame=_renderer.ReadFrameState();
        if(frame.Active||_closing||_environmentPreparing||_recovering||_pendingAnimationBinding||
            _play is {State: not (Ncma.Gameplay.PlayState.Running or Ncma.Gameplay.PlayState.Paused)})
            throw new InvalidOperationException("Retain animation only at a ready, stopped presentation boundary with healthy gameplay.");
        if(_animation is null)return null;
        if(_animationBorrow is not null)throw new InvalidOperationException("This scene does not own the retained animation.");
        _animationBorrow=_animation.BorrowForRecovery();return _animation;
    }
    // For a first frame without an existing animation observer: preparation runs with Play
    // control frozen; attach only after the host has validated and released that scope.
    public void CompleteDeferredAnimationBinding()
    {
        Verify();if(!_pendingAnimationBinding)return;
        if(_closing||_environmentPreparing||_renderer.ReadFrameState().Active)throw new InvalidOperationException("Animation binding requires an off-frame ready scene.");
        _animation!.Attach(_play!);_pendingAnimationBinding=false;
    }
    public void Dispose()
    {
        _lifetime.VerifyOwner(); if(_disposed)return;
        if(_lifetime.Phase==ScenePreparationPhase.Preparing)_lifetime.BeginClose();
        if(_recovering){_recoveryRejected=true;throw new InvalidOperationException("Cannot close during resource recovery.");}
        if(_environmentPreparing)throw new InvalidOperationException("Cannot close during environment preparation.");
        if(!_lifetime.BeginClose())return;
        _closing=true;
        try{ReleasePendingResources();CloseInstalledPipeline();ReleaseEnvironmentLeases();_resources.Dispose();
            if(_animationBorrow is not null)_animationBorrow.Dispose();else _animation?.Dispose();
            _animationBorrow=null;_animation=null;
            _cache.Trim();_disposed=true;_lifetime.Closed();}
        catch{MarkExchangeFailure(SceneResourceExchangeFailure.Closing);throw;}
    }
}
