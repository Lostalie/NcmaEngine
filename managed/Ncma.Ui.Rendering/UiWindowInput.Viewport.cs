using System.Numerics;
using System.Text;
using Ncma.Platform;

namespace Ncma.Ui.Rendering;

public readonly record struct UiInputDisposition(bool Accepted, bool CaptureKeyboard, bool CapturePointer, bool Cancelled);

public sealed partial class UiWindowInput
{
    private ulong _platformSequence;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private void VerifyOwner()
    { if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("UI input adapter requires its owner thread."); }

    // Origin is in framebuffer pixels. Hosts apply capture to camera/picking AND gameplay before
    // advancing. This overload is CPU-only; it neither prepares GPU resources nor invokes scripts.
    public UiInputDisposition Pump(UiRuntime runtime, WindowState state, ReadOnlySpan<InputEvent> events,
        Vector2 framebufferOrigin, bool otherUiCaptured = false)
    {
        VerifyOwner(); ArgumentNullException.ThrowIfNull(runtime); _ = runtime.Stamp;
        Validate(state, events, framebufferOrigin);
        bool lostFocus = false;
        foreach (var e in events) lostFocus |= e.Kind == 6 && e.Action == 0;
        if (lostFocus || state.InputReset != 0 || state.Overflow != 0 || state.Focused == 0 || state.Minimized != 0 || otherUiCaptured) {
            _platformSequence = state.Sequence; Cancel(runtime); return new(true, true, true, true);
        }
        var viewport = runtime.PreparedViewport;
        var bounds = new UiRect(0, 0, viewport.Width, viewport.Height);
        Vector2 Point(double x, double y) => new((float)(x * state.ScaleX - framebufferOrigin.X), (float)(y * state.ScaleY - framebufferOrigin.Y));
        var pointer = Point(state.PointerX, state.PointerY);
        Span<ulong> held = stackalloc ulong[8]; state.CopyHeld(held);
        bool reverse = (held[340 / 64] & (1ul << (340 % 64))) != 0 || (held[344 / 64] & (1ul << (344 % 64))) != 0;
        bool capturePointer = runtime.Capture != Guid.Empty, captureKeyboard = runtime.Focus != Guid.Empty;
        bool dragging = capturePointer, keyboardEligible = captureKeyboard;
        ulong sequence = _sequence;
        int count = 0;
        try {
            foreach (var e in events) {
                Vector2 point = e.Kind is 2 or 3 ? Point(e.X, e.Y) : pointer;
                if (e.Kind is 2 or 3) pointer = point;
                bool hit = bounds.Contains(point) && runtime.HitTest(point) != Guid.Empty;
                UiInput? input = null;
                switch (e.Kind) {
                    case 2 when e.Key == 0 && e.Action is 0 or 1:
                        capturePointer |= dragging || hit;
                        if (e.Action == 1) { dragging = hit; keyboardEligible = hit; }
                        else dragging = false;
                        input = new(e.Action == 1 ? UiInputKind.PointerDown : UiInputKind.PointerUp, checked(++sequence), point);
                        break;
                    case 2:
                        capturePointer |= dragging || hit;
                        break;
                    case 3:
                        capturePointer |= dragging || hit;
                        input = new(UiInputKind.PointerMove, checked(++sequence), point);
                        break;
                    case 4:
                        capturePointer |= hit;
                        input = new(UiInputKind.Scroll, checked(++sequence), pointer, Delta: (float)(-e.Y * 32));
                        break;
                    case 1 when e.Action is 1 or 2 && e.Key == 258 && (keyboardEligible || bounds.Contains(pointer)):
                        keyboardEligible = true; captureKeyboard = true;
                        input = new(UiInputKind.Tab, checked(++sequence), default, Reverse: reverse);
                        break;
                    case 1 when keyboardEligible:
                        input = e switch {
                            { Action: 1, Key: 257 or 32 } => new(UiInputKind.Activate, checked(++sequence), default),
                            { Action: 1 or 2, Key: 259 } => new(UiInputKind.Backspace, checked(++sequence), default),
                            { Action: 1 or 2, Key: 263 } => new(UiInputKind.Left, checked(++sequence), default),
                            { Action: 1 or 2, Key: 262 } => new(UiInputKind.Right, checked(++sequence), default),
                            _ => null
                        };
                        captureKeyboard = true;
                        break;
                    case 5 when keyboardEligible:
                        captureKeyboard = true;
                        input = new(UiInputKind.Text, checked(++sequence), default, new Rune((int)e.Codepoint).ToString());
                        break;
                }
                if (input is { } value) {
                    if (count == _inputs.Length) return Drop(runtime, state.Sequence, sequence);
                    _inputs[count++] = value;
                }
            }
            // Conservative reservation; overflow cancels, never applies a partial pointer/key batch.
            if (runtime.QueuedActions + count > UiRuntime.MaxQueuedActions) return Drop(runtime, state.Sequence, sequence);
            runtime.Input(runtime.Stamp, _inputs.AsSpan(0, count));
            _sequence = sequence; _platformSequence = state.Sequence;
            capturePointer |= runtime.Capture != Guid.Empty || (bounds.Contains(pointer) && runtime.HitTest(pointer) != Guid.Empty);
            captureKeyboard |= runtime.Focus != Guid.Empty;
            return new(true, captureKeyboard, capturePointer, false);
        }
        finally { Array.Clear(_inputs); }
    }

    public void Cancel(UiRuntime runtime)
    { VerifyOwner(); ArgumentNullException.ThrowIfNull(runtime); runtime.CancelInput(); Array.Clear(_actions); }

    private UiInputDisposition Drop(UiRuntime runtime, ulong platformSequence, ulong sequence)
    {
        Cancel(runtime); DroppedBatches++; _platformSequence = platformSequence; _sequence = sequence;
        return new(false, true, true, true);
    }

    private void Validate(WindowState state, ReadOnlySpan<InputEvent> events, Vector2 origin)
    {
        if (state.StructSize != 136 || state.Focused > 1 || state.Minimized > 1 || state.InputReset > 1 || state.Overflow > 1 ||
            !float.IsFinite(origin.X) || !float.IsFinite(origin.Y) || Math.Abs(origin.X) > 1_000_000 || Math.Abs(origin.Y) > 1_000_000 ||
            !float.IsFinite(state.ScaleX) || !float.IsFinite(state.ScaleY) || state.ScaleX <= 0 || state.ScaleY <= 0 || state.ScaleX > 16 || state.ScaleY > 16 ||
            !double.IsFinite(state.PointerX) || !double.IsFinite(state.PointerY) || Math.Abs(state.PointerX) > 1_000_000 || Math.Abs(state.PointerY) > 1_000_000 ||
            events.Length > 4096 || state.Sequence < _platformSequence)
            throw new ArgumentException("Invalid UI window input snapshot/viewport origin.");
        ulong last = _platformSequence;
        foreach (var e in events) {
            if (e.Sequence <= last || e.Sequence > state.Sequence || e.Kind is < 1 or > 7 ||
                !double.IsFinite(e.X) || !double.IsFinite(e.Y) || Math.Abs(e.X) > 1_000_000 || Math.Abs(e.Y) > 1_000_000 ||
                (e.Kind == 1 && ((e.Key >= 384 && e.Key != uint.MaxValue) || e.Action > 2)) || (e.Kind == 2 && (e.Key >= 8 || e.Action > 1)) ||
                (e.Kind == 6 && e.Action > 1) ||
                (e.Kind == 5 && (e.Codepoint > 0x10FFFF || !Rune.IsValid((int)e.Codepoint))))
                throw new ArgumentException("Invalid or replayed UI platform event.");
            last = e.Sequence;
        }
    }

    public ReadOnlySpan<UiAction> Drain(UiRuntime runtime)
    { VerifyOwner(); ArgumentNullException.ThrowIfNull(runtime); var stamp = runtime.Stamp; Array.Clear(_actions); int count = runtime.Drain(stamp, _actions); return _actions.AsSpan(0, count); }
}
