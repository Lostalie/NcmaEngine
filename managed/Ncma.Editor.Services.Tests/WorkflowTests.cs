using System.Diagnostics;
using System.IO.Pipes;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Ncma.Animation;
using Ncma.Assets.Authoring;
using Ncma.Editor.Core;
using Ncma.Editor.Protocol;
using Ncma.Editor.Services;
using Ncma.Editor.Transport;
using Ncma.Gui;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Ui;
using Ncma.Ui.Rendering;

internal static class WorkflowTests
{
    private sealed class FaultObserver(EditSession edit) : IEditorRequestMonitor
    {
        internal bool WriteBlocked, NestedBlocked;
        public string? Before(Guid connection, CapabilityRequest request) => null;
        public void After(Guid connection, CapabilityRequest request, CapabilityResult result)
        {
            if (!result.Changed) return;
            try { edit.Document.World.CreateObject("Forbidden observer write"); } catch (InvalidOperationException) { WriteBlocked = true; }
            NestedBlocked = edit.Invoke(new(2, Guid.NewGuid(), edit.SessionId, edit.Revision, "ncma.scene.inspect", JsonSerializer.SerializeToElement(new { }))).Code == "edit_busy";
            throw new InvalidOperationException("Injected receipt observer failure after commit.");
        }
    }
    private static void Check(bool value, string reason = "") { if (!value) throw new Exception("M6.9 workflow assertion: " + reason); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is InvalidOperationException or ArgumentException or JsonException) { return; } throw new Exception("Workflow authority accepted."); }
    private static object Step(string capability, object input, string expectation = "success") => new { id = Guid.NewGuid(), capability, input, expectation };
    private static object Plan(Guid id, object[] steps, int deadline = 120, int budget = 1) => new { version = 1, workflowId = id, deadlineSeconds = deadline, repairBudget = budget, steps };
    private static JsonElement Data(JsonElement response) { Check(!response.GetProperty("isError").GetBoolean(), response.GetRawText()); return response.GetProperty("structuredContent").GetProperty("data"); }
    private static JsonElement Op(GraphInspectionTests.Fixture f, string op, Guid id) => f.Call(EditorWorkflows.Capability, new { op, workflowId = id }, Guid.NewGuid());
    private static void Registry(GraphInspectionTests.Fixture f) { var s = f.Owner.Workflows!; var r = s.CaptureRegistry(); s.ApproveRegistry(r, r.Fingerprint, true); }
    private static Guid Propose(GraphInspectionTests.Fixture f, object[] steps, int deadline = 120, int budget = 1)
    { Guid id = Guid.NewGuid(); Data(f.Call(EditorWorkflows.Capability, new { op = "propose", plan = Plan(id, steps, deadline, budget) }, Guid.NewGuid())); return id; }
    private static void Approve(GraphInspectionTests.Fixture f, Guid id) { var s = f.Owner.Workflows!; var r = s.CaptureReview(id); s.Approve(r, r.Fingerprint, true); }
    private static CapabilityRequest Next(GraphInspectionTests.Fixture f, Guid id) => Wire.Decode<CapabilityRequest>(Encoding.UTF8.GetBytes(Data(Op(f, "next", id)).GetProperty("ticket").GetString()!));
    private static JsonElement Execute(GraphInspectionTests.Fixture f, CapabilityRequest ticket) => f.Call(ticket.Capability, ticket.Input, ticket.RequestId, ticket.ExpectedRevision);
    private static void ApproveTool(GraphInspectionTests.Fixture f) {
        var controller = new EditorAuthorizationController(f.Workspace); var page = controller.Capture()!; var proposal = page.Proposals.Single();
        controller.Approve(page, proposal.Scope.Id, proposal.Fingerprint, false, null);
    }
    private static void Click(GraphInspectionTests.Fixture f, ulong high, ulong low, double value = 0) { f.Click(high, low, value); f.Build(); }
    private static void ReviewUi(GraphInspectionTests.Fixture f) {
        f.Build(); while (f.View.Items.ToArray().Single(i => i.WidgetHigh == 61 && i.WidgetLow == 6).Enabled != 0) Click(f, 61, 6);
        Click(f, 61, 7, 1); Click(f, 61, 8);
    }
    internal static IEnumerable<(string, Action)> Cases(string output, string repository, string plugins)
    {
        yield return ("M6.9 post-commit observer fault preserves original receipt and fail-stops subsequent execution", () => {
            var document = new Ncma.Scene.SceneDocument("Monitor fault"); var edit = new EditSession(document);
            using var endpoint = new EditorEndpoint(edit, repository); var observer = new FaultObserver(edit); endpoint.AttachRequestMonitor(observer);
            var descriptor = Wire.Decode<EndpointDescriptor>(File.ReadAllBytes(endpoint.DescriptorPath));
            using var pipe = new NamedPipeClientStream(".", descriptor.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly); pipe.Connect(3000);
            Wire.WriteAsync(pipe, Wire.Encode(new Hello(1, descriptor.InstanceId, descriptor.ProjectRoot, "M6.9 monitor fault")), CancellationToken.None).GetAwaiter().GetResult();
            var hello = Wire.ReadAsync(pipe, CancellationToken.None); var watch = Stopwatch.StartNew();
            while (endpoint.View.Connections.Length == 0) { if (watch.ElapsedMilliseconds > 10000) throw new TimeoutException(); Thread.Sleep(1); }
            endpoint.Pair(endpoint.View.Connections.Single().ConnectionId, true); Check(hello.Wait(10000));
            CapabilityResult Invoke(CapabilityRequest request) {
                Wire.WriteAsync(pipe, Wire.Encode(new IpcRequest("invoke", edit.DocumentGeneration, JsonSerializer.SerializeToElement(request, Wire.Json))), CancellationToken.None).GetAwaiter().GetResult();
                var response = Wire.ReadAsync(pipe, CancellationToken.None); var timer = Stopwatch.StartNew();
                while (!response.IsCompleted) { if (timer.ElapsedMilliseconds > 10000) throw new TimeoutException(); endpoint.Pump(); Thread.Sleep(1); }
                var frame = Wire.Decode<IpcResponse>(response.Result!); return frame.Result!.Value.Deserialize<CapabilityResult>(Wire.Json)!;
            }
            var request = new CapabilityRequest(2, Guid.NewGuid(), edit.SessionId, edit.Revision, "ncma.scene.transaction", JsonSerializer.SerializeToElement(new { operations = new[] { new { op = "create", objectId = Guid.NewGuid(), name = "Committed retained" } } }));
            Check(Invoke(request).Code == "permission_denied"); endpoint.Approve(endpoint.Proposals.Single().Id, false, null);
            var committed = Invoke(request); Check(committed.Status == "ok" && committed.Changed && document.World.Count == 1 && edit.State.UndoCount == 1 && observer.WriteBlocked && observer.NestedBlocked);
            Check(Invoke(new(2, Guid.NewGuid(), edit.SessionId, edit.Revision, "ncma.scene.inspect", JsonSerializer.SerializeToElement(new { }))).Code == "request_monitor_faulted");
            Check(document.World.Count == 1 && edit.State.UndoCount == 1);
        });
        yield return ("M6.9 real stdio default deny/exact tickets/two approvals/revision receipts/no automatic Undo", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository); var service = f.Owner.Workflows!; Guid obj = Guid.NewGuid();
            object[] steps = [Step("ncma.scene.transaction", new { operations = new[] { new { op = "create", objectId = obj, name = "Reviewed" } } }), Step("ncma.scene.validate", new { }, "valid")];
            Check(f.Call(EditorWorkflows.Capability, new { op = "propose", plan = Plan(Guid.NewGuid(), steps) }, Guid.NewGuid()).GetProperty("isError").GetBoolean());
            Registry(f); Guid id = Propose(f, steps); Check(Op(f, "next", id).GetProperty("isError").GetBoolean()); var review = service.CaptureReview(id);
            Reject(() => service.Approve(review with { Hash = "forged" }, review.Fingerprint, true)); Reject(() => service.Approve(review, review.Fingerprint, false)); Approve(f, id);
            var ticket = Next(f, id); Check(Next(f, id) == ticket || Next(f, id).RequestId == ticket.RequestId); Check(Execute(f, ticket).GetProperty("isError").GetBoolean());
            Check(service.Capture().Single().Index == 0 && service.Capture().Single().Receipts.Length == 0 && f.Owner.Document.World.Count == 0);
            ApproveTool(f); Data(Execute(f, ticket)); Check(f.Owner.Document.World.Count == 1 && f.Owner.Edit!.State.UndoCount == 1);
            Check(Execute(f, ticket).GetProperty("isError").GetBoolean()); // receipt inspection replaces step re-execution
            var receipt = service.Capture().Single(); Check(receipt.Index == 1 && receipt.Receipts.Single().Changed && receipt.Receipts.Single().ExecutionRevision == f.Owner.Edit!.Revision);
            var validate = Next(f, id); Check(validate.ExpectedRevision == f.Owner.Edit!.Revision); Data(Execute(f, validate));
            var completed = service.Capture().Single(); Check(completed.Status == "completed" && completed.Receipts.Length == 2 && completed.Receipts.All(r => r.AssertionPassed));
            CharacterInspectionTests.Schema(Op(f, "inspect", id).GetProperty("structuredContent"), WorkflowSchemas.Descriptor().OutputSchema);
            f.Workspace.Transaction(f.Workspace.Stamp, "Human later edit", [new { op = "rename", objectId = obj, name = "Human retained" }]);
            byte[] before = f.Owner.Document.CaptureBytes(); int undo = f.Owner.Edit.State.UndoCount; service.Cancel(id); service.Revoke();
            Check(before.SequenceEqual(f.Owner.Document.CaptureBytes()) && f.Owner.Edit.State.UndoCount == undo && service.Capture().Single().Receipts.Length == 2 && f.Owner.Document.World.Tick == 0);
        });
        yield return ("M6.9 failed actual isolated assertion blocks successor/repair cannot alter assertion policy", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository, true); Registry(f); f.ApproveUi(); f.Writer!.Sequences.PrepareTrusted();
            Guid test = Guid.NewGuid(); var sequence = new AnimationSequenceCase(.1, 1, [], [new(1, AnimationSequenceAssertionKind.RootX, Guid.Empty, 999)]);
            Guid id = Propose(f, [Step(AnimationSequenceSchemas.Propose, new { graphId = f.Graph, caseId = test, test = AnimationSequenceCodec.Encode(sequence) }),
                Step(AnimationSequenceSchemas.Run, new { graphId = f.Graph, caseId = test, section = "summary", offset = 0, limit = 1 }, "passed"), Step("ncma.scene.inspect", new { })]);
            Approve(f, id); Data(Execute(f, Next(f, id))); var r = f.Writer.Sequences.CaptureReview(test); f.Writer.Sequences.Approve(r, r.Fingerprint, true);
            var result = Data(Execute(f, Next(f, id))); Check(!result.GetProperty("items")[0].GetProperty("passed").GetBoolean());
            var before = f.Owner.Workflows!.Capture().Single(); Check(before.Status == "failed" && before.Index == 1 && before.Receipts.Length == 2 && !before.Receipts[1].AssertionPassed);
            Check(Op(f, "next", id).GetProperty("isError").GetBoolean()); Check(f.Call(EditorWorkflows.Capability, new { op = "repair", workflowId = id,
                input = new { graphId = f.Graph, caseId = test, section = "timeline", offset = 0, limit = 1 } }, Guid.NewGuid()).GetProperty("isError").GetBoolean());
            var after = f.Owner.Workflows.Capture().Single(); Check(before.Hash == after.Hash && after.RepairCount == 0 && after.Index == 1 && f.Owner.Edit!.State.UndoCount == 0 && f.Owner.Document.World.Tick == 0);
        });
        yield return ("M6.9 two active plans/foreign owner/registry tamper and stale review rejection", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository); var service = f.Owner.Workflows!;
            var registry = service.CaptureRegistry(); Reject(() => service.ApproveRegistry(registry with { Audience = [] }, registry.Fingerprint, true)); Registry(f);
            Guid a = Propose(f, [Step("ncma.scene.inspect", new { })]), b = Propose(f, [Step("ncma.scene.inspect", new { })]), c = Propose(f, [Step("ncma.scene.inspect", new { })]);
            Approve(f, a); Approve(f, b); Reject(() => Approve(f, c)); var ticket = Next(f, a);
            Check(service.Before(Guid.NewGuid(), ticket) == "workflow_owner_mismatch"); service.Cancel(b); Approve(f, c);
            var review = service.CaptureReview(Propose(f, [Step("ncma.scene.inspect", new { })])); f.Workspace.CreateObject(f.Workspace.Stamp);
            Reject(() => service.Approve(review, review.Fingerprint, true)); Check(Execute(f, ticket).GetProperty("isError").GetBoolean());
            Check(service.Capture().All(p => p.Receipts.Length == 0));
        });
        yield return ("M6.9 actual NCA graph propose/approved transaction/validation/isolated assertion workflow", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository, true); var writer = f.Writer!; Registry(f); f.ApproveUi();
            Guid proposal = Guid.NewGuid(), test = Guid.NewGuid(); ulong assets = f.Owner.Assets!.Clock.Revision;
            var sequence = new AnimationSequenceCase(.1, 3, [], [new(1, AnimationSequenceAssertionKind.EventCount, Guid.Empty, 0)]);
            Guid id = Propose(f, [Step("ncma.animgraph.inspect", new { graphId = f.Graph, section = "summary" }),
                Step(AnimationGraphAuthoringSchemas.Propose, new { graphId = f.Graph, proposalId = proposal, operations = AnimationGraphEdits.Operations(new { op = "graph.rename", name = "Workflow verified" }) }),
                Step(AnimationGraphCommands.CapabilityName, new { proposalId = proposal, expectedAssetRevision = assets }),
                Step("ncma.animgraph.validate", new { graphId = f.Graph }, "valid"),
                Step(AnimationSequenceSchemas.Propose, new { graphId = f.Graph, caseId = test, test = AnimationSequenceCodec.Encode(sequence) }),
                Step(AnimationSequenceSchemas.Run, new { graphId = f.Graph, caseId = test, section = "summary", offset = 0, limit = 1 }, "passed")]);
            Approve(f, id); byte[] scene = f.Owner.Document.CaptureBytes(); Data(Execute(f, Next(f, id))); Data(Execute(f, Next(f, id)));
            var tx = Next(f, id); Check(Execute(f, tx).GetProperty("isError").GetBoolean()); ApproveTool(f);
            Check(Execute(f, tx).GetProperty("isError").GetBoolean()); // scope still lacks independent actual resource/file approval
            var r = writer.CaptureReview(proposal); writer.Approve(r, r.Fingerprint, r.Graph, true); Data(Execute(f, tx));
            f.View.SynchronizeGraph(); f.Build(); f.ApproveUi(); writer.Sequences.PrepareTrusted();
            Data(Execute(f, Next(f, id))); Data(Execute(f, Next(f, id))); var run = Next(f, id);
            Check(Execute(f, run).GetProperty("isError").GetBoolean()); var sr = writer.Sequences.CaptureReview(test); writer.Sequences.Approve(sr, sr.Fingerprint, true);
            Data(Execute(f, run)); var final = f.Owner.Workflows!.Capture().Single(); Check(final.Status == "completed" && final.Receipts.Length == 6 && final.Receipts.All(x => x.AssertionPassed));
            Check(AnimationGraphCodec.Decode(File.ReadAllBytes(f.File)).Name == "Workflow verified" && f.Owner.Edit!.State.UndoCount == 1 && scene.SequenceEqual(f.Owner.Document.CaptureBytes()) && f.Owner.Document.World.Tick == 0);
        });
        yield return ("M6.9 cancel queued approved mutation retains tombstone and commits nothing", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository); Registry(f);
            Guid id = Propose(f, [Step("ncma.scene.transaction", new { operations = new[] { new { op = "create", objectId = Guid.NewGuid(), name = "Never committed" } } })]);
            Approve(f, id); var t = Next(f, id); Check(Execute(f, t).GetProperty("isError").GetBoolean()); ApproveTool(f);
            f.Client.StandardInput.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 690900, method = "tools/call", @params = new { name = t.Capability,
                arguments = new { contractVersion = t.ContractVersion, requestId = t.RequestId, sessionId = t.SessionId, expectedRevision = t.ExpectedRevision, input = t.Input } } }, Wire.Json)); f.Client.StandardInput.Flush();
            var pending = f.Client.StandardOutput.ReadLineAsync(); var clock = Stopwatch.StartNew();
            while (f.Owner.Endpoint!.View.QueueCount == 0) { if (clock.ElapsedMilliseconds > 10000) throw new TimeoutException("Workflow queue"); Thread.Sleep(1); }
            Check(!pending.IsCompleted); f.Owner.Workflows!.Cancel(id); f.Owner.Endpoint.Pump(); Check(pending.Wait(10000));
            using var result = JsonDocument.Parse(pending.Result!); Check(result.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
            Check(Execute(f, t).GetProperty("isError").GetBoolean()); Check(f.Owner.Document.World.Count == 0 && f.Owner.Edit!.State.UndoCount == 0 && f.Owner.Workflows.Capture().Single().Receipts.Length == 0);
        });
        yield return ("M6.9 deadline/TTL/revoke-repair/audience/edit/endpoint/foreign ticket fences", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository); Registry(f);
            Guid id = Propose(f, [Step("ncma.scene.validate", new { }, "valid")], 1); Approve(f, id); var ticket = Next(f, id); f.Time.Now = 1000;
            Check(Execute(f, ticket).GetProperty("isError").GetBoolean() && f.Owner.Workflows!.Capture().Single().Status == "expired");
            Registry(f); Guid next = Propose(f, [Step("ncma.scene.validate", new { })]); Approve(f, next); ticket = Next(f, next);
            Check(Execute(f, ticket with { Input = JsonSerializer.SerializeToElement(new { forged = true }) }).GetProperty("isError").GetBoolean());
            Check(Execute(f, ticket with { Capability = EditorWorkflows.Capability, Input = JsonSerializer.SerializeToElement(new { op = "cancel", workflowId = next }) }).GetProperty("isError").GetBoolean());
            var direct = f.Owner.Edit!.Invoke(new(2, Guid.NewGuid(), f.Owner.Edit.SessionId, f.Owner.Edit.Revision, EditorWorkflows.Capability,
                JsonSerializer.SerializeToElement(new { op = "inspect", workflowId = next })));
            Check(direct.Code == "workflow_endpoint_context_required", "Rejected forged tickets must leave no Agent invocation context.");
            var endpoint = f.Owner.Endpoint!; Guid connection = endpoint.View.Connections.Single().ConnectionId; endpoint.Revoke(connection); endpoint.Pair(connection, true);
            Check(Execute(f, ticket).GetProperty("isError").GetBoolean()); Registry(f); Guid other = Propose(f, [Step("ncma.scene.validate", new { })]); Approve(f, other); ticket = Next(f, other);
            f.Workspace.CreateObject(f.Workspace.Stamp); Check(Execute(f, ticket).GetProperty("isError").GetBoolean());
            Registry(f); Guid ttl = Propose(f, [Step("ncma.scene.validate", new { })]); Approve(f, ttl); ticket = Next(f, ttl); f.Time.Now += 60000;
            Check(Execute(f, ticket).GetProperty("isError").GetBoolean()); Registry(f); Guid last = Propose(f, [Step("ncma.scene.validate", new { })]); Approve(f, last); ticket = Next(f, last); f.Owner.Workflows!.Revoke();
            Check(Execute(f, ticket).GetProperty("isError").GetBoolean()); f.Owner.ConfigureEndpoint(false, f.Root); Check(f.Owner.Workflows.Capture().All(p => p.Receipts.Length == 0));
        });
        yield return ("M6.9 finite repair/fresh complete approval/original receipts/copied data and closed budgets", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository); Registry(f);
            Guid id = Propose(f, [Step("ncma.scene.inspect", new { }), Step("ncma.scene.object.inspect", new { objectId = Guid.NewGuid(), section = "summary" })]); Approve(f, id);
            Data(Execute(f, Next(f, id))); Check(Execute(f, Next(f, id)).GetProperty("isError").GetBoolean()); var service = f.Owner.Workflows!; Check(service.Capture().Single().Status == "failed");
            Data(f.Call(EditorWorkflows.Capability, new { op = "repair", workflowId = id, input = new { objectId = Guid.NewGuid(), section = "summary" } }, Guid.NewGuid()));
            Check(Op(f, "next", id).GetProperty("isError").GetBoolean()); var copy = service.Capture(); copy[0].Receipts[0] = copy[0].Receipts[0] with { ResultHash = "forged" }; Check(service.Capture()[0].Receipts[0].ResultHash.Length == 64);
            Approve(f, id); Check(Execute(f, Next(f, id)).GetProperty("isError").GetBoolean()); Check(f.Call(EditorWorkflows.Capability, new { op = "repair", workflowId = id, input = new { } }, Guid.NewGuid()).GetProperty("isError").GetBoolean());
            Check(service.Capture().Single().RepairCount == 1 && service.Capture().Single().Receipts.Length == 3 && f.Owner.Edit!.State.UndoCount == 0);
            foreach (var invalid in new[] { Plan(Guid.NewGuid(), [Step("ncma.scene.delete_object", new { })]), Plan(Guid.NewGuid(), [Step("ncma.python.eval", new { })]),
                Plan(Guid.NewGuid(), [Step(EditorWorkflows.Capability, new { })]), Plan(Guid.NewGuid(), Enumerable.Range(0,13).Select(_ => Step("ncma.scene.inspect",new{})).ToArray()),
                Plan(Guid.NewGuid(), [Step("ncma.scene.inspect", new { x = new string('x', EditorWorkflows.MaxStepBytes) })]), Plan(Guid.NewGuid(), [Step("ncma.scene.inspect", new { }, "passed")]) })
                Check(f.Call(EditorWorkflows.Capability, new { op = "propose", plan = invalid }, Guid.NewGuid()).GetProperty("isError").GetBoolean());
            for (int i = 1; i < EditorWorkflows.MaxPlans; i++) { Guid p = Propose(f, [Step("ncma.scene.inspect", new { })]); service.Cancel(p); }
            Check(f.Call(EditorWorkflows.Capability, new { op = "propose", plan = Plan(Guid.NewGuid(), [Step("ncma.scene.inspect", new { })]) }, Guid.NewGuid()).GetProperty("isError").GetBoolean());
            Check(service.Capture().Length == EditorWorkflows.MaxPlans);
        });
        yield return ("M6.9 full UI-page guard/same MCP ledger and actual foreground ImGui", () => {
            using var f = new GraphInspectionTests.Fixture(output, repository); Click(f, 40, 8); Click(f, 24, 12); Click(f, 24, 70); Click(f, 61, 1);
            Check(f.View.Items.ToArray().Single(i => i.WidgetHigh == 61 && i.WidgetLow == 7).Enabled == 0); Click(f, 61, 8); ReviewUi(f);
            Guid id = Propose(f, Enumerable.Range(0,12).Select(_ => Step("ncma.scene.validate", new { }, "valid")).ToArray()); f.Build(); Click(f, 61, 1000); Click(f, 61, 3);
            Check(f.View.Items.ToArray().Single(i => i.WidgetHigh == 61 && i.WidgetLow == 7).Enabled == 0);
            Click(f, 61, 8); Check(Op(f, "next", id).GetProperty("isError").GetBoolean()); ReviewUi(f);
            for (int i = 0; i < 12; i++) Data(Execute(f, Next(f, id))); f.Build(); Check(Encoding.UTF8.GetString(f.View.Text).Contains("completed"));
            Check(f.View.Items.ToArray().Single(i => i.WidgetHigh == 61 && i.WidgetLow == 11).Enabled == 1);
            Click(f, 61, 11); Click(f, 61, 11); Check(f.View.Items.ToArray().Single(i => i.WidgetHigh == 61 && i.WidgetLow == 11).Enabled == 0);
            Click(f, 61, 10); Click(f, 61, 10);
            var scene = f.Owner.Document.CaptureBytes(); var state = f.Owner.Edit!.State;
            using var loader = new PluginLoader(); loader.Load(plugins, [new("platform", ModuleKind.Platform, "NcmaPlatform.dll", "NcmaPlatform.dll", 1, 0, []), new("renderer", ModuleKind.Renderer, "NcmaRenderer.dll", "NcmaRenderer.dll", 1, 2, ["platform"]), new("gui", ModuleKind.Gui, "NcmaGui.dll", "NcmaGui.dll", 1, 7, ["platform", "renderer"])]);
            using var window = new PlatformWindow(loader.Modules.Single(p => p.Kind == ModuleKind.Platform), "M6.9 workflows", 1280, 720, false);
            using var renderer = new RendererSession(loader.Modules.Single(p => p.Kind == ModuleKind.Renderer), window, 1280, 720, pureUi: true);
            using var gui = new GuiSession(loader.Modules.Single(p => p.Kind == ModuleKind.Gui), window, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "msyh.ttc")); gui.AttachRenderer(renderer);
            using var white = renderer.CreateUiImage(1, 1, new byte[] { 255,255,255,255 }); var builder = new UiDisplayListBuilder(renderer);
            builder.Quad(new(Guid.NewGuid(), new(0,0,1,1), Matrix3x2.Identity, new(0,0,1,1), 1, true, -1), white, UiColor.Transparent); using var clear = builder.Build();
            for (int frame = 0; frame < 2; frame++) { gui.Begin(window.Poll(), 1d/60); f.Build(); Check(f.View.Items.ToArray().Single(i => i.WidgetHigh == 1 && i.WidgetLow == 413).Value == 2);
                gui.Draw(f.View.Frame, f.View.Items, f.View.Text); renderer.SubmitUi(clear, f.View.Frame.Frame, new(.02f,.03f,.04f,1)); gui.RenderGpu();
                if (frame == 1) { byte[] pixels = new byte[1280*720*4]; renderer.Capture(pixels); GraphAuthoringTests.SaveBmp(Path.Combine(output, "m6-9-workflow.bmp"), pixels, 1280, 720); } renderer.Present(); }
            renderer.WaitIdle(); Check(renderer.Stats.ValidationErrors == 0 && renderer.Stats.ValidationWarnings == 0 && renderer.UiStats.PureUi == 1 && renderer.Stats.LiveGroups == 0);
            Check(scene.SequenceEqual(f.Owner.Document.CaptureBytes()) && state == f.Owner.Edit.State && f.Owner.Document.World.Tick == 0);
        });
        yield return ("M6.9 owner/monitor observation forbids live writes/nested invocation and preserves editor-only registration", () => {
            using var owner = new EditorSessionOwner("Observe"); var edit = owner.Edit!; ulong revision = edit.Revision;
            Reject(() => edit.ObserveReadOnly(() => owner.Document.World.CreateObject("Forbidden")));
            var nested = edit.ObserveReadOnly(() => edit.Invoke(new(2, Guid.NewGuid(), edit.SessionId, edit.Revision, "ncma.scene.inspect", JsonSerializer.SerializeToElement(new { }))));
            Check(nested.Code == "edit_busy" && owner.Document.World.Count == 0 && edit.Revision == revision);
            var direct = edit.Invoke(new(2, Guid.NewGuid(), edit.SessionId, edit.Revision, EditorWorkflows.Capability, JsonSerializer.SerializeToElement(new { op = "next", workflowId = Guid.NewGuid() })));
            Check(direct.Code == "workflow_endpoint_context_required");
            Reject(() => Task.Run(() => owner.Workflows!.Capture()).GetAwaiter().GetResult());
            using var player = new EditorSessionOwner("Runtime only", activateEditor: false); Check(player.Workflows is null && player.Edit is null);
        });
    }
}
