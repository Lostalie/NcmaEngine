using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ncma.Asset.Import;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImportWorkerRequest(
    [property: JsonRequired] int Version, [property: JsonRequired] Guid JobId, [property: JsonRequired] Guid ProjectId,
    [property: JsonRequired] ulong ProjectGeneration, [property: JsonRequired] ulong AssetRevision,
    [property: JsonRequired] string SourcePath, [property: JsonRequired] string SourceHash,
    [property: JsonRequired] int SampleRate, [property: JsonRequired] string ResultPath,
    [property: JsonRequired] bool StaticOnly = false);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImportWorkerMessage(
    [property: JsonRequired] int Version, [property: JsonRequired] Guid JobId,
    [property: JsonRequired] string State, [property: JsonRequired] uint Phase,
    [property: JsonRequired] ulong BytesRead, [property: JsonRequired] ulong BytesTotal,
    [property: JsonRequired] string Code, [property: JsonRequired] string? ResultHash,
    [property: JsonRequired] long ResultBytes);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImportWorkerCancel([property: JsonRequired] int Version, [property: JsonRequired] Guid JobId,
    [property: JsonRequired] string Operation);

// One length-prefixed JSON object per frame. No stdout logs, arbitrary code, type names or native layouts.
public static class ImportProtocol
{
    public const int MaxFrameBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 8 };
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellation = default)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (bytes.Length is < 2 or > MaxFrameBytes) throw new ArgumentException("Import IPC frame budget exceeded.");
        byte[] header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, cancellation); await stream.WriteAsync(bytes, cancellation); await stream.FlushAsync(cancellation);
    }
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellation = default)
    {
        byte[] header = new byte[4]; await stream.ReadExactlyAsync(header, cancellation);
        int size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size is < 2 or > MaxFrameBytes) throw new ArgumentException("Invalid import IPC frame length.");
        byte[] bytes = new byte[size]; await stream.ReadExactlyAsync(bytes, cancellation);
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Import IPC must be an object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.EnumerateObject()) if (!names.Add(property.Name)) throw new ArgumentException("Duplicate import IPC property.");
        return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new ArgumentException("Null import IPC frame.");
    }
    public static void Validate(ImportWorkerRequest request)
    {
        if (request.Version != 1 || request.JobId == Guid.Empty || request.ProjectId == Guid.Empty || request.ProjectGeneration == 0 ||
            request.SampleRate is < 1 or > 120 || !Path.IsPathFullyQualified(request.SourcePath) || !Path.IsPathFullyQualified(request.ResultPath) ||
            request.SourcePath == request.ResultPath || request.SourcePath.Length > 32768 || request.ResultPath.Length > 32768)
            throw new ArgumentException("Invalid import worker request.");
        Ncma.Assets.AssetRecordCodec.ValidateHash(request.SourceHash);
    }
    public static void Validate(ImportWorkerMessage message, Guid jobId)
    {
        if (message.Version != 1 || message.JobId != jobId || message.Phase > 6 || message.BytesRead > message.BytesTotal ||
            message.Code is null || message.Code.Length > 128 || message.State is not ("Progress" or "Ready" or "Cancelled" or "Failed"))
            throw new ArgumentException("Invalid import worker message.");
        if (message.State == "Ready")
        {
            if (message.ResultHash is null || message.ResultBytes is < 48 or > Ncma.Assets.ImportedModelCodec.MaxBytes || message.Code != "ok") throw new ArgumentException("Invalid Ready manifest.");
            Ncma.Assets.AssetRecordCodec.ValidateHash(message.ResultHash);
        }
        else if (message.ResultHash is not null || message.ResultBytes != 0) throw new ArgumentException("Unexpected import result fields.");
    }
}
