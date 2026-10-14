using System.Security.Cryptography;
using System.Text;
using Ncma.Assets;
using Ncma.Assets.Runtime;
using Ncma.Text;

namespace Ncma.Ui.Rendering;

// Trusted off-frame cook inputs. Neither paths nor executable callbacks are stored in NCU1.
public sealed record UiPackageFont(Guid Id, byte[] Bytes, string License, bool Redistributable);
public sealed record UiPackageImage(Guid Id, uint Width, uint Height, byte[] Rgba);

// Immutable, source-free closure. One explicitly pinned file, no authoring fallback or OS fonts.
// Opening/decoding is CPU-only; native font validation happens in SceneUiPresentation before gameplay.
public sealed class RuntimeUiPackage : IDisposable
{
    public const int MaxBytes = 64 * 1024 * 1024, MaxResources = 64;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly UiDefinition _definition;
    private readonly Dictionary<Guid, FontAsset> _fonts;
    private readonly Dictionary<Guid, UiImageAsset> _images;
    private RuntimeReadPin? _pin;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private bool _disposed;
    public Guid ProjectId { get; }
    public Guid DocumentId => _definition.AssetId;
    public ulong Generation { get; }
    public string Hash { get; }
    public bool HasFonts { get { Verify(); return _fonts.Count != 0; } }
    public UiDefinition Capture() { Verify(); return UiCodec.Copy(_definition); }
    internal IReadOnlyDictionary<Guid, FontAsset> Fonts { get { Verify(); return _fonts; } }
    internal IReadOnlyDictionary<Guid, UiImageAsset> Images { get { Verify(); return _images; } }
    private void Verify()
    {
        if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("UI package owner thread required.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
    private RuntimeUiPackage(Guid project, ulong generation, string hash, UiDefinition definition,
        Dictionary<Guid, FontAsset> fonts, Dictionary<Guid, UiImageAsset> images)
    { ProjectId = project; Generation = generation; Hash = hash; _definition = definition; _fonts = fonts; _images = images; }

    public static RuntimeUiPackage Open(string root, Guid project, UiPackageReference selection)
    {
        selection = selection.Validate(); root = RuntimeReadPin.Root(root);
        var pin = new RuntimeReadPin(root, selection.Path);
        try {
            var result = Decode(pin.Read(MaxBytes, default), project, selection.DocumentId, selection.Generation, selection.Hash);
            result._pin = pin; return result;
        } catch { pin.Dispose(); throw; }
    }
    public static RuntimeUiPackage Decode(ReadOnlySpan<byte> input, Guid project, Guid document, ulong generation, string expectedHash)
    {
        if (input.Length is < 52 or > MaxBytes || project == Guid.Empty || document == Guid.Empty || generation == 0 ||
            expectedHash is null || expectedHash.Length != 64 || expectedHash.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("UI package identity/byte budget.");
        // Copy before verifying so mutation of the caller's buffer cannot change the admitted bytes.
        byte[] bytes = input.ToArray(); string hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("UI package hash mismatch.");
        using var stream = new MemoryStream(bytes, false); using var r = new BinaryReader(stream, Utf8);
        byte[] Read(int count, int maximum) {
            if (count < 0 || count > maximum || count > stream.Length - stream.Position) throw new ArgumentException("UI package field budget/truncation.");
            return r.ReadBytes(count);
        }
        Guid Id() => new(Read(16, 16));
        if (r.ReadUInt32() != 0x3155434e || r.ReadUInt32() != 1 || Id() != project ||
            r.ReadUInt64() != generation) throw new ArgumentException("NCU1 identity/version required.");
        int documentBytes = r.ReadInt32(), count = r.ReadInt32();
        if (count is < 0 or > MaxResources) throw new ArgumentException("UI resource count budget.");
        var definition = UiCodec.Decode(Read(documentBytes, UiCodec.MaxBytes));
        if (definition.AssetId != document) throw new ArgumentException("UI document identity mismatch.");
        var fonts = new Dictionary<Guid, FontAsset>(); var images = new Dictionary<Guid, UiImageAsset>(); var ids = new HashSet<Guid>();
        for (int i = 0; i < count; i++) {
            Guid id = Id(); byte kind = r.ReadByte(); uint width = r.ReadUInt32(), height = r.ReadUInt32();
            byte distributable = r.ReadByte(); int licenseBytes = r.ReadInt32(), payloadBytes = r.ReadInt32();
            string license = Utf8.GetString(Read(licenseBytes, 8192)); byte[] payload = Read(payloadBytes, 32 * 1024 * 1024);
            if (id == Guid.Empty || id == document || !ids.Add(id)) throw new ArgumentException("UI resource identity/duplicate.");
            if (kind == 1 && width == 0 && height == 0 && distributable == 1)
                fonts.Add(id, new(id, payload, license, true));
            else if (kind == 2 && licenseBytes == 0 && distributable == 0)
                images.Add(id, new(id, width, height, payload));
            else throw new ArgumentException("UI resource kind/metadata/redistribution declaration.");
        }
        if (stream.Position != stream.Length) throw new ArgumentException("Trailing UI package bytes.");
        var wantedFonts = definition.Elements.Where(e => e.Font != Guid.Empty).Select(e => e.Font).ToHashSet();
        var wantedImages = definition.Elements.Where(e => e.Image != Guid.Empty).Select(e => e.Image).ToHashSet();
        if (!wantedFonts.SetEquals(fonts.Keys) || !wantedImages.SetEquals(images.Keys) || wantedFonts.Overlaps(wantedImages))
            throw new ArgumentException("Exact typed UI resource closure required; no missing or unused payloads.");
        if (definition.Elements.Any(e => e.Kind == UiKind.Image && e.Image == Guid.Empty || e.Text.Length != 0 && e.Font == Guid.Empty))
            throw new ArgumentException("Explicit UI image/font UUID required.");
        return new(project, generation, hash, definition, fonts, images);
    }
    public static byte[] Encode(Guid project, ulong generation, UiDefinition definition,
        IReadOnlyList<UiPackageFont> fonts, IReadOnlyList<UiPackageImage> images)
    {
        ArgumentNullException.ThrowIfNull(fonts); ArgumentNullException.ThrowIfNull(images);
        if (fonts.Count > MaxResources || images.Count > MaxResources || fonts.Count + images.Count > MaxResources)
            throw new ArgumentException("UI resource count budget.");
        byte[] doc = UiCodec.Encode(definition); long total = 40L + doc.Length;
        foreach (var f in fonts) { ArgumentNullException.ThrowIfNull(f); ArgumentNullException.ThrowIfNull(f.Bytes); ArgumentNullException.ThrowIfNull(f.License); total += 34L + Utf8.GetByteCount(f.License) + f.Bytes.Length; }
        foreach (var image in images) { ArgumentNullException.ThrowIfNull(image); ArgumentNullException.ThrowIfNull(image.Rgba); total += 34L + image.Rgba.Length; }
        if (total > MaxBytes) throw new ArgumentException("UI package byte budget.");
        using var stream = new MemoryStream((int)total); using var w = new BinaryWriter(stream, Utf8, true);
        w.Write(0x3155434eu); w.Write(1u); w.Write(project.ToByteArray()); w.Write(generation); w.Write(doc.Length); w.Write(fonts.Count + images.Count); w.Write(doc);
        foreach (var f in fonts) {
            byte[] license = Utf8.GetBytes(f.License); w.Write(f.Id.ToByteArray()); w.Write((byte)1); w.Write(0u); w.Write(0u);
            w.Write((byte)(f.Redistributable ? 1 : 0)); w.Write(license.Length); w.Write(f.Bytes.Length); w.Write(license); w.Write(f.Bytes);
        }
        foreach (var image in images) {
            w.Write(image.Id.ToByteArray()); w.Write((byte)2); w.Write(image.Width); w.Write(image.Height); w.Write((byte)0); w.Write(0); w.Write(image.Rgba.Length); w.Write(image.Rgba);
        }
        byte[] result = stream.ToArray(); using var validated = Decode(result, project, definition.AssetId, generation, Convert.ToHexString(SHA256.HashData(result))); return result;
    }
    public void Dispose() { if (_disposed) return; Verify(); _pin?.Dispose(); _pin = null; _disposed = true; }
}
