using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Ncma.Animation;
using Ncma.Editor.App;
using Ncma.Editor.Core;
using Ncma.Editor.Protocol;
using Ncma.Editor.Services;
using Ncma.Gui;

internal static class GraphInspectionTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Graph MCP assertion failed."); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or JsonException) { return; } throw new Exception("Invalid graph review accepted."); }
    internal sealed class Clock : TimeProvider
    {
        internal long Now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Now;
    }
    internal sealed class Fixture : IDisposable
    {
        internal readonly string Root, File;
        internal readonly EditorSessionOwner Owner = new("Graph inspection");
        internal readonly EditorWorkspace Workspace;
        internal readonly AnimationGraphInspections Graphs;
        internal readonly EditorAnimationGraphWorkspace? Writer;
        internal readonly EditorPresenter View;
        internal readonly Guid Graph = Guid.NewGuid(), Clip = Guid.NewGuid(), Rig = Guid.NewGuid();
        internal readonly Clock Time = new();
        internal readonly Process Client;
        private readonly Task<string> _errors;
        private ulong _frame;
        private int _id;
        internal Fixture(string output, string repository, bool authoring=false)
        {
            Guid project=Guid.NewGuid();
            if(authoring){var sample=Ncma.Samples.ActionSample.Create(Path.Combine(output,"m6-4-mcp"),Path.Combine(repository,"out/managed/Ncma.Gameplay.Sample.dll"),0);Root=sample.Root;project=sample.ProjectId;var record=Ncma.Assets.AssetRecordCodec.Decode(System.IO.File.ReadAllBytes(Path.Combine(Root,"assets/procedural.fbx.ncmeta")));Rig=record.Subassets.Single(s=>s.Kind==Ncma.Assets.AssetKind.Skeleton).AssetId;Clip=record.Subassets.First(s=>s.Kind==Ncma.Assets.AssetKind.Clip).AssetId;}
            else Root = Path.Combine(output, "m6-2-graph-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(Root, "assets")); File = Path.Combine(Root, "assets/test.ncmaanim");
            var clip = AnimationGraphNode.Create(Guid.NewGuid(), "approved clip", AnimationNodeKind.Clip) with { ClipId = Clip, Speed = 1, Loop = true };
            var end = AnimationGraphNode.Create(Guid.NewGuid(), "Output", AnimationNodeKind.Output);
            var d = new AnimationGraphDefinition(AnimationGraphCodec.CurrentVersion, Graph, "Approved graph", Rig, Guid.Empty, [], [clip, end], [new(Guid.NewGuid(), clip.Id, "pose", end.Id, "pose")], [], []);
            System.IO.File.WriteAllBytes(File, AnimationGraphCodec.Encode(d)); Workspace = new(Owner);
            if(authoring){Writer=new(Workspace,Root,project,Time);Owner.ConfigureAssets(Root,project,1,graphScope:Writer.Scope);Graphs=Writer.Reads;}else Graphs = new(Workspace, Root, Time);
            View = new(Workspace, null, Root, filePicker: kind => kind == Ncma.Platform.LocalFileKind.OpenAnimationGraph ? File : null, workspaceStyle: true);if(Writer is not null)View.AttachGraphAuthoring(Writer);else View.AttachGraphReads(Graphs);
            Owner.ConfigureEndpoint(true, Root);
            var launch = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            launch.ArgumentList.Add(Path.Combine(repository, "out/managed/editor-mcp/Ncma.Editor.Mcp.dll")); launch.ArgumentList.Add("--descriptor"); launch.ArgumentList.Add(Owner.Endpoint!.DescriptorPath);
            Client = Process.Start(launch)!; _errors = Client.StandardError.ReadToEndAsync();
            Send(new { jsonrpc = "2.0", id = ++_id, method = "initialize", @params = new { protocolVersion = "2025-11-25" } });
            Client.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"); Client.StandardInput.Flush();
            // initialize is transport-local. tools/list establishes the real paired editor connection.
            var tools = Send(new { jsonrpc = "2.0", id = ++_id, method = "tools/list" }).GetProperty("result").GetProperty("tools");
            Check(tools.EnumerateArray().Any(t => t.GetProperty("name").GetString() == "ncma.animgraph.inspect"));
            Build(); Click(24, 12); Build(); Click(24, 47); Build(); Click(40, 1); Build();
        }
        internal void Build() => View.Build(++_frame, 1280, 720);
        internal GuiEvent Event(ulong high, ulong low, double value = 0)
        {
            var item = View.Items.ToArray().Single(i => i.WidgetHigh == high && i.WidgetLow == low); var f = View.Frame;
            return new() { Kind = item.Kind, WidgetHigh = high, WidgetLow = low, Phase = 3, Value = value, Frame = f.Frame, ViewGeneration = f.ViewGeneration, DocumentGeneration = f.DocumentGeneration, Revision = f.Revision };
        }
        internal void Click(ulong high, ulong low, double value = 0) => View.Apply([Event(high, low, value)], []);
        internal JsonElement Send(object message)
        {
            Client.StandardInput.WriteLine(JsonSerializer.Serialize(message, Wire.Json)); Client.StandardInput.Flush(); var pending = Client.StandardOutput.ReadLineAsync(); var watch = Stopwatch.StartNew();
            while (!pending.IsCompleted) {
                if (watch.ElapsedMilliseconds > 10000) throw new TimeoutException("Graph MCP timeout.");
                var endpoint = Owner.Endpoint!; if (endpoint.View.Connections.Any(c => !c.Paired && c.Connected)) {
                    var authorization = new EditorAuthorizationController(Workspace); var page = authorization.Capture()!;
                    authorization.Pair(page, page.Endpoint.Connections.Single(c => !c.Paired && c.Connected).ConnectionId, true);
                }
                endpoint.Pump(); Thread.Sleep(1);
            }
            using var parsed = JsonDocument.Parse(pending.GetAwaiter().GetResult() ?? throw new IOException("MCP closed.")); return parsed.RootElement.Clone();
        }
        internal CapabilityResult Direct(Guid graph, Guid? request = null) => Owner.Edit!.Invoke(new(2, request ?? Guid.NewGuid(), Owner.Edit.SessionId, Owner.Edit.Revision,
            "ncma.animgraph.inspect", JsonSerializer.SerializeToElement(new { graphId = graph, section = "nodes" })));
        internal JsonElement Call(string name, object input, Guid request, ulong? revision=null) => Send(new { jsonrpc = "2.0", id = ++_id, method = "tools/call", @params = new { name, arguments = new {
            contractVersion = 2, requestId = request, sessionId = Owner.Edit!.SessionId, expectedRevision = revision??Owner.Edit.Revision, input } } }).GetProperty("result");
        internal void ApproveUi() { Build(); Click(40, 2); Build(); Click(40, 3, 1); Build(); Click(40, 4); Build(); }
        public void Dispose()
        {
            Client.StandardInput.Close(); if (!Client.WaitForExit(5000)) throw new TimeoutException("Graph MCP shutdown timeout.");
            Check(Client.ExitCode == 0 && _errors.GetAwaiter().GetResult().Length == 0); Client.Dispose(); Writer?.Dispose();Owner.Dispose();
        }
    }
    internal static IEnumerable<(string, Action)> Cases(string output, string repository)
    {
        yield return ("M6.2 real stdio graph tools + trusted visible review/default-denied + no tick IO", () => {
            using var f = new Fixture(output, repository); byte[] before = f.Owner.Document.CaptureBytes(); var history = f.Owner.Edit!.State;
            Guid request = Guid.NewGuid(); object input = new { graphId = f.Graph, section = "nodes", offset = 0, limit = 1 };
            var denied = f.Call("ncma.animgraph.inspect", input, request); Check(denied.GetProperty("structuredContent").GetProperty("code").GetString() == "graph_not_visible");
            f.ApproveUi(); using (var locked = new FileStream(f.File, FileMode.Open, FileAccess.Read, FileShare.None)) {
                var read = f.Call("ncma.animgraph.inspect", input, request); Check(!read.GetProperty("isError").GetBoolean()); var data = read.GetProperty("structuredContent").GetProperty("data");
                Check(data.GetProperty("items").GetArrayLength() == 1 && data.GetProperty("nextOffset").GetInt32() == 1);
                Check(data.GetProperty("items")[0].GetProperty("kind").ValueKind == JsonValueKind.String && !read.GetRawText().Contains(".ncmaanim"));
                var valid = f.Call("ncma.animgraph.validate", new { graphId = f.Graph }, Guid.NewGuid()).GetProperty("structuredContent").GetProperty("data");
                Check(valid.GetProperty("valid").GetBoolean() && !valid.GetProperty("resourcesPrepared").GetBoolean() && valid.GetProperty("validation").GetString() == "structural_only");
            }
            Check(f.Direct(Guid.NewGuid()).Code == "graph_not_visible"); f.Build(); f.Click(40, 5);
            Check(f.Call("ncma.animgraph.inspect", input, request).GetProperty("structuredContent").GetProperty("code").GetString() == "graph_not_visible");
            Check(before.SequenceEqual(f.Owner.Document.CaptureBytes()) && history == f.Owner.Edit.State);
        });
        yield return ("M6.2 graph read grant tamper/expiry/audience epoch/edit/reload/endpoint negatives", () => {
            using var f = new Fixture(output, repository); var page = f.Graphs.Capture(); Reject(() => f.Graphs.Approve(page, page.Fingerprint, false));
            Reject(() => f.Graphs.Approve(page with { Dependencies = [] }, page.Fingerprint, true)); Reject(() => f.Graphs.Approve(page with { Hash = new string('0', 64) }, page.Fingerprint, true));
            f.Graphs.Approve(page, page.Fingerprint, true); Check(f.Direct(f.Graph).Status == "ok"); f.Time.Now = 60000; Check(f.Direct(f.Graph).Code == "graph_not_visible");
            f.Graphs.Approve(f.Graphs.Capture(), f.Graphs.Capture().Fingerprint, true); var endpoint = f.Owner.Endpoint!; Guid client = endpoint.View.Connections.Single().ConnectionId;
            endpoint.Revoke(client); endpoint.Pair(client, true); Check(f.Direct(f.Graph).Code == "graph_not_visible"); // Identical audience UUIDs must NOT revive it.
            page = f.Graphs.Capture(); f.Graphs.Approve(page, page.Fingerprint, true); f.Workspace.CreateObject(f.Workspace.Stamp); Check(f.Direct(f.Graph).Code == "graph_not_visible");
            page = f.Graphs.Capture(); f.Graphs.Approve(page, page.Fingerprint, true); f.Graphs.OpenTrustedRelative("assets/test.ncmaanim"); Check(f.Direct(f.Graph).Code == "graph_not_visible");
            Reject(() => f.Graphs.Approve(page, page.Fingerprint, true)); // Same bytes reopen revokes but content unchanged: explicit publication epoch must change.
        });
        yield return ("M6.2 disabled/forged/stale graph UI approval and strict source requests", () => {
            using var f = new Fixture(output, repository); f.Build(); f.Click(40, 2); f.Build(); f.Click(40, 4); Check(f.Direct(f.Graph).Code == "graph_not_visible");
            var forged = f.Event(40, 3, 1); forged.Kind = (uint)GuiItemKind.Text; f.View.Apply([forged], []); f.Build(); f.Click(40, 4); Check(f.Direct(f.Graph).Code == "graph_not_visible");
            f.Click(40, 3, 1); f.Build(); var stale = f.Event(40, 4); f.Graphs.OpenTrustedRelative("assets/test.ncmaanim"); f.View.Apply([stale], []); Check(f.Direct(f.Graph).Code == "graph_not_visible");
            foreach (string path in new[] { "../escape.ncmaanim", "assets/test.ncscene", "out/test.ncmaanim", f.File }) Reject(() => f.Graphs.OpenTrustedRelative(path));
            f.ApproveUi(); Guid request = Guid.NewGuid(); var bad = f.Call("ncma.animgraph.inspect", new { graphId = f.Graph, section = "nodes", limit = 33 }, request); Check(bad.GetProperty("isError").GetBoolean());
            bad = f.Call("ncma.animgraph.inspect", new { graphId = f.Graph, section = "nodes", path = "assets/test.ncmaanim" }, Guid.NewGuid()); Check(bad.GetProperty("isError").GetBoolean());
        });
    }
}
