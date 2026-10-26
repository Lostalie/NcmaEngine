using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Ncma.Modules;

namespace Ncma.AI;

public sealed record AiCallDiagnostic(Guid RequestId, string Code, int? HttpStatus, int RequestBytes,
    int ResponseBytes, double ElapsedMilliseconds, AiTokenUsage? Usage)
{
    // Fallback exception type only: never retain exception/message/stack/inner exception.
    public string? ExceptionType { get; init; }
}

// Trusted host composition only. Approval/authoring permissions remain in the existing domain bridge.
// No automatic request replay, provider fallback, discovery, file access, process launch or key lookup.
public sealed class DeepSeekChatAdapter : IModuleAdapter, IDisposable
{
    private readonly object gate = new();
    private readonly AiConnectionSnapshot connection;
    private readonly ModuleOperation operation;
    private readonly AiSessionCredential? credential;
    private readonly HttpClient client;
    private readonly ArrayPool<byte> responsePool;
    private readonly HashSet<Guid> issued = [];
    private bool active, disposed;
    private AiCallDiagnostic? diagnostic;
    public AiCallDiagnostic? LastDiagnostic { get { lock (gate) return diagnostic; } }

    public DeepSeekChatAdapter(AiConnectionSnapshot connection, ModuleOperation operation, AiSessionCredential? credential = null)
        : this(connection, operation, credential, null) { }

    // Only the test assembly may substitute transport. Public callers cannot override TLS/proxy/redirect policy.
    internal DeepSeekChatAdapter(AiConnectionSnapshot connection, ModuleOperation operation,
        AiSessionCredential? credential, HttpMessageHandler? testHandler, ArrayPool<byte>? testResponsePool = null)
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentNullException.ThrowIfNull(operation);
        _ = new ModuleCatalog([operation]);
        if (!connection.Owner.IsCurrent(connection) || operation.ConfigurationId != connection.Profile.ConfigurationId ||
            operation.InputSchema != AiRequestContent.InputSchema || operation.TimeoutMilliseconds > connection.Profile.TimeoutMilliseconds)
            throw new ArgumentException("ai_adapter_configuration");
        if ((connection.Profile.Credential is null) != (credential is null) ||
            credential is not null && !credential.IsCurrent(connection.Profile)) throw new ArgumentException("ai_adapter_credential_scope");
        this.connection = connection; this.operation = operation; this.credential = credential;
        responsePool = testResponsePool ?? ArrayPool<byte>.Shared;
        client = AiHttpPolicy.Create(testHandler);
    }

    private bool Current => connection.ValidForDispatch && (credential?.IsCurrent(connection.Profile) ?? true);

    public async Task<ModuleReply> InvokeAsync(ModuleRequest request, TimeSpan remainingBudget, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        long started = Stopwatch.GetTimestamp();
        bool ownsActive = false;
        byte[]? buffer = null;
        int requestBytes = 0, responseBytes = 0; int? status = null;
        string code = "adapter_failed"; string? exceptionType = null; AiTokenUsage? usage = null;
        try {
            lock (gate) {
                // Every admission refusal publishes its own diagnostic through finally.
                // Preserve admission precedence; refused calls never own the active slot.
                if (disposed) { code = "adapter_disposed"; return Reply(request, ModuleReplyStatus.Rejected); }
                if (active) { code = "adapter_busy"; return Reply(request, ModuleReplyStatus.Rejected); }
                if (issued.Count >= ModuleSession.MaximumLifetimeRequests) { code = "lifetime_budget"; return Reply(request, ModuleReplyStatus.Rejected); }
                if (!issued.Add(request.RequestId)) { code = "request_duplicate"; return Reply(request, ModuleReplyStatus.Rejected); }
                active = true; ownsActive = true;
            }
            if (remainingBudget <= TimeSpan.Zero || remainingBudget > TimeSpan.FromMilliseconds(operation.TimeoutMilliseconds)) {
                code = "deadline_invalid"; return Reply(request, ModuleReplyStatus.Rejected);
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            TimeSpan preparationBudget = remainingBudget - Stopwatch.GetElapsedTime(started);
            if (preparationBudget <= TimeSpan.Zero) { code = "timed_out"; return Reply(request, ModuleReplyStatus.Unavailable); }
            deadline.CancelAfter(preparationBudget);
            var cancel = deadline.Token;
            cancel.ThrowIfCancellationRequested();
            if (!Current || request.Operation != operation) { code = "configuration_stale"; return Reply(request, ModuleReplyStatus.Rejected); }
            var prepared = AiChatCodec.Prepare(request.Observation, request.Context, operation, connection);
            requestBytes = prepared.Length;
            // Encoding and credential preparation consume the original remaining deadline.
            if (Stopwatch.GetElapsedTime(started) >= remainingBudget) { code = "timed_out"; return Reply(request, ModuleReplyStatus.Unavailable); }
            using var message = new HttpRequestMessage(HttpMethod.Post, connection.Profile.Endpoint) {
                Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact,
                Content = new ByteArrayContent(prepared.CopyBytes())
            };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            message.Headers.Accept.Add(new("application/json"));
            // Client-local pooling; no application retry or exactly-once transport guarantee.
            message.Headers.ConnectionClose = false;
            if (credential is not null) message.Headers.Authorization = new("Bearer", credential.Acquire(connection.Profile));
            cancel.ThrowIfCancellationRequested();
            if (!Current) { code = "configuration_stale"; return Reply(request, ModuleReplyStatus.Rejected); }
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            status = (int)response.StatusCode;
            if (status != 200) {
                code = status switch { >= 300 and < 400 => "redirect_denied", 401 or 403 => "authentication_failed",
                    429 => "rate_limited", >= 500 => "service_unavailable", _ => "http_rejected" };
                return Reply(request, ModuleReplyStatus.Unavailable); // Never read/log untrusted error bodies.
            }
            if (response.Content.Headers.ContentEncoding.Count != 0 ||
                response.Content.Headers.ContentType?.MediaType != "application/json" ||
                response.Content.Headers.ContentType?.CharSet is string charset &&
                    !charset.Trim('"').Equals("utf-8", StringComparison.OrdinalIgnoreCase))
                throw new AiProtocolException("response_encoding_denied");
            if (response.Content.Headers.ContentLength > AiChatCodec.MaximumTransportBytes)
                throw new AiProtocolException("response_byte_budget");
            await using var body = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            const int responseCapacity = AiChatCodec.MaximumTransportBytes + 1;
            buffer = responsePool.Rent(responseCapacity);
            while (true) {
                cancel.ThrowIfCancellationRequested();
                // Rent can return a larger bucket; never enlarge the protocol's +1 probe budget.
                int read = await body.ReadAsync(buffer.AsMemory(responseBytes, Math.Min(8192, responseCapacity - responseBytes)), cancel).ConfigureAwait(false);
                if (read == 0) break;
                responseBytes += read;
                if (responseBytes > AiChatCodec.MaximumTransportBytes) throw new AiProtocolException("response_byte_budget");
            }
            cancel.ThrowIfCancellationRequested();
            if (!Current) { code = "configuration_stale"; return Reply(request, ModuleReplyStatus.Rejected); }
            // Decode synchronously while the rented memory is owned; returned payload owns its bytes.
            var decoded = AiChatCodec.Decode(buffer.AsMemory(0, responseBytes)); usage = decoded.Usage;
            if (usage?.CompletionTokens > connection.Profile.MaximumOutputTokens) throw new AiProtocolException("completion_token_budget");
            cancel.ThrowIfCancellationRequested();
            if (!Current || Stopwatch.GetElapsedTime(started) >= remainingBudget) {
                code = !Current ? "configuration_stale" : "timed_out"; return Reply(request, ModuleReplyStatus.Unavailable);
            }
            code = "proposal_requires_domain_validation";
            return Reply(request, ModuleReplyStatus.Proposal, decoded.Proposal);
        }
        catch (OperationCanceledException) { code = cancellation.IsCancellationRequested ? "cancelled" : "timed_out"; return Reply(request, ModuleReplyStatus.Unavailable); }
        catch (AiProtocolException e) { code = e.Code; return Reply(request, ModuleReplyStatus.Rejected); }
        catch (HttpRequestException) { code = "transport_failed"; return Reply(request, ModuleReplyStatus.Unavailable); }
        catch (Exception e) when (e is not OutOfMemoryException) {
            // Json/UTF8/IO/shape errors never escape with server body, token, prompt or endpoint attached.
            code = "invalid_response_or_request"; exceptionType = e.GetType().Name;
            return Reply(request, ModuleReplyStatus.Rejected);
        }
        finally {
            // Includes decode/read failures, cancellation and stale-source exits. No response data in the pool.
            if (buffer is not null) responsePool.Return(buffer, clearArray: true);
            lock (gate) {
                diagnostic = new(request.RequestId, code, status, requestBytes, responseBytes,
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds, usage) { ExceptionType = exceptionType };
                // A busy/disposed/duplicate/budget rejection must not release another call.
                if (ownsActive) active = false; // Only after actual IO/decoding/disposal exits, not merely cancellation requested.
            }
        }
    }
    private static ModuleReply Reply(ModuleRequest r, ModuleReplyStatus status, ModulePayload? proposal = null) =>
        new(ModuleRequest.ContractVersion, r.RequestId, r.SessionId, r.Epoch, r.Fingerprint, r.Operation.OutputSchema, status, proposal);
    public void Dispose()
    {
        lock (gate) {
            if (active) throw new InvalidOperationException("ai_adapter_drain_required");
            if (disposed) return;
            disposed = true; client.Dispose();
        }
        // Caller owns the independently revocable credential and must dispose it explicitly.
    }
}
