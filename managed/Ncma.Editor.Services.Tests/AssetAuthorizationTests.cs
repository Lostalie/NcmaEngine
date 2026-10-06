using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Editor.App;
using Ncma.Editor.Core;
using Ncma.Editor.Protocol;
using Ncma.Editor.Services;
using Ncma.Editor.Transport;
using Ncma.Gui;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;

internal static class AssetAuthorizationTests
{
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Asset authorization assertion failed."); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; } throw new Exception("Expected asset authorization rejection."); }
    private sealed class Fixture : IDisposable
    {
        public readonly Guid Root = Guid.NewGuid(), Sub = Guid.NewGuid(), Hidden = Guid.NewGuid(), Project = Guid.NewGuid();
        public readonly string Directory;
        public readonly EditorSessionOwner Owner = new("Asset read UI");
        public readonly EditorWorkspace Workspace;
        public readonly EditorAssetWorkflow Flow;
        public readonly EditorAssetAuthorizationController Controller;
        public readonly EditorPresenter View;
        public readonly AssetRecord Record;
        private ulong _frame;
        public Fixture(string output)
        {
            Directory = Path.Combine(output, "asset-read-ui-" + Guid.NewGuid().ToString("N")); System.IO.Directory.CreateDirectory(Path.Combine(Directory, "assets"));
            Record = new(1, Root, AssetKind.Character, "assets/PRIVATE.fbx", new string('A', 64), "ufbx", 1, new(1, 60, true),
                [new(Sub, AssetKind.Skeleton, "PRIVATE_KEY", "PRIVATE_NAME", false)], [], null);
            File.WriteAllBytes(Path.Combine(Directory, "assets/PRIVATE.fbx.ncmeta"), AssetRecordCodec.Encode(Record));
            File.WriteAllBytes(Path.Combine(Directory, "assets/HIDDEN.png.ncmeta"), AssetRecordCodec.Encode(new(1, Hidden, AssetKind.Texture,
                "assets/HIDDEN.png", new string('B', 64), "image", 1, new(1, 60, true), [], [], null)));
            Workspace = new(Owner); Flow = new(Workspace, Directory, Project, 1); Owner.ConfigureAssets(Directory, Project, 1, Flow.Scope);
            Controller = new(Workspace); View = new(Workspace, null, Directory, assetWorkflow: Flow);
        }
        public void Build() => View.Build(++_frame, 1280, 720);
        public GuiEvent Event(ulong high, ulong low, double number = 0)
        {
            var item = View.Items.ToArray().Single(i => i.WidgetHigh == high && i.WidgetLow == low); var frame = View.Frame;
            return new() { Kind = item.Kind, WidgetHigh = high, WidgetLow = low, Phase = 3, Value = number, Frame = frame.Frame,
                ViewGeneration = frame.ViewGeneration, DocumentGeneration = frame.DocumentGeneration, Revision = frame.Revision };
        }
        public void Click(ulong high, ulong low, double number = 0) => View.Apply([Event(high, low, number)], []);
        public ulong RootWidget => View.Items.ToArray().Single(i => i.WidgetHigh == 16 && Encoding.UTF8.GetString(View.Text.Slice((int)i.TextOffset, (int)i.TextLength)) == Root.ToString("D")).WidgetLow;
        public void ReviewRoot()
        {
            Build(); Click(20, RootWidget, 1); Build(); Click(21, 1); Build();
        }
        public CapabilityResult Call(Guid asset) => Owner.Edit!.Invoke(new(2, Guid.NewGuid(), Owner.Edit.SessionId, Owner.Edit.Revision,
            "ncma.assets.inspect", JsonSerializer.SerializeToElement(new { assetId = asset, section = "summary" })));
        public void Dispose()
        {
            var assets = Owner.Assets!; Flow.Dispose(); Owner.Dispose(); Check(assets.Completion.Wait(TimeSpan.FromSeconds(10)));
        }
    }
    private sealed class Client : IDisposable
    {
        public readonly NamedPipeClientStream Pipe;
        public readonly Task<byte[]?> Handshake;
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(30));
        public Client(EditorEndpoint endpoint, string name)
        {
            var descriptor = Wire.Decode<EndpointDescriptor>(File.ReadAllBytes(endpoint.DescriptorPath));
            Pipe = new(".", descriptor.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            Pipe.Connect(3000); Wire.WriteAsync(Pipe, Wire.Encode(new Hello(1, descriptor.InstanceId, descriptor.ProjectRoot, name)), _stop.Token).GetAwaiter().GetResult();
            Handshake = Wire.ReadAsync(Pipe, _stop.Token);
        }
        public void Dispose() { Pipe.Dispose(); _stop.Dispose(); }
    }
    private static void Until(EditorEndpoint endpoint, Func<bool> done)
    {
        var watch = Stopwatch.StartNew(); while (!done()) { if (watch.ElapsedMilliseconds > 5000) throw new TimeoutException("Asset UI IPC timeout."); endpoint.Pump(); Thread.Sleep(1); }
    }
    private static Client Pair(Fixture f)
    {
        if (f.Owner.Endpoint is null) f.Owner.ConfigureEndpoint(true, f.Directory);
        var endpoint = f.Owner.Endpoint!; int count = endpoint.View.Connections.Length;
        var client = new Client(endpoint, "Asset read human review test");
        Until(endpoint, () => endpoint.View.Connections.Length > count);
        var authorization = new EditorAuthorizationController(f.Workspace); var page = authorization.Capture()!;
        authorization.Pair(page, page.Endpoint.Connections.Single(c => !c.Paired && c.Connected).ConnectionId, true);
        Until(endpoint, () => client.Handshake.IsCompleted); Check(Wire.Decode<HelloResult>(client.Handshake.Result!).Code == "ok"); return client;
    }
    public static IEnumerable<(string, Action)> Cases(string output, string repository, string plugins)
    {
        yield return ("M3.8 trusted asset review binds exact rows, endpoint/audience and rejects tampering", () => {
            using var f = new Fixture(output); Reject(() => f.Controller.Capture([f.Root])); f.Owner.ConfigureEndpoint(true, f.Directory);
            Reject(() => f.Controller.Capture([f.Root])); using var client = Pair(f);
            byte[] before = f.Owner.Document.CaptureBytes(); var history = f.Owner.Edit!.State;
            var review = f.Controller.Capture([f.Root]);
            Reject(() => f.Controller.Approve(review, review.Fingerprint, false)); Reject(() => f.Controller.Approve(review, "wrong", true));
            Reject(() => f.Controller.Approve(review with { Assets = [review.Assets[0] with { AssetId = f.Hidden }] }, review.Fingerprint, true));
            Reject(() => f.Controller.Approve(review with { Audience = [] }, review.Fingerprint, true));
            Reject(() => f.Controller.Capture([f.Root, f.Root])); Reject(() => f.Controller.Capture([Guid.NewGuid()]));
            f.Controller.Approve(review, review.Fingerprint, true); Check(f.Call(f.Root).Status == "ok" && f.Call(f.Sub).Code == "asset_not_visible" && f.Call(f.Hidden).Code == "asset_not_visible");
            var grant = f.Owner.AssetInspections!.Grant; Check(grant.RemainingSeconds is > 0 and <= 60 && grant.AssetIds.SequenceEqual([f.Root]));
            grant.AssetIds[0] = f.Hidden; Check(f.Call(f.Hidden).Code == "asset_not_visible");
            f.Owner.ConfigureEndpoint(false, f.Directory); Check(f.Owner.AssetInspections.Grant.AssetIds.Length == 0 && !f.Controller.IsCurrent(review));
            Reject(() => f.Controller.Approve(review, review.Fingerprint, true)); Check(before.SequenceEqual(f.Owner.Document.CaptureBytes()) && history == f.Owner.Edit.State);
        });
        yield return ("M3.8 asset GUI disabled/forged/unreviewed/stale approval cannot grant reads", () => {
            using var f = new Fixture(output); f.Build(); f.Click(21, 1); Check(f.Owner.AssetInspections!.Grant.AssetIds.Length == 0);
            using var client = Pair(f); f.ReviewRoot(); f.Click(21, 3); Check(f.Call(f.Root).Code == "asset_not_visible");
            var fake = f.Event(21, 2, 1); fake.Kind = (uint)GuiItemKind.Text; f.View.Apply([fake], []); f.Build(); f.Click(21, 3); Check(f.Call(f.Root).Code == "asset_not_visible");
            f.Click(21, 2, 1); f.Build(); var stale = f.Event(21, 3); f.Click(20, f.RootWidget, 0); f.View.Apply([stale], []); Check(f.Call(f.Root).Code == "asset_not_visible");
            f.ReviewRoot(); f.Click(21, 2, 1); f.Build(); f.Click(21, 3); Check(f.Call(f.Root).Status == "ok" && f.Call(f.Sub).Code == "asset_not_visible");
            f.Build(); f.Click(16, f.RootWidget); f.Build();
            var roots = f.View.Items.ToArray().Where(i => i.WidgetHigh == 16).Select(i => i.WidgetLow).ToHashSet();
            var subWidget = f.View.Items.ToArray().Single(i => i.WidgetHigh == 20 && !roots.Contains(i.WidgetLow)).WidgetLow;
            f.Click(20, subWidget, 1); f.Build(); f.Click(21, 1); f.Build(); f.Click(21, 2, 1); f.Build(); f.Click(21, 3);
            Check(f.Call(f.Sub).Status == "ok" && f.Call(f.Hidden).Code == "asset_not_visible");
            f.Build(); f.Click(21, 4); Check(f.Call(f.Root).Code == "asset_not_visible" && f.Call(f.Sub).Code == "asset_not_visible");
            Check(f.Owner.Edit!.State.UndoCount == 0 && f.Owner.Document.World.Count == 0);
        });
        yield return ("M3.8 asset UI grant revokes on audience and asset/document changes", () => {
            using var f = new Fixture(output); using var one = Pair(f); var review = f.Controller.Capture([f.Root]); f.Controller.Approve(review, review.Fingerprint, true);
            using var two = Pair(f); Check(f.Owner.AssetInspections!.Grant.AssetIds.Length == 0 && !f.Controller.IsCurrent(review));
            review = f.Controller.Capture([f.Root]); f.Controller.Approve(review, review.Fingerprint, true);
            var endpoint = f.Owner.Endpoint!; endpoint.Revoke(endpoint.View.Connections.First().ConnectionId); Check(f.Call(f.Root).Code == "asset_not_visible");
            review = f.Controller.Capture([f.Root]); f.Controller.Approve(review, review.Fingerprint, true);
            File.WriteAllBytes(Path.Combine(f.Directory, "assets/PRIVATE.fbx.ncmeta"), AssetRecordCodec.Encode(f.Record with { Subassets = [f.Record.Subassets[0] with { Name = "CHANGED" }] }));
            f.Owner.RefreshAssets(true); Check(f.Owner.AssetInspections.Grant.AssetIds.Length == 0 && !f.Controller.IsCurrent(review));
            review = f.Controller.Capture([f.Root]); f.Controller.Approve(review, review.Fingerprint, true); f.Workspace.New(f.Workspace.Stamp, true);
            Check(f.Call(f.Root).Code == "asset_not_visible" && !f.Controller.IsCurrent(review));
        });
        yield return ("M3.8 real stdio MCP uses human UI review/revoke with no asset/scene write authority", () => {
            using var f = new Fixture(output); f.Owner.ConfigureEndpoint(true, f.Directory); var endpoint = f.Owner.Endpoint!;
            var launch = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            launch.ArgumentList.Add(Path.Combine(repository, "out/managed/editor-mcp/Ncma.Editor.Mcp.dll")); launch.ArgumentList.Add("--descriptor"); launch.ArgumentList.Add(endpoint.DescriptorPath);
            using var process = Process.Start(launch)!; var errors = process.StandardError.ReadToEndAsync();
            JsonElement Send(object message)
            {
                process.StandardInput.WriteLine(JsonSerializer.Serialize(message, Wire.Json)); process.StandardInput.Flush(); var pending = process.StandardOutput.ReadLineAsync();
                var watch = Stopwatch.StartNew(); while (!pending.IsCompleted) {
                    if (watch.ElapsedMilliseconds > 10000) throw new TimeoutException("Asset UI stdio timeout.");
                    if (endpoint.View.Connections.Any(c => !c.Paired && c.Connected)) { f.Build(); f.Click(5, 100); }
                    endpoint.Pump(); Thread.Sleep(1);
                }
                using var document = JsonDocument.Parse(pending.GetAwaiter().GetResult() ?? throw new IOException("MCP closed.")); return document.RootElement.Clone();
            }
            Guid requestId = Guid.NewGuid();
            object Call(int id, string name, object input) => new { jsonrpc = "2.0", id, method = "tools/call", @params = new { name, arguments = new { contractVersion = 2,
                requestId, sessionId = f.Owner.Edit!.SessionId, expectedRevision = f.Owner.Edit.Revision, input } } };
            try {
                Check(Send(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-11-25" } }).GetProperty("result").GetProperty("protocolVersion").GetString() == "2025-11-25");
                process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"); process.StandardInput.Flush();
                object input = new { assetId = f.Root, section = "summary" };
                Check(Send(Call(2, "ncma.assets.inspect", input)).GetProperty("result").GetProperty("isError").GetBoolean());
                f.ReviewRoot(); f.Click(21, 2, 1); f.Build(); f.Click(21, 3);
                var result = Send(Call(3, "ncma.assets.inspect", input)).GetProperty("result"); Check(!result.GetProperty("isError").GetBoolean() && !result.GetRawText().Contains("PRIVATE"));
                f.Build(); f.Click(21, 4); Check(Send(Call(4, "ncma.assets.inspect", input)).GetProperty("result").GetProperty("structuredContent").GetProperty("code").GetString() == "asset_not_visible");
                requestId = Guid.NewGuid(); var mutation = Send(Call(5, "ncma.assets.import.commit", new { ticket = Guid.NewGuid(), expectedAssetRevision = f.Owner.Assets!.Clock.Revision, projectGeneration = 1, confirmNewIdentities = true })).GetProperty("result");
                Check(mutation.GetProperty("isError").GetBoolean() && f.Owner.Edit!.State.UndoCount == 0 && f.Owner.Document.World.Count == 0);
            }
            finally { process.StandardInput.Close(); if (!process.WaitForExit(5000)) process.Kill(true); }
            Check(process.ExitCode == 0 && errors.GetAwaiter().GetResult().Length == 0);
        });
        yield return ("M3.8 asset approval view renders through native GUI with locked metadata", () => {
            using var f = new Fixture(output); using var client = Pair(f); f.ReviewRoot();
            using var loader = new PluginLoader(); loader.Load(plugins, [
                new("platform", ModuleKind.Platform, "NcmaPlatform.dll", "NcmaPlatform.dll", 1, 0, []),
                new("renderer", ModuleKind.Renderer, "NcmaRenderer.dll", "NcmaRenderer.dll", 1, 1, ["platform"]),
                new("gui", ModuleKind.Gui, "NcmaGui.dll", "NcmaGui.dll", 1, 2, ["platform", "renderer"])]);
            using var window = new PlatformWindow(loader.Modules.Single(m => m.Kind == ModuleKind.Platform), "Asset approval validation", 1280, 720, false);
            using var renderer = new RendererSession(loader.Modules.Single(m => m.Kind == ModuleKind.Renderer), window, 1280, 720);
            using var pipeline = new RenderPipelineService(renderer); pipeline.Configure(RenderConfiguration.Default(Guid.NewGuid()), 680, 315);
            using var gui = new GuiSession(loader.Modules.Single(m => m.Kind == ModuleKind.Gui), window, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "msyh.ttc")); gui.AttachRenderer(renderer);
            using var locked = new FileStream(Path.Combine(f.Directory, "assets/PRIVATE.fbx.ncmeta"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            for (ulong frame = 1; frame <= 3; frame++) {
                var state = window.Poll(); gui.Begin(state, 1.0 / 60); f.Build();
                var stats = gui.Draw(f.View.Frame, f.View.Items, f.View.Text); Check(stats.Vertices > 0 && stats.EventOverflow == 0);
                pipeline.Submit(frame, 300, 405); gui.RenderGpu(); renderer.Present();
            }
            Check(renderer.Stats.ValidationErrors == 0 && renderer.Stats.ValidationWarnings == 0 && f.Owner.Edit!.State.UndoCount == 0);
        });
    }
}
