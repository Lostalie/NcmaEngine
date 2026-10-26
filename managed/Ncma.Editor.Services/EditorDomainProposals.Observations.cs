using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Editor.Core;

namespace Ncma.Editor.Services;

public sealed record DomainModuleTarget(Guid ModuleId, string ModuleVersion, Guid ConfigurationId,
    string Operation, Guid InputSchema, Guid OutputSchema);
public sealed record DomainReadRequest(string Capability, JsonElement Input);

// Optional trusted plugin composition. No transport implementation in the default product.
public interface IDomainModuleController
{
    string Status { get; }
    void Begin(ApprovedDomainRequest request);
    void Pump();
    void Cancel();
    bool TryClose();
}

// Local human review only. Possessing a review is not permission to send its bytes to a module.
public sealed class DomainObservationReview
{
    internal readonly EditorDomainProposals Owner;
    internal readonly long Started;
    internal readonly DomainReadRequest[] Reads;
    internal readonly UiDomainObservation? Ui;
    internal readonly MetadataDomainObservation? Metadata;
    private readonly byte[] bytes;
    private readonly byte[]? requestContent;
    internal readonly Func<bool>? RequestContentCurrent;
    internal readonly Func<bool>? RequestIdentityCurrent;
    internal DomainObservationReview(EditorDomainProposals owner, long started, DomainProposalSource source,
        DomainModuleTarget target, DomainReadRequest[] reads, byte[] bytes, byte[]? requestContent = null,
        Func<bool>? requestContentCurrent = null, UiDomainObservation? ui = null, Func<bool>? requestIdentityCurrent = null,
        MetadataDomainObservation? metadata = null)
    {
        Owner = owner; Started = started; Source = source; Target = target;
        Reads = reads; Ui = ui; Metadata = metadata; this.bytes = bytes; this.requestContent = requestContent;
        RequestContentCurrent = requestContentCurrent;
        RequestIdentityCurrent = requestIdentityCurrent ?? requestContentCurrent;
        Fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
            version = 2, RequestId, Source, Target, reads, sha256 = Convert.ToHexString(SHA256.HashData(bytes)) })));
    }
    public DomainProposalSource Source { get; }
    public Guid RequestId { get; } = Guid.NewGuid();
    public DomainModuleTarget Target { get; }
    public string Fingerprint { get; }
    public byte[] CopyBytes() => (byte[])bytes.Clone();
    internal byte[] WrapObservation(byte[] current) => EditorDomainProposals.WrapRequest(current, requestContent);
}

// Created only by explicit local approval. Exact recipient, bytes, source and request identity.
// Optional bridge claims it once; no plugin can construct an approved request itself.
public sealed class ApprovedDomainRequest
{
    internal readonly EditorDomainProposals Owner;
    internal readonly DomainObservationReview Review;
    internal bool Claimed, Completed;
    internal ApprovedDomainRequest(EditorDomainProposals owner, DomainObservationReview review)
    { Owner = owner; Review = review; }
    public Guid RequestId => Review.RequestId;
    public DomainProposalSource Source => Review.Source;
    public DomainModuleTarget Target => Review.Target;
}

public sealed partial class EditorDomainProposals
{
    // Internal typed reader callback: retain all source checks without reentering public Check.
    internal bool ObservationSourceCurrent(DomainProposalSource source)
    { edit.Document.VerifyAccess(); return !closed && source == Source(); }
    public const int MaximumObservationReads = 8;
    private DomainObservationReview? observation;
    private ApprovedDomainRequest? pending;
    private readonly Dictionary<DomainModuleTarget, IDomainModuleController> modules = [];
    private long? lastPump;
    private bool modulePumping;
    public void PumpModules()
    {
        edit.Document.VerifyAccess();
        if (closed || busy || modulePumping || modules.Count == 0) return;
        if (lastPump is long last && time.GetElapsedTime(last, time.GetTimestamp()) < TimeSpan.FromMilliseconds(100)) return;
        lastPump = time.GetTimestamp(); modulePumping = true;
        try {
            foreach (var controller in modules.Values) {
                try { controller.Pump(); }
                catch (Exception e) when (e is not OutOfMemoryException) {
                    try { controller.Cancel(); }
                    catch (Exception cleanup) when (cleanup is not OutOfMemoryException) {
                        Record(new(Guid.Empty, "", "error", "module_cleanup_required", false, edit.Revision, true));
                    }
                    Record(new(Guid.Empty, "", "error", "module_result_rejected", false, edit.Revision));
                }
            }
        }
        finally { modulePumping = false; }
    }
    internal bool TryCloseModules()
    {
        edit.Document.VerifyAccess();
        bool complete = true;
        foreach (var controller in modules.Values) complete &= controller.TryClose();
        return complete;
    }
    public DomainModuleTarget[] RegisteredModules { get { Check(); return modules.Keys.ToArray(); } }
    public DomainObservationReview? LatestObservation { get {
        Check(); busy = true;
        try { return observation is not null && Fresh(observation.Started) && observation.Source == Source() &&
            observation.RequestContentCurrent?.Invoke() != false && observation.Ui?.IsCurrent() != false && observation.Metadata?.IsCurrent() != false ? observation : null; }
        finally { busy = false; }
    } }
    public ApprovedDomainRequest? PendingRequest { get { Check(); return pending; } }
    public void RegisterModule(DomainModuleTarget target, IDomainModuleController controller)
    {
        Check(); TargetValid(target); ArgumentNullException.ThrowIfNull(controller);
        if (modulePumping || modules.Count >= 16 || pending is not null || !modules.TryAdd(target, controller))
            throw new EditRejectedException("module_registration_denied");
    }
    public IDomainModuleController Module(DomainModuleTarget target)
    { Check(); return modules.GetValueOrDefault(target) ?? throw new EditRejectedException("module_not_registered"); }
    // Trusted composition teardown only. Keep the controller registered until actual work drains;
    // never lose ownership of an ignored-cancellation worker, even after owner close.
    public bool TryUnregisterModule(DomainModuleTarget target, IDomainModuleController controller)
    {
        edit.Document.VerifyAccess(); ArgumentNullException.ThrowIfNull(controller);
        if (busy || modulePumping) throw new EditRejectedException("module_registration_denied");
        if (!modules.TryGetValue(target, out var registered) || !ReferenceEquals(registered, controller))
            throw new EditRejectedException("module_registration_owner_mismatch");
        modulePumping = true;
        try {
            controller.Cancel();
            if (!controller.TryClose()) return false;
            modules.Remove(target);
            if (observation?.Target == target) observation = null;
            if (pending?.Target == target) { pending.Completed = true; pending = null; }
            return true;
        }
        finally { modulePumping = false; }
    }
    private static readonly HashSet<string> ObservationCapabilities = new(StringComparer.Ordinal) {
        "ncma.scene.object.inspect", "ncma.assets.inspect", "ncma.assets.validate",
        "ncma.animgraph.inspect", "ncma.animgraph.validate", "ncma.animgraph.bones" };

    private bool Fresh(long started) => time.GetElapsedTime(started, time.GetTimestamp()) is var elapsed &&
        elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromSeconds(ReviewSeconds);
    private static void TargetValid(DomainModuleTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        static bool Token(string s, int max) => s is { Length: > 0 } && s.Length <= max &&
            s.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
        if (target.ModuleId == Guid.Empty || target.ConfigurationId == Guid.Empty || target.InputSchema == Guid.Empty ||
            target.OutputSchema == Guid.Empty || !Token(target.ModuleVersion, 32) || !Token(target.Operation, 96))
            throw new ArgumentException("Exact registered module target required.");
    }
    private byte[] ReadObservation(DomainProposalSource source, DomainReadRequest[] requests, UiDomainObservation? ui = null,
        MetadataDomainObservation? metadata = null)
    {
        var values = new List<object>();
        if (metadata is not null) {
            if (metadata.Source != source || requests.Length != 0 || ui is not null) throw new EditRejectedException("metadata_observation_source_mismatch");
            values.Add(new { capability = "local.asset.metadata", input = new { assetId = metadata.AssetId }, data = metadata.Read() });
        }
        if (ui is not null) {
            if (ui.Source != source || requests.Length != 0) throw new EditRejectedException("ui_observation_source_mismatch");
            values.Add(new { capability = "local.ui.document", input = new { documentId = ui.Stamp.Document }, data = ui.Read() });
        }
        // Reuses original read capability validators and independent asset/graph read grants.
        // No wildcard tool dispatch, runtime state, private reflection or filesystem reader.
        foreach (var read in requests) {
            if (!ObservationCapabilities.Contains(read.Capability) || read.Input.ValueKind != JsonValueKind.Object)
                throw new EditRejectedException("observation_capability_denied");
            Unique(read.Input);
            if (read.Capability == "ncma.scene.object.inspect" &&
                (!read.Input.TryGetProperty("objectId", out var id) || !id.TryGetGuid(out var uuid) || uuid == Guid.Empty))
                throw new EditRejectedException("observation_exact_object_required");
            var response = edit.Invoke(new(EditSession.ContractVersion, Guid.NewGuid(), source.EditSessionId,
                source.Revision, read.Capability, read.Input), CapabilityPermissions.ReadOnly);
            if (response.Status != "ok") throw new EditRejectedException(response.Code);
            values.Add(new { capability = read.Capability, input = read.Input, data = response.Data });
        }
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { version = 1, source, observations = values });
        if (bytes.Length > MaximumBytes) throw new EditRejectedException("observation_byte_budget");
        if (Source() != source) throw new EditRejectedException("proposal_source_stale");
        return bytes;
    }
    public DomainObservationReview ReviewObservation(DomainModuleTarget target, IReadOnlyList<DomainReadRequest> reads)
        => ReviewObservationCore(target, reads, null, null);
    // Trusted local composition only; content is shown/hashed together with the actual authorized reads.
    // This adds no remote capability, approval or arbitrary reader.
    public DomainObservationReview ReviewObservationRequest(DomainModuleTarget target,
        IReadOnlyList<DomainReadRequest> reads, ReadOnlySpan<byte> requestContent, Func<bool>? requestContentCurrent = null,
        Func<bool>? requestIdentityCurrent = null)
        => ReviewRequestContent(target, reads, requestContent, requestContentCurrent, null, requestIdentityCurrent);
    // Typed local UI selection, never dispatchable by remote capability name. Human approval
    // covers the complete bytes/recipient just like all other observations.
    public DomainObservationReview ReviewUiObservationRequest(DomainModuleTarget target,
        UiDomainObservation ui, ReadOnlySpan<byte> requestContent, Func<bool>? requestContentCurrent = null)
    {
        ArgumentNullException.ThrowIfNull(ui);
        return ReviewRequestContent(target, [], requestContent, requestContentCurrent, ui);
    }
    public DomainObservationReview ReviewMetadataObservationRequest(DomainModuleTarget target,
        MetadataDomainObservation metadata, ReadOnlySpan<byte> requestContent, Func<bool>? requestContentCurrent = null,
        Func<bool>? requestIdentityCurrent = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return ReviewRequestContent(target, [], requestContent, requestContentCurrent, null, requestIdentityCurrent, metadata);
    }
    private DomainObservationReview ReviewRequestContent(DomainModuleTarget target,
        IReadOnlyList<DomainReadRequest> reads, ReadOnlySpan<byte> requestContent, Func<bool>? requestContentCurrent, UiDomainObservation? ui,
        Func<bool>? requestIdentityCurrent = null, MetadataDomainObservation? metadata = null)
    {
        Check();
        if (requestContent.Length is < 2 or > MaximumBytes) throw new ArgumentException("request_content_budget");
        byte[] owned = requestContent.ToArray();
        _ = new System.Text.UTF8Encoding(false, true).GetCharCount(owned);
        using var json = JsonDocument.Parse(owned, new JsonDocumentOptions { MaxDepth = 12 });
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("request_content_object_required");
        Unique(json.RootElement);
        return ReviewObservationCore(target, reads, owned, requestContentCurrent, ui, requestIdentityCurrent, metadata);
    }
    internal static byte[] WrapRequest(byte[] observationBytes, byte[]? content)
    {
        if (content is null) return observationBytes;
        using var observationJson = JsonDocument.Parse(observationBytes);
        using var contentJson = JsonDocument.Parse(content);
        byte[] result = JsonSerializer.SerializeToUtf8Bytes(new { version = 2,
            request = contentJson.RootElement, observation = observationJson.RootElement });
        if (result.Length > MaximumBytes) throw new EditRejectedException("observation_byte_budget");
        using var depthCheck = JsonDocument.Parse(result, new JsonDocumentOptions { MaxDepth = 16 });
        return result;
    }
    private DomainObservationReview ReviewObservationCore(DomainModuleTarget target,
        IReadOnlyList<DomainReadRequest> reads, byte[]? content, Func<bool>? contentCurrent, UiDomainObservation? ui = null,
        Func<bool>? identityCurrent = null, MetadataDomainObservation? metadata = null)
    {
        Check(); TargetValid(target); ArgumentNullException.ThrowIfNull(reads);
        if (!modules.ContainsKey(target)) throw new EditRejectedException("module_not_registered");
        if (ui is not null && metadata is not null || (ui is null && metadata is null ? reads.Count is < 1 or > MaximumObservationReads : reads.Count != 0))
            throw new ArgumentException("Observation request budget.");
        // Copy by bounded index, not unbounded caller enumeration, before domain callbacks.
        var owned = new DomainReadRequest[reads.Count];
        for (int i = 0; i < owned.Length; i++) {
            var item = reads[i] ?? throw new ArgumentException("Missing observation request.");
            owned[i] = new(item.Capability, item.Input.Clone());
        }
        if (JsonSerializer.SerializeToUtf8Bytes(owned).Length > MaximumBytes) throw new ArgumentException("Observation input budget.");
        busy = true;
        try {
            if (contentCurrent?.Invoke() == false) throw new EditRejectedException("request_content_stale");
            var source = Source(); byte[] bytes = WrapRequest(ReadObservation(source, owned, ui, metadata), content);
            if (contentCurrent?.Invoke() == false || source != Source()) throw new EditRejectedException("request_content_stale");
            // Preparing another review revokes any previous unclaimed/in-flight export.
            pending = null;
            return observation = new(this, time.GetTimestamp(), source, target, owned, bytes, content, contentCurrent, ui, identityCurrent, metadata);
        }
        finally { busy = false; }
    }
    private void RequireObservationIdentity(DomainObservationReview review, bool fullContentGuard = true)
    {
        // Full content/asset-clock guards run before mutation. During original Publish, the
        // participant advances its own clock; retain identity/revoke/config guards instead.
        var guard = fullContentGuard ? review.RequestContentCurrent : review.RequestIdentityCurrent;
        if (!ReferenceEquals(review.Owner, this) || !ReferenceEquals(observation, review) ||
            !Fresh(review.Started) || review.Source != Source() || guard?.Invoke() == false || review.Ui?.IdentityCurrent() == false || review.Metadata?.IdentityCurrent() == false)
            throw new EditRejectedException("observation_stale");
    }
    private void RequireObservation(DomainObservationReview review)
    {
        RequireObservationIdentity(review);
        byte[] current = review.WrapObservation(ReadObservation(review.Source, review.Reads, review.Ui, review.Metadata));
        if (!current.AsSpan().SequenceEqual(review.CopyBytes()) || review.RequestContentCurrent?.Invoke() == false ||
            review.Source != Source()) throw new EditRejectedException("observation_changed");
    }
    public ApprovedDomainRequest ApproveObservation(DomainObservationReview review, string fingerprint, bool humanReviewed)
    {
        Check(); ArgumentNullException.ThrowIfNull(review);
        if (!humanReviewed || fingerprint != review.Fingerprint || pending is not null)
            throw new EditRejectedException("observation_confirmation_required");
        busy = true;
        try { RequireObservation(review); return pending = new(this, review); }
        finally { busy = false; }
    }
    public byte[] ClaimObservation(ApprovedDomainRequest request, DomainModuleTarget target)
    {
        Check(); ArgumentNullException.ThrowIfNull(request);
        if (!ReferenceEquals(pending, request) || !ReferenceEquals(request.Owner, this) || request.Claimed ||
            request.Completed || request.Target != target) throw new EditRejectedException("observation_claim_denied");
        busy = true;
        // Consume even if revalidation fails. There is no silent re-export/retry.
        request.Claimed = true;
        try { RequireObservation(request.Review); return request.Review.CopyBytes(); }
        catch { request.Completed = true; pending = null; throw; }
        finally { busy = false; }
    }
    public bool RequestCurrent(ApprovedDomainRequest request)
        => RequestCurrentCore(request, contents: true);
    // Passive polling only: owner/source/config/revocation/expiry guards, no complete read or
    // file IO. Claim and ReceiveModuleResult still require full exact-content revalidation.
    public bool RequestIdentityCurrent(ApprovedDomainRequest request)
        => RequestCurrentCore(request, contents: false);
    private bool RequestCurrentCore(ApprovedDomainRequest request, bool contents)
    {
        Check(); ArgumentNullException.ThrowIfNull(request);
        if (!ReferenceEquals(pending, request) || request.Completed || !request.Claimed) return false;
        busy = true;
        try {
            if (contents) RequireObservation(request.Review);
            else {
                RequireObservationIdentity(request.Review); // full configuration/content VERSION guard, no read callback
                if (request.Review.Ui?.IsCurrent() == false) throw new EditRejectedException("ui_observation_stale");
                if (request.Review.Metadata?.IsCurrent() == false) throw new EditRejectedException("metadata_observation_stale");
            }
            return true;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or EditRejectedException) {
            request.Completed = true; pending = null; return false;
        }
        finally { busy = false; }
    }
    public void CancelRequest(ApprovedDomainRequest request)
    {
        Check(); ArgumentNullException.ThrowIfNull(request);
        if (!ReferenceEquals(request.Owner, this)) throw new EditRejectedException("proposal_foreign_request");
        request.Completed = true;
        if (ReferenceEquals(pending, request)) pending = null;
    }
    public DomainProposalReview ReceiveModuleResult(ApprovedDomainRequest request, ReadOnlySpan<byte> proposal)
    {
        if (!RequestCurrent(request)) throw new EditRejectedException("proposal_source_stale");
        request.Completed = true; pending = null;
        // Proposal JSON cannot choose source, request identity, recipient, grants or execution mode.
        // Retain original export/configuration/read-scope validity through the SECOND human
        // approval and original Commit, not merely through receipt of the network response.
        return ReviewCore(request.RequestId, request.Source, proposal, validateReadContents => {
            if (validateReadContents) RequireObservation(request.Review);
            else RequireObservationIdentity(request.Review, fullContentGuard: false);
            return true;
        });
    }
}
