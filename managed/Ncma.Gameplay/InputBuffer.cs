namespace Ncma.Gameplay;

public sealed record InputFrame(Guid SessionId, ulong Sequence, bool Focused,
    ulong[] Held, ulong[] Pressed, ulong[] Released, double PointerX = 0, double PointerY = 0);

// Ingest belongs to the owner thread; transient consumption is installed with a successful fixed step.
internal sealed class InputBuffer
{
    private ulong[] _held = new ulong[InputState.WordCount], _pressed = new ulong[InputState.WordCount], _released = new ulong[InputState.WordCount];
    private ulong _sequence;
    private bool _focused;
    private double _x, _y;
    private const double PointerLimit = 1_000_000;
    internal InputState Read() => new(_held, _pressed, _released, _x, _y, _sequence, _focused);
    internal void Submit(InputFrame frame, Guid session, bool acceptTransient)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.SessionId != session || frame.Sequence == 0 || frame.Sequence <= _sequence ||
            frame.Held is null || frame.Pressed is null || frame.Released is null ||
            frame.Held.Length != InputState.WordCount || frame.Pressed.Length != InputState.WordCount || frame.Released.Length != InputState.WordCount ||
            !double.IsFinite(frame.PointerX) || !double.IsFinite(frame.PointerY) ||
            Math.Abs(frame.PointerX) > PointerLimit || Math.Abs(frame.PointerY) > PointerLimit)
            throw new ArgumentException("Invalid or stale input snapshot.");
        bool edges = acceptTransient && frame.Focused && _focused;
        double x = edges ? _x + frame.PointerX : 0, y = edges ? _y + frame.PointerY : 0;
        if (Math.Abs(x) > PointerLimit || Math.Abs(y) > PointerLimit) throw new ArgumentException("Pointer accumulation exceeds capacity.");
        var held = frame.Focused ? (ulong[])frame.Held.Clone() : new ulong[InputState.WordCount];
        var pressed = new ulong[InputState.WordCount]; var released = new ulong[InputState.WordCount];
        if (edges) for (int i = 0; i < held.Length; i++) { pressed[i] = _pressed[i] | frame.Pressed[i]; released[i] = _released[i] | frame.Released[i]; }
        _held = held; _pressed = pressed; _released = released; _sequence = frame.Sequence; _focused = frame.Focused; _x = x; _y = y;
    }
    internal Action PrepareConsume()
    {
        var pressed = new ulong[InputState.WordCount]; var released = new ulong[InputState.WordCount];
        return () => { _pressed = pressed; _released = released; _x = _y = 0; };
    }
    internal void Clear(bool resetSequence = false)
    {
        _held = new ulong[InputState.WordCount]; _pressed = new ulong[InputState.WordCount]; _released = new ulong[InputState.WordCount];
        _x = _y = 0; _focused = false; if (resetSequence) _sequence = 0;
    }
}
