using System.Text.Json;

namespace Ncma.Application;

// Pure offline byte consumer; labels are supplied evidence, not a filesystem or model capability.
public static class ProcessMeasurementEvidence
{
    public static ProcessMeasurementReport ValidateReport(ProcessMeasurementReport report)
    {
        ArgumentNullException.ThrowIfNull(report); ArgumentNullException.ThrowIfNull(report.Identity);
        report.Identity.Validate(); var series = ProcessSeriesEvidence.ValidateReport(report.Series);
        if (report.Version != 1 || report.Identity.RunId != series.RunId || report.Iterations < 0 || report.CompletedIterations < 0 ||
            report.StoppedIterations < 0 || report.FaultedIterations < 0 || report.ObserverFailures is < 0 or > 1 ||
            (Int128)report.CompletedIterations + report.StoppedIterations + report.FaultedIterations != report.Iterations ||
            report.ObserverFailures > report.Iterations || series.Samples.Length > report.Iterations ||
            !report.BoundToLoop && (report.Iterations != 0 || report.ObserverFailures != 0))
            throw new ArgumentException("process_measurement_report");
        return report with { Series = series };
    }
    public static byte[] Encode(ProcessMeasurementReport report, int maximumBytes = ProcessSeriesEvidence.MaximumBytes)
    {
        if (maximumBytes is < 1 or > ProcessSeriesEvidence.MaximumBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var canonical = ValidateReport(report);
        using var output = new ProcessSeriesEvidence.BoundedOutput(maximumBytes);
        JsonSerializer.Serialize(output, canonical, ProcessSeriesEvidence.Options); return output.Copy();
    }
    public static ProcessMeasurementReport Decode(ReadOnlySpan<byte> bytes, int maximumBytes = ProcessSeriesEvidence.MaximumBytes)
    {
        ProcessSeriesEvidence.CheckBytes(bytes, maximumBytes);
        try {
            ProcessSeriesEvidence.InspectShape(bytes, 2);
            var report = JsonSerializer.Deserialize<ProcessMeasurementReport>(bytes, ProcessSeriesEvidence.Options) ?? throw new ArgumentException("process_measurement_null");
            var canonical = ValidateReport(report);
            if (!bytes.SequenceEqual(Encode(canonical, maximumBytes))) throw new ArgumentException("process_measurement_noncanonical");
            return canonical;
        } catch (JsonException) { throw new ArgumentException("process_measurement_json"); }
    }
    // Explicit expected labels must come from the caller's separately pinned manifest/workload records.
    public static ProcessMeasurementReport DecodeExpected(ReadOnlySpan<byte> bytes, ProcessMeasurementIdentity expected,
        int maximumBytes = ProcessSeriesEvidence.MaximumBytes)
    {
        ArgumentNullException.ThrowIfNull(expected); expected.Validate();
        var report = Decode(bytes, maximumBytes);
        if (report.Identity != expected) throw new ArgumentException("process_measurement_expected_identity");
        return report;
    }
}
