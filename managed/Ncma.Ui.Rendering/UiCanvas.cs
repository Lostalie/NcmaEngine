using System.Numerics;
using Ncma.Rendering;
using Ncma.Text;

namespace Ncma.Ui.Rendering;

public sealed class UiImageAsset
{
    private readonly byte[] _rgba;
    public Guid Id { get; } public uint Width { get; } public uint Height { get; }
    public UiImageAsset(Guid id, uint width, uint height, ReadOnlySpan<byte> rgba)
    {
        if (id == Guid.Empty || width == 0 || height == 0 || width > 4096 || height > 4096 || (ulong)width * height * 4 != (ulong)rgba.Length) throw new ArgumentException("UI image identity/RGBA budget.");
        Id = id; Width = width; Height = height; _rgba = rgba.ToArray();
    }
    internal ReadOnlySpan<byte> Rgba => _rgba;
}

// Shared Editor/Player UI composition. The host prepares it OUTSIDE simulation/render submission.
// Assets are immutable copied data resolved by trusted UUID adapters, never paths from UI/Agent.
// Static Prepare is O(1), no document serialization/shaping/upload. Dynamic changes prepare a new
// display list while retaining the old candidate until validation/resource preparation succeeds.
public sealed class UiCanvas : IDisposable, IUiTextMetrics
{
    private sealed record Run(PreparedText Text, UiGpuImage Image, float Width, float Height, float Scale, float Size);
    private readonly UiDefinition _definition; private readonly RendererSession _renderer; private readonly TextService? _text;
    private readonly Dictionary<Guid, PreparedFont> _fonts = [];
    private readonly Dictionary<Guid, UiGpuImage> _images = [];
    private readonly Dictionary<Guid, Run> _runs = [];
    // Explicit retention on failed GPU/resource close; Dispose can be retried, no unload after failure.
    private readonly List<IDisposable> _retained = [];
    private readonly UiGpuImage _white;
    private UiGpuList? _list; private UiViewport? _preparedViewport; private ulong _preparedRevision = ulong.MaxValue;
    private bool _disposed;
    public UiRuntime Runtime { get; }
    public ulong Preparations { get; private set; }
    private UiCanvas(UiDocument document, RendererSession renderer, TextService? text)
    {
        ArgumentNullException.ThrowIfNull(document); _definition = document.Capture(); _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer)); _text = text;
        Runtime = new(document, this); _white = renderer.CreateUiImage(1, 1, new byte[] { 255, 255, 255, 255 });
    }
    public static UiCanvas Create(UiDocument document, RendererSession renderer, TextService? text,
        IReadOnlyDictionary<Guid, FontAsset>? fonts = null, IReadOnlyDictionary<Guid, UiImageAsset>? images = null)
    {
        var canvas = new UiCanvas(document, renderer, text);
        try { canvas.LoadAssets(fonts, images); return canvas; }
        catch (Exception creation) when (creation is not OutOfMemoryException) {
            try { canvas.Dispose(); }
            catch (Exception close) when (close is not OutOfMemoryException) { throw new UiCanvasRecoveryException(canvas, creation, close); }
            throw;
        }
    }
    private void LoadAssets(IReadOnlyDictionary<Guid, FontAsset>? fonts, IReadOnlyDictionary<Guid, UiImageAsset>? images)
    {
            foreach (Guid id in _definition.Elements.Select(e => e.Font).Where(id => id != Guid.Empty).Distinct()) {
                if (_text is null || fonts is null || !fonts.TryGetValue(id, out var font) || font.Id != id) throw new ArgumentException("Prepared explicit font assets required.");
                _fonts.Add(id, _text.CreateFont(font));
            }
            foreach (Guid id in _definition.Elements.Select(e => e.Image).Where(id => id != Guid.Empty).Distinct()) {
                if (images is null || !images.TryGetValue(id, out var image) || image.Id != id) throw new ArgumentException("Prepared explicit image assets required.");
                _images.Add(id, _renderer.CreateUiImage(image.Width, image.Height, image.Rgba));
            }
    }
    public Vector2 Measure(UiElement element, float availableWidth)
    {
        if (element.Text.Length == 0) return new(0, element.FontSize);
        if (!_runs.TryGetValue(element.Id, out var run) || run.Text.Text != element.Text || run.Text.FontId != element.Font || run.Size != element.FontSize) throw new InvalidOperationException("Matching prepared text required.");
        return new(run.Text.Metrics.ContentWidth, run.Text.Metrics.ContentHeight);
    }
    public void Prepare(UiViewport viewport)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); _ = Runtime.Stamp; viewport.Validate();
        if (_preparedViewport == viewport && _preparedRevision == Runtime.PresentationRevision) return;
        _preparedRevision = ulong.MaxValue; // A failed new preparation cannot submit an old list as current UI.
        foreach (var e in _definition.Elements.Where(e => e.Kind is UiKind.Text or UiKind.Button or UiKind.TextInput)) {
            string content = Runtime.Text(e.Id);
            if (content.Length == 0) { Runtime.PreparedText(e.Id, this); continue; }
            float w = e.Layout.WidthMode == UiSizing.Fixed ? Math.Max(1, e.Layout.Width) : Math.Clamp(viewport.Width / viewport.Scale, 1, Math.Min(e.Layout.MaxWidth, 4096 / viewport.Scale));
            float h = e.Layout.HeightMode == UiSizing.Fixed ? Math.Max(1, e.Layout.Height) : Math.Clamp(viewport.Height / viewport.Scale, 1, Math.Min(e.Layout.MaxHeight, 4096 / viewport.Scale));
            PrepareRun(e, content, w, h, viewport.Scale); Runtime.PreparedText(e.Id, this);
        }
        // Final sized wrapping is resolved in a bounded second pass. A later incompatible intrinsic
        // dependency fails explicitly rather than entering an unbounded layout/shaping loop.
        var boxes = Runtime.Layout(viewport).ToArray();
        foreach (var box in boxes) {
            var e = _definition.Elements.First(e => e.Id == box.Id);
            if (!_runs.TryGetValue(e.Id, out var run)) continue;
            if (e.Layout.WidthMode == UiSizing.Fill && run.Width != Math.Max(1, box.Rect.Width) || e.Layout.HeightMode == UiSizing.Fill && run.Height != Math.Max(1, box.Rect.Height)) {
                PrepareRun(e, run.Text.Text, Math.Max(1, box.Rect.Width), Math.Max(1, box.Rect.Height), viewport.Scale); Runtime.PreparedText(e.Id, this);
            }
        }
        boxes = Runtime.Layout(viewport).ToArray(); var builder = new UiDisplayListBuilder(_renderer);
        foreach (var box in boxes) {
            var e = _definition.Elements.First(e => e.Id == box.Id); var fill = Resolve(e.Style.FillToken, e.Style.Fill); var foreground = Resolve(e.Style.ForegroundToken, e.Style.Foreground);
            if (fill.A > 0) builder.Quad(box, _white, fill, viewport.Scale, e.Style.CornerRadius);
            if (e.Kind == UiKind.Image) {
                if (e.Image == Guid.Empty || !_images.TryGetValue(e.Image, out var image)) throw new ArgumentException("Missing UI image asset.");
                builder.Quad(box, image, UiColor.White, viewport.Scale, e.Style.CornerRadius);
            }
            if (e.Kind == UiKind.Slider) {
                float value = Runtime.Widget(e.Id).Value;
                if (value > 0) builder.Quad(box with { Rect = box.Rect with { Width = box.Rect.Width * value } }, _white, foreground, viewport.Scale, e.Style.CornerRadius);
            }
            if (_runs.TryGetValue(e.Id, out var text) && (e.Kind != UiKind.TextInput || Runtime.Widget(e.Id).Text.Length != 0)) {
                float w = Math.Min(box.Rect.Width, text.Width), h = Math.Min(box.Rect.Height, text.Height);
                builder.Quad(box with { Rect = new(0, 0, w, h) }, text.Image, foreground, viewport.Scale, 0, new(0, 0, w / text.Width, h / text.Height));
            }
            if (Runtime.Focus == e.Id) {
                // Stable visible focus; no wall-clock blinking/extra per-frame invalidation.
                float thickness = Math.Min(2, Math.Min(box.Rect.Width, box.Rect.Height));
                builder.Quad(box with { Rect = new(0, 0, box.Rect.Width, thickness) }, _white, foreground, viewport.Scale);
                builder.Quad(box with { Rect = new(0, 0, thickness, box.Rect.Height) }, _white, foreground, viewport.Scale);
            }
        }
        if (builder.VertexCount == 0) builder.Quad(new(Guid.NewGuid(), new(0, 0, 1, 1), Matrix3x2.Identity, new(0, 0, 1, 1), 1, true, -1), _white, UiColor.Transparent);
        UiGpuList candidate = builder.Build(); UiGpuList? previous = _list;
        try { previous?.Dispose(); } catch { _retained.Add(candidate); throw; }
        _list = candidate; _preparedViewport = viewport; _preparedRevision = Runtime.PresentationRevision; Preparations++;
        while (_retained.Count != 0) { _retained[0].Dispose(); _retained.RemoveAt(0); }
    }
    private void PrepareRun(UiElement e, string content, float width, float height, float scale)
    {
        if (_runs.TryGetValue(e.Id, out var old) && old.Text.Text == content && old.Width == width && old.Height == height && old.Scale == scale && old.Size == e.FontSize) return;
        if (_text is null || !_fonts.TryGetValue(e.Font, out var font)) throw new ArgumentException("Text has no explicit prepared font asset.");
        var text = _text.Prepare(font, content, e.FontSize, width, height, scale);
        UiGpuImage image;
        try { image = _renderer.CreateUiImage(text.Metrics.Width, text.Metrics.Height, text.CopyRgba()); }
        catch { text.Dispose(); throw; }
        if (old is not null) { _retained.Add(old.Image); _retained.Add(old.Text); }
        _runs[e.Id] = new(text, image, width, height, scale, e.FontSize);
    }
    private UiColor Resolve(Guid token, UiColor fallback) => token == Guid.Empty ? fallback : _definition.Tokens.First(t => t.Id == token).Color;
    public void Submit(ulong frame, Vector4 clear, bool overlay = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_list is null || _preparedRevision != Runtime.PresentationRevision) throw new InvalidOperationException("Prepare changed UI before rendering.");
        _renderer.SubmitUi(_list, frame, clear, overlay);
    }
    public void Produce(UiRenderTarget target,ulong frame,ulong contentRevision,Vector4 clear)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(_list is null||_preparedRevision!=Runtime.PresentationRevision)throw new InvalidOperationException("Prepare changed UI before rendering.");
        _renderer.SubmitUiTarget(target,_list,frame,contentRevision,clear);
    }
    public void Dispose()
    {
        if (_disposed) return; _ = Runtime.Stamp;
        _list?.Dispose(); _list = null;
        // Stop at the first failure. All remaining owners stay reachable for explicit retry.
        while (_retained.Count != 0) { _retained[0].Dispose(); _retained.RemoveAt(0); }
        foreach (var id in _runs.Keys.ToArray()) { var r = _runs[id]; r.Image.Dispose(); r.Text.Dispose(); _runs.Remove(id); }
        foreach (var id in _images.Keys.ToArray()) { _images[id].Dispose(); _images.Remove(id); }
        foreach (var id in _fonts.Keys.ToArray()) { _fonts[id].Dispose(); _fonts.Remove(id); }
        _white.Dispose(); _disposed = true;
    }
}

public sealed class UiCanvasRecoveryException(UiCanvas owner, Exception creation, Exception close)
    : AggregateException("UI initialization close failed; retain the owner and retry Dispose before plugin unload.", creation, close)
{ public UiCanvas Owner { get; } = owner; }
