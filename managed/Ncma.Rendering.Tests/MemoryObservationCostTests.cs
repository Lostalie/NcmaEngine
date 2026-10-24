using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ncma.Application;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;

internal static unsafe partial class Program
{
    private sealed record MemoryOperationCost(string Scenario, int Round, string Operation, bool Completed,
        string? FailureType, long StartedTimestamp, long EndedTimestamp, long RawOwnerAllocatedBytes,
        int Gen0Before, int Gen1Before, int Gen2Before, int Gen0After, int Gen1After, int Gen2After)
    {
        public bool AllocationReadingValid => RawOwnerAllocatedBytes >= 0;
        public bool CollectionObserved => Gen0Before != Gen0After || Gen1Before != Gen1After || Gen2Before != Gen2After;
    }

    private static int MemoryObservationCostTests(string plugins, string output)
    {
        string directory = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var rows = new List<MemoryOperationCost>(160);
        var reports = new List<object>(8);
        int checks = 0;
        bool complete = false;
        void Pass(bool condition, string message) { Check(condition, "Memory operation costs: " + message); checks++; }
        // Delegate preparation and the collector's own record/list/report are outside the interval.
        // The complete called API, including returned objects/arrays, is inside it. No GC controls.
        T Measure<T>(string scenario, int round, string operation, Func<T> action)
        {
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            bool succeeded = false; string? failure = null;
            try { T result = action(); succeeded = true; return result; }
            catch (Exception error) { failure = error.GetType().Name; throw; }
            finally
            {
                long end = Stopwatch.GetTimestamp(), allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                rows.Add(new(scenario, round, operation, succeeded, failure, start, end, allocated,
                    g0, g1, g2, GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)));
            }
        }
        var playerJson = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }, WriteIndented = true };
        try
        {
            using var loader = new PluginLoader(); loader.Load(Path.GetFullPath(plugins), Specs().Take(2).ToArray());
            using var window = new PlatformWindow(loader.Modules.Single(m => m.Kind == ModuleKind.Platform), "Memory cost calibration", 64, 64, false);
            using var renderer = new RendererSession(loader.Modules.Single(m => m.Kind == ModuleKind.Renderer), window, 64, 64, pureUi: true);
            using var image = renderer.CreateUiImage(1, 1, [255, 255, 255, 255]);
            var originalLedger = renderer.CaptureResourceLedger();
            // First measured calls and seven subsequent rounds, not cold-process or representative gameplay.
            for (int round = 0; round < 8; round++)
            {
                string scenario = round < 4 ? "NoRenderer" : "LiveDx11";
                RendererSession? selected = round < 4 ? null : renderer;
                var process = Measure(scenario, round, "ConstructProcess", () => new ProcessMemoryCapture());
                var resource = Measure(scenario, round, "ConstructResource", () => new RendererMemoryCapture());
                foreach (MemoryCheckpoint checkpoint in Enum.GetValues<MemoryCheckpoint>())
                {
                    Measure(scenario, round, "ObserveProcess." + checkpoint, () => { process.Observe(checkpoint); return true; });
                    var stage = checkpoint switch {
                        MemoryCheckpoint.BeforeStartup => RendererMemoryCheckpoint.BeforeStartup,
                        MemoryCheckpoint.RuntimeReady => RendererMemoryCheckpoint.RuntimeReady,
                        MemoryCheckpoint.BeforeShutdown => RendererMemoryCheckpoint.BeforeShutdown,
                        _ => RendererMemoryCheckpoint.AfterShutdown };
                    Measure(scenario, round, "ObserveResource." + stage, () => {
                        resource.Observe(stage, checkpoint is MemoryCheckpoint.RuntimeReady or MemoryCheckpoint.BeforeShutdown ? selected : null);
                        return true;
                    });
                }
                var processReport = Measure(scenario, round, "CaptureProcess", process.Capture);
                var resourceReport = Measure(scenario, round, "CaptureResource", resource.Capture);
                string processLine = Measure(scenario, round, "SerializeEditorProcess", () => Ncma.Editor.App.MemoryDiagnosticJson.Process(processReport));
                string resourceLine = Measure(scenario, round, "SerializeEditorResource", () => Ncma.Editor.App.MemoryDiagnosticJson.Resource(resourceReport));
                // This is exactly the memory fragment, not the entire PlayerReport or its other diagnostics.
                byte[] fragment = Measure(scenario, round, "SerializePlayerMemoryFragment", () => JsonSerializer.SerializeToUtf8Bytes(
                    new { memoryCheckpoints = processReport, resourceMemory = resourceReport }, playerJson));
                Measure(scenario, round, "WriteEditorMemoryLines", () => { Console.WriteLine(processLine); Console.WriteLine(resourceLine); return true; });
                string fragmentPath = Path.Combine(directory, $"memory-fragment-{round}.json");
                Measure(scenario, round, "WritePlayerMemoryFragment", () => {
                    using var file = new FileStream(fragmentPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    file.Write(fragment); file.Flush(true); return true;
                });
                using var json = JsonDocument.Parse(fragment);
                Pass(processLine == JsonSerializer.Serialize(new { memoryCheckpoints = processReport }), "Editor process JSON exact reference bytes");
                Pass(resourceLine == JsonSerializer.Serialize(new { resourceMemory = resourceReport }), "Editor resource JSON exact reference bytes");
                Pass(json.RootElement.GetProperty("memoryCheckpoints").GetProperty("samples").GetArrayLength() == 4 &&
                    json.RootElement.GetProperty("resourceMemory").GetProperty("samples").GetArrayLength() == 4, "serialized sample counts");
                Pass(processReport.Samples.Length == 4 && resourceReport.TotalObservations == 4 && resourceReport.OmittedObservations == 0, "complete fixed observations");
                Pass(resourceReport.Samples[1].State == (selected is null ? RendererMemoryState.NoRenderer : RendererMemoryState.Available), "actual renderer state");
                Pass(selected is null || resourceReport.Samples[1].Ledger!.Domains[5].LogicalBytes == 4, "actual native resource bytes");
                processReport.Samples[0] = default;
                Pass(process.Capture().Samples[0].ProcessId == Environment.ProcessId, "process snapshot isolation");
                if (selected is not null)
                {
                    resourceReport.Samples[1].Ledger!.Domains[5] = default;
                    Pass(resource.Capture().Samples[1].Ledger!.Domains[5].LogicalBytes == 4, "nested snapshot isolation");
                }
                // Preserve the original JSON bytes; mutation probes above never rewrite emitted samples.
                reports.Add(new { scenario, round, processBytes = System.Text.Encoding.UTF8.GetByteCount(processLine),
                    resourceBytes = System.Text.Encoding.UTF8.GetByteCount(resourceLine), fragmentBytes = fragment.Length });
            }
            var afterLedger = renderer.CaptureResourceLedger();
            Pass(originalLedger.Generation == afterLedger.Generation && originalLedger.Domains.SequenceEqual(afterLedger.Domains), "observation creates no renderer resources");
            Pass(rows.Count == 136 && rows.All(r => r.Completed && r.FailureType is null && r.EndedTimestamp >= r.StartedTimestamp), "all 17 operations per round recorded");
            Pass(rows.Select(r => (r.Round, r.Operation)).Distinct().Count() == rows.Count, "no replacement samples");
            Pass(rows.Zip(rows.Skip(1)).All(pair => pair.First.EndedTimestamp <= pair.Second.StartedTimestamp), "ordered nonoverlapping measurement intervals");
            complete = true;
        }
        finally
        {
            // Write even a failed prefix once. Never manufacture successful completion or discard evidence.
            using var file = new FileStream(Path.Combine(directory, "memory-operation-costs.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(file, new { version = 1, checks, complete, timestampFrequency = Stopwatch.Frequency,
                normalGc = true, performanceAccepted = false, memoryAccepted = false,
                scope = "caller-scoped constructor/Observe/Capture/JSON/output calls; includes result creation, excludes delegate preparation and collector bookkeeping; first measured calls not cold-process; memory fragment not whole PlayerReport; native 64x64 pure-UI diagnostic not representative content; sequential GC counters not pause times; stdout sink affects write costs",
                rows, reports }, new JsonSerializerOptions { WriteIndented = true });
        }
        Console.WriteLine($"PASS memory operation costs {checks} checks; rows={rows.Count}; report={directory}");
        return 0;
    }
}
