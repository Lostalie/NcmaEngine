using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.Runtime;
using Ncma.Scene;

namespace Ncma.Editor.Core;

// One complete document, one history. No window, graphics, native or Python dependency.
public sealed partial class EditSession
{
    public const int ContractVersion = 2;
    public const int MaxOperations = 128, MaxInputBytes = 65536, MaxHistoryEntries = 64;
    public const int MaxHistoryBytes = 16 * 1024 * 1024, MaxCachedRequests = 128;
    private sealed record FileContext(string? Path, string? SavedHash);
    private sealed record Entry(byte[] Before, byte[] After, string Label, string Capability, Guid? Target,
        Guid? BeforeSelection, Guid? AfterSelection, FileContext? BeforeFile, FileContext? AfterFile, CommandImpact Impact,
        ParticipantEntry? Participant = null)
    { public int Bytes => checked(Before.Length + After.Length + (Participant?.Bytes ?? 0)); }
    private sealed record Cached(string Fingerprint, CapabilityResult Result, string Permission, Guid? Target, CommandImpact Impact,
        ParticipantEntry? Participant = null);
    private sealed record Draft(Guid Token, ulong Revision, Guid? Selection, string Label, JsonElement Input);
    private readonly SceneDocument _document;
    private readonly Dictionary<string, CapabilityDescriptor> _capabilities;
    private Entry[] _history = [];
    private Dictionary<Guid, Cached> _cache = [];
    private Queue<Guid> _cacheOrder = [];
    private int _cursor;
    private ulong _knownRevision;
    private string _currentHash;
    private FileContext _file = new(null, null);
    private Guid? _selection;
    private bool _invalidated, _invoking, _frozen;
    private Draft? _draft;
    private static readonly JsonSerializerOptions OutputJson = new(SceneJson.Options) { IgnoreReadOnlyProperties = false };

    public EditSession(SceneDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.VerifyAccess(); _document = document; _knownRevision = document.Revision;
        _currentHash = Hash(document.CaptureBytes());
        _capabilities = BuildCapabilities().ToDictionary(c => c.Name, StringComparer.Ordinal);
    }
    public Guid SessionId { get; } = Guid.NewGuid();
    public SceneDocument Document => _document;
    public ulong Revision => _document.Revision;
    public EditState State
    {
        get
        {
            Observe();
            return StateFor(Revision, _history, _cursor, _invalidated ? Hash(_document.CaptureBytes()) : _currentHash, _file, _selection);
        }
    }
    private EditState StateFor(ulong revision, Entry[] history, int cursor, string hash, FileContext file, Guid? selection) =>
        new(SessionId, revision, _invalidated ? 0 : cursor, _invalidated ? 0 : history.Length - cursor,
            !_invalidated && cursor > 0 ? history[cursor - 1].Label : "", !_invalidated && cursor < history.Length ? history[cursor].Label : "",
            file.SavedHash != hash, _invalidated, InteractionBusy, _frozen, selection, file.Path);
    private void Observe()
    {
        _document.VerifyAccess();
        if (_document.Revision != _knownRevision) _invalidated = true;
    }
    public void Resynchronize()
    {
        _document.VerifyAccess();
        if (_invoking || InteractionBusy || _frozen) throw new InvalidOperationException("Session is busy or frozen.");
        byte[] bytes = _document.CaptureBytes();
        string hash = Hash(bytes);
        _history = []; _cursor = 0; _cache = []; _cacheOrder = [];
        _knownRevision = _document.Revision; _currentHash = hash; _invalidated = false; DocumentGeneration = checked(DocumentGeneration + 1);
        if (_selection is Guid id && !Exists(SceneDocumentCodec.Decode(bytes), id)) _selection = null;
    }
    public void Select(Guid? objectId)
    {
        _document.VerifyAccess();
        if (_invoking || InteractionBusy) throw new InvalidOperationException("Finish the interaction before selecting.");
        if (objectId is Guid id) _ = _document.World.FindObject(id);
        _selection = objectId;
    }
    public void SetFrozen(bool frozen)
    {
        _document.VerifyAccess();
        if (_invoking) throw new InvalidOperationException("Session is executing.");
        if (frozen) { _draft = null; _interactionGate = null; }
        _frozen = frozen;
    }
    // Trusted hosts may inspect the one pending history authorization, never grant it from request JSON.
    public Guid? HistoryTarget(bool redo)
    {
        Observe();
        int index = redo ? _cursor : _cursor - 1;
        return !_invalidated && index >= 0 && index < _history.Length ? _history[index].Target : null;
    }
    public IReadOnlyList<CapabilityDescriptor> Describe()
    {
        _document.VerifyAccess();
        return _capabilities.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ToArray();
    }
    public CapabilityResult Invoke(CapabilityRequest request, CapabilityPermissions? permissions = null)
    {
        _document.VerifyAccess();
        ArgumentNullException.ThrowIfNull(request);
        permissions ??= CapabilityPermissions.ReadOnly;
        if (_invoking) return Result(request, "error", "edit_busy", false, new { });
        _invoking = true;
        try
        {
            Observe();
            if (request.ContractVersion != ContractVersion || request.RequestId == Guid.Empty)
                return Result(request, "error", "invalid_envelope", false, new { });
            if (request.SessionId != SessionId) return Result(request, "conflict", "session_mismatch", false, new { });
            if (!_capabilities.TryGetValue(request.Capability, out var descriptor))
                return Result(request, "error", "unknown_capability", false, new { });
            CheckInput(request.Input);
            if (descriptor.Risk == MutationRisk.ReadOnly)
            {
                if (request.ExpectedRevision is not null && request.ExpectedRevision != Revision)
                    return Result(request, "conflict", "revision_conflict", false, new { });
                return Inspect(request);
            }
            if (!permissions.Allows(request.Capability)) return Result(request, "denied", "permission_denied", false, new { });
            if (_frozen) return Result(request, "denied", "play_frozen", false, new { });
            if (InteractionBusy) return Result(request, "conflict", "edit_busy", false, new { });
            if (_invalidated) return Result(request, "conflict", "history_invalidated", false, new { });
            string fingerprint = Hash(Encoding.UTF8.GetBytes(request.Capability + ":" + request.ExpectedRevision + ":" + request.Input.GetRawText()));
            if (_cache.TryGetValue(request.RequestId, out var cached))
            {
                if (cached.Fingerprint != fingerprint) return Result(request, "conflict", "request_id_reused", false, new { });
                if (!permissions.Allows(cached.Permission, cached.Target) || !permissions.InScope(cached.Impact)) return Result(request, "denied", "permission_denied", false, new { });
                if (cached.Participant is not null)
                    using (_document.World.ReadOnly()) cached.Participant.Owner.Authorize(cached.Participant.Memento);
                return cached.Result with { Revision = Revision, Replayed = true,
                    Data = JsonSerializer.SerializeToElement(new { history = State, execution = cached.Result.Data }, OutputJson) };
            }
            if (request.ExpectedRevision is null || request.ExpectedRevision != Revision)
                return Result(request, "conflict", "revision_conflict", false, new { });
            return request.Capability is "ncma.history.undo" or "ncma.history.redo"
                ? History(request, permissions, fingerprint)
                : _participants.ContainsKey(request.Capability) ? ParticipantTransaction(request, permissions, fingerprint)
                : Transaction(request, permissions, fingerprint);
        }
        catch (EditRejectedException e) { return Result(request, "denied", e.Code, false, new { }); }
        catch (EditCommandRejectedException e) { return Result(request, e.Denied ? "denied" : "conflict", e.Code, false, new { }); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or JsonException or KeyNotFoundException or FormatException or OverflowException)
        { return Result(request, "error", "invalid_input", false, new { message = e.Message }); }
        catch (Exception e) when (e is not OutOfMemoryException)
        { return Result(request, "error", "operation_failed", false, new { message = e.Message }); }
        finally { _invoking = false; }
    }
    private CapabilityResult Transaction(CapabilityRequest request, CapabilityPermissions permissions, string fingerprint)
    {
        byte[] before = _document.CaptureBytes();
        SceneDocumentSnapshot candidate = SceneDocumentCodec.Decode(before);
        Guid? target = null;
        string label = "Edit scene";
        Guid? selection = _selection;
        if (request.Capability == "ncma.scene.delete_object")
        {
            Closed(request.Input, ["objectId"], ["objectId"]);
            target = Uuid(request.Input, "objectId");
            if (!permissions.Allows(request.Capability, target) || !permissions.ObjectAllowed(target.Value))
                return Result(request, "denied", "target_not_authorized", false, new { });
            if (!Exists(candidate, target.Value)) throw new ArgumentException("Unknown object UUID.");
            candidate = candidate with { Objects = candidate.Objects.Where(o => o.Id != target).ToArray() };
            label = "Delete GameObject";
        }
        else
        {
            Closed(request.Input, ["operations", "label", "selection"], ["operations"]);
            if (request.Input.TryGetProperty("label", out var text)) { label = text.GetString() ?? ""; World.ValidateName(label); }
            if (request.Input.TryGetProperty("selection", out var chosen)) selection = chosen.ValueKind == JsonValueKind.Null ? null : Uuid(request.Input, "selection");
            JsonElement ops = request.Input.GetProperty("operations");
            if (ops.ValueKind != JsonValueKind.Array || ops.GetArrayLength() is < 1 or > MaxOperations) throw new ArgumentException("Requires 1..128 operations.");
            foreach (var op in ops.EnumerateArray())
            {
                AuthorizeIntent(permissions, op); candidate = Apply(candidate, op);
                if (permissions.RequireTrustedBindings && Text(op, "op") == "set_bindings")
                    foreach (var binding in candidate.Objects.Single(o => o.Id == Uuid(op, "objectId")).Behaviours) RequireBinding(binding);
            }
            if (request.Input.TryGetProperty("selection", out _) && selection is Guid selectedId && !permissions.ObjectAllowed(selectedId)) throw new EditRejectedException("scope_denied");
        }
        if (selection is Guid selected && !Exists(candidate, selected)) selection = null;
        return Commit(request, fingerprint, candidate, before, label, request.Capability, target, selection, permissions: permissions);
    }
    private CapabilityResult Commit(CapabilityRequest request, string fingerprint, SceneDocumentSnapshot candidate,
        byte[] before, string label, string capability, Guid? target, Guid? selection,
        FileContext? replacementFile = null, CapabilityPermissions? permissions = null, ParticipantEntry? participant = null)
    {
        byte[] after = [];
        Action install = _document.PrepareRestore(candidate, request.ExpectedRevision, normalized => after = SceneDocumentCodec.Encode(normalized));
        permissions ??= CapabilityPermissions.ReadOnly;
        var impact = RequestedImpact(request, Impact(before, after, _selection, selection, replacementFile is not null));
        if (!permissions.Allows(capability, target) || !permissions.InScope(impact)) throw new EditRejectedException("scope_denied");
        if (before.AsSpan().SequenceEqual(after) && (replacementFile is null || replacementFile == _file) &&
            (participant is null || participant.Memento.Before.AsSpan().SequenceEqual(participant.Memento.After)))
            return Remember(request, fingerprint, Result(request, "ok", "no_change", false, new { history = State }), capability, target, impact, participant);
        string hash = Hash(after);
        FileContext file = replacementFile ?? _file;
        var entry = new Entry(before, after, label, capability, target, _selection, selection,
            replacementFile is null ? null : _file, replacementFile, impact, participant);
        if (entry.Bytes > MaxHistoryBytes) throw new ArgumentException("Command exceeds history budget.");
        var history = _history.Take(_cursor).Append(entry).ToArray();
        int bytes = history.Sum(e => e.Bytes);
        int drop = 0;
        while (history.Length - drop > MaxHistoryEntries || bytes > MaxHistoryBytes) bytes -= history[drop++].Bytes;
        if (drop > 0) history = history[drop..];
        ulong next = checked(Revision + 1);
        var state = StateFor(next, history, history.Length, hash, file, selection) with { EditBusy = false };
        var result = Result(request, "ok", "ok", true, new { history = state }, next);
        var cache = PrepareCache(request.RequestId, new(fingerprint, result, capability, target, impact, participant));
        ulong generation = impact.DocumentReplacement ? checked(DocumentGeneration + 1) : DocumentGeneration;
        if (!permissions.Allows(capability, target) || !permissions.InScope(impact)) throw new EditRejectedException("scope_denied");
        InstallWithParticipant(participant, true, install, () => permissions.Allows(capability, target) && permissions.InScope(impact));
        // History/results were prepared before installation. Only the trusted participant's durable
        // completion ran afterward; failure freezes the session instead of reporting a rollback.
        _history = history; _cursor = history.Length; _knownRevision = next; _currentHash = hash;
        _selection = selection; _file = file; DocumentGeneration = generation; _cache = cache.Values; _cacheOrder = cache.Order;
        return result;
    }
    private CapabilityResult History(CapabilityRequest request, CapabilityPermissions permissions, string fingerprint)
    {
        Closed(request.Input, [], []);
        bool undo = request.Capability == "ncma.history.undo";
        int index = undo ? _cursor - 1 : _cursor;
        if (index < 0 || index >= _history.Length) return Result(request, "error", "history_empty", false, new { });
        Entry entry = _history[index];
        if (!permissions.Allows(entry.Capability, entry.Target)) return Result(request, "denied", "permission_denied", false, new { });
        if (!permissions.InScope(entry.Impact)) return Result(request, "denied", "history_scope_denied", false, new { });
        byte[] after = [];
        Action install = _document.PrepareRestore(SceneDocumentCodec.Decode(undo ? entry.Before : entry.After),
            request.ExpectedRevision, normalized => after = SceneDocumentCodec.Encode(normalized));
        // Validators must not silently change previously committed history.
        if (!after.AsSpan().SequenceEqual(undo ? entry.Before : entry.After)) throw new ArgumentException("History validators changed committed content.");
        string hash = Hash(after);
        int cursor = _cursor + (undo ? -1 : 1);
        var selection = undo ? entry.BeforeSelection : entry.AfterSelection;
        var file = (undo ? entry.BeforeFile : entry.AfterFile) ?? _file;
        ulong next = checked(Revision + 1);
        var result = Result(request, "ok", "ok", true, new { history = StateFor(next, _history, cursor, hash, file, selection) }, next);
        var cache = PrepareCache(request.RequestId, new(fingerprint, result, entry.Capability, entry.Target, entry.Impact, entry.Participant));
        ulong generation = entry.Impact.DocumentReplacement ? checked(DocumentGeneration + 1) : DocumentGeneration;
        if (!permissions.Allows(entry.Capability, entry.Target) || !permissions.InScope(entry.Impact)) throw new EditRejectedException("history_scope_denied");
        InstallWithParticipant(entry.Participant, !undo, install, () => permissions.Allows(entry.Capability, entry.Target) && permissions.InScope(entry.Impact));
        DocumentGeneration = generation;
        _cursor = cursor; _knownRevision = next; _currentHash = hash; _selection = selection; _file = file;
        _cache = cache.Values; _cacheOrder = cache.Order;
        return result;
    }
    private CapabilityResult Remember(CapabilityRequest request, string fingerprint, CapabilityResult result, string permission, Guid? target, CommandImpact impact,
        ParticipantEntry? participant = null)
    {
        var cache = PrepareCache(request.RequestId, new(fingerprint, result, permission, target, impact, participant));
        _cache = cache.Values; _cacheOrder = cache.Order; return result;
    }
    private (Dictionary<Guid, Cached> Values, Queue<Guid> Order) PrepareCache(Guid id, Cached value)
    {
        var values = new Dictionary<Guid, Cached>(_cache); var order = new Queue<Guid>(_cacheOrder);
        values.Add(id, value); order.Enqueue(id);
        long participantBytes = values.Values.Sum(c => (long)(c.Participant?.Bytes ?? 0));
        while (order.Count > MaxCachedRequests || participantBytes > MaxHistoryBytes)
        {
            var removed = values[order.Peek()]; participantBytes -= removed.Participant?.Bytes ?? 0;
            values.Remove(order.Dequeue());
        }
        return (values, order);
    }
    private CapabilityResult Result(CapabilityRequest request, string status, string code, bool changed, object data, ulong? revision = null)
    {
        ulong value = revision ?? Revision;
        return new(ContractVersion, request.RequestId, SessionId, value, status, code, changed,
            JsonSerializer.SerializeToElement(data, OutputJson), value);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static bool Exists(SceneDocumentSnapshot snapshot, Guid id) => snapshot.Objects.Any(o => o.Id == id);
    internal static void CheckInput(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(input.GetRawText()) > MaxInputBytes)
            throw new ArgumentException("Input must be a bounded object.");
        using var json = JsonDocument.Parse(input.GetRawText(), new JsonDocumentOptions { MaxDepth = 32 });
        SceneJson.RejectDuplicateFields(json.RootElement);
    }
    private static Guid Uuid(JsonElement data, string name) =>
        data.GetProperty(name).TryGetGuid(out Guid id) && id != Guid.Empty ? id : throw new ArgumentException("Invalid UUID: " + name);
    private static string Text(JsonElement data, string name) => data.GetProperty(name).GetString() ?? throw new ArgumentException("Missing string: " + name);
    internal static void Closed(JsonElement data, string[] allowed, string[] required)
    {
        if (data.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in data.EnumerateObject())
            if (!seen.Add(p.Name) || !allowed.Contains(p.Name, StringComparer.Ordinal)) throw new ArgumentException("Unknown/duplicate field: " + p.Name);
        foreach (string name in required) if (!seen.Contains(name)) throw new ArgumentException("Missing field: " + name);
    }
    private SceneDocumentSnapshot Apply(SceneDocumentSnapshot scene, JsonElement operation)
    {
        string op = Text(operation, "op");
        Guid id = Uuid(operation, "objectId");
        if (op == "create")
        {
            Closed(operation, ["op", "objectId", "name"], ["op", "objectId", "name"]);
            if (Exists(scene, id)) throw new ArgumentException("Duplicate UUID.");
            return scene with { Objects = scene.Objects.Append(new SceneObjectData(id, Text(operation, "name"), [], [])).ToArray() };
        }
        int index = Array.FindIndex(scene.Objects, o => o.Id == id);
        if (index < 0) throw new ArgumentException("Unknown object UUID.");
        SceneObjectData item = scene.Objects[index];
        switch (op)
        {
            case "rename":
                Closed(operation, ["op", "objectId", "name"], ["op", "objectId", "name"]);
                item = item with { Name = Text(operation, "name") }; break;
            case "set_component":
                Closed(operation, ["op", "objectId", "typeId", "version", "data"], ["op", "objectId", "typeId", "version", "data"]);
                var component = new ComponentSnapshot(Text(operation, "typeId"), operation.GetProperty("version").GetInt32(), operation.GetProperty("data").Clone());
                if (!_document.World.Components.Describe().Any(c => c.TypeId == component.TypeId && c.Version == component.Version))
                    throw new ArgumentException("Unknown component type/version.");
                item = item with { Components = item.Components.Where(c => c.TypeId != component.TypeId).Append(component).ToArray() }; break;
            case "remove_component":
                Closed(operation, ["op", "objectId", "typeId"], ["op", "objectId", "typeId"]);
                string typeId = Text(operation, "typeId");
                if (!_document.World.Components.Describe().Any(c => c.TypeId == typeId)) throw new ArgumentException("Unknown component type.");
                item = item with { Components = item.Components.Where(c => c.TypeId != typeId).ToArray() }; break;
            case "set_bindings":
                Closed(operation, ["op", "objectId", "bindings"], ["op", "objectId", "bindings"]);
                var bindings = operation.GetProperty("bindings").Deserialize<BehaviourBindingData[]>(SceneJson.Options)
                    ?? throw new ArgumentException("Missing bindings.");
                item = item with { Behaviours = bindings }; break;
            case "add_binding": case "remove_binding": case "set_binding_enabled": case "set_export":
                item = BindingOperation(item, operation, op); break;
            default: throw new ArgumentException("Operation is not implemented. Deletion requires separate UUID authorization.");
        }
        var objects = (SceneObjectData[])scene.Objects.Clone(); objects[index] = item;
        return scene with { Objects = objects };
    }
}
