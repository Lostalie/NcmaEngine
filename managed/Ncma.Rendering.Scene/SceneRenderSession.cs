using System.Numerics;
using System.Diagnostics;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;
namespace Ncma.Rendering.Scene;

public readonly record struct SceneRenderCosts(double ExtractionMilliseconds, double EncodeMilliseconds, int GeometryDraws, int ShadowDraws, bool GpuSubmitted);
// Application owns shared cache and independent Edit/Play sessions. No reference resources are created.
public sealed class SceneRenderSession : IDisposable
{
    private readonly RendererSession _renderer;
    private readonly RenderResourceCache _cache;
    private readonly RenderSceneExtractor _extractor;
    private readonly SceneGpuResources _resources;
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
    public SceneRenderSession(RendererSession renderer, RenderResourceCache cache, World world, PreparedSceneAssetLease assets, SceneDocumentSnapshot startup,
        Func<float, float, bool, RenderPipeline>? pipelineFactory = null)
    {
        _renderer = renderer; _cache = cache; _extractor = new(world); _readDiagnostics = _diagnostics.AsReadOnly();
        _pipelineFactory = pipelineFactory ?? ((exposure, ambient, shadows) => new Scene3DPipeline(exposure, ambient, shadows: shadows));
        _resources = SceneGpuResources.Prepare(cache, assets, startup);
    }
    public bool Submit(ulong frame, uint width, uint height, Guid sceneCamera, SceneCameraView? browserCamera = null,
        float exposure = 1, float ambient = .03f, SceneShadowSettings? shadow = null, GpuViewTarget? target = null, Vector2 origin = default, Vector4 clear = default)
    {
        Verify(); long time = Stopwatch.GetTimestamp(); var view = _extractor.Extract(_resources.Metadata, sceneCamera, width, height, browserCamera); View = view;
        double extraction = Stopwatch.GetElapsedTime(time).TotalMilliseconds; _diagnostics.Clear();
        for(int i=0;i<view.Diagnostics.Count;i++) _diagnostics.Add(view.Diagnostics[i]);
        for(int i=0;i<_resources.Diagnostics.Count;i++) _diagnostics.Add(_resources.Diagnostics[i]);
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
        _pipeline.Submit(frame, _geometry.AsSpan(0, geo), _casters.AsSpan(0, casters), new(camera.Position, toLight, color), lightVP,
            settings, target, new(origin.X + width * camera.Data.ViewportX, origin.Y + height * camera.Data.ViewportY, w, h), clear);
        Costs = new(extraction, encoding, geo, casters, true); return true;
    }
    private void Verify() { ObjectDisposedException.ThrowIf(_disposed, this); if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Scene render session requires owner thread."); }
    public void Dispose() { if (_disposed) return; Verify(); _pipeline?.Dispose(); _pipeline = null; _resources.Dispose(); _cache.Trim(); _disposed = true; }
}
