#if NCMA_AI
using System.Diagnostics.Tracing;
using System.Text.Json;
using Ncma.Application;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Scene;

// Actual compiled host API, not a relocated apphost or a human UI acceptance test.
// No transport factory substitution: unconfigured/configured-but-unused production AI.
internal static class AiOfflineHostTests
{
    private static void Check(bool value) { if (!value) throw new Exception("AI offline host assertion"); }
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
    internal static IEnumerable<(string, Action)> Cases(string repository, string plugins, string output)
    {
        foreach (bool cpu in new[] { true, false }) foreach (bool configure in new[] { false, true }) {
            bool cpuOnly = cpu, configured = configure;
            yield return ($"M8.4-E offline {(cpuOnly ? "CPU GUI" : "DX11")} {(configured ? "configured-unused" : "unconfigured")} edit Play Undo close", () => {
                string root = Path.Combine(output, "ai-offline-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
                foreach (string suffix in new[] { ".dll", ".deps.json" })
                    File.Copy(Path.Combine(repository, "out/managed/Ncma.Gameplay.Sample" + suffix), Path.Combine(root, "gameplay" + suffix));
                var document = new SceneDocument("Offline AI host"); SceneDocumentFiles.Save(document, Path.Combine(root, "start.ncmascene"));
                string path = Path.Combine(root, "project.ncmaproject");
                File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new ProjectConfiguration(1, Guid.NewGuid(), "Offline AI", "start.ncmascene", "gameplay.dll", "Direct3D11", []),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                using var network = new NetworkEvents();
                using var host = new CandidatePresentation(plugins, ProjectContext.Load(path), true, cpuOnly, false);
                host.Start();
                var ai = host.VerificationAi; Check(ai.Status == "unconfigured" && ai.Profile is null && ai.Diagnostic is null);
                if (configured) Check(ai.ConfigureLocal("http://127.0.0.1:1/v1/chat/completions", "offline-fixture")); // never contacted; no listener started
                var owner = host.VerificationEditor; var ui = new EditorWorkspace(owner);
                byte[] original = owner.Document.CaptureBytes();
                Guid id = ui.CreateObject(ui.Stamp);
                ui.Transaction(ui.Stamp, "Offline rename", [new { op = "rename", objectId = id, name = "Offline object" }], id);
                Check(owner.Document.GetObjectName(id) == "Offline object" && owner.Edit!.State.UndoCount == 2);
                byte[] edited = owner.Document.CaptureBytes();
                ulong frame = 0;
                void Frame() {
                    var context = new FrameContext(++frame, 1.0 / 60);
                    host.PumpPlatform(); host.ApplyEditorIntents(context); host.PumpAgent(context);
                    host.BeginPresentation(context); host.SubmitInput(context); host.AdvancePlay(context); host.Present(context);
                }
                Frame(); ui.PlayControl(ui.Stamp, "start");
                var play = owner.Play!; ulong startTick = play.Scene.World.Tick;
                for (int i = 0; i < 8; i++) Frame();
                Check(ReferenceEquals(owner.Play, play) && play.Scene.World.Tick > startTick && edited.SequenceEqual(owner.Document.CaptureBytes()));
                ui.PlayControl(ui.Stamp, "stop"); Frame();
                Check(owner.Play is null && edited.SequenceEqual(owner.Document.CaptureBytes()) && owner.Edit!.State.UndoCount == 2);
                ui.History(ui.Stamp, false); ui.History(ui.Stamp, false);
                Check(original.SequenceEqual(owner.Document.CaptureBytes()));
                ui.History(ui.Stamp, true); ui.History(ui.Stamp, true); Check(edited.SequenceEqual(owner.Document.CaptureBytes()));
                Check(ai.Diagnostic is null && ai.Status == (configured ? "configured-not-connected" : "unconfigured"));
                if (!cpuOnly) Check(host.VerificationStats.ValidationErrors == 0);
                host.Dispose(); host.Dispose();
                var closed = host.VerificationAiLifetime;
                Check(closed.Closed && closed.Modules == 0 && closed.Editor is null && closed.Gui is null && closed.Renderer is null && ai.Status == "closed");
                Check(network.Count == 0); // managed HTTP/socket/DNS EventSources only, not an OS firewall assertion
                File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new { cpuOnly, configured, frames = frame,
                    playAdvanced = true, originalHistory = true, managedNetworkEvents = network.Count, actualHost = true, relocatedApphost = false, humanUiAcceptance = false }));
            });
        }
    }
}
#endif
