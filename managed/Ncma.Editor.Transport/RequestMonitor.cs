using Ncma.Editor.Core;
namespace Ncma.Editor.Transport;

// Trusted owner-thread restriction/receipt observer. It cannot grant permissions, invoke
// another tool or replace the actual result. No IPC registration method exists.
public interface IEditorRequestMonitor
{
    string? Before(Guid connection, CapabilityRequest request);
    void After(Guid connection, CapabilityRequest request, CapabilityResult result);
}

public sealed partial class EditorEndpoint
{
    private IEditorRequestMonitor? _requestMonitor;
    private bool _requestMonitorFault;
    public void AttachRequestMonitor(IEditorRequestMonitor monitor)
    {
        Verify(); ArgumentNullException.ThrowIfNull(monitor);
        if (_requestMonitor is not null) throw new InvalidOperationException("Request monitor already installed.");
        _requestMonitor = monitor;
    }
    private string? CheckMonitor(Guid connection, CapabilityRequest request)
    {
        if (_requestMonitorFault) return "request_monitor_faulted";
        try { return _edit.ObserveReadOnly(() => _requestMonitor?.Before(connection, request)); }
        catch { _requestMonitorFault = true; return "request_monitor_faulted"; }
    }
    private void ObserveMonitor(Guid connection, CapabilityRequest request, CapabilityResult result)
    {
        try { _edit.ObserveReadOnly(() => { _requestMonitor?.After(connection, request, result); return true; }); }
        // A committed result is never replaced by a pretend rollback. Subsequent calls fail closed.
        catch { _requestMonitorFault = true; }
    }
}
