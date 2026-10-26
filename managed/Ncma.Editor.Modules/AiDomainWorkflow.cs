using Ncma.AI;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Modules;
using Ncma.Animation;
using Ncma.Assets;

namespace Ncma.Editor.Modules;

public enum AiAuthoringIntent { Scene = 1, AssetMetadata = 2, AnimationGraph = 3, UiDocument = 4 }

// Optional trusted composition, not a default product dependency or an MCP capability.
// A workflow binds ONE configuration epoch and one intent. Reconfiguration requires draining
// and unregistering the old workflow, then explicit new composition and human send approval.
public sealed class AiDomainWorkflow : IDomainModuleController
{
    public static Guid ModuleId { get; } = new("ef704084-3001-4001-8001-000000000001");
    public static Guid OutputSchema { get; } = new("ef704084-3001-4001-8001-000000000002");
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly EditorDomainProposals domains;
    private readonly AiConnectionConfiguration configuration;
    private readonly AiConnectionSnapshot connection;
    private readonly DeepSeekChatAdapter adapter;
    private readonly DomainModuleBridge bridge;
    private readonly AiAnimationDraftStage? animation;
    private readonly AiUiDraftStage? ui;
    private readonly AiMetadataDraftStage? metadata;
    private AiDomainRequestReview? outbound;
    private bool closing, disposed, unregistered;
    private ulong requestEpoch;
    public AiAuthoringIntent Intent { get; }
    public DomainModuleTarget Target => bridge.Target;
    public string Status { get { Verify(); return disposed ? "closed" : closing ? "draining" :
        animation?.Status ?? (outbound is not null && bridge.Status == "cancelled" ? "send-review" : bridge.Status); } }
    public AiCallDiagnostic? LastDiagnostic { get { Verify(); return adapter.LastDiagnostic; } }
    public DomainProposalReview? Review {
        get { Verify(); return !closing && !disposed && configuration.IsCurrent(connection) &&
            (animation?.Review ?? bridge.Review) is { } review && domains.IsCurrent(review) ? review : null; }
    }

    // Credential remains caller-owned; closing this workflow disposes transport, not a shared secret.
    // Construction/configuration performs no network IO and makes no "connected" claim.
    public AiDomainWorkflow(EditorDomainProposals domains, AiConnectionConfiguration configuration,
        AiAuthoringIntent intent, AiSessionCredential? credential = null)
        : this(domains, configuration, intent, credential, null) { }

    public AiDomainWorkflow(EditorDomainProposals domains, AiConnectionConfiguration configuration,
        EditorAnimationGraphWorkspace animationWorkspace, AiSessionCredential? credential = null)
        : this(domains, configuration, AiAuthoringIntent.AnimationGraph, credential, null, animationWorkspace) { }

    public AiDomainWorkflow(EditorDomainProposals domains, AiConnectionConfiguration configuration,
        EditorUiWorkspace uiWorkspace, AiSessionCredential? credential = null)
        : this(domains, configuration, AiAuthoringIntent.UiDocument, credential, null, uiWorkspace: uiWorkspace) { }

    internal AiDomainWorkflow(EditorDomainProposals domains, AiConnectionConfiguration configuration,
        AiAuthoringIntent intent, AiSessionCredential? credential,
        Func<AiConnectionSnapshot, ModuleOperation, DeepSeekChatAdapter>? testFactory,
        EditorAnimationGraphWorkspace? animationWorkspace = null, EditorUiWorkspace? uiWorkspace = null,
        EditorMetadataContent? metadataWorkspace = null)
    {
        ArgumentNullException.ThrowIfNull(domains); ArgumentNullException.ThrowIfNull(configuration);
        string name = intent switch {
            AiAuthoringIntent.Scene => "ai.scene.propose",
            AiAuthoringIntent.AssetMetadata => "ai.metadata.propose",
            AiAuthoringIntent.AnimationGraph when animationWorkspace is not null => "ai.animation.propose",
            AiAuthoringIntent.UiDocument when uiWorkspace is not null => "ai.ui.propose",
            _ => throw new ArgumentException("ai_intent_not_supported")
        };
        this.domains = domains; this.configuration = configuration; Intent = intent;
        if (intent != AiAuthoringIntent.AnimationGraph && animationWorkspace is not null) throw new ArgumentException("ai_workspace_intent_mismatch");
        if (intent != AiAuthoringIntent.UiDocument && uiWorkspace is not null) throw new ArgumentException("ai_workspace_intent_mismatch");
        if (animationWorkspace is not null) animation = new(domains, animationWorkspace);
        if (uiWorkspace is not null) ui = new(domains, uiWorkspace);
        if (intent != AiAuthoringIntent.AssetMetadata && metadataWorkspace is not null) throw new ArgumentException("ai_workspace_intent_mismatch");
        if (metadataWorkspace is not null) metadata = new(domains, metadataWorkspace);
        connection = configuration.Capture();
        var operation = new ModuleOperation(ModuleId, "1", connection.Profile.ConfigurationId, name,
            AiRequestContent.InputSchema, metadata is not null ? AiMetadataDraft.OutputSchema : ui is not null ? AiUiDraft.OutputSchema : animation is null ? OutputSchema : AiAnimationDraft.OutputSchema,
            TimeoutMilliseconds: connection.Profile.TimeoutMilliseconds);
        adapter = testFactory is null ? new(connection, operation, credential) : testFactory(connection, operation);
        try {
            bridge = new(domains, operation, adapter, register: false,
                intent switch {
                    AiAuthoringIntent.Scene => ["ncma.scene.transaction", "ncma.scene.delete_object"],
                    AiAuthoringIntent.AssetMetadata => metadata is not null ? [AiMetadataDraft.Capability] : ["ncma.assets.metadata.edit"],
                    AiAuthoringIntent.AnimationGraph => [AiAnimationDraft.Capability],
                    _ => [AiUiDraft.Capability]
                }, metadata is not null ? metadata.Stage : ui is not null ? ui.Stage : animation is null ? null : animation.Stage);
            domains.RegisterModule(Target, this);
        }
        catch { adapter.Dispose(); throw; }
    }

    private void Verify() {
        if (owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("ai_workflow_owner_required");
    }
    private void RequireActive() {
        Verify(); if (closing || disposed || !configuration.IsCurrent(connection))
            throw new InvalidOperationException("ai_workflow_closed_or_configuration_stale");
    }
    public AiDomainRequestReview Prepare(IReadOnlyList<DomainReadRequest> reads, string instruction,
        string templateId, string templateVersion, string templateText)
        => PrepareCore(reads, null, instruction, templateId, templateVersion, templateText);
    public AiDomainRequestReview PrepareUi(Guid exactDocument, string instruction,
        string templateId, string templateVersion, string templateText)
        => PrepareCore([], exactDocument, instruction, templateId, templateVersion, templateText);
    public AiDomainRequestReview PrepareMetadata(Guid exactAsset, AssetKind kind, string instruction,
        string templateId, string templateVersion, string templateText)
        => PrepareCore([], null, instruction, templateId, templateVersion, templateText, (exactAsset, kind));
    private AiDomainRequestReview PrepareCore(IReadOnlyList<DomainReadRequest> reads, Guid? exactUiDocument,
        string instruction, string templateId, string templateVersion, string templateText, (Guid Id, AssetKind Kind)? exactMetadata = null)
    {
        RequireActive();
        if ((ui is not null) != exactUiDocument.HasValue) throw new ArgumentException("ai_ui_exact_selection_required");
        if ((metadata is not null) != exactMetadata.HasValue || exactMetadata.HasValue && reads.Count != 0) throw new ArgumentException("ai_metadata_exact_selection_required");
        if (bridge.Status == "pending" || !bridge.TryClose()) throw new InvalidOperationException("ai_workflow_drain_required");
        Cancel();
        animation?.Bind(domains.CaptureSource(), reads);
        var uiObservation = ui?.Bind(domains.CaptureSource(), exactUiDocument!.Value);
        var metadataObservation = metadata?.Bind(domains.CaptureSource(), exactMetadata!.Value.Id, exactMetadata.Value.Kind);
        ulong epoch = requestEpoch;
        var content = new AiRequestContent(connection, instruction, templateId, templateVersion, templateText);
        return outbound = new(domains, Target, reads, content,
            () => !closing && !disposed && epoch == requestEpoch && (animation?.IsCurrent() ?? true) && (ui?.IsBound ?? true) && (metadata?.IsBound ?? true), uiObservation,
            () => !closing && !disposed && epoch == requestEpoch && (animation?.IdentityCurrent() ?? true) && (ui?.IsBound ?? true) && (metadata?.IsBound ?? true), metadataObservation);
    }
    public void ApproveAndBegin(AiDomainRequestReview displayed, string fingerprint, bool humanReviewed)
    {
        RequireActive();
        if (!ReferenceEquals(displayed, outbound)) throw new EditRejectedException("ai_send_review_foreign_or_stale");
        Begin(displayed.Approve(fingerprint, humanReviewed));
    }
    public void Begin(ApprovedDomainRequest request)
    {
        RequireActive(); ArgumentNullException.ThrowIfNull(request);
        if (outbound is null || request.RequestId != outbound.Observation.RequestId ||
            request.Target != Target || !outbound.ConfigurationCurrent)
            throw new EditRejectedException("ai_send_review_foreign_or_stale");
        bridge.Begin(request);
    }
    public void Pump()
    {
        Verify(); if (disposed) return;
        if (closing) { TryClose(); return; }
        if (!configuration.IsCurrent(connection)) { Cancel(); closing = true; TryClose(); return; }
        try {
            if (animation?.PendingCurrent() == false) { Cancel(); return; }
            bridge.Pump();
        }
        catch { animation?.Cancel(); ui?.Cancel(); metadata?.Cancel(); throw; }
    }
    // No automatic commit: even a valid model result remains a complete original-domain review.
    public CapabilityResult Confirm(DomainProposalReview displayed, string fingerprint, bool humanReviewed,
        Guid? confirmedDelete = null, bool bindingsReviewed = false)
    {
        RequireActive();
        if (!ReferenceEquals(displayed, Review)) throw new EditRejectedException("ai_result_review_foreign_or_stale");
        return domains.Confirm(displayed, fingerprint, humanReviewed, confirmedDelete, bindingsReviewed);
    }
    public AnimationGraphDefinition CaptureAnimationCandidate() {
        RequireActive(); return (animation ?? throw new InvalidOperationException("ai_animation_intent_required")).CaptureCandidate();
    }
    public AnimationGraphWriteReview ReviewAnimationResources() {
        RequireActive(); return (animation ?? throw new InvalidOperationException("ai_animation_intent_required")).CaptureResources();
    }
    public DomainProposalReview ApproveAnimationResources(AnimationGraphWriteReview displayed, string fingerprint, Guid exactGraph, bool humanReviewed) {
        RequireActive(); return (animation ?? throw new InvalidOperationException("ai_animation_intent_required")).ApproveResources(displayed, fingerprint, exactGraph, humanReviewed);
    }
    public void Cancel() {
        Verify(); requestEpoch = checked(requestEpoch + 1);
        animation?.Cancel();
        ui?.Cancel();
        metadata?.Cancel();
        if (!disposed) bridge.Cancel(); outbound = null;
    }

    // Permanent close, never forced disposal. The host retains this controller until true.
    public bool TryClose()
    {
        Verify(); if (disposed) return true;
        closing = true; Cancel();
        if (!bridge.TryClose()) return false;
        adapter.Dispose(); disposed = true; return true;
    }
    // Explicit composition replacement. Domain owner shutdown also calls Cancel/TryClose.
    public bool TryUnregister()
    {
        Verify(); if (unregistered) return true;
        if (!domains.TryUnregisterModule(Target, this)) return false;
        unregistered = true; return true;
    }
}
