using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ncma.Animation;

public static class AnimationGraphCodec
{
    public const string Extension = ".ncmaanim";
    public const int CurrentVersion=4,MaxEvents=4096,MaxBlendSpaces=16,MaxLayers=16,MaxClipDependencies=128;
    public const int MaxPlanInstructions=3*MaxNodes+1+10*MaxBlendSpaces+MaxLayers;
    public const int MaxBytes = 1024 * 1024, MaxNodes = 256, MaxLinks = 1024,
        MaxParameters = 64, MaxStates = 64, MaxTransitions = 256, MaxConditions = 8;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16, Converters = { new ExactEnum<AnimationNodeKind>(), new ExactEnum<AnimationParameterKind>(),
            new ExactEnum<AnimationComparison>() }
    };
    public static void RequireExtension(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0') ||
            !string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Animation graphs require .ncmaanim, not a legacy graph/scene format.");
    }
    public static byte[] Encode(AnimationGraphDefinition definition)
    {
        AnimationGraphValidation.Validate(definition);
        // Stable bytes independent of collection enumeration order. All references use persistent UUIDs.
        var canonical = definition with {
            Events=definition.Events.OrderBy(e=>e.Id).ToArray(),Parameters = definition.Parameters.OrderBy(p => p.Id).ToArray(), Nodes = definition.Nodes.OrderBy(n => n.Id).Select(CanonicalNode).ToArray(),
            Links = definition.Links.OrderBy(l => l.Id).ToArray(), States = definition.States.OrderBy(s => s.Id).ToArray(),
            Transitions = definition.Transitions.OrderBy(t => t.Id).Select(t => t with {
                Conditions = t.Conditions.OrderBy(c => c.ParameterId).ToArray() }).ToArray()
        };
        byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(canonical, Json);
        if (encoded.Length > MaxBytes) throw new ArgumentException("Animation graph byte budget.");
        return encoded;
    }
    private static AnimationGraphNode CanonicalNode(AnimationGraphNode n)=>n with{
        BlendSpace=n.BlendSpace is{} s?s with{Samples=s.Samples.OrderBy(p=>p.Id).ToArray()}:null,
        Layer=n.Layer is{} l?l with{Mask=l.Mask with{Bones=l.Mask.Bones.OrderBy(b=>b.BonePath,StringComparer.Ordinal).ToArray()}}:null};
    public static AnimationGraphDefinition Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 2 or > MaxBytes) throw new ArgumentException("Animation graph byte budget.");
        _ = Utf8.GetCharCount(bytes);
        using var parsed = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
        Visit(parsed.RootElement);
        var definition = JsonSerializer.Deserialize<AnimationGraphDefinition>(bytes, Json)
            ?? throw new ArgumentException("Animation graph required.");
        AnimationGraphValidation.Validate(definition); return definition;
    }
    private static void Visit(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object) {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in value.EnumerateObject()) {
                if (!names.Add(p.Name)) throw new ArgumentException("Duplicate animation graph property."); Visit(p.Value);
            }
        } else if (value.ValueKind == JsonValueKind.Array) foreach (var child in value.EnumerateArray()) Visit(child);
    }
    internal static void Text(string value, int limit = 128)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > limit || value.Any(char.IsControl))
            throw new ArgumentException("Animation graph text budget/control character.");
        _ = Utf8.GetByteCount(value); // Reject unpaired UTF-16 surrogates in public DTOs too.
    }
    internal static void Scalar(double value, double min, double max)
    { if (!double.IsFinite(value) || value < min || value > max) throw new ArgumentException("Animation graph finite scalar range."); }
    private sealed class ExactEnum<T> : JsonConverter<T> where T : struct, Enum
    {
        private static readonly Dictionary<string, T> Values = Enum.GetValues<T>()
            .ToDictionary(v => JsonNamingPolicy.CamelCase.ConvertName(v.ToString()), v => v, StringComparer.Ordinal);
        public override T Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String || !Values.TryGetValue(reader.GetString()!, out T value))
                throw new JsonException("Exact supported camelCase animation enum required.");
            return value;
        }
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (!Enum.IsDefined(value)) throw new JsonException("Unsupported animation enum.");
            writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
        }
    }
}
