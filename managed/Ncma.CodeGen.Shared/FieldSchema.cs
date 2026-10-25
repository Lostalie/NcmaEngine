using System.Text.Json;

namespace Ncma.ComponentContracts;

// Linked source in compiler/runtime: one bounded schema subset, no runtime generator dependency.
internal static class FieldSchema
{
    internal const int MaxFields = 128, MaxPath = 256;
    internal static bool SupportedObject(string name) => !name.StartsWith("System.", StringComparison.Ordinal) ||
        name is "System.Numerics.Vector2" or "System.Numerics.Vector3" or "System.Numerics.Vector4" or
            "System.Numerics.Quaternion" or "System.Numerics.Matrix3x2" or "System.Numerics.Matrix4x4" or "System.Numerics.Plane";
    internal static void Definition(string id, string memberPath, string jsonPath, int flags)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 128 || id.Any(c => !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9') && c is not '.' and not '_' and not '-'))
            throw new ArgumentException("FieldId must be an explicit 1..128 ASCII identifier (letters/digits/._-).");
        foreach (var path in new[] { memberPath, jsonPath })
            if (string.IsNullOrEmpty(path) || path.Length > MaxPath || path.Split('.').Any(s => s.Length == 0 || s.Any(c => !char.IsLetterOrDigit(c) && c != '_')))
                throw new ArgumentException("Field path must contain bounded dot-separated member names.");
        // This version describes persisted component fields. It does not invent a second serializer.
        if ((flags & ~15) != 0 || (flags & 1) == 0 || ((flags & 8) != 0 && (flags & 4) == 0))
            throw new ArgumentException("Field flags require Persistent; RuntimeWrite requires RuntimeRead. Unknown flags rejected.");
    }

    internal static Dictionary<string, string> Flatten(JsonElement root)
    {
        Duplicates(root);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        Visit(root, "", 0);
        return fields;

        void Visit(JsonElement schema, string path, int depth)
        {
            if (depth > 32 || schema.ValueKind != JsonValueKind.Object) throw new ArgumentException("Invalid field schema depth/object.");
            if (schema.TryGetProperty("$ref", out var reference)) {
                string name = reference.GetString() ?? "";
                if (schema.EnumerateObject().Count() != 1 || !name.StartsWith("#/$defs/", StringComparison.Ordinal) ||
                    name.Length <= 8 || name.Substring(8).IndexOf('/') >= 0 || !root.TryGetProperty("$defs", out var defs) || !defs.TryGetProperty(name.Substring(8), out var target))
                    throw new ArgumentException("Only exact local $defs references without siblings are supported.");
                Visit(target, path, depth + 1); return;
            }
            string kind = schema.GetProperty("type").GetString() ?? "";
            if (kind is not ("object" or "number" or "integer" or "boolean" or "string")) throw new ArgumentException("Unsupported field schema kind.");
            foreach (var property in schema.EnumerateObject())
                if (property.Name is not ("type" or "title" or "description" or "$defs") &&
                    !(kind == "object" && property.Name is "properties" or "required" or "additionalProperties"))
                    throw new ArgumentException("Unsupported field schema keyword: " + property.Name);
            if (path.Length != 0) {
                if (path.Length > MaxPath || fields.Count >= MaxFields || fields.ContainsKey(path)) throw new ArgumentException("Field schema path/count budget exceeded.");
                fields.Add(path, kind);
            } else if (kind != "object") throw new ArgumentException("Component root must be object.");
            if (kind != "object") return;
            if (!schema.TryGetProperty("additionalProperties", out var extra) || extra.ValueKind != JsonValueKind.False ||
                !schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object ||
                !schema.TryGetProperty("required", out var required) || required.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("Field schema objects must be closed with explicit properties and required lists.");
            var names = new HashSet<string>(properties.EnumerateObject().Select(p => p.Name), StringComparer.Ordinal);
            var requiredNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in required.EnumerateArray())
                if (name.ValueKind != JsonValueKind.String || !requiredNames.Add(name.GetString()!)) throw new ArgumentException("Duplicate/invalid required field.");
            if (!names.SetEquals(requiredNames)) throw new ArgumentException("Every persisted field must be required, exactly once.");
            foreach (var property in properties.EnumerateObject()) {
                if (property.Name.Length == 0 || property.Name.Any(c => !char.IsLetterOrDigit(c) && c != '_')) throw new ArgumentException("Unsupported JSON field name.");
                Visit(property.Value, path.Length == 0 ? property.Name : path + "." + property.Name, depth + 1);
            }
        }
    }

    private static void Duplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object) {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in value.EnumerateObject()) {
                if (!names.Add(item.Name)) throw new ArgumentException("Duplicate schema member: " + item.Name);
                Duplicates(item.Value);
            }
        } else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Duplicates(item);
    }
}
