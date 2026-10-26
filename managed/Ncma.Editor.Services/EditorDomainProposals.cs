using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.Editor.Core;

namespace Ncma.Editor.Services;

// Authoring identities only. This is not a runtime observation or a permission grant.
public sealed record DomainProposalSource(Guid ProjectId, Guid HostSessionId, Guid EditSessionId,
    Guid DocumentSessionId, ulong DocumentGeneration, ulong Revision, Guid BehaviourCatalogGeneration,
    ulong ReviewEpoch, Guid? Selection, Guid EndpointId, ulong AudienceRevision);

public sealed class DomainProposalReview
{
    private readonly byte[] payload;
    private readonly CommandImpact impact;
    private readonly byte[] changes;
    internal readonly OriginalCommandBatchReview? Batch;
    internal readonly ParticipantCommandReview? Participant;
    internal readonly string Capability;
    internal readonly Guid? DeleteTarget;
    internal readonly EditorDomainProposals Owner;
    internal readonly long Started;
    // true: re-read original observations at a safe preflight boundary;
    // false: identity/configuration only inside the original command's no-reentry fence.
    internal readonly Func<bool, bool>? RequestStillCurrent;
    internal DomainProposalReview(EditorDomainProposals owner, long started, Guid id,
        DomainProposalSource source, byte[] bytes, CommandImpact impact, OriginalCommandBatchReview? batch,
        byte[] changes, string capability, Guid? deleteTarget, ParticipantCommandReview? participant = null,
        Func<bool, bool>? requestStillCurrent = null)
    {
        Owner = owner; Started = started; ProposalId = id; Source = source;
        RequestStillCurrent = requestStillCurrent;
        payload = (byte[])bytes.Clone(); this.impact = Copy(impact); this.changes = (byte[])changes.Clone();
        Batch = batch; Participant = participant; Capability = capability; DeleteTarget = deleteTarget;
        PayloadSha256 = Convert.ToHexString(SHA256.HashData(payload));
        Fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
            version = 2, ProposalId, Source, PayloadSha256, impact, batchFingerprint = batch?.Fingerprint,
            changesHash = Convert.ToHexString(SHA256.HashData(changes)), capability, deleteTarget
        })));
    }
    public Guid ProposalId { get; }
    public DomainProposalSource Source { get; }
    public string PayloadSha256 { get; }
    public string Fingerprint { get; }
    public string DomainCapability => Capability;
    public Guid? DestructiveTarget => DeleteTarget;
    public byte[] CopyPayload() => (byte[])payload.Clone();
    public byte[] CopyChanges() => (byte[])changes.Clone();
    public CommandImpact CopyImpact() => Copy(impact);
    private static CommandImpact Copy(CommandImpact value) => new(
        (Guid[])value.Objects.Clone(), (Guid[])value.CreatedObjects.Clone(),
        (string[])value.ComponentTypes.Clone(), (Guid[])value.Bindings.Clone(), value.DocumentReplacement);
}

/// <summary>
/// Trusted local review/confirmation, not a remote approval capability or transport dependency.
/// Original domain permissions remain authoritative; reviews cannot be sent to unapproved audiences.
/// </summary>
public sealed partial class EditorDomainProposals
{
    public const int MaximumBytes = 65_536, MaximumLifetimeProposals = 256, ReviewSeconds = 60;
    private readonly EditSession edit;
    private readonly Guid project, host;
    private readonly TimeProvider time;
    private readonly Func<(Guid EndpointId, ulong AudienceRevision)>? hostState;
    private readonly HashSet<Guid> issued = [];
    private ulong epoch = 1;
    private bool busy, closed;
    internal bool ReviewInProgress => busy;

    public EditorDomainProposals(EditSession edit, Guid projectId, Guid hostSessionId, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(edit); edit.Document.VerifyAccess();
        if (projectId == Guid.Empty || hostSessionId == Guid.Empty) throw new ArgumentException("Missing host identity.");
        this.edit = edit; project = projectId; host = hostSessionId; time = timeProvider ?? TimeProvider.System;
    }

    // Only the actual owner installs lifecycle checks. No model-supplied callback or authority.
    internal EditorDomainProposals(EditSession edit, Guid projectId, Guid hostSessionId, TimeProvider? timeProvider,
        Func<(Guid EndpointId, ulong AudienceRevision)> hostState) : this(edit, projectId, hostSessionId, timeProvider)
        => this.hostState = hostState;

    private void Check()
    {
        edit.Document.VerifyAccess();
        if (closed || busy) throw new EditRejectedException("proposal_service_unavailable");
        _ = hostState?.Invoke();
    }
    private DomainProposalSource Source()
    {
        var audience = hostState?.Invoke() ?? (Guid.Empty, 0UL);
        var state = edit.State;
        if (state.Frozen || state.HistoryInvalidated || state.EditBusy)
            throw new EditRejectedException("proposal_edit_unavailable");
        return new(project, host, edit.SessionId, edit.Document.SessionId, edit.DocumentGeneration,
            edit.Revision, edit.BehaviourCatalogGeneration, epoch, state.Selection, audience.Item1, audience.Item2);
    }
    public DomainProposalSource CaptureSource() { Check(); return Source(); }

    // Host calls this on audience/scope revocation or project lifecycle invalidation. Tombstones
    // remain bounded for this service lifetime; revocation cannot restore an earlier review.
    public void Invalidate() { Check(); HostInvalidate(); }
    public void Close() { Check(); HostInvalidate(close: true); }

    // Closing/retrying disposal and Stop may overlap. Owner cleanup must not reopen a closed
    // service or fail just because the first disposal attempt already invalidated proposals.
    internal void HostInvalidate(bool close = false)
    {
        edit.Document.VerifyAccess();
        if (busy) throw new EditRejectedException("proposal_service_unavailable");
        if (closed) return;
        epoch = checked(epoch + 1); closed = close;
        if (pending is not null) pending.Completed = true;
        pending = null; observation = null;
        busy = true;
        try { foreach (var controller in modules.Values) controller.Cancel(); }
        finally { busy = false; }
    }

    public DomainProposalReview Review(Guid proposalId, DomainProposalSource expected, ReadOnlySpan<byte> utf8)
        => ReviewCore(proposalId, expected, utf8, null);

    private DomainProposalReview ReviewCore(Guid proposalId, DomainProposalSource expected, ReadOnlySpan<byte> utf8,
        Func<bool, bool>? requestStillCurrent)
    {
        Check();
        if (expected != Source()) throw new EditRejectedException("proposal_source_stale");
        if (proposalId == Guid.Empty) throw new ArgumentException("Missing proposal identity.");
        if (issued.Count == MaximumLifetimeProposals) throw new EditRejectedException("proposal_lifetime_budget");
        if (!issued.Add(proposalId)) throw new EditRejectedException("proposal_id_reused");
        busy = true;
        try
        {
            if (requestStillCurrent?.Invoke(true) == false) throw new EditRejectedException("proposal_request_stale");
            if (utf8.Length is < 2 or > MaximumBytes) throw new ArgumentException("Proposal byte budget.");
            byte[] bytes = utf8.ToArray();
            _ = new UTF8Encoding(false, true).GetCharCount(bytes);
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            var root = json.RootElement; Unique(root);
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 3 ||
                !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out int v) || v != 1 ||
                !root.TryGetProperty("capability", out var capability) || capability.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Invalid proposal envelope.");
            // Closed supported set; every file domain retains its independent original grants.
            if (capability.GetString() is not ("ncma.scene.transaction" or "ncma.scene.delete_object" or
                "ncma.assets.metadata.edit" or "ncma.ui.document.commit" or "ncma.animgraph.transaction"))
                throw new EditRejectedException("proposal_domain_not_supported");
            var request = new CapabilityRequest(EditSession.ContractVersion, proposalId, edit.SessionId,
                expected.Revision, capability.GetString()!, input.Clone());
            if (edit.CachedRequestExists(proposalId)) throw new EditRejectedException("proposal_id_reused");
            if (request.Capability is not ("ncma.scene.transaction" or "ncma.scene.delete_object")) {
                var participant = edit.ReviewParticipantCommand(request);
                if (Source() != expected || requestStillCurrent?.Invoke(true) == false) throw new EditRejectedException("proposal_source_stale");
                return latest = new(this, time.GetTimestamp(), proposalId, expected, bytes, new([], [], [], [], false),
                    null, participant.CopyDetails(), request.Capability, null, participant, requestStillCurrent);
            }
            var result = edit.DescribeRequestImpact(request);
            var batch = edit.ReviewOriginalBatch("AI scene proposal", expected.Revision, [new(request.Capability, request.Input)]);
            if (!batch.PreservesProposedValues) throw new EditRejectedException("proposal_normalization_requires_new_draft");
            byte[] changes = edit.DescribeOriginalBatchChanges(batch);
            if (Source() != expected || requestStillCurrent?.Invoke(true) == false) throw new EditRejectedException("proposal_source_stale");
            return latest = new(this, time.GetTimestamp(), proposalId, expected, bytes, result.Impact, batch,
                changes, request.Capability, result.DestructiveTarget, requestStillCurrent: requestStillCurrent);
        }
        finally { busy = false; }
    }

    public bool IsCurrent(DomainProposalReview review)
    {
        Check(); ArgumentNullException.ThrowIfNull(review);
        if (!ReferenceEquals(review.Owner, this) || consumed.Contains(review.ProposalId)) return false;
        var elapsed = time.GetElapsedTime(review.Started, time.GetTimestamp());
        if (elapsed < TimeSpan.Zero || elapsed >= TimeSpan.FromSeconds(ReviewSeconds)) return false;
        busy = true;
        try { return review.Source == Source() && review.RequestStillCurrent?.Invoke(true) != false; }
        catch (Exception e) when (e is EditRejectedException or ArgumentException or InvalidOperationException) { return false; }
        finally { busy = false; }
    }

    private static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in value.EnumerateObject())
            {
                if (!names.Add(p.Name)) throw new ArgumentException("Duplicate proposal member.");
                Unique(p.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) Unique(child);
    }
}
