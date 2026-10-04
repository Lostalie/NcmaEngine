using System.Runtime.InteropServices;
namespace Ncma.Interop;
public enum ModuleKind : uint { Platform = 1, Gui = 2, Renderer = 3, Physics = 4, Fixture = 127 }
public enum PluginResult : uint { Ok, AbiMismatch, InvalidArgument, WrongThread, InvalidHandle, BufferTooSmall, UnsupportedFeature, Busy, DeviceLost, ShutdownTimeout, InternalError }
public sealed class PluginException(string pluginId, string phase, PluginResult result, string message) : InvalidOperationException(message)
{
    public string PluginId { get; } = pluginId;
    public string Phase { get; } = phase;
    public PluginResult Result { get; } = result;
}
[StructLayout(LayoutKind.Sequential)]
public struct ModuleStatus { public uint StructSize, State; public ulong LiveResources, LiveJobs, Sequence; }
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PluginError
{
    public uint Code, Reserved, RequiredBytes, MessageLength;
    public fixed byte Message[512];
    public readonly string Text
    {
        get { fixed (byte* bytes = Message) return new System.Text.UTF8Encoding(false, true).GetString(bytes, (int)Math.Min(MessageLength, 512)); }
    }
}
[StructLayout(LayoutKind.Sequential)]
internal struct ModuleApi
{
    public uint StructSize, Major, Minor, Kind;
    public ulong Capabilities;
    public nint Initialize, Shutdown, GetStatus, ReadDiagnostic;
}
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate uint GetApi(uint major, uint minor, void* output, uint capacity, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate uint Initialize(byte* input, uint count, ulong* context, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate uint Shutdown(ulong context, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate uint GetStatus(ulong context, ModuleStatus* output, PluginError* error);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate uint ReadDiagnostic(ulong context, byte* output, uint capacity, uint* required, PluginError* error);
