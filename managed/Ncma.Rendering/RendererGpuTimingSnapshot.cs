using System.Runtime.InteropServices;
using Ncma.Interop;
namespace Ncma.Rendering;

/// <summary>Current query status beside the latest Valid/Disjoint/Invalid resolution.
/// One retained copy, not a history or complete GPU coverage; deduplicate by generation/domain/sequence.
/// Retired resource tokens are historical metadata, never authorization to access resources.</summary>
public readonly record struct GpuTimingSnapshot(GpuWorkTimingSample Current, GpuWorkTimingSample LastResolved);
[StructLayout(LayoutKind.Sequential)]
internal struct GpuTimingSnapshotNative { public GpuWorkSampleNative Current, LastResolved; }
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate uint ReadGpuTimingSnapshotNative(ulong module, ulong renderer, uint domain,
    GpuTimingSnapshotNative* output, PluginError* error);
public sealed unsafe partial class RendererSession
{
    private ReadGpuTimingSnapshotNative? _readGpuTimingSnapshot;
    /// <summary>Optional query21/v1. At most three nonblocking DONOTFLUSH polls; no waits,
    /// resource initialization or histories. A new Begin cannot overwrite LastResolved.</summary>
    public GpuTimingSnapshot ReadGpuTimingSnapshot(GpuTimingDomain domain)
    {
        Verify();
        if (domain is not (GpuTimingDomain.MainSubmissionBeforePresent or GpuTimingDomain.SkinDispatch or GpuTimingDomain.UiProduction))
            throw new ArgumentOutOfRangeException(nameof(domain));
        if (_readGpuTimingSnapshot is null)
        {
            if (Module.AbiMinor < 2) throw new NotSupportedException("GPU timing snapshots require query21/v1.");
            if (sizeof(GpuTimingSnapshotNative) != 176 || sizeof(RendererTimingApi) != 24)
                throw new InvalidOperationException("GPU timing snapshot x64 layout.");
            RendererTimingApi api = default; PluginError queryError = default;
            PluginModule.Check(Module.Id, "query_gpu_timing_snapshot",
                Module.ReadFunction<QuerySceneRender>(144)(Module.Context, 21, &api, 24, &queryError), queryError);
            if (api.Size != 24 || api.Version != 1 || api.Caps != 7 || api.Read == 0)
                throw new InvalidOperationException("GPU timing snapshot query21/v1.");
            _readGpuTimingSnapshot = Marshal.GetDelegateForFunctionPointer<ReadGpuTimingSnapshotNative>(api.Read);
        }
        GpuTimingSnapshotNative value = default; PluginError error = default;
        PluginModule.Check(Module.Id, "read_gpu_timing_snapshot",
            _readGpuTimingSnapshot(Module.Context, Handle, (uint)domain, &value, &error), error);
        var current = ValidateGpuSnapshotSource(value.Current, Handle, domain);
        var resolved = ValidateGpuSnapshotSource(value.LastResolved, Handle, domain);
        if (resolved.Timing.State is not (GpuSampleState.Unavailable or GpuSampleState.Valid or GpuSampleState.Disjoint or GpuSampleState.Invalid) ||
            resolved.Timing.Sequence > current.Timing.Sequence ||
            resolved.Timing.ObservedFrame != current.Timing.ObservedFrame ||
            (current.Timing.State is GpuSampleState.Valid or GpuSampleState.Disjoint or GpuSampleState.Invalid && current != resolved))
            throw new InvalidOperationException("Invalid GPU snapshot resolution/state.");
        return new(current, resolved);
    }
    private static GpuWorkTimingSample ValidateGpuSnapshotSource(GpuWorkSampleNative value, ulong generation, GpuTimingDomain domain)
    {
        var timing = ValidateGpuTiming(value.Timing, generation, domain);
        if (domain != GpuTimingDomain.UiProduction ? value.Resource != 0 || value.ContentRevision != 0 :
            timing.Sequence == 0 ? value.Resource != 0 || value.ContentRevision != 0 : value.Resource == 0 || value.ContentRevision == 0)
            throw new InvalidOperationException("Invalid GPU snapshot source metadata.");
        return new(timing, value.Resource, value.ContentRevision);
    }
}
