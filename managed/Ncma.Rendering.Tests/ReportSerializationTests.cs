using System.Text.Json;
using System.Text.Json.Serialization;
using Ncma.Application;
using Ncma.Editor.App;
using Ncma.Gameplay;
using Ncma.Interop;
using Ncma.Player.App;
using Ncma.Rendering;

internal static unsafe partial class Program
{
    // The superseded reflection implementation exists only as an independent test oracle.
    private static JsonSerializerOptions OriginalPlayerJson() => new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }, WriteIndented = true };

    private static int ReportSerializationTests(string output, string? originalFile = null)
    {
        string directory = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var original = OriginalPlayerJson();
        int checks = 0;
        void Pass(bool value, string message) { Check(value, "Report JSON: " + message); checks++; }
        void Compare(PlayerReport value, string name)
        {
            byte[] expected = JsonSerializer.SerializeToUtf8Bytes(value, original);
            byte[] actual = JsonSerializer.SerializeToUtf8Bytes(value, PlayerReportJsonContext.Default.PlayerReport);
            Pass(expected.SequenceEqual(actual), name + " exact UTF8 reference bytes");
            string path = Path.Combine(directory, name + ".json");
            PlayerReports.Write(path, value);
            Pass(File.ReadAllBytes(path).SequenceEqual(expected), name + " actual Write/Flush bytes");
            bool rejected = false;
            try { PlayerReports.Write(path, value); } catch (ArgumentException) { rejected = true; }
            Pass(rejected && File.ReadAllBytes(path).SequenceEqual(expected), name + " existing file preserved");
        }

        if (originalFile is not null)
        {
            byte[] bytes = File.ReadAllBytes(originalFile);
            var value = JsonSerializer.Deserialize<PlayerReport>(bytes, original) ?? throw new InvalidOperationException("Missing report.");
            Pass(JsonSerializer.SerializeToUtf8Bytes(value, original).SequenceEqual(bytes), "actual archived report roundtrip bytes");
            Compare(value, "actual-report");
            Console.WriteLine($"PASS report serialization {checks} checks; actual={originalFile}; output={directory}");
            return 0;
        }

        var basic = new PlayerReport(1, "Headless", "Null", Guid.Empty, Guid.Empty, Guid.Empty,
            null, 0, 1.0 / 60, PlayState.Stopped, 0, "关闭\n\"<>&\\ 🚀", null, [], null, null, 0, 0, [], 0, 0, 0);
        Pass(PlayerReportJsonContext.Default.PlayerReport.SerializeHandler is not null &&
            PlayerReportJsonContext.Default.PlayerReport.Properties.Count == 0,
            "output-only generated writer without unused root property metadata");
        Compare(basic, "default-off");
        using (var json = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(basic, PlayerReportJsonContext.Default.PlayerReport)))
        {
            Pass(!json.RootElement.TryGetProperty("memoryCheckpoints", out _) &&
                !json.RootElement.TryGetProperty("resourceMemory", out _) &&
                !json.RootElement.TryGetProperty("loopCosts", out _), "disabled diagnostics absent");
            Pass(json.RootElement.GetProperty("fault").ValueKind == JsonValueKind.Null &&
                json.RootElement.GetProperty("state").GetString() == "Stopped", "original null/string enum contract");
        }
        var processSample = default(ProcessMemorySample) with { Checkpoint = MemoryCheckpoint.RuntimeReady,
            ProcessId = 1, ObserverThreadId = 2, StartedTimestamp = 10, EndedTimestamp = 11,
            RawManagedHeapBytes = -1, ProcessCounterFailure = "拒绝\n\"<>&\\", LastGcIndex = long.MaxValue };
        var process = new ProcessMemoryReport(1, long.MaxValue, [processSample]);
        var ledger = new RendererResourceLedger(ulong.MaxValue, true, true, false,
            Enum.GetValues<RendererResourceDomain>().Select(d => new RendererResourceReading(d, ulong.MaxValue, 0)).ToArray());
        var resource = new RendererMemoryReport(1, 5, 0,
            Enum.GetValues<RendererMemoryState>().Append((RendererMemoryState)987).Select((s, i) =>
                new RendererMemorySample((ulong)i + 1, (RendererMemoryCheckpoint)i, s, 1, 2, -1,
                    s == RendererMemoryState.Available ? ledger : null, s == RendererMemoryState.ReadFailed ? "failure\n" : null,
                    s == RendererMemoryState.Unsupported ? PluginResult.UnsupportedFeature : null)).ToArray());
        foreach (var p in new[] { process, process with { Samples = [] }, process with {
            Samples = [processSample with { Checkpoint = (MemoryCheckpoint)999, WorkingSetBytes = long.MaxValue,
                PrivateBytes = 0, PeakWorkingSetBytes = long.MaxValue, ProcessCounterFailure = null }] } })
            Pass(MemoryDiagnosticJson.Process(p) == JsonSerializer.Serialize(new { memoryCheckpoints = p }), "Editor process numeric enums/null/failure/empty exact bytes");
        foreach (var r in new[] { resource, resource with { Samples = [], OmittedObservations = ulong.MaxValue } })
            Pass(MemoryDiagnosticJson.Resource(r) == JsonSerializer.Serialize(new { resourceMemory = r }), "Editor resource nested/unknown enums/empty exact bytes");

        var full = basic with {
            Mode = "Windowed", Renderer = "Direct3D11", State = PlayState.Faulted, TargetTicks = ulong.MaxValue,
            Tick = ulong.MaxValue, ExitCode = 1, ObjectCount = int.MaxValue, ElapsedSeconds = double.Epsilon,
            Fault = new("fault", "shutdown", Guid.NewGuid(), Guid.NewGuid(), ulong.MaxValue, null, Guid.Empty, null, "错误\t", 1),
            ShutdownErrors = ["first", "second\n"], ReportError = "写入失败", StateSha256 = "hash",
            Modules = [new("native\"", uint.MaxValue, 0, ulong.MaxValue)], FrameRateLimit = 30,
            MemoryCheckpoints = process, ResourceMemory = resource,
            PresentationCosts = new(1, 2, true, false, 0.125, -1, default(PresentationCounters),
                new(default, CostSampleAttribution.CurrentSourceFrame), new(default, (CostSampleAttribution)999), null),
            SimulationCosts = new(default, new Ncma.Characters.CharacterStepProfile(Guid.Empty, Guid.Empty, 1,
                1, 2, 3, 4, 5, 6, default)),
            LoopCosts = new(2, 1, 1, 1, 0, 0, 0, 0, [new(1, 1, LoopCostOutcome.Faulted, 1, 2, -1, default, null, null, null)], 30) {
                Stream = new(1, Guid.Empty, "failed", 1, 1, 1, 1, 0, 0, 0, 1, 0, 2, -1, 3, -1, 0, "IO\n") },
            PresentationStages = new(1, 1, 1, 1, 0, [new(1, 1, PresentationCall.Present, false, true, 1, 2, -1,
                [new(PresentationStage.Diagnostics, 1, 1, -1)])]),
            Output = new(new(1920, 1080), new(1920, 1080), null, 0, 0, 0) { RequestedMode = Ncma.Platform.WindowMode.BorderlessPixels }
        };
        Compare(full, "all-diagnostics");
        Compare(full with { State = (PlayState)uint.MaxValue, Output = full.Output with { LastPresentedSize = new(853, 479) } }, "unknown-enums");
        Compare(full with { MemoryCheckpoints = process with { Samples = Enumerable.Repeat(processSample, 4).ToArray() },
            ResourceMemory = resource with { TotalObservations = 33, OmittedObservations = 1,
                Samples = Enumerable.Repeat(resource.Samples[1], RendererMemoryCapture.Capacity).ToArray() } }, "bounded-prefix");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var value = basic with { ElapsedSeconds = invalid };
            bool oldRejected = false, newRejected = false;
            try { JsonSerializer.SerializeToUtf8Bytes(value, original); } catch (ArgumentException) { oldRejected = true; }
            string path = Path.Combine(directory, "nonfinite-" + checks + ".json");
            try { PlayerReports.Write(path, value); } catch (ArgumentException) { newRejected = true; }
            Pass(oldRejected && newRejected && !File.Exists(path), "nonfinite reject before output");
        }
        string oversized = Path.Combine(directory, "oversized.json");
        bool budgetRejected = false;
        try { PlayerReports.Write(oversized, basic with { Reason = new string('x', PlayerReports.MaxBytes) }); }
        catch (ArgumentException error) { budgetRejected = error.Message == "report_budget"; }
        Pass(budgetRejected && !File.Exists(oversized), "unchanged report budget/no partial file");
        Console.WriteLine($"PASS report serialization {checks} checks; output={directory}");
        return 0;
    }
}
