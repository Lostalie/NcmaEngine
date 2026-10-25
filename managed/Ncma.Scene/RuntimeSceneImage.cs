using System.Security.Cryptography;
using Ncma.Runtime;

namespace Ncma.Scene;

// A cold load image, NOT a live document or a second component store synchronized with a World.
// Mutable DTOs are private; public copies and loaded instances cannot modify the image.
public sealed class RuntimeSceneImage
{
    public const int FormatVersion = 1;
    public const string Format = "ncma.runtime-scene";
    private readonly byte[] _bytes;
    private readonly RuntimeSceneSnapshot _data;

    internal RuntimeSceneImage(SceneDocumentSnapshot normalized)
    {
        // The caller has already validated/normalized components and composition. This copy
        // owns the JSON elements and nested arrays and enforces the aggregate authoring budget.
        _data = RuntimeSceneCodec.Copy(new(Format, FormatVersion, normalized.Name, normalized.Objects.Select(o =>
            new RuntimeObjectSnapshot(o.Id, o.Name, o.Components, o.Behaviours)).ToArray()));
        _bytes = RuntimeSceneCodec.Encode(_data);
        ContentHash = Convert.ToHexString(SHA256.HashData(_bytes));
    }

    public string Name => _data.Name;
    public int ObjectCount => _data.Objects.Length;
    public int ByteCount => _bytes.Length;
    public string ContentHash { get; }
    public byte[] CopyBytes() => (byte[])_bytes.Clone();
    public RuntimeSceneSnapshot CopySnapshot() => RuntimeSceneCodec.Copy(_data);

    // Preserve authored object order: creation/query order is observable, not canonicalized away.
    public Guid[] CopyObjectIds() => _data.Objects.Select(o => o.Id).ToArray();

    public RuntimeSceneInstance Instantiate(ComponentRegistry components)
    {
        ArgumentNullException.ThrowIfNull(components);
        // Every load revalidates against its actual registry. Failed loads publish no instance,
        // and cannot mutate another running scene. No authoring callbacks are retained here.
        var world = new World(Name, components);
        var objects = _data.Objects.Select(o => new ObjectSnapshot(o.Id, o.Name,
            o.Components.Select(c => c with { Data = c.Data.Clone() }).ToArray())).ToArray();
        var bindings = _data.Objects.ToDictionary(o => o.Id, o => CopyBindings(o.Behaviours));
        world.PrepareRestore(new(1, Name, objects), normalized =>
        {
            // A different trusted registry may normalize/enlarge payloads on load. The combined
            // components + script budget must still fit BEFORE the new instance is published.
            _ = RuntimeSceneCodec.Encode(new(Format, FormatVersion, normalized.Name, normalized.Objects.Select(o =>
                new RuntimeObjectSnapshot(o.PersistentId, o.Name, o.Components, bindings[o.PersistentId])).ToArray()));
        })();
        return new(world, bindings, ContentHash);
    }

    internal static BehaviourBindingData[] CopyBindings(BehaviourBindingData[] bindings) => bindings
        .Select(b => b with { Exports = (ExportData[])b.Exports.Clone() }).ToArray();

}

// Provenance is outside content bytes: identical normalized input yields identical image bytes
// across authoring sessions. A revision stamp is not authority to apply runtime changes to Edit.
public sealed record SceneBakeStamp(Guid DocumentSession, ulong Revision);
public sealed record SceneBakeResult(SceneBakeStamp Source, RuntimeSceneImage Image);
