using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ncma.Application;

// Pure bounded validation/output. Host must separately bind product/workload/source and choose a trusted output path.
public static class ProcessSeriesEvidence
{
    public const int MaximumBytes = 16 * 1024 * 1024;
    internal static readonly JsonSerializerOptions Options = new() { MaxDepth = 8, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition)
    { if (!condition) throw new ArgumentException("process_series_evidence_mismatch"); }
    public static ProcessSeriesReport ValidateReport(ProcessSeriesReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Require(report.Version == 1 && report.RunId != Guid.Empty && report.ProcessId > 0 && report.ObserverThreadId > 0 && report.TimestampFrequency >= 1000 &&
            report.StartedTimestamp >= 0 && report.EndedTimestamp >= report.StartedTimestamp && report.IntervalTicks > 0 && report.DurationTicks > 0 &&
            (Int128)report.IntervalTicks * 10 >= report.TimestampFrequency && (Int128)report.IntervalTicks <= (Int128)60 * report.TimestampFrequency &&
            report.DurationTicks >= report.IntervalTicks - 1 && (Int128)report.DurationTicks <= (Int128)3600 * report.TimestampFrequency &&
            report.DurationTicks <= long.MaxValue - report.StartedTimestamp && report.Capacity is >= 1 and <= ProcessSeriesCapture.MaximumCapacity &&
            report.PlannedSlots is >= 1 and <= ProcessSeriesCapture.MaximumPlannedSlots && report.MissingDueSlots >= 0 && report.UnobservedSlots >= 0 &&
            Enum.IsDefined(report.Stop) && report.Stop != ProcessSeriesStop.Sampling && report.Samples is not null && report.Samples.Length <= report.Capacity);
        Require((Int128)report.PlannedSlots == ((Int128)report.DurationTicks + report.IntervalTicks - 1) / report.IntervalTicks &&
            (long)report.Samples.Length + report.MissingDueSlots + report.UnobservedSlots == report.PlannedSlots);
        var samples = (ProcessSeriesSample[])report.Samples.Clone(); int previousSlot = -1; long previousEnd = report.StartedTimestamp;
        foreach (var sample in samples) {
            Require(sample.Slot > previousSlot && sample.Slot < report.PlannedSlots && sample.StartedTimestamp >= previousEnd &&
                sample.StartedTimestamp - report.StartedTimestamp < report.DurationTicks &&
                (sample.StartedTimestamp - report.StartedTimestamp) / report.IntervalTicks == sample.Slot &&
                sample.EndedTimestamp >= sample.StartedTimestamp && sample.EndedTimestamp <= report.EndedTimestamp);
            ProcessSeriesCounters.Validate(sample.Counters);
            previousSlot = sample.Slot; previousEnd = sample.EndedTimestamp;
        }
        long elapsed = report.EndedTimestamp - report.StartedTimestamp;
        switch (report.Stop) {
            case ProcessSeriesStop.WindowEnded:
                Require(elapsed >= report.DurationTicks && report.UnobservedSlots == 0 && report.MissingDueSlots == report.PlannedSlots - samples.Length); break;
            case ProcessSeriesStop.ExplicitSeal:
                Require(elapsed < report.DurationTicks && report.MissingDueSlots == elapsed / report.IntervalTicks + 1 - samples.Length); break;
            case ProcessSeriesStop.Capacity:
                Require(samples.Length == report.Capacity && elapsed < report.DurationTicks && elapsed / report.IntervalTicks > previousSlot &&
                    report.MissingDueSlots == elapsed / report.IntervalTicks - samples.Length); break;
            case ProcessSeriesStop.ClockFailure:
                Require(report.MissingDueSlots >= previousSlot + 1 - samples.Length); break; // Unknown later clock cannot reconstruct unseen slots.
            default: throw new ArgumentException("process_series_unsealed");
        }
        return report with { Samples = samples };
    }
    public static byte[] Encode(ProcessSeriesReport report, int maximumBytes = MaximumBytes)
    {
        if (maximumBytes is < 1 or > MaximumBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var canonical = ValidateReport(report);
        using var output = new BoundedOutput(maximumBytes); JsonSerializer.Serialize(output, canonical, Options); return output.Copy();
    }
    public static ProcessSeriesReport Decode(ReadOnlySpan<byte> bytes, int maximumBytes = MaximumBytes)
    {
        CheckBytes(bytes, maximumBytes);
        try {
            InspectShape(bytes, 1);
            var report = JsonSerializer.Deserialize<ProcessSeriesReport>(bytes, Options) ?? throw new ArgumentException("process_series_null");
            var canonical = ValidateReport(report);
            if (!bytes.SequenceEqual(Encode(canonical, maximumBytes))) throw new ArgumentException("process_series_noncanonical");
            return canonical;
        } catch (JsonException) { throw new ArgumentException("process_series_json"); }
    }
    internal static void CheckBytes(ReadOnlySpan<byte> bytes, int maximum)
    {
        if (maximum is < 1 or > MaximumBytes) throw new ArgumentOutOfRangeException(nameof(maximum));
        if (bytes.Length == 0 || bytes.Length > maximum) throw new ArgumentException("process_series_byte_budget");
    }
    // Preflight before typed arrays: one Samples array, bounded objects/scalars, no duplicate names.
    internal static void InspectShape(ReadOnlySpan<byte> bytes, int sampleDepth)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 8 });
        var names = new HashSet<string>?[9]; var properties = new string?[9];
        bool inSamples = false, seenSamples = false; int count = 0;
        while (reader.Read()) {
            int depth = reader.CurrentDepth;
            if (reader.TokenType is JsonTokenType.PropertyName or JsonTokenType.String && reader.ValueSpan.Length > 1024 ||
                reader.TokenType == JsonTokenType.Number && reader.ValueSpan.Length > 32)
                throw new ArgumentException("process_series_scalar_budget");
            if (inSamples && depth == sampleDepth + 1 && reader.TokenType is not JsonTokenType.EndObject and not JsonTokenType.EndArray &&
                (reader.TokenType != JsonTokenType.StartObject || ++count > ProcessSeriesCapture.MaximumCapacity))
                throw new ArgumentException("process_series_array_budget");
            switch (reader.TokenType) {
                case JsonTokenType.StartObject: names[depth] = new(StringComparer.Ordinal); break;
                case JsonTokenType.EndObject: names[depth] = null; break;
                case JsonTokenType.PropertyName:
                    string name = reader.GetString()!; var set = depth > 0 ? names[depth - 1] : null;
                    if (set is null || set.Count == 32 || !set.Add(name)) throw new ArgumentException("process_series_members");
                    properties[depth] = name; break;
                case JsonTokenType.StartArray:
                    if (seenSamples || depth != sampleDepth || properties[depth] != "Samples") throw new ArgumentException("process_series_array_shape");
                    seenSamples = inSamples = true; break;
                case JsonTokenType.EndArray: inSamples = false; break;
            }
        }
        if (!seenSamples) throw new ArgumentException("process_series_samples_required");
    }
    internal sealed class BoundedOutput(int maximum) : Stream
    {
        private readonly MemoryStream data = new();
        internal byte[] Copy() => data.ToArray();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) { if (buffer.Length > maximum - data.Length) throw new ArgumentException("process_series_byte_budget"); data.Write(buffer); }
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => data.Length; public override long Position { get => data.Position; set => throw new NotSupportedException(); }
        public override void Flush() { } public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) data.Dispose(); base.Dispose(disposing); }
    }
}
