using System.Text.Json;
using System.Text.Json.Nodes;
using Ncma.Runtime;
using Ncma.Scene;
namespace Ncma.Editor.Core;

public sealed partial class EditSession
{
    private void RequireBinding(BehaviourBindingData binding)
    {
        var type = _catalog?.Types.FirstOrDefault(t => t.TypeName == binding.TypeName) ?? throw new EditRejectedException("catalog_unavailable");
        foreach (var value in binding.Exports)
        {
            var export = type.Exports.FirstOrDefault(e => e.Name == value.Name && e.Kind == (uint)value.Kind) ?? throw new ArgumentException("Unknown or changed Export.");
            if (!double.IsFinite(value.Value) || export.Kind == 1 && !float.IsFinite((float)value.Value) ||
                export.Kind == 3 && (value.Value != Math.Truncate(value.Value) || value.Value < int.MinValue || value.Value > int.MaxValue) ||
                export.Kind == 4 && value.Value is not (0 or 1)) throw new ArgumentException("Invalid Export value.");
        }
    }
    private SceneObjectData BindingOperation(SceneObjectData obj, JsonElement operation, string op)
    {
        switch (op)
        {
            case "add_binding":
                Closed(operation, ["op", "objectId", "binding"], ["op", "objectId", "binding"]);
                var added = operation.GetProperty("binding").Deserialize<BehaviourBindingData>(SceneJson.Options) ?? throw new ArgumentException("Missing binding.");
                RequireBinding(added);
                if (obj.Behaviours.Any(b => b.Id == added.Id)) throw new ArgumentException("Duplicate binding.");
                return obj with { Behaviours = [.. obj.Behaviours, added] };
            case "remove_binding":
                Closed(operation, ["op", "objectId", "bindingId"], ["op", "objectId", "bindingId"]);
                Guid removed = Uuid(operation, "bindingId");
                if (!obj.Behaviours.Any(b => b.Id == removed)) throw new ArgumentException("Unknown binding.");
                return obj with { Behaviours = obj.Behaviours.Where(b => b.Id != removed).ToArray() };
            case "set_binding_enabled":
                Closed(operation, ["op", "objectId", "bindingId", "enabled"], ["op", "objectId", "bindingId", "enabled"]);
                Guid enabledId = Uuid(operation, "bindingId");
                var before = obj.Behaviours.FirstOrDefault(b => b.Id == enabledId) ?? throw new ArgumentException("Unknown binding.");
                bool enabled = operation.GetProperty("enabled").GetBoolean();
                if (enabled) RequireBinding(before);
                return obj with { Behaviours = obj.Behaviours.Select(b => b.Id == enabledId ? b with { Enabled = enabled } : b).ToArray() };
            case "set_export":
                Closed(operation, ["op", "objectId", "bindingId", "name", "kind", "value"], ["op", "objectId", "bindingId", "name", "kind", "value"]);
                Guid bindingId = Uuid(operation, "bindingId"); string name = Text(operation, "name");
                var binding = obj.Behaviours.FirstOrDefault(b => b.Id == bindingId) ?? throw new ArgumentException("Unknown binding.");
                var old = binding.Exports.FirstOrDefault(e => e.Name == name) ?? throw new ArgumentException("Export configuration is not present.");
                var export = new ExportData(name, (ExportKind)operation.GetProperty("kind").GetUInt32(), operation.GetProperty("value").GetDouble());
                if (old.Kind != export.Kind) throw new ArgumentException("Export kind changed.");
                var updated = binding with { Exports = binding.Exports.Select(e => e.Name == name ? export : e).ToArray() }; RequireBinding(updated);
                return obj with { Behaviours = obj.Behaviours.Select(b => b.Id == bindingId ? updated : b).ToArray() };
            default: throw new ArgumentException("Unknown binding operation.");
        }
    }
    private static JsonElement TransactionSchema(string json)
    {
        var root = JsonNode.Parse(json)!; var variants = root["properties"]!["operations"]!["items"]!["oneOf"]!.AsArray();
        var binding = variants[3]!["properties"]!["bindings"]!["items"]!.DeepClone();
        JsonObject Variant(string op, params (string Name, JsonNode Schema)[] fields)
        {
            var properties = new JsonObject { ["op"] = new JsonObject { ["const"] = op }, ["objectId"] = JsonNode.Parse("""{"type":"string","format":"uuid"}""") };
            var required = new JsonArray("op", "objectId");
            foreach (var (name, schema) in fields) { properties[name] = schema; required.Add(name); }
            return new() { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties, ["required"] = required };
        }
        JsonNode UuidSchema() => JsonNode.Parse("""{"type":"string","format":"uuid"}""")!;
        variants.Add(Variant("add_binding", ("binding", binding)));
        variants.Add(Variant("remove_binding", ("bindingId", UuidSchema())));
        variants.Add(Variant("set_binding_enabled", ("bindingId", UuidSchema()), ("enabled", JsonNode.Parse("""{"type":"boolean"}""")!)));
        variants.Add(Variant("set_export", ("bindingId", UuidSchema()), ("name", JsonNode.Parse("""{"type":"string","minLength":1,"maxLength":128}""")!),
            ("kind", JsonNode.Parse("""{"enum":[1,2,3,4]}""")!), ("value", JsonNode.Parse("""{"type":"number"}""")!)));
        return JsonSerializer.SerializeToElement(root);
    }
}
