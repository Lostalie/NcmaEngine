using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ncma.Runtime;

// Identity only: not a World reference, a permission grant or a persistent object ID.
[StructLayout(LayoutKind.Sequential)]
[JsonConverter(typeof(EntityJsonConverter))]
public readonly record struct Entity(Guid WorldIdentity, int Index, ulong Generation);

public sealed class EntityJsonConverter : JsonConverter<Entity>
{
    public override Entity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new JsonException("Runtime Entity identities cannot be deserialized; resolve a persistent UUID in its World.");
    public override void Write(Utf8JsonWriter writer, Entity value, JsonSerializerOptions options) =>
        throw new JsonException("Runtime Entity identities cannot be serialized; persist a UUID instead.");
}

// Reservations deliberately outlive aborted candidates. Committed entries retain their own generation,
// so reusing a deleted candidate slot does not invalidate the still-visible committed entity.
internal sealed class EntityGenerations
{
    private readonly ulong[] _reserved = new ulong[World.MaxObjects];

    internal (int Index, ulong Generation) Reserve(bool[] occupied)
    {
        for (int i = 0; i < _reserved.Length; i++)
            if (!occupied[i] && _reserved[i] != ulong.MaxValue)
                return (i, ++_reserved[i]);
        throw new InvalidOperationException("Entity capacity exhausted (occupied or generation-retired slots).");
    }
}
