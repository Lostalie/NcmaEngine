using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Editor.Protocol;
using Ncma.Editor.Transport;
using Ncma.Runtime;
using Ncma.Scene;

// Test-only actual endpoint observations. Never enables an existing editor endpoint,
// grants a write capability, or claims transport wall time is simulation CPU time.
internal static class EcsPipeCosts
{
    private const string Contract = "ecs-e5-d-domain-v2-audit-inclusive";
    private static readonly string[] Workloads = ["page_cold_revision", "page_warm_first", "page_warm_last", "object_warm"];
    private static readonly FieldInfo CapturesField = typeof(SceneDocument).GetField("_capturedDocumentCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private readonly record struct Cost(double RoundtripMilliseconds, long OwnerThreadAllocatedBytes,
        double PumpMilliseconds, long PumpOwnerThreadAllocatedBytes, int Pumps, int ResponseBytes, long FullCaptures);
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, Wire.Json);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("E5-D pipe cost: " + message); }
    private static long Captures(SceneDocument doc) => (long)CapturesField.GetValue(doc)!;
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    internal static int Run(string contractPath, string output, bool measured)
    {
        // Caller gives a fresh explicit output directory. Do not overwrite failed evidence.
        Check(Path.IsPathFullyQualified(output) && !Directory.Exists(output) && !File.Exists(output), "fresh absolute output required");
        using var contract = JsonDocument.Parse(File.ReadAllBytes(contractPath));
        var c = contract.RootElement; var reads = c.GetProperty("pipeReads"); var sampling = c.GetProperty("sampling");
        Check(c.GetProperty("schema").GetInt32() == 2 && c.GetProperty("id").GetString() == Contract, "contract identity");
        Check(reads.GetProperty("counts").EnumerateArray().Select(x => x.GetInt32()).SequenceEqual(new[] { 100, 1000, 4096 }) &&
            reads.GetProperty("workloads").EnumerateArray().Select(x => x.GetString()).SequenceEqual(Workloads), "exact matrix");
        Check(sampling.GetProperty("warmup").GetInt32() == 12 && sampling.GetProperty("samples").GetInt32() == 100 &&
            reads.GetProperty("smokeCount").GetInt32() == 8 && reads.GetProperty("pageLimit").GetInt32() == 128 &&
            reads.GetProperty("detailLimit").GetInt32() == 64 && reads.GetProperty("maxOutputBytes").GetInt32() == EditSession.MaxReadOutputBytes &&
            reads.GetProperty("pollSleepMilliseconds").GetInt32() == 1 && reads.GetProperty("maxRequestSeconds").GetInt32() == 10 &&
            !c.GetProperty("fullPerformanceAccepted").GetBoolean() && !c.GetProperty("deployed").GetBoolean(), "sampling/budget contract");
#if DEBUG
        if (measured) throw new InvalidOperationException("Release required for domain measurement.");
#endif
        Directory.CreateDirectory(output);
        var rows = new List<object>();
        foreach (int count in measured ? new[] { 100, 1000, 4096 } : new[] { 8 })
            foreach (string workload in Workloads) rows.Add(Measure(output, count, workload, measured));
        string path = Path.Combine(output, "pipe-costs.json");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(stream, new { schema = 3, authoringModel = "ncma.authoring-scene", authoringModelVersion = 1, historicalPerformanceAcceptance = false, workloadContractId = Contract, contractSha256 = Hash(contractPath), measured,
                scope = "development actual-private-pipe probe; no formal package closure or complete E5-D acceptance",
                roundtripScope = reads.GetProperty("scope").GetString(), pumpScope = reads.GetProperty("pumpScope").GetString(),
                allocationScope = reads.GetProperty("allocationScope").GetString(),
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                processId = Environment.ProcessId, rows,
                products = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && a.GetName().Name!.StartsWith("Ncma.", StringComparison.Ordinal))
                    .OrderBy(a => a.GetName().Name).Select(a => new { name = a.GetName().Name, path = a.Location, sha256 = Hash(a.Location) }),
                memoryAcceptance = "pending", targetBudgetAccepted = false, fullPerformanceAccepted = false, deployed = false });
        Console.WriteLine($"E5-D actual private-pipe cost fixtures: {rows.Count} passed; measured={measured}; {path}");
        return 0;
    }

    private static object Measure(string root, int count, string workload, bool measured)
    {
        var doc = new SceneDocument("E5-D pipe fixture"); var ids = new Guid[count];
        // Cold fixture construction is outside every measured window; preserve exact IDs/order/payloads.
        var fixtureObjects = Enumerable.Range(0, count).Select(i => {
            ids[i] = new Guid(count - i, 0, 0, new byte[8]);
            return new SceneObjectData(ids[i], "Object " + i,
                [new ComponentSnapshot("ncma.transform", 1, doc.Components.Encode(TransformData.Identity with { Position = new(i, 2, 3) }))], []);
        }).ToArray();
        doc.RestoreSnapshot(new(1, doc.Name, fixtureObjects));
        byte[] startup = doc.CaptureBytes();
        string startupHash = Convert.ToHexString(SHA256.HashData(startup));
        var sourceObjects = SceneDocumentCodec.Decode(startup).Objects;
        string[] componentOracle = sourceObjects.Select(o => o.Components.Single().Data.GetRawText()).ToArray();
        string firstName = "Object 0";
        var edit = new EditSession(doc); string descriptorPath;
        object result;
        using (var endpoint = new EditorEndpoint(edit, root)) {
            descriptorPath = endpoint.DescriptorPath;
            var descriptor = Wire.Decode<EndpointDescriptor>(File.ReadAllBytes(descriptorPath));
            using var pipe = new NamedPipeClientStream(".", descriptor.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            pipe.Connect(3000);
            void Until(Func<bool> done) {
                long start = Stopwatch.GetTimestamp();
                while (!done()) { if (Stopwatch.GetElapsedTime(start).TotalSeconds > 10) throw new TimeoutException("E5-D pipe fixture timeout"); endpoint.Pump(); Thread.Sleep(1); }
            }
            Wire.WriteAsync(pipe, Wire.Encode(new Hello(1, descriptor.InstanceId, root, "isolated E5-D test client")), CancellationToken.None).GetAwaiter().GetResult();
            var handshake = Wire.ReadAsync(pipe, CancellationToken.None);
            Until(() => endpoint.View.Connections.Length == 1);
            Check(!handshake.IsCompleted, "fixture must not auto-pair");
            // Explicit trusted TEST setup on this newly-created isolated endpoint only.
            endpoint.Pair(endpoint.View.Connections[0].ConnectionId, true); Until(() => handshake.IsCompleted);
            var hello = Wire.Decode<HelloResult>(handshake.Result!); Check(hello.SessionId == edit.SessionId, "active session");
            var auditedRequests = new List<Guid>();
            CapabilityRequest Request(string name, object input) => new(EditSession.ContractVersion, Guid.NewGuid(), edit.SessionId, edit.Revision, name, Json(input));
            (IpcResponse Response, Cost Cost) Send(CapabilityRequest request) {
                ulong generation = endpoint.View.DocumentGeneration;
                long captures = Captures(doc), allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                Wire.WriteAsync(pipe, Wire.Encode(new IpcRequest("invoke", generation, Json(request), CallId: Guid.NewGuid())), CancellationToken.None).GetAwaiter().GetResult();
                var task = Wire.ReadAsync(pipe, CancellationToken.None); int pumps = 0; long pumpTicks = 0, pumpBytes = 0;
                while (!task.IsCompleted) {
                    if (Stopwatch.GetElapsedTime(start).TotalSeconds > 10) throw new TimeoutException("E5-D timed request timeout");
                    long pbytes = GC.GetAllocatedBytesForCurrentThread(), pt = Stopwatch.GetTimestamp(); endpoint.Pump();
                    pumpTicks += Stopwatch.GetTimestamp() - pt; pumpBytes += GC.GetAllocatedBytesForCurrentThread() - pbytes; pumps++;
                    Thread.Sleep(1); // Explicit scheduling contribution; never called simulation CPU.
                }
                byte[] bytes = task.GetAwaiter().GetResult() ?? throw new IOException("Pipe closed");
                var response = Wire.Decode<IpcResponse>(bytes);
                double wall = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                long allocation = GC.GetAllocatedBytesForCurrentThread() - allocated;
                if (response.Code == "ok") auditedRequests.Add(request.RequestId); // Outside the timed/allocated window.
                return (response, new(wall, allocation, pumpTicks * 1000d / Stopwatch.Frequency, pumpBytes, pumps, bytes.Length, Captures(doc) - captures));
            }
            int offset = workload == "page_warm_last" ? Math.Max(0, count - 128) : 0;
            CapabilityRequest Read() => workload == "object_warm"
                ? Request("ncma.scene.object.inspect", new { objectId = ids[^1], section = "components", limit = 64 })
                : Request("ncma.scene.inspect", new { offset, limit = 128 });
            void Validate(IpcResponse response, Cost cost) {
                Check(response.Code == "ok" && response.Result.HasValue && cost.ResponseBytes <= EditSession.MaxReadOutputBytes, "bounded real response");
                var answer = response.Result!.Value;
                Check(answer.GetProperty("status").GetString() == "ok" && !answer.GetProperty("changed").GetBoolean(), "read-only result");
                var data = answer.GetProperty("data");
                if (workload == "object_warm") {
                    var items = data.GetProperty("items"); Check(items.GetArrayLength() == 1, "one component");
                    Check(items[0].GetProperty("data").GetRawText() == componentOracle[^1], "exact full-codec component value");
                } else {
                    var objects = data.GetProperty("scene").GetProperty("objects");
                    Check(data.GetProperty("documentSessionId").GetGuid() == doc.SessionId && data.GetProperty("documentRevision").GetUInt64() == doc.Revision && data.GetProperty("model").GetString() == "ncma.authoring-scene" && data.GetProperty("modelVersion").GetInt32() == 1 && data.GetProperty("totalCount").GetInt32() == count &&
                        objects.GetArrayLength() == Math.Min(128, count - offset), "page identity/count");
                    for (int i = 0; i < objects.GetArrayLength(); i++) {
                        int index = offset + i; var value = objects[i];
                        Check(value.GetProperty("id").GetGuid() == ids[index] &&
                            value.GetProperty("name").GetString() == (index == 0 ? firstName : "Object " + index), "creation order and current names");
                        var components = value.GetProperty("components");
                        // Scene pages intentionally expose component descriptors, not payloads.
                        // Full payload equality belongs to the separate object-detail workload.
                        Check(components.GetArrayLength() == 1 && components[0].GetProperty("typeId").GetString() == sourceObjects[index].Components[0].TypeId &&
                            components[0].GetProperty("version").GetInt32() == sourceObjects[index].Components[0].Version, "exact page component descriptor");
                    }
                }
                Check(double.IsFinite(cost.RoundtripMilliseconds) && cost.RoundtripMilliseconds >= cost.PumpMilliseconds &&
                    cost.PumpMilliseconds >= 0 && cost.OwnerThreadAllocatedBytes >= cost.PumpOwnerThreadAllocatedBytes &&
                    cost.PumpOwnerThreadAllocatedBytes >= 0, "nested nonnegative costs");
            }
            var prime = Send(Read()); Validate(prime.Response, prime.Cost); Check(prime.Cost.FullCaptures == 0, "first authoring read performs no full capture");
            int warmup = measured ? 12 : 1, samples = measured ? 100 : 2; var costs = new Cost[samples];
            for (int i = 0; i < warmup + samples; i++) {
                if (workload == "page_cold_revision") {
                    var renamed = edit.Invoke(Request("ncma.scene.transaction", new { operations = new[] { new { op = "rename", objectId = ids[0], name = "Revision " + i } } }), new(["ncma.scene.transaction"]));
                    Check(renamed.Changed, "trusted out-of-timing cold invalidation");
                    firstName = "Revision " + i;
                }
                var sample = Send(Read()); Validate(sample.Response, sample.Cost);
                Check(sample.Cost.FullCaptures == 0, "cold/warm authoring capture count");
                if (i >= warmup) costs[i - warmup] = sample.Cost;
            }
            Check(endpoint.Grants.Length == 0 && endpoint.Proposals.Length == 0 && edit.State.UndoCount <= EditSession.MaxHistoryEntries, "no remote write grant or unbounded history");
            endpoint.Revoke(hello.ConnectionId); Check(Send(Read()).Response.Code == "connection_revoked", "revocation cannot reuse warm reads");
            var audit = EcsPipeAudit.Verify(descriptorPath, descriptor.InstanceId, edit.SessionId, hello.ConnectionId, auditedRequests);
            result = new { workload, objects = count, warmup, sampleCount = samples, startupHash, raw = costs,
                audit,
                semanticChecksPassed = true, pairingRequired = true, remoteWriteGrants = 0, revokedReadRejected = true };
        }
        Check(!File.Exists(descriptorPath), "owned endpoint descriptor released");
        return result;
    }
}
