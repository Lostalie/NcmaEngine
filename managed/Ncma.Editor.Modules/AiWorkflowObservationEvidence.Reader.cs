using System.Text.Json;
using System.Text.Json.Serialization;
using Ncma.Modules;

namespace Ncma.Editor.Modules;

public static partial class AiWorkflowObservationEvidence
{
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 12, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static byte[] EncodeDocument(Document document, int maximumBytes = MaximumBytes)
    {
        Limit(maximumBytes); var canonical = ValidateDocument(document, true);
        using var output = new BoundedOutput(maximumBytes);
        JsonSerializer.Serialize(output, canonical, Options); return output.Copy();
    }
    public static Document Decode(ReadOnlySpan<byte> bytes, int maximumBytes = MaximumBytes)
    {
        Limit(maximumBytes);
        if (bytes.Length == 0 || bytes.Length > maximumBytes) throw new ArgumentException("workflow_observation_byte_budget");
        byte[] owned = bytes.ToArray(); // One snapshot for shape, typed decode, replay and canonical comparison.
        try {
            InspectShape(owned);
            var document = JsonSerializer.Deserialize<Document>(owned, Options) ?? throw new ArgumentException("workflow_observation_null");
            var canonical = ValidateDocument(document, true);
            if (!owned.AsSpan().SequenceEqual(EncodeDocument(canonical, maximumBytes))) throw new ArgumentException("workflow_observation_noncanonical");
            return canonical;
        } catch (JsonException) { throw new ArgumentException("workflow_observation_json"); }
    }
    public static Document DecodeExpected(ReadOnlySpan<byte> bytes, ObservationIdentity expected, AiAuthoringIntent intent,
        int maximumBytes = MaximumBytes)
    {
        ArgumentNullException.ThrowIfNull(expected); _ = new ModuleObservationCapture(expected, 0, 1);
        if (!Enum.IsDefined(intent)) throw new ArgumentException("workflow_observation_expected_intent");
        var document = Decode(bytes, maximumBytes);
        if (document.Identity != expected || document.Intent != intent) throw new ArgumentException("workflow_observation_expected_identity");
        return document;
    }
    private static void Limit(int value)
    { if (value is < 1 or > MaximumBytes) throw new ArgumentOutOfRangeException(nameof(value)); }
    private static void InspectShape(ReadOnlySpan<byte> bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 12 });
        var names = new HashSet<string>?[13]; var property = new string?[13];
        var arrays = new int[13]; var counts = new int[13];
        while (reader.Read()) {
            int depth = reader.CurrentDepth;
            if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName && reader.ValueSpan.Length > 512 ||
                reader.TokenType == JsonTokenType.Number && reader.ValueSpan.Length > 32)
                throw new ArgumentException("workflow_observation_scalar_budget");
            if (depth > 0 && arrays[depth - 1] != 0 && reader.TokenType is not JsonTokenType.EndObject and not JsonTokenType.EndArray &&
                (reader.TokenType != JsonTokenType.StartObject || ++counts[depth - 1] > arrays[depth - 1]))
                throw new ArgumentException("workflow_observation_array_budget");
            switch (reader.TokenType) {
                case JsonTokenType.StartObject: names[depth] = new(StringComparer.Ordinal); break;
                case JsonTokenType.EndObject: names[depth] = null; break;
                case JsonTokenType.PropertyName:
                    string name = reader.GetString()!; var set = depth > 0 ? names[depth - 1] : null;
                    if (set is null || set.Count == 32 || !set.Add(name)) throw new ArgumentException("workflow_observation_members");
                    property[depth] = name; break;
                case JsonTokenType.StartArray:
                    arrays[depth] = (depth, property[depth]) switch {
                        (1, "Requests") => AiWorkflowObservations.MaximumRequests,
                        (1, "Events") => AiWorkflowObservations.MaximumEvents,
                        (2, "Samples") when property[1] == "Transport" => 4096,
                        (4, "Events") when property[1] == "Requests" && property[3] == "Module" => AiWorkflowObservations.ModuleEventCapacity,
                        (4, "Latencies") when property[1] == "Requests" && property[3] == "Module" => 42,
                        _ => throw new ArgumentException("workflow_observation_array_shape")
                    };
                    counts[depth] = 0; break;
                case JsonTokenType.EndArray: arrays[depth] = 0; break;
            }
        }
    }
}
