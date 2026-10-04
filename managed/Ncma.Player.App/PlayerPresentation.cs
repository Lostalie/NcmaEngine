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
    private readonly WindowInputAccumulator _input = new();
    private WindowState _state;
    private readonly ulong[] _held = new ulong[8], _pressed = new ulong[8], _released = new ulong[8];
    private readonly float[] _model = new float[16];
    private ulong _frame;
    private double _x, _y;
    private bool _focused, _disposed;
    public PlayerModule[] Modules => _loader.Modules.Select(m => new PlayerModule(m.Id, m.AbiMajor, m.AbiMinor, m.Capabilities)).ToArray();
    public RendererStats Stats => _renderer!.Stats;
    public void Start()
    {
        _loader.Load(plugins, [
            new("ncma.platform", ModuleKind.Platform, "NcmaPlatform.dll", "NcmaPlatform.dll", 1, 0, []),
            new("ncma.renderer", ModuleKind.Renderer, "NcmaRenderer.dll", "NcmaRenderer.dll", 1, 1, ["ncma.platform"]) ]);
        _window = new(_loader.Modules.Single(m => m.Kind == ModuleKind.Platform), "NcmaPlayer — DX11 reference", visible: visible);
        _window.SetIcon(Path.Combine(AppContext.BaseDirectory, "NcmaEngine.ico"));
        _renderer = new(_loader.Modules.Single(m => m.Kind == ModuleKind.Renderer), _window, 1280, 720);
        _pipeline = new(_renderer);
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
        _pipeline!.Configure(config, _state.FramebufferWidth, _state.FramebufferHeight);
        var objects = play.RenderView.Objects;
        if (objects.Length != 0)
        {
            var item = objects[0];
            new Ncma.Rendering.RenderFrameView(play.Document.World.Identity, play.Document.Revision, item.ObjectId, item.Transform).CopyColumnMajorModel(_model);
            _pipeline.Submit(++_frame, model: _model);
        }
        else _pipeline.Submit(++_frame);
        _renderer.Present();
    }
    public void Dispose()
    {
        if (_disposed) return;
        // Stop at the first failed release: never unload dependencies while a live native lease remains.
        _pipeline?.Dispose(); _pipeline = null; _renderer?.Dispose(); _renderer = null;
        _window?.Dispose(); _window = null; _loader.Dispose(); _disposed = true;
    }
}
