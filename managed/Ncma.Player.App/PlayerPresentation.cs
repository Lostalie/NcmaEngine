using Ncma.Gameplay;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
namespace Ncma.Player.App;

internal sealed class PlayerPresentation(string plugins, bool visible) : IDisposable
{
    private readonly PluginLoader _loader = new();
    private PlatformWindow? _window;
    private RendererSession? _renderer;
    private RenderPipelineService? _pipeline;
    private RenderResourceCache? _cache;
    private Ncma.Rendering.Scene.SceneRenderSession? _scene;
    private Ncma.Animation.Native.PoseKernel? _poseKernel;
    private Guid _camera;
    private readonly WindowInputAccumulator _input = new();
    private WindowState _state;
    private readonly ulong[] _held = new ulong[8], _pressed = new ulong[8], _released = new ulong[8];
    private ulong _frame;
    private double _x, _y;
    private bool _focused, _disposed;
    public PlayerModule[] Modules => _loader.Modules.Select(m => new PlayerModule(m.Id, m.AbiMajor, m.AbiMinor, m.Capabilities)).ToArray();
    public RendererStats Stats => _renderer!.Stats;
    public void Start(RuntimeShaderFileSet? shaderFiles = null)
    {
        _loader.Load(plugins, [
            new("ncma.platform", ModuleKind.Platform, "NcmaPlatform.dll", "NcmaPlatform.dll", 1, 0, []),
            new("ncma.renderer", ModuleKind.Renderer, "NcmaRenderer.dll", "NcmaRenderer.dll", 1, 2, ["ncma.platform"]) ]);
        _window = new(_loader.Modules.Single(m => m.Kind == ModuleKind.Platform), "NcmaPlayer — DX11 scene", visible: visible);
        _window.SetIcon(Path.Combine(AppContext.BaseDirectory, "NcmaEngine.ico"));
        _renderer = new(_loader.Modules.Single(m => m.Kind == ModuleKind.Renderer), _window, 1280, 720);
        shaderFiles?.InstallSelection(_renderer,()=>!_disposed);
        _pipeline = new(_renderer);
        _cache = new(_renderer);
    }
    public void BindScene(PlaySession play, Ncma.Scene.Rendering.PreparedSceneAssetLease assets, Guid? camera,Ncma.Animation.IRootMotionPresentation? rootMotion=null,Ncma.Scene.Rendering.SceneAnimatorRuntime? animators=null)
    {
        if (_scene is not null) throw new InvalidOperationException("Scene already bound.");
        var snapshot=play.Document.CaptureSnapshot();
        if(snapshot.Objects.Any(o=>o.Components.Any(c=>c.TypeId==Ncma.Scene.Rendering.SkinnedMeshData.TypeId))) {
            string path=Path.Combine(plugins,"NcmaAnimationKernel.dll");
            _poseKernel=new(path,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))),blendSupport:true,layerSupport:true);
        }
        _scene = new(_renderer!, _cache!, play.Document.World, assets, snapshot,poseKernel:_poseKernel,play:play,
            interpolateTransforms:Ncma.Characters.CharacterComponents.HasPhysics(snapshot),rootMotion:rootMotion,animators:animators); _camera = camera ?? Guid.Empty;
    }
    public bool Pump(PlaySession play)
    {
        _state = _window!.Poll(); if (_state.CloseRequested != 0) return false;
        _input.Update(_state, _window.Events); _input.CopyGameplayBits(false, false, _held, _pressed, _released);
        double dx = _focused && _input.Focused ? _input.PointerX - _x : 0, dy = _focused && _input.Focused ? _input.PointerY - _y : 0;
        _x = _input.PointerX; _y = _input.PointerY; _focused = _input.Focused;
        play.SubmitInput(new(play.SessionId, _input.Sequence, _input.Focused, _held, _pressed, _released, dx, dy));
        return true;
    }
    public void Present(PlaySession play)
    {
        if (_state.Minimized != 0 || _state.FramebufferWidth == 0 || _state.FramebufferHeight == 0) { _window!.Wait(.01); return; }
        var configurations = play.Document.World.GetObjects().Where(o => o.Has<RenderConfiguration>()).Take(2).ToArray();
        if (configurations.Length > 1) throw new ArgumentException("ambiguous_render_configuration");
        var config = configurations.Length == 1 ? configurations[0].Get<RenderConfiguration>() : RenderConfiguration.Default(play.SessionId);
        _renderer!.Resize(_state.FramebufferWidth, _state.FramebufferHeight);
        ulong frame = ++_frame;
        var follow=Ncma.Characters.CharacterPlayRuntime.FollowView(play,_camera,_state.FramebufferWidth,_state.FramebufferHeight);
        if (!_scene!.Submit(frame, _state.FramebufferWidth, _state.FramebufferHeight, _camera,follow, exposure: config.Exposure, ambient: config.Ambient)) {
            // Empty, culled or missing-camera views clear; never substitute a reference cube.
            _pipeline!.Configure(config with {PipelineType="ncma.clear.v1",ToneExposureOverride=0,FeatureExposure=0,ReplaceToneStage=false},_state.FramebufferWidth,_state.FramebufferHeight);
            _pipeline.Submit(frame);
        }
        _renderer.Present();
    }
    public void Dispose()
    {
        if (_disposed) return;
        // Stop at the first failed release: never unload dependencies while a live native lease remains.
        _scene?.Dispose(); _scene = null; _cache?.Dispose(); _cache = null;
        _poseKernel?.Dispose(); _poseKernel=null;
        _pipeline?.Dispose(); _pipeline = null; _renderer?.Dispose(); _renderer = null;
        _window?.Dispose(); _window = null; _loader.Dispose(); _disposed = true;
    }
}
