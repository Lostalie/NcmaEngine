using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Animation;
using Ncma.Assets;
using Ncma.Assets.Authoring;
using Ncma.Assets.Runtime;
using Ncma.Editor.Core;
using Ncma.Editor.Transport;

namespace Ncma.Editor.Services;

public readonly record struct AnimationGraphEditorStamp(EditorViewStamp Edit, Guid Graph, ulong Content, ulong Assets);
public sealed record AnimationGraphWriteReview(Guid Proposal, AnimationGraphEditorStamp Stamp, string Path, Guid Graph,
    string? BeforeHash, string AfterHash, Guid[] Dependencies, string[] ResourceHashes, Guid Endpoint, ulong AudienceEpoch,
    Guid[] Audience, bool Agent, ulong ReadPermission, string Fingerprint);

// Authoring only, same complete-document history. Not a live Animator or Agent approval tool.
public sealed class EditorAnimationGraphWorkspace : IDisposable
{
    private sealed record Candidate(AnimationGraphDefinition Graph, byte[]? Before, AnimationGraphEditorStamp Stamp, bool Agent, Guid Endpoint, ulong AudienceEpoch, Guid[] Audience, ulong ReadPermission);
    private sealed record Approval(AnimationGraphWriteReview Review, long Start);
    private readonly Dictionary<string, RuntimeAssetSnapshot> _pins = [];
    private readonly EditorWorkspace _workspace; private readonly string _root; private readonly Guid _project, _session;
    private readonly ulong _generation; private readonly TimeProvider _time;
    private readonly Dictionary<Guid, Candidate> _candidates = []; private readonly List<Approval> _approvals = [];
    private readonly HashSet<Guid> _retired = [];
    private AnimationGraphDefinition? _source, _draft, _baseline; private byte[]? _raw; private string? _path;
    private EditInteractionLease? _interaction; private bool _fileWrite, _disposed; private ulong _content = 1, _knownAssets;
    public AnimationGraphInspections Reads { get; }
    public AnimationGraphSequences Sequences {get;}
    public AnimationGraphSkeletons Skeletons {get;}
    public AnimationGraphWriteScope Scope { get; }
    private EditSession Edit => _workspace.Owner.Edit!;
    private AssetProjectAuthoring Assets => _workspace.Owner.Assets ?? throw new EditRejectedException("graph_project_missing");
    public AnimationGraphEditorStamp Stamp { get { Verify(); return new(_workspace.Stamp, _source?.AssetId ?? Guid.Empty, _content, Assets.Clock.Revision); } }
    public bool HasDraft => _interaction is not null;
    public string? Path => _path;
    public bool Writable => Current() && _fileWrite && _source is not null && !Edit.State.Frozen && !Edit.State.HistoryInvalidated;
    private AnimationGraphWriteReview? _review;
    public AnimationGraphWriteReview? Review { get => _review is null ? null : CloneReview(_review); private set => _review = value is null ? null : CloneReview(value); }
    public EditorAnimationGraphWorkspace(EditorWorkspace workspace, string root, Guid project, TimeProvider? time = null)
    {
        _workspace = workspace; _root = new AssetProjectPaths(root).Root; _project = project; _time = time ?? TimeProvider.System;
        if (project == Guid.Empty) throw new ArgumentException("Graph authoring project UUID.");
        _session = workspace.Stamp.SessionId; _generation = workspace.Stamp.Generation;
        Scope = new(Approved, id => { try { _=Required(id);return true; } catch(Exception e) when(e is InvalidOperationException or ArgumentException){return false;} }, ValidateResources);
        Reads = new(workspace, root, _time);
        Sequences=new(workspace,root,project,Reads,()=>Stamp,()=>HasDraft?null:Capture(),_time);
        Skeletons=new(workspace,root,project,Reads,()=>Stamp,()=>Capture(),()=>HasDraft,_time);
        Edit.RegisterInspection(AnimationGraphAuthoringSchemas.Descriptor, Propose);
    }
    private bool Current() => !_disposed && _workspace.Stamp.SessionId == _session && _workspace.Stamp.Generation == _generation;
    private void Verify() { _ = Edit.State; ObjectDisposedException.ThrowIf(_disposed, this); if (!Current()) throw new EditRejectedException("graph_workspace_stale"); }
    private void Check(AnimationGraphEditorStamp stamp) { Verify(); if (stamp != Stamp || Edit.State.Frozen || Edit.State.HistoryInvalidated) throw new EditRejectedException("graph_view_stale_or_frozen"); }
    public AnimationGraphDefinition? Capture() { Verify(); return _source is null ? null : AnimationGraphEdits.CopyDraft(_draft ?? _source); }
    public void Open(string relative)
    {
        Verify(); Cancel(); ClearProposals(); Revoke(); Reads.OpenTrustedRelative(relative);
        _path = AnimationGraphCommands.ValidatePath(relative); _raw = Assets.Graphs.Read(_path); _source = AnimationGraphCodec.Decode(_raw);
        _fileWrite = false; _knownAssets = Assets.Clock.Revision; _content = checked(_content + 1);
    }
    public void New(string relative, AnimationGraphDefinition definition)
    {
        Verify(); if(Edit.State.Frozen||Edit.State.EditBusy)throw new EditRejectedException("graph_open_busy");
        relative=AnimationGraphCommands.ValidatePath(relative); var d=AnimationGraphCodec.Decode(AnimationGraphCodec.Encode(definition));
        Assets.Graphs.RequireMissing(relative); Cancel(); Revoke(); _path=relative;_raw=null;_source=d;_knownAssets=Assets.Clock.Revision;_content=checked(_content+1);
        Reads.PublishTrustedDefinition(relative,d); // still no read/write grant
    }
    // Local exact file approval is separate from read UUID scope and from each Agent proposal.
    public void ApproveFileWrite(AnimationGraphEditorStamp stamp, string displayedPath, Guid graph, bool reviewed)
    {
        Check(stamp); if (!reviewed || _path != displayedPath || _source?.AssetId != graph || HasDraft) throw new EditRejectedException("graph_file_review_stale");
        RequireUnchangedFile(); _fileWrite = true;
    }
    public void Begin(AnimationGraphEditorStamp stamp)
    {
        Check(stamp); if (!Writable || HasDraft) throw new EditRejectedException("graph_draft_readonly_or_busy"); RequireUnchangedFile(); ClearProposals();
        _baseline = AnimationGraphEdits.CopyDraft(_source!); _draft = AnimationGraphEdits.CopyDraft(_source!);
        _interaction = Edit.BeginHostInteraction(AnimationGraphCommands.CapabilityName);
    }
    public void Update(JsonElement operations)
    {
        Verify(); if (_interaction?.IsActive != true || Edit.State.Frozen) throw new EditRejectedException("graph_draft_stale");
        _draft = AnimationGraphEdits.Apply(_baseline!, operations, requireComplete: false); _content = checked(_content + 1); ClearProposals();
    }
    public void ApplyDraft(JsonElement operations)
    {
        Verify(); if (_interaction?.IsActive != true || Edit.State.Frozen) throw new EditRejectedException("graph_draft_stale");
        _draft = AnimationGraphEdits.Apply(_draft!, operations, requireComplete: false); _content = checked(_content + 1); ClearProposals();
    }
    public Guid PrepareLocal()
    {
        Verify(); if (_draft is null || _interaction?.IsActive != true) throw new EditRejectedException("graph_draft_missing");
        Guid id = Guid.NewGuid(); return PrepareCandidate(id, _draft, agent: false);
    }
    private static Guid[] Audience(EditorEndpoint endpoint) => endpoint.View.Connections.Where(c => c.Paired && c.Connected).Select(c => c.ConnectionId).Order().ToArray();
    private Guid PrepareCandidate(Guid id, AnimationGraphDefinition definition, bool agent)
    {
        if (_candidates.Count >= 4 || _candidates.ContainsKey(id) || _retired.Contains(id) || _retired.Count >= 256) throw new EditRejectedException("graph_proposal_capacity_or_reused");
        var canonical = AnimationGraphCodec.Decode(AnimationGraphCodec.Encode(definition));
        if (canonical.AssetId != _source!.AssetId) throw new EditRejectedException("graph_identity_changed");
        var endpoint = agent ? _workspace.Owner.Endpoint ?? throw new EditRejectedException("graph_endpoint_missing") : null;
        var c = new Candidate(canonical, _raw?.ToArray(), Stamp, agent, endpoint?.View.InstanceId ?? Guid.Empty, endpoint?.AudienceRevision ?? 0, endpoint is null ? [] : Audience(endpoint),Reads.PermissionRevision);
        Assets.Graphs.PrepareProposal(id, _path!, c.Before, c.Graph, Assets.Clock.Revision); _candidates.Add(id, c); return id;
    }
    private object Propose(JsonElement input)
    {
        Verify(); AnimationGraphEdits.Closed(input, "graphId", "proposalId", "operations");
        Guid graph = AnimationGraphEdits.Uuid(input, "graphId"), id = AnimationGraphEdits.Uuid(input, "proposalId");
        if (_source is null || graph != _source.AssetId || HasDraft || Edit.State.Frozen || Reads.Grant.GraphId != graph || Reads.LocalSummary.Hash != Hash(AnimationGraphCodec.Encode(_source))) throw new EditRejectedException("graph_not_visible");
        var candidate = AnimationGraphEdits.Apply(_source, input.GetProperty("operations"));
        if(_candidates.TryGetValue(id,out var previous)){_ = Required(id);if(!previous.Agent||!AnimationGraphCodec.Encode(previous.Graph).AsSpan().SequenceEqual(AnimationGraphCodec.Encode(candidate)))throw new EditRejectedException("graph_proposal_id_reused");}
        else PrepareCandidate(id, candidate, agent: true);
        var before = Rows(_source); var after = Rows(candidate);
        return new { proposalId = id, graphId = graph, beforeHash = _raw is null ? null : Hash(_raw), afterHash = Hash(AnimationGraphCodec.Encode(candidate)),
            added = after.Keys.Except(before.Keys).Count(), removed = before.Keys.Except(after.Keys).Count(), updated = before.Keys.Intersect(after.Keys).Count(k => before[k] != after[k]), resourcesPrepared = false };
    }
    private static Dictionary<Guid, string> Rows(AnimationGraphDefinition graph) {
        var result = new Dictionary<Guid, string> { [graph.AssetId] = JsonSerializer.Serialize(new { graph.Name, graph.EntryState,graph.InterruptTransitions }) };
        if(graph.Montage is{} montage){result.Add(montage.AssetId,JsonSerializer.Serialize(new{montage.Version,montage.Name,montage.SkeletonId}));foreach(var slot in montage.Slots)result.Add(slot.Id,JsonSerializer.Serialize(slot));foreach(var section in montage.Sections)result.Add(section.Id,JsonSerializer.Serialize(section));}
        foreach (var n in graph.Nodes) result.Add(n.Id, JsonSerializer.Serialize(n)); foreach (var l in graph.Links) result.Add(l.Id, JsonSerializer.Serialize(l));
        foreach (var p in graph.Parameters) result.Add(p.Id, JsonSerializer.Serialize(p)); foreach (var s in graph.States) result.Add(s.Id, JsonSerializer.Serialize(s)); foreach (var t in graph.Transitions) result.Add(t.Id, JsonSerializer.Serialize(t));foreach(var e in graph.Events)result.Add(e.Id,JsonSerializer.Serialize(e));return result;
    }
    public Guid[] Pending { get { Verify(); return _candidates.Keys.Order().ToArray(); } }
    public AnimationGraphDefinition CandidateCopy(Guid proposal) { Verify(); return AnimationGraphCodec.Decode(AnimationGraphCodec.Encode(Required(proposal).Graph)); }
    private Candidate Required(Guid id) {
        if (!_candidates.TryGetValue(id, out var c) || c.Stamp != Stamp || Edit.State.Frozen || Edit.State.HistoryInvalidated || c.Agent && (!AudienceCurrent(c.Endpoint, c.AudienceEpoch, c.Audience)||c.ReadPermission!=Reads.PermissionRevision||!Reads.RetainsGrant())) throw new EditRejectedException("graph_proposal_stale"); return c;
    }
    public AnimationGraphWriteReview CaptureReview(Guid proposal)
    {
        Verify(); var c = Required(proposal); RequireUnchangedFile();
        var dependencies = AnimationGraphValidation.Dependencies(c.Graph).ToArray();
        using var preparation = RuntimeAssetLoader.Prepare(_root, _project, dependencies.Select(d => new AssetRef(new(d.Id), d.IsSkeleton ? AssetKind.Skeleton : AssetKind.Clip)));
        using var lease = preparation.AcquireLease(); var skeleton = (RuntimeDataAsset)lease.Require(c.Graph.SkeletonId, AssetKind.Skeleton);
        var clips = dependencies.Where(d => !d.IsSkeleton).Select(d => (RuntimeDataAsset)lease.Require(d.Id, AssetKind.Clip)).ToArray();
        if (clips.Any(clip => clip.ModelId != skeleton.ModelId || clip.SkeletonId != skeleton.Id || clip.Generation != skeleton.Generation)) throw new EditRejectedException("graph_resources_mismatch");
        using var preview=lease.CreateGraphPreview(c.Graph); // actual rig/clip bone counts, manifest membership and numerical scratch budget
        var closure=lease.List();
        var review = new AnimationGraphWriteReview(proposal, c.Stamp, _path!, c.Graph.AssetId, c.Before is null ? null : Hash(c.Before), Hash(AnimationGraphCodec.Encode(c.Graph)),
            closure.Select(r => r.Id).ToArray(), closure.Select(ResourceProof).ToArray(),
            c.Endpoint, c.AudienceEpoch, c.Audience.ToArray(), c.Agent, c.ReadPermission, "");
        Review = review with { Fingerprint = Hash(JsonSerializer.SerializeToUtf8Bytes(review)) }; return CloneReview(Review);
    }
    private static AnimationGraphWriteReview CloneReview(AnimationGraphWriteReview r) => r with { Dependencies = r.Dependencies.ToArray(), ResourceHashes = r.ResourceHashes.ToArray(), Audience = r.Audience.ToArray() };
    public void Approve(AnimationGraphWriteReview displayed, string fingerprint, Guid exactGraph, bool reviewed)
    {
        Verify(); _ = Required(displayed.Proposal);
        if (!reviewed || exactGraph != displayed.Graph || fingerprint != displayed.Fingerprint || Review is null || JsonSerializer.Serialize(displayed) != JsonSerializer.Serialize(Review)) throw new EditRejectedException("graph_review_stale");
        // Explicit trusted action; pin exact approved generations again and compare to displayed hashes.
        var actual = CaptureReview(displayed.Proposal); if (actual.Fingerprint != fingerprint) throw new EditRejectedException("graph_resource_review_changed");
        if (!displayed.Agent && !_fileWrite) throw new EditRejectedException("graph_file_write_not_approved");
        if (_approvals.Count >= 64) throw new EditRejectedException("graph_history_scope_budget");
        var c = Required(displayed.Proposal); var dependencies = AnimationGraphValidation.Dependencies(c.Graph);
        var assets = RuntimeAssetLoader.Prepare(_root, _project, dependencies.Select(d => new AssetRef(new(d.Id), d.IsSkeleton ? AssetKind.Skeleton : AssetKind.Clip)));
        string key = string.Join("|", actual.Dependencies.Zip(actual.ResourceHashes, (id, hash) => id.ToString("D") + ":" + hash));
        try {
            using var lease = assets.AcquireLease();
            var closure=lease.List();if (!closure.Select(r=>r.Id).SequenceEqual(actual.Dependencies)||!closure.Select(ResourceProof).SequenceEqual(actual.ResourceHashes)) throw new EditRejectedException("graph_resource_review_changed");
            if (!_pins.ContainsKey(key)) { if (_pins.Count >= 4) throw new EditRejectedException("graph_pinned_closure_budget"); _pins.Add(key, assets); assets = null!; }
            _approvals.Add(new(CloneReview(actual), _time.GetTimestamp()));
        } finally { assets?.Dispose(); }
    }
    private bool Approved(string path, Guid graph, string? before, string after)
    {
        if (!Current() || Edit.State.Frozen || Edit.State.HistoryInvalidated) return false;
        return _approvals.Any(a => a.Review.Path == path && a.Review.Graph == graph && a.Review.BeforeHash == before && a.Review.AfterHash == after &&
            (!a.Review.Agent || _time.GetElapsedTime(a.Start, _time.GetTimestamp()) < TimeSpan.FromSeconds(60) && AudienceCurrent(a.Review.Endpoint, a.Review.AudienceEpoch, a.Review.Audience)) &&
            !_candidates.Any(c=>c.Value.Agent&&c.Value.Graph.AssetId==graph&&Hash(AnimationGraphCodec.Encode(c.Value.Graph))==after&&(!a.Review.Agent||a.Review.Proposal!=c.Key||a.Review.ReadPermission!=Reads.PermissionRevision)));
    }
    private bool AudienceCurrent(Guid id, ulong epoch, Guid[] audience) => _workspace.Owner.Endpoint is { } endpoint && endpoint.View.InstanceId == id && endpoint.AudienceRevision == epoch && audience.SequenceEqual(Audience(endpoint));
    private void ValidateResources(string path,Guid graph,string? before,string after)
    {
        if(!Approved(path,graph,before,after))throw new EditRejectedException("graph_scope_denied");
        var review=_approvals.Last(a=>a.Review.Path==path&&a.Review.Graph==graph&&a.Review.BeforeHash==before&&a.Review.AfterHash==after).Review;
        var skeleton=review.Dependencies.Zip(review.ResourceHashes).Single(r=>r.Second.StartsWith("Skeleton:",StringComparison.Ordinal)).First;
        using var current=RuntimeAssetLoader.Prepare(_root,_project,[new(new(skeleton),AssetKind.Skeleton)]);using var lease=current.AcquireLease();var closure=lease.List();
        if(!closure.Select(r=>r.Id).SequenceEqual(review.Dependencies)||!closure.Select(ResourceProof).SequenceEqual(review.ResourceHashes))throw new EditRejectedException("graph_resource_review_changed");
    }
    public CapabilityResult CommitLocal(Guid proposal)
    {
        Verify(); var c = Required(proposal); if (c.Agent || _interaction?.IsActive != true) throw new EditRejectedException("graph_local_draft_required");
        var result = _interaction.Commit(new(2, Guid.NewGuid(), _session, _workspace.Stamp.Revision, AnimationGraphCommands.CapabilityName,
            JsonSerializer.SerializeToElement(new { proposalId = proposal, expectedAssetRevision = Assets.Clock.Revision })), new([AnimationGraphCommands.CapabilityName]));
        _interaction.Dispose(); _interaction = null; _draft = _baseline = null;
        if (result.Status != "ok") throw new EditRejectedException(result.Code); Synchronize(); return result;
    }
    public void Synchronize()
    {
        Verify(); if (_knownAssets == Assets.Clock.Revision) return; if (HasDraft) Cancel(); ClearProposals();
        if(Edit.State.Frozen)return;
        if (_path is not null) {
            try { _raw = Assets.Graphs.Read(_path); _source = AnimationGraphCodec.Decode(_raw); Reads.OpenTrustedRelative(_path); }
            catch(FileNotFoundException) { _raw=null;_source=null;Reads.Revoke();_fileWrite=false; }
        }
        _knownAssets = Assets.Clock.Revision; _content = checked(_content + 1);
    }
    public void Cancel() { _ = Edit.State; _interaction?.Dispose(); _interaction = null; _draft = _baseline = null; _content = checked(_content + 1); ClearProposals(); }
    public void CancelProposal(Guid id) { Verify(); Assets.Graphs.CancelProposal(id); _candidates.Remove(id); _retired.Add(id); Review = null; }
    private void ClearProposals() { foreach (Guid id in _candidates.Keys.ToArray()) { _workspace.Owner.Assets?.Graphs.CancelProposal(id); _retired.Add(id); } _candidates.Clear(); Review = null; }
    public void Revoke() { _ = Edit.State; foreach (var pin in _pins.Values) pin.Dispose(); _pins.Clear(); _approvals.Clear(); _fileWrite = false; Reads.Revoke();Sequences.Revoke();Skeletons.Revoke();Review = null; }
    private void RequireUnchangedFile() { if (_path is null) throw new EditRejectedException("graph_file_conflict"); if(_raw is null){Assets.Graphs.RequireMissing(_path);return;} if(!Assets.Graphs.Read(_path).AsSpan().SequenceEqual(_raw))throw new EditRejectedException("graph_file_conflict"); }
    // Trusted explicit preview preparation, never called by a capability or simulation callback.
    public RuntimeAssetSnapshot PreparePreview(Guid proposal)
    {
        Verify();var c=Required(proposal); var approval=_approvals.LastOrDefault(a=>a.Review.Proposal==proposal&&Approved(a.Review.Path,a.Review.Graph,a.Review.BeforeHash,a.Review.AfterHash))??throw new EditRejectedException("graph_preview_not_approved");
        string key=string.Join("|",approval.Review.Dependencies.Zip(approval.Review.ResourceHashes,(id,hash)=>id.ToString("D")+":"+hash));
        using var lease=_pins[key].AcquireLease();return lease.CreateGraphPreview(c.Graph);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string ResourceProof(RuntimeAsset asset)=>asset.Kind+":"+asset.Generation+":"+asset.ContentHash;
    public void Dispose() { if (_disposed) return; Cancel(); Revoke();Sequences.Dispose();Skeletons.Dispose();_disposed = true; }
}
