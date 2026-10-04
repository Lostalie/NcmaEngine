using System.Runtime.InteropServices;
using System.Text;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
namespace Ncma.Gui;
public enum GuiItemKind : uint { PanelBegin = 1, PanelEnd, Label, Button, Number, Checkbox, Text, SameLine, CanvasBegin, CanvasLines, CanvasEnd, Theme }
[StructLayout(LayoutKind.Sequential)]
public struct GuiFrame { public uint StructSize, ItemCount, TextBytes, Reserved; public ulong Frame, ViewGeneration, DocumentGeneration, Revision; }
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GuiItem
{
    public uint Kind, Enabled; public ulong WidgetHigh, WidgetLow;
    public uint LabelOffset, LabelLength, TextOffset, TextLength;
    public double Value, Minimum, Maximum;
    public fixed float Rect[4];
    public fixed uint Reserved[4];
}
[StructLayout(LayoutKind.Sequential)]
public struct GuiEvent
{
    public uint Kind, Phase; public ulong WidgetHigh, WidgetLow; public double Value;
    public ulong Frame, ViewGeneration, DocumentGeneration, Revision;
    public uint TextOffset, TextLength;
}
[StructLayout(LayoutKind.Sequential)]
public struct GuiCapture { public uint StructSize, Keyboard, Mouse, CancelInteraction; }
[StructLayout(LayoutKind.Sequential)]
public struct GuiStats { public uint StructSize, Vertices, Indices, DrawLists, EventOverflow, TextBytes; public ulong Frame; }
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GuiDescription
{
    public uint StructSize, Reserved; public ulong Platform, Window;
    public byte* FontPath; public uint FontPathLength; public float FontSize;
    public byte* IniPath; public uint IniPathLength, Reserved2;
}
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate uint CreateGui(ulong context, GuiDescription* description, ulong* handle, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate uint BeginGui(ulong context, ulong handle, WindowState* state, double delta, GuiCapture* capture, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate uint DrawGui(ulong context, ulong handle, GuiFrame* frame, GuiItem* items, byte* text,
    GuiEvent* events, uint capacity, uint* count, byte* outputText, uint textCapacity, GuiStats* stats, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate uint DestroyGui(ulong context, ulong handle, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate uint AttachRenderer(ulong context, ulong gui, ulong rendererModule, ulong renderer, PluginError* error);
public sealed unsafe class GuiSession : IDisposable
{
    private readonly PluginLease _lease;
    private readonly PluginLease? _platformLease;
    private readonly CreateGui _create; private readonly BeginGui _begin; private readonly DrawGui _draw; private readonly DestroyGui _destroy;
    private readonly GuiEvent[] _events = new GuiEvent[256];
    private readonly byte[] _text = new byte[65536];
    private int _count, _textCount;
    private ulong _handle;
    private PluginLease? _rendererLease;
    private RendererSession? _rendererOwner;
    private readonly AttachRenderer _attach;
    private readonly DestroyGui _renderGpu;
    private bool _disposed;
    public ulong BeginCalls { get; private set; }
    public ulong DrawCalls { get; private set; }
    public ulong CopiedBytes { get; private set; }
    public double LastDrawMilliseconds { get; private set; }
    private PluginModule Module => _lease.Module;
    public GuiSession(PluginModule module, PlatformWindow window, string fontPath = "", string iniPath = "", float fontSize = 18)
    {
        if (module.Kind != ModuleKind.Gui) throw new ArgumentException("GUI module required.");
        _lease = module.AcquireLease();
        try {
            _platformLease = window.Module.AcquireLease();
            _create = module.ReadFunction<CreateGui>(56); _begin = module.ReadFunction<BeginGui>(64);
            _attach = module.ReadFunction<AttachRenderer>(88); _renderGpu = module.ReadFunction<DestroyGui>(96);
            _draw = module.ReadFunction<DrawGui>(72); _destroy = module.ReadFunction<DestroyGui>(80);
            var utf8 = new UTF8Encoding(false, true); byte[] font = utf8.GetBytes(fontPath), ini = utf8.GetBytes(iniPath);
            fixed (byte* fontText = font) fixed (byte* iniText = ini) {
                GuiDescription desc = new() { StructSize = (uint)sizeof(GuiDescription), Platform = window.Module.Context,
                    Window = window.Handle, FontPath = fontText, FontPathLength = (uint)font.Length, FontSize = fontSize,
                    IniPath = iniText, IniPathLength = (uint)ini.Length };
                PluginError error = default; ulong handle = 0;
                PluginModule.Check(module.Id, "create_gui", _create(module.Context, &desc, &handle, &error), error);
                _handle = handle;
            }
        } catch { _platformLease?.Dispose(); _lease.Dispose(); throw; }
    }
    private void Verify() { _ = Module; ObjectDisposedException.ThrowIf(_disposed, this); }
    public void AttachRenderer(RendererSession renderer)
    {
        Verify(); if (_rendererLease is not null) throw new InvalidOperationException("Renderer already attached.");
        var lease = renderer.Module.AcquireLease();
        try {
            PluginError error = default;
            PluginModule.Check(Module.Id,"attach_gui_renderer",_attach(Module.Context,_handle,renderer.Module.Context,renderer.Handle,&error),error);
            _rendererLease = lease; _rendererOwner = renderer;
        } catch { lease.Dispose(); throw; }
    }
    public void RenderGpu()
    {
        Verify(); PluginError error = default;
        PluginModule.Check(Module.Id,"render_gui_gpu",_renderGpu(Module.Context,_handle,&error),error);
    }
    public GuiCapture Begin(WindowState state, double delta)
    {
        Verify(); PluginError error = default; GuiCapture capture = default;
        PluginModule.Check(Module.Id, "begin_gui", _begin(Module.Context, _handle, &state, delta, &capture, &error), error);
        if (capture.StructSize != sizeof(GuiCapture) || capture.Keyboard > 1 || capture.Mouse > 1 || capture.CancelInteraction > 1)
            throw new InvalidOperationException("Invalid GUI capture.");
        BeginCalls++; CopiedBytes += (uint)sizeof(WindowState) + (uint)sizeof(GuiCapture) + 8;
        return capture;
    }
    public GuiStats Draw(GuiFrame frame, ReadOnlySpan<GuiItem> items, ReadOnlySpan<byte> text)
    {
        Verify(); long started = System.Diagnostics.Stopwatch.GetTimestamp();
        if (frame.ItemCount != items.Length || frame.TextBytes != text.Length) throw new ArgumentException("GUI view length mismatch.");
        PluginError error = default; GuiStats stats = default; uint count = 0;
        fixed (GuiItem* input = items) fixed (byte* source = text) fixed (GuiEvent* events = _events) fixed (byte* outputText = _text)
            PluginModule.Check(Module.Id, "draw_gui", _draw(Module.Context, _handle, &frame, input, source, events, 256, &count, outputText, 65536, &stats, &error), error);
        if (count > 256 || stats.TextBytes > 65536 || stats.StructSize != sizeof(GuiStats)) throw new InvalidOperationException("Invalid GUI response.");
        for (int i = 0; i < count; i++) {
            var e = _events[i];
            if (e.Frame != frame.Frame || e.ViewGeneration != frame.ViewGeneration || e.DocumentGeneration != frame.DocumentGeneration ||
                e.Revision != frame.Revision || e.Phase is < 1 or > 3 || !double.IsFinite(e.Value) ||
                e.TextOffset > stats.TextBytes || e.TextLength > stats.TextBytes - e.TextOffset)
                throw new InvalidOperationException("Invalid/foreign GUI event.");
        }
        _count = (int)count; _textCount = (int)stats.TextBytes;
        DrawCalls++; CopiedBytes += (uint)sizeof(GuiFrame) + frame.ItemCount * (uint)sizeof(GuiItem) + frame.TextBytes + count * (uint)sizeof(GuiEvent) + stats.TextBytes + (uint)sizeof(GuiStats);
        LastDrawMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return stats;
    }
    // Borrowed managed spans valid until next Draw; native has already copied all output.
    public ReadOnlySpan<GuiEvent> Events { get { Verify(); return _events.AsSpan(0, _count); } }
    public ReadOnlySpan<byte> OutputText { get { Verify(); return _text.AsSpan(0, _textCount); } }
    public void Dispose()
    {
        if (_disposed) return; Verify(); PluginError error = default;
        if (_rendererOwner is not null) {
            try { _rendererOwner.WaitIdle(); }
            catch (PluginException e) when (e.Result == PluginResult.DeviceLost) { /* Faulted device has stopped execution; release, do not recover. */ }
        }
        PluginModule.Check(Module.Id, "destroy_gui", _destroy(Module.Context, _handle, &error), error);
        _handle = 0; _rendererLease?.Dispose(); _rendererLease = null; _rendererOwner = null; _platformLease?.Dispose(); _lease.Dispose(); _disposed = true;
    }
}
