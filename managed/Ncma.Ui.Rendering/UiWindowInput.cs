using System.Numerics;
using System.Text;
using Ncma.Platform;

namespace Ncma.Ui.Rendering;

// Trusted platform adapter. Capture supplied by the host, not by request JSON. No gameplay key
// forwarding when a widget is focused. IME pre-edit composition needs an explicit platform bridge;
// current GLFW character events are committed Unicode scalars only, not a full IME implementation.
public sealed class UiWindowInput
{
    private ulong _sequence;
    private readonly UiAction[] _actions = new UiAction[UiRuntime.MaxQueuedActions];
    private readonly UiInput[] _inputs = new UiInput[UiRuntime.MaxInputBatch];
    public ulong DroppedBatches { get; private set; }
    public bool Pump(UiCanvas canvas, WindowState state, ReadOnlySpan<InputEvent> events, bool otherUiCaptured = false)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (state.InputReset != 0 || state.Overflow != 0 || state.Focused == 0 || otherUiCaptured) {
            canvas.Runtime.Input(canvas.Runtime.Stamp, [new(UiInputKind.FocusLost, ++_sequence, default)]); return true;
        }
        Vector2 Point(double x, double y) => new((float)(x * state.ScaleX), (float)(y * state.ScaleY));
        var pointer = Point(state.PointerX, state.PointerY);
        Span<ulong> held = stackalloc ulong[8]; state.CopyHeld(held);
        bool reverse = (held[340 / 64] & (1ul << (340 % 64))) != 0 || (held[344 / 64] & (1ul << (344 % 64))) != 0;
        int count = 0;
        foreach (var e in events) {
            UiInput? input = e.Kind switch {
                2 when e.Key == 0 && e.Action == 1 => new(UiInputKind.PointerDown, ++_sequence, Point(e.X, e.Y)),
                2 when e.Key == 0 && e.Action == 0 => new(UiInputKind.PointerUp, ++_sequence, Point(e.X, e.Y)),
                3 => new(UiInputKind.PointerMove, ++_sequence, Point(e.X, e.Y)),
                4 => new(UiInputKind.Scroll, ++_sequence, pointer, Delta: (float)(-e.Y * 32)),
                5 when Rune.IsValid((int)e.Codepoint) => new(UiInputKind.Text, ++_sequence, default, new Rune((int)e.Codepoint).ToString()),
                1 when e.Action is 1 or 2 && e.Key == 258 => new(UiInputKind.Tab, ++_sequence, default, Reverse: reverse),
                1 when e.Action == 1 && e.Key is 257 or 32 => new(UiInputKind.Activate, ++_sequence, default),
                1 when e.Action is 1 or 2 && e.Key == 259 => new(UiInputKind.Backspace, ++_sequence, default),
                1 when e.Action is 1 or 2 && e.Key == 263 => new(UiInputKind.Left, ++_sequence, default),
                1 when e.Action is 1 or 2 && e.Key == 262 => new(UiInputKind.Right, ++_sequence, default),
                _ => null
            };
            if (input is { } value) {
                if (count == _inputs.Length) { DroppedBatches++; canvas.Runtime.Input(canvas.Runtime.Stamp, [new(UiInputKind.FocusLost, ++_sequence, default)]); return false; }
                _inputs[count++] = value;
            }
        }
        if (canvas.Runtime.QueuedActions + count > UiRuntime.MaxQueuedActions) { DroppedBatches++; canvas.Runtime.Input(canvas.Runtime.Stamp, [new(UiInputKind.FocusLost, ++_sequence, default)]); return false; }
        canvas.Runtime.Input(canvas.Runtime.Stamp, _inputs.AsSpan(0, count)); return true;
    }
    public ReadOnlySpan<UiAction> Drain(UiCanvas canvas)
    { int count = canvas.Runtime.Drain(canvas.Runtime.Stamp, _actions); return _actions.AsSpan(0, count); }
}
