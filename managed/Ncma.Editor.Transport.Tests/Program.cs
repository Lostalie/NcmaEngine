using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Editor.Protocol;
using Ncma.Editor.Transport;
using Ncma.Scene;

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, Wire.Json);
static void Until(Func<bool> condition, Action progress, int timeoutMs = 10000)
{
    var clock = Stopwatch.StartNew();
    while (!condition()) { if (clock.ElapsedMilliseconds > timeoutMs) throw new TimeoutException(); progress(); Thread.Sleep(1); }
}
try
{
if (args.Length == 2 && args[0] == "--live")
{
    RunMcp(args[1], true, () => { });
    Console.WriteLine("Live ImGui editor stdio -> private pipe -> current EditSession read-only smoke passed.");
    return 0;
}
Schema.Adversarial();
string root = Path.GetFullPath(".");
var doc = new SceneDocument("Pipe test"); var obj = doc.World.CreateObject("Original");
var edit = new EditSession(doc); using var endpoint = new EditorEndpoint(edit, root);
var descriptor = Wire.Decode<EndpointDescriptor>(File.ReadAllBytes(endpoint.DescriptorPath));
using var client = new NamedPipeClientStream(".", descriptor.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
client.Connect(3000);
var helloWrite = Wire.WriteAsync(client, Wire.Encode(new Hello(1, descriptor.InstanceId, descriptor.ProjectRoot, "Pipe test")), CancellationToken.None); helloWrite.GetAwaiter().GetResult();
var helloRead = Wire.ReadAsync(client, CancellationToken.None);
Until(() => endpoint.View.Connections.Length == 1, () => endpoint.Pump());
Check(!helloRead.IsCompleted, "Unpaired connection received a session credential");
endpoint.Pair(endpoint.View.Connections[0].ConnectionId, true);
Until(() => helloRead.IsCompleted, () => endpoint.Pump());
var hello = Wire.Decode<HelloResult>(helloRead.Result!);
Check(hello.SessionId == edit.SessionId && hello.Credential?.Length == 64, "Handshake routed to another session");
var request = new CapabilityRequest(2, Guid.NewGuid(), edit.SessionId, edit.Revision, "ncma.scene.inspect", Json(new { }));
Wire.WriteAsync(client, Wire.Encode(new IpcRequest("invoke", 1, Json(request))), CancellationToken.None).GetAwaiter().GetResult();
var responseTask = Wire.ReadAsync(client, CancellationToken.None);
Until(() => responseTask.IsCompleted, () => endpoint.Pump());
var result = Wire.Decode<IpcResponse>(responseTask.Result!);
Check(result.Result!.Value.GetProperty("data").GetProperty("scene").GetProperty("objects")[0].GetProperty("name").GetString() == "Original", "Not the active committed scene");
var rename = new CapabilityRequest(2, Guid.NewGuid(), edit.SessionId, edit.Revision, "ncma.scene.transaction", Json(new { operations = new[] { new { op = "rename", objectId = obj.PersistentId, name = "User changed" } } }));
Check(edit.Invoke(rename, new(["ncma.scene.transaction"])).Changed, "Trusted user mutation failed");
request = request with { RequestId = Guid.NewGuid(), ExpectedRevision = edit.Revision };
Wire.WriteAsync(client, Wire.Encode(new IpcRequest("invoke", 1, Json(request))), CancellationToken.None).GetAwaiter().GetResult();
responseTask = Wire.ReadAsync(client, CancellationToken.None); Until(() => responseTask.IsCompleted, () => endpoint.Pump());
Check(Wire.Decode<IpcResponse>(responseTask.Result!).Result!.Value.GetProperty("data").GetProperty("scene").GetProperty("objects")[0].GetProperty("name").GetString() == "User changed", "IPC has a cloned document");
var readonlyWrite = rename with { RequestId = Guid.NewGuid(), ExpectedRevision = edit.Revision };
Wire.WriteAsync(client, Wire.Encode(new IpcRequest("invoke", 1, Json(readonlyWrite))), CancellationToken.None).GetAwaiter().GetResult();
responseTask = Wire.ReadAsync(client, CancellationToken.None); Until(() => responseTask.IsCompleted, () => endpoint.Pump());
Check(Wire.Decode<IpcResponse>(responseTask.Result!).Result!.Value.GetProperty("status").GetString() == "denied" && edit.State.UndoCount == 1, "Unapproved remote write reached history");
// G4: trusted approvals, shared history, cache rechecks and revocation.
Guid objectId = doc.CaptureSnapshot().Objects[0].Id;
string? measureKind = null;
var measuredLatency = new List<double>(); var measuredAllocation = new List<long>();
IpcResponse InvokeRemote(CapabilityRequest r)
{
    long beforeAllocation = GC.GetAllocatedBytesForCurrentThread(); var measured = Stopwatch.StartNew();
    Wire.WriteAsync(client, Wire.Encode(new IpcRequest("invoke", endpoint.View.DocumentGeneration, Json(r), CallId: Guid.NewGuid())), CancellationToken.None).GetAwaiter().GetResult();
    var read = Wire.ReadAsync(client, CancellationToken.None); Until(() => read.IsCompleted, () => endpoint.Pump());
    var response = Wire.Decode<IpcResponse>(read.Result!);
    if (measureKind is not null) { measuredLatency.Add(measured.Elapsed.TotalMilliseconds); measuredAllocation.Add(GC.GetAllocatedBytesForCurrentThread() - beforeAllocation); }
    return response;
}
CapabilityRequest Rename(string name) => new(2, Guid.NewGuid(), edit.SessionId, edit.Revision, "ncma.scene.transaction",
    Json(new { operations = new[] { new { op = "rename", objectId, name } } }));
var approvedWrite = Rename("Agent changed");
Check(InvokeRemote(approvedWrite).Result!.Value.GetProperty("code").GetString() == "permission_denied", "Default write bypass");
endpoint.Approve(endpoint.Proposals.Single(p => p.RequestId == approvedWrite.RequestId).Id, true);
var applied = InvokeRemote(approvedWrite).Result!.Value;
Check(applied.GetProperty("changed").GetBoolean() && edit.State.UndoCount == 2, "Approved write did not share UI history");
Check(InvokeRemote(approvedWrite).Result!.Value.GetProperty("replayed").GetBoolean() && edit.State.UndoCount == 2, "Retry committed twice");
var userUndo = new CapabilityRequest(2, Guid.NewGuid(), edit.SessionId, edit.Revision, "ncma.history.undo", Json(new { }));
Check(edit.Invoke(userUndo, new(["ncma.scene.transaction", "ncma.history.undo"])).Changed, "UI Undo failed");
var remoteRedo = userUndo with { RequestId = Guid.NewGuid(), ExpectedRevision = edit.Revision, Capability = "ncma.history.redo" };
Check(InvokeRemote(remoteRedo).Result!.Value.GetProperty("changed").GetBoolean(), "Scoped MCP Redo failed");
endpoint.RevokeGrants(hello.ConnectionId);
Check(InvokeRemote(approvedWrite).Result!.Value.GetProperty("status").GetString() == "denied", "Cached retry bypassed revoke");
var remove = new CapabilityRequest(2, Guid.NewGuid(), edit.SessionId, edit.Revision, "ncma.scene.delete_object", Json(new { objectId }));
InvokeRemote(remove); var deletion = endpoint.Proposals.Single(p => p.RequestId == remove.RequestId);
bool missingConfirmation = false;
try { endpoint.Approve(deletion.Id, false); } catch (InvalidOperationException) { missingConfirmation = true; }
Check(missingConfirmation, "Deletion lacked exact UUID confirmation");
endpoint.Approve(deletion.Id, false, objectId);
Check(InvokeRemote(remove).Result!.Value.GetProperty("changed").GetBoolean(), "Confirmed deletion failed");
Check(edit.Invoke(new(2, Guid.NewGuid(), edit.SessionId, edit.Revision, "ncma.history.undo", Json(new { })),
    new(["ncma.history.undo", "ncma.scene.delete_object"], [objectId])).Changed, "User delete Undo failed");
var duplicate = Rename("Deduplicated"); InvokeRemote(duplicate);
endpoint.Approve(endpoint.Proposals.Single(p => p.RequestId == duplicate.RequestId).Id, false);
int beforeDuplicate = edit.State.UndoCount;
var callA = Guid.NewGuid(); var callB = Guid.NewGuid();
Wire.WriteAsync(client, Wire.Encode(new IpcRequest("invoke", endpoint.View.DocumentGeneration, Json(duplicate), CallId: callA)), CancellationToken.None).GetAwaiter().GetResult();
Wire.WriteAsync(client, Wire.Encode(new IpcRequest("invoke", endpoint.View.DocumentGeneration, Json(duplicate), CallId: callB)), CancellationToken.None).GetAwaiter().GetResult();
Until(() => endpoint.View.QueueCount == 1, () => { });
Thread.Sleep(30);
var readA = Wire.ReadAsync(client, CancellationToken.None); Until(() => readA.IsCompleted, () => endpoint.Pump());
var readB = Wire.ReadAsync(client, CancellationToken.None); Until(() => readB.IsCompleted, () => endpoint.Pump());
Check(edit.State.UndoCount == beforeDuplicate + 1 && endpoint.View.QueueCount == 0, "Pending duplicates installed twice");
var cancelled = Rename("Cancelled"); InvokeRemote(cancelled);
endpoint.Approve(endpoint.Proposals.Single(p => p.RequestId == cancelled.RequestId).Id, false);
ulong beforeCancel = edit.Revision; Guid cancelCall = Guid.NewGuid();
Wire.WriteAsync(client, Wire.Encode(new IpcRequest("invoke", endpoint.View.DocumentGeneration, Json(cancelled), CallId: cancelCall)), CancellationToken.None).GetAwaiter().GetResult();
Until(() => endpoint.View.QueueCount == 1, () => { });
Wire.WriteAsync(client, Wire.Encode(new IpcRequest("cancel", endpoint.View.DocumentGeneration, RequestId: cancelled.RequestId, CallId: Guid.NewGuid())), CancellationToken.None).GetAwaiter().GetResult();
var cancelA = Wire.ReadAsync(client, CancellationToken.None); Until(() => cancelA.IsCompleted, () => { });
var cancelB = Wire.ReadAsync(client, CancellationToken.None); Until(() => cancelB.IsCompleted, () => { });
endpoint.Pump(); Check(edit.Revision == beforeCancel && endpoint.View.QueueCount == 0, "Cancelled queued write installed");
edit.SetBehaviourCatalog(new(Guid.NewGuid(), [])); endpoint.Pump(); Check(endpoint.Grants.Length == 0, "Reload catalog kept old grants");
var beforePlay = Rename("Before Play"); InvokeRemote(beforePlay); endpoint.Approve(endpoint.Proposals.Single(p => p.RequestId == beforePlay.RequestId).Id, false);
edit.SetFrozen(true); endpoint.Pump(); Check(endpoint.Grants.Length == 0, "Play did not revoke grants");
Check(InvokeRemote(new(2, Guid.NewGuid(), edit.SessionId, null, "ncma.scene.inspect", Json(new { }))).Code == "ok", "Play broke committed edit reads");
edit.SetFrozen(false); endpoint.Pump();
var evicted = Rename("Eviction marker"); InvokeRemote(evicted);
endpoint.Approve(endpoint.Proposals.Single(p => p.RequestId == evicted.RequestId).Id, false); InvokeRemote(evicted);
for (int i = 0; i < 129; i++) Check(edit.Invoke(Rename("User " + i), new(["ncma.scene.transaction"])).Status == "ok", "Cache pressure failed");
Check(InvokeRemote(evicted).Code == "outcome_unknown", "Evicted retry reexecuted or misreported");
Console.WriteLine("G4 exact approvals/history/delete/revocation + pending dedup/cancel/freeze/cache-eviction tests passed.");
foreach (string mode in new[] { "read", "approved_write" })
{
    measuredLatency.Clear(); measuredAllocation.Clear();
    for (int i = 0; i < 40; i++)
    {
        var sample = mode == "read" ? new CapabilityRequest(2, Guid.NewGuid(), edit.SessionId, null, "ncma.scene.inspect", Json(new { })) : Rename("Measured " + i);
        if (mode != "read") { measureKind = null; InvokeRemote(sample); endpoint.Approve(endpoint.Proposals.Single(p => p.RequestId == sample.RequestId).Id, false); }
        measureKind = i >= 8 ? mode : null; var sampled = InvokeRemote(sample); measureKind = null;
        Check(sampled.Code == "ok" && sampled.Result!.Value.GetProperty("status").GetString() == "ok", "Measured request failed");
    }
    measuredLatency.Sort(); measuredAllocation.Sort();
    Console.WriteLine(JsonSerializer.Serialize(new { fixture = "single_connection_" + mode, samples = measuredLatency.Count,
        medianEndToEndMs = measuredLatency[16], p95EndToEndMs = measuredLatency[30], maxEndToEndMs = measuredLatency[^1],
        medianOwnerAndHarnessAllocationBytes = measuredAllocation[16], maxOwnerAndHarnessAllocationBytes = measuredAllocation[^1] }));
}
client.Dispose();
Until(() => !endpoint.View.Connections[0].Connected, () => endpoint.Pump());
RunMcp(endpoint.DescriptorPath, false, () => {
    foreach (var c in endpoint.View.Connections.Where(c => c.Connected && !c.Paired)) endpoint.Pair(c.ConnectionId, true);
    endpoint.Pump();
});
Check(Task.Run(() => { try { endpoint.Pump(); return false; } catch (InvalidOperationException) { return true; } }).Result, "Foreign thread pumped the model");
Concurrency.Run(root);
Lifetimes.Run(root);
endpoint.Dispose(); Check(!File.Exists(endpoint.DescriptorPath), "Owned discovery descriptor leaked");
Console.WriteLine("Local IPC owner-thread pairing, active document, default read-only, shutdown and real stdio subprocess passed.");
return 0;
}
catch (Exception error) { Console.Error.WriteLine("Transport test failed: " + error);
    if (args.Length == 2 && args[0] == "--live") { var descriptor = Wire.Decode<EndpointDescriptor>(File.ReadAllBytes(args[1]));
        File.WriteAllText(Path.Combine(descriptor.ProjectRoot, "out", "verification", "m1", "live-client-failure.log"), error.ToString()); }
    return 1; }

static void RunMcp(string descriptorPath, bool live, Action progress)
{
    string root = Wire.Decode<EndpointDescriptor>(File.ReadAllBytes(descriptorPath)).ProjectRoot;
    string helper = Path.Combine(root, "out", "managed", "editor-mcp", "Ncma.Editor.Mcp.dll");
    var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add(helper); start.ArgumentList.Add("--descriptor"); start.ArgumentList.Add(descriptorPath);
    using var process = Process.Start(start) ?? throw new Exception("MCP child did not start");
    var stderr = process.StandardError.ReadToEndAsync();
    try
    {
        JsonElement Send(object message)
        {
            process.StandardInput.WriteLine(JsonSerializer.Serialize(message, Wire.Json)); process.StandardInput.Flush();
            var read = process.StandardOutput.ReadLineAsync(); Until(() => read.IsCompleted, progress, 15000);
            return JsonDocument.Parse(read.Result ?? throw new Exception("MCP protocol EOF")).RootElement.Clone();
        }
        var initialized = Send(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "Ncma deterministic MCP client", version = "1.0" } } });
        Check(initialized.GetProperty("result").GetProperty("protocolVersion").GetString() == "2025-11-25", "Protocol negotiation failed");
        process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"); process.StandardInput.Flush();
        var listed = Send(new { jsonrpc = "2.0", id = 2, method = "tools/list", @params = new { } });
        var tools = listed.GetProperty("result").GetProperty("tools");
        Check(tools.GetArrayLength() == 10, "Unexpected implemented capability list");
        Check(tools.EnumerateArray().Single(t => t.GetProperty("name").GetString() == "ncma.scene.inspect").GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean(), "Read-only annotation incorrect");
        Guid session = tools[0].GetProperty("inputSchema").GetProperty("properties").GetProperty("sessionId").GetProperty("const").GetGuid();
        JsonElement Call(string name, object input, ulong? revision = null)
        {
            var result = Send(new { jsonrpc = "2.0", id = Guid.NewGuid().ToString(), method = "tools/call", @params = new { name,
                arguments = new { contractVersion = 2, requestId = Guid.NewGuid(), sessionId = session, expectedRevision = revision, input } } })
                .GetProperty("result").GetProperty("structuredContent");
            Check(Schema.Accepts(tools.EnumerateArray().Single(t => t.GetProperty("name").GetString() == name).GetProperty("outputSchema"), result), "Output does not match schema: " + name);
            return result;
        }
        var inspected = Call("ncma.scene.inspect", new { }); var revision = inspected.GetProperty("revision").GetUInt64();
        var objects = inspected.GetProperty("data").GetProperty("scene").GetProperty("objects"); Check(objects.GetArrayLength() > 0, "Live scene is empty");
        Guid id = (live ? objects.EnumerateArray().Single(o => o.GetProperty("name").GetString() == "Live MCP fixture") : objects[0]).GetProperty("id").GetGuid();
        if (live) Check(objects.EnumerateArray().Any(o => o.GetProperty("name").GetString() == "Live MCP fixture"), "Did not inspect trusted UI fixture");
        var detail = Call("ncma.scene.object.inspect", new { objectId = id, section = "components", limit = 1 }, revision);
        Check(detail.GetProperty("status").GetString() == "ok" && detail.GetProperty("data").GetProperty("complete").ValueKind is JsonValueKind.True or JsonValueKind.False, "Complete component paging failed");
        if (live) Check(Call("ncma.engine.behaviour_types", new { }, revision).GetProperty("data").GetProperty("items").GetArrayLength() > 0, "Loaded Behaviour catalog unavailable");
        var denied = Call("ncma.scene.transaction", new { operations = new[] { new { op = "rename", objectId = id, name = "Unauthorized" } } }, revision);
        Check(denied.GetProperty("status").GetString() == "denied" && !denied.GetProperty("changed").GetBoolean(), "MCP default write permission bypass");
        if (live)
        {
            Guid approvedId = Guid.NewGuid();
            object envelope = new { contractVersion = 2, requestId = approvedId, sessionId = session, expectedRevision = revision,
                input = new { operations = new[] { new { op = "rename", objectId = id, name = "Live MCP approved" } } } };
            JsonElement submit = default;
            Until(() => submit.ValueKind == JsonValueKind.Object && submit.GetProperty("status").GetString() == "ok", () => {
                submit = Send(new { jsonrpc = "2.0", id = Guid.NewGuid().ToString(), method = "tools/call",
                    @params = new { name = "ncma.scene.transaction", arguments = envelope } }).GetProperty("result").GetProperty("structuredContent");
                Thread.Sleep(5);
            });
            Check(submit.GetProperty("changed").GetBoolean(), "Live approved transaction did not commit");
            JsonElement afterUndo = default;
            Until(() => afterUndo.ValueKind == JsonValueKind.Object && afterUndo.GetProperty("revision").GetUInt64() > submit.GetProperty("executionRevision").GetUInt64(),
                () => afterUndo = Call("ncma.scene.inspect", new { }));
            var redo = Call("ncma.history.redo", new { }, afterUndo.GetProperty("revision").GetUInt64());
            Check(redo.GetProperty("changed").GetBoolean(), "Live user Undo / MCP Redo did not share history");
            Check(Call("ncma.scene.validate", new { }).GetProperty("data").GetProperty("valid").GetBoolean(), "Live document invalid after Redo");
        }
        Check(Call("ncma.scene.inspect", new { unknown = true }).GetProperty("status").GetString() == "error", "Unknown business field accepted");
        var bad = Send(new { jsonrpc = "2.0", id = 10, method = "unsupported" }); Check(bad.GetProperty("error").GetProperty("code").GetInt32() == -32601, "Unknown method not rejected");
        if (!live)
        {
            process.StandardInput.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = "cancel-me", method = "tools/call",
                @params = new { name = "ncma.scene.transaction", arguments = new { contractVersion = 2, requestId = Guid.NewGuid(),
                    sessionId = session, expectedRevision = revision, input = new { operations = new[] { new { op = "rename", objectId = id, name = "Must not run" } } } } } }, Wire.Json));
            process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":\"cancel-me\",\"reason\":\"test\"}}");
            process.StandardInput.Flush();
            var cancellation = process.StandardOutput.ReadLineAsync(); Until(() => cancellation.IsCompleted, () => { });
            var cancelled = JsonDocument.Parse(cancellation.Result!).RootElement;
            Check(cancelled.GetProperty("error").GetProperty("code").GetInt32() == -32800, "stdio cancellation not processed concurrently");
        }
        process.StandardInput.Close(); Until(() => process.HasExited, progress); Check(process.ExitCode == 0, "MCP helper shutdown failed");
        Check(stderr.Result.Length == 0, "Unexpected helper diagnostics");
    }
    finally
    {
        if (!process.HasExited) { process.Kill(); process.WaitForExit(3000); }
    }
}
