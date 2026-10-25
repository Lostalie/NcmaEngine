using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ncma.Assets;
using Ncma.Runtime;
namespace Ncma.Scene.Rendering;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PostParameterInput([property: JsonRequired] string Name, [property: JsonRequired] float Value);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PostFeatureInput([property: JsonRequired] Guid ModuleId, [property: JsonRequired] int Version,
    [property: JsonRequired] PostParameterInput[] Parameters);

// World components retain immutable scalar/string values only. Structured authoring APIs encode
// the bounded stack; neither a mutable array nor a JsonDocument lifetime crosses into World storage.
public readonly record struct PostProcessData(int Version, bool Enabled, Guid ToneModule, int ToneVersion,
    float ExposureEv, string StackJson) : IComponent
{
    public const string TypeId = "ncma.render.post_process";
    public const int MaxStackBytes = 8192;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 8 };
    public static PostProcessData Off => new(1, false, PostProcessLibrarySelection.AcesModule, 1, 0, "[]");
    public static string EncodeStack(IEnumerable<PostFeatureInput> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        var owned = ValidateInputs(features.Take(9).ToArray());
        string text = JsonSerializer.Serialize(owned, Json);
        if (Encoding.UTF8.GetByteCount(text) > MaxStackBytes) throw new ArgumentException("Post stack byte budget.");
        return text;
    }
    public static PostFeatureInput[] DecodeStack(string text)
    {
        if (text is null || text.Length > MaxStackBytes || Encoding.UTF8.GetByteCount(text) > MaxStackBytes)
            throw new ArgumentException("Post stack byte budget.");
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
        Unique(document.RootElement);
        return ValidateInputs(JsonSerializer.Deserialize<PostFeatureInput[]>(text, Json) ?? throw new ArgumentException("Post stack required."));
    }
    private static PostFeatureInput[] ValidateInputs(PostFeatureInput[] values)
    {
        if (values.Length > 8) throw new ArgumentException("At most eight post Features.");
        var ids = new HashSet<Guid>(); var result = new PostFeatureInput[values.Length];
        for (int i = 0; i < values.Length; i++) {
            var value = values[i];
            if (value is null || value.ModuleId == Guid.Empty || value.Version < 1 || !ids.Add(value.ModuleId) || value.Parameters is null || value.Parameters.Length > 16)
                throw new ArgumentException("Post Feature identity/version/parameter budget.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in value.Parameters)
                if (parameter is null || !ValidName(parameter.Name) || !names.Add(parameter.Name) || !float.IsFinite(parameter.Value) || MathF.Abs(parameter.Value) > 65504)
                    throw new ArgumentException("Post parameter name/duplicate/finite range.");
            result[i] = value with { Parameters = value.Parameters.OrderBy(p => p.Name, StringComparer.Ordinal).ToArray() };
        }
        return result;
    }
    public static bool ValidName(string name) => !string.IsNullOrEmpty(name) && name.Length <= 48 && char.IsAsciiLetter(name[0]) && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    private static void Unique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object) { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var p in element.EnumerateObject()) { if (!names.Add(p.Name)) throw new ArgumentException("Duplicate post field."); Unique(p.Value); } }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var value in element.EnumerateArray()) Unique(value);
    }
    public static PostProcessData Validate(PostProcessData value)
    {
        if (value.Version != 1 || value.ToneModule == Guid.Empty || value.ToneVersion < 1 || !float.IsFinite(value.ExposureEv) || value.ExposureEv is < -6 or > 4)
            throw new ArgumentException("Post settings version/Tone/exposure.");
        return value with { StackJson = EncodeStack(DecodeStack(value.StackJson)) };
    }
    // Formal hosts consume this scene-wide value; gameplay cannot attach it during simulation.
    [ComponentRegistration]
    public static void Register(ComponentRegistry registry) => registry.Register<PostProcessData>(TypeId, 1, """
        {"type":"object","additionalProperties":false,"required":["version","enabled","toneModule","toneVersion","exposureEv","stackJson"],
         "properties":{"version":{"type":"integer"},"enabled":{"type":"boolean"},"toneModule":{"type":"string"},
         "toneVersion":{"type":"integer"},"exposureEv":{"type":"number"},"stackJson":{"type":"string"}}}
        """, Validate, runtimeAttachable:false);
}
