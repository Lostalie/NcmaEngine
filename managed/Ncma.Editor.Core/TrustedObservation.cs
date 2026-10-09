namespace Ncma.Editor.Core;

public sealed partial class EditSession
{
    // Trusted host observers may inspect copied state, not mutate World or recursively
    // invoke/register capabilities. This is not an Agent tool or a permission grant.
    public T ObserveReadOnly<T>(Func<T> observe)
    {
        _document.VerifyAccess(); ArgumentNullException.ThrowIfNull(observe);
        if (_invoking) throw new EditRejectedException("edit_busy");
        _invoking = true;
        try { using (_document.World.ReadOnly()) return observe(); }
        finally { _invoking = false; }
    }
}
