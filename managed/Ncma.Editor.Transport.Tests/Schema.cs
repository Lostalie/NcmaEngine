using System.Text.Json;
using Ncma.Editor.Protocol;
internal static class Schema
{
    // Test evaluator for the concrete output-schema vocabulary, not a production full JSON Schema implementation.
    internal static bool Accepts(JsonElement schema, JsonElement value)
    {
        if (schema.TryGetProperty("anyOf", out var alternatives) && !alternatives.EnumerateArray().Any(s => Accepts(s, value))) return false;
        if (schema.TryGetProperty("const", out var constant) && constant.GetRawText() != value.GetRawText()) return false;
        if (schema.TryGetProperty("enum", out var options) && !options.EnumerateArray().Any(o => o.GetRawText() == value.GetRawText())) return false;
        bool Is(string type) => type switch {
            "object" => value.ValueKind == JsonValueKind.Object, "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String, "null" => value.ValueKind == JsonValueKind.Null,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False, "number" => value.ValueKind == JsonValueKind.Number,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && decimal.Truncate(number) == number,
            _ => throw new Exception("Untested schema vocabulary: " + type) };
        if (schema.TryGetProperty("type", out var type) && !(type.ValueKind == JsonValueKind.Array ?
            type.EnumerateArray().Any(t => Is(t.GetString()!)) : Is(type.GetString()!))) return false;
        if (schema.TryGetProperty("format", out var format) && format.GetString() == "uuid" && !Guid.TryParse(value.GetString(), out _)) return false;
        if (value.ValueKind == JsonValueKind.Number && schema.TryGetProperty("minimum", out var minimum) && value.GetDouble() < minimum.GetDouble()) return false;
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var required) && required.EnumerateArray().Any(p => !value.TryGetProperty(p.GetString()!, out _))) return false;
            if (schema.TryGetProperty("properties", out var properties))
                foreach (var p in value.EnumerateObject())
                { if (properties.TryGetProperty(p.Name, out var child)) { if (!Accepts(child, p.Value)) return false; }
                  else if (schema.TryGetProperty("additionalProperties", out var extra) && extra.ValueKind == JsonValueKind.False) return false; }
        }
        if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
            if (!value.EnumerateArray().All(v => Accepts(items, v))) return false;
        return true;
    }
    internal static void Adversarial()
    {
        int rejected = 0;
        foreach (byte[] bytes in new[] { Wire.Utf8.GetBytes("{\"method\":\"session\",\"method\":\"invoke\"}"),
            Wire.Utf8.GetBytes("{\"method\":\"session\",\"unknown\":true}"), new byte[] { 0xff, 0xfe },
            Wire.Utf8.GetBytes(new string('[', 33) + "0" + new string(']', 33)) })
        { try { _ = Wire.Decode<IpcRequest>(bytes); } catch (Exception e) when (e is ArgumentException or JsonException) { rejected++; } }
        if (rejected != 4) throw new Exception("Hostile IPC input accepted");
        foreach (int length in new[] { -1, 0, Wire.MaxMessageBytes + 1 })
        {
            using var frame = new MemoryStream(BitConverter.GetBytes(length));
            try { Wire.ReadAsync(frame, CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("Invalid frame accepted"); }
            catch (ArgumentException) { }
        }
        Console.WriteLine("Strict duplicate/unknown-field/UTF8/depth/length protocol negatives passed.");
    }
}
