using System.Numerics;
using Ncma.Platform;
using Ncma.Ui;
using Ncma.Ui.Rendering;

internal static class WindowInputTests
{
    private static void Check(bool value) { if (!value) throw new Exception("UI window input assertion failed."); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; } throw new Exception("Invalid UI window input accepted."); }
    private static UiRuntime Runtime(float scale = 1)
    {
        var d = UiDefinition.Create(Guid.NewGuid(), "Input");
        var button = UiElement.Create(Guid.NewGuid(), "Button", UiKind.Button, d.Root) with { Action = "button", Layout = UiLayout.Fixed(10, 10, 80, 30) };
        var text = UiElement.Create(Guid.NewGuid(), "Text", UiKind.TextInput, d.Root) with { Order = 1, Action = "text", Layout = UiLayout.Fixed(10, 50, 80, 30) };
        var slider = UiElement.Create(Guid.NewGuid(), "Slider", UiKind.Slider, d.Root) with { Order = 2, Action = "slider", Layout = UiLayout.Fixed(10, 90, 100, 20) };
        var r = new UiRuntime(new(d with { Elements = [d.Elements[0], button, text, slider] }));
        r.Layout(new(200 * scale, 150 * scale, scale, default)); return r;
    }
    private static WindowState State(ulong sequence, double x = 20, double y = 20, float scale = 1) => new() {
        StructSize = 136, Focused = 1, Width = 800, Height = 600, FramebufferWidth = (uint)(800 * scale),
        FramebufferHeight = (uint)(600 * scale), ScaleX = scale, ScaleY = scale, Sequence = sequence, PointerX = x, PointerY = y
    };
    private static InputEvent Pointer(ulong seq, uint action, double x = 20, double y = 20) => new() { Kind = 2, Sequence = seq, Action = action, X = x, Y = y };
    public static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("UI viewport input maps framebuffer origin and DPI without clicking the scene", () => {
            var r = Runtime(2); var input = new UiWindowInput();
            var result = input.Pump(r, State(2, 120, 70, 2), [Pointer(1, 1, 120, 70), Pointer(2, 0, 120, 70)], new(200, 100));
            Check(result.Accepted && result.CapturePointer && result.CaptureKeyboard && !result.Cancelled);
            var actions = input.Drain(r); Check(actions.Length == 1 && actions[0].Action == "button");
            result = input.Pump(r, State(4, 20, 20, 2), [Pointer(3, 1), Pointer(4, 0)], new(200, 100));
            Check(result.Accepted && !result.CapturePointer && r.Focus == Guid.Empty && input.Drain(r).Length == 0);
        });
        yield return ("UI mouse capture survives outside drag and suppresses release in the same batch", () => {
            var r = Runtime(); var input = new UiWindowInput();
            input.Pump(r, State(1, 30, 100), [Pointer(1, 1, 30, 100)], default);
            Check(r.Capture != Guid.Empty); input.Drain(r);
            var result = input.Pump(r, State(3, 350, 100), [new() { Kind = 3, Sequence = 2, X = 350, Y = 100 }, Pointer(3, 0, 350, 100)], default);
            Check(result.CapturePointer && r.Capture == Guid.Empty && input.Drain(r).Length == 1);
            result = input.Pump(r, State(3, 350, 100), [], default); Check(!result.CapturePointer);
        });
        yield return ("UI text and Tab stay in the selected viewport; committed Unicode is copied", () => {
            var r = Runtime(); var input = new UiWindowInput();
            var result = input.Pump(r, State(2, 350, 300), [new() { Kind = 1, Key = 258, Action = 1, Sequence = 1 }, new() { Kind = 5, Codepoint = 65, Sequence = 2 }], default);
            Check(!result.CaptureKeyboard && r.Focus == Guid.Empty);
            result = input.Pump(r, State(5, 20, 60), [Pointer(3, 1, 20, 60), Pointer(4, 0, 20, 60), new() { Kind = 5, Codepoint = 0x1F600, Sequence = 5 }], default);
            Check(result.CaptureKeyboard && result.CapturePointer); var actions = input.Drain(r);
            Check(actions.Length == 1 && actions[0].Text == "😀");
        });
        yield return ("UI lifecycle cancel revokes pending actions, composition and stamps without losing values", () => {
            var r = Runtime(); var input = new UiWindowInput();
            input.Pump(r, State(3, 20, 60), [Pointer(1, 1, 20, 60), new() { Kind = 5, Codepoint = 65, Sequence = 2 }, Pointer(3, 0, 20, 60)], default);
            var text = r.Focus; r.Input(r.Stamp, [new(UiInputKind.Composition, 100, default, "中")]);
            var stamp = r.Stamp; input.Cancel(r);
            Check(r.QueuedActions == 0 && r.Focus == Guid.Empty && r.Capture == Guid.Empty && r.Stamp.Generation > stamp.Generation);
            Check(r.Widget(text).Text == "A" && r.Widget(text).Composition == "");
            Reject(() => r.Input(stamp, [])); Reject(() => r.Drain(stamp, new UiAction[128]));
            input.Pump(r, State(5), [Pointer(4, 1), Pointer(5, 0)], default); Check(input.Drain(r).Length == 1);
        });
        yield return ("UI focus loss, minimize, reset, platform overflow and other UI cancel queued clicks", () => {
            for (int reason = 0; reason < 5; reason++) {
                var r = Runtime(); var input = new UiWindowInput(); input.Pump(r, State(2), [Pointer(1, 1), Pointer(2, 0)], default);
                var stamp = r.Stamp; var state = State(2);
                switch (reason) { case 0: state.Focused = 0; break; case 1: state.Minimized = 1; break; case 2: state.InputReset = 1; break; case 3: state.Overflow = 1; break; }
                var result = input.Pump(r, state, [], default, otherUiCaptured: reason == 4);
                Check(result.Accepted && result.Cancelled && result.CaptureKeyboard && result.CapturePointer);
                Check(r.Stamp.Generation > stamp.Generation && r.QueuedActions == 0 && input.Drain(r).Length == 0);
            }
        });
        yield return ("UI oversized translated batch cancels instead of applying a click prefix", () => {
            var r = Runtime(); var input = new UiWindowInput();
            var events = new List<InputEvent> { Pointer(1, 1), Pointer(2, 0) };
            for (ulong i = 3; i <= 130; i++) events.Add(new() { Kind = 1, Key = 257, Action = 1, Sequence = i });
            var stamp = r.Stamp; var result = input.Pump(r, State(130), events.ToArray(), default);
            Check(!result.Accepted && result.Cancelled && result.CapturePointer && input.DroppedBatches == 1);
            Check(r.Stamp.Generation > stamp.Generation && r.Focus == Guid.Empty && r.QueuedActions == 0);
            input.Pump(r, State(132), [Pointer(131, 1), Pointer(132, 0)], default); Check(input.Drain(r).Length == 1);
        });
        yield return ("UI platform validation rejects full malformed batches and replay before mutation", () => {
            var r = Runtime(); var input = new UiWindowInput(); var stamp = r.Stamp;
            var invalid = new[] { new InputEvent { Kind = 5, Sequence = 2, Codepoint = 0xD800 }, Pointer(1, 0),
                Pointer(3, 0), new InputEvent { Kind = 1, Key = 999, Sequence = 2 }, Pointer(2, 99),
                new InputEvent { Kind = 3, Sequence = 2, X = double.NaN } };
            foreach (var bad in invalid) Reject(() => input.Pump(r, State(2), [Pointer(1, 1), bad], default));
            Check(r.Stamp == stamp && r.Focus == Guid.Empty && r.Capture == Guid.Empty);
            var state = State(2); state.ScaleX = 0; Reject(() => input.Pump(r, state, [], default));
            Reject(() => input.Pump(r, State(2), [], new(float.NaN, 0)));
            input.Pump(r, State(2), [Pointer(1, 1), Pointer(2, 0)], default);
            Reject(() => input.Pump(r, State(2), [Pointer(2, 0)], default)); Check(input.Drain(r).Length == 1);
            Task.Run(() => Reject(() => input.Cancel(r))).GetAwaiter().GetResult();
            Task.Run(() => {
                var workerRuntime = Runtime(); // The adapter has its own owner, not just a borrowed runtime's guard.
                Reject(() => input.Cancel(workerRuntime)); Reject(() => input.Drain(workerRuntime));
                Reject(() => input.Pump(workerRuntime, State(3), [], default));
            }).GetAwaiter().GetResult();
        });
        yield return ("UI rapid focus loss/regain cancels and unknown GLFW keys are ignored", () => {
            var r = Runtime(); var input = new UiWindowInput(); input.Pump(r, State(2), [Pointer(1, 1), Pointer(2, 0)], default);
            var stamp = r.Stamp;
            var result = input.Pump(r, State(4), [new() { Kind = 6, Action = 0, Sequence = 3 }, new() { Kind = 6, Action = 1, Sequence = 4 }], default);
            Check(result.Cancelled && r.Stamp.Generation > stamp.Generation && input.Drain(r).Length == 0);
            result = input.Pump(r, State(7), [new() { Kind = 1, Key = uint.MaxValue, Action = 1, Sequence = 5 }, Pointer(6, 1), Pointer(7, 0)], default);
            Check(result.Accepted && input.Drain(r).Length == 1);
        });
        yield return ("UI input capture masks both physical gameplay mouse and keyboard edges", () => {
            var r = Runtime(); var input = new UiWindowInput(); var physical = new WindowInputAccumulator();
            InputEvent[] events = [Pointer(1, 1), new() { Kind = 1, Key = 32, Action = 1, Sequence = 2 }, Pointer(3, 0)];
            var state = State(3); physical.Update(state, events); var result = input.Pump(r, state, events, default);
            Span<ulong> held = stackalloc ulong[8], pressed = stackalloc ulong[8], released = stackalloc ulong[8];
            physical.CopyGameplayBits(false, false, held, pressed, released); Check(pressed[0] != 0 && pressed[6] != 0 && released[6] != 0);
            physical.CopyGameplayBits(result.CaptureKeyboard, result.CapturePointer, held, pressed, released);
            Check(pressed[0] == 0 && pressed[6] == 0 && released[6] == 0);
        });
    }
}
