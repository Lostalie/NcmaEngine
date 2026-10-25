using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ncma.Editor.App;
using Ncma.Editor.Core;
using Ncma.Editor.Protocol;
using Ncma.Editor.Services;
using Ncma.Editor.Transport;
using Ncma.Gameplay;
using Ncma.Gui;
using Ncma.Runtime;
using Ncma.Scene;

internal static class RuntimeInspectionTests
{
    private static void Check(bool condition, string message = "Runtime inspection assertion")
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or EditRejectedException or EditCommandRejectedException) { return; }
        throw new InvalidOperationException("Expected rejection");
    }
    private sealed class Clock : TimeProvider
    { internal long Ticks; public override long TimestampFrequency => 1000; public override long GetTimestamp() => Ticks; }
    private sealed class Observer(Action action) : IWorldSystem
    { public void FixedUpdate(World world, double seconds) => action(); }
    private readonly record struct Payload<T>(string Text) : IComponent where T : struct;
    private readonly struct A { } private readonly struct B { } private readonly struct C { } private readonly struct D { }
    private sealed class Fixture : IDisposable
    {
        internal readonly EditorSessionOwner Owner;
        internal readonly RuntimeInspectionService Service;
        internal readonly Clock Time = new();
        internal readonly Guid Visible = Guid.NewGuid(), Hidden = Guid.NewGuid(), Binding = Guid.NewGuid();
        internal readonly string Directory;
        internal Action? DuringStep;
        internal Fixture(string output, string repository, bool large = false)
        {
            Directory = Path.Combine(output, "runtime-inspection-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            var registry = global::Ncma.Runtime.CompositionRegistry.Build();
            if (large)
            {
                const string schema = """{"type":"object","required":["text"],"properties":{"text":{"type":"string"}}}""";
                registry.Register<Payload<A>>("payload.a", 1, schema, v => v); registry.Register<Payload<B>>("payload.b", 1, schema, v => v);
                registry.Register<Payload<C>>("payload.c", 1, schema, v => v); registry.Register<Payload<D>>("payload.d", 1, schema, v => v);
            }
            Owner = new("Runtime inspection", components: registry, composePlay: p => { p.AddSystem(new Observer(() => DuringStep?.Invoke())); return null; });
            Owner.LoadGameplay(Path.Combine(repository, "out/managed/Ncma.Gameplay.Sample.dll"));
            var document = Owner.Document;
            document.SetComponent(document.CreateObject("Visible", Visible), TransformData.Identity);
            document.SetComponent(document.CreateObject("PRIVATE_HIDDEN", Hidden), TransformData.Identity);
            if (large)
            {
                string text = new('x', 64600);
                document.SetComponent(Visible, new Payload<A>(text)); document.SetComponent(Visible, new Payload<B>(text));
                document.SetComponent(Visible, new Payload<C>(text)); document.SetComponent(Visible, new Payload<D>(text));
            }
            Owner.Document.SetBindings(Visible, [new(Binding, Owner.Catalog.Snapshot.Types.Single().TypeName, false,
                [new("DegreesPerSecond", ExportKind.Float, 90), new("Multiplier", ExportKind.Integer, 1), new("Clockwise", ExportKind.Boolean, 1)])]);
            Owner.Edit!.Resynchronize(); Service = new(Owner, Time); Service.Register();
            Owner.ConfigureEndpoint(true, Directory); Owner.StartPlay();
        }
        internal CapabilityRequest Request(string name, object input, Guid? request = null) => new(2,
            request ?? Guid.NewGuid(), Owner.Edit!.SessionId, Owner.Edit.Revision, name, JsonSerializer.SerializeToElement(input, Wire.Json));
        internal CapabilityResult Call(string name, object input, Guid? request = null) => Owner.Edit!.Invoke(Request(name, input, request));
        internal JsonObject Input(string section = "objects", Guid? id = null, Guid? binding = null)
        {
            var input = JsonSerializer.SerializeToNode(new { playSessionId = Owner.Play!.SessionId,
                stamp = Owner.Play.Scene.GetInspectionStamp(), section, offset = 0, limit = 16 }, Wire.Json)!.AsObject();
            if (id.HasValue) input["objectId"] = id.Value.ToString("D");
            if (binding.HasValue) input["bindingId"] = binding.Value.ToString("D");
            return input;
        }
        internal void Approve() { var page = Service.Capture([Visible]); Service.Approve(page, page.Fingerprint, true); }
        public void Dispose() => Owner.Dispose();
    }
    private sealed class Client : IDisposable
    {
        private readonly NamedPipeClientStream _pipe;
        private readonly CancellationTokenSource _cancel = new(TimeSpan.FromSeconds(30));
        internal readonly Guid Connection;
        private readonly Fixture _fixture;
        private readonly EditorEndpoint _endpoint;
        internal Client(Fixture fixture)
        {
            _fixture = fixture; _endpoint = fixture.Owner.Endpoint!;
            var descriptor = Wire.Decode<EndpointDescriptor>(File.ReadAllBytes(_endpoint.DescriptorPath));
            _pipe = new(".", descriptor.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            _pipe.Connect(3000);
            Wire.WriteAsync(_pipe, Wire.Encode(new Hello(1, descriptor.InstanceId, descriptor.ProjectRoot, "E6 runtime read test")), _cancel.Token).GetAwaiter().GetResult();
            var hello = Wire.ReadAsync(_pipe, _cancel.Token);
            Pump(() => _endpoint.View.Connections.Any(c => c.Connected && !c.Paired));
            Connection = _endpoint.View.Connections.Single(c => c.Connected && !c.Paired).ConnectionId;
            _endpoint.Pair(Connection, true); Pump(() => hello.IsCompleted);
            Check(Wire.Decode<HelloResult>(hello.GetAwaiter().GetResult()!).Code == "ok");
        }
        private void Pump(Func<bool> done)
        {
            var watch = Stopwatch.StartNew();
            while (!done()) { if (watch.ElapsedMilliseconds > 5000) throw new TimeoutException("Runtime inspection IPC timeout"); _endpoint.Pump(); Thread.Sleep(1); }
        }
        internal CapabilityResult Call(string name, object input, Guid? id = null)
        {
            var request = _fixture.Request(name, input, id);
            Wire.WriteAsync(_pipe, Wire.Encode(new IpcRequest("invoke", _fixture.Owner.Edit!.DocumentGeneration,
                JsonSerializer.SerializeToElement(request, Wire.Json))), _cancel.Token).GetAwaiter().GetResult();
            var pending = Wire.ReadAsync(_pipe, _cancel.Token); Pump(() => pending.IsCompleted);
            var response = Wire.Decode<IpcResponse>(pending.GetAwaiter().GetResult()!);
            Check(response.Code == "ok", "IPC response: " + response.Code);
            return response.Result!.Value.Deserialize<CapabilityResult>(Wire.Json)!;
        }
        public void Dispose() { _pipe.Dispose(); _cancel.Dispose(); }
    }
    internal static IEnumerable<(string, Action)> Cases(string output, string repository)
    {
        yield return ("E6 local runtime detail pages are complete, detached and never grant remote reads", () => {
            using var f = new Fixture(output, repository);
            var play = f.Owner.Play!;
            play.Scene.World.Set(play.Scene.World.FindEntity(f.Visible), TransformData.Identity with { Position = new(7, 8, 9) });
            byte[] author = f.Owner.Document.CaptureBytes(), runtime = play.Scene.CaptureCheckpoint().CopyBytes();
            var history = f.Owner.Edit!.State;
            var request = new RuntimeDetailRequest(play.SessionId, play.Scene.GetInspectionStamp(), f.Visible);
            foreach (string section in new[] { "components", "bindings", "exports" })
            {
                var page = f.Service.LocalDetails(request, section, bindingId: section == "exports" ? f.Binding : null);
                Check(page.Source == request && page.TotalCount == (section == "exports" ? 3 : 1));
                Check(page.Data.GetProperty("items").GetArrayLength() == 1 && page.Data.GetRawText() == page.Text);
                Check(!page.Text.Contains("PRIVATE_HIDDEN") && !page.Text.Contains(f.Hidden.ToString("D")));
                Check(JsonSerializer.SerializeToUtf8Bytes(page.Data, Wire.Json).Length <= RuntimeSceneInstance.MaxInspectionBytes - 4096);
                if (section == "components")
                {
                    var encoded = page.Data.GetProperty("items")[0].Deserialize<ComponentSnapshot>(Wire.Json)!;
                    Check(play.Scene.World.Components.Decode<TransformData>(encoded).Position == new System.Numerics.Vector3(7, 8, 9));
                    Check(f.Owner.Document.GetComponent<TransformData>(f.Visible).Position == System.Numerics.Vector3.Zero,
                        "Local details read actual runtime values, never unchanged authoring data");
                }
                if (section == "exports")
                    for (int offset = 0; offset < 3; offset++)
                        Check(f.Service.LocalDetails(request, section, offset, f.Binding).Data.GetProperty("items").GetArrayLength() == 1);
            }
            Reject(() => f.Service.LocalDetails(request, "objects"));
            Reject(() => f.Service.LocalDetails(request, "components", -1));
            Reject(() => f.Service.LocalDetails(request, "bindings", bindingId: f.Binding));
            Reject(() => f.Service.LocalDetails(request, "exports"));
            Reject(() => f.Service.LocalDetails(request, "exports", bindingId: Guid.NewGuid()));
            Check(f.Service.Grant.Objects == 0 && f.Call("ncma.runtime.state", new { }).Code == "runtime_not_visible");
            Check(author.SequenceEqual(f.Owner.Document.CaptureBytes()) && history == f.Owner.Edit.State &&
                runtime.SequenceEqual(play.Scene.CaptureCheckpoint().CopyBytes()));
        });
        yield return ("E6 local runtime details reject stale tick, generation, reload and pending-step reads", () => {
            using var f = new Fixture(output, repository); var play = f.Owner.Play!;
            RuntimeDetailRequest Current() => new(play.SessionId, play.Scene.GetInspectionStamp(), f.Visible);
            var request = Current(); var first = f.Service.LocalDetails(request, "components"); string detached = first.Text;
            play.AdvanceFrame(1d / 60); Check(!f.Service.IsLocalCurrent(request));
            Reject(() => f.Service.LocalDetails(request, "components"));
            request = f.Service.RefreshLocal(request); Check(f.Service.IsLocalCurrent(request));
            play.Scene.World.Destroy(play.Scene.World.FindEntity(f.Visible)); play.Scene.World.CreateEntity("Replacement", f.Visible);
            Check(!f.Service.IsLocalCurrent(request)); Reject(() => f.Service.LocalDetails(request, "bindings"));
            request = f.Service.RefreshLocal(request); Check(f.Service.LocalDetails(request, "bindings").TotalCount == 0);
            Check(first.Text == detached && first.Data.GetRawText() == detached, "Old detached page cannot change behind the view");
            bool rejectedDuringStep = false; f.DuringStep = () => { Reject(() => f.Service.LocalDetails(request, "components")); rejectedDuringStep = true; };
            play.AdvanceFrame(1d / 60); Check(rejectedDuringStep); f.DuringStep = null;
            request = Current(); Task.Run(() => Reject(() => f.Service.LocalDetails(request, "components"))).GetAwaiter().GetResult();
            play.Reload(f.Owner.Catalog.Instantiate); Check(!f.Service.IsLocalCurrent(request)); Reject(() => f.Service.RefreshLocal(request));
            f.Owner.StopPlay(); Reject(() => f.Service.LocalDetails(request, "components"));
        });
        yield return ("E6 local large component and Unicode text paging preserve every field", () => {
            using var f = new Fixture(output, repository, large: true); var play = f.Owner.Play!;
            var request = new RuntimeDetailRequest(play.SessionId, play.Scene.GetInspectionStamp(), f.Visible);
            for (int offset = 0; offset < 5; offset++)
            {
                var page = f.Service.LocalDetails(request, "components", offset);
                Check(page.TotalCount == 5 && page.Data.GetProperty("items").GetArrayLength() == 1);
                var rebuilt = new System.Text.StringBuilder();
                for (int at = 0; at < page.Text.Length; at += 80) rebuilt.Append(EditorPresenter.RuntimeDetailTextSlice(page.Text, at, 80));
                Check(rebuilt.ToString() == page.Text, "Large component is paginated, not truncated");
            }
            string unicode = new string('a', 79) + "😀" + new string('b', 878) + "😀" + new string('c', 130);
            var slices = Enumerable.Range(0, (unicode.Length + 79) / 80).Select(i => EditorPresenter.RuntimeDetailTextSlice(unicode, i * 80, 80)).ToArray();
            Check(string.Concat(slices) == unicode && slices.All(s => s.Length == 0 || (!char.IsLowSurrogate(s[0]) && !char.IsHighSurrogate(s[^1]))));
        });
        yield return ("E6 presenter local detail navigation is separate from approval and hides stale values", () => {
            using var f = new Fixture(output, repository); var presenter = new EditorPresenter(new EditorWorkspace(f.Owner), null);
            presenter.AttachRuntimeInspection(f.Service); ulong frameId = 1;
            void Click(ulong high, ulong low)
            {
                presenter.Build(frameId++, 1280, 720); var item = presenter.Items.ToArray().Single(i => i.WidgetHigh == high && i.WidgetLow == low);
                var frame = presenter.Frame;
                presenter.Apply([new GuiEvent { Kind = item.Kind, WidgetHigh = high, WidgetLow = low, Phase = 3,
                    Frame = frame.Frame, ViewGeneration = frame.ViewGeneration, DocumentGeneration = frame.DocumentGeneration, Revision = frame.Revision }], []);
            }
            string Text() { presenter.Build(frameId++, 1280, 720); return System.Text.Encoding.UTF8.GetString(presenter.Text); }
            Click(62, 1); Click(63, 100); Check(Text().Contains("components"));
            Click(63, 3); Check(Text().Contains("bindings")); Click(63, 9); Check(Text().Contains("exports"));
            Click(63, 6); Check(f.Service.Grant.Objects == 0 && f.Call("ncma.runtime.state", new { }).Code == "runtime_not_visible");
            f.Owner.Play!.AdvanceFrame(1d / 60); Check(Text().Contains("旧属性已隐藏"));
            Check(!presenter.Items.ToArray().Any(i => i.WidgetHigh == 63 && i.WidgetLow == 6));
            Click(63, 4); Check(!Text().Contains("旧属性已隐藏"));
            Click(63, 1); _ = Text(); Check(!presenter.Items.ToArray().Any(i => i.WidgetHigh == 63 && i.WidgetLow == 4));
            f.Owner.StopPlay(); Check(Text().Contains("运行快照不可用"));
        });
        yield return ("E6 runtime host exact approval, actual IPC four sections, schemas and no mutation", () => {
            using var f = new Fixture(output, repository); using var client = new Client(f);
            byte[] edit = f.Owner.Document.CaptureBytes(), runtime = f.Owner.Play!.Scene.CaptureCheckpoint().CopyBytes();
            var history = f.Owner.Edit!.State; Guid retry = Guid.NewGuid();
            Check(client.Call("ncma.runtime.state", new { }, retry).Code == "runtime_not_visible");
            var review = f.Service.Capture([f.Visible]);
            Reject(() => f.Service.Approve(review, review.Fingerprint, false));
            Reject(() => f.Service.Approve(review with { Objects = [new(f.Hidden, "PRIVATE_HIDDEN")] }, review.Fingerprint, true));
            f.Service.Approve(review, review.Fingerprint, true);
            foreach (var descriptor in RuntimeInspectionSchemas.Descriptors())
            {
                Check(descriptor.Risk == MutationRisk.ReadOnly);
                Check(f.Owner.Edit.Describe().Single(d => d.Name == descriptor.Name).OutputSchema.GetRawText() == descriptor.OutputSchema.GetRawText());
            }
            var state = client.Call("ncma.runtime.state", new { }, retry);
            Check(state.Status == "ok" && !state.Replayed && !state.Changed);
            Check(state.Data.GetProperty("objectIds").GetArrayLength() == 1 && !state.Data.GetRawText().Contains(f.Hidden.ToString("D")));
            CharacterInspectionTests.Schema(JsonSerializer.SerializeToElement(state, Wire.Json), RuntimeInspectionSchemas.Descriptors()[0].OutputSchema);
            foreach (string section in new[] { "objects", "components", "bindings", "exports" })
            {
                var input = f.Input(section, section == "objects" ? null : f.Visible, section == "exports" ? f.Binding : null);
                var result = client.Call("ncma.runtime.inspect", input);
                Check(result.Status == "ok" && !result.Changed && !result.Replayed, section);
                CharacterInspectionTests.Schema(JsonSerializer.SerializeToElement(result, Wire.Json), RuntimeInspectionSchemas.Descriptors()[1].OutputSchema);
                var page = result.Data.GetProperty("page");
                Check(page.GetProperty("totalCount").GetInt32() == (section == "exports" ? 3 : 1));
                Check(!result.Data.GetRawText().Contains("PRIVATE_HIDDEN") && !result.Data.GetRawText().Contains(f.Hidden.ToString("D")));
                Check(JsonSerializer.SerializeToUtf8Bytes(result, Wire.Json).Length <= RuntimeSceneInstance.MaxInspectionBytes);
            }
            Check(client.Call("ncma.runtime.inspect", f.Input("components", f.Hidden)).Code == "runtime_not_visible");
            f.Service.Revoke(); Check(client.Call("ncma.runtime.state", new { }, retry).Code == "runtime_not_visible", "No replay of revoked result");
            Check(edit.SequenceEqual(f.Owner.Document.CaptureBytes()) && history == f.Owner.Edit.State && runtime.SequenceEqual(f.Owner.Play.Scene.CaptureCheckpoint().CopyBytes()));
        });
        yield return ("E6 runtime host stale tick/revision, strict paging inputs and pending-step rejection", () => {
            using var f = new Fixture(output, repository); using var client = new Client(f); f.Approve();
            var old = f.Input(); Check(f.Owner.Play!.AdvanceFrame(1d / 60).State == PlayState.Running);
            Check(f.Call("ncma.runtime.inspect", old).Code == "runtime_snapshot_stale");
            Check(f.Call("ncma.runtime.inspect", f.Input()).Status == "ok", "Fresh stamp with same grant");
            foreach (var (field, value) in new (string, JsonNode?)[] { ("offset", JsonValue.Create(-1)), ("limit", JsonValue.Create(129)),
                ("limit", JsonValue.Create(1.5)), ("approve", JsonValue.Create(true)), ("section", JsonValue.Create("write")) })
            { var bad = f.Input(); bad[field] = value; Check(f.Call("ncma.runtime.inspect", bad).Status != "ok"); }
            Check(f.Call("ncma.runtime.inspect", f.Input("exports", f.Visible)).Status != "ok");
            Check(f.Call("ncma.runtime.inspect", f.Input("objects", f.Visible)).Status != "ok");
            var foreign = f.Input(); foreign["stamp"]!["worldId"] = Guid.NewGuid().ToString("D");
            Check(f.Call("ncma.runtime.inspect", foreign).Code == "runtime_snapshot_stale");
            using var duplicate = JsonDocument.Parse("{\"offset\":0,\"offset\":1}");
            Check(f.Call("ncma.runtime.inspect", duplicate.RootElement).Status != "ok");
            string? during = null; f.DuringStep = () => during = f.Call("ncma.runtime.state", new { }).Status;
            f.Owner.Play.AdvanceFrame(1d / 60); Check(during is not (null or "ok"), "No candidate-step observation");
        });
        yield return ("E6 runtime human grant expires, audience epoch and endpoint replacement invalidate", () => {
            using var f = new Fixture(output, repository); using var client = new Client(f);
            var review = f.Service.Capture([f.Visible]); f.Service.Approve(review, review.Fingerprint, true);
            f.Owner.Endpoint!.Revoke(client.Connection); f.Owner.Endpoint.Pair(client.Connection, true);
            Reject(() => f.Service.Approve(review, review.Fingerprint, true));
            Check(f.Call("ncma.runtime.state", new { }).Code == "runtime_not_visible", "No intervening read needed to observe epoch revocation");
            f.Approve(); f.Time.Ticks = 60000; Check(f.Call("ncma.runtime.state", new { }).Code == "runtime_not_visible");
            var pending = f.Service.Capture([f.Visible]); f.Time.Ticks += 60000; Reject(() => f.Service.Approve(pending, pending.Fingerprint, true));
            f.Approve(); f.Owner.ConfigureEndpoint(false, f.Directory); Check(f.Service.Grant.Objects == 0);
        });
        yield return ("E6 runtime grant pins actual object generation and Play reload/start lifecycle", () => {
            using var f = new Fixture(output, repository); using var client = new Client(f); f.Approve();
            var play = f.Owner.Play!; var world = play.Scene.World;
            world.Destroy(world.FindEntity(f.Visible)); world.CreateEntity("Visible", f.Visible);
            Check(f.Call("ncma.runtime.state", new { }).Code == "runtime_not_visible", "Same UUID recreated cannot inherit grant");
            f.Approve(); play.Reload(f.Owner.Catalog.Instantiate);
            Check(f.Call("ncma.runtime.state", new { }).Code == "runtime_not_visible");
            f.Approve(); f.Owner.StopPlay(); Check(f.Call("ncma.runtime.state", new { }).Code == "runtime_not_visible");
            f.Owner.StartPlay(); Check(f.Call("ncma.runtime.state", new { }).Code == "runtime_not_visible");
        });
        yield return ("E6 runtime host complete response budget rejects without truncation, bounded pages remain readable", () => {
            using var f = new Fixture(output, repository, large: true); using var client = new Client(f); f.Approve();
            var play = f.Owner.Play!; byte[] original = play.Scene.CaptureCheckpoint().CopyBytes();
            // The raw runtime page fits 256 KiB; the host must still reserve the outer transport envelopes.
            var page = play.Scene.InspectComponents(play.Scene.GetInspectionStamp(), f.Visible);
            Check(JsonSerializer.SerializeToUtf8Bytes(page, Wire.Json).Length <= RuntimeSceneInstance.MaxInspectionBytes);
            Check(client.Call("ncma.runtime.inspect", f.Input("components", f.Visible)).Code == "runtime_response_budget");
            for (int i = 0; i < 5; i++)
            {
                var input = f.Input("components", f.Visible); input["offset"] = i; input["limit"] = 1;
                var read = client.Call("ncma.runtime.inspect", input);
                Check(read.Status == "ok" && read.Data.GetProperty("page").GetProperty("totalCount").GetInt32() == 5);
                Check(read.Data.GetProperty("page").GetProperty("items").GetArrayLength() == 1);
                Check(JsonSerializer.SerializeToUtf8Bytes(read, Wire.Json).Length <= RuntimeSceneInstance.MaxInspectionBytes);
            }
            Check(original.SequenceEqual(play.Scene.CaptureCheckpoint().CopyBytes()));
        });
        yield return ("E6 runtime MCP stdio discovers read-only tools and rechecks approval on repeated request IDs", () => {
            using var f = new Fixture(output, repository);
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(repository, "out/managed/editor-mcp/Ncma.Editor.Mcp.dll"));
            start.ArgumentList.Add("--descriptor"); start.ArgumentList.Add(f.Owner.Endpoint!.DescriptorPath);
            using var process = Process.Start(start)!; var errors = process.StandardError.ReadToEndAsync();
            JsonElement Send(object value)
            {
                process.StandardInput.WriteLine(JsonSerializer.Serialize(value, Wire.Json)); process.StandardInput.Flush();
                var pending = process.StandardOutput.ReadLineAsync(); var watch = Stopwatch.StartNew();
                while (!pending.IsCompleted)
                {
                    if (watch.ElapsedMilliseconds > 10000) throw new TimeoutException("Runtime MCP timeout");
                    foreach (var c in f.Owner.Endpoint.View.Connections.Where(c => c.Connected && !c.Paired)) f.Owner.Endpoint.Pair(c.ConnectionId, true);
                    f.Owner.Endpoint.Pump(); Thread.Sleep(1);
                }
                using var message = JsonDocument.Parse(pending.GetAwaiter().GetResult() ?? throw new IOException("MCP closed"));
                return message.RootElement.Clone();
            }
            try
            {
                Send(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-11-25" } });
                process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"); process.StandardInput.Flush();
                var tools = Send(new { jsonrpc = "2.0", id = 2, method = "tools/list", @params = new { } }).GetProperty("result").GetProperty("tools");
                foreach (var d in RuntimeInspectionSchemas.Descriptors())
                {
                    var tool = tools.EnumerateArray().Single(t => t.GetProperty("name").GetString() == d.Name);
                    Check(tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
                    Check(tool.GetProperty("outputSchema").GetRawText() == d.OutputSchema.GetRawText());
                }
                Guid request = Guid.NewGuid();
                JsonElement Call(int id) => Send(new { jsonrpc = "2.0", id, method = "tools/call", @params = new { name = "ncma.runtime.state",
                    arguments = new { contractVersion = 2, requestId = request, sessionId = f.Owner.Edit!.SessionId, expectedRevision = f.Owner.Edit.Revision, input = new { } } } }).GetProperty("result").GetProperty("structuredContent");
                Check(Call(3).GetProperty("code").GetString() == "runtime_not_visible"); f.Approve();
                var result = Call(4); Check(result.GetProperty("status").GetString() == "ok");
                CharacterInspectionTests.Schema(result, RuntimeInspectionSchemas.Descriptors()[0].OutputSchema);
                f.Service.Revoke(); Check(Call(5).GetProperty("code").GetString() == "runtime_not_visible");
            }
            finally { process.StandardInput.Close(); if (!process.WaitForExit(5000)) { process.Kill(true); process.WaitForExit(); } }
            Check(process.ExitCode == 0 && errors.GetAwaiter().GetResult().Length == 0);
        });
        yield return ("E6 runtime presenter requires exact visible human review and blocks disabled/old-frame approval", () => {
            using var f = new Fixture(output, repository); using var client = new Client(f);
            var presenter = new EditorPresenter(new EditorWorkspace(f.Owner), null); presenter.AttachRuntimeInspection(f.Service);
            ulong frameId = 1;
            void Click(ulong low, double value = 0)
            {
                presenter.Build(frameId++, 1280, 720);
                var item = presenter.Items.ToArray().Single(i => i.WidgetHigh == 62 && i.WidgetLow == low); var frame = presenter.Frame;
                presenter.Apply([new GuiEvent { Kind = item.Kind, WidgetHigh = 62, WidgetLow = low, Phase = 3,
                    Frame = frame.Frame, ViewGeneration = frame.ViewGeneration, DocumentGeneration = frame.DocumentGeneration, Revision = frame.Revision, Value = value }], []);
            }
            Click(1); Click(100, 1); Click(6); Click(8); Check(f.Service.Grant.Objects == 0);
            Click(7, 1); Click(8); Check(f.Service.Grant.Objects == 1);
            Click(3); Check(f.Service.Grant.Objects == 0);
            Click(6); Click(7, 1);
            f.Owner.Play!.Reload(f.Owner.Catalog.Instantiate);
            // Old displayed review is invalid even if Edit revision stayed frozen.
            Check(f.Service.Grant.Objects == 0); presenter.Build(frameId++, 1280, 720);
            Check(!presenter.Items.ToArray().Any(i => i.WidgetHigh == 62 && i.WidgetLow == 8));
        });
    }
}
