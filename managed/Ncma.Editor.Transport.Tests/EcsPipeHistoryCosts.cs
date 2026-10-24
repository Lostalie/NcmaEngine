using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Editor.Protocol;
using Ncma.Editor.Transport;
using Ncma.Runtime;
using Ncma.Scene;

// Isolated test fixture only. Trusted approvals are explicit, never a product IPC capability.
internal static class EcsPipeHistoryCosts
{
    private static readonly string[] Workloads = ["approved_transaction", "undo", "redo"];
    private readonly record struct Cost(Guid RequestId, ulong RevisionBefore, ulong RevisionAfter,
        double RoundtripMilliseconds, long OwnerThreadAllocatedBytes, double PumpMilliseconds,
        long PumpOwnerThreadAllocatedBytes, int Pumps, int ResponseBytes);
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, Wire.Json);
    private static void Check(bool value, string label) { if (!value) throw new Exception("E5-D pipe history: " + label); }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    internal static int Run(string contractPath, string output, bool measured)
    {
        Check(Path.IsPathFullyQualified(output) && !Directory.Exists(output) && !File.Exists(output), "fresh absolute output required");
        using var contract = JsonDocument.Parse(File.ReadAllBytes(contractPath)); var c = contract.RootElement;
        var history = c.GetProperty("pipeHistory"); var sampling = c.GetProperty("sampling");
        Check(c.GetProperty("schema").GetInt32() == 2 && c.GetProperty("id").GetString() == "ecs-e5-d-domain-v2-audit-inclusive" &&
            history.GetProperty("counts").EnumerateArray().Select(x => x.GetInt32()).SequenceEqual(new[] { 100, 1000, 4096 }) &&
            history.GetProperty("workloads").EnumerateArray().Select(x => x.GetString()).SequenceEqual(Workloads) &&
            history.GetProperty("smokeCount").GetInt32() == 8 && sampling.GetProperty("warmup").GetInt32() == 12 &&
            sampling.GetProperty("samples").GetInt32() == 100 && sampling.GetProperty("processes").GetInt32() == 3 &&
            !c.GetProperty("fullPerformanceAccepted").GetBoolean() && !c.GetProperty("deployed").GetBoolean(), "contract boundary");
#if DEBUG
        if (measured) throw new InvalidOperationException("Release required for history measurement.");
#endif
        Directory.CreateDirectory(output);
        var fixtures = new List<object>();
        foreach (int count in measured ? new[] { 100, 1000, 4096 } : new[] { 8 }) fixtures.Add(Measure(output, count, measured));
        string path = Path.Combine(output, "pipe-history-costs.json");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(stream, new { schema = 3, authoringModel = "ncma.authoring-scene", authoringModelVersion = 1, historicalPerformanceAcceptance = false, workloadContractId = c.GetProperty("id").GetString(), contractSha256 = Hash(contractPath),
                measured, scope = history.GetProperty("scope").GetString(), allocationScope = c.GetProperty("pipeReads").GetProperty("allocationScope").GetString(),
                processId = Environment.ProcessId, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, fixtures,
                products = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && a.GetName().Name!.StartsWith("Ncma.", StringComparison.Ordinal))
                    .OrderBy(a => a.GetName().Name).Select(a => new { name = a.GetName().Name, path = a.Location, sha256 = Hash(a.Location) }),
                formalClosureVerified = false, targetBudgetAccepted = false, memoryAcceptance = "pending", fullPerformanceAccepted = false, deployed = false });
        Console.WriteLine($"E5-D actual private-pipe history fixtures: {fixtures.Count} passed; measured={measured}; {path}");
        return 0;
    }

    private static object Measure(string root, int count, bool measured)
    {
        var doc = new SceneDocument("E5-D history fixture");
        // Cold authoring setup is not part of the timed transport/history workloads.
        doc.RestoreSnapshot(new(1, doc.Name, Enumerable.Range(0, count).Select(i =>
            new SceneObjectData(new Guid(count - i, 0, 0, new byte[8]), "Object " + i,
                [new ComponentSnapshot("ncma.transform", 1, doc.Components.Encode(TransformData.Identity with { Position = new(i, 2, 3) }))], [])).ToArray()));
        byte[] startup = doc.CaptureBytes(); var expected = SceneDocumentCodec.Decode(startup); Guid id = expected.Objects[^1].Id;
        var edit = new EditSession(doc); string descriptorPath; object result;
        using (var endpoint = new EditorEndpoint(edit, root)) {
            descriptorPath = endpoint.DescriptorPath;
            var descriptor = Wire.Decode<EndpointDescriptor>(File.ReadAllBytes(descriptorPath));
            using var pipe = new NamedPipeClientStream(".", descriptor.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            pipe.Connect(3000);
            void Until(Func<bool> done) {
                long start = Stopwatch.GetTimestamp();
                while (!done()) { if (Stopwatch.GetElapsedTime(start).TotalSeconds > 10) throw new TimeoutException("History pipe timeout"); endpoint.Pump(); Thread.Sleep(1); }
            }
            Wire.WriteAsync(pipe, Wire.Encode(new Hello(1, descriptor.InstanceId, root, "isolated E5-D history test")), CancellationToken.None).GetAwaiter().GetResult();
            var handshake = Wire.ReadAsync(pipe, CancellationToken.None); Until(() => endpoint.View.Connections.Length == 1);
            Check(!handshake.IsCompleted, "no implicit pairing"); endpoint.Pair(endpoint.View.Connections[0].ConnectionId, true);
            Until(() => handshake.IsCompleted); var hello = Wire.Decode<HelloResult>(handshake.Result!);
            Check(hello.SessionId == edit.SessionId, "active session");
            var auditedRequests = new List<Guid>();
            CapabilityRequest Request(string capability, object input) => new(EditSession.ContractVersion, Guid.NewGuid(), edit.SessionId, edit.Revision, capability, Json(input));
            (IpcResponse Response, Cost Cost) Send(CapabilityRequest request) {
                ulong generation = endpoint.View.DocumentGeneration, before = edit.Revision;
                long allocation = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp(), pumpTicks = 0, pumpBytes = 0; int pumps = 0;
                Wire.WriteAsync(pipe, Wire.Encode(new IpcRequest("invoke", generation, Json(request), CallId: Guid.NewGuid())), CancellationToken.None).GetAwaiter().GetResult();
                var task = Wire.ReadAsync(pipe, CancellationToken.None);
                while (!task.IsCompleted) {
                    if (Stopwatch.GetElapsedTime(start).TotalSeconds > 10) throw new TimeoutException("Timed history pipe timeout");
                    long allocated = GC.GetAllocatedBytesForCurrentThread(), ticks = Stopwatch.GetTimestamp(); endpoint.Pump();
                    pumpTicks += Stopwatch.GetTimestamp() - ticks; pumpBytes += GC.GetAllocatedBytesForCurrentThread() - allocated; pumps++; Thread.Sleep(1);
                }
                byte[] bytes = task.GetAwaiter().GetResult() ?? throw new IOException("History pipe closed"); var response = Wire.Decode<IpcResponse>(bytes);
                double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds; long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocation;
                if (response.Code == "ok") auditedRequests.Add(request.RequestId);
                return (response, new(request.RequestId, before, edit.Revision, elapsed, allocatedBytes, pumpTicks * 1000d / Stopwatch.Frequency, pumpBytes, pumps, bytes.Length));
            }
            JsonElement Answer(IpcResponse response) { Check(response.Code == "ok" && response.Result.HasValue, "invoke response"); return response.Result!.Value; }
            void Semantic(SceneDocumentSnapshot oracle) => Check(doc.CaptureBytes().SequenceEqual(SceneDocumentCodec.Encode(oracle)), "entire independent codec oracle");
            void Committed((IpcResponse Response, Cost Cost) sample, SceneDocumentSnapshot oracle) {
                var answer = Answer(sample.Response);
                Check(answer.GetProperty("status").GetString() == "ok" && answer.GetProperty("changed").GetBoolean() &&
                    !answer.GetProperty("replayed").GetBoolean() && sample.Cost.RevisionAfter == sample.Cost.RevisionBefore + 1, "single commit");
                Check(double.IsFinite(sample.Cost.RoundtripMilliseconds) && sample.Cost.RoundtripMilliseconds >= sample.Cost.PumpMilliseconds &&
                    sample.Cost.PumpMilliseconds >= 0 && sample.Cost.OwnerThreadAllocatedBytes >= sample.Cost.PumpOwnerThreadAllocatedBytes &&
                    sample.Cost.PumpOwnerThreadAllocatedBytes >= 0 && sample.Cost.Pumps > 0 && sample.Cost.ResponseBytes <= EditSession.MaxReadOutputBytes, "nested bounded cost");
                Semantic(oracle);
            }
            int warmup = measured ? 12 : 1, samples = measured ? 100 : 2;
            var costs = Workloads.ToDictionary(w => w, _ => new List<Cost>()); CapabilityRequest last = null!;
            for (int i = 0; i < warmup + samples; i++) {
                var objects = (SceneObjectData[])expected.Objects.Clone(); objects[^1] = objects[^1] with { Name = "Approved " + i };
                var changed = expected with { Objects = objects };
                last = Request("ncma.scene.transaction", new { operations = new[] { new { op = "rename", objectId = id, name = objects[^1].Name } } });
                ulong revision = edit.Revision;
                Check(Answer(Send(last).Response).GetProperty("code").GetString() == "permission_denied" && edit.Revision == revision, "default and previous grants cannot authorize new request");
                endpoint.Approve(endpoint.Proposals.Single(p => p.RequestId == last.RequestId).Id, true); // Explicit trusted TEST action, untimed.
                var write = Send(last); Committed(write, changed); int undoCount = edit.State.UndoCount;
                Check(undoCount >= 1 && undoCount <= EditSession.MaxHistoryEntries && edit.State.RedoCount == 0, "single bounded history");
                var replay = Answer(Send(last).Response);
                Check(replay.GetProperty("replayed").GetBoolean() && edit.Revision == write.Cost.RevisionAfter && edit.State.UndoCount == undoCount, "retry does not commit twice");
                ulong previousRevision = doc.Revision;
                var undo = Send(Request("ncma.history.undo", new { })); Committed(undo, expected);
                Check(edit.State.UndoCount == undoCount - 1 && edit.State.RedoCount == 1 && doc.Revision == previousRevision + 1, "undo shares history and invalidates the previous authoring revision");
                previousRevision = doc.Revision;
                var redo = Send(Request("ncma.history.redo", new { })); Committed(redo, changed);
                Check(edit.State.UndoCount == undoCount && edit.State.RedoCount == 0 && doc.Revision == previousRevision + 1, "redo shares history and invalidates the previous authoring revision");
                if (i >= warmup) { costs[Workloads[0]].Add(write.Cost); costs[Workloads[1]].Add(undo.Cost); costs[Workloads[2]].Add(redo.Cost); }
                expected = changed;
            }
            ulong finalRevision = edit.Revision; endpoint.RevokeGrants(hello.ConnectionId);
            Check(Answer(Send(last).Response).GetProperty("code").GetString() == "permission_denied" &&
                Answer(Send(Request("ncma.history.undo", new { })).Response).GetProperty("code").GetString() == "permission_denied" &&
                edit.Revision == finalRevision, "revocation rejects cached write and history"); Semantic(expected);
            endpoint.Revoke(hello.ConnectionId);
            Check(Send(Request("ncma.scene.inspect", new { })).Response.Code == "connection_revoked", "connection revoked");
            var audit = EcsPipeAudit.Verify(descriptorPath, descriptor.InstanceId, edit.SessionId, hello.ConnectionId, auditedRequests);
            result = new { objects = count, warmup, sampleCount = samples, startupHash = Convert.ToHexString(SHA256.HashData(startup)),
                finalHash = Convert.ToHexString(SHA256.HashData(doc.CaptureBytes())), semanticChecksPassed = true, pairingRequired = true,
                exactApprovalRequired = true, retryIdempotent = true, singleHistory = true, restoreInvalidatesRevision = true,
                revokedGrantRejected = true, revokedConnectionRejected = true, audit,
                rows = Workloads.Select(w => new { workload = w, raw = costs[w] }).ToArray() };
        }
        Check(!File.Exists(descriptorPath), "descriptor released"); return result;
    }
}
