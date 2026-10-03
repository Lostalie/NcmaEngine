using System.Text.Json;
using Ncma.Scene;
namespace Ncma.Editor.Core;

public sealed partial class EditSession
{
    public ulong DocumentGeneration { get; private set; } = 1;
    private static CommandImpact Impact(byte[] beforeBytes, byte[] afterBytes, Guid? beforeSelection, Guid? afterSelection, bool replacement)
    {
        var before = SceneDocumentCodec.Decode(beforeBytes).Objects.ToDictionary(o => o.Id);
        var after = SceneDocumentCodec.Decode(afterBytes).Objects.ToDictionary(o => o.Id);
        var objects = new HashSet<Guid>(); var components = new HashSet<string>(StringComparer.Ordinal); var bindings = new HashSet<Guid>();
        string Encode(object? value) => value is null ? "" : JsonSerializer.Serialize(value, OutputJson);
        foreach (Guid id in before.Keys.Union(after.Keys))
        {
            var old = before.GetValueOrDefault(id); var current = after.GetValueOrDefault(id);
            if (Encode(old) == Encode(current)) continue;
            objects.Add(id);
            var oldComponents = (old?.Components ?? []).ToDictionary(c => c.TypeId, StringComparer.Ordinal);
            var newComponents = (current?.Components ?? []).ToDictionary(c => c.TypeId, StringComparer.Ordinal);
            foreach (string type in oldComponents.Keys.Union(newComponents.Keys))
                if (Encode(oldComponents.GetValueOrDefault(type)) != Encode(newComponents.GetValueOrDefault(type))) components.Add(type);
            var oldBindings = (old?.Behaviours ?? []).ToDictionary(b => b.Id);
            var newBindings = (current?.Behaviours ?? []).ToDictionary(b => b.Id);
            foreach (Guid binding in oldBindings.Keys.Union(newBindings.Keys))
                if (Encode(oldBindings.GetValueOrDefault(binding)) != Encode(newBindings.GetValueOrDefault(binding))) bindings.Add(binding);
        }
        if (beforeSelection != afterSelection) { if (beforeSelection is Guid a) objects.Add(a); if (afterSelection is Guid b) objects.Add(b); }
        return new(objects.Order().ToArray(), after.Keys.Except(before.Keys).Order().ToArray(), components.Order(StringComparer.Ordinal).ToArray(), bindings.Order().ToArray(), replacement);
    }
    private static void AuthorizeIntent(CapabilityPermissions permissions, JsonElement operation)
    {
        Guid id = Uuid(operation, "objectId"); string op = Text(operation, "op");
        if (!permissions.ObjectAllowed(id, op == "create")) throw new EditRejectedException("scope_denied");
        string[] componentTypes = operation.TryGetProperty("typeId", out var component) ? [component.GetString() ?? ""] : [];
        Guid[] bindings = operation.TryGetProperty("bindingId", out _) ? [Uuid(operation, "bindingId")] :
            operation.TryGetProperty("binding", out var binding) ? [Uuid(binding, "id")] : [];
        if (!permissions.InScope(new([id], op == "create" ? [id] : [], componentTypes, bindings, false))) throw new EditRejectedException("scope_denied");
    }
    private static CommandImpact RequestedImpact(CapabilityRequest request, CommandImpact actual)
    {
        if (request.Capability != "ncma.scene.transaction" || !request.Input.TryGetProperty("operations", out var ops)) return actual;
        var objects = new HashSet<Guid>(actual.Objects); var created = new HashSet<Guid>(actual.CreatedObjects);
        var components = new HashSet<string>(actual.ComponentTypes, StringComparer.Ordinal); var bindings = new HashSet<Guid>(actual.Bindings);
        foreach (var op in ops.EnumerateArray())
        {
            Guid id = Uuid(op, "objectId"); objects.Add(id); if (Text(op, "op") == "create") created.Add(id);
            if (op.TryGetProperty("typeId", out var type)) components.Add(type.GetString()!);
            if (op.TryGetProperty("bindingId", out _)) bindings.Add(Uuid(op, "bindingId"));
            if (op.TryGetProperty("binding", out var binding)) bindings.Add(Uuid(binding, "id"));
            if (op.TryGetProperty("bindings", out var all)) foreach (var value in all.EnumerateArray()) bindings.Add(Uuid(value, "id"));
        }
        if (request.Input.TryGetProperty("selection", out var selected) && selected.ValueKind != JsonValueKind.Null) objects.Add(Uuid(request.Input, "selection"));
        return new(objects.Order().ToArray(), created.Order().ToArray(), components.Order(StringComparer.Ordinal).ToArray(), bindings.Order().ToArray(), actual.DocumentReplacement);
    }
    public (CommandImpact Impact, string OriginalCapability, Guid? DestructiveTarget) DescribeRequestImpact(CapabilityRequest request)
    {
        Observe(); CheckInput(request.Input);
        if (_invoking || _draft is not null || _frozen || _invalidated) throw new EditRejectedException("edit_busy");
        if (request.ContractVersion != ContractVersion || request.SessionId != SessionId || request.ExpectedRevision != Revision)
            throw new EditRejectedException("revision_conflict");
        if (request.Capability is "ncma.history.undo" or "ncma.history.redo")
        {
            Closed(request.Input, [], []);
            int index = request.Capability == "ncma.history.undo" ? _cursor - 1 : _cursor;
            if (index < 0 || index >= _history.Length) throw new EditRejectedException("history_empty");
            var entry = _history[index];
            if (entry.Impact.DocumentReplacement) throw new EditRejectedException("history_scope_denied");
            return (entry.Impact with { Objects = (Guid[])entry.Impact.Objects.Clone(), CreatedObjects = (Guid[])entry.Impact.CreatedObjects.Clone(),
                ComponentTypes = (string[])entry.Impact.ComponentTypes.Clone(), Bindings = (Guid[])entry.Impact.Bindings.Clone() }, entry.Capability, entry.Target);
        }
        byte[] before = _document.CaptureBytes(); var candidate = SceneDocumentCodec.Decode(before); Guid? target = null; Guid? selection = _selection;
        if (request.Capability == "ncma.scene.delete_object")
        {
            Closed(request.Input, ["objectId"], ["objectId"]); target = Uuid(request.Input, "objectId");
            if (!Exists(candidate, target.Value)) throw new ArgumentException("Unknown object.");
            candidate = candidate with { Objects = candidate.Objects.Where(o => o.Id != target.Value).ToArray() };
            if (selection == target) selection = null;
        }
        else if (request.Capability == "ncma.scene.transaction")
        {
            Closed(request.Input, ["operations", "label", "selection"], ["operations"]);
            var ops = request.Input.GetProperty("operations"); if (ops.GetArrayLength() is < 1 or > MaxOperations) throw new ArgumentException("Invalid operation count.");
            foreach (var op in ops.EnumerateArray()) candidate = Apply(candidate, op);
            if (request.Input.TryGetProperty("selection", out var chosen)) selection = chosen.ValueKind == JsonValueKind.Null ? null : Uuid(request.Input, "selection");
        }
        else throw new ArgumentException("Not a mutation proposal.");
        byte[] after = [];
        _ = _document.PrepareRestore(candidate, request.ExpectedRevision, normalized => after = SceneDocumentCodec.Encode(normalized));
        return (RequestedImpact(request, Impact(before, after, _selection, selection, false)), request.Capability, target);
    }
    public bool CachedRequestExists(Guid id) { _document.VerifyAccess(); return _cache.ContainsKey(id); }
}
