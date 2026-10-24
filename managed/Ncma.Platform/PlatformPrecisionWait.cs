using System.Runtime.InteropServices;
using Ncma.Interop;
namespace Ncma.Platform;

// Low-level waiting only. Host retains this owner-thread resource across renderer
// recovery and disposes it before unloading Platform. No finalizer/native fallback.
public sealed unsafe class PlatformPrecisionWait : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint Create(ulong context, ulong* handle, PluginError* error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint WaitNative(ulong context, ulong handle, uint microseconds, PluginError* error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint Destroy(ulong context, ulong handle, PluginError* error);
    private readonly PluginLease _lease;
    private readonly WaitNative _wait;
    private readonly Destroy _destroy;
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private ulong _handle;
    private bool _disposed;

    public PlatformPrecisionWait(PluginModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if(module.Kind!=ModuleKind.Platform||module.AbiMajor!=1||module.AbiMinor is not (1 or 2))
            throw new ArgumentException("Explicit Platform 1.1 or 1.2 required.",nameof(module));
        _lease=module.AcquireLease();
        try {
            var create=module.ReadFunction<Create>(120);
            _wait=module.ReadFunction<WaitNative>(128);
            _destroy=module.ReadFunction<Destroy>(136);
            PluginError error=default;ulong handle=0;
            PluginModule.Check(module.Id,"create_precision_wait",create(module.Context,&handle,&error),error);
            _handle=handle;
        } catch {_lease.Dispose();throw;}
    }
    private PluginModule Verify()
    {
        if(Environment.CurrentManagedThreadId!=_owner)throw new InvalidOperationException("Precision wait requires its owner thread.");
        ObjectDisposedException.ThrowIf(_disposed,this);
        return _lease.Module;
    }
    public void WaitMicroseconds(uint microseconds)
    {
        var module=Verify();
        if(microseconds is <1 or >8000)throw new ArgumentOutOfRangeException(nameof(microseconds));
        PluginError error=default;
        PluginModule.Check(module.Id,"precision_wait",_wait(module.Context,_handle,microseconds,&error),error);
    }
    public void Dispose()
    {
        if(Environment.CurrentManagedThreadId!=_owner)throw new InvalidOperationException("Precision wait requires its owner thread.");
        if(_disposed)return;
        var module=Verify();PluginError error=default;
        PluginModule.Check(module.Id,"destroy_precision_wait",_destroy(module.Context,_handle,&error),error);
        _handle=0;_lease.Dispose();_disposed=true;
    }
}
