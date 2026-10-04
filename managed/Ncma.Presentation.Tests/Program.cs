using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Ncma.Gui;
using Ncma.Interop;
using Ncma.Platform;
static void Check(bool value) { if (!value) throw new Exception("Assertion failed."); }
static void Reject(Action action)
{
    try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or AggregateException) { return; }
    throw new Exception("Expected rejection.");
}
string root = Path.GetFullPath(args[0]);
static PluginSpecification[] Specs() => [
    new("platform", ModuleKind.Platform, "NcmaPlatform.dll", "NcmaPlatform.dll", 1, 0, []),
    new("renderer", ModuleKind.Renderer, "NcmaRenderer.dll", "NcmaRenderer.dll", 1, 0, ["platform"]),
    new("gui", ModuleKind.Gui, "NcmaGui.dll", "NcmaGui.dll", 1, 0, ["platform", "renderer"]) ];
static WindowState Focused(ulong sequence) => new() { StructSize = 136, Focused = 1, Sequence = sequence };
var cases = new (string, Action)[]
{
    ("Native/CLR presentation layouts", () => {
        Check(Marshal.SizeOf<WindowState>() == 136 && Marshal.SizeOf<InputEvent>() == 40 && Marshal.SizeOf<WindowDescription>() == 32);
        Check(Marshal.SizeOf<GuiItem>() == 96 && Marshal.SizeOf<GuiFrame>() == 48 && Marshal.SizeOf<GuiEvent>() == 72 &&
            Marshal.SizeOf<GuiDescription>() == 56 && Marshal.SizeOf<GuiCapture>() == 16 && Marshal.SizeOf<GuiStats>() == 32);
    }),
    ("Press release in one poll retains both edges", () => {
        var adapter = new WindowInputAccumulator();
        adapter.Update(Focused(2), [new() { Kind = 1, Key = 65, Action = 1, Sequence = 1 }, new() { Kind = 1, Key = 65, Action = 0, Sequence = 2 }]);
        ulong[] held = new ulong[8], pressed = new ulong[8], released = new ulong[8];
        adapter.CopyGameplayBits(false, false, held, pressed, released);
        Check(held[1] == 0 && pressed[1] == 2 && released[1] == 2);
        adapter.Update(Focused(2), []); adapter.CopyGameplayBits(false, false, held, pressed, released);
        Check(pressed.All(v => v == 0) && released.All(v => v == 0) && adapter.Sequence == 2);
    }),
    ("Overflow refocus and GUI capture do not leak transient input", () => {
        var adapter = new WindowInputAccumulator(); var state = Focused(3); state.Overflow = 1;
        adapter.Update(state, []); Check(adapter.CancelInteraction);
        state = Focused(4); state.InputReset = 1; adapter.Update(state, []); Check(adapter.CancelInteraction);
        state = Focused(5);
        adapter.Update(state, [new() { Kind = 1, Key = 65, Action = 1, Sequence = 5 }]);
        ulong[] held = new ulong[8], pressed = new ulong[8], released = new ulong[8];
        adapter.CopyGameplayBits(true, false, held, pressed, released); Check(pressed.All(v => v == 0));
        adapter.Update(new() { StructSize = 136, Sequence = 5 }, []); Check(!adapter.Focused && adapter.CancelInteraction);
    }),
    ("Invalid input validation is atomic and text is not keys", () => {
        var adapter = new WindowInputAccumulator();
        Reject(() => adapter.Update(Focused(2), [new() { Kind = 1, Sequence = 2 }, new() { Kind = 1, Sequence = 1 }]));
        Check(adapter.Sequence == 0);
        Reject(() => adapter.Update(Focused(1), [new() { Kind = 5, Codepoint = 0xD800, Sequence = 1 }]));
        adapter.Update(Focused(1), [new() { Kind = 5, Codepoint = 0x4E2D, Sequence = 1 }]);
        ulong[] held = new ulong[8], pressed = new ulong[8], released = new ulong[8];
        adapter.CopyGameplayBits(false, false, held, pressed, released); Check(pressed.All(v => v == 0));
        Check(Task.Run(() => { try { adapter.Update(Focused(2), []); return false; } catch (InvalidOperationException) { return true; } }).Result);
    }),
    ("Hidden GLFW window DPI state title close and lease safety", () => {
        using var loader = new PluginLoader(); loader.Load(root, Specs());
        var module = loader.Modules.Single(m => m.Kind == ModuleKind.Platform);
        using var window = new PlatformWindow(module, "中文 title", 320, 200, false);
        window.SetTitle("New title"); var state = window.Poll();
        Check(state.Width > 0 && state.FramebufferWidth > 0 && state.ScaleX > 0 && state.ScaleY > 0);
        Reject(module.Dispose); Reject(() => new PlatformWindow(module, "second", 100, 100, false));
        Reject(() => window.Wait(double.NaN)); Reject(() => window.SetTitle("embedded\0nul"));
        Check(Task.Run(() => { try { window.Poll(); return false; } catch (PluginException e) { return e.Result == PluginResult.WrongThread; } }).Result);
        window.RequestClose(); Check(window.State.CloseRequested == 1);
        window.Dispose(); window.Dispose(); Check(module.Status.LiveResources == 0);
    }),
    ("Creation failures do not leak window or GUI leases", () => {
        using var loader = new PluginLoader(); loader.Load(root, Specs());
        var platform = loader.Modules.Single(m => m.Kind == ModuleKind.Platform);
        var gui = loader.Modules.Single(m => m.Kind == ModuleKind.Gui);
        int leases = platform.OutstandingLeases;
        Reject(() => new PlatformWindow(platform, "bad", 0)); Check(platform.OutstandingLeases == leases);
        using var window = new PlatformWindow(platform, "fixture", 320, 200, false);
        int guiLeases = gui.OutstandingLeases, windowLeases = platform.OutstandingLeases;
        Reject(() => new GuiSession(gui, window, "missing-font.ttf"));
        Check(gui.Status.LiveResources == 0 && gui.OutstandingLeases == guiLeases && platform.OutstandingLeases == windowLeases);
        window.Dispose(); Reject(() => new GuiSession(gui, window)); Check(gui.OutstandingLeases == guiLeases);
    }),
    ("CPU Dear ImGui copy view generation budgets and normal cleanup", () => {
        using var loader = new PluginLoader(); loader.Load(root, Specs());
        using var window = new PlatformWindow(loader.Modules.Single(m => m.Kind == ModuleKind.Platform), "fixture", 640, 480, false);
        using var gui = new GuiSession(loader.Modules.Single(m => m.Kind == ModuleKind.Gui), window);
        var view = View.Create(); var state = window.Poll();
        var capture = gui.Begin(state, 0); Check(capture.Keyboard == 1 && capture.Mouse == 1);
        Reject(window.Dispose); Reject(() => gui.Begin(state, 0));
        var bad = view.Frame(1); bad.ItemCount++; Reject(() => gui.Draw(bad, view.Items, view.Text));
        var invalid = (GuiItem[])view.Items.Clone(); invalid[1].WidgetLow = invalid[0].WidgetLow;
        Reject(() => gui.Draw(view.Frame(1), invalid, view.Text));
        invalid = (GuiItem[])view.Items.Clone(); invalid[1].LabelOffset = uint.MaxValue;
        Reject(() => gui.Draw(view.Frame(1), invalid, view.Text));
        var stats = gui.Draw(view.Frame(1), view.Items, view.Text);
        Check(stats.Vertices > 0 && stats.Indices > 0 && stats.DrawLists == 1 && stats.EventOverflow == 0);
        gui.Begin(state, 1.0 / 60); Reject(() => gui.Draw(view.Frame(1), view.Items, view.Text));
        bad = view.Frame(2); bad.DocumentGeneration = 2; gui.Draw(bad, view.Items, view.Text);
        gui.Begin(state, 1.0 / 60); Reject(() => gui.Draw(view.Frame(3), view.Items, view.Text)); // Late document generation.
        bad.Frame = 3; gui.Draw(bad, view.Items, view.Text);
        gui.Dispose(); Check(loader.Modules.Single(m => m.Kind == ModuleKind.Gui).Status.LiveResources == 0);
        window.Dispose();
    }),
    ("32 window and GUI resource cycles return to baseline", () => {
        using var loader = new PluginLoader(); loader.Load(root, Specs()); var view = View.Create();
        for (int i = 0; i < 32; i++) {
            using var window = new PlatformWindow(loader.Modules.Single(m => m.Kind == ModuleKind.Platform), "cycle", 320, 200, false);
            using var gui = new GuiSession(loader.Modules.Single(m => m.Kind == ModuleKind.Gui), window);
            var state = window.Poll(); gui.Begin(state, 1.0 / 60); gui.Draw(view.Frame(1), view.Items, view.Text);
            gui.Dispose(); window.Dispose();
            Check(loader.Modules.All(m => m.Status.LiveResources == 0));
        }
    }),
    ("Presentation wrappers load no Host CLR bridge or World", () => {
        Check(!typeof(PlatformWindow).Assembly.GetReferencedAssemblies().Any(a => a.Name!.Contains("Scene") || a.Name.Contains("Gameplay") || a.Name.Contains("Host")));
        Check(!typeof(GuiSession).Assembly.GetReferencedAssemblies().Any(a => a.Name!.Contains("Editor") || a.Name.Contains("Scene")));
    }),
};
foreach (var (name, run) in cases) {
    try { run(); Console.WriteLine("PASS " + name); }
    catch (Exception e) { Console.Error.WriteLine("FAIL " + name + ": " + e); return 1; }
}
Console.WriteLine($"M2.3 presentation: {cases.Length} cases passed; synthetic/hidden CPU tests, manual UI and GPU acceptance pending.");
return 0;

sealed unsafe class View
{
    public GuiItem[] Items = new GuiItem[3];
    public byte[] Text = Encoding.UTF8.GetBytes("FixtureCPU draw data");
    public static View Create()
    {
        var view = new View();
        view.Items[0] = new() { Kind = (uint)GuiItemKind.PanelBegin, Enabled = 1, WidgetHigh = 1, WidgetLow = 1, LabelLength = 7 };
        view.Items[0].Rect[2] = 300; view.Items[0].Rect[3] = 160;
        view.Items[1] = new() { Kind = (uint)GuiItemKind.Label, Enabled = 1, WidgetHigh = 1, WidgetLow = 2, LabelOffset = 7, LabelLength = 13 };
        view.Items[2].Kind = (uint)GuiItemKind.PanelEnd; return view;
    }
    public GuiFrame Frame(ulong frame) => new() { StructSize = (uint)Marshal.SizeOf<GuiFrame>(), Frame = frame,
        ViewGeneration = 1, DocumentGeneration = 1, ItemCount = (uint)Items.Length, TextBytes = (uint)Text.Length };
}
