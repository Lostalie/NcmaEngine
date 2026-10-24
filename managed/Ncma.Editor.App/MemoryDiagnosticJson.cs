using System.Text.Json;
using System.Text.Json.Serialization;
using Ncma.Application;
using Ncma.Rendering;

namespace Ncma.Editor.App;

// Only invoked by the existing opt-in shutdown diagnostics. No capture, IO or GC policy here.
internal static class MemoryDiagnosticJson
{
    public static string Process(ProcessMemoryReport report) => JsonSerializer.Serialize(
        new ProcessMemoryEnvelope(report), MemoryDiagnosticJsonContext.Default.ProcessMemoryEnvelope);
    public static string Resource(RendererMemoryReport report) => JsonSerializer.Serialize(
        new RendererMemoryEnvelope(report), MemoryDiagnosticJsonContext.Default.RendererMemoryEnvelope);
}

internal sealed record ProcessMemoryEnvelope(
    [property: JsonPropertyName("memoryCheckpoints")] ProcessMemoryReport Report);
internal sealed record RendererMemoryEnvelope(
    [property: JsonPropertyName("resourceMemory")] RendererMemoryReport Report);

// Preserve Editor's original PascalCase inner properties, numeric enums and compact output.
[JsonSerializable(typeof(ProcessMemoryEnvelope))]
[JsonSerializable(typeof(RendererMemoryEnvelope))]
internal partial class MemoryDiagnosticJsonContext : JsonSerializerContext;
