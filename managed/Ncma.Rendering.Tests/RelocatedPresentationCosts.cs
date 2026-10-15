using System.Diagnostics;
using System.Text.Json;
using Ncma.Rendering;

// Test-only copied evidence from ordinary relocated hosts. No native synchronization,
// scene access, or per-frame JSON in the measured window.
internal sealed class RelocatedPresentationCosts(UiPublicationProductEvidence evidence)
{
    private readonly HashSet<(ulong Generation, ulong Frame)> _diagnostics = [];
    private readonly List<object> _windows = [];

    internal void Diagnostic(PresentationCostReport? value)
    {
        var report = value ?? throw new Exception("Missing opted-in diagnostic cost report");
        evidence.Pass(report.IncludesDiagnosticCapture, "relocated screenshot marked diagnostic");
        _diagnostics.Add((report.Generation, report.HostFrame));
        if (!report.Completed)
            evidence.Pass(report.Counters is null && report.Main is null && report.Skin is null && report.Ui is null,
                "failed presentation never reuses successful cost counters or GPU samples");
    }

    internal void Window(string label, RendererSession renderer, Action draw, Func<PresentationCostReport?> read)
    {
        if (_windows.Count >= 24) throw new Exception("Joint cost evidence window budget");
        // All storage/delegates are allocated before the timing window. Keep the
        // original workload's 8 warm + 16 measured frames, without capture or wait.
        for (int i = 0; i < 8; i++) draw();
        var before = read() ?? throw new Exception("Missing warm cost observation");
        evidence.Pass(before.Generation == renderer.ReadFrameState().Generation,
            "cost observer bound to actual current renderer, not retired generation");
        var reports = new PresentationCostReport[16];
        long allocated = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
        for (int i = 0; i < reports.Length; i++)
        {
            draw();
            reports[i] = read() ?? throw new Exception("Missing relocated cost observation");
        }
        double milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        evidence.Pass(renderer.ReadFrameState().Generation == before.Generation &&
            renderer.Stats.ValidationErrors == 0 && renderer.Stats.ValidationWarnings == 0,
            "measured window has stable actual device and API0/0");
        ulong previous = before.HostFrame;
        foreach (var report in reports)
        {
            evidence.Pass(report.Completed && !report.IncludesDiagnosticCapture && report.Generation == before.Generation &&
                report.HostFrame > previous && report.CpuWallMilliseconds >= 0 && double.IsFinite(report.CpuWallMilliseconds) &&
                report.OwnerThreadAllocatedBytes >= 0 && !report.FullPerformanceAccepted,
                "warm relocated cost identity, complete non-capture scope, finite CPU/allocation");
            previous = report.HostFrame;
            evidence.Pass(report.Counters is { MainSubmissions: 1, Presents: 1, UiUploadedBytes: 0,
                MeshUploadedBytes: 0, ResourceUploadedBytes: 0, EnvironmentUploadedBytes: 0 },
                "warm formal host submits once without immutable resource reupload");
            Sample(report, report.Main, GpuTimingDomain.MainSubmissionBeforePresent);
            Sample(report, report.Skin, GpuTimingDomain.SkinDispatch);
            Sample(report, report.Ui, GpuTimingDomain.UiProduction);
        }
        object? Origin(AttributedGpuCost? value) => value is { } sample ? new
        {
            sample.Sample.Timing.Domain,
            sample.Sample.Timing.Generation,
            sample.Sample.Timing.OriginFrame,
            diagnosticSource = _diagnostics.Contains((sample.Sample.Timing.Generation, sample.Sample.Timing.OriginFrame)),
            sample.Attribution
        } : null;
        _windows.Add(new
        {
            label, frames = reports.Length, hostCpuMilliseconds = milliseconds, hostOwnerAllocatedBytes = allocated,
            hostScope = "test host draw callback plus reading copied report; excludes startup/simulation; no capture/wait",
            reports, origins = reports.Select(r => new { main = Origin(r.Main), skin = Origin(r.Skin), ui = Origin(r.Ui) }).ToArray(),
            fullPerformanceAccepted = false
        });
    }

    private void Sample(PresentationCostReport report, AttributedGpuCost? value, GpuTimingDomain domain)
    {
        var attributed = value ?? throw new Exception("Missing bounded GPU observation");
        var timing = attributed.Sample.Timing;
        var expected = !timing.IsValid ? CostSampleAttribution.NoValidSample :
            timing.OriginFrame == report.HostFrame ? CostSampleAttribution.CurrentSourceFrame : CostSampleAttribution.EarlierSourceFrame;
        evidence.Pass(timing.Generation == report.Generation && timing.Domain == domain &&
            timing.OriginFrame <= report.HostFrame && attributed.Attribution == expected &&
            double.IsFinite(timing.Milliseconds) && timing.Milliseconds >= 0 && (timing.IsValid || timing.Milliseconds == 0),
            "GPU sample retains actual domain/device/source identity; pending/old never relabeled current");
    }

    internal void Save(string host) => File.WriteAllText(Path.Combine(evidence.Output, "presentation-costs.json"),
        JsonSerializer.Serialize(new { host, windows = _windows, diagnosticFrames = _diagnostics.Count,
            physicalDeviceLoss = false, fullPerformanceAccepted = false }));
}
