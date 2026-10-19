using System.Text;
using System.Text.Json;

namespace Ncma.Runtime;

// Precomputed checks for the existing closed-object/scalar schema subset, not a new schema
// dialect. Unsupported, malformed, large or cyclic schemas retain the original checker.
// The immutable plan caches schema structure only: EVERY payload is still inspected.
internal sealed class ComponentShape
{
    private enum Kind { Object, Number, Integer, String, Boolean }
    private readonly record struct Field(byte[] Name, ComponentShape Shape);
    private readonly Kind _kind;
    private readonly Field[] _fields;
    private readonly ulong _required;
    private ComponentShape(Kind kind, Field[] fields, ulong required)
    { _kind = kind; _fields = fields; _required = required; }

    internal static ComponentShape? TryCreate(JsonElement schema)
    {
        int budget = 4096;
        try { return Compile(schema, schema, 0, ref budget); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException)
        { return null; }
    }
    private static ComponentShape? Compile(JsonElement schema, JsonElement root, int depth, ref int budget)
    {
        if (depth > 32 || --budget < 0) return null;
        if (schema.TryGetProperty("$ref", out var reference))
        {
            string path = reference.GetString() ?? "";
            return path.StartsWith("#/$defs/", StringComparison.Ordinal)
                ? Compile(root.GetProperty("$defs").GetProperty(path[8..]), root, depth + 1, ref budget) : null;
        }
        Kind? kind = schema.GetProperty("type").GetString() switch
        {
            "object" => Kind.Object, "number" => Kind.Number, "integer" => Kind.Integer,
            "string" => Kind.String, "boolean" => Kind.Boolean, _ => null
        };
        if (kind is null) return null;
        if (kind != Kind.Object) return new(kind.Value, [], 0);
        var fields = new List<Field>(); var names = new List<string>();
        foreach (var property in schema.GetProperty("properties").EnumerateObject())
        {
            // Small closed schemas get allocation-free matching; large ones use the old checker.
            if (fields.Count == 32 || names.Contains(property.Name, StringComparer.Ordinal)) return null;
            var child = Compile(property.Value, root, depth + 1, ref budget);
            if (child is null) return null;
            names.Add(property.Name); fields.Add(new(Encoding.UTF8.GetBytes(property.Name), child));
        }
        ulong required = 0;
        if (schema.TryGetProperty("required", out var array))
            foreach (var value in array.EnumerateArray())
            {
                int index = names.IndexOf(value.GetString()!);
                if (index < 0) return null;
                required |= 1UL << index;
            }
        return new(kind.Value, fields.ToArray(), required);
    }
    internal void Check(JsonElement data)
    {
        bool valid = _kind switch
        {
            Kind.Object => data.ValueKind == JsonValueKind.Object,
            Kind.Number => data.ValueKind == JsonValueKind.Number && data.TryGetDouble(out double number) && double.IsFinite(number),
            Kind.Integer => data.ValueKind == JsonValueKind.Number && data.TryGetInt64(out _),
            Kind.String => data.ValueKind == JsonValueKind.String,
            Kind.Boolean => data.ValueKind is JsonValueKind.True or JsonValueKind.False,
            _ => false
        };
        if (!valid) throw new ArgumentException("Component field does not match its schema.");
        if (_kind != Kind.Object) return;
        ulong seen = 0;
        foreach (var property in data.EnumerateObject())
        {
            int index = 0;
            while (index < _fields.Length && !property.NameEquals(_fields[index].Name)) index++;
            if (index == _fields.Length || (seen & (1UL << index)) != 0)
                throw new ArgumentException("Unknown or duplicate component field: " + property.Name);
            seen |= 1UL << index;
            _fields[index].Shape.Check(property.Value);
        }
        if ((seen & _required) != _required) throw new ArgumentException("Missing component field.");
    }
}
