using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ncma.Ui;

// .ncmaui JSON v1 only. Persistent UUIDs, no native/runtime handles or executable script strings.
public static class UiCodec
{
    public const int MaxBytes = 4 * 1024 * 1024, MaxElements = 4096, MaxDepth = 64, MaxTokens = 256;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) } };
    public static byte[] Encode(UiDefinition document)
    {
        Validate(document); byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, Json);
        if (bytes.Length > MaxBytes) throw new ArgumentException("UI document byte budget.");
        return bytes;
    }
    public static UiDefinition Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 2 or > MaxBytes) throw new ArgumentException("UI document byte budget.");
        _ = Utf8.GetCharCount(bytes);
        using var parsed = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
        Visit(parsed.RootElement);
        var document = JsonSerializer.Deserialize<UiDefinition>(bytes, Json) ?? throw new ArgumentException("UI document required.");
        Validate(document); return document;
    }
    public static UiDefinition Copy(UiDefinition document) => Decode(Encode(document));
    private static void Visit(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object) {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in e.EnumerateObject()) { if (!keys.Add(p.Name)) throw new ArgumentException("Duplicate UI field."); Visit(p.Value); }
        } else if (e.ValueKind == JsonValueKind.Array) foreach (var v in e.EnumerateArray()) Visit(v);
    }
    internal static void Finite(float value, float minimum = -65536, float maximum = 65536)
    { if (!float.IsFinite(value) || value < minimum || value > maximum) throw new ArgumentException("UI finite scalar range."); }
    internal static void Color(UiColor c) { Finite(c.R, 0, 1); Finite(c.G, 0, 1); Finite(c.B, 0, 1); Finite(c.A, 0, 1); }
    internal static void Text(string text, int max, bool nonempty = false)
    {
        if (text is null || text.Length > max || (nonempty && string.IsNullOrWhiteSpace(text)) || text.Contains('\0')) throw new ArgumentException("UI text budget.");
        for (int i = 0; i < text.Length; i++) if (char.IsSurrogate(text[i])) {
            if (!char.IsHighSurrogate(text[i]) || ++i >= text.Length || !char.IsLowSurrogate(text[i])) throw new ArgumentException("Invalid UI Unicode.");
        }
    }
    public static void Validate(UiDefinition d)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (d.Version != 1 || d.AssetId == Guid.Empty || d.Root == Guid.Empty || d.Elements is null || d.Tokens is null ||
            d.Elements.Length is < 1 or > MaxElements || d.Tokens.Length > MaxTokens) throw new ArgumentException("UI identity/version/budget.");
        Text(d.Name, 256, true);
        var tokens = new Dictionary<Guid, UiToken>(); var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in d.Tokens) {
            if (t is null || t.Id == Guid.Empty || t.Id == d.AssetId || !tokens.TryAdd(t.Id, t) || !Enum.IsDefined(t.Kind)) throw new ArgumentException("UI token identity/kind.");
            Text(t.Name, 128, true); if (!names.Add(t.Name)) throw new ArgumentException("Duplicate UI token name.");
            Color(t.Color); Finite(t.Scalar);
        }
        var elements = new Dictionary<Guid, UiElement>(); var order = new HashSet<(Guid, int)>();
        foreach (var e in d.Elements) {
            if (e is null || e.Id == Guid.Empty || e.Id == d.AssetId || tokens.ContainsKey(e.Id) || !elements.TryAdd(e.Id, e) ||
                !Enum.IsDefined(e.Kind) || e.Order < 0 || !order.Add((e.Parent, e.Order))) throw new ArgumentException("UI element identity/order/kind.");
            Text(e.Name, 256, true); Text(e.Text, 16384); Text(e.Action, 128);
            if (e.Action.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-')) throw new ArgumentException("Semantic UI action name required.");
            if (e.Kind != UiKind.Image && e.Image != Guid.Empty || e.Kind is not (UiKind.Text or UiKind.Button or UiKind.TextInput) && (e.Font != Guid.Empty || e.Text.Length != 0)) throw new ArgumentException("Kind-specific UI resources.");
            Finite(e.FontSize, 1, 512);
            if (e.Layout is not { } l || e.Style is not { } s || !Enum.IsDefined(l.Flow) || !Enum.IsDefined(l.WidthMode) || !Enum.IsDefined(l.HeightMode)) throw new ArgumentException("UI layout/style required.");
            Finite(l.X); Finite(l.Y); Finite(l.Width, 0); Finite(l.Height, 0); Finite(l.MinWidth, 0); Finite(l.MinHeight, 0);
            Finite(l.MaxWidth, l.MinWidth); Finite(l.MaxHeight, l.MinHeight); Finite(l.AnchorX, 0, 1); Finite(l.AnchorY, 0, 1);
            Finite(l.Rotation, -360, 360); Finite(l.Gap, 0); Finite(l.Padding.Left, 0); Finite(l.Padding.Top, 0); Finite(l.Padding.Right, 0); Finite(l.Padding.Bottom, 0);
            Color(s.Fill); Color(s.Foreground); Finite(s.Opacity, 0, 1); Finite(s.CornerRadius, 0);
            Require(s.FillToken, UiTokenKind.Color); Require(s.ForegroundToken, UiTokenKind.Color); Require(s.OpacityToken, UiTokenKind.Scalar);
            if (s.OpacityToken != Guid.Empty) Finite(tokens[s.OpacityToken].Scalar, 0, 1);
        }
        if (!elements.TryGetValue(d.Root, out var root) || root.Parent != Guid.Empty || root.Kind != UiKind.Frame || d.Elements.Count(e => e.Parent == Guid.Empty) != 1) throw new ArgumentException("Exactly one UI Frame root required.");
        foreach (var e in d.Elements) {
            var seen = new HashSet<Guid>(); var current = e;
            while (current.Parent != Guid.Empty) {
                if (!seen.Add(current.Id) || seen.Count >= MaxDepth || !elements.TryGetValue(current.Parent, out var parent)) throw new ArgumentException("UI hierarchy cycle/depth/missing parent.");
                if (parent.Kind is not (UiKind.Frame or UiKind.Group or UiKind.Button or UiKind.ScrollView)) throw new ArgumentException("UI leaf cannot own children.");
                current = parent;
            }
            if (current.Id != d.Root) throw new ArgumentException("Unreachable UI element.");
        }
        void Require(Guid id, UiTokenKind kind) { if (id != Guid.Empty && (!tokens.TryGetValue(id, out var t) || t.Kind != kind)) throw new ArgumentException("Typed UI token reference required."); }
    }
    public static Guid[] Dependencies(UiDefinition d)
    { Validate(d); return d.Elements.SelectMany(e => new[] { e.Font, e.Image }).Where(id => id != Guid.Empty).Distinct().Order().ToArray(); }
}
