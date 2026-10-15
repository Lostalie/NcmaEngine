using System.Numerics;
using Ncma.Interop;
using Ncma.Rendering;
using Ncma.Text;

namespace Ncma.Ui.Rendering;

// Typed, fixed safe diagnostic; a host may explain this admission refusal without
// forwarding arbitrary trusted callback exception messages to its public output.
public sealed class SceneUiRoutesRequiredException : NotSupportedException
{
    public const string Diagnostic="Scene HUD interaction requires trusted application startup routes; use passive elements by default.";
    public SceneUiRoutesRequiredException():base(Diagnostic){}
}

// HUD presentation only. No World, Editor, gameplay callback, author file or AI execution owner.
public sealed class SceneUiPresentation : IDisposable
{
    private readonly UiTextResources _textResources;
    private readonly bool _ownsTextResources;
    public SceneUiPresentation(UiTextResources? textResources = null)
    { _textResources=textResources??new(); _ownsTextResources=textResources is null; }
    private TextService? _text;
    private UiCanvas? _canvas;
    private UiRuntime? _runtime;
    private RuntimeUiPackage? _package;
    private RendererSession? _renderer;
    private UiRenderTarget? _surface;
    private UiPresentationLease? _surfaceLease;
    private readonly List<IDisposable> _retained = [];
    private ulong _surfacePreparation, _surfaceContent, _surfaceRuntimeRevision;
    private bool _surfaceReady, _surfaceDirty, _surfaceRecovery;
    private bool _disposed;
    public ulong Preparations => _canvas?.Preparations ?? 0;
    public ulong TextPreparations => _text?.PrepareCalls ?? 0;
    public bool TextLoaded => _text is not null;
    public IReadOnlyList<PluginModule> Modules => _textResources.Modules;
    public UiRuntime Runtime { get { ObjectDisposedException.ThrowIf(_disposed, this); var value = _runtime ?? _canvas?.Runtime ?? throw new InvalidOperationException("Prepare HUD first."); _ = value.Stamp; return value; } }
    public static void Validate(RuntimeUiPackage package, bool interactive = false)
    {
        // Do not accept apparently interactive UI that this formal entry cannot dispatch yet.
        if (!interactive && package.Capture().Elements.Any(e => e.Action.Length != 0 || e.Kind is UiKind.Button or UiKind.Slider or UiKind.TextInput or UiKind.ScrollView))
            throw new SceneUiRoutesRequiredException();
    }
    public void Prepare(RuntimeUiPackage package, RendererSession renderer, string plugins, UiViewport viewport, bool interactive = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_canvas is not null || _text is not null) throw new InvalidOperationException("HUD startup already prepared.");
        if(_package is not null&&!ReferenceEquals(_package,package))throw new ArgumentException("HUD graphics recovery requires the original pinned package.");
        Validate(package, interactive);
        _package=package;
        _renderer = renderer;
        if (package.HasFonts) {
            _text = _textResources.Get(plugins);
        }
        try { _canvas = _runtime is null?UiCanvas.Create(new(package.Capture()), renderer, _text, package.Fonts, package.Images):UiCanvas.Recreate(_runtime,renderer,_text,package.Fonts,package.Images); }
        catch (UiCanvasRecoveryException failure) { _canvas = failure.Owner; throw; }
        _runtime=_canvas.Runtime;
        _canvas.Prepare(viewport); // Includes native font/shaping validation before gameplay.
    }
    public void PrepareView(UiViewport viewport)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_canvas is null) throw new InvalidOperationException("Prepare HUD before its view.");
        if (_surfaceLease is not null) throw new InvalidOperationException("Release HUD presentation before preparing.");
        ulong previous = _canvas.Preparations;
        try { _canvas.Prepare(viewport); }
        catch { _surfaceReady = false; throw; }
        if (_canvas.Preparations != previous) _surfaceReady = false;
    }
    public void Submit(ulong frame, bool overlay = true) { ObjectDisposedException.ThrowIf(_disposed, this); _canvas!.Submit(frame, Vector4.Zero, overlay); }

    // Explicit Editor viewport path. A Player using Submit never allocates an offscreen target.
    // The host owns GUI submission and must release the lease AFTER the GUI has consumed it.
    public void PrepareSurface(UiViewport viewport)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_surfaceRecovery) throw new InvalidOperationException("HUD surface requires explicit close recovery.");
        if (_canvas is null) throw new InvalidOperationException("Prepare HUD before its surface.");
        _ = _canvas.Runtime.Stamp; // Verify owner even when the cached state needs no GPU work.
        if (_surfaceLease is not null) throw new InvalidOperationException("Release HUD presentation before preparing.");
        _surfaceReady = false;
        PrepareView(viewport);
        uint width = checked((uint)Math.Ceiling(viewport.Width)), height = checked((uint)Math.Ceiling(viewport.Height));
        if (_surface is null || _surface.Width != width || _surface.Height != height) {
            var candidate = _renderer!.CreateUiTarget(width, height);
            try { _surface?.Dispose(); }
            catch { _retained.Add(candidate); _surfaceRecovery = true; throw; }
            _surface = candidate; _surfaceContent = 0; _surfaceDirty = true;
        }
        _surfaceDirty |= _surfacePreparation != _canvas.Preparations;
        _surfacePreparation = _canvas.Preparations; _surfaceRuntimeRevision = _canvas.Runtime.PresentationRevision; _surfaceReady = true;
    }
    public GuiCachedImageToken AcquireSurface(ulong frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_canvas is null) throw new InvalidOperationException("Prepare HUD before presentation.");
        _ = _canvas.Runtime.Stamp;
        if (frame == 0) throw new ArgumentOutOfRangeException(nameof(frame));
        if (!_surfaceReady || _surfaceRecovery || _surface is null || _surfacePreparation != _canvas.Preparations || _surfaceRuntimeRevision != _canvas.Runtime.PresentationRevision)
            throw new InvalidOperationException("Prepare current HUD surface before presentation.");
        if (_surfaceLease is not null) throw new InvalidOperationException("HUD presentation already acquired.");
        if (_surfaceDirty) {
            ulong content = checked(_surfaceContent + 1);
            _canvas.Produce(_surface, frame, content, Vector4.Zero);
            _surfaceContent = content; _surfaceDirty = false;
        }
        _surfaceLease = _renderer!.AcquireUiPresentation(_surface, _surfaceContent, frame);
        return _surfaceLease.Token;
    }
    public void ReleaseSurface()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_canvas is not null) _ = _canvas.Runtime.Stamp;
        _surfaceLease?.Dispose(); _surfaceLease = null;
    }
    public byte[] CaptureSurface()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_surfaceReady || _surfaceDirty || _surface is null || _surfaceContent == 0 || _surfacePreparation != _canvas!.Preparations || _surfaceRuntimeRevision != _canvas.Runtime.PresentationRevision)
            throw new InvalidOperationException("Produce current HUD surface before capture.");
        return _renderer!.CaptureUiTarget(_surface);
    }
    // Explicit GPU-only retirement. Preserve the same CPU widget values and original package;
    // stale input is revoked, and no old surface remains presentable after a failed close.
    public void RetireGraphics()
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(_canvas is not null)_runtime??=_canvas.Runtime;
        _runtime?.CancelInput();_surfaceReady=false;
        ReleaseSurface(); _surface?.Dispose(); _surface = null;
        while (_retained.Count != 0) { _retained[0].Dispose(); _retained.RemoveAt(0); }
        _canvas?.Dispose(); _canvas = null; _text = null; _renderer=null;
        _surfacePreparation=_surfaceContent=_surfaceRuntimeRevision=0;
        _surfaceDirty=_surfaceRecovery=false;
    }
    public void Dispose()
    {
        if (_disposed) return;
        RetireGraphics();if(_ownsTextResources)_textResources.Dispose();_runtime=null;_package=null;_disposed=true;
    }
}
