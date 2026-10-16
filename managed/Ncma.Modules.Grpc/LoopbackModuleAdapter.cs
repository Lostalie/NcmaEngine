using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using global::Grpc.Core;
using global::Grpc.Net.Client;

namespace Ncma.Modules.Grpc;

/// <summary>
/// Authenticated transport to an explicitly trusted local worker. This owns a connection, NOT a
/// process, World, editor permission, or interpreter. Prepare/stop/close outside runtime ticks.
/// The approved process owner must retain the worker until sessions and this adapter have drained.
/// </summary>
public sealed class LoopbackModuleAdapter : IModuleAdapter
{
    private static readonly Marshaller<byte[]> Marshaller = Marshallers.Create<byte[]>(bytes => bytes, bytes => (byte[])bytes.Clone());
    private static readonly Method<byte[], byte[]> HandshakeMethod = new(MethodType.Unary, WorkerWire.Service, "Handshake", Marshaller, Marshaller);
    private static readonly Method<byte[], byte[]> InvokeMethod = new(MethodType.Unary, WorkerWire.Service, "Invoke", Marshaller, Marshaller);
    private readonly object gate = new();
    private readonly CancellationTokenSource stop = new();
    private readonly GrpcChannel channel;
    private readonly SocketsHttpHandler http;
    private readonly X509Certificate2 trustRoot;
    private readonly Metadata headers;
    private readonly Guid instance;
    private readonly ModuleOperation operation;
    private int active;
    private bool stopping;
    private bool closed;
    private Task? cancellationWork;
    private int connectionLost;
    public bool ConnectionLost => Volatile.Read(ref connectionLost) != 0;

    private LoopbackModuleAdapter(int port, Guid instance, byte[] credential, byte[] rootDer,
        byte[] leafSha256, ModuleOperation operation)
    {
        if (port is < 1 or > 65535 || instance == Guid.Empty || credential.Length != 32 ||
            rootDer.Length is < 1 or > 8192 || leafSha256.Length != 32) throw new ArgumentException("Invalid local worker identity.");
        _ = new ModuleCatalog([operation]);
        var known = TextStatisticsModule.Operation with
        {
            MaximumInputBytes = operation.MaximumInputBytes,
            MaximumOutputBytes = operation.MaximumOutputBytes,
            TimeoutMilliseconds = operation.TimeoutMilliseconds
        };
        if (operation != known) throw new ArgumentException("Unsupported registered worker operation.");
        this.instance = instance;
        this.operation = operation;
        headers = new Metadata { { "authorization", "Bearer " + Convert.ToHexString(credential) } };
        trustRoot = new X509Certificate2(rootDer);
        var pin = (byte[])leafSha256.Clone();
        var chain = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck, // Ephemeral, privately pinned local CA; no external CRL/OCSP IO.
            VerificationFlags = X509VerificationFlags.NoFlag,
            DisableCertificateDownloads = true
        };
        chain.CustomTrustStore.Add(trustRoot);
        chain.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        http = new SocketsHttpHandler
        {
            UseProxy = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(3),
            SslOptions = new SslClientAuthenticationOptions
            {
                CertificateChainPolicy = chain,
                RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                    errors == SslPolicyErrors.None && certificate is not null &&
                    CryptographicOperations.FixedTimeEquals(SHA256.HashData(certificate.GetRawCertData()), pin)
            }
        };
        channel = GrpcChannel.ForAddress(new UriBuilder("https", IPAddress.Loopback.ToString(), port).Uri,
            new GrpcChannelOptions
            {
                HttpHandler = http, DisposeHttpClient = true,
                MaxSendMessageSize = WorkerWire.MaximumBytes, MaxReceiveMessageSize = WorkerWire.MaximumBytes,
                MaxRetryAttempts = 1, MaxRetryBufferSize = 0, MaxRetryBufferPerCallSize = 0,
                DisableResolverServiceConfig = true
            });
    }

    public static async Task<LoopbackModuleAdapter> ConnectAsync(int port, Guid instance, int expectedProcessId,
        ReadOnlyMemory<byte> credential, ReadOnlyMemory<byte> rootCertificateDer,
        ReadOnlyMemory<byte> serverCertificateSha256, ModuleOperation operation, CancellationToken cancellation = default)
    {
        if (expectedProcessId <= 0) throw new ArgumentOutOfRangeException(nameof(expectedProcessId));
        ArgumentNullException.ThrowIfNull(operation);
        if (credential.Length != 32 || rootCertificateDer.Length is < 1 or > 8192 || serverCertificateSha256.Length != 32)
            throw new ArgumentException("Invalid local worker credential budget.");
        var adapter = new LoopbackModuleAdapter(port, instance, credential.ToArray(),
            rootCertificateDer.ToArray(), serverCertificateSha256.ToArray(), operation);
        try
        {
            var nonce = Guid.NewGuid();
            var bytes = WorkerWire.Encode(new { version = 1, instance, nonce, operation = WorkerWire.Describe(operation) });
            var reply = await adapter.Call(HandshakeMethod, bytes, TimeSpan.FromSeconds(5), cancellation).ConfigureAwait(false);
            using var document = WorkerWire.Parse(reply, "version", "instance", "nonce", "processId", "maxWireBytes");
            var root = document.RootElement;
            if (WorkerWire.Integer(root, "version", 1, 1) != 1 || WorkerWire.Identity(root, "instance") != instance ||
                WorkerWire.Identity(root, "nonce") != nonce ||
                WorkerWire.Integer(root, "processId", 1, int.MaxValue) != expectedProcessId ||
                WorkerWire.Integer(root, "maxWireBytes", WorkerWire.MaximumBytes, WorkerWire.MaximumBytes) != WorkerWire.MaximumBytes)
                throw WorkerWire.Invalid();
            return adapter;
        }
        catch
        {
            adapter.Stop();
            await adapter.cancellationWork!.ConfigureAwait(false);
            if (!adapter.TryClose()) throw new InvalidOperationException("Failed startup transport did not drain.");
            throw;
        }
    }

    public async Task<ModuleReply> InvokeAsync(ModuleRequest request, TimeSpan remainingBudget, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Operation != operation || remainingBudget <= TimeSpan.Zero ||
            remainingBudget > TimeSpan.FromMilliseconds(operation.TimeoutMilliseconds)) throw new ArgumentException("Invalid worker dispatch.");
        var started = Stopwatch.GetTimestamp();
        var encoded = WorkerWire.Request(instance, request);
        var remaining = remainingBudget - Stopwatch.GetElapsedTime(started);
        if (remaining <= TimeSpan.Zero) throw new OperationCanceledException();
        byte[] reply;
        try { reply = await Call(InvokeMethod, encoded, remaining, cancellation).ConfigureAwait(false); }
        catch (RpcException error) when (error.StatusCode is StatusCode.Unavailable or StatusCode.Unauthenticated or StatusCode.PermissionDenied or StatusCode.Internal)
        {
            Interlocked.Exchange(ref connectionLost, 1);
            throw;
        }
        return WorkerWire.Reply(instance, reply);
    }

    private async Task<byte[]> Call(Method<byte[], byte[]> method, byte[] bytes, TimeSpan budget, CancellationToken cancellation)
    {
        lock (gate)
        {
            if (stopping || closed) throw new InvalidOperationException("Worker transport is stopping.");
            if (active >= 4) throw new InvalidOperationException("Worker transport call budget.");
            active++;
        }
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation, stop.Token);
            timeout.CancelAfter(budget);
            using var call = channel.CreateCallInvoker().AsyncUnaryCall(method, null,
                new CallOptions(headers, DateTime.UtcNow + budget, timeout.Token), bytes);
            return await call.ResponseAsync.ConfigureAwait(false);
        }
        finally { lock (gate) active--; }
    }

    public void Stop()
    {
        lock (gate)
        {
            if (closed || stopping) return;
            stopping = true;
            cancellationWork = stop.CancelAsync();
        }
    }

    public bool TryClose()
    {
        lock (gate)
        {
            if (closed) return true;
            if (!stopping) throw new InvalidOperationException("Stop the worker transport before closing.");
            if (active != 0 || cancellationWork is not { IsCompleted: true }) return false;
            _ = cancellationWork.Exception;
            channel.Dispose();
            http.Dispose();
            trustRoot.Dispose();
            stop.Dispose();
            closed = true;
            return true;
        }
    }
}
