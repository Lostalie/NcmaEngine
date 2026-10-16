using System.Text;
using System.Text.Json;

namespace Ncma.Modules.Grpc;

/// <summary>A real deterministic Python tool, not an inference model or editor capability.</summary>
public static class TextStatisticsModule
{
    public static ModuleOperation Operation { get; } = new(
        Guid.Parse("cde08200-1001-4001-8001-000000000001"), "1.0",
        Guid.Parse("cde08200-1001-4001-8001-000000000004"), "ncma.tools.text_stats",
        Guid.Parse("cde08200-1001-4001-8001-000000000002"),
        Guid.Parse("cde08200-1001-4001-8001-000000000003"));
}

internal static class WorkerWire
{
    internal const int MaximumBytes = 131_072;
    internal const string Service = "ncma.modules.v1.Worker";

    // Custom gRPC marshalling: the message body is strict UTF-8 JSON, NOT protobuf/ProtoJSON.
    // This avoids protobuf's merge/unknown-field semantics; it is still HTTP/2 gRPC framing.
    internal static byte[] Encode(object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > MaximumBytes) throw Invalid();
        return bytes;
    }

    internal static JsonDocument Parse(byte[] bytes, params string[] fields)
    {
        if (bytes.Length is < 2 or > MaximumBytes) throw Invalid();
        _ = new UTF8Encoding(false, true).GetCharCount(bytes);
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        try
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Invalid();
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!fields.Contains(property.Name, StringComparer.Ordinal) || !found.Add(property.Name)) throw Invalid();
            if (found.Count != fields.Length) throw Invalid();
            return document;
        }
        catch { document.Dispose(); throw; }
    }

    internal static string Text(JsonElement root, string field, int maximum)
    {
        var value = root.GetProperty(field);
        if (value.ValueKind != JsonValueKind.String) throw Invalid();
        var text = value.GetString()!;
        if (text.Length > maximum) throw Invalid();
        _ = new UTF8Encoding(false, true).GetByteCount(text);
        return text;
    }

    internal static Guid Identity(JsonElement root, string field)
    {
        var text = Text(root, field, 36);
        if (!Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty || text != id.ToString("D")) throw Invalid();
        return id;
    }

    internal static long Integer(JsonElement root, string field, long minimum, long maximum)
    {
        var element = root.GetProperty(field);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var value) || value < minimum || value > maximum) throw Invalid();
        return value;
    }

    internal static object Describe(ModuleOperation operation) => new
    {
        moduleId = operation.ModuleId, moduleVersion = operation.ModuleVersion,
        configurationId = operation.ConfigurationId, name = operation.Name,
        inputSchema = operation.InputSchema, outputSchema = operation.OutputSchema,
        maxInputBytes = operation.MaximumInputBytes, maxOutputBytes = operation.MaximumOutputBytes,
        timeoutMilliseconds = operation.TimeoutMilliseconds
    };

    internal static byte[] Request(Guid instance, ModuleRequest request) => Encode(new
    {
        version = 1, instance, requestId = request.RequestId, sessionId = request.SessionId,
        epoch = request.Epoch, fingerprint = request.Fingerprint, operation = Describe(request.Operation),
        context = new
        {
            projectId = request.Context.ProjectId, hostSessionId = request.Context.HostSessionId,
            documentId = request.Context.DocumentId, generation = request.Context.Generation,
            revision = request.Context.Revision, worldId = request.Context.WorldId,
            observationTick = request.Context.ObservationTick
        },
        payload = Convert.ToBase64String(request.Observation.CopyBytes())
    });

    internal static ModuleReply Reply(Guid instance, byte[] bytes)
    {
        using var document = Parse(bytes, "version", "instance", "requestId", "sessionId", "epoch", "fingerprint", "outputSchema", "status", "payload");
        var root = document.RootElement;
        if (Integer(root, "version", 1, 1) != 1 || Identity(root, "instance") != instance) throw Invalid();
        var fingerprint = Text(root, "fingerprint", 64);
        if (fingerprint.Length != 64 || fingerprint.Any(c => !char.IsAsciiHexDigitUpper(c))) throw Invalid();
        var status = (ModuleReplyStatus)Integer(root, "status", 1, 3);
        var encoded = Text(root, "payload", 87_384);
        ModulePayload? proposal = null;
        if (status == ModuleReplyStatus.Proposal)
        {
            var payload = Convert.FromBase64String(encoded);
            if (Convert.ToBase64String(payload) != encoded) throw Invalid();
            proposal = new ModulePayload(payload);
        }
        else if (encoded.Length != 0) throw Invalid();
        return new(1, Identity(root, "requestId"), Identity(root, "sessionId"),
            Integer(root, "epoch", 1, long.MaxValue), fingerprint, Identity(root, "outputSchema"), status, proposal);
    }

    internal static InvalidDataException Invalid() => new("Invalid module wire message.");
}
