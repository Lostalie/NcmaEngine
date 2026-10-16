using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Core;
using Grpc.Net.Client;

internal static partial class Program
{
    // Independent test peer deliberately bypasses the product encoder, never TLS validation.
    private sealed class RawPeer : IDisposable
    {
        private readonly X509Certificate2 root;
        private readonly GrpcChannel channel;
        private readonly Metadata credentials;
        private static readonly Marshaller<byte[]> Bytes = Marshallers.Create<byte[]>(x => x, x => x);

        internal RawPeer(int port, byte[] ca, byte[] leaf, byte[] secret)
        {
            root = new X509Certificate2(ca);
            var policy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust, RevocationMode = X509RevocationMode.NoCheck,
                VerificationFlags = X509VerificationFlags.NoFlag, DisableCertificateDownloads = true
            };
            policy.CustomTrustStore.Add(root);
            policy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
            var handler = new SocketsHttpHandler
            {
                UseProxy = false, AllowAutoRedirect = false,
                SslOptions = new SslClientAuthenticationOptions
                {
                    CertificateChainPolicy = policy,
                    RemoteCertificateValidationCallback = (_, cert, _, errors) => errors == SslPolicyErrors.None &&
                        cert is not null && CryptographicOperations.FixedTimeEquals(SHA256.HashData(cert.GetRawCertData()), leaf)
                }
            };
            channel = GrpcChannel.ForAddress($"https://127.0.0.1:{port}", new GrpcChannelOptions
            {
                HttpHandler = handler, DisposeHttpClient = true, MaxSendMessageSize = 262144,
                MaxReceiveMessageSize = 131072, MaxRetryAttempts = 1, DisableResolverServiceConfig = true
            });
            credentials = new Metadata { { "authorization", "Bearer " + Convert.ToHexString(secret) } };
        }

        internal byte[] Call(string name, byte[] input, Metadata? headers = null, bool deadline = true)
        {
            // Even a deliberately absent RPC deadline has a bounded test cancellation owner.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var method = new Method<byte[], byte[]>(MethodType.Unary, "ncma.modules.v1.Worker", name, Bytes, Bytes);
            using var call = channel.CreateCallInvoker().AsyncUnaryCall(method, null,
                new CallOptions(headers ?? credentials, deadline ? DateTime.UtcNow.AddSeconds(5) : null, timeout.Token), input);
            return call.ResponseAsync.GetAwaiter().GetResult();
        }

        internal AsyncUnaryCall<byte[]> Begin(string name, byte[] input, CancellationToken cancellation, TimeSpan? budget = null) =>
            channel.CreateCallInvoker().AsyncUnaryCall(new Method<byte[], byte[]>(MethodType.Unary,
                "ncma.modules.v1.Worker", name, Bytes, Bytes), null,
                new CallOptions(credentials, DateTime.UtcNow + (budget ?? TimeSpan.FromSeconds(5)), cancellation), input);

        public void Dispose() { channel.Dispose(); root.Dispose(); }
    }

    private static void ProtocolRefusals(string root)
    {
        var before = checks;
        using var worker = new WorkerFixture(root);
        using var peer = worker.OpenRawPeer();
        var operation = JsonNode.Parse("""
            {"moduleId":"cde08200-1001-4001-8001-000000000001","moduleVersion":"1.0",
             "configurationId":"cde08200-1001-4001-8001-000000000004","name":"ncma.tools.text_stats",
             "inputSchema":"cde08200-1001-4001-8001-000000000002","outputSchema":"cde08200-1001-4001-8001-000000000003",
             "maxInputBytes":65536,"maxOutputBytes":65536,"timeoutMilliseconds":10000}
            """)!;
        var handshake = new JsonObject { ["version"] = 1, ["instance"] = worker.Instance.ToString(),
            ["nonce"] = Guid.NewGuid().ToString(), ["operation"] = operation.DeepClone() };
        static byte[] Encode(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());
        static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
        void Reject(string method, byte[] input, string label, StatusCode status = StatusCode.InvalidArgument,
            Metadata? headers = null, bool deadline = true)
        {
            try { peer.Call(method, input, headers, deadline); throw new InvalidOperationException("Accepted " + label); }
            catch (RpcException error)
            {
                Check(error.StatusCode == status, label + ": " + error.Status);
                if (status == StatusCode.InvalidArgument)
                    Check(error.Status.Detail is "invalid module handshake" or "invalid module request" or "module deadline required",
                        "Fixed diagnostic, no supplied payload in error: " + label);
            }
        }
        void Mutate(JsonNode basis, string method, string label, Action<JsonNode> change)
        { var copy = basis.DeepClone(); change(copy); Reject(method, Encode(copy), label); }

        Reject("Handshake", Encode(handshake), "missing authentication", StatusCode.Unauthenticated, new Metadata());
        var duplicateAuth = new Metadata { { "authorization", "Bearer " + Convert.ToHexString(worker.Secret) },
            { "authorization", "Bearer " + Convert.ToHexString(worker.Secret) } };
        Reject("Handshake", Encode(handshake), "duplicate authentication", StatusCode.Unauthenticated, duplicateAuth);
        Reject("Handshake", Encode(handshake), "missing handshake deadline", deadline: false);
        Mutate(handshake, "Handshake", "future handshake version", x => x["version"] = 2);
        Mutate(handshake, "Handshake", "boolean version", x => x["version"] = true);
        Mutate(handshake, "Handshake", "unknown handshake field", x => x["extra"] = 1);
        Mutate(handshake, "Handshake", "wrong instance", x => x["instance"] = Guid.NewGuid().ToString());
        Mutate(handshake, "Handshake", "empty nonce", x => x["nonce"] = Guid.Empty.ToString());
        Mutate(handshake, "Handshake", "unknown operation", x => x["operation"]!["name"] = "eval");

        var session = Guid.NewGuid().ToString();
        var request = new JsonObject
        {
            ["version"] = 1, ["instance"] = worker.Instance.ToString(), ["requestId"] = Guid.NewGuid().ToString(),
            ["sessionId"] = session, ["epoch"] = 1, ["fingerprint"] = new string('A', 64),
            ["operation"] = operation.DeepClone(),
            ["context"] = new JsonObject { ["projectId"] = Guid.NewGuid().ToString(), ["hostSessionId"] = Guid.NewGuid().ToString(),
                ["documentId"] = Guid.NewGuid().ToString(), ["generation"] = 1, ["revision"] = 0,
                ["worldId"] = Guid.Empty.ToString(), ["observationTick"] = 0 },
            ["payload"] = Convert.ToBase64String(Utf8("{\"text\":\"动作 🤖\"}"))
        };
        Reject("Invoke", Encode(request), "invoke before handshake");
        using (var response = JsonDocument.Parse(peer.Call("Handshake", Encode(handshake))))
            Check(response.RootElement.GetProperty("processId").GetInt32() == worker.ProcessId &&
                response.RootElement.GetProperty("nonce").GetString() == handshake["nonce"]!.GetValue<string>(), "Valid handshake after refusals.");
        Reject("Handshake", Encode(handshake), "second handshake");
        Reject("Invoke", Encode(request), "missing invocation deadline", deadline: false);
        Mutate(request, "Invoke", "future invocation version", x => x["version"] = 2);
        Mutate(request, "Invoke", "unknown root member", x => x["extra"] = 1);
        Mutate(request, "Invoke", "missing root member", x => x.AsObject().Remove("epoch"));
        Mutate(request, "Invoke", "wrong request instance", x => x["instance"] = Guid.NewGuid().ToString());
        Mutate(request, "Invoke", "zero request identity", x => x["requestId"] = Guid.Empty.ToString());
        Mutate(request, "Invoke", "noncanonical UUID", x => x["requestId"] = "ABCDEF00-0000-4000-8000-000000000001");
        Mutate(request, "Invoke", "negative epoch", x => x["epoch"] = -1);
        Mutate(request, "Invoke", "boolean epoch", x => x["epoch"] = true);
        Mutate(request, "Invoke", "float epoch", x => x["epoch"] = 1.5);
        Mutate(request, "Invoke", "integer overflow", x => x["epoch"] = ulong.MaxValue);
        Mutate(request, "Invoke", "lowercase fingerprint", x => x["fingerprint"] = new string('a', 64));
        Mutate(request, "Invoke", "oversized fingerprint", x => x["fingerprint"] = new string('A', 65));
        Mutate(request, "Invoke", "wrong input schema", x => x["operation"]!["inputSchema"] = Guid.NewGuid().ToString());
        Mutate(request, "Invoke", "changed operation budget", x => x["operation"]!["maxInputBytes"] = 60000);
        Mutate(request, "Invoke", "unknown context field", x => x["context"]!["extra"] = 1);
        Mutate(request, "Invoke", "missing context field", x => x["context"]!.AsObject().Remove("revision"));
        Mutate(request, "Invoke", "zero generation", x => x["context"]!["generation"] = 0);
        Mutate(request, "Invoke", "negative revision", x => x["context"]!["revision"] = -1);
        Mutate(request, "Invoke", "tick without world", x => x["context"]!["observationTick"] = 1);
        Mutate(request, "Invoke", "malformed base64", x => x["payload"] = "!invalid");
        Mutate(request, "Invoke", "whitespace base64", x => x["payload"] = request["payload"]!.GetValue<string>() + "\n");
        Mutate(request, "Invoke", "empty payload", x => x["payload"] = "");
        foreach (var bad in new[] { "{\"text\":1}", "{\"text\":\"a\",\"extra\":1}", "{\"text\":\"a\",\"text\":\"b\"}",
            "{\"text\":\"a\",\"t\\u0065xt\":\"b\"}", "{\"text\":\"\\ud800\"}", "[]", "{\"text\":NaN}" })
            Mutate(request, "Invoke", "invalid text document", x => x["payload"] = Convert.ToBase64String(Utf8(bad)));
        Mutate(request, "Invoke", "payload invalid UTF8", x => x["payload"] = Convert.ToBase64String(new byte[] { 0xff, 0xfe }));
        Mutate(request, "Invoke", "decoded payload too large", x => x["payload"] = Convert.ToBase64String(new byte[65537]));
        var encoded = request.ToJsonString();
        Reject("Invoke", Utf8(encoded.Insert(1, "\"version\":1,")), "duplicate root field");
        Reject("Invoke", Utf8(encoded.Insert(1, "\"ver\\u0073ion\":1,")), "escaped duplicate root field");
        Reject("Invoke", Utf8(encoded.Replace("\"generation\":1", "\"generation\":1,\"generation\":1", StringComparison.Ordinal)), "duplicate nested field");
        Reject("Invoke", new byte[] { 0xff, 0xfe }, "wire invalid UTF8");
        Reject("Invoke", Utf8("[]"), "wire array root");
        Reject("Invoke", Utf8("{\"a\":[[[[[[[[[]]]]]]]]]}"), "wire excessive depth");
        Reject("Invoke", Utf8(encoded + "{}"), "trailing JSON");
        Reject("Invoke", new byte[131073], "wire receive budget", StatusCode.ResourceExhausted);

        void Accept(JsonNode value, string label)
        {
            using var reply = JsonDocument.Parse(peer.Call("Invoke", Encode(value)));
            var data = reply.RootElement;
            Check(data.GetProperty("requestId").GetString() == value["requestId"]!.GetValue<string>() &&
                data.GetProperty("sessionId").GetString() == session && data.GetProperty("status").GetInt32() == 1, label);
            using var payload = JsonDocument.Parse(Convert.FromBase64String(data.GetProperty("payload").GetString()!));
            Check(payload.RootElement.GetProperty("codePoints").GetInt32() == 4, "Independent Unicode result after malformed peers.");
        }
        Accept(request, "Malformed requests did not consume request ID or poison session.");
        Reject("Invoke", Encode(request), "replayed request");
        var next = request.DeepClone(); next["requestId"] = Guid.NewGuid().ToString();
        Mutate(next, "Invoke", "cross session", x => x["sessionId"] = Guid.NewGuid().ToString());
        next["epoch"] = 2;
        Accept(next, "Same-session next epoch accepted.");
        next["requestId"] = Guid.NewGuid().ToString(); next["epoch"] = 1;
        Reject("Invoke", Encode(next), "stale epoch");
        next["epoch"] = 2;
        Accept(next, "Stale epoch refusal did not consume ID.");
        // Three successful requests above. Refused requests must not exhaust the lifetime ledger.
        for (var i = 3; i < 4096; i++)
        {
            next["requestId"] = Guid.NewGuid().ToString();
            using var reply = JsonDocument.Parse(peer.Call("Invoke", Encode(next)));
            Check(reply.RootElement.GetProperty("requestId").GetString() == next["requestId"]!.GetValue<string>(),
                "Actual worker lifetime admission identity.");
        }
        next["requestId"] = Guid.NewGuid().ToString();
        Reject("Invoke", Encode(next), "worker lifetime budget after exactly 4096 accepted IDs");
        next["epoch"] = 3;
        Reject("Invoke", Encode(next), "new epoch cannot reset lifetime budget");
        worker.Close();
        Check(worker.ExitCode == 0 && worker.ErrorBytes == 0, "Malformed RPCs retain healthy clean worker shutdown.");
        Console.WriteLine($"PASS actual TLS raw-wire negative/after-refusal health: {checks - before} checks.");
    }
}
