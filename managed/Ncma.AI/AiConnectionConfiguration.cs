namespace Ncma.AI;

// Configuration state is deliberately NOT a claim of a successful transport connection.
public enum AiConfigurationState { Unconfigured, Configured, Closed }
public sealed class AiConnectionConfiguration
{
    private readonly int owner = Environment.CurrentManagedThreadId;
    private AiConnectionProfile? profile;
    private Guid epoch = Guid.NewGuid();
    private AiConnectionLifetime lifetime = new();
    private bool closed;
    public AiConfigurationState State { get { Verify(); return closed ? AiConfigurationState.Closed :
        profile is null ? AiConfigurationState.Unconfigured : AiConfigurationState.Configured; } }
    private void Verify()
    {
        if (Environment.CurrentManagedThreadId != owner) throw new InvalidOperationException("ai_owner_thread_required");
    }
    public void Configure(AiConnectionProfile value)
    {
        Verify(); ArgumentNullException.ThrowIfNull(value);
        if (closed) throw new ObjectDisposedException(nameof(AiConnectionConfiguration));
        lifetime.Invalidate(); lifetime = new();
        profile = value; epoch = Guid.NewGuid(); // Even restoring identical configuration never revives a review.
    }
    public void Clear()
    {
        Verify(); if (closed) throw new ObjectDisposedException(nameof(AiConnectionConfiguration));
        lifetime.Invalidate(); lifetime = new(); profile = null; epoch = Guid.NewGuid();
    }
    public void Close() { Verify(); lifetime.Invalidate(); profile = null; epoch = Guid.NewGuid(); closed = true; }
    public AiConnectionSnapshot Capture()
    {
        Verify();
        if (closed || profile is null) throw new InvalidOperationException("ai_not_configured");
        return new(this, epoch, profile, lifetime);
    }
    public bool IsCurrent(AiConnectionSnapshot snapshot)
    {
        Verify(); ArgumentNullException.ThrowIfNull(snapshot);
        return !closed && ReferenceEquals(snapshot.Owner, this) && snapshot.Epoch == epoch &&
            ReferenceEquals(snapshot.Profile, profile);
    }
}

public sealed class AiConnectionSnapshot
{
    private readonly AiConnectionLifetime lifetime;
    internal bool ValidForDispatch => lifetime.IsValid;
    internal AiConnectionConfiguration Owner { get; }
    public Guid Epoch { get; }
    public AiConnectionProfile Profile { get; }
    internal AiConnectionSnapshot(AiConnectionConfiguration owner, Guid epoch, AiConnectionProfile profile, AiConnectionLifetime lifetime)
    { Owner = owner; Epoch = epoch; Profile = profile; this.lifetime = lifetime; }
}

// Background reads never call owner-thread configuration APIs. No cancellation callbacks execute on Configure.
internal sealed class AiConnectionLifetime
{
    private int valid = 1;
    internal bool IsValid => Volatile.Read(ref valid) != 0;
    internal void Invalidate() => Volatile.Write(ref valid, 0);
}
