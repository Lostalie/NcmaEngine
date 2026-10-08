using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Ncma.Assets;

public sealed record DerivedAssetBlock(Guid AssetId, AssetKind Kind, byte[] Data);

// NCA1: 16-byte header, 72-byte block entries, contiguous payloads. Integers LE, UUIDs RFC byte order.
public static class DerivedAssetCodec
{
    public const int MaxBytes = 256 * 1024 * 1024, MaxBlocks = 64, MaxBlockBytes = 64 * 1024 * 1024;
    public const int HeaderBytes = 16, EntryBytes = 72;
    private const uint Magic = 0x3141434E; // ASCII NCA1

    public static byte[] Encode(IEnumerable<DerivedAssetBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var owned = new List<DerivedAssetBlock>();
        var ids = new HashSet<Guid>();
        int length = HeaderBytes;
        foreach (var block in blocks)
        {
            if (block is null || block.AssetId == Guid.Empty || !Enum.IsDefined(block.Kind) || block.Kind > AssetKind.OverrideSet || block.Data is null ||
                block.Data.Length is < 1 or > MaxBlockBytes || owned.Count >= MaxBlocks || !ids.Add(block.AssetId))
                throw new ArgumentException("Invalid derived block identity/kind/budget.");
            length = checked(length + EntryBytes + block.Data.Length);
            if (length > MaxBytes) throw new ArgumentException("Derived file exceeds budget.");
            owned.Add(block with { Data = (byte[])block.Data.Clone() });
        }
        if (owned.Count == 0) throw new ArgumentException("Derived file requires blocks.");
        owned.Sort((a, b) => a.AssetId.CompareTo(b.AssetId));
        byte[] bytes = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)owned.Count);
        int offset = HeaderBytes + EntryBytes * owned.Count;
        for (int i = 0; i < owned.Count; i++)
        {
            var block = owned[i];
            Span<byte> entry = bytes.AsSpan(HeaderBytes + EntryBytes * i, EntryBytes);
            block.AssetId.TryWriteBytes(entry[..16], bigEndian: true, out _);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[16..], checked((uint)block.Kind + 1));
            BinaryPrimitives.WriteUInt64LittleEndian(entry[24..], (ulong)offset);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[32..], (ulong)block.Data.Length);
            SHA256.HashData(block.Data, entry[40..72]);
            block.Data.CopyTo(bytes, offset); offset += block.Data.Length;
        }
        return bytes;
    }

    public static DerivedAssetBlock[] Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderBytes || bytes.Length > MaxBytes || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != 1 || BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]) != 0)
            throw new ArgumentException("Invalid derived header/version/budget.");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
        if (count is < 1 or > MaxBlocks || HeaderBytes + count * EntryBytes > bytes.Length)
            throw new ArgumentException("Invalid derived table.");
        var result = new DerivedAssetBlock[count];
        var ids = new HashSet<Guid>();
        ulong expectedOffset = HeaderBytes + count * EntryBytes;
        Guid previousId = Guid.Empty;
        Span<byte> hash = stackalloc byte[32];
        for (int i = 0; i < count; i++)
        {
            var entry = bytes.Slice(HeaderBytes + EntryBytes * i, EntryBytes);
            Guid id = new(entry[..16], bigEndian: true);
            uint tag = BinaryPrimitives.ReadUInt32LittleEndian(entry[16..]);
            if (id == Guid.Empty || !ids.Add(id) || (i > 0 && id.CompareTo(previousId) <= 0) || tag is < 1 or > 10 ||
                BinaryPrimitives.ReadUInt32LittleEndian(entry[20..]) != 0)
                throw new ArgumentException("Invalid/unsorted derived block identity/type.");
            ulong offset = BinaryPrimitives.ReadUInt64LittleEndian(entry[24..]);
            ulong size = BinaryPrimitives.ReadUInt64LittleEndian(entry[32..]);
            if (offset != expectedOffset || size is < 1 or > MaxBlockBytes || offset > (ulong)bytes.Length || size > (ulong)bytes.Length - offset)
                throw new ArgumentException("Invalid derived block range.");
            var payload = bytes.Slice((int)offset, (int)size);
            SHA256.HashData(payload, hash);
            if (!CryptographicOperations.FixedTimeEquals(hash, entry[40..72])) throw new ArgumentException("Derived checksum mismatch.");
            result[i] = new(id, (AssetKind)(tag - 1), payload.ToArray());
            expectedOffset += size; previousId = id;
        }
        if (expectedOffset != (ulong)bytes.Length) throw new ArgumentException("Unexpected derived trailing data.");
        return result;
    }
}
