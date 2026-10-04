using System.Runtime.InteropServices;
using System.Text;
using Ncma.Interop;
namespace Ncma.Platform;

[StructLayout(LayoutKind.Sequential)]
public struct InputEvent { public uint Kind, Key, Action, Codepoint; public double X, Y; public ulong Sequence; }
[StructLayout(LayoutKind.Sequential)]
public unsafe struct WindowState
{
    public uint StructSize, Width, Height, FramebufferWidth, FramebufferHeight, Focused, Minimized, CloseRequested, Overflow, InputReset;
    public float ScaleX, ScaleY;
    public double PointerX, PointerY;
    public ulong Sequence;
    public fixed ulong Held[8];
    public readonly void CopyHeld(Span<ulong> output)
    {
        if (output.Length != 8) throw new ArgumentException("Eight words required.");
        fixed (ulong* held = Held) new ReadOnlySpan<ulong>(held, 8).CopyTo(output);
    }
}
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WindowDescription
{
    public uint StructSize, Width, Height, Visible;
    public byte* Title;
    public uint TitleLength, Reserved;
}
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate uint CreateWindow(ulong context, WindowDescription* description, ulong* window, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate uint DestroyWindow(ulong context, ulong window, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate uint PollEvents(ulong context, ulong window, InputEvent* events, uint capacity, uint* count, WindowState* state, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate uint GetWindowState(ulong context, ulong window, WindowState* state, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate uint SetWindowText(ulong context, ulong window, byte* text, uint length, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate uint RequestClose(ulong context, ulong window, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate uint WaitEvents(ulong context, double seconds, PluginError* error);

public sealed unsafe class PlatformWindow : IDisposable
{
    private readonly PluginLease _lease;
    private readonly CreateWindow _create;
    private readonly DestroyWindow _destroy;
    private readonly PollEvents _poll;
    private readonly GetWindowState _state;
    private readonly SetWindowText _title, _icon;
    private readonly RequestClose _close;
    private readonly WaitEvents _wait;
    private readonly InputEvent[] _events = new InputEvent[4096];
    private int _eventCount;
    private ulong _handle;
    private bool _disposed;
    public ulong PollCalls { get; private set; }
    public ulong CopiedBytes { get; private set; }
    public double LastPollMilliseconds { get; private set; }
    internal PluginModule Module => _lease.Module;
    internal ulong Handle { get { Verify(); return _handle; } }
    public PlatformWindow(PluginModule module, string title, uint width = 1280, uint height = 720, bool visible = true)
    {
        if (module.Kind != ModuleKind.Platform) throw new ArgumentException("Platform module required.");
        _lease = module.AcquireLease();
        try {
            _create = module.ReadFunction<CreateWindow>(56); _destroy = module.ReadFunction<DestroyWindow>(64);
            _poll = module.ReadFunction<PollEvents>(72); _state = module.ReadFunction<GetWindowState>(80);
            _title = module.ReadFunction<SetWindowText>(88); _icon = module.ReadFunction<SetWindowText>(96);
            _close = module.ReadFunction<RequestClose>(104); _wait = module.ReadFunction<WaitEvents>(112);
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(title);
            fixed (byte* text = bytes) {
                WindowDescription description = new() { StructSize = (uint)sizeof(WindowDescription), Width = width, Height = height,
                    Visible = visible ? 1u : 0u, Title = text, TitleLength = (uint)bytes.Length };
                PluginError error = default; ulong handle = 0;
                PluginModule.Check(module.Id, "create_window", _create(module.Context, &description, &handle, &error), error);
                _handle = handle;
            }
        } catch { _lease.Dispose(); throw; }
    }
    private void Verify() { _ = Module; ObjectDisposedException.ThrowIf(_disposed, this); }
    public WindowState State
    {
        get { Verify(); WindowState state = default; PluginError error = default;
            PluginModule.Check(Module.Id, "window_state", _state(Module.Context, _handle, &state, &error), error); return state; }
    }
    public WindowState Poll()
    {
        Verify(); long started = System.Diagnostics.Stopwatch.GetTimestamp(); WindowState state = default; uint count = 0; PluginError error = default;
        fixed (InputEvent* events = _events)
            PluginModule.Check(Module.Id, "poll", _poll(Module.Context, _handle, events, 4096, &count, &state, &error), error);
        if (count > 4096 || state.StructSize != sizeof(WindowState)) throw new InvalidOperationException("Invalid platform response.");
        _eventCount = (int)count; PollCalls++; CopiedBytes += count * (uint)sizeof(InputEvent) + (uint)sizeof(WindowState);
        LastPollMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return state;
    }
    public ReadOnlySpan<InputEvent> Events { get { Verify(); return _events.AsSpan(0, _eventCount); } }
    private void SetText(SetWindowText operation, string value, string phase)
    {
        Verify(); byte[] bytes = new UTF8Encoding(false, true).GetBytes(value); PluginError error = default;
        fixed (byte* text = bytes) PluginModule.Check(Module.Id, phase, operation(Module.Context, _handle, text, (uint)bytes.Length, &error), error);
    }
    public void SetTitle(string title) => SetText(_title, title, "set_title");
    public void SetIcon(string path) => SetText(_icon, path, "set_icon");
    public void RequestClose()
    { Verify(); PluginError error = default; PluginModule.Check(Module.Id, "request_close", _close(Module.Context, _handle, &error), error); }
    public void Wait(double seconds)
    { Verify(); PluginError error = default; PluginModule.Check(Module.Id, "wait", _wait(Module.Context, seconds, &error), error); }
    public void Dispose()
    {
        if (_disposed) return; Verify(); PluginError error = default;
        PluginModule.Check(Module.Id, "destroy_window", _destroy(Module.Context, _handle, &error), error);
        _handle = 0; _lease.Dispose(); _disposed = true;
    }
}
