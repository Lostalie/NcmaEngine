using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Modules;
using System.Text.Json;

namespace Ncma.Editor.Modules;

// Optional host integration, not a default Editor dependency. Adapter selection/installation is
// trusted composition, not model data. No gRPC/Python/inference startup or hidden approval here.
public sealed class DomainModuleBridge : IDomainModuleController
{
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly EditorDomainProposals domains;
    private readonly ModuleOperation operation;
    private readonly IModuleAdapter adapter;
    private readonly HashSet<string>? allowedCapabilities;
    private readonly Func<ApprovedDomainRequest, byte[], DomainProposalReview?>? stageResult;
    private ModuleSession? session;
    private ApprovedDomainRequest? request;
    private ModuleContext? context;
    public DomainProposalReview? Review { get; private set; }
    public string Status { get; private set; } = "idle";
    public DomainModuleBridge(EditorDomainProposals domains, ModuleOperation operation, IModuleAdapter adapter)
        : this(domains, operation, adapter, true, null) { }
    internal DomainModuleBridge(EditorDomainProposals domains, ModuleOperation operation, IModuleAdapter adapter,
        bool register, IEnumerable<string>? allowedCapabilities,
        Func<ApprovedDomainRequest, byte[], DomainProposalReview?>? stageResult = null)
    {
        this.domains = domains ?? throw new ArgumentNullException(nameof(domains));
        this.operation = operation ?? throw new ArgumentNullException(nameof(operation));
        this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        this.allowedCapabilities = allowedCapabilities is null ? null : new(allowedCapabilities, StringComparer.Ordinal);
        this.stageResult = stageResult;
        _ = new ModuleCatalog([operation]);
        if (register) domains.RegisterModule(Target, this);
    }
    private void Verify() { if (owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Module bridge owner thread required."); }
    public DomainModuleTarget Target => new(operation.ModuleId, operation.ModuleVersion, operation.ConfigurationId,
        operation.Name, operation.InputSchema, operation.OutputSchema);
    public static ModuleContext Context(DomainProposalSource s) => new(s.ProjectId, s.HostSessionId, s.EditSessionId,
        s.DocumentSessionId, s.DocumentGeneration, s.Revision, s.BehaviourCatalogGeneration, s.ReviewEpoch,
        s.Selection, s.EndpointId, s.AudienceRevision);
    public void Begin(ApprovedDomainRequest approved)
    {
        ArgumentNullException.ThrowIfNull(approved);
        Verify(); if (session is not null) throw new InvalidOperationException("Drain the previous module session first.");
        context = Context(approved.Source); context.Validate();
        byte[] observation = domains.ClaimObservation(approved, Target);
        request = approved; Review = null;
        try {
            session = new(new([operation]), adapter, context);
            if (session.Submit(approved.RequestId, operation.Name, observation) != ModuleAdmission.Accepted)
                throw new EditRejectedException("module_admission_failed");
            Status = "pending";
        }
        catch { Status = "failed"; session?.Stop(); domains.CancelRequest(approved); throw; }
    }
    // Explicit host pump; original mailbox owns remaining deadlines, backpressure and one delivery.
    public void Pump()
    {
        Verify(); if (session is null || request is null) return;
        if (Status != "pending") { TryClose(); return; }
        try {
            if (!domains.RequestIdentityCurrent(request)) { Cancel(); Status = "stale"; return; }
            if (!session.TryTake(request.RequestId, context!, out var outcome)) return;
            if (outcome!.Status == ModuleOutcomeStatus.NeedsDomainValidation && outcome.Proposal is not null) {
                byte[] bytes = outcome.Proposal.CopyBytes();
                if (allowedCapabilities is not null) {
                    using var json = JsonDocument.Parse(bytes);
                    if (!json.RootElement.TryGetProperty("capability", out var capability) ||
                        capability.ValueKind != JsonValueKind.String || !allowedCapabilities.Contains(capability.GetString()!))
                        throw new EditRejectedException("ai_intent_domain_mismatch");
                }
                Review = stageResult is null ? domains.ReceiveModuleResult(request, bytes) : stageResult(request, bytes);
                Status = Review is null ? "resource-review" : "review";
            }
            else { domains.CancelRequest(request); Status = outcome.Status.ToString(); }
        }
        catch { Status = "failed"; domains.CancelRequest(request); throw; }
        finally { if (Status != "pending") session.Stop(); }
    }
    public void Cancel()
    {
        Verify();
        // Always release mailbox ownership even if the authoring owner has already closed.
        session?.Stop();
        if (request is not null) {
            try { domains.CancelRequest(request); }
            catch (Exception e) when (e is EditRejectedException or ObjectDisposedException) { }
        }
        if (Review is not null) {
            try { domains.Discard(Review); }
            catch (Exception e) when (e is EditRejectedException or ObjectDisposedException) { }
        }
        Review = null; Status = "cancelled";
    }
    public bool TryClose()
    {
        Verify(); if (session is null) return true;
        if (Status == "pending") throw new InvalidOperationException("Cancel or consume before closing.");
        if (!session.TryClose()) return false;
        session = null; request = null; context = null; return true;
    }
}
