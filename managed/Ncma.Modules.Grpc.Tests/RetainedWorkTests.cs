using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Core;

internal static partial class Program
{
    private static void RetainedWorkerWork(string root)
    {
        var before = checks;
        using var worker = new WorkerFixture(root, WorkerProbe.RetainedWork);
        var adapter = worker.Connect(); // Production handshake, exact worker PID, TLS/pin/credential.
        using var peer = worker.OpenRawPeer();
        var session = Guid.NewGuid().ToString();
        byte[] Request(string text) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1, instance = worker.Instance, requestId = Guid.NewGuid(), sessionId = session,
            epoch = 1, fingerprint = new string('A', 64),
            operation = new
            {
                moduleId = "cde08200-1001-4001-8001-000000000001", moduleVersion = "1.0",
                configurationId = "cde08200-1001-4001-8001-000000000004", name = "ncma.tools.text_stats",
                inputSchema = "cde08200-1001-4001-8001-000000000002", outputSchema = "cde08200-1001-4001-8001-000000000003",
                maxInputBytes = 65536, maxOutputBytes = 65536, timeoutMilliseconds = 10000
            },
            context = new { projectId = Guid.NewGuid(), hostSessionId = Guid.NewGuid(), documentId = Guid.NewGuid(),
                generation = 1, revision = 0, worldId = Guid.Empty, observationTick = 0 },
            payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { text }))
        });
        JsonNode Probe(string operation = "status") => JsonNode.Parse(peer.Call("TestProbe", Encoding.ASCII.GetBytes(operation)))!;
        try
        {
            for (var i = 1; i <= 4; i++)
            {
                using var cancellation = new CancellationTokenSource();
                using var call = peer.Begin("Invoke", Request("test-hold"), cancellation.Token,
                    i == 4 ? TimeSpan.FromSeconds(1) : null);
                Wait(() => Probe()["started"]!.GetValue<int>() == i);
                if (i != 4) cancellation.Cancel(); // Fourth call expires through a real gRPC deadline.
                try { call.ResponseAsync.GetAwaiter().GetResult(); throw new InvalidOperationException("Cancelled RPC completed."); }
                catch (RpcException error)
                {
                    Check(error.StatusCode == (i == 4 ? StatusCode.DeadlineExceeded : StatusCode.Cancelled),
                        "Actual gRPC client cancellation or deadline expiry.");
                }
                Wait(() => Probe()["cancelled"]!.GetValue<int>() == i);
                var state = Probe();
                Check(state["retained"]!.GetValue<int>() == i && state["finished"]!.GetValue<int>() == 0,
                    "Client completed but actual Python work still retains its slot.");
            }
            for (var i = 0; i < 32; i++)
            {
                try { peer.Call("Invoke", Request("must-not-start")); throw new InvalidOperationException("Retained-work overflow admitted."); }
                catch (RpcException error)
                {
                    Check(error.StatusCode == StatusCode.ResourceExhausted && error.Status.Detail == "module work budget",
                        "Production Worker rejects new work, not merely the gRPC active-RPC limiter.");
                }
            }
            var full = Probe();
            Check(full["started"]!.GetValue<int>() == 4 && full["retained"]!.GetValue<int>() == 4,
                "32 replenishment attempts cannot grow actual background work.");
            Probe("release");
            Wait(() => Probe()["retained"]!.GetValue<int>() == 0);
            Check(Probe()["finished"]!.GetValue<int>() == 4, "All actual cancellation-resistant work terminated.");
            using var healthy = JsonDocument.Parse(peer.Call("Invoke", Request("healthy")));
            Check(healthy.RootElement.GetProperty("status").GetInt32() == 1, "Capacity released only after actual work finished.");
        }
        finally
        {
            // Release our exact synthetic waiters even on assertion failure, before owner EOF.
            Probe("release");
            adapter.Stop(); Wait(adapter.TryClose);
        }
        worker.Close();
        Check(worker.ExitCode == 0 && worker.ErrorBytes == 0, "Retained work drained before clean worker exit.");
        Console.WriteLine($"PASS real RPC cancellation with test-only resistant calculation under production admission: {checks - before} checks.");
    }
}
