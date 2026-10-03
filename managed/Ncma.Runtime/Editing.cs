using System.Text;
using System.Text.Json;

namespace Ncma.Runtime;

public enum MutationRisk { ReadOnly, Reversible, Destructive }
public sealed record CapabilityDescriptor(string Name, string Description, MutationRisk Risk,
    JsonElement InputSchema, JsonElement OutputSchema);
public sealed record CapabilityRequest(int ContractVersion, Guid RequestId, Guid SessionId,
    ulong? ExpectedRevision, string Capability, JsonElement Input);
public sealed record CapabilityResult(int ContractVersion, Guid RequestId, Guid SessionId,
    ulong Revision, string Status, string Code, bool Changed, JsonElement Data);

// Host-owned permissions, never populated from model-supplied JSON.
public sealed class CapabilityPermissions
{
    private readonly HashSet<string> _allowed;
    private readonly HashSet<Guid> _destructiveTargets;
    public CapabilityPermissions(IEnumerable<string>? allowedMutations = null, IEnumerable<Guid>? approvedDestructiveTargets = null)
    {
        _allowed = new(allowedMutations ?? [], StringComparer.Ordinal);
        _destructiveTargets = new(approvedDestructiveTargets ?? []);
    }
    public static CapabilityPermissions ReadOnly { get; } = new();
    internal bool Allows(string name, Guid? destructiveTarget = null) => _allowed.Contains(name) &&
        (destructiveTarget is null || _destructiveTargets.Contains(destructiveTarget.Value));
}

// Editor and Agent call the SAME gateway. This is headless, not connected to legacy ImGui/MCP.
public sealed class EditSession
{
    public const int MaxOperations = 128;
    public const int MaxInputBytes = 65536;
    public const int MaxHistoryEntries = 64;
    public const int MaxHistoryBytes = 16 * 1024 * 1024;
    private sealed record HistoryEntry(WorldSnapshot Before, WorldSnapshot After, string Capability,
        Guid? DestructiveTarget, int Bytes);
    private sealed record Cached(string Fingerprint, CapabilityResult Result, string RequiredCapability, Guid? Target);
    private readonly World _world;
    private readonly List<HistoryEntry> _history = new();
    private readonly Dictionary<Guid, Cached> _cache = new();
    private readonly Queue<Guid> _cacheOrder = new();
    private readonly Dictionary<string, CapabilityDescriptor> _capabilities;
    private int _cursor, _historyBytes;
    private ulong _knownRevision;
    private static readonly JsonSerializerOptions OutputJson = new(SceneJson.Options) { IgnoreReadOnlyProperties = false };

    public EditSession(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        world.VerifyAccess();
        _world = world;
        _knownRevision = world.Revision;
        SessionId = Guid.NewGuid();
        _capabilities = BuildCapabilities().ToDictionary(c => c.Name, StringComparer.Ordinal);
    }
    public Guid SessionId { get; }
    public ulong Revision => _world.Revision;
    public IReadOnlyList<CapabilityDescriptor> Describe()
    {
        _world.VerifyAccess();
        return _capabilities.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ToArray();
    }

    public CapabilityResult Invoke(CapabilityRequest request, CapabilityPermissions? permissions = null)
    {
        _world.VerifyAccess(); // Wrong-thread access is a host error, not an AI callback path.
        ArgumentNullException.ThrowIfNull(request);
        permissions ??= CapabilityPermissions.ReadOnly;
        try
        {
            if (request.ContractVersion != 1 || request.RequestId == Guid.Empty || request.SessionId != SessionId)
                return Result(request, "error", "invalid_envelope", false, new { message = "Contract/request/session identity mismatch." });
            if (!_capabilities.TryGetValue(request.Capability, out var capability))
                return Result(request, "error", "unknown_capability", false, new { message = "Capability is not implemented." });
            if (request.Input.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(request.Input.GetRawText()) > MaxInputBytes)
                throw new ArgumentException("Input must be a bounded JSON object.");
            using var boundedInput = JsonDocument.Parse(request.Input.GetRawText(), new JsonDocumentOptions { MaxDepth = 32 });
            SceneJson.RejectDuplicateFields(boundedInput.RootElement);
            if (capability.Risk != MutationRisk.ReadOnly && !permissions.Allows(request.Capability))
                return Result(request, "denied", "permission_denied", false, new { message = "Mutation permission is required." });
            string fingerprint = request.Capability + ":" + request.ExpectedRevision + ":" + request.Input.GetRawText();
            if (_cache.TryGetValue(request.RequestId, out var cached))
            {
                if (cached.Fingerprint != fingerprint) return Result(request, "conflict", "request_id_reused", false, new { });
                if (!permissions.Allows(cached.RequiredCapability, cached.Target)) return Result(request, "denied", "permission_denied", false, new { });
                return cached.Result; // Exact retries never reapply a successful command.
            }
            if (capability.Risk == MutationRisk.ReadOnly)
            {
                Closed(request.Input, [], []);
                if (request.ExpectedRevision is not null && request.ExpectedRevision != _world.Revision)
                    return Result(request, "conflict", "revision_conflict", false, new { });
                object data = request.Capability switch
                {
                    "ncma.capabilities.list" => new { capabilities = Describe().Select(c => new {
                        c.Name, c.Description, risk = RiskName(c.Risk), c.InputSchema, c.OutputSchema }) },
                    "ncma.engine.component_types" => new { components = _world.Components.Describe() },
                    "ncma.scene.inspect" => new { worldId = _world.Identity, tick = _world.Tick, scene = _world.CaptureSnapshot() },
                    "ncma.scene.validate" => ValidateScene(),
                    _ => throw new ArgumentException("Unknown inspection operation.")
                };
                return Result(request, "ok", "ok", false, data);
            }
            if (request.ExpectedRevision is null || request.ExpectedRevision != _world.Revision || _knownRevision != _world.Revision)
                return Result(request, "conflict", "revision_conflict", false, new { message = "Refresh state; outside mutations invalidate this session's history." });

            string required = request.Capability;
            Guid? target = null;
            CapabilityResult result;
            if (request.Capability is "ncma.history.undo" or "ncma.history.redo")
            {
                Closed(request.Input, [], []);
                bool undo = request.Capability == "ncma.history.undo";
                int index = undo ? _cursor - 1 : _cursor;
                if (index < 0 || index >= _history.Count) return Result(request, "error", "history_empty", false, new { });
                var entry = _history[index];
                if (!permissions.Allows(entry.Capability, entry.DestructiveTarget))
                    return Result(request, "denied", "permission_denied", false, new { });
                required = entry.Capability;
                target = entry.DestructiveTarget;
                _world.RestoreSnapshot(undo ? entry.Before : entry.After);
                _cursor += undo ? -1 : 1;
                _knownRevision = _world.Revision;
                result = Result(request, "ok", "ok", true, new { worldId = _world.Identity, undoCount = _cursor, redoCount = _history.Count - _cursor });
            }
            else
            {
                WorldSnapshot before = _world.CaptureSnapshot();
                var staged = new World(before.Name, _world.Components);
                staged.RestoreSnapshot(before);
                if (request.Capability == "ncma.scene.delete_object")
                {
                    Closed(request.Input, ["objectId"], ["objectId"]);
                    target = Uuid(request.Input, "objectId");
                    if (!permissions.Allows(request.Capability, target))
                        return Result(request, "denied", "target_not_authorized", false, new { });
                    staged.FindObject(target.Value).Destroy();
                }
                else
                {
                    Closed(request.Input, ["operations"], ["operations"]);
                    JsonElement operations = request.Input.GetProperty("operations");
                    if (operations.ValueKind != JsonValueKind.Array || operations.GetArrayLength() is < 1 or > MaxOperations)
                        throw new ArgumentException("Transaction requires 1..128 operations.");
                    foreach (var operation in operations.EnumerateArray()) Apply(staged, operation);
                }
                WorldSnapshot after = staged.CaptureSnapshot();
                int bytes = JsonSerializer.SerializeToUtf8Bytes(before, SceneJson.Options).Length +
                    JsonSerializer.SerializeToUtf8Bytes(after, SceneJson.Options).Length;
                if (bytes > MaxHistoryBytes) throw new ArgumentException("Transaction exceeds the undo budget.");
                if (_world.Revision != request.ExpectedRevision || _knownRevision != _world.Revision)
                    return Result(request, "conflict", "revision_conflict", false, new { message = "World changed while validating the candidate." });
                // Restore validates the whole candidate before any live mutation. One live revision per transaction.
                _world.RestoreSnapshot(after);
                for (int i = _history.Count - 1; i >= _cursor; --i) { _historyBytes -= _history[i].Bytes; _history.RemoveAt(i); }
                _history.Add(new(before, after, request.Capability, target, bytes));
                _historyBytes += bytes;
                _cursor++;
                while (_history.Count > MaxHistoryEntries || _historyBytes > MaxHistoryBytes)
                {
                    _historyBytes -= _history[0].Bytes;
                    _history.RemoveAt(0);
                    _cursor--;
                }
                _knownRevision = _world.Revision;
                result = Result(request, "ok", "ok", true, new { worldId = _world.Identity, undoCount = _cursor, redoCount = 0 });
            }
            _cache.Add(request.RequestId, new(fingerprint, result, required, target));
            _cacheOrder.Enqueue(request.RequestId);
            if (_cacheOrder.Count > 128) _cache.Remove(_cacheOrder.Dequeue());
            return result;
        }
        catch (Exception error) when (error is ArgumentException or JsonException or KeyNotFoundException or InvalidOperationException or OverflowException or FormatException)
        {
            return Result(request, "error", "invalid_input", false, new { message = error.Message });
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Trusted extension validator failures remain structured; fatal memory exhaustion is not hidden.
            return Result(request, "error", "operation_failed", false, new { message = error.Message });
        }
    }

    private object ValidateScene()
    {
        var candidate = new World("Validation", _world.Components);
        candidate.RestoreSnapshot(_world.CaptureSnapshot());
        return new { valid = true, objectCount = candidate.Count };
    }
    private static void Apply(World staged, JsonElement operation)
    {
        if (operation.ValueKind != JsonValueKind.Object) throw new ArgumentException("Operation must be an object.");
        string op = Text(operation, "op");
        switch (op)
        {
            case "create":
                Closed(operation, ["op", "objectId", "name"], ["op", "objectId", "name"]);
                staged.CreateObject(Text(operation, "name"), Uuid(operation, "objectId"));
                break;
            case "rename":
                Closed(operation, ["op", "objectId", "name"], ["op", "objectId", "name"]);
                staged.FindObject(Uuid(operation, "objectId")).Name = Text(operation, "name");
                break;
            case "set_component":
                Closed(operation, ["op", "objectId", "typeId", "version", "data"], ["op", "objectId", "typeId", "version", "data"]);
                object value = staged.Components.Decode(new(Text(operation, "typeId"), operation.GetProperty("version").GetInt32(), operation.GetProperty("data")));
                staged.Set(staged.FindObject(Uuid(operation, "objectId")).Reference, value);
                break;
            default: throw new ArgumentException("Unsupported operation. Deletion uses its separately authorized capability.");
        }
    }

    private CapabilityResult Result(CapabilityRequest request, string status, string code, bool changed, object data) =>
        new(1, request.RequestId, SessionId, _world.Revision, status, code, changed, JsonSerializer.SerializeToElement(data, OutputJson));
    private static Guid Uuid(JsonElement data, string name) => data.GetProperty(name).TryGetGuid(out Guid id) && id != Guid.Empty
        ? id : throw new ArgumentException("Invalid UUID: " + name);
    private static string Text(JsonElement data, string name) => data.GetProperty(name).GetString() ?? throw new ArgumentException("Missing string: " + name);
    private static void Closed(JsonElement data, string[] allowed, string[] required)
    {
        if (data.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected object input.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in data.EnumerateObject())
            if (!seen.Add(property.Name) || !allowed.Contains(property.Name, StringComparer.Ordinal)) throw new ArgumentException("Unknown or duplicate field: " + property.Name);
        foreach (string name in required) if (!seen.Contains(name)) throw new ArgumentException("Missing field: " + name);
    }
    private static string RiskName(MutationRisk risk) => risk switch
    { MutationRisk.ReadOnly => "read_only", MutationRisk.Reversible => "reversible", _ => "destructive" };
    private static JsonElement Json(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }

    private static IEnumerable<CapabilityDescriptor> BuildCapabilities()
    {
        const string empty = """{"type":"object","additionalProperties":false,"properties":{}}""";
        const string result = """
            {"type":"object","additionalProperties":false,
             "required":["contractVersion","requestId","sessionId","revision","status","code","changed","data"],
             "properties":{"contractVersion":{"const":1},"requestId":{"type":"string","format":"uuid"},
             "sessionId":{"type":"string","format":"uuid"},"revision":{"type":"integer","minimum":0},
             "status":{"enum":["ok","error","conflict","denied"]},"code":{"type":"string"},
             "changed":{"type":"boolean"},"data":{"type":"object"}}}
            """;
        CapabilityDescriptor Descriptor(string name, string description, MutationRisk risk, string input) =>
            new(name, description, risk, Json(input), Json(result));
        yield return Descriptor("ncma.capabilities.list", "Inspect implemented headless capabilities and schemas.", MutationRisk.ReadOnly, empty);
        yield return Descriptor("ncma.engine.component_types", "Inspect trusted registered component identities, versions and schemas.", MutationRisk.ReadOnly, empty);
        yield return Descriptor("ncma.scene.inspect", "Read managed scene UUIDs, components and world identity without mutation.", MutationRisk.ReadOnly, empty);
        yield return Descriptor("ncma.scene.validate", "Validate the managed scene without changing it.", MutationRisk.ReadOnly, empty);
        yield return Descriptor("ncma.scene.transaction", "Atomically create/rename objects and set registered components through undoable commands.", MutationRisk.Reversible, """
            {"type":"object","additionalProperties":false,"required":["operations"],"properties":{
            "operations":{"type":"array","minItems":1,"maxItems":128,"items":{"oneOf":[
              {"type":"object","additionalProperties":false,"required":["op","objectId","name"],"properties":{
                "op":{"enum":["create","rename"]},"objectId":{"type":"string","format":"uuid"},"name":{"type":"string","minLength":1,"maxLength":256}}},
              {"type":"object","additionalProperties":false,"required":["op","objectId","typeId","version","data"],"properties":{
                "op":{"const":"set_component"},"objectId":{"type":"string","format":"uuid"},"typeId":{"type":"string","maxLength":128},
                "version":{"type":"integer","minimum":1},"data":{"type":"object"}}}]}}}}
            """);
        yield return Descriptor("ncma.scene.delete_object", "Delete one explicitly approved object UUID; deletion is undoable.", MutationRisk.Destructive,
            """{"type":"object","additionalProperties":false,"required":["objectId"],"properties":{"objectId":{"type":"string","format":"uuid"}}}""");
        yield return Descriptor("ncma.history.undo", "Undo one command; original capability permissions still apply.", MutationRisk.Reversible, empty);
        yield return Descriptor("ncma.history.redo", "Redo one command; original capability and destructive target permissions still apply.", MutationRisk.Reversible, empty);
    }
}
