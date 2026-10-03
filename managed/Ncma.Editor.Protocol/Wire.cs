using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Ncma.Editor.Protocol;

public sealed record EndpointDescriptor(int IpcVersion, Guid InstanceId, string ProjectRoot, string PipeName);
public sealed record Hello(int IpcVersion, Guid InstanceId, string ProjectRoot, string ClientName, Guid? ConnectionId = null, string? Credential = null);
public sealed record HelloResult(string Code, Guid InstanceId, Guid ConnectionId, string? Credential, Guid SessionId, ulong DocumentGeneration);
public sealed record IpcRequest(string Method, ulong DocumentGeneration, JsonElement? Request = null, Guid? RequestId = null, Guid? CallId = null);
public sealed record IpcResponse(string Code, JsonElement? Result = null, Guid? CallId = null);
public static class Wire
{
    public const int Version = 1, MaxMessageBytes = 1024 * 1024;
    public static readonly UTF8Encoding Utf8 = new(false, true);
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32 };
    public static byte[] Encode<T>(T value)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (bytes.Length is 0 or > MaxMessageBytes) throw new ArgumentException("message_too_large");
        return bytes;
    }
    public static T Decode<T>(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > MaxMessageBytes) throw new ArgumentException("message_too_large");
        _ = Utf8.GetString(bytes);
        using var doc = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
        RejectDuplicates(doc.RootElement);
        return doc.RootElement.Deserialize<T>(Json) ?? throw new ArgumentException("invalid_message");
    }
    public static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in element.EnumerateObject()) { if (!names.Add(p.Name)) throw new ArgumentException("duplicate_field"); RejectDuplicates(p.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) RejectDuplicates(child);
    }
    public static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken cancellation)
    {
        byte[] prefix = new byte[4];
        int first = await stream.ReadAsync(prefix.AsMemory(0, 1), cancellation);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(prefix.AsMemory(1), cancellation);
        int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is <= 0 or > MaxMessageBytes) throw new ArgumentException("message_too_large");
        byte[] bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, cancellation);
        _ = Utf8.GetString(bytes); return bytes;
    }
    public static async Task WriteAsync(Stream stream, byte[] bytes, CancellationToken cancellation)
    {
        if (bytes.Length is 0 or > MaxMessageBytes) throw new ArgumentException("message_too_large");
        byte[] prefix = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
        await stream.WriteAsync(prefix, cancellation); await stream.WriteAsync(bytes, cancellation); await stream.FlushAsync(cancellation);
    }
}
