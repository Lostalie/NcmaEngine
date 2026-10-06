using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ncma.Runtime;

public interface IComponent { }

public readonly record struct TransformData(Vector3 Position, Quaternion Rotation, Vector3 Scale) : IComponent
{
    public static TransformData Identity => new(Vector3.Zero, Quaternion.Identity, Vector3.One);

    public static TransformData Validate(TransformData value)
    {
        static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
        float length = value.Rotation.LengthSquared();
        if (!Finite(value.Position) || !Finite(value.Scale) || !float.IsFinite(length) || length < 1e-6f)
            throw new ArgumentException("Transform values must be finite and rotation must be nonzero.");
        return MathF.Abs(length - 1) <= 1e-6f ? value : value with { Rotation = Quaternion.Normalize(value.Rotation) };
    }
}

public sealed record ComponentDescriptor(string TypeId, int Version, JsonElement Schema);

// Explicit trusted registration, not arbitrary assembly/type loading from Agent input.
public sealed class ComponentRegistry
{
    public const int MaxPayloadBytes = 65536;
    private sealed record Registration(Type Type, ComponentDescriptor Descriptor,
        Func<object, object> Validate, Func<JsonElement, object> Decode, bool RuntimeAttachable);
    private readonly Dictionary<string, Registration> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, Registration> _byType = new();
    private bool _frozen;

    public static ComponentRegistry CreateDefault()
    {
        var registry = new ComponentRegistry();
        registry.Register<TransformData>("ncma.transform", 1, """
            {"type":"object","additionalProperties":false,"required":["position","rotation","scale"],
             "properties":{"position":{"$ref":"#/$defs/vector"},"rotation":{"$ref":"#/$defs/quaternion"},
             "scale":{"$ref":"#/$defs/vector"}},"$defs":{
             "vector":{"type":"object","additionalProperties":false,"required":["x","y","z"],
               "properties":{"x":{"type":"number"},"y":{"type":"number"},"z":{"type":"number"}}},
             "quaternion":{"type":"object","additionalProperties":false,"required":["x","y","z","w"],
               "properties":{"x":{"type":"number"},"y":{"type":"number"},"z":{"type":"number"},"w":{"type":"number"}}}}}
            """, TransformData.Validate);
        return registry;
    }

    public void Register<T>(string typeId, int version, string schema, Func<T, T> validate, bool runtimeAttachable = true) where T : struct, IComponent
    {
        if (_frozen) throw new InvalidOperationException("Register extensions before creating a World.");
        ArgumentNullException.ThrowIfNull(validate);
        if (string.IsNullOrWhiteSpace(typeId) || typeId.Length > 128 || version < 1)
            throw new ArgumentException("Component type identity/version is invalid.");
        if (_byId.ContainsKey(typeId) || _byType.ContainsKey(typeof(T)))
            throw new ArgumentException("Duplicate component registration.");
        VerifyValueType(typeof(T), new HashSet<Type>());
        if (System.Text.Encoding.UTF8.GetByteCount(schema) > MaxPayloadBytes)
            throw new ArgumentException("Component schema exceeds the size limit.");
        using var document = JsonDocument.Parse(schema, new JsonDocumentOptions { MaxDepth = 32 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Component schema must be an object.");
        var descriptor = new ComponentDescriptor(typeId, version, document.RootElement.Clone());
        var registration = new Registration(typeof(T), descriptor, value => validate((T)value),
            value => validate(value.Deserialize<T>(SceneJson.Options)), runtimeAttachable);
        _byId.Add(typeId, registration);
        _byType.Add(typeof(T), registration);
    }

    public IReadOnlyList<ComponentDescriptor> Describe() => _byId.Values.Select(r => r.Descriptor)
        .OrderBy(d => d.TypeId, StringComparer.Ordinal).ToArray();

    public JsonElement Encode<T>(T value) where T : struct, IComponent => EncodeObject(Validate(value));
    public T Decode<T>(ComponentSnapshot component) where T : struct, IComponent
    {
        ArgumentNullException.ThrowIfNull(component);
        return Decode(component) is T value ? value :
            throw new ArgumentException("Component does not match the requested registered value type.");
    }

    internal void Freeze() => _frozen = true;
    internal ComponentDescriptor Describe(Type type) => Require(type).Descriptor;
    // Trusted registration policy, not serialized metadata or an Agent-controlled flag.
    internal void VerifyRuntimeAttachment(Type type)
    {
        if (!Require(type).RuntimeAttachable) throw new InvalidOperationException("This component requires authoring/startup composition; runtime attachment is not supported.");
    }
    internal object Validate(object value)
    {
        object validated = Require(value.GetType()).Validate(value);
        CheckPayload(EncodeObject(validated));
        return validated;
    }
    internal JsonElement EncodeObject(object value) => JsonSerializer.SerializeToElement(value, value.GetType(), SceneJson.Options);
    internal object Decode(ComponentSnapshot component)
    {
        if (!_byId.TryGetValue(component.TypeId, out var registration) || component.Version != registration.Descriptor.Version)
            throw new ArgumentException("Unknown component type/version: " + component.TypeId);
        CheckPayload(component.Data);
        CheckShape(component.Data, registration.Descriptor.Schema, registration.Descriptor.Schema);
        object decoded = registration.Decode(component.Data);
        JsonElement normalized = EncodeObject(decoded);
        CheckPayload(normalized);
        CheckShape(normalized, registration.Descriptor.Schema, registration.Descriptor.Schema);
        return decoded;
    }

    // This slice supports closed object schemas, numeric/string/boolean fields and local $defs.
    // Full JSON Schema evaluation is not claimed.
    private static void CheckShape(JsonElement data, JsonElement schema, JsonElement root, int depth = 0)
    {
        if (depth > 32) throw new ArgumentException("Component schema nesting/reference limit exceeded.");
        if (schema.TryGetProperty("$ref", out var reference))
        {
            string path = reference.GetString() ?? "";
            if (!path.StartsWith("#/$defs/", StringComparison.Ordinal)) throw new ArgumentException("Unsupported schema reference.");
            CheckShape(data, root.GetProperty("$defs").GetProperty(path[8..]), root, depth + 1);
            return;
        }
        string kind = schema.GetProperty("type").GetString() ?? "";
        bool valid = kind switch
        {
            "object" => data.ValueKind == JsonValueKind.Object,
            "number" => data.ValueKind == JsonValueKind.Number && data.TryGetDouble(out double n) && double.IsFinite(n),
            "integer" => data.ValueKind == JsonValueKind.Number && data.TryGetInt64(out _),
            "string" => data.ValueKind == JsonValueKind.String,
            "boolean" => data.ValueKind is JsonValueKind.True or JsonValueKind.False,
            _ => throw new ArgumentException("Unsupported component schema type: " + kind)
        };
        if (!valid) throw new ArgumentException("Component field does not match its schema.");
        if (kind != "object") return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        JsonElement properties = schema.GetProperty("properties");
        foreach (var property in data.EnumerateObject())
        {
            if (!seen.Add(property.Name) || !properties.TryGetProperty(property.Name, out var field))
                throw new ArgumentException("Unknown or duplicate component field: " + property.Name);
            CheckShape(property.Value, field, root, depth + 1);
        }
        if (schema.TryGetProperty("required", out var required))
            foreach (var field in required.EnumerateArray())
                if (!seen.Contains(field.GetString()!)) throw new ArgumentException("Missing component field: " + field.GetString());
    }

    private Registration Require(Type type) => _byType.TryGetValue(type, out var value) ? value :
        throw new ArgumentException("Component type is not registered: " + type.Name);
    private static void CheckPayload(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || System.Text.Encoding.UTF8.GetByteCount(data.GetRawText()) > MaxPayloadBytes)
            throw new ArgumentException("Component payload must be a bounded object.");
    }
    private static void VerifyValueType(Type type, HashSet<Type> visited)
    {
        if (type == typeof(IntPtr) || type == typeof(UIntPtr) || type.IsPointer || type.IsFunctionPointer)
            throw new ArgumentException("Components cannot retain native addresses.");
        if (type == typeof(string) || type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(Guid)) return;
        if (!type.IsValueType || type.IsPointer || type.IsByRefLike) throw new ArgumentException("Components cannot retain mutable reference objects or pointers.");
        if (!visited.Add(type)) return;
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            VerifyValueType(field.FieldType, visited);
    }
}

internal static class SceneJson
{
    internal static byte[] EncodeBounded<T>(T value, int maximum)
    {
        using var output = new BoundedBuffer(maximum);
        JsonSerializer.Serialize(output, value, Options);
        return output.ToArray();
    }
    private sealed class BoundedBuffer(int maximum) : MemoryStream
    {
        private void CheckSize(int count)
        {
            if (count > maximum - Position) throw new ArgumentException("Snapshot size limit exceeded.");
        }
        public override void Write(byte[] buffer, int offset, int count) { CheckSize(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { CheckSize(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { CheckSize(1); base.WriteByte(value); }
    }
    internal static void RejectDuplicateFields(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ArgumentException("Duplicate JSON field: " + property.Name);
                RejectDuplicateFields(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateFields(item);
    }
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
        IgnoreReadOnlyProperties = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };
}
