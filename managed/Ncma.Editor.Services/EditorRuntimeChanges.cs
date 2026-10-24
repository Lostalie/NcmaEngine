using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Gameplay;
using Ncma.Scene;

namespace Ncma.Editor.Services;

// A copied observation, not an executable command, authorization, persistent scene format,
// runtime handle or automatic synchronization. Complete before/after payloads are never truncated.
public sealed record RuntimeObjectChange(Guid ObjectId, string Kind, string? TypeId,
    JsonElement? Before, JsonElement? After);
public sealed record RuntimeChangeReviewData(int Version, EditorViewStamp Edit, SceneBakeStamp Authoring,
    Guid PlaySessionId, RuntimeInspectionStamp Runtime, Guid CatalogGeneration, Guid[] ObjectIds,
    RuntimeObjectChange[] Changes);

public sealed class RuntimeChangeReview
{
    private readonly byte[] _bytes;
    internal RuntimeChangeReview(byte[] bytes)
    {
        _bytes = (byte[])bytes.Clone();
        Fingerprint = Convert.ToHexString(SHA256.HashData(_bytes));
    }
    public string Fingerprint { get; }
    public int ByteCount => _bytes.Length;
    public byte[] CopyBytes() => (byte[])_bytes.Clone();
    public RuntimeChangeReviewData CopyData() => JsonSerializer.Deserialize<RuntimeChangeReviewData>(_bytes,
        EditorRuntimeChanges.Json) ?? throw new InvalidOperationException("Missing runtime change review.");
}

// Cold, explicit, local review over the existing author's exact Play origin and actual runtime.
// Explicit local execution uses original commands/history, never snapshot replacement or MCP grants.
public sealed partial class EditorRuntimeChanges(EditorSessionOwner owner)
{
    public const int MaxObjects = 32, MaxChanges = 128, MaxReviewBytes = 65536;
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private sealed record Pending(RuntimeChangeReview Review, RuntimeChangeReviewData Data, PlaySession Play);
    private Pending? _pending;
    private bool _capturing;
    internal void Invalidate() { _pending = null; _prepared = null; } // Checked owner lifecycle only.
    public void Discard()
    {
        _ = owner.Edit!.State;
        if (_capturing) throw new EditRejectedException("runtime_change_review_busy");
        Invalidate();
    }

    private (PlaySession Play, EditorViewStamp Edit, SceneBakeStamp Authoring, RuntimeInspectionStamp Runtime) Source()
    {
        var edit = owner.Edit ?? throw new EditRejectedException("runtime_change_editor_missing");
        var state = edit.State;
        if (state.HistoryInvalidated || state.EditBusy || !state.Frozen ||
            owner.Play is not { State: PlayState.Paused } play || owner.PlayAuthoringSource is not { } source)
            throw new EditRejectedException("runtime_change_requires_paused_play");
        if (source.DocumentSession != owner.Document.SessionId || source.Revision != owner.Document.Revision)
            throw new EditRejectedException("runtime_change_authoring_stale");
        return (play, new(edit.SessionId, edit.DocumentGeneration, edit.Revision), source, play.Scene.GetInspectionStamp());
    }

    public bool IsCurrent(RuntimeChangeReview review)
        => !_capturing && MatchesCurrent(review);

    private bool MatchesCurrent(RuntimeChangeReview review)
    {
        try
        {
            if (_pending is not { } p || !ReferenceEquals(review, p.Review)) return false;
            var source = Source(); var data = p.Data;
            return ReferenceEquals(source.Play, p.Play) && source.Edit == data.Edit && source.Authoring == data.Authoring &&
                source.Play.SessionId == data.PlaySessionId && source.Runtime == data.Runtime &&
                owner.Catalog.Snapshot.Generation == data.CatalogGeneration;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or EditRejectedException) { return false; }
    }

    public RuntimeChangeReview Capture(IReadOnlyList<Guid> exactObjectIds)
    {
        _ = owner.Edit!.State; // Affinity before touching even the service's temporary state.
        if (_capturing) throw new EditRejectedException("runtime_change_review_busy");
        _capturing = true;
        try { return CaptureCore(exactObjectIds); }
        finally { _capturing = false; }
    }
    private RuntimeChangeReview CaptureCore(IReadOnlyList<Guid> exactObjectIds)
    {
        ArgumentNullException.ThrowIfNull(exactObjectIds);
        var source = Source();
        // Own the caller's choice before user-defined component encoders can run.
        Guid[] ids = CopyBoundedIds(exactObjectIds, false);
        if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new EditRejectedException("runtime_change_object_scope");
        Array.Sort(ids);
        Guid catalog = owner.Catalog.Snapshot.Generation;
        using var authorRead = owner.Document.ReadOnly();
        var author = owner.Document.CaptureSnapshot();
        var runtime = source.Play.Scene.CaptureSnapshot();
        if (author.Name != runtime.Name) throw new EditRejectedException("runtime_change_scene_name_requires_separate_review");
        var before = author.Objects.ToDictionary(o => o.Id, o => (ISceneObjectContent)o);
        var after = runtime.Objects.ToDictionary(o => o.Id, o => (ISceneObjectContent)o);
        var changes = new List<RuntimeObjectChange>();
        void Add(Guid id, string kind, string? type, JsonElement? oldValue, JsonElement? newValue)
        {
            if (changes.Count >= MaxChanges) throw new EditRejectedException("runtime_change_item_budget");
            changes.Add(new(id, kind, type, oldValue?.Clone(), newValue?.Clone()));
        }
        foreach (Guid id in ids)
        {
            before.TryGetValue(id, out var old); after.TryGetValue(id, out var value);
            if (old is null && value is null) throw new EditRejectedException("runtime_change_unknown_object");
            if (old is null) { Add(id, "create_object", null, null, ObjectValue(value!)); continue; }
            if (value is null) { Add(id, "delete_object", null, ObjectValue(old), null); continue; }
            if (old.Name != value.Name) Add(id, "rename_object", null, Encode(old.Name), Encode(value.Name));
            var oldComponents = old.Components.ToDictionary(c => c.TypeId, StringComparer.Ordinal);
            var components = value.Components.ToDictionary(c => c.TypeId, StringComparer.Ordinal);
            foreach (string type in oldComponents.Keys.Union(components.Keys).Order(StringComparer.Ordinal))
            {
                oldComponents.TryGetValue(type, out var a); components.TryGetValue(type, out var b);
                JsonElement? aValue = a is null ? null : Encode(a), bValue = b is null ? null : Encode(b);
                if (!Equal(aValue, bValue)) Add(id, b is null ? "remove_component" : "set_component", type, aValue, bValue);
            }
            var oldBindings = Bindings(old.Behaviours); var bindings = Bindings(value.Behaviours);
            if (!Equal(oldBindings, bindings)) Add(id, "set_bindings", null, oldBindings, bindings);
        }
        var data = new RuntimeChangeReviewData(1, source.Edit, source.Authoring, source.Play.SessionId,
            source.Runtime, catalog, ids, changes.ToArray());
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(data, Json);
        if (bytes.Length > MaxReviewBytes) throw new EditRejectedException("runtime_change_review_budget");
        var current = Source();
        if (!ReferenceEquals(current.Play, source.Play) || current.Edit != source.Edit || current.Authoring != source.Authoring ||
            current.Runtime != source.Runtime || owner.Catalog.Snapshot.Generation != catalog)
            throw new EditRejectedException("runtime_change_source_changed");
        var review = new RuntimeChangeReview(bytes);
        // Failed preparation above preserves the previous review; no partially updated plan escapes.
        _pending = new(review, review.CopyData(), source.Play);
        _prepared = null;
        return review;
    }

    private static JsonElement ObjectValue(ISceneObjectContent item) =>
        Encode(new { item.Id, item.Name, Components = item.Components.OrderBy(c => c.TypeId, StringComparer.Ordinal).ToArray(),
            Behaviours = OrderedBindings(item.Behaviours) });
    // Behaviour order may affect lifecycle order and is retained. Export order is not semantic;
    // bake canonicalizes names, so a fresh unchanged Play must not produce false changes.
    private static BehaviourBindingData[] OrderedBindings(BehaviourBindingData[] values) =>
        values.Select(b => b with { Exports = b.Exports.OrderBy(e => e.Name, StringComparer.Ordinal).ToArray() }).ToArray();
    private static JsonElement Bindings(BehaviourBindingData[] values) => Encode(OrderedBindings(values));
    private static JsonElement Encode<T>(T value) => JsonSerializer.SerializeToElement(value, Json);
    private static bool Equal(JsonElement? a, JsonElement? b)
    {
        if (!a.HasValue || !b.HasValue) return a.HasValue == b.HasValue;
        return Canonical(a.Value).AsSpan().SequenceEqual(Canonical(b.Value));
    }
    private static byte[] Canonical(JsonElement value)
    {
        using var output = new MemoryStream(); using (var writer = new Utf8JsonWriter(output)) Write(writer, value);
        return output.ToArray();
    }
    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            { writer.WritePropertyName(property.Name); Write(writer, property.Value); }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) Write(writer, item); writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }
}
