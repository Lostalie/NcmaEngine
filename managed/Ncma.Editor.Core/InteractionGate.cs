namespace Ncma.Editor.Core;

// Trusted host composition only. Neither the lease nor its commit route is an Agent capability.
// Holds no document/history: the existing EditSession remains the sole mutation authority.
public sealed class EditInteractionLease : IDisposable
{
    private readonly EditSession _owner;
    internal readonly Guid Token = Guid.NewGuid();
    internal readonly string Capability;
    internal readonly ulong Revision, Generation;
    internal EditInteractionLease(EditSession owner, string capability)
    { _owner = owner; Capability = capability; Revision = owner.Revision; Generation = owner.DocumentGeneration; }
    public bool IsActive => _owner.IsInteractionActive(this);
    public CapabilityResult Commit(CapabilityRequest request, CapabilityPermissions permissions) =>
        _owner.CommitHostInteraction(this, request, permissions);
    public void Dispose() => _owner.CancelHostInteraction(this);
}

public sealed partial class EditSession
{
    private EditInteractionLease? _interactionGate;
    private bool InteractionBusy => _draft is not null || _interactionGate is not null;
    public EditInteractionLease BeginHostInteraction(string capability)
    {
        Observe();
        if (_invoking || InteractionBusy || _frozen || _invalidated)
            throw new EditRejectedException("edit_busy");
        if (!_capabilities.TryGetValue(capability, out var descriptor) || descriptor.Risk != MutationRisk.Reversible)
            throw new ArgumentException("Interaction requires a registered reversible command.", nameof(capability));
        _ = _document.CaptureSnapshot();
        return _interactionGate = new(this, capability);
    }
    internal bool IsInteractionActive(EditInteractionLease lease)
    {
        Observe();
        return ReferenceEquals(_interactionGate, lease) && !_frozen && !_invalidated &&
            lease.Revision == Revision && lease.Generation == DocumentGeneration;
    }
    internal void CancelHostInteraction(EditInteractionLease lease)
    {
        _document.VerifyAccess();
        if (_invoking) throw new EditRejectedException("edit_busy");
        if (ReferenceEquals(_interactionGate, lease)) _interactionGate = null;
    }
    internal CapabilityResult CommitHostInteraction(EditInteractionLease lease, CapabilityRequest request, CapabilityPermissions permissions)
    {
        _document.VerifyAccess(); ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(permissions);
        if (_invoking) throw new EditRejectedException("edit_busy");
        if (!IsInteractionActive(lease) || request.SessionId != SessionId || request.ExpectedRevision != lease.Revision ||
            request.Capability != lease.Capability)
            throw new EditRejectedException("interaction_conflict");
        // No callbacks or IO between consuming the lease and entering Invoke's reentrancy guard.
        _interactionGate = null;
        return Invoke(request, permissions);
    }
}
