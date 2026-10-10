using System.Text.Json;
using Ncma.Assets;
using Ncma.Editor.Core;

namespace Ncma.Editor.Services;

public sealed record AssetReadRow(Guid AssetId, string Kind, string Name, string State);
public sealed record AssetReadDiagnostic(string Code, Guid AssetId, string Field, string Severity);
public sealed record AssetReadGrantView(Guid[] AssetIds, int RemainingSeconds);

// Trusted local composition. A grant exposes sanitized metadata to paired clients, NOT file access.
// Publish is outside the endpoint pump: inspection never scans, hashes, parses assets or waits for IO.
public sealed class AssetInspectionService
{
    public const ulong MaxRevision = 9007199254740991;
    private sealed record Entry(AssetReadRow Row, Guid Root, ulong Generation);
    private readonly EditSession _edit;
    private readonly Func<bool> _current;
    private readonly Func<(ulong Clock, ulong Snapshot)> _revisions;
    private readonly TimeProvider _time;
    private Dictionary<Guid, Entry> _entries = [];
    private Dictionary<Guid, AssetDependency[]> _dependencies = [];
    private Dictionary<Guid, string[]> _diagnostics = [];
    private HashSet<Guid> _approved = [];
    private ulong _revision, _grantGeneration;
    private long _grantTime;
    private bool _published;
    private Func<bool>? _audienceCurrent;
    public Guid ProjectId { get; }
    public ulong ProjectGeneration { get; }
    public ulong PublishedRevision { get { Verify(); return _revision; } }

    public AssetInspectionService(EditSession edit, Guid projectId, ulong generation,
        Func<bool> current, Func<(ulong Clock, ulong Snapshot)> revisions, TimeProvider? time = null)
    {
        _edit = edit ?? throw new ArgumentNullException(nameof(edit)); _ = edit.State;
        if (projectId == Guid.Empty || generation == 0) throw new ArgumentException("Invalid asset inspection project.");
        _current = current ?? throw new ArgumentNullException(nameof(current));
        _revisions = revisions ?? throw new ArgumentNullException(nameof(revisions)); _time = time ?? TimeProvider.System;
        ProjectId = projectId; ProjectGeneration = generation;
    }
    private void Verify() { _ = _edit.State; }
    public void Register()
    {
        Verify(); var descriptors = AssetInspectionSchemas.Descriptors();
        _edit.RegisterInspections(descriptors.Select(d => (d, (Func<JsonElement, object>)(input => InspectSafely(d.Name, input)))).ToArray());
    }
    public void Publish(AssetCatalog catalog, AssetDiagnostic[] diagnostics, ulong revision)
    {
        Verify(); ArgumentNullException.ThrowIfNull(catalog); ArgumentNullException.ThrowIfNull(diagnostics);
        if (!_current()) { Revoke(); throw new EditCommandRejectedException("asset_snapshot_stale"); }
        var versions = _revisions();
        if (revision > MaxRevision || revision != versions.Clock || revision != versions.Snapshot || diagnostics.Length > 16384)
            throw new EditCommandRejectedException("asset_snapshot_stale");
        if (_published && revision <= _revision) throw new EditCommandRejectedException("asset_snapshot_revision_reused");
        var entries = new Dictionary<Guid, Entry>(); var dependencies = new Dictionary<Guid, AssetDependency[]>();
        for (int offset = 0; offset < catalog.Count; offset += 128)
            foreach (var root in catalog.List(offset, 128)) {
                ulong generation = root.Generation?.Number ?? 0;
                if (generation > MaxRevision) throw new ArgumentException("Invalid public generation budget.");
                entries.Add(root.AssetId, new(Row(root.AssetId, root.Kind, generation == 0 ? "registered" : "derived"), root.AssetId, generation));
                foreach (var sub in root.Subassets) entries.Add(sub.AssetId, new(Row(sub.AssetId, sub.Kind, sub.Tombstone ? "tombstone" : "registered"), root.AssetId, generation));
                dependencies.Add(root.AssetId, (AssetDependency[])root.Dependencies.Clone());
            }
        var safeDiagnostics = diagnostics.Where(d => d.AssetId is Guid id && entries.ContainsKey(id))
            .GroupBy(d => d.AssetId!.Value).ToDictionary(g => g.Key, g => g.Select(d => d.Code == "source_missing" ? "source_missing" : "asset_diagnostic").Distinct().ToArray());
        bool changed = !_published || _revision != revision;
        _entries = entries; _dependencies = dependencies; _diagnostics = safeDiagnostics; _revision = revision; _published = true;
        if (changed) Revoke();
    }
    // No request JSON or Agent tool can approve itself. Host must review exact root AND subasset UUIDs.
    public void ApproveForPairedClients(Guid[] exactIds, Func<bool>? audienceCurrent = null)
    {
        Verify(); ArgumentNullException.ThrowIfNull(exactIds); RequireSnapshot();
        var state = _edit.State;
        if (state.Frozen || state.EditBusy || state.HistoryInvalidated || exactIds.Length is < 1 or > 64 ||
            exactIds.Distinct().Count() != exactIds.Length || exactIds.Any(id => id == Guid.Empty || !_entries.ContainsKey(id)))
            throw new EditRejectedException("asset_read_scope_invalid");
        if (audienceCurrent is not null && !audienceCurrent()) throw new EditRejectedException("asset_read_scope_invalid");
        _approved = new(exactIds); _grantGeneration = _edit.DocumentGeneration; _grantTime = _time.GetTimestamp(); _audienceCurrent = audienceCurrent;
    }
    public void Revoke() { Verify(); _approved = []; _audienceCurrent = null; }
    // Trusted UI cache only. No source filenames or inherited child approval.
    public AssetReadRow[] ReviewRows(Guid[] exactIds)
    {
        Verify(); ArgumentNullException.ThrowIfNull(exactIds); RequireSnapshot();
        if (exactIds.Length is < 1 or > 64 || exactIds.Distinct().Count() != exactIds.Length || exactIds.Any(id => !_entries.ContainsKey(id)))
            throw new EditRejectedException("asset_read_scope_invalid");
        return exactIds.Order().Select(id => _entries[id].Row).ToArray();
    }
    public AssetReadGrantView Grant
    {
        get {
            Verify(); if (!GrantActive()) return new([], 0);
            return new(_approved.Order().ToArray(), (int)Math.Ceiling((TimeSpan.FromSeconds(60) - _time.GetElapsedTime(_grantTime, _time.GetTimestamp())).TotalSeconds));
        }
    }
    private bool GrantActive()
    {
        if (_approved.Count == 0) return false;
        bool active;
        try { var versions = _revisions(); active = _current() && versions.Clock == _revision && versions.Snapshot == _revision &&
            _grantGeneration == _edit.DocumentGeneration && _time.GetElapsedTime(_grantTime, _time.GetTimestamp()) < TimeSpan.FromSeconds(60) &&
            (_audienceCurrent?.Invoke() ?? true); }
        catch (Exception e) when (e is not OutOfMemoryException) { active = false; }
        if (!active) Revoke(); // Once observed invalid, a disconnected/re-paired audience cannot revive a grant.
        return active;
    }
    private void RequireSnapshot()
    {
        if (!_current()) { Revoke(); throw new EditCommandRejectedException("asset_snapshot_stale"); }
        var versions = _revisions();
        if (!_published || versions.Clock != _revision || versions.Snapshot != _revision)
        { Revoke(); throw new EditCommandRejectedException("asset_snapshot_stale"); }
    }
    private bool Visible(Guid id) => _approved.Contains(id); // Grant checked once at the owner query boundary, before enumeration.
    private static string Kind(AssetKind kind) => JsonNamingPolicy.CamelCase.ConvertName(kind.ToString());
    private static AssetReadRow Row(Guid id, AssetKind kind, string state) => new(id, Kind(kind), Kind(kind) + " " + id.ToString("D")[..8], state);
    private object InspectSafely(string name, JsonElement input)
    {
        try { return Inspect(name, input); }
        catch (EditRejectedException e) when (e.Code == "asset_not_visible") { throw; }
        catch (EditCommandRejectedException e) when (e.Code is "asset_snapshot_stale" or "asset_inspection_invalid_input") { throw; }
        catch (Exception e) when (e is not OutOfMemoryException) { throw new EditCommandRejectedException("asset_inspection_failed"); }
    }
    private object Inspect(string name, JsonElement input)
    {
        Verify(); RequireSnapshot();
        _ = GrantActive();
        bool list = name == "ncma.assets.list", validate = name == "ncma.assets.validate";
        string[] allowed = list ? ["kind", "offset", "limit"] : validate ? ["assetId", "offset", "limit"] : ["assetId", "section", "offset", "limit"];
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Any(p => !allowed.Contains(p.Name))) Bad();
        int offset = Integer(input, "offset", 0, 0, 65536), limit = Integer(input, "limit", 16, 1, 64);
        if (list) {
            string? kind = input.TryGetProperty("kind", out var k) ? k.ValueKind == JsonValueKind.String ? k.GetString() : throw Invalid() : null;
            if (kind is not null && !Enum.GetValues<AssetKind>().Any(v => AssetRecordCodec.SupportsKind(v) && Kind(v) == kind)) Bad();
            var rows = _approved.Where(Visible).Select(id => _entries[id].Row).Where(e => kind is null || kind == e.Kind).OrderBy(e => e.AssetId).ToArray();
            return new { assetRevision = _revision, total = rows.Length, items = rows.Skip(offset).Take(limit).ToArray(), nextOffset = Next(offset, limit, rows.Length) };
        }
        Guid id = Uuid(input);
        if (!Visible(id) || !_entries.TryGetValue(id, out var entry)) throw new EditRejectedException("asset_not_visible");
        if (validate) {
            var diagnostics = new List<AssetReadDiagnostic>();
            if (entry.Row.State == "tombstone") diagnostics.Add(new("asset_tombstone", id, "asset", "error"));
            foreach (string code in _diagnostics.GetValueOrDefault(entry.Root, [])) diagnostics.Add(new(code, id, "asset", "error"));
            foreach (var dependency in _dependencies[entry.Root]) {
                string? code = !Visible(dependency.AssetId) ? "dependency_not_visible" : !_entries.TryGetValue(dependency.AssetId, out var target) ? "asset_missing" :
                    target.Row.State == "tombstone" ? "asset_tombstone" : target.Row.Kind != Kind(dependency.ExpectedKind) ? "asset_kind_mismatch" : null;
                if (code is not null) diagnostics.Add(new(code, id, "dependencies", code == "dependency_not_visible" ? "warning" : "error"));
            }
            var values = diagnostics.Distinct().OrderBy(d => d.Code, StringComparer.Ordinal).ToArray();
            return new { assetRevision = _revision, valid = values.Length == 0, total = values.Length, diagnostics = values.Skip(offset).Take(limit).ToArray(), nextOffset = Next(offset, limit, values.Length) };
        }
        if (!input.TryGetProperty("section", out var section) || section.ValueKind != JsonValueKind.String) throw Invalid();
        IEnumerable<AssetReadRow> items = section.GetString() switch {
            "summary" => [entry.Row],
            "subassets" => _approved.Where(Visible).Select(id => _entries[id]).Where(e => e.Root == entry.Root && e.Row.AssetId != entry.Root).Select(e => e.Row),
            "dependencies" => _dependencies[entry.Root].Where(d => Visible(d.AssetId) && _entries.ContainsKey(d.AssetId)).Select(d => _entries[d.AssetId].Row).Distinct(),
            _ => throw Invalid()
        };
        var result = items.OrderBy(e => e.AssetId).ToArray();
        return new { assetRevision = _revision, assetId = id, generation = entry.Generation, section = section.GetString(), total = result.Length, items = result.Skip(offset).Take(limit).ToArray(), nextOffset = Next(offset, limit, result.Length) };
    }
    private static int? Next(int offset, int limit, int total) => offset + limit < total ? offset + limit : null;
    private static EditCommandRejectedException Invalid() => new("asset_inspection_invalid_input");
    private static void Bad() => throw Invalid();
    private static int Integer(JsonElement input, string key, int fallback, int min, int max)
    {
        if (!input.TryGetProperty(key, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number) || number < min || number > max) throw Invalid();
        return number;
    }
    private static Guid Uuid(JsonElement input)
    {
        if (!input.TryGetProperty("assetId", out var value) || value.ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(value.GetString(), "D", out Guid id) || id == Guid.Empty || id.ToString("D") != value.GetString()) throw Invalid();
        return id;
    }
}
