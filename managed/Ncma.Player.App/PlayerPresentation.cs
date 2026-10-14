using Ncma.Gameplay;
using Ncma.Application.Runtime;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
namespace Ncma.Player.App;

internal sealed class PlayerPresentation(string plugins, bool visible, Func<float,float,bool,RenderPipeline>? pipelineFactory = null) : IDisposable
{
    private readonly PluginLoader _loader = new();
    private PlatformWindow? _window;
    private RendererSession? _renderer;
    private RenderPipelineService? _pipeline;
    private RenderResourceCache? _cache;
    private Ncma.Rendering.Scene.SceneRenderSession? _scene;
    private Ncma.Rendering.Scene.SceneEnvironmentPreflight? _environmentPreflight;
    private Ncma.Rendering.Scene.PostProcessRuntime? _postRuntime;
    private PreparedShadowLibrary? _highShadows;
    private Ncma.Scene.Rendering.PreparedSceneAssetLease? _assets;
    private Ncma.Animation.Native.PoseKernel? _poseKernel;
    private Ncma.Ui.Rendering.SceneUiPresentation? _ui;
    private SceneUiActions? _uiActions;
    private SceneUiInput? _uiInput;
    private readonly Ncma.Ui.Rendering.UiWindowInput _uiWindowInput = new();
    private Guid _camera;
    private readonly WindowInputAccumulator _input = new();
    private WindowState _state;
    private readonly ulong[] _held = new ulong[8], _pressed = new ulong[8], _released = new ulong[8];
    private ulong _frame;
    private double _x, _y;
    private bool _focused, _disposed;
    public PlayerModule[] Modules => _loader.Modules.Concat(_ui?.Modules ?? []).Select(m => new PlayerModule(m.Id, m.AbiMajor, m.AbiMinor, m.Capabilities)).ToArray();
    public RendererStats Stats => _renderer!.Stats;
    public void Start(RuntimeShaderFileSet? shaderFiles = null, RuntimePostProcessLibrary? postLibrary = null, Ncma.Rendering.Scene.PostProcessModules? postModules = null,RuntimeShadowLibrary? shadowLibrary=null)
    {
        _loader.Load(plugins, [
            new("ncma.platform", ModuleKind.Platform, "NcmaPlatform.dll", "NcmaPlatform.dll", 1, 0, []),
            new("ncma.renderer", ModuleKind.Renderer, "NcmaRenderer.dll", "NcmaRenderer.dll", 1, 2, ["ncma.platform"]) ]);
        _window = new(_loader.Modules.Single(m => m.Kind == ModuleKind.Platform), "NcmaPlayer — DX11 scene", visible: visible);
        _window.SetIcon(Path.Combine(AppContext.BaseDirectory, "NcmaEngine.ico"));
        _renderer = new(_loader.Modules.Single(m => m.Kind == ModuleKind.Renderer), _window, 1280, 720);
        shaderFiles?.InstallSelection(_renderer,()=>!_disposed);
        if(shadowLibrary is not null)_highShadows=shadowLibrary.Prepare(_renderer,shaderFiles??throw new ArgumentException("High shadows require checked scene shader files."),()=>!_disposed);
        if(postLibrary is not null)_postRuntime=new(_renderer,shaderFiles??throw new ArgumentException("Checked scene shaders required."),postLibrary,postModules??Ncma.Rendering.Scene.PostProcessModules.Official(),()=>!_disposed);
        _pipeline = new(_renderer);
        _cache = new(_renderer);
    }
    public void PrepareBeforeGameplay(Ncma.Scene.SceneDocument document,Ncma.Scene.Rendering.PreparedSceneAssetLease assets,Ncma.Ui.Rendering.RuntimeUiPackage? uiPackage=null,SceneUiActions? uiActions=null)
    {
        if(_environmentPreflight is not null)throw new InvalidOperationException("Startup already prepared.");
        Ncma.Rendering.Scene.ScenePostProcessPreflight.Prepare(_renderer!,document.World,document.CaptureSnapshot(),_postRuntime);
        Ncma.Rendering.Scene.ScenePipelineStartup.Prepare(_renderer!,document.World,document.CaptureSnapshot(),pipelineFactory,_postRuntime,()=>!_disposed,_highShadows);
        _environmentPreflight=Ncma.Rendering.Scene.SceneEnvironmentPreflight.Prepare(_renderer!,_cache!,document.World,assets,document.CaptureSnapshot(),()=>!_disposed);
        if (uiPackage is not null) {
            uiActions?.Validate(uiPackage.Capture());
            _ui = new(); _ui.Prepare(uiPackage, _renderer!, plugins, new(1280,720,1,default), interactive:uiActions is not null); _uiActions=uiActions;
        }
    }
    public void BindScene(PlaySession play, Ncma.Scene.Rendering.PreparedSceneAssetLease assets, Guid? camera,Ncma.Animation.IRootMotionPresentation? rootMotion=null,Ncma.Scene.Rendering.SceneAnimatorRuntime? animators=null)
    {
        if (_scene is not null) throw new InvalidOperationException("Scene already bound.");
        var snapshot=play.Document.CaptureSnapshot();
        if(snapshot.Objects.Any(o=>o.Components.Any(c=>c.TypeId==Ncma.Scene.Rendering.SkinnedMeshData.TypeId))) {
            string path=Path.Combine(plugins,"NcmaAnimationKernel.dll");
            _poseKernel=new(path,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))),blendSupport:true,layerSupport:true);
        }
        _scene = new(_renderer!, _cache!, play.Document.World, assets, snapshot,
            pipelineFactory:Ncma.Rendering.Scene.ScenePipelineStartup.Bind(_renderer!,play.Document.World,pipelineFactory,()=>!_disposed),poseKernel:_poseKernel,play:play,
            interpolateTransforms:Ncma.Characters.CharacterComponents.HasPhysics(snapshot),rootMotion:rootMotion,animators:animators,
            environmentPreparationAllowed:()=>!_disposed,postProcessing:_postRuntime,highShadows:_highShadows); _camera = camera ?? Guid.Empty;_assets=assets;
        _environmentPreflight?.Dispose();_environmentPreflight=null;
        if (_ui is not null && _uiActions is not null) _uiInput = new(_ui.Runtime,play,_uiActions);
    }
    public bool Pump(PlaySession play)
    {
        _state = _window!.Poll(); if (_state.CloseRequested != 0) return false;
        ProcessInput(play, _state, _window.Events); return true;
    }
    // Same input boundary as Pump; internal access only for bounded actual-host regression probes.
    internal void ProcessInput(PlaySession play, WindowState state, ReadOnlySpan<InputEvent> events)
    {
        _state=state; _input.Update(_state, events);
        bool captureKeyboard=false,captureMouse=false;
        if (_uiInput is not null) {
            var previous=_ui!.Runtime.PreparedViewport;
            bool resized=previous.Width!=_state.FramebufferWidth||previous.Height!=_state.FramebufferHeight||previous.Scale!=_state.ScaleX;
            if (_state.Minimized!=0 || _state.FramebufferWidth==0 || _state.FramebufferHeight==0 || resized || !_uiInput.Observe()) {
                _uiInput.Cancel(); captureKeyboard=captureMouse=true;
            } else {
                var view=new Ncma.Ui.UiViewport(_state.FramebufferWidth,_state.FramebufferHeight,_state.ScaleX,default);
                _ui!.PrepareView(view);
                var capture=_uiWindowInput.Pump(_ui.Runtime,_state,events,System.Numerics.Vector2.Zero);
                captureKeyboard=capture.CaptureKeyboard;captureMouse=capture.CapturePointer;
                if(capture.Cancelled)_uiInput.Cancel();
                else {
                    // Dynamic text/layout must prepare successfully before its proposals reach simulation.
                    try { _ui.PrepareView(view); _uiInput.Submit(_uiWindowInput.Drain(_ui.Runtime)); }
                    catch { _uiInput.Cancel(); throw; }
                }
            }
        }
        _input.CopyGameplayBits(captureKeyboard,captureMouse,_held,_pressed,_released);
        bool focused=_input.Focused&&!captureMouse;
        double dx = _focused && focused ? _input.PointerX - _x : 0, dy = _focused && focused ? _input.PointerY - _y : 0;
        _x = _input.PointerX; _y = _input.PointerY; _focused = focused;
        play.SubmitInput(new(play.SessionId, _input.Sequence, _input.Focused, _held, _pressed, _released, dx, dy));
    }
    public void Present(PlaySession play,Action<byte[],uint,uint>? verificationCapture=null)
    {
        if (_state.Minimized != 0 || _state.FramebufferWidth == 0 || _state.FramebufferHeight == 0) { _window!.Wait(.01); return; }
        var objects = play.Document.World.GetObjects();
        var configurations = objects.Where(o => o.Has<RenderConfiguration>()).Take(2).ToArray();
        if (configurations.Length > 1) throw new ArgumentException("ambiguous_render_configuration");
        var config = configurations.Length == 1 ? configurations[0].Get<RenderConfiguration>() : RenderConfiguration.Default(play.SessionId);
        _renderer!.Resize(_state.FramebufferWidth, _state.FramebufferHeight);
        ulong frame = ++_frame;
        var follow=Ncma.Characters.CharacterPlayRuntime.FollowView(play,_camera,_state.FramebufferWidth,_state.FramebufferHeight);
        var environments=objects.Where(o=>o.Has<Ncma.Scene.Rendering.EnvironmentLightingData>()).Take(2).ToArray();
        if(environments.Length>1)throw new ArgumentException("ambiguous_environment_configuration");
        var environment=environments.Length==1?environments[0].Get<Ncma.Scene.Rendering.EnvironmentLightingData>():Ncma.Scene.Rendering.EnvironmentLightingData.Off;
        try {
            _ui?.PrepareView(new(_state.FramebufferWidth,_state.FramebufferHeight,_state.ScaleX,default));
            if(_scene!.PreparedEnvironment!=environment)_scene.RefreshEnvironment(_assets!,()=>!_disposed);
            _scene.PrepareEnvironmentView(_state.FramebufferWidth,_state.FramebufferHeight,_camera,follow,config.Exposure,config.Ambient);
        } catch(Exception preparationFailure) {
            // Keep the installed scene intact for orderly disposal, but never present its stale
            // output as the requested settings. The runner records a fault and closes the host.
            try {
                _pipeline!.Configure(RenderConfiguration.Default(play.SessionId) with {PipelineType="ncma.clear.v1"},_state.FramebufferWidth,_state.FramebufferHeight);
                _pipeline.Submit(frame);
                if(verificationCapture is not null){var pixels=new byte[checked((int)(_state.FramebufferWidth*_state.FramebufferHeight*4))];_renderer.Capture(pixels);verificationCapture(pixels,_state.FramebufferWidth,_state.FramebufferHeight);}
                _renderer.Present();
            } catch(Exception clearFailure) {throw new AggregateException("Scene preparation and fail-closed clear failed.",preparationFailure,clearFailure);}
            throw;
        }
        if (!_scene!.Submit(frame, _state.FramebufferWidth, _state.FramebufferHeight, _camera,follow, exposure: config.Exposure, ambient: config.Ambient)) {
            // Empty, culled or missing-camera views clear; never substitute a reference cube.
            _pipeline!.Configure(config with {PipelineType="ncma.clear.v1",ToneExposureOverride=0,FeatureExposure=0,ReplaceToneStage=false},_state.FramebufferWidth,_state.FramebufferHeight);
            _pipeline.Submit(frame);
        }
        _ui?.Submit(frame);
        if(verificationCapture is not null){var pixels=new byte[checked((int)(_state.FramebufferWidth*_state.FramebufferHeight*4))];_renderer.Capture(pixels);verificationCapture(pixels,_state.FramebufferWidth,_state.FramebufferHeight);}
        _renderer.Present();
    }
    public void Dispose()
    {
        if (_disposed) return;
        // Stop at the first failed release: never unload dependencies while a live native lease remains.
        _uiInput?.Dispose(); _uiInput=null;
        _ui?.Dispose(); _ui = null;
        _scene?.Dispose(); _scene = null;_environmentPreflight?.Dispose();_environmentPreflight=null;_assets=null; _cache?.Dispose(); _cache = null;
        _postRuntime?.Dispose();_postRuntime=null;
        _poseKernel?.Dispose(); _poseKernel=null;
        _pipeline?.Dispose(); _pipeline = null; _renderer?.Dispose(); _renderer = null;
        _window?.Dispose(); _window = null; _loader.Dispose(); _disposed = true;
    }
}
