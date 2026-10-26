using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Ncma.Application;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Scene;

// Runs inside the checked product ALC. Not a shipped CLI or human UI acceptance.
internal static class AiRelocatedProductProbe
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class NetworkEvents : EventListener
    {
        private int count;
        internal int Count => Volatile.Read(ref count);
        protected override void OnEventSourceCreated(EventSource source) {
            if (source.Name is "System.Net.Http" or "System.Net.Sockets" or "System.Net.NameResolution")
                EnableEvents(source, EventLevel.Verbose, EventKeywords.All);
        }
        protected override void OnEventWritten(EventWrittenEventArgs data) => Interlocked.Increment(ref count);
    }
    internal static void Run(string requestPath)
    {
        using var request = JsonDocument.Parse(File.ReadAllBytes(requestPath)); var value = request.RootElement;
        string product = value.GetProperty("productRoot").GetString()!, output = value.GetProperty("output").GetString()!;
        bool cpu = value.GetProperty("cpuOnly").GetBoolean(), ai = value.GetProperty("ai").GetBoolean(), configured = value.GetProperty("configured").GetBoolean();
        Check(ai || !configured, "Default product cannot configure AI");
        string game = Path.Combine(output, "game"); Directory.CreateDirectory(game);
        foreach (string suffix in new[] { ".dll", ".deps.json" })
            File.Copy(Path.Combine(product, "sample", "Ncma.Gameplay.Sample" + suffix), Path.Combine(game, "gameplay" + suffix));
        SceneDocumentFiles.Save(new SceneDocument("Relocated offline host"), Path.Combine(game, "start.ncmascene"));
        string project = Path.Combine(game, "project.ncmaproject");
        File.WriteAllBytes(project, JsonSerializer.SerializeToUtf8Bytes(new ProjectConfiguration(1, Guid.NewGuid(), "Relocated offline host", "start.ncmascene", "gameplay.dll", "Direct3D11", []),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        using var network = new NetworkEvents();
        using var host = new CandidatePresentation(Path.Combine(product, "plugins"), ProjectContext.Load(project), true, cpu, false);
        host.Start();
        if (ai) CheckAi(host, configured, false);
        var owner = host.VerificationEditor; var ui = new EditorWorkspace(owner);
        byte[] original = owner.Document.CaptureBytes();
        int originalUndo = owner.Edit!.State.UndoCount;
        Guid id = ui.CreateObject(ui.Stamp);
        ui.Transaction(ui.Stamp, "Relocated rename", [new { op = "rename", objectId = id, name = "Relocated object" }], id);
        byte[] edited = owner.Document.CaptureBytes();
        Check(owner.Document.GetObjectName(id) == "Relocated object" && owner.Edit.State.UndoCount == originalUndo + 2, "Original create/rename/history");
        ulong frame = 0;
        void Frame() {
            var context = new FrameContext(++frame, 1.0 / 60);
            host.PumpPlatform(); host.ApplyEditorIntents(context); host.PumpAgent(context);
            host.BeginPresentation(context); host.SubmitInput(context); host.AdvancePlay(context); host.Present(context);
        }
        Frame(); ui.PlayControl(ui.Stamp, "start");
        var play = owner.Play!; ulong beforeTick = play.Scene.World.Tick;
        for (int i = 0; i < 8; i++) Frame();
        ulong afterTick = play.Scene.World.Tick;
        Check(ReferenceEquals(owner.Play, play) && afterTick > beforeTick && edited.SequenceEqual(owner.Document.CaptureBytes()), "Actual runtime advance and author isolation");
        ui.PlayControl(ui.Stamp, "stop"); Frame();
        Check(owner.Play is null && edited.SequenceEqual(owner.Document.CaptureBytes()) && owner.Edit.State.UndoCount == originalUndo + 2, "Stop isolation");
        ui.History(ui.Stamp, false); ui.History(ui.Stamp, false); Check(original.SequenceEqual(owner.Document.CaptureBytes()), "Undo byte oracle");
        ui.History(ui.Stamp, true); ui.History(ui.Stamp, true); Check(edited.SequenceEqual(owner.Document.CaptureBytes()), "Redo byte oracle");
        if (!cpu) Check(host.VerificationStats.ValidationErrors == 0, "DX11 validation");
        if (ai) CheckAi(host, configured, true);
        host.Dispose(); host.Dispose();
        if (ai) CheckAiClosed(host);
        Check(network.Count == 0, "Unexpected managed HTTP/socket/DNS events");
        File.WriteAllText(Path.Combine(output, "probe.json"), JsonSerializer.Serialize(new {
            actualRelocatedProductHost = true, actualApphost = false, cpuOnly = cpu, ai, configured, frames = frame,
            beforeTick, afterTick, originalHistory = true, managedNetworkEvents = network.Count,
            osNetworkIsolationProven = false, humanUiAcceptance = false
        }));
    }
    // Separate JIT boundary: the default product has no AI members or AI libraries.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckAi(CandidatePresentation host, bool configured, bool after)
    {
#if NCMA_AI
        var ai = host.VerificationAi;
        if (!after) {
            Check(ai.Status == "unconfigured" && ai.Profile is null && ai.Diagnostic is null, "No automatic AI setup");
            if (configured) Check(ai.ConfigureLocal("http://127.0.0.1:1/v1/chat/completions", "relocated-offline-fixture"), "Local profile only");
        }
        Check(ai.Diagnostic is null && ai.Status == (configured ? "configured-not-connected" : "unconfigured"), "No automatic inference/probe");
#else
        throw new InvalidOperationException("AI probe requires isolated optional harness");
#endif
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckAiClosed(CandidatePresentation host)
    {
#if NCMA_AI
        var state = host.VerificationAiLifetime;
        Check(state.Closed && state.Modules == 0 && state.Editor is null && state.Gui is null && state.Renderer is null && state.Window is null && state.Physics is null, "Closed native/managed owners");
#else
        throw new InvalidOperationException("AI probe requires isolated optional harness");
#endif
    }
}
