namespace Ncma.Assets.Authoring;

// One owner-thread clock shared by every asset participant and watcher consumer of a project session.
public sealed class AssetRevisionClock
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    public ulong Revision { get; private set; }
    public void Advance()
    {
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Asset revision requires owner thread.");
        Revision = checked(Revision + 1);
    }
}
