using System.Text.Json;
using Ncma.Runtime;
namespace Ncma.Scene;

// Complete scene document format for both snapshots and .ncmascene assets.
public static class SceneDocumentCodec
{
    public const int Version = 1;
    public const int MaxBytes = World.MaxSnapshotBytes;
    public static byte[] Encode(SceneDocumentSnapshot snapshot)
    {
        SceneDocumentValidator.CheckMetadata(snapshot);
        return SceneJson.EncodeBounded(snapshot, MaxBytes);
    }
    public static SceneDocumentSnapshot Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > MaxBytes) throw new ArgumentException("Invalid document snapshot size.");
        using var json = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
        SceneJson.RejectDuplicateFields(json.RootElement);
        var snapshot = json.RootElement.Deserialize<SceneDocumentSnapshot>(SceneJson.Options)
            ?? throw new ArgumentException("Missing document snapshot.");
        SceneDocumentValidator.CheckMetadata(snapshot);
        return snapshot;
    }
    internal static SceneDocumentSnapshot Copy(SceneDocumentSnapshot snapshot) => Decode(Encode(snapshot));
}
