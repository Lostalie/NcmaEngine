using System.Numerics;
using Ncma.Platform;

namespace Ncma.Ui.Rendering;

// Trusted platform adapter. Capture supplied by the host, not by request JSON. No gameplay key
// forwarding when a widget is focused. IME pre-edit composition needs an explicit platform bridge;
// current GLFW character events are committed Unicode scalars only, not a full IME implementation.
public sealed partial class UiWindowInput
{
    private ulong _sequence;
    private readonly UiAction[] _actions = new UiAction[UiRuntime.MaxQueuedActions];
    private readonly UiInput[] _inputs = new UiInput[UiRuntime.MaxInputBatch];
    public ulong DroppedBatches { get; private set; }
    public bool Pump(UiCanvas canvas, WindowState state, ReadOnlySpan<InputEvent> events, bool otherUiCaptured = false)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        return Pump(canvas.Runtime, state, events, Vector2.Zero, otherUiCaptured).Accepted;
    }
    public ReadOnlySpan<UiAction> Drain(UiCanvas canvas)
    { ArgumentNullException.ThrowIfNull(canvas); return Drain(canvas.Runtime); }
}
