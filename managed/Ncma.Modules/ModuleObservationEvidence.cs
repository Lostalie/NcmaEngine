using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ncma.Modules;

// Canonical bounded UTF-8 BYTES only. Caller owns storage; no path/overwrite/automatic export API.
public static class ModuleObservationEvidence
{
    public const int Version = 2;
    public const int MaximumBytes = 64 * 1024 * 1024;
    private sealed record Envelope(int Version, ModuleObservationReport Report);
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static byte[] Encode(ModuleObservationReport report, int maximumBytes = MaximumBytes)
    {
        Limit(maximumBytes);
        var canonical = ModuleObservationCapture.ValidateReport(report);
        using var output = new BoundedBytes(maximumBytes);
        JsonSerializer.Serialize(output, new Envelope(Version, canonical), Options);
        return output.Copy();
    }
    public static ModuleObservationReport Decode(ReadOnlySpan<byte> bytes, int maximumBytes = MaximumBytes)
    {
        Limit(maximumBytes);
        if (bytes.Length == 0 || bytes.Length > maximumBytes) throw new ArgumentException("observation_evidence_bytes");
        try {
            InspectShape(bytes); // before allocating typed event arrays
            var envelope = JsonSerializer.Deserialize<Envelope>(bytes, Options);
            if (envelope is null || envelope.Version != Version || envelope.Report is null) throw new ArgumentException("observation_evidence_version");
            var canonical = ModuleObservationCapture.ValidateReport(envelope.Report);
            // Also verifies read-only derived properties, exact null/number shape and representation.
            if (!bytes.SequenceEqual(Encode(canonical, maximumBytes))) throw new ArgumentException("observation_evidence_noncanonical");
            return canonical;
        }
        catch (JsonException) { throw new ArgumentException("observation_evidence_json"); } // never echo input/body
    }
    private static void Limit(int value)
    { if (value is < 1 or > MaximumBytes) throw new ArgumentOutOfRangeException(nameof(value), "observation_evidence_limit"); }
    private static void InspectShape(ReadOnlySpan<byte> bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 16 });
        var names = new HashSet<string>?[17]; var property = new string?[17];
        var arrays = new int[17]; var counts = new int[17];
        while (reader.Read()) {
            int depth = reader.CurrentDepth;
            if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName && reader.ValueSpan.Length > 256 ||
                reader.TokenType == JsonTokenType.Number && reader.ValueSpan.Length > 32)
                throw new ArgumentException("observation_evidence_scalar_budget");
            if (depth > 0 && arrays[depth - 1] != 0 && reader.TokenType is not JsonTokenType.EndObject and not JsonTokenType.EndArray) {
                if (reader.TokenType != JsonTokenType.StartObject || ++counts[depth - 1] > arrays[depth - 1])
                    throw new ArgumentException("observation_evidence_array_budget");
            }
            switch (reader.TokenType) {
                case JsonTokenType.StartObject: names[depth] = new(StringComparer.Ordinal); break;
                case JsonTokenType.EndObject: names[depth] = null; break;
                case JsonTokenType.PropertyName:
                    var set = depth > 0 ? names[depth - 1] : null;
                    string name = reader.GetString()!;
                    if (set is null || set.Count == 32 || !set.Add(name)) throw new ArgumentException("observation_evidence_members");
                    property[depth] = name; break;
                case JsonTokenType.StartArray:
                    // Envelope.Report has exactly two arrays of fixed-shape objects; no arbitrary tree.
                    if (depth != 2) throw new ArgumentException("observation_evidence_array_shape");
                    arrays[depth] = property[depth] switch { "Events" => ModuleObservationCapture.MaximumEvents, "Latencies" => 42, _ => throw new ArgumentException("observation_evidence_array_name") };
                    counts[depth] = 0; break;
                case JsonTokenType.EndArray: arrays[depth] = 0; break;
            }
        }
    }
    private sealed class BoundedBytes(int maximum) : Stream
    {
        private readonly MemoryStream data = new();
        public byte[] Copy() => data.ToArray();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) {
            if (buffer.Length > maximum - data.Length) throw new ArgumentException("observation_evidence_bytes");
            data.Write(buffer);
        }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => data.Length;
        public override long Position { get => data.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) data.Dispose(); base.Dispose(disposing); }
    }
}
