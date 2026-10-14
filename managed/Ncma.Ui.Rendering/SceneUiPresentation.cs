using System.Numerics;
using Ncma.Interop;
using Ncma.Rendering;
using Ncma.Text;

namespace Ncma.Ui.Rendering;

// Display-only HUD entry. No World, Editor, gameplay callback, author file or AI execution owner.
public sealed class SceneUiPresentation : IDisposable
{
    private readonly PluginLoader _loader = new();
    private TextService? _text;
    private UiCanvas? _canvas;
    private bool _disposed;
    public ulong Preparations => _canvas?.Preparations ?? 0;
    public ulong TextPreparations => _text?.PrepareCalls ?? 0;
    public bool TextLoaded => _text is not null;
    public IReadOnlyList<PluginModule> Modules => _loader.Modules;
    public static void Validate(RuntimeUiPackage package)
    {
        // Do not accept apparently interactive UI that this formal entry cannot dispatch yet.
        if (package.Capture().Elements.Any(e => e.Action.Length != 0 || e.Kind is UiKind.Button or UiKind.Slider or UiKind.TextInput or UiKind.ScrollView))
            throw new NotSupportedException("Scene HUD input/action routing is not implemented; use passive elements.");
    }
    public void Prepare(RuntimeUiPackage package, RendererSession renderer, string plugins, UiViewport viewport)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_canvas is not null || _text is not null) throw new InvalidOperationException("HUD startup already prepared.");
        Validate(package);
        if (package.HasFonts) {
            _loader.Load(plugins, [new("ncma.text", ModuleKind.Text, "NcmaText.dll", "NcmaText.dll", 1, 0, [])]);
            _text = new(_loader.Modules.Single());
        }
        try { _canvas = UiCanvas.Create(new(package.Capture()), renderer, _text, package.Fonts, package.Images); }
        catch (UiCanvasRecoveryException failure) { _canvas = failure.Owner; throw; }
        _canvas.Prepare(viewport); // Includes native font/shaping validation before gameplay.
    }
    public void PrepareView(UiViewport viewport) { ObjectDisposedException.ThrowIf(_disposed, this); _canvas!.Prepare(viewport); }
    public void Submit(ulong frame, bool overlay = true) { ObjectDisposedException.ThrowIf(_disposed, this); _canvas!.Submit(frame, Vector4.Zero, overlay); }
    public void Dispose()
    {
        if (_disposed) return;
        _canvas?.Dispose(); _canvas = null; _text?.Dispose(); _text = null; _loader.Dispose(); _disposed = true;
    }
}
