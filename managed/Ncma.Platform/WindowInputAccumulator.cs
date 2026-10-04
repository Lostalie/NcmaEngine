namespace Ncma.Platform;
// Reusable owner-thread frame adapter. Text/scroll remain GUI/tool input, not gameplay key bits.
public sealed class WindowInputAccumulator
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly ulong[] _held = new ulong[8], _pressed = new ulong[8], _released = new ulong[8];
    private ulong _lastEvent, _sequence;
    public bool Focused { get; private set; }
    public bool CancelInteraction { get; private set; }
    public double PointerX { get; private set; }
    public double PointerY { get; private set; }
    public ulong Sequence => _sequence;
    private void Verify() { if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Input adapter requires owner thread."); }
    public void Update(WindowState state, ReadOnlySpan<InputEvent> events)
    {
        Verify();
        if (state.StructSize != 136 || state.Focused > 1 || state.Minimized > 1 || state.Overflow > 1 || state.InputReset > 1 || events.Length > 4096 || !double.IsFinite(state.PointerX) || !double.IsFinite(state.PointerY))
            throw new ArgumentException("Invalid input snapshot.");
        ulong last = _lastEvent;
        foreach (var item in events)
        {
            if (item.Kind is < 1 or > 7 || item.Sequence <= last || item.Sequence > state.Sequence ||
                !double.IsFinite(item.X) || !double.IsFinite(item.Y) ||
                (item.Kind == 5 && (item.Codepoint > 0x10FFFF || item.Codepoint is >= 0xD800 and <= 0xDFFF)))
                throw new ArgumentException("Invalid/late input event.");
            last = item.Sequence;
        }
        if (state.Sequence < _lastEvent) throw new ArgumentException("Stale input state.");
        ulong sequence = checked(_sequence + 1);
        Array.Clear(_pressed); Array.Clear(_released); state.CopyHeld(_held);
        Focused = state.Focused != 0 && state.Minimized == 0;
        CancelInteraction = !Focused || state.Overflow != 0 || state.InputReset != 0;
        if (CancelInteraction) {
            if (!Focused) Array.Clear(_held);
        } else {
            foreach (var item in events) {
                uint key = item.Kind == 1 ? item.Key : item.Kind == 2 && item.Key < 8 ? item.Key + 384 : 512;
                if (key >= 512) continue;
                if (item.Action == 1) _pressed[key / 64] |= 1UL << (int)(key % 64);
                if (item.Action == 0) _released[key / 64] |= 1UL << (int)(key % 64);
            }
        }
        _lastEvent = state.Sequence; _sequence = sequence; PointerX = state.PointerX; PointerY = state.PointerY;
    }
    public void CopyGameplayBits(bool captureKeyboard, bool captureMouse, Span<ulong> held, Span<ulong> pressed, Span<ulong> released)
    {
        Verify();
        if (held.Length != 8 || pressed.Length != 8 || released.Length != 8) throw new ArgumentException("Eight words required.");
        _held.CopyTo(held); _pressed.CopyTo(pressed); _released.CopyTo(released);
        if (captureKeyboard) for (int i = 0; i < 6; i++) { held[i] = 0; pressed[i] = 0; released[i] = 0; }
        if (captureMouse) { held[6] &= ~255UL; pressed[6] &= ~255UL; released[6] &= ~255UL; }
    }
}
