using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Editor.Protocol;
namespace Ncma.Editor.Transport;

public sealed record ProposalView(Guid Id, Guid ConnectionId, Guid RequestId, string Capability, string Risk,
    ulong Revision, Guid[] Objects, Guid[] CreatedObjects, string[] ComponentTypes, Guid[] Bindings, Guid? DeleteTarget);
public sealed record GrantView(Guid ConnectionId, Guid RequestId, long RemainingSeconds, bool AllowHistory);
public sealed record AuditView(Guid ConnectionId, Guid RequestId, string Capability, string Code, bool Changed, bool Replayed, double DurationMs);
public sealed partial class EditorEndpoint
{
    private sealed record Proposal(Guid Id, Connection Connection, CapabilityRequest Request, string Fingerprint,
        CommandImpact Impact, string OriginalCapability, Guid? Target, ulong Generation);
    private sealed record Grant(Proposal Proposal, long Deadline, ulong Epoch, bool AllowHistory);
    private sealed record Route(Guid ConnectionId, string Fingerprint);
    private readonly List<Proposal> _proposals = [];
    private readonly Dictionary<Guid, Grant> _grants = [];
    // A bounded ownership ledger plus conservative tombstone filter. Filter hits fail closed, never auto-reexecute.
    private readonly Dictionary<Guid, Route> _routes = [];
    private readonly Queue<Guid> _routeOrder = [];
    private readonly byte[] _retiredIds = new byte[65536];
    private readonly Queue<AuditView> _audit = [];
    private ulong _grantEpoch;
    private bool _wasFrozen;
    private string AuditPath => Path.Combine(Path.GetDirectoryName(DescriptorPath)!, "audit-" + _descriptor.InstanceId.ToString("N") + ".jsonl");

    public ProposalView[] Proposals { get { Verify(); lock (_gate) return _proposals.Select(p => new ProposalView(p.Id, p.Connection.Id,
        p.Request.RequestId, p.Request.Capability, p.Target.HasValue ? "Destructive" : "Reversible", p.Request.ExpectedRevision!.Value,
        (Guid[])p.Impact.Objects.Clone(), (Guid[])p.Impact.CreatedObjects.Clone(), (string[])p.Impact.ComponentTypes.Clone(),
        (Guid[])p.Impact.Bindings.Clone(), p.Target)).ToArray(); } }
    public string ProposalInput(Guid id) { Verify(); lock (_gate) return _proposals.Single(p => p.Id == id).Request.Input.GetRawText(); }
    public GrantView[] Grants { get { Verify(); lock (_gate) return _grants.Values.Select(g => new GrantView(g.Proposal.Connection.Id,
        g.Proposal.Request.RequestId, Math.Max(0, (g.Deadline - Stopwatch.GetTimestamp()) / Stopwatch.Frequency), g.AllowHistory)).ToArray(); } }
    public AuditView[] Audit { get { Verify(); lock (_gate) return _audit.ToArray(); } }

    // Trusted owner-thread UI only; no IPC or MCP method can call this.
    public void Approve(Guid proposalId, bool allowHistory, Guid? confirmedDeleteTarget = null)
    {
        Verify(); Synchronize();
        lock (_gate)
        {
            var p = _proposals.Single(p => p.Id == proposalId);
            if (p.Generation != _generation || p.Request.ExpectedRevision != _edit.Revision || !p.Connection.Paired ||
                !p.Connection.Connected || _edit.State.Frozen || _edit.State.EditBusy) throw new InvalidOperationException("stale_proposal");
            if (p.Target is Guid target && confirmedDeleteTarget != target) throw new InvalidOperationException("delete_confirmation_required");
            // Repeat trusted pure validators before granting, not just when first proposed.
            _ = _edit.DescribeRequestImpact(p.Request);
            if (!_grants.ContainsKey(p.Connection.Id) && _grants.Count >= MaxConnections) throw new InvalidOperationException("grant_capacity");
            _grants[p.Connection.Id] = new(p, Stopwatch.GetTimestamp() + Stopwatch.Frequency * 60, _grantEpoch, allowHistory);
            _proposals.Remove(p);
        }
    }
    public void RevokeGrants(Guid connectionId)
    { Verify(); lock (_gate) { _grants.Remove(connectionId); _proposals.RemoveAll(p => p.Connection.Id == connectionId); } }

    private void Synchronize()
    {
        var state = _edit.State;
        lock (_gate)
        {
            bool replaced = _generation != _edit.DocumentGeneration;
            if (replaced || _catalogGeneration != _edit.BehaviourCatalogGeneration || state.Frozen && !_wasFrozen || state.HistoryInvalidated)
            { _grantEpoch = checked(_grantEpoch + 1); _grants.Clear(); _proposals.Clear(); }
            _generation = _edit.DocumentGeneration; _wasFrozen = state.Frozen; _catalogGeneration = _edit.BehaviourCatalogGeneration;
            foreach (var id in _grants.Where(g => g.Value.Deadline <= Stopwatch.GetTimestamp()).Select(g => g.Key).ToArray()) _grants.Remove(id);
            _proposals.RemoveAll(p => p.Generation != _generation || p.Request.ExpectedRevision != state.Revision || !p.Connection.Paired);
        }
    }
    private CapabilityPermissions Permissions(Connection c, CapabilityRequest request, string fingerprint)
    {
        if (!_grants.TryGetValue(c.Id, out var grant)) return CapabilityPermissions.ReadOnly;
        bool history = request.Capability is "ncma.history.undo" or "ncma.history.redo";
        if (!(grant.Proposal.Fingerprint == fingerprint && grant.Proposal.Request.RequestId == request.RequestId) && !(history && grant.AllowHistory))
            return CapabilityPermissions.ReadOnly;
        var p = grant.Proposal;
        string[] caps = grant.AllowHistory ? [p.OriginalCapability, "ncma.history.undo", "ncma.history.redo"] : [p.OriginalCapability];
        return new(caps, p.Target is Guid target ? [target] : [], p.Impact.Objects, p.Impact.CreatedObjects,
            p.Impact.ComponentTypes, p.Impact.Bindings, allowDocumentHistory: false, requireTrustedBindings: true,
            isCurrent: () => c.Paired && c.Connected && !_disposed && ! _edit.State.Frozen &&
                _generation == _edit.DocumentGeneration && _catalogGeneration == _edit.BehaviourCatalogGeneration && grant.Epoch == _grantEpoch && grant.Deadline > Stopwatch.GetTimestamp() &&
                _grants.GetValueOrDefault(c.Id) == grant);
    }
    private static string Fingerprint(CapabilityRequest r) => Convert.ToHexString(SHA256.HashData(Wire.Encode(r)));
    private void Propose(Connection c, CapabilityRequest r, string fingerprint)
    {
        if (_proposals.Any(p => p.Connection.Id == c.Id && p.Request.RequestId == r.RequestId) || _proposals.Count >= 16) return;
        try { var info = _edit.DescribeRequestImpact(r);
            _proposals.Add(new(Guid.NewGuid(), c, r, fingerprint, info.Impact, info.OriginalCapability, info.DestructiveTarget, _generation)); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or JsonException or KeyNotFoundException or OverflowException) { }
    }
    private IpcResponse Invoke(Connection c, JsonElement envelope)
    {
        CapabilityRequest request;
        try { request = Wire.Decode<CapabilityRequest>(Wire.Utf8.GetBytes(envelope.GetRawText())); }
        catch (Exception e) when (e is ArgumentException or JsonException) { return new("invalid_envelope"); }
        if (Wire.Utf8.GetByteCount(envelope.GetRawText()) > EditSession.MaxInputBytes || request.Capability is null ||
            request.Capability.Length > 128 || request.RequestId == Guid.Empty) return new("invalid_envelope");
        string fingerprint = Fingerprint(request);
        bool mutation = _edit.Describe().Any(d => d.Name == request.Capability && d.Risk != MutationRisk.ReadOnly);
        if (mutation)
        {
            if (_routes.TryGetValue(request.RequestId, out var route))
            {
                if (route.ConnectionId != c.Id) return new("request_owner_mismatch");
                if (route.Fingerprint != fingerprint) return new("request_id_reused");
                if (!_edit.CachedRequestExists(request.RequestId)) return new("outcome_unknown");
            }
            else if (_edit.CachedRequestExists(request.RequestId) || Retired(request.RequestId)) return new("outcome_unknown");
        }
        var permissions = mutation ? Permissions(c, request, fingerprint) : CapabilityPermissions.ReadOnly;
        var watch = Stopwatch.StartNew();
        var result = _edit.Invoke(request, permissions);
        if (mutation && result.Status == "ok" && !result.Replayed)
        {
            _routes[request.RequestId] = new(c.Id, fingerprint); _routeOrder.Enqueue(request.RequestId);
            while (_routeOrder.Count > 256) { var id = _routeOrder.Dequeue(); _routes.Remove(id); Retire(id); }
        }
        if (mutation && result.Code == "permission_denied") Propose(c, request, fingerprint);
        Record(c, request, result, watch.Elapsed.TotalMilliseconds, permissions != CapabilityPermissions.ReadOnly);
        var json = JsonSerializer.SerializeToElement(result, Wire.Json);
        if (Wire.Utf8.GetByteCount(json.GetRawText()) > 256 * 1024) return new("item_too_large");
        return new("ok", json);
    }
    private int TombstoneBit(Guid id)
    {
        var hash = SHA256.HashData(id.ToByteArray());
        return (int)(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(hash) % (uint)(_retiredIds.Length * 8));
    }
    private void Retire(Guid id) { int bit = TombstoneBit(id); _retiredIds[bit / 8] |= (byte)(1 << (bit % 8)); }
    private bool Retired(Guid id) { int bit = TombstoneBit(id); return (_retiredIds[bit / 8] & (1 << (bit % 8))) != 0; }
    private void Record(Connection c, CapabilityRequest r, CapabilityResult result, double ms, bool approved)
    {
        var item = new AuditView(c.Id, r.RequestId, r.Capability, result.Code, result.Changed, result.Replayed, ms);
        _audit.Enqueue(item); if (_audit.Count > 32) _audit.Dequeue();
        // Diagnostics are not a commit precondition. No secrets, raw payloads or project paths.
        try
        {
            for (int logIndex = 0; logIndex < 4; logIndex++)
            { string log = AuditPath + (logIndex == 0 ? "" : "." + logIndex); if (File.Exists(log) && (File.GetAttributes(log) & FileAttributes.ReparsePoint) != 0) return; }
            if (File.Exists(AuditPath) && new FileInfo(AuditPath).Length >= 1024 * 1024)
            {
                for (int i = 3; i >= 1; i--) { string from = AuditPath + (i == 1 ? "" : "." + (i - 1));
                    string to = AuditPath + "." + i; if (File.Exists(from)) File.Move(from, to, true); }
            }
            File.AppendAllText(AuditPath, JsonSerializer.Serialize(new { time = DateTimeOffset.UtcNow, instance = _descriptor.InstanceId,
                connection = c.Id, session = _sessionId, generation = _generation, requestId = r.RequestId,
                capability = r.Capability, risk = _edit.Describe().SingleOrDefault(d => d.Name == r.Capability)?.Risk.ToString() ?? "Unknown",
                approved, approvingIdentity = approved ? "trusted_editor_ui" : null, scopeFingerprint = approved ? _grants.GetValueOrDefault(c.Id)?.Proposal.Fingerprint : null, expectedRevision = r.ExpectedRevision,
                executionRevision = result.ExecutionRevision, revision = result.Revision, result.Code, result.Changed, result.Replayed, durationMs = ms }, Wire.Json) + "\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
