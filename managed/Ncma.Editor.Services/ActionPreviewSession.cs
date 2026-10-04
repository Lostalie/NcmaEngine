namespace Ncma.Editor.Services;

// Lazy trusted-local numerical module. No native entry/hostfxr or scene integration.
public sealed class ActionPreviewSession(string libraryPath) : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private ActionAnimationSession? _session;
    private bool _disposed;
    private void Verify() {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Preview owner thread required.");
        ObjectDisposedException.ThrowIf(_disposed,this);
    }
    public ActionAnimationSession? Current { get { Verify(); return _session; } }
    public ActionAnimationSession Open() { Verify(); return _session ??= new(libraryPath); }
    public void Tick(double seconds) { Verify(); _session?.Tick(seconds); }
    public void Dispose() { if (_disposed)return;Verify();_session?.Dispose();_session=null;_disposed=true; }
}
