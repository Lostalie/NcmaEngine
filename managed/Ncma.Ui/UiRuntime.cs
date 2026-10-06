using System.Globalization;
using System.Numerics;

namespace Ncma.Ui;

public enum UiInputKind { PointerMove, PointerDown, PointerUp, Tab, Activate, Text, Backspace, Left, Right, Scroll, FocusLost, Composition, CompositionCancel }
public readonly record struct UiInput(UiInputKind Kind, ulong Sequence, Vector2 Position, string Text = "", bool Reverse = false, float Delta = 0);
public readonly record struct UiStamp(Guid Instance, ulong Generation, Guid PlaySession, Guid World, ulong Tick);
public readonly record struct UiAction(UiStamp Stamp, ulong Sequence, Guid Element, string Action, UiInputKind Kind, float Value, string Text);
public readonly record struct UiWidgetState(string Text, int Caret, string Composition, float Value, float Scroll);

// C# runtime widgets, separate from authoring data and ImGui. No World/GameObject references,
// callbacks, IO or native calls. Events are copied proposals for host validation at safe boundaries.
public sealed class UiRuntime
{
    public const int MaxQueuedActions = 128, MaxInputBatch = 256;
    private readonly UiDocument _document; private readonly UiLayoutEngine _layout; private readonly Dictionary<Guid, UiElement> _elements;
    private readonly Dictionary<Guid, UiWidgetState> _state = [];
    private readonly Queue<UiAction> _actions = []; private UiLayoutBox[] _boxes = [];
    private UiViewport _viewport; private ulong _lastSequence, _presentation, _layoutRevision = ulong.MaxValue;
    private Guid _focus, _capture; private UiStamp _stamp;
    public UiRuntime(UiDocument document, IUiTextMetrics? metrics = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document)); _layout = new(document, metrics);
        _elements = document.Definition.Elements.ToDictionary(e => e.Id);
        foreach (var e in _elements.Values.Where(e => e.Kind == UiKind.ScrollView))
            for (UiElement? ancestor = e; ancestor is not null; ancestor = ancestor.Parent == Guid.Empty ? null : _elements[ancestor.Parent])
                if (ancestor.Layout.Rotation != 0) throw new ArgumentException("Rotated scroll clipping unsupported.");
        _stamp = new(document.InstanceId, 1, Guid.Empty, Guid.Empty, 0);
        foreach (var e in _elements.Values) if (e.Kind is UiKind.TextInput or UiKind.Slider or UiKind.ScrollView) _state.Add(e.Id, new(e.Text, e.Text.Length, "", 0, 0));
    }
    public UiStamp Stamp { get { _document.VerifyAccess(); return _stamp; } }
    public Guid Focus { get { _document.VerifyAccess(); return _focus; } }
    public Guid Capture { get { _document.VerifyAccess(); return _capture; } }
    public ulong PresentationRevision { get { _document.VerifyAccess(); return _presentation; } }
    public int QueuedActions { get { _document.VerifyAccess(); return _actions.Count; } }
    public UiWidgetState Widget(Guid id) { _document.VerifyAccess(); return _state.TryGetValue(id, out var state) ? state : throw new ArgumentException("Stateful widget required."); }
    public UiElement Element(Guid id) { _document.VerifyAccess(); return _elements.GetValueOrDefault(id) ?? throw new ArgumentException("Unknown UI element."); }
    public string Text(Guid id) { var e = Element(id); return e.Kind == UiKind.TextInput ? _state[id].Text : e.Text; }
    public void SetText(Guid id, string text)
    {
        _document.VerifyAccess(); UiCodec.Text(text, 16384); var e = Element(id);
        if (e.Kind is not (UiKind.Text or UiKind.Button or UiKind.TextInput)) throw new ArgumentException("Text widget required.");
        if (Text(id) == text) return;
        if (e.Kind == UiKind.TextInput) _state[id] = _state[id] with { Text = text, Caret = text.Length, Composition = "" };
        else _elements[id] = e with { Text = text };
        _presentation = checked(_presentation + 1);
    }
    public void SetFlags(Guid id, bool visible, bool enabled)
    {
        _document.VerifyAccess(); var e = Element(id); if (e.Visible == visible && e.Enabled == enabled) return;
        _elements[id] = e with { Visible = visible, Enabled = enabled }; _layout.UpdateFlags(id, visible, enabled);
        _presentation = checked(_presentation + 1); _layoutRevision = ulong.MaxValue;
        _actions.Clear(); // Old interaction proposals never cross a changed visible/enabled topology.
        _focus = _capture = Guid.Empty; // Safe conservative cancellation on changed interaction topology.
    }
    public void SetValue(Guid id, float value)
    {
        _document.VerifyAccess(); UiCodec.Finite(value, 0, 1); var e = Element(id);
        if (e.Kind != UiKind.Slider) throw new ArgumentException("Slider required.");
        if (_state[id].Value == value) return; _state[id] = _state[id] with { Value = value }; _presentation = checked(_presentation + 1);
    }
    public void PreparedText(Guid elementId, IUiTextMetrics metrics)
    {
        _document.VerifyAccess(); var e = _elements.GetValueOrDefault(elementId) ?? throw new ArgumentException("Missing UI text element.");
        _layout.UpdateText(elementId, Text(elementId), metrics); _layoutRevision = ulong.MaxValue;
    }
    public void Bind(Guid playSession, Guid world, ulong committedTick)
    {
        _document.VerifyAccess(); if ((playSession == Guid.Empty) != (world == Guid.Empty)) throw new ArgumentException("UI Play/World identity pair required.");
        if (playSession != _stamp.PlaySession || world != _stamp.World) {
            _actions.Clear(); _capture = _focus = Guid.Empty; _lastSequence = 0;
            _stamp = _stamp with { Generation = checked(_stamp.Generation + 1) };
        } else if (committedTick < _stamp.Tick) throw new ArgumentException("UI committed tick cannot move backwards.");
        _stamp = _stamp with { PlaySession = playSession, World = world, Tick = committedTick };
    }
    public ReadOnlySpan<UiLayoutBox> Layout(UiViewport viewport)
    {
        _document.VerifyAccess(); viewport.Validate();
        if (_layoutRevision == _presentation && _viewport == viewport) return _boxes;
        _boxes = _layout.Layout(viewport).ToArray(); _viewport = viewport;
        var offsets = new Vector2[_boxes.Length];
        for (int i = 0; i < _boxes.Length; i++) {
            var box = _boxes[i]; var element = _elements[box.Id]; var offset = Vector2.Zero;
            UiRect clip = new(0, 0, viewport.Width / viewport.Scale, viewport.Height / viewport.Scale);
            if (box.ParentIndex >= 0) {
                var parent = _boxes[box.ParentIndex]; var pe = _elements[parent.Id]; offset = offsets[box.ParentIndex];
                if (pe.Kind == UiKind.ScrollView) offset += Vector2.TransformNormal(new(0, -_state[pe.Id].Scroll), parent.Transform);
                clip = parent.Clip;
                if (pe.Style.Clip || pe.Kind == UiKind.ScrollView) clip = UiRect.Intersect(clip, UiLayoutEngine.Bounds(parent.Rect.Width, parent.Rect.Height, parent.Transform));
            }
            var transform = box.Transform; transform.M31 += offset.X; transform.M32 += offset.Y;
            offsets[i] = offset; _boxes[i] = box with { Transform = transform, Clip = clip };
            if (element.Kind == UiKind.ScrollView && element.Layout.Rotation != 0) throw new ArgumentException("Rotated scroll clipping unsupported.");
        }
        _layoutRevision = _presentation; return _boxes;
    }
    public Guid HitTest(Vector2 framebufferPoint)
    {
        _document.VerifyAccess(); Vector2 point = framebufferPoint / _viewport.Scale;
        for (int i = _boxes.Length - 1; i >= 0; i--) {
            var box = _boxes[i]; var e = _elements[box.Id];
            if (!box.Enabled || box.Opacity <= 0 || !box.Clip.Contains(point) || !Interactive(e) || !Matrix3x2.Invert(box.Transform, out var inverse)) continue;
            var local = Vector2.Transform(point, inverse);
            if (!box.Rect.Contains(local)) continue;
            float radius = Math.Min(e.Style.CornerRadius, Math.Min(box.Rect.Width, box.Rect.Height) / 2);
            if (radius > 0) {
                float dx = Math.Max(Math.Abs(local.X - box.Rect.Width / 2) - (box.Rect.Width / 2 - radius), 0);
                float dy = Math.Max(Math.Abs(local.Y - box.Rect.Height / 2) - (box.Rect.Height / 2 - radius), 0);
                if (dx * dx + dy * dy > radius * radius) continue;
            }
            return box.Id;
        }
        return Guid.Empty;
    }
    private static bool Interactive(UiElement e) => e.Kind is UiKind.Button or UiKind.Slider or UiKind.TextInput or UiKind.ScrollView;
    public void Input(UiStamp expected, ReadOnlySpan<UiInput> input)
    {
        _document.VerifyAccess();
        if (expected != _stamp || _layoutRevision == ulong.MaxValue) throw new InvalidOperationException("Stale/unprepared UI input.");
        int potentialActions = 0;
        foreach (var e in input) if (e.Kind is UiInputKind.PointerDown or UiInputKind.PointerMove or UiInputKind.PointerUp or UiInputKind.Activate or UiInputKind.Text or UiInputKind.Backspace) potentialActions++;
        if (input.Length > MaxInputBatch || _actions.Count + potentialActions > MaxQueuedActions) throw new InvalidOperationException("UI input/action queue backpressure; no partial application.");
        ulong last = _lastSequence;
        foreach (var e in input) {
            if (!Enum.IsDefined(e.Kind) || e.Sequence <= last || !float.IsFinite(e.Position.X) || !float.IsFinite(e.Position.Y) || !float.IsFinite(e.Delta)) throw new ArgumentException("UI input identity/scalars.");
            UiCodec.Text(e.Text, 16384); last = e.Sequence;
        }
        foreach (var e in input) {
            Guid hit = HitTest(e.Position);
            Guid oldFocus = _focus, oldCapture = _capture; bool visual = false;
            switch (e.Kind) {
                case UiInputKind.FocusLost:
                    _capture = _focus = Guid.Empty; foreach (var id in _state.Keys.ToArray()) { visual |= _state[id].Composition.Length != 0; _state[id] = _state[id] with { Composition = "" }; } break;
                case UiInputKind.PointerDown:
                    _capture = hit; _focus = hit; if (hit != Guid.Empty && _elements[hit].Kind == UiKind.Slider) visual = Slide(hit, e); break;
                case UiInputKind.PointerMove:
                    if (_capture != Guid.Empty && _elements[_capture].Kind == UiKind.Slider) visual = Slide(_capture, e); break;
                case UiInputKind.PointerUp:
                    if (_capture != Guid.Empty && _capture == hit && _elements[hit].Kind == UiKind.Button) Emit(hit, e);
                    _capture = Guid.Empty; break;
                case UiInputKind.Tab:
                    var ids = _boxes.Where(b => b.Enabled && b.Opacity > 0 && _elements[b.Id].Kind is UiKind.Button or UiKind.Slider or UiKind.TextInput).Select(b => b.Id).ToArray();
                    if (ids.Length != 0) { int index = Array.IndexOf(ids, _focus); _focus = ids[index < 0 ? (e.Reverse ? ids.Length - 1 : 0) : (index + (e.Reverse ? ids.Length - 1 : 1)) % ids.Length]; } _capture = Guid.Empty; break;
                case UiInputKind.Activate:
                    if (_focus != Guid.Empty && _elements[_focus].Kind == UiKind.Button) Emit(_focus, e); break;
                case UiInputKind.Scroll:
                    if (hit != Guid.Empty) {
                        var element = _elements[hit]; while (element.Kind != UiKind.ScrollView && element.Parent != Guid.Empty) element = _elements[element.Parent];
                        if (element.Kind == UiKind.ScrollView) { var state = _state[element.Id]; float scroll = Math.Clamp(state.Scroll + e.Delta, 0, 65536); visual = scroll != state.Scroll; _state[element.Id] = state with { Scroll = scroll }; }
                    } break;
                case UiInputKind.Text: case UiInputKind.Backspace: case UiInputKind.Left: case UiInputKind.Right:
                case UiInputKind.Composition: case UiInputKind.CompositionCancel:
                    if (_focus != Guid.Empty && _elements[_focus].Kind == UiKind.TextInput) { var before = _state[_focus]; EditText(_focus, e); visual = before != _state[_focus]; } break;
            }
            _lastSequence = e.Sequence;
            if (visual || oldFocus != _focus || oldCapture != _capture) _presentation = checked(_presentation + 1);
        }
    }
    private bool Slide(Guid id, UiInput input)
    {
        var box = _boxes.First(b => b.Id == id); if (!Matrix3x2.Invert(box.Transform, out var inverse)) return false;
        float x = Vector2.Transform(input.Position / _viewport.Scale, inverse).X;
        float value = box.Rect.Width <= 0 ? 0 : Math.Clamp(x / box.Rect.Width, 0, 1); bool changed = value != _state[id].Value;
        _state[id] = _state[id] with { Value = value }; if (changed) Emit(id, input, value); return changed;
    }
    private void EditText(Guid id, UiInput input)
    {
        var state = _state[id]; int[] starts = StringInfo.ParseCombiningCharacters(state.Text);
        int previous = starts.LastOrDefault(i => i < state.Caret), next = starts.FirstOrDefault(i => i > state.Caret, state.Text.Length);
        switch (input.Kind) {
            case UiInputKind.Text:
                if (state.Text.Length + input.Text.Length > 16384) return;
                state = state with { Text = state.Text.Insert(state.Caret, input.Text), Caret = state.Caret + input.Text.Length, Composition = "" }; break;
            case UiInputKind.Backspace:
                if (state.Caret == 0) return; state = state with { Text = state.Text.Remove(previous, state.Caret - previous), Caret = previous, Composition = "" }; break;
            case UiInputKind.Left: state = state with { Caret = previous }; break;
            case UiInputKind.Right: state = state with { Caret = next }; break;
            case UiInputKind.Composition: state = state with { Composition = input.Text }; break;
            case UiInputKind.CompositionCancel: state = state with { Composition = "" }; break;
        }
        _state[id] = state;
        if (input.Kind is UiInputKind.Text or UiInputKind.Backspace) Emit(id, input, text: state.Text);
    }
    private void Emit(Guid id, UiInput input, float value = 0, string text = "")
    {
        var element = _elements[id]; if (element.Action.Length != 0) _actions.Enqueue(new(_stamp, input.Sequence, id, element.Action, input.Kind, value, text));
    }
    // Host must use its CURRENT identity/tick. Old queued actions are dropped at a changed
    // generation/session/world/tick rather than applied to a newly loaded game or stale observation.
    public int Drain(UiStamp expected, Span<UiAction> output)
    {
        _document.VerifyAccess(); if (expected != _stamp) throw new InvalidOperationException("UI drain identity mismatch.");
        if (output.Length < _actions.Count) throw new ArgumentException("Complete bounded UI drain required.");
        int count = 0; while (_actions.TryDequeue(out var action)) if (action.Stamp == _stamp) output[count++] = action;
        return count;
    }
}
