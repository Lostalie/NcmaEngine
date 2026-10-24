using System.Text.Json.Serialization;

namespace Ncma.Player.App;

// Output-only contract: do not eagerly construct unused deserialization/property metadata.
// Preserve the existing writer, options and report bytes; no reflection fallback or prewarming.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true, WriteIndented = true, GenerationMode = JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(PlayerReport))]
internal partial class PlayerReportJsonContext : JsonSerializerContext;
