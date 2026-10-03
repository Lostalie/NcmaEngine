namespace Ncma;

/// <summary>Immutable committed input observation; key codes 0..383, mouse buttons 384..391.</summary>
public sealed class InputState
{
    public const int WordCount = 8;
    private readonly ulong[] _held, _pressed, _released;
    internal InputState(ulong[] held, ulong[] pressed, ulong[] released, double x, double y, ulong sequence, bool focused)
    { _held = (ulong[])held.Clone(); _pressed = (ulong[])pressed.Clone(); _released = (ulong[])released.Clone();
      PointerX = x; PointerY = y; FrameSequence = sequence; Focused = focused; }
    public double PointerX { get; }
    public double PointerY { get; }
    public ulong FrameSequence { get; }
    public bool Focused { get; }
    private static bool Test(ulong[] words, int key)
    { if ((uint)key >= WordCount * 64) throw new ArgumentOutOfRangeException(nameof(key)); return (words[key / 64] & (1UL << (key % 64))) != 0; }
    public bool Held(int key) => Test(_held, key);
    public bool Pressed(int key) => Test(_pressed, key);
    public bool Released(int key) => Test(_released, key);
    internal static InputState Empty => new(new ulong[WordCount], new ulong[WordCount], new ulong[WordCount], 0, 0, 0, false);
}

public interface IGameplayContext
{
    Guid SessionId { get; }
    ulong Tick { get; }
    InputState Input { get; }
    IRuntimeCommands Commands { get; }
}

public sealed record CleanupContext(Guid ObjectId, Guid BindingId, string TypeName);

public sealed partial class SceneWorld
{
    private IGameplayContext? _context;
    public IGameplayContext Context { get { Verify(); return _context ?? throw new InvalidOperationException("No Play context."); } }
    internal void SetContext(IGameplayContext? context) => _context = context;
}
