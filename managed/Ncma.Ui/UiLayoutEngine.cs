using System.Numerics;

namespace Ncma.Ui;

public readonly record struct UiRect(float X, float Y, float Width, float Height)
{
    public bool Contains(Vector2 p) => p.X >= X && p.Y >= Y && p.X < X + Width && p.Y < Y + Height;
    public static UiRect Intersect(UiRect a, UiRect b)
    { float x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y); return new(x, y, Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - x), Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - y)); }
}
public readonly record struct UiViewport(float Width, float Height, float Scale, UiInsets SafeArea)
{
    public void Validate()
    {
        UiCodec.Finite(Width, 1, 16384); UiCodec.Finite(Height, 1, 16384); UiCodec.Finite(Scale, .25f, 8);
        UiCodec.Finite(SafeArea.Left, 0); UiCodec.Finite(SafeArea.Right, 0); UiCodec.Finite(SafeArea.Top, 0); UiCodec.Finite(SafeArea.Bottom, 0);
        if (SafeArea.Left + SafeArea.Right >= Width / Scale || SafeArea.Top + SafeArea.Bottom >= Height / Scale) throw new ArgumentException("UI safe area exceeds logical viewport.");
    }
}
public readonly record struct UiLayoutBox(Guid Id, UiRect Rect, Matrix3x2 Transform, UiRect Clip,
    float Opacity, bool Enabled, int ParentIndex);
public interface IUiTextMetrics { Vector2 Measure(UiElement element, float availableWidth); }

// One owner-thread layout implementation shared by editor preview and runtime. Compiled topology
// is built once, not by serializing the whole document on each frame. Static repeated layouts
// return the same read-only storage. Caller must copy if retaining across a changed viewport.
public sealed class UiLayoutEngine
{
    private readonly UiDocument _document; private readonly UiDefinition _definition; private IUiTextMetrics? _text;
    private readonly Dictionary<Guid, UiElement[]> _children; private readonly Dictionary<Guid, Vector2> _measured = [];
    private readonly Dictionary<Guid, UiElement> _elements;
    private UiLayoutBox[] _boxes = []; private UiViewport? _viewport;
    public ulong LayoutPasses { get; private set; }
    public ulong MeasurePasses { get; private set; }
    public UiLayoutEngine(UiDocument document, IUiTextMetrics? text = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document)); _definition = document.Definition; _text = text;
        _elements = _definition.Elements.ToDictionary(e => e.Id);
        _children = _definition.Elements.Where(e => e.Parent != Guid.Empty).GroupBy(e => e.Parent).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Order).ToArray());
        foreach (var e in _definition.Elements) {
            var l = e.Layout;
            foreach (var child in Children(e.Id).Where(c => c.Visible)) {
                if (l.WidthMode == UiSizing.Hug && (child.Layout.WidthMode == UiSizing.Fill || child.Layout.AnchorX != 0) ||
                    l.HeightMode == UiSizing.Hug && (child.Layout.HeightMode == UiSizing.Fill || child.Layout.AnchorY != 0)) throw new ArgumentException("Cyclic UI Hug/Fill/anchor constraint.");
            }
            // Scissor clipping cannot represent a rotated ancestor. Reject explicitly rather
            // than silently disagreeing between rendering and pointer input.
            if (e.Style.Clip) for (UiElement? ancestor = e; ancestor is not null; ancestor = ancestor.Parent == Guid.Empty ? null : _elements[ancestor.Parent])
                if (ancestor.Layout.Rotation != 0) throw new ArgumentException("Rotated UI clipping requires a future stencil contract.");
        }
    }
    private IEnumerable<UiElement> Children(Guid id) => _children.GetValueOrDefault(id, []).Select(e => _elements[e.Id]);
    internal void UpdateFlags(Guid id, bool visible, bool enabled)
    {
        _document.VerifyAccess(); var e = _elements.GetValueOrDefault(id) ?? throw new ArgumentException("Unknown UI element.");
        _elements[id] = e with { Visible = visible, Enabled = enabled };
        for (UiElement? current = e; current is not null; current = current.Parent == Guid.Empty ? null : _elements[current.Parent]) _measured.Remove(current.Id);
        _viewport = null;
    }
    internal void UpdateText(Guid id, string text, IUiTextMetrics metrics)
    {
        _document.VerifyAccess(); UiCodec.Text(text, 16384); ArgumentNullException.ThrowIfNull(metrics);
        if (!_elements.TryGetValue(id, out var e) || e.Kind is not (UiKind.Text or UiKind.Button or UiKind.TextInput)) throw new ArgumentException("UI text element required.");
        _text = metrics; _elements[id] = e with { Text = text };
        for (UiElement? current = e; current is not null; current = current.Parent == Guid.Empty ? null : _elements[current.Parent]) _measured.Remove(current.Id);
        _viewport = null;
    }
    public ReadOnlySpan<UiLayoutBox> Layout(UiViewport viewport)
    {
        _document.VerifyAccess(); viewport.Validate(); if (_viewport == viewport) return _boxes;
        float width = viewport.Width / viewport.Scale, height = viewport.Height / viewport.Scale;
        // Intrinsic measurements are document-dependent. They survive viewport-only changes.
        Measure(_elements[_definition.Root]);
        var boxes = new List<UiLayoutBox>(_definition.Elements.Length);
        var content = new UiRect(viewport.SafeArea.Left, viewport.SafeArea.Top,
            width - viewport.SafeArea.Left - viewport.SafeArea.Right, height - viewport.SafeArea.Top - viewport.SafeArea.Bottom);
        Arrange(_elements[_definition.Root], content, Matrix3x2.Identity, new(0, 0, width, height), 1, true, -1, false);
        _boxes = boxes.ToArray(); _viewport = viewport; LayoutPasses++; return _boxes;

        void Arrange(UiElement e, UiRect slot, Matrix3x2 parent, UiRect clip, float opacity, bool enabled, int parentIndex, bool flowSlot)
        {
            e = _elements[e.Id];
            if (!e.Visible) return;
            var l = e.Layout; var intrinsic = Measure(e);
            float w = Size(l.WidthMode, l.Width, intrinsic.X, slot.Width, l.MinWidth, l.MaxWidth);
            float h = Size(l.HeightMode, l.Height, intrinsic.Y, slot.Height, l.MinHeight, l.MaxHeight);
            float x = slot.X + (flowSlot ? 0 : l.X + l.AnchorX * (slot.Width - w));
            float y = slot.Y + (flowSlot ? 0 : l.Y + l.AnchorY * (slot.Height - h));
            var local = Matrix3x2.CreateRotation(l.Rotation * MathF.PI / 180, new(w / 2, h / 2)) * Matrix3x2.CreateTranslation(x, y);
            var transform = local * parent;
            float ownOpacity = e.Style.OpacityToken == Guid.Empty ? e.Style.Opacity : _definition.Tokens.First(t => t.Id == e.Style.OpacityToken).Scalar;
            opacity *= ownOpacity; enabled &= e.Enabled;
            int index = boxes.Count; boxes.Add(new(e.Id, new(0, 0, w, h), transform, clip, opacity, enabled, parentIndex));
            if (e.Style.Clip) clip = UiRect.Intersect(clip, Bounds(w, h, transform));
            var padding = l.Padding; var inner = new UiRect(padding.Left, padding.Top, Math.Max(0, w - padding.Left - padding.Right), Math.Max(0, h - padding.Top - padding.Bottom));
            var children = Children(e.Id).Where(c => c.Visible).ToArray(); bool horizontal = l.Flow == UiFlow.Horizontal;
            if (l.Flow == UiFlow.Free) { foreach (var child in children) Arrange(child, inner, transform, clip, opacity, enabled, index, false); return; }
            float fixedTotal = Math.Max(0, children.Length - 1) * l.Gap; int fills = 0;
            foreach (var child in children) {
                var c = child.Layout; var m = Measure(child); var mode = horizontal ? c.WidthMode : c.HeightMode;
                if (mode == UiSizing.Fill) fills++;
                else fixedTotal += horizontal ? Size(c.WidthMode, c.Width, m.X, inner.Width, c.MinWidth, c.MaxWidth) : Size(c.HeightMode, c.Height, m.Y, inner.Height, c.MinHeight, c.MaxHeight);
            }
            float share = fills == 0 ? 0 : Math.Max(0, (horizontal ? inner.Width : inner.Height) - fixedTotal) / fills;
            float cursor = horizontal ? inner.X : inner.Y;
            foreach (var child in children) {
                var c = child.Layout; var m = Measure(child);
                float cw = Size(c.WidthMode, c.Width, m.X, horizontal && c.WidthMode == UiSizing.Fill ? share : inner.Width, c.MinWidth, c.MaxWidth);
                float ch = Size(c.HeightMode, c.Height, m.Y, !horizontal && c.HeightMode == UiSizing.Fill ? share : inner.Height, c.MinHeight, c.MaxHeight);
                Arrange(child, horizontal ? new(cursor, inner.Y, cw, inner.Height) : new(inner.X, cursor, inner.Width, ch), transform, clip, opacity, enabled, index, true);
                cursor += (horizontal ? cw : ch) + l.Gap;
            }
        }
    }
    private Vector2 Measure(UiElement e)
    {
        e = _elements[e.Id];
        if (_measured.TryGetValue(e.Id, out var result)) return result;
        var l = e.Layout; float w = 0, h = 0; int count = 0;
        if (e.Kind is UiKind.Text or UiKind.Button or UiKind.TextInput && (l.WidthMode == UiSizing.Hug || l.HeightMode == UiSizing.Hug)) {
            if (_text is null) throw new InvalidOperationException("Intrinsic UI text requires prepared font metrics.");
            var measured = _text.Measure(e, l.WidthMode == UiSizing.Fixed ? l.Width : l.MaxWidth);
            UiCodec.Finite(measured.X, 0); UiCodec.Finite(measured.Y, 0); w = measured.X; h = measured.Y;
        }
        foreach (var child in Children(e.Id)) {
            if (!child.Visible) continue;
            var c = child.Layout; var m = Measure(child);
            float cw = c.WidthMode == UiSizing.Hug ? m.X : c.WidthMode == UiSizing.Fill ? c.MinWidth : c.Width;
            float ch = c.HeightMode == UiSizing.Hug ? m.Y : c.HeightMode == UiSizing.Fill ? c.MinHeight : c.Height;
            cw = Math.Clamp(cw, c.MinWidth, c.MaxWidth); ch = Math.Clamp(ch, c.MinHeight, c.MaxHeight);
            if (l.Flow == UiFlow.Horizontal) { w += cw; h = Math.Max(h, ch); }
            else if (l.Flow == UiFlow.Vertical) { w = Math.Max(w, cw); h += ch; }
            else { w = Math.Max(w, c.X + cw); h = Math.Max(h, c.Y + ch); }
            count++;
        }
        if (l.Flow == UiFlow.Horizontal) w += Math.Max(0, count - 1) * l.Gap;
        if (l.Flow == UiFlow.Vertical) h += Math.Max(0, count - 1) * l.Gap;
        w += l.Padding.Left + l.Padding.Right; h += l.Padding.Top + l.Padding.Bottom;
        UiCodec.Finite(w, 0); UiCodec.Finite(h, 0); result = new(w, h); _measured.Add(e.Id, result); MeasurePasses++; return result;
    }
    private static float Size(UiSizing mode, float fixedSize, float measured, float available, float min, float max) => Math.Clamp(mode switch { UiSizing.Fixed => fixedSize, UiSizing.Hug => measured, _ => available }, min, max);
    public static UiRect Bounds(float width, float height, Matrix3x2 transform)
    {
        Vector2 a = Vector2.Transform(Vector2.Zero, transform), b = Vector2.Transform(new(width, 0), transform), c = Vector2.Transform(new(0, height), transform), d = Vector2.Transform(new(width, height), transform);
        float x = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)), y = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        return new(x, y, Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) - x, Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) - y);
    }
}
