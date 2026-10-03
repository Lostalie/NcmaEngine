using System.Runtime.InteropServices;

namespace Ncma;

/// <summary>
/// Isolated native action-animation preview session (not yet a scene/skinned-mesh component).
/// Step is deterministic and explicit. It returns root motion and notifies through InspectJson.
/// </summary>
public sealed class ActionAnimationSession : IDisposable
{
    private readonly object gate = new();
    private ulong handle;

    public ActionAnimationSession()
    {
        if (AnimationNative.Version() != 1)
            throw new NotSupportedException("Unsupported native animation ABI.");
        handle = AnimationNative.Create(1);
        if (handle == 0) throw new InvalidOperationException(AnimationNative.Error());
    }

    public void SetSpeed(double speed) => Execute(1, speed);
    public void TriggerAction(string action) => Execute(2, 0, action);
    public void SetPaused(bool paused) => Execute(3, paused ? 1 : 0);
    public void Step(double seconds) => Execute(4, seconds);
    public void Reset() => Execute(5);
    public void Undo() => Execute(6);
    public void Redo() => Execute(7);

    public string InspectJson()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(handle == 0, this);
            nint pointer = AnimationNative.Inspect(handle);
            if (pointer == 0) throw new InvalidOperationException(AnimationNative.Error());
            return Marshal.PtrToStringUTF8(pointer)!;
        }
    }

    private void Execute(uint command, double value = 0, string text = "")
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(handle == 0, this);
            if (AnimationNative.Command(handle, command, value, text) == 0)
                throw new InvalidOperationException(AnimationNative.Error());
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (handle != 0) AnimationNative.Destroy(handle);
            handle = 0;
        }
        GC.SuppressFinalize(this);
    }

    ~ActionAnimationSession() { Dispose(); }

    private static class AnimationNative
    {
        private const string Library = "NcmaNative";
        [DllImport(Library, EntryPoint = "ncma_animation_abi_version", CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint Version();
        [DllImport(Library, EntryPoint = "ncma_animation_create", CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong Create(uint version);
        [DllImport(Library, EntryPoint = "ncma_animation_destroy", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Destroy(ulong session);
        [DllImport(Library, EntryPoint = "ncma_animation_command", CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte Command(ulong session, uint command, double value,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
        [DllImport(Library, EntryPoint = "ncma_animation_inspect", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint Inspect(ulong session);
        [DllImport(Library, EntryPoint = "ncma_animation_last_error", CallingConvention = CallingConvention.Cdecl)]
        private static extern nint LastError();
        internal static string Error() => Marshal.PtrToStringUTF8(LastError()) ?? "Native animation error";
    }
}
