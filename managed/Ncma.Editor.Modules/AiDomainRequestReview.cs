using Ncma.AI;
using Ncma.Editor.Services;
using Ncma.Modules;

namespace Ncma.Editor.Modules;

// Trusted local UI composition; not a model-callable approval endpoint or an HTTP adapter.
public sealed class AiDomainRequestReview
{
    private readonly EditorDomainProposals domains;
    private readonly AiRequestContent content;
    private readonly Func<bool>? hostCurrent;
    private readonly Func<bool>? hostIdentityCurrent;
    public DomainObservationReview Observation { get; }
    public AiPreparedChatRequest WireRequest { get; }
    public AiDomainRequestReview(EditorDomainProposals domains, DomainModuleTarget target,
        IReadOnlyList<DomainReadRequest> reads, AiRequestContent content, Func<bool>? hostCurrent = null)
        : this(domains, target, reads, content, hostCurrent, null) { }
    internal AiDomainRequestReview(EditorDomainProposals domains, DomainModuleTarget target,
        IReadOnlyList<DomainReadRequest> reads, AiRequestContent content, Func<bool>? hostCurrent, UiDomainObservation? ui,
        Func<bool>? hostIdentityCurrent = null, MetadataDomainObservation? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(domains); ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(target);
        if (!content.IsCurrent() || target.InputSchema != AiRequestContent.InputSchema ||
            target.ConfigurationId != content.Connection.Profile.ConfigurationId)
            throw new InvalidOperationException("ai_request_target_mismatch");
        this.domains = domains; this.content = content; this.hostCurrent = hostCurrent;
        this.hostIdentityCurrent = hostIdentityCurrent ?? hostCurrent;
        if ((ui is not null || metadata is not null) && reads.Count != 0 || ui is not null && metadata is not null) throw new ArgumentException("typed_observation_must_be_exclusive");
        Observation = metadata is not null ? domains.ReviewMetadataObservationRequest(target, metadata, content.CopyBytes(), Current, IdentityCurrent) :
            ui is null ? domains.ReviewObservationRequest(target, reads, content.CopyBytes(), Current, IdentityCurrent) :
            domains.ReviewUiObservationRequest(target, ui, content.CopyBytes(), Current);
        var operation = new ModuleOperation(target.ModuleId, target.ModuleVersion, target.ConfigurationId,
            target.Operation, target.InputSchema, target.OutputSchema, TimeoutMilliseconds: content.Connection.Profile.TimeoutMilliseconds);
        WireRequest = AiChatCodec.Prepare(new ModulePayload(Observation.CopyBytes()), DomainModuleBridge.Context(Observation.Source),
            operation, content.Connection);
        if (!content.IsCurrent()) throw new InvalidOperationException("ai_configuration_stale");
    }
    public ApprovedDomainRequest Approve(string fingerprint, bool humanReviewed)
    {
        if (!content.IsCurrent()) throw new InvalidOperationException("ai_configuration_stale");
        return domains.ApproveObservation(Observation, fingerprint, humanReviewed);
    }
    // Must also be used by the future dispatch adapter before IO; approval does not pin mutable transport state.
    public bool ConfigurationCurrent => content.IsCurrent();
    // Invoked by original domain service only while its callback/reentry fence is held.
    private bool Current() => content.IsCurrent() && hostCurrent?.Invoke() != false;
    private bool IdentityCurrent() => content.IsCurrent() && hostIdentityCurrent?.Invoke() != false;
}
