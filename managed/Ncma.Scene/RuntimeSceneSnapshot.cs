using System.Text.Json;
using System.Text.Json.Serialization;
using Ncma.Runtime;

namespace Ncma.Scene;

// Cold copied state for loading/checkpoints/inspection. Never the live ECS authority.
public sealed record RuntimeObjectSnapshot(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired] string Name,
    [property: JsonRequired] ComponentSnapshot[] Components,
    [property: JsonRequired] BehaviourBindingData[] Behaviours) : ISceneObjectContent;
public sealed record RuntimeSceneSnapshot(
    [property: JsonRequired] string Format,
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Name,
    [property: JsonRequired] RuntimeObjectSnapshot[] Objects) : ISceneContent
{
    IReadOnlyList<ISceneObjectContent> ISceneContent.Objects => Objects;
}

internal static class RuntimeSceneCodec
{
    internal static byte[] Encode(RuntimeSceneSnapshot snapshot)
    {
        Validate(snapshot);
        return SceneJson.EncodeBounded(snapshot, World.MaxSnapshotBytes);
    }
    internal static RuntimeSceneSnapshot Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > World.MaxSnapshotBytes) throw new ArgumentException("Invalid runtime scene size.");
        using var json = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
        SceneJson.RejectDuplicateFields(json.RootElement);
        var snapshot = json.RootElement.Deserialize<RuntimeSceneSnapshot>(SceneJson.Options)
            ?? throw new ArgumentException("Missing runtime scene.");
        Validate(snapshot);
        return snapshot;
    }
    internal static RuntimeSceneSnapshot Copy(RuntimeSceneSnapshot snapshot) => Decode(Encode(snapshot));
    private static void Validate(RuntimeSceneSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Format != RuntimeSceneImage.Format || snapshot.Version != RuntimeSceneImage.FormatVersion ||
            snapshot.Objects is null || snapshot.Objects.Length > World.MaxObjects)
            throw new ArgumentException("Unsupported runtime scene format/version/capacity.");
        World.ValidateName(snapshot.Name);
        var ids = new HashSet<Guid>(); var bindings = new HashSet<Guid>();
        foreach (var item in snapshot.Objects)
        {
            if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id) ||
                item.Components is null || item.Components.Length > World.MaxComponentsPerObject)
                throw new ArgumentException("Invalid runtime object identity/capacity.");
            World.ValidateName(item.Name);
            var types = new HashSet<string>(StringComparer.Ordinal);
            foreach (var component in item.Components)
            {
                if (component is null || string.IsNullOrWhiteSpace(component.TypeId) || component.TypeId.Length > 128 ||
                    component.Version < 1 || !types.Add(component.TypeId) || component.Data.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("Invalid runtime component identity/data.");
                SceneJson.VerifySnapshotComponentDepth(component.Data);
                SceneJson.RejectDuplicateFields(component.Data);
                if (System.Text.Encoding.UTF8.GetByteCount(component.Data.GetRawText()) > ComponentRegistry.MaxPayloadBytes)
                    throw new ArgumentException("Runtime component payload limit exceeded.");
            }
            BehaviourBindingRules.Validate(item.Behaviours, bindings, "runtime object");
        }
    }
}

// Session-bound immutable checkpoint; cannot be injected as an edit transaction or loaded into
// another runtime session. Runtime entity identities are deliberately absent from its payload.
public sealed class RuntimeSceneCheckpoint
{
    private readonly byte[] _bytes;
    internal RuntimeSceneCheckpoint(Guid session, byte[] ownedBytes)
    { SessionId = session; _bytes = ownedBytes; }
    public Guid SessionId { get; }
    public byte[] CopyBytes() => (byte[])_bytes.Clone();
    internal RuntimeSceneSnapshot Read() => RuntimeSceneCodec.Decode(_bytes);
}
