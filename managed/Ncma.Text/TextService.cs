using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Ncma.Interop;
using Ncma.Ui;

namespace Ncma.Text;

// Explicit trusted font resource. License declaration is metadata, not proof of redistribution
// rights. M5.10 cook/publish must separately check that policy. No OS font discovery or downloads.
public sealed class FontAsset
{
    private readonly byte[] _bytes;
    public Guid Id { get; } public string License { get; } public bool Redistributable { get; } public string Hash { get; }
    public FontAsset(Guid id, ReadOnlySpan<byte> bytes, string license, bool redistributable)
    {
        if (id == Guid.Empty || bytes.Length is < 12 or > 32 * 1024 * 1024 || string.IsNullOrWhiteSpace(license) || license.Length > 2048) throw new ArgumentException("Explicit font identity/data/licence required.");
        Id = id; License = license; Redistributable = redistributable; _bytes = bytes.ToArray(); Hash = Convert.ToHexString(SHA256.HashData(_bytes));
    }
    internal ReadOnlySpan<byte> Bytes => _bytes;
}
[StructLayout(LayoutKind.Sequential)] internal unsafe struct TextRequest
{ public uint Size, Bytes; public ulong Font; public byte* Text; public float FontSize, Width, Height, Scale; public uint Wrap, Rtl; }
[StructLayout(LayoutKind.Sequential)] public struct TextMetrics
{ public uint Size, Width, Height, Bytes; public float ContentWidth, ContentHeight; public uint Lines, Reserved; }
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CreateFont(ulong module, byte* bytes, uint count, ulong* output, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint PrepareText(ulong module, TextRequest* request, ulong* output, TextMetrics* metrics, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CopyText(ulong module, ulong handle, byte* output, uint bytes, PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint ReleaseText(ulong module, ulong handle, PluginError* error);

public sealed class PreparedFont : IDisposable
{
    internal TextService Owner { get; } internal ulong Handle { get; private set; } public FontAsset Asset { get; }
    internal PreparedFont(TextService owner, ulong handle, FontAsset asset) { Owner = owner; Handle = handle; Asset = asset; }
    public void Dispose() { if (Handle == 0) return; Owner.Release(Handle); Handle = 0; }
}
public sealed class PreparedText : IDisposable
{
    internal TextService Owner { get; } internal ulong Handle { get; private set; } public TextMetrics Metrics { get; }
    public Guid FontId { get; } public string FontHash { get; } public string Text { get; }
    internal PreparedText(TextService owner, ulong handle, TextMetrics metrics, PreparedFont font, string text)
    { Owner = owner; Handle = handle; Metrics = metrics; FontId = font.Asset.Id; FontHash = font.Asset.Hash; Text = text; }
    public byte[] CopyRgba() => Owner.Copy(this);
    public void Dispose() { if (Handle == 0) return; Owner.Release(Handle); Handle = 0; }
}
public sealed unsafe class TextService : IDisposable
{
    private readonly PluginLease _lease; private readonly CreateFont _font; private readonly PrepareText _prepare;
    private readonly CopyText _copy; private readonly ReleaseText _release; private readonly HashSet<ulong> _resources = [];
    private bool _disposed; private PluginModule Module => _lease.Module;
    public ulong PrepareCalls { get; private set; }
    public TextService(PluginModule module)
    {
        if (module.Kind != ModuleKind.Text) throw new ArgumentException("Text module required."); _lease = module.AcquireLease();
        try { _font = module.ReadFunction<CreateFont>(56); _prepare = module.ReadFunction<PrepareText>(64); _copy = module.ReadFunction<CopyText>(72); _release = module.ReadFunction<ReleaseText>(80); }
        catch { _lease.Dispose(); throw; }
    }
    private void Verify() { _ = Module; ObjectDisposedException.ThrowIf(_disposed, this); }
    public PreparedFont CreateFont(FontAsset asset)
    {
        Verify(); ArgumentNullException.ThrowIfNull(asset); ulong key = 0; PluginError error = default;
        fixed (byte* bytes = asset.Bytes) PluginModule.Check(Module.Id, "create_font", _font(Module.Context, bytes, (uint)asset.Bytes.Length, &key, &error), error);
        _resources.Add(key); return new(this, key, asset);
    }
    public PreparedText Prepare(PreparedFont font, string text, float size, float width, float height, float scale = 1, bool wrap = true, bool rightToLeft = false)
    {
        Verify(); ArgumentNullException.ThrowIfNull(font); ArgumentNullException.ThrowIfNull(text);
        if (font.Owner != this || font.Handle == 0 || text.Contains('\0') || text.Length > 16384) throw new ArgumentException("Foreign/released font or text budget.");
        byte[] data = new UTF8Encoding(false, true).GetBytes(text); ulong key = 0; TextMetrics metrics = default; PluginError error = default;
        fixed (byte* bytes = data) {
            TextRequest input = new() { Size = 48, Bytes = (uint)data.Length, Font = font.Handle, Text = bytes, FontSize = size, Width = width, Height = height, Scale = scale, Wrap = wrap ? 1u : 0, Rtl = rightToLeft ? 1u : 0 };
            PluginModule.Check(Module.Id, "prepare_text", _prepare(Module.Context, &input, &key, &metrics, &error), error);
        }
        if (metrics.Size != 32 || metrics.Reserved != 0 || metrics.Width == 0 || metrics.Height == 0 || (ulong)metrics.Width * metrics.Height * 4 != metrics.Bytes) {
            PluginModule.Check(Module.Id, "release_invalid_text", _release(Module.Context, key, &error), error); throw new ArgumentException("Text metrics contract.");
        }
        _resources.Add(key); PrepareCalls++; return new(this, key, metrics, font, text);
    }
    internal byte[] Copy(PreparedText text)
    {
        Verify(); if (text.Owner != this || text.Handle == 0) throw new ArgumentException("Released/foreign text.");
        byte[] bytes = new byte[text.Metrics.Bytes]; PluginError error = default;
        fixed (byte* output = bytes) PluginModule.Check(Module.Id, "copy_text", _copy(Module.Context, text.Handle, output, (uint)bytes.Length, &error), error);
        return bytes;
    }
    internal void Release(ulong handle) { Verify(); PluginError error = default; PluginModule.Check(Module.Id, "release_text_resource", _release(Module.Context, handle, &error), error); _resources.Remove(handle); }
    public void Dispose() { if (_disposed) return; Verify(); if (_resources.Count != 0) throw new InvalidOperationException("Release text runs and fonts before TextService; ownership retained."); _lease.Dispose(); _disposed = true; }
}

// Only prepared runs participate in layout measurement; no native call/font parsing in Layout.
public sealed class PreparedTextMetrics : IUiTextMetrics
{
    private readonly Dictionary<Guid, (Guid Font, string Text, Vector2 Size)> _metrics;
    public PreparedTextMetrics(IEnumerable<KeyValuePair<Guid, PreparedText>> runs)
    {
        ArgumentNullException.ThrowIfNull(runs); _metrics = runs.ToDictionary(p => p.Key, p => (p.Value.FontId, p.Value.Text, new Vector2(p.Value.Metrics.ContentWidth, p.Value.Metrics.ContentHeight)));
        if (_metrics.Count > UiCodec.MaxElements || _metrics.ContainsKey(Guid.Empty)) throw new ArgumentException("Text measurement identity/budget.");
    }
    public Vector2 Measure(UiElement element, float availableWidth)
    {
        if (!_metrics.TryGetValue(element.Id, out var prepared) || prepared.Font != element.Font || prepared.Text != element.Text || prepared.Size.X > availableWidth) throw new InvalidOperationException("Prepared matching text metrics required for this layout.");
        return prepared.Size;
    }
}
