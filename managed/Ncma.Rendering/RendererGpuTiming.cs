using System.Runtime.InteropServices;
using Ncma.Interop;
namespace Ncma.Rendering;

public enum GpuSampleState : uint { Unavailable, Recording, Pending, Valid, Disjoint, Invalid, Failed, Abandoned }
public enum GpuTimingDomain : uint { MainSubmissionBeforePresent = 1, SkinDispatch = 2, UiProduction = 3 }
/// <summary>A copied device-scoped sample, NOT current-frame or whole-frame GPU cost.</summary>
public readonly record struct GpuTimingSample(ulong Generation, ulong Sequence, ulong OriginFrame,
    ulong ObservedFrame, ulong SkippedSamples, GpuSampleState State, GpuTimingDomain Domain, double Milliseconds)
{
    public bool IsValid => State == GpuSampleState.Valid;
}
[StructLayout(LayoutKind.Sequential)]
internal struct GpuSampleNative
{
    public uint Size, Version, State, Flags;
    public ulong Generation, Sequence, OriginFrame, ObservedFrame, Skipped;
    public double Milliseconds;
    public uint Domain, Reserved;
}
[StructLayout(LayoutKind.Sequential)]
internal struct RendererTimingApi { public uint Size, Version; public ulong Caps; public nint Read; }
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate uint ReadGpuSampleNative(ulong module, ulong renderer, GpuSampleNative* output, PluginError* error);
public sealed unsafe partial class RendererSession
{
    private ReadGpuSampleNative? _readGpuSample;
    /// <summary>Nonblocking bounded polling; no Flush/wait. Scope excludes earlier skin/offscreen uploads and display wait.</summary>
    public GpuTimingSample ReadGpuTiming()
    {
        Verify();
        if (_readGpuSample is null)
        {
            if (Module.AbiMinor < 2) throw new NotSupportedException("GPU timing requires query19/v1.");
            if (sizeof(RendererTimingApi) != 24 || sizeof(GpuSampleNative) != 72) throw new InvalidOperationException("GPU timing x64 layout.");
            RendererTimingApi api = default; PluginError queryError = default;
            PluginModule.Check(Module.Id, "query_gpu_timing", Module.ReadFunction<QuerySceneRender>(144)(Module.Context, 19, &api, 24, &queryError), queryError);
            if (api.Size != 24 || api.Version != 1 || api.Caps != 1 || api.Read == 0) throw new InvalidOperationException("GPU timing query19/v1.");
            _readGpuSample = Marshal.GetDelegateForFunctionPointer<ReadGpuSampleNative>(api.Read);
        }
        GpuSampleNative value = default; PluginError error = default;
        PluginModule.Check(Module.Id, "read_gpu_timing", _readGpuSample(Module.Context, Handle, &value, &error), error);
        return ValidateGpuTiming(value,Handle,GpuTimingDomain.MainSubmissionBeforePresent);
    }
    private static GpuTimingSample ValidateGpuTiming(GpuSampleNative value,ulong generation,GpuTimingDomain domain)
    {
        if (value.Size != 72 || value.Version != 1 || value.Flags != 0 || value.Reserved != 0 || value.Generation != generation ||
            value.Domain != (uint)domain || !Enum.IsDefined((GpuSampleState)value.State) || !double.IsFinite(value.Milliseconds) || value.Milliseconds < 0 ||
            (value.State != (uint)GpuSampleState.Valid && value.Milliseconds != 0) || (value.Sequence == 0) != (value.OriginFrame == 0) ||
            (value.State is >= 1 and <= 5 && value.Sequence == 0) || (value.State == 0 && value.Sequence != 0) ||
            (value.State is >= 2 and <= 5 && value.OriginFrame > value.ObservedFrame))
            throw new InvalidOperationException("Invalid GPU timing identity/state.");
        return new(value.Generation, value.Sequence, value.OriginFrame, value.ObservedFrame, value.Skipped,
            (GpuSampleState)value.State, (GpuTimingDomain)value.Domain, value.Milliseconds);
    }
}
