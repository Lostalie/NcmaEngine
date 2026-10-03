using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Editor.Protocol;
using Ncma.Editor.Transport;
using Ncma.Scene;

internal static class Concurrency
{
    internal static void Run(string root)
    {
        void Check(bool yes, string reason) { if (!yes) throw new Exception(reason); }
        void Until(Func<bool> complete, Action progress) { var timer = Stopwatch.StartNew(); while (!complete()) {
            if (timer.Elapsed.TotalSeconds > 12) throw new TimeoutException("Concurrency fixture"); progress(); Thread.Sleep(1); } }
        var doc = new SceneDocument("Four clients"); Guid objectId = doc.World.CreateObject("Shared").PersistentId;
        var edit = new EditSession(doc); using var endpoint = new EditorEndpoint(edit, root);
        var descriptor = Wire.Decode<EndpointDescriptor>(File.ReadAllBytes(endpoint.DescriptorPath));
        var clients = new List<NamedPipeClientStream>(); var hellos = new List<HelloResult>();
        var sentAt = new Dictionary<Guid, long>();
        var idlePumps = new List<double>();
        for (int i = 0; i < 40; i++) { var timer = Stopwatch.StartNew(); endpoint.Pump(); if (i >= 8) idlePumps.Add(timer.Elapsed.TotalMilliseconds); }
        idlePumps.Sort(); Console.WriteLine(JsonSerializer.Serialize(new { fixture = "endpoint_idle_no_connection", samples = idlePumps.Count,
            medianMs = idlePumps[16], p95Ms = idlePumps[30], maxMs = idlePumps[^1] }));
        JsonElement Json(object v) => JsonSerializer.SerializeToElement(v, Wire.Json);
        void Send(int i, IpcRequest r) { if (r.CallId is Guid id) sentAt[id] = Stopwatch.GetTimestamp();
            Wire.WriteAsync(clients[i], Wire.Encode(r), CancellationToken.None).GetAwaiter().GetResult(); }
        IpcResponse Receive(int i) { var read = Wire.ReadAsync(clients[i], CancellationToken.None);
            Until(() => read.IsCompleted, () => endpoint.Pump()); return Wire.Decode<IpcResponse>(read.Result!); }
        CapabilityRequest Rename(string value) => new(2, Guid.NewGuid(), edit.SessionId, edit.Revision, "ncma.scene.transaction",
            Json(new { operations = new[] { new { op = "rename", objectId, name = value } } }));
        IpcResponse Invoke(int i, CapabilityRequest r) { Send(i, new("invoke", endpoint.View.DocumentGeneration, Json(r), CallId: Guid.NewGuid())); return Receive(i); }
        try
        {
            for (int i = 0; i < 4; i++)
            {
                var pipe = new NamedPipeClientStream(".", descriptor.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                pipe.Connect(3000); clients.Add(pipe);
                Wire.WriteAsync(pipe, Wire.Encode(new Hello(1, descriptor.InstanceId, descriptor.ProjectRoot, "Client " + i)), CancellationToken.None).GetAwaiter().GetResult();
                var handshake = Wire.ReadAsync(pipe, CancellationToken.None);
                Until(() => endpoint.View.Connections.Any(c => c.ClientName == "Client " + i), () => endpoint.Pump());
                endpoint.Pair(endpoint.View.Connections.Single(c => c.ClientName == "Client " + i).ConnectionId, true);
                Until(() => handshake.IsCompleted, () => endpoint.Pump());
                hellos.Add(Wire.Decode<HelloResult>(handshake.Result!));
            }
            var a = Rename("A"); var b = Rename("B"); Invoke(0, a); Invoke(1, b);
            endpoint.Approve(endpoint.Proposals.Single(p => p.RequestId == a.RequestId).Id, false);
            endpoint.Approve(endpoint.Proposals.Single(p => p.RequestId == b.RequestId).Id, false);
            Send(0, new("invoke", 1, Json(a), CallId: Guid.NewGuid())); Send(1, new("invoke", 1, Json(b), CallId: Guid.NewGuid()));
            Until(() => endpoint.View.QueueCount == 2, () => { });
            var first = Receive(0); var second = Receive(1);
            Check(new[] { first, second }.Count(r => r.Result!.Value.GetProperty("status").GetString() == "ok") == 1 &&
                new[] { first, second }.Count(r => r.Result!.Value.GetProperty("code").GetString() == "revision_conflict") == 1, "Dual-revision writers did not conflict");
            var winner = first.Result!.Value.GetProperty("status").GetString() == "ok" ? a : b;
            int other = winner == a ? 1 : 0;
            Check(Invoke(other, winner).Code == "request_owner_mismatch", "Cross-connection cached request ownership bypass");
            var stale = Rename("Before user"); Invoke(2, stale); endpoint.Approve(endpoint.Proposals.Single(p => p.RequestId == stale.RequestId).Id, false);
            Check(edit.Invoke(Rename("User wins"), new(["ncma.scene.transaction"])).Changed, "User fixture failed");
            Check(Invoke(2, stale).Result!.Value.GetProperty("code").GetString() == "revision_conflict", "User precedence ignored");
            var pumps = new List<double>(); var pumpAllocations = new List<long>();
            var latency = new System.Collections.Concurrent.ConcurrentBag<double>();
            for (int i = 0; i < 4; i++) for (int n = 0; n < 16; n++)
                Send(i, new("invoke", 1, Json(new CapabilityRequest(2, Guid.NewGuid(), edit.SessionId, null, "ncma.scene.inspect", Json(new { }))), CallId: Guid.NewGuid()));
            Until(() => endpoint.View.QueueCount == 64, () => { });
            Send(0, new("invoke", 1, Json(new CapabilityRequest(2, Guid.NewGuid(), edit.SessionId, null, "ncma.scene.inspect", Json(new { }))), CallId: Guid.NewGuid()));
            async Task<List<IpcResponse>> ReadAll(NamedPipeClientStream pipe, int count)
            {
                var results = new List<IpcResponse>();
                for (int i = 0; i < count; i++) { var response = Wire.Decode<IpcResponse>((await Wire.ReadAsync(pipe, CancellationToken.None))!);
                    results.Add(response); if (response.Code == "ok" && response.CallId is Guid id) latency.Add(Stopwatch.GetElapsedTime(sentAt[id]).TotalMilliseconds); }
                return results;
            }
            var all = clients.Select((p, i) => ReadAll(p, i == 0 ? 17 : 16)).ToArray();
            Until(() => all.All(t => t.IsCompleted), () => { long allocated = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew(); endpoint.Pump(); pumps.Add(timer.Elapsed.TotalMilliseconds); pumpAllocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocated); });
            Check(all.SelectMany(t => t.Result).Count(r => r.Code == "queue_full") == 1 && all.SelectMany(t => t.Result).Count(r => r.Code == "ok") == 64 &&
                endpoint.View.QueueCount == 0, "Four-client saturation did not enforce capacity");
            var ordered = pumps.Order().ToArray(); var latencies = latency.Order().ToArray(); var allocations = pumpAllocations.Order().ToArray();
            Console.WriteLine(JsonSerializer.Serialize(new { fixture = "four_clients_64_queued_reads", environment = Environment.OSVersion.ToString(),
                pumps = ordered.Length, medianMs = ordered[ordered.Length / 2], p95Ms = ordered[(int)Math.Floor((ordered.Length - 1) * .95)], maxMs = ordered[^1], medianPumpAllocationBytes = allocations[allocations.Length / 2], maxPumpAllocationBytes = allocations[^1],
                latencySamples = latencies.Length, medianEndToEndMs = latencies[latencies.Length / 2], p95EndToEndMs = latencies[(int)Math.Ceiling(latencies.Length * .95) - 1], maxEndToEndMs = latencies[^1] }));
            var expire = Rename("Expired"); Invoke(3, expire); endpoint.Approve(endpoint.Proposals.Single(p => p.RequestId == expire.RequestId).Id, false);
            ulong beforeExpiry = edit.Revision; Send(3, new("invoke", 1, Json(expire), CallId: Guid.NewGuid()));
            var expiryRead = Wire.ReadAsync(clients[3], CancellationToken.None);
            Until(() => expiryRead.IsCompleted, () => { });
            Check(Wire.Decode<IpcResponse>(expiryRead.Result!).Code == "request_expired" && edit.Revision == beforeExpiry, "Timed-out queued write executed");
            var disconnected = Rename("Disconnected"); Invoke(3, disconnected); endpoint.Approve(endpoint.Proposals.Single(p => p.RequestId == disconnected.RequestId).Id, false);
            Send(3, new("invoke", 1, Json(disconnected), CallId: Guid.NewGuid()));
            Until(() => endpoint.View.QueueCount == 1, () => { }); clients[3].Dispose();
            Until(() => !endpoint.View.Connections.Single(c => c.ConnectionId == hellos[3].ConnectionId).Connected, () => { });
            endpoint.Pump(); Check(edit.Revision == beforeExpiry && endpoint.View.QueueCount == 0, "Disconnected queued write executed");
            ulong generation = endpoint.View.DocumentGeneration;
            Check(edit.NewDocument(edit.Revision, new(["ncma.scene.transaction"])).Changed, "NewDocument fixture failed");
            endpoint.Pump(); Check(endpoint.Grants.Length == 0 && endpoint.View.DocumentGeneration > generation, "Document replacement kept grants/generation");
            Send(0, new("invoke", generation, Json(new CapabilityRequest(2, Guid.NewGuid(), edit.SessionId, null, "ncma.scene.inspect", Json(new { }))), CallId: Guid.NewGuid()));
            Check(Receive(0).Code == "document_generation_mismatch", "Old generation routed to a new document");
            Console.WriteLine("G5 four-client conflict/ownership/fair bounded queue/timeout/disconnect/document replacement passed.");
        }
        finally { foreach (var pipe in clients) pipe.Dispose(); }
    }
}
