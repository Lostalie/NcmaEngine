using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Editor.Protocol;
using Ncma.Editor.Transport;
using Ncma.Runtime;
using Ncma.Scene;

// Development measurement of the real stdio helper, not an in-process MCP substitute.
internal static class EcsMcpCosts
{
    private static readonly string[] Workloads = ["page_cold_revision", "page_warm_first", "page_warm_last", "object_warm", "approved_transaction", "undo", "redo"];
    private static readonly FieldInfo CapturesField = typeof(SceneDocument).GetField("_capturedDocumentCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private readonly record struct Cost(Guid RequestId, ulong RevisionBefore, ulong RevisionAfter, long FullCaptures,
        double RoundtripMilliseconds, long OwnerThreadAllocatedBytes, double PumpMilliseconds,
        long PumpOwnerThreadAllocatedBytes, int Pumps, int ResponseBytes, int StructuredBytes);
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, Wire.Json);
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Check(bool condition, string label) { if (!condition) throw new Exception("E5-D MCP costs: " + label); }
    private static long Captures(SceneDocument doc) => (long)CapturesField.GetValue(doc)!;

    internal static int Run(string contractPath, string output, bool measured)
    {
        Check(Path.IsPathFullyQualified(output) && !Directory.Exists(output) && !File.Exists(output), "fresh absolute output required");
        using var contract = JsonDocument.Parse(File.ReadAllBytes(contractPath)); var c = contract.RootElement; var limits = c.GetProperty("limits");
        Check(c.GetProperty("schema").GetInt32() == 1 && c.GetProperty("id").GetString() == "ecs-e5-d-mcp-stdio-v1" &&
            c.GetProperty("domainContractSha256").GetString() == Hash(Path.Combine(Path.GetDirectoryName(contractPath)!, "ecs-e5d-domain-contract.json")) &&
            c.GetProperty("counts").EnumerateArray().Select(x => x.GetInt32()).SequenceEqual(new[] { 100, 1000, 4096 }) &&
            c.GetProperty("workloads").EnumerateArray().Select(x => x.GetString()).SequenceEqual(Workloads) && c.GetProperty("smokeCount").GetInt32() == 8 &&
            c.GetProperty("sampling").GetProperty("warmup").GetInt32() == 12 && c.GetProperty("sampling").GetProperty("samples").GetInt32() == 100 &&
            limits.GetProperty("page").GetInt32() == 128 && limits.GetProperty("detail").GetInt32() == 64 &&
            limits.GetProperty("stdioFrameBytes").GetInt32() == Wire.MaxMessageBytes && limits.GetProperty("structuredOutputBytes").GetInt32() == EditSession.MaxReadOutputBytes &&
            limits.GetProperty("requestTimeoutSeconds").GetInt32() == 15 && limits.GetProperty("pollSleepMilliseconds").GetInt32() == 1, "contract matrix/bounds");
#if DEBUG
        if (measured) throw new InvalidOperationException("Release required for MCP measurement.");
#endif
        string helper = Path.Combine(AppContext.BaseDirectory, "Ncma.Editor.Mcp.dll");
        string[] helperFiles = [helper, Path.ChangeExtension(helper, ".deps.json"), Path.ChangeExtension(helper, ".runtimeconfig.json"), Path.Combine(AppContext.BaseDirectory, "Ncma.Editor.Protocol.dll")];
        var helperHashes = helperFiles.Select(Hash).ToArray();
        Directory.CreateDirectory(output); var fixtures = new List<object>();
        foreach (int count in measured ? new[] { 100, 1000, 4096 } : new[] { 8 }) fixtures.Add(Measure(output, helper, count, measured));
        Check(helperFiles.Select(Hash).SequenceEqual(helperHashes), "helper changed during observations");
        string path = Path.Combine(output, "mcp-costs.json");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(stream, new { schema = 3, authoringModel = "ncma.authoring-scene", authoringModelVersion = 1, historicalPerformanceAcceptance = false, workloadContractId = c.GetProperty("id").GetString(), contractSha256 = Hash(contractPath),
                measured, scope = c.GetProperty("scope").GetString(), allocationScope = c.GetProperty("allocationScope").GetString(), pumpScope = c.GetProperty("pumpScope").GetString(),
                processId = Environment.ProcessId, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, fixtures,
                products = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && a.GetName().Name!.StartsWith("Ncma.", StringComparison.Ordinal))
                    .OrderBy(a => a.GetName().Name).Select(a => new { name = a.GetName().Name, path = a.Location, sha256 = Hash(a.Location) }),
                helperFiles = helperFiles.Select((p, i) => new { path = p, sha256 = helperHashes[i] }),
                formalClosureVerified = false, targetBudgetAccepted = false, memoryAcceptance = "pending", fullPerformanceAccepted = false, deployed = false });
        Console.WriteLine($"E5-D actual MCP stdio cost fixtures: {fixtures.Count} passed; measured={measured}; {path}"); return 0;
    }

    private static object Measure(string root, string helper, int count, bool measured)
    {
        var doc = new SceneDocument("E5-D stdio fixture");
        // Cold authoring setup is not part of the timed transport/history workloads.
        doc.RestoreSnapshot(new(1, doc.Name, Enumerable.Range(0, count).Select(i =>
            new SceneObjectData(new Guid(count - i, 0, 0, new byte[8]), "Object " + i,
                [new ComponentSnapshot("ncma.transform", 1, doc.Components.Encode(TransformData.Identity with { Position = new(i, 2, 3) }))], [])).ToArray()));
        byte[] startup = doc.CaptureBytes(); var original = SceneDocumentCodec.Decode(startup); Guid id = original.Objects[^1].Id;
        var edit = new EditSession(doc); string descriptorPath; object result;
        using (var endpoint = new EditorEndpoint(edit, root)) {
            descriptorPath = endpoint.DescriptorPath;
            var descriptor = Wire.Decode<EndpointDescriptor>(File.ReadAllBytes(descriptorPath));
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(helper); start.ArgumentList.Add("--descriptor"); start.ArgumentList.Add(descriptorPath);
            using var process = Process.Start(start) ?? throw new IOException("MCP child did not start");
            var stderr = process.StandardError.ReadToEndAsync(); int childId = process.Id;
            string childStartedUtc = process.StartTime.ToUniversalTime().ToString("O");
            Check(childId != Environment.ProcessId, "MCP is a separate process");
            var requests = new List<Guid>(); Guid connection = default; bool pairDuringSetup = false;
            try {
                void Progress() {
                    if (pairDuringSetup && endpoint.View.Connections is [{ Paired: false } pending]) {
                        Check(pending.ClientName == "Ncma MCP stdio", "unexpected fixture client");
                        connection = pending.ConnectionId; endpoint.Pair(connection, true); // Explicit TEST setup only.
                        pairDuringSetup = false;
                    }
                    endpoint.Pump(); Thread.Sleep(1);
                }
                (JsonElement Rpc, Cost Cost) Send(object message, Guid requestId = default) {
                    ulong before = edit.Revision; long captures = Captures(doc), allocation = GC.GetAllocatedBytesForCurrentThread();
                    long ticks = Stopwatch.GetTimestamp(), pumpTicks = 0, pumpBytes = 0; int pumps = 0;
                    process.StandardInput.WriteLine(JsonSerializer.Serialize(message, Wire.Json)); process.StandardInput.Flush();
                    var read = process.StandardOutput.ReadLineAsync();
                    while (!read.IsCompleted) {
                        if (Stopwatch.GetElapsedTime(ticks).TotalSeconds > 15) throw new TimeoutException("MCP request timeout");
                        if (pairDuringSetup) { Progress(); continue; } // Startup only, never in measured calls.
                        long bytes = GC.GetAllocatedBytesForCurrentThread(), time = Stopwatch.GetTimestamp(); endpoint.Pump();
                        pumpTicks += Stopwatch.GetTimestamp() - time; pumpBytes += GC.GetAllocatedBytesForCurrentThread() - bytes; pumps++; Thread.Sleep(1);
                    }
                    string line = read.GetAwaiter().GetResult() ?? throw new IOException("MCP stdout EOF");
                    int responseBytes = Wire.Utf8.GetByteCount(line); Check(responseBytes <= Wire.MaxMessageBytes, "stdio output bound");
                    using var json = JsonDocument.Parse(line); var rpc = json.RootElement.Clone();
                    int structured = rpc.TryGetProperty("result", out var r) && r.TryGetProperty("structuredContent", out var content) ? Wire.Utf8.GetByteCount(content.GetRawText()) : 0;
                    double elapsed = Stopwatch.GetElapsedTime(ticks).TotalMilliseconds; long allocated = GC.GetAllocatedBytesForCurrentThread() - allocation;
                    return (rpc, new(requestId, before, edit.Revision, Captures(doc) - captures, elapsed, allocated, pumpTicks * 1000d / Stopwatch.Frequency, pumpBytes, pumps, responseBytes, structured));
                }
                var initialize = Send(new { jsonrpc = "2.0", id = "init", method = "initialize", @params = new {
                    protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "isolated E5-D probe", version = "1" } } });
                Check(initialize.Rpc.GetProperty("result").GetProperty("protocolVersion").GetString() == "2025-11-25", "protocol");
                process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"); process.StandardInput.Flush();
                // First start the tools/list request WITHOUT pairing; it must stay pending.
                process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":\"list\",\"method\":\"tools/list\",\"params\":{}}"); process.StandardInput.Flush();
                var listRead = process.StandardOutput.ReadLineAsync(); long waiting = Stopwatch.GetTimestamp();
                while (endpoint.View.Connections.Length == 0) { Check(Stopwatch.GetElapsedTime(waiting).TotalSeconds < 15, "connection setup timeout"); Progress(); }
                Check(!listRead.IsCompleted && !endpoint.View.Connections[0].Paired, "mandatory pairing"); pairDuringSetup = true;
                while (!listRead.IsCompleted) { Check(Stopwatch.GetElapsedTime(waiting).TotalSeconds < 15, "pairing timeout"); Progress(); }
                using var listed = JsonDocument.Parse(listRead.GetAwaiter().GetResult()!); var tools = listed.RootElement.GetProperty("result").GetProperty("tools").Clone();
                Check(tools.GetArrayLength() == 10 && tools[0].GetProperty("inputSchema").GetProperty("properties").GetProperty("sessionId").GetProperty("const").GetGuid() == edit.SessionId, "live schema/session");
                Check(endpoint.Audit.Length == 1 && endpoint.Audit[0].Capability == "ncma.capabilities.list", "one actual helper startup catalog");
                requests.Add(endpoint.Audit[0].RequestId);
                CapabilityRequest Request(string capability, object input) => new(2, Guid.NewGuid(), edit.SessionId, edit.Revision, capability, Json(input));
                (JsonElement Answer, Cost Cost) Call(CapabilityRequest request, bool auditExpected = true) {
                    string call = Guid.NewGuid().ToString();
                    var sample = Send(new { jsonrpc = "2.0", id = call, method = "tools/call", @params = new { name = request.Capability,
                        arguments = new { request.ContractVersion, request.RequestId, request.SessionId, request.ExpectedRevision, request.Input } } }, request.RequestId);
                    Check(sample.Rpc.GetProperty("id").GetString() == call, "response correlation");
                    var answer = sample.Rpc.GetProperty("result").GetProperty("structuredContent");
                    Check(answer.GetProperty("requestId").GetGuid() == request.RequestId && answer.GetProperty("sessionId").GetGuid() == edit.SessionId &&
                        sample.Cost.StructuredBytes <= EditSession.MaxReadOutputBytes && Schema.Accepts(tools.EnumerateArray().Single(t => t.GetProperty("name").GetString() == request.Capability).GetProperty("outputSchema"), answer), "structured identity/schema/budget");
                    Check(sample.Rpc.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString() == answer.GetRawText(), "text/structured equivalence");
                    Check(double.IsFinite(sample.Cost.RoundtripMilliseconds) && sample.Cost.RoundtripMilliseconds >= sample.Cost.PumpMilliseconds &&
                        sample.Cost.PumpMilliseconds >= 0 && sample.Cost.OwnerThreadAllocatedBytes >= sample.Cost.PumpOwnerThreadAllocatedBytes && sample.Cost.PumpOwnerThreadAllocatedBytes >= 0, "nested costs");
                    if (auditExpected) requests.Add(request.RequestId); return (answer.Clone(), sample.Cost);
                }
                int warmup = measured ? 12 : 1, samples = measured ? 100 : 2; string firstName = "Object 0";
                var rows = Workloads.ToDictionary(w => w, _ => new List<Cost>());
                void ReadCheck(JsonElement answer, int offset, bool detail) {
                    Check(answer.GetProperty("status").GetString() == "ok" && !answer.GetProperty("changed").GetBoolean(), "read result");
                    var data = answer.GetProperty("data");
                    if (detail) {
                        var items = data.GetProperty("items"); Check(items.GetArrayLength() == 1 && items[0].GetProperty("data").GetRawText() == original.Objects[^1].Components[0].Data.GetRawText(), "full detail codec oracle");
                    } else {
                        var objects = data.GetProperty("scene").GetProperty("objects");
                        Check(data.GetProperty("documentSessionId").GetGuid() == doc.SessionId && data.GetProperty("documentRevision").GetUInt64() == doc.Revision && data.GetProperty("model").GetString() == "ncma.authoring-scene" && data.GetProperty("modelVersion").GetInt32() == 1 && data.GetProperty("totalCount").GetInt32() == count && objects.GetArrayLength() == Math.Min(128, count - offset), "page identity/count");
                        for (int i = 0; i < objects.GetArrayLength(); i++) {
                            int index = offset + i; var obj = objects[i]; var component = obj.GetProperty("components");
                            Check(obj.GetProperty("id").GetGuid() == original.Objects[index].Id && obj.GetProperty("name").GetString() == (index == 0 ? firstName : original.Objects[index].Name) &&
                                component.GetArrayLength() == 1 && component[0].GetProperty("typeId").GetString() == original.Objects[index].Components[0].TypeId && component[0].GetProperty("version").GetInt32() == original.Objects[index].Components[0].Version, "full page order/descriptor oracle");
                        }
                    }
                }
                var prime = Call(Request("ncma.scene.inspect", new { offset = 0, limit = 128 })); ReadCheck(prime.Answer, 0, false); Check(prime.Cost.FullCaptures == 0, "first authoring read performs no full capture");
                foreach (string workload in Workloads[..4]) {
                    int offset = workload == "page_warm_last" ? Math.Max(0, count - 128) : 0; bool detail = workload == "object_warm";
                    for (int i = 0; i < warmup + samples; i++) {
                        if (workload == "page_cold_revision") {
                            firstName = "Cold " + i;
                            Check(edit.Invoke(Request("ncma.scene.transaction", new { operations = new[] { new { op = "rename", objectId = original.Objects[0].Id, name = firstName } } }), new(["ncma.scene.transaction"])).Changed, "untimed trusted cold invalidation");
                        }
                        var sample = Call(detail ? Request("ncma.scene.object.inspect", new { objectId = id, section = "components", limit = 64 }) : Request("ncma.scene.inspect", new { offset, limit = 128 }));
                        ReadCheck(sample.Answer, offset, detail); Check(sample.Cost.FullCaptures == 0, "cold/warm authoring capture count");
                        if (i >= warmup) rows[workload].Add(sample.Cost);
                    }
                }
                var expectedObjects = (SceneObjectData[])original.Objects.Clone(); expectedObjects[0] = expectedObjects[0] with { Name = firstName };
                var expected = original with { Objects = expectedObjects }; CapabilityRequest last = null!;
                void Semantic(SceneDocumentSnapshot oracle) => Check(doc.CaptureBytes().SequenceEqual(SceneDocumentCodec.Encode(oracle)), "complete history codec oracle");
                void Commit((JsonElement Answer, Cost Cost) sample, SceneDocumentSnapshot oracle) {
                    Check(sample.Answer.GetProperty("status").GetString() == "ok" && sample.Answer.GetProperty("changed").GetBoolean() && !sample.Answer.GetProperty("replayed").GetBoolean() && sample.Cost.RevisionAfter == sample.Cost.RevisionBefore + 1, "single MCP commit"); Semantic(oracle);
                }
                Semantic(expected);
                for (int i = 0; i < warmup + samples; i++) {
                    var objects = (SceneObjectData[])expected.Objects.Clone(); objects[^1] = objects[^1] with { Name = "Approved " + i }; var changed = expected with { Objects = objects };
                    last = Request("ncma.scene.transaction", new { operations = new[] { new { op = "rename", objectId = id, name = objects[^1].Name } } });
                    ulong before = edit.Revision;
                    Check(Call(last).Answer.GetProperty("code").GetString() == "permission_denied" && edit.Revision == before, "no implicit/new-request grant");
                    endpoint.Approve(endpoint.Proposals.Single(p => p.RequestId == last.RequestId).Id, true);
                    var write = Call(last); Commit(write, changed); int undoCount = edit.State.UndoCount;
                    Check(undoCount is >= 1 and <= EditSession.MaxHistoryEntries && edit.State.RedoCount == 0, "bounded history");
                    Check(Call(last).Answer.GetProperty("replayed").GetBoolean() && edit.Revision == write.Cost.RevisionAfter && edit.State.UndoCount == undoCount, "retry is not new write");
                    ulong previousRevision = doc.Revision; var undo = Call(Request("ncma.history.undo", new { })); Commit(undo, expected);
                    Check(edit.State.UndoCount == undoCount - 1 && edit.State.RedoCount == 1 && doc.Revision == previousRevision + 1, "Undo authoring revision/history");
                    previousRevision = doc.Revision; var redo = Call(Request("ncma.history.redo", new { })); Commit(redo, changed);
                    Check(edit.State.UndoCount == undoCount && edit.State.RedoCount == 0 && doc.Revision == previousRevision + 1, "Redo authoring revision/history");
                    if (i >= warmup) { rows[Workloads[4]].Add(write.Cost); rows[Workloads[5]].Add(undo.Cost); rows[Workloads[6]].Add(redo.Cost); } expected = changed;
                }
                ulong finalRevision = edit.Revision; endpoint.RevokeGrants(connection);
                Check(Call(last).Answer.GetProperty("code").GetString() == "permission_denied" && Call(Request("ncma.history.undo", new { })).Answer.GetProperty("code").GetString() == "permission_denied" && edit.Revision == finalRevision, "revoked grant and retry/history");
                endpoint.Revoke(connection);
                var revokedRequest = Request("ncma.scene.inspect", new { offset = 0, limit = 128 });
                var revoked = Send(new { jsonrpc = "2.0", id = "revoked", method = "tools/call", @params = new { name = revokedRequest.Capability,
                    arguments = new { revokedRequest.ContractVersion, revokedRequest.RequestId, revokedRequest.SessionId, revokedRequest.ExpectedRevision, revokedRequest.Input } } });
                Check(revoked.Rpc.GetProperty("error").GetProperty("code").GetInt32() == -32602 && endpoint.Grants.Length == 0, "revoked connection session refresh rejected");
                Semantic(expected);
                process.StandardInput.Close(); long shutdown = Stopwatch.GetTimestamp();
                while (!process.HasExited) { Check(Stopwatch.GetElapsedTime(shutdown).TotalSeconds < 10, "child shutdown timeout"); Progress(); }
                Check(process.ExitCode == 0 && stderr.GetAwaiter().GetResult().Length == 0, "clean real helper exit");
                var audit = EcsPipeAudit.Verify(descriptorPath, descriptor.InstanceId, edit.SessionId, connection, requests);
                result = new { objects = count, warmup, sampleCount = samples, startupHash = Convert.ToHexString(SHA256.HashData(startup)),
                    finalHash = Convert.ToHexString(SHA256.HashData(doc.CaptureBytes())), childProcessId = childId, childStartedUtc, childExitCode = process.ExitCode,
                    pairingRequired = true, exactApprovalRequired = true, semanticChecksPassed = true, schemaValidated = true,
                    retryIdempotent = true, singleHistory = true, restoreInvalidatesRevision = true, revokedGrantRejected = true,
                    revokedConnectionRejected = true, cleanShutdown = true, audit, rows = Workloads.Select(w => new { workload = w, raw = rows[w] }).ToArray() };
            }
            finally { if (!process.HasExited) { process.Kill(); if (!process.WaitForExit(5000)) throw new TimeoutException("Owned MCP child did not stop"); } }
        }
        Check(!File.Exists(descriptorPath), "owned descriptor released"); return result;
    }
}
