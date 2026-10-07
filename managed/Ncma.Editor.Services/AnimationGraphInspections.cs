using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Animation;
using Ncma.Assets;
using Ncma.Assets.Authoring;
using Ncma.Editor.Core;
using Ncma.Editor.Transport;

namespace Ncma.Editor.Services;

public sealed record AnimationGraphReadReview(EditorViewStamp Stamp, Guid GraphId, string Hash,
    ulong Publication, Guid[] Dependencies, Guid EndpointId, ulong AudienceRevision, Guid[] Audience, string Fingerprint);
public sealed record AnimationGraphReadGrant(Guid GraphId, int Seconds);

// Host-owned immutable authoring snapshot. Never reads/parses/hashes files during a capability request.
public sealed class AnimationGraphInspections
{
    private readonly EditorWorkspace _workspace;
    private readonly string _root;
    private readonly TimeProvider _time;
    private AnimationGraphDefinition? _snapshot;
    private string _hash = "", _relative = "";
    private Guid[] _dependencies = [];
    private Dictionary<string, object[]> _rows = [];
    private AnimationGraphReadReview? _grant;
    private EditorEndpoint? _grantEndpoint;
    private long _grantStart;
    private ulong _publication;
    private EditSession Edit => _workspace.Owner.Edit ?? throw new InvalidOperationException("Editor required.");
    public AnimationGraphInspections(EditorWorkspace workspace, string projectRoot, TimeProvider? time = null)
    {
        _workspace = workspace; _root = new AssetProjectPaths(projectRoot).Root; _time = time ?? TimeProvider.System;
        Edit.RegisterInspections(AnimationGraphInspectionSchemas.Descriptors().Select(d => (d, (Func<JsonElement, object>)(i => Inspect(d.Name, i)))).ToArray());
    }
    public void OpenTrustedRelative(string relative)
    {
        _ = Edit.State; if (Edit.State.Frozen || Edit.State.EditBusy || Edit.State.HistoryInvalidated) throw new EditRejectedException("graph_open_busy");
        relative = AssetPaths.Validate(relative); AnimationGraphCodec.RequireExtension(relative); Revoke();
        var paths = new AssetProjectPaths(_root);
        if (File.Exists(paths.Resolve(relative + ".journal")) || Directory.Exists(paths.Resolve(relative + ".journal"))) throw new IOException("Graph recovery required.");
        byte[] bytes = UiAuthoringSource.Read(_root, relative, AnimationGraphCodec.MaxBytes); // Shared checked HANDLE-based read, not a UI codec.
        var candidate = AnimationGraphCodec.Decode(bytes); byte[] canonical = AnimationGraphCodec.Encode(candidate);
        Guid[] dependencies = AnimationGraphValidation.Dependencies(candidate).Select(d => d.Id).ToArray();
        using var parsed = JsonDocument.Parse(canonical);
        object[] Rows(string name) => parsed.RootElement.GetProperty(name).EnumerateArray().Select(v => (object)v.Clone()).ToArray();
        var rows = new Dictionary<string, object[]> {
            ["summary"] = [new { candidate.AssetId, candidate.Name, candidate.SkeletonId, candidate.EntryState, version = candidate.Version }],
            ["nodes"] = Rows("nodes"), ["parameters"] = Rows("parameters"),
            ["links"] = Rows("links"), ["states"] = Rows("states"), ["transitions"] = Rows("transitions"),
            ["dependencies"] = AnimationGraphValidation.Dependencies(candidate).Cast<object>().ToArray()
        };
        ulong publication = checked(_publication + 1); string hash = Convert.ToHexString(SHA256.HashData(canonical));
        _snapshot = candidate; _hash = hash; _dependencies = dependencies; _relative = relative; _rows = rows; _publication = publication;
    }
    public (Guid Id, string Name, string Hash, string Relative) LocalSummary {
        get { _ = Edit.State; return _snapshot is null ? (Guid.Empty, "", "", "") : (_snapshot.AssetId, _snapshot.Name, _hash, _relative); }
    }
    public AnimationGraphDefinition LocalCopy() { _ = Edit.State; return AnimationGraphCodec.Decode(AnimationGraphCodec.Encode(Required())); }
    private AnimationGraphDefinition Required() => _snapshot ?? throw new EditRejectedException("graph_not_open");
    private static Guid[] Audience(EditorEndpoint endpoint) => endpoint.View.Connections.Where(c => c.Paired && c.Connected).Select(c => c.ConnectionId).Order().ToArray();
    private static string Fingerprint(AnimationGraphReadReview page) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(page with { Fingerprint = "" })));
    public AnimationGraphReadReview Capture()
    {
        _ = Edit.State; var graph = Required(); var state = Edit.State;
        if (state.Frozen || state.EditBusy || state.HistoryInvalidated || _workspace.HasDraft) throw new EditRejectedException("graph_review_busy");
        var endpoint = _workspace.Owner.Endpoint ?? throw new EditRejectedException("graph_endpoint_missing"); var audience = Audience(endpoint);
        if (audience.Length == 0) throw new EditRejectedException("graph_audience_missing");
        var page = new AnimationGraphReadReview(_workspace.Stamp, graph.AssetId, _hash, _publication, (Guid[])_dependencies.Clone(), endpoint.View.InstanceId, endpoint.AudienceRevision, audience, "");
        return page with { Fingerprint = Fingerprint(page) };
    }
    public bool IsCurrent(AnimationGraphReadReview review)
    {
        try { return Fingerprint(review) == review.Fingerprint && Capture().Fingerprint == review.Fingerprint; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return false; }
    }
    public void Approve(AnimationGraphReadReview displayed, string fingerprint, bool reviewed)
    {
        if (!reviewed || fingerprint != displayed.Fingerprint || !IsCurrent(displayed)) throw new EditRejectedException("graph_review_stale");
        _grant = displayed with { Dependencies = (Guid[])displayed.Dependencies.Clone(), Audience = (Guid[])displayed.Audience.Clone() };
        _grantEndpoint = _workspace.Owner.Endpoint; _grantStart = _time.GetTimestamp();
    }
    public void Revoke() { _ = Edit.State; _grant = null; _grantEndpoint = null; }
    private bool Active()
    {
        _ = Edit.State;
        if (_grant is null) return false;
        bool valid;
        try { valid = ReferenceEquals(_grantEndpoint, _workspace.Owner.Endpoint) &&
            _time.GetElapsedTime(_grantStart, _time.GetTimestamp()) < TimeSpan.FromSeconds(60) && IsCurrent(_grant); }
        catch (Exception e) when (e is not OutOfMemoryException) { valid = false; }
        if (!valid) Revoke(); return valid;
    }
    public AnimationGraphReadGrant Grant => Active() ? new(_grant!.GraphId, (int)Math.Ceiling(60 - _time.GetElapsedTime(_grantStart, _time.GetTimestamp()).TotalSeconds)) : new(Guid.Empty, 0);
    private object Inspect(string capability, JsonElement input)
    {
        _ = Edit.State; // owner thread and disposed checks before any private state access.
        if (!Active()) throw new EditRejectedException("graph_not_visible");
        var graph = Required();
        string[] allowed = capability == "ncma.animgraph.validate" ? ["graphId"] : ["graphId", "section", "offset", "limit"];
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Any(p => !allowed.Contains(p.Name))) throw Invalid();
        if (!input.TryGetProperty("graphId", out var idValue) || idValue.ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(idValue.GetString(), "D", out Guid id) || id == Guid.Empty || id.ToString("D") != idValue.GetString()) throw Invalid();
        if (id != graph.AssetId) throw new EditRejectedException("graph_not_visible");
        if (capability == "ncma.animgraph.validate") return new { graphId = id, hash = _hash, valid = true,
            validation = "structural_only", resourcesPrepared = false, diagnostics = Array.Empty<AnimationGraphDiagnostic>() };
        if (!input.TryGetProperty("section", out var s) || s.ValueKind != JsonValueKind.String) throw Invalid();
        string section = s.GetString()!; int offset = Integer(input, "offset", 0, 0, 4096), limit = Integer(input, "limit", 16, 1, 32);
        // Cache is already copied and validated. Inspection NEVER compiles, advances, samples or reads disk.
        if (!_rows.TryGetValue(section, out var rows)) throw Invalid();
        return new { graphId = id, hash = _hash, section, total = rows.Length, items = rows.Skip(offset).Take(limit).ToArray(), nextOffset = offset + limit < rows.Length ? (int?)(offset + limit) : null };
    }
    private static EditCommandRejectedException Invalid() => new("graph_input_invalid");
    private static int Integer(JsonElement input, string name, int fallback, int min, int max)
    { if (!input.TryGetProperty(name, out var value)) return fallback; if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int n) || n < min || n > max) throw Invalid(); return n; }
}
