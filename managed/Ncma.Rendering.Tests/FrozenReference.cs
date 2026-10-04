using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

internal static class FrozenReference
{
    public static byte[] Load(string path)
    {
        if (new FileInfo(path).Length is <= 0 or > 1048576) throw new ArgumentException("Frozen image budget.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var value = document.RootElement;
        if (value.GetProperty("schemaVersion").GetInt32() != 1 || value.GetProperty("kind").GetString() != "frozen_legacy_reference" ||
            value.GetProperty("width").GetInt32() != 256 || value.GetProperty("height").GetInt32() != 256 ||
            value.GetProperty("encoding").GetString() != "rgba8-gzip-base64") throw new ArgumentException("Frozen image scope.");
        byte[] compressed = Convert.FromBase64String(value.GetProperty("data").GetString()!);
        if (compressed.Length > 65536) throw new ArgumentException("Frozen compression budget.");
        using var input = new MemoryStream(compressed);
        using var zip = new GZipStream(input, CompressionMode.Decompress);
        byte[] pixels = new byte[256 * 256 * 4];
        zip.ReadExactly(pixels);
        if (zip.ReadByte() != -1 || !Convert.ToHexString(SHA256.HashData(pixels)).Equals(value.GetProperty("pixelSha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Frozen image integrity.");
        return pixels;
    }
}
