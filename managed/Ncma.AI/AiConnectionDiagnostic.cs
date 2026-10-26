using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace Ncma.AI;

// Full fixed request preview, no project context or credential value. Not approval by itself.
public sealed class AiConnectionDiagnosticReview
{
    public Guid RequestId { get; } = Guid.NewGuid();
    public string Endpoint { get; }
    public string Model { get; }
    public Guid ConfigurationId { get; }
    public int MaximumOutputTokens { get; }
    public int TimeoutMilliseconds { get; }
    public string Fingerprint { get; }
    public AiPreparedChatRequest Body { get; }
    internal AiConnectionDiagnosticReview(AiConnectionSnapshot connection)
    {
        var p = connection.Profile; Endpoint = p.Endpoint; Model = p.Model; ConfigurationId = p.ConfigurationId;
        MaximumOutputTokens = Math.Min(64, p.MaximumOutputTokens); TimeoutMilliseconds = Math.Min(10_000, p.TimeoutMilliseconds);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) {
            writer.WriteStartObject(); writer.WriteString("model", Model); writer.WritePropertyName("messages"); writer.WriteStartArray();
            writer.WriteStartObject(); writer.WriteString("role", "system"); writer.WriteString("content", "Return exactly this JSON object: {\"ncmaDiagnostic\":\"ok\"}. Do not return commands, tools, code or additional fields."); writer.WriteEndObject();
            writer.WriteStartObject(); writer.WriteString("role", "user"); writer.WriteString("content", "Explicit one-shot protocol connectivity diagnostic. No project data is supplied."); writer.WriteEndObject();
            writer.WriteEndArray(); writer.WriteBoolean("stream", false); writer.WriteNumber("max_tokens", MaximumOutputTokens);
            writer.WritePropertyName("response_format"); writer.WriteStartObject(); writer.WriteString("type", "json_object"); writer.WriteEndObject();
            if (p.ProviderId == "deepseek") { writer.WritePropertyName("thinking"); writer.WriteStartObject(); writer.WriteString("type", "disabled"); writer.WriteEndObject(); }
            writer.WriteEndObject();
        }
        Body = new(stream.ToArray());
        if (Body.Length > 4096) throw new ArgumentException("diagnostic_request_byte_budget");
        Fingerprint = AiContract.Hash(JsonSerializer.SerializeToUtf8Bytes(new { version = 1, RequestId, ConfigurationId,
            connection.Epoch, profile = p.Fingerprint, Endpoint, Model, MaximumOutputTokens, TimeoutMilliseconds,
            maximumCalls = 1, automaticRetries = 0, maximumResponseBytes = AiConnectionDiagnosticSession.MaximumResponseBytes, Body.Sha256 }));
    }
}

// Independent diagnostic lane: never registered as an authoring module, no proposal/Commit API.
// One reviewed application attempt per session, no application retries or hidden startup probes.
public sealed class AiConnectionDiagnosticSession
{
    public const int MaximumResponseBytes = 16 * 1024;
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly AiConnectionSnapshot connection;
    private readonly AiSessionCredential? credential;
    private readonly HttpClient client;
    private readonly TimeProvider time;
    private readonly long preparedAt;
    private CancellationTokenSource? cancellation;
    private Task<AiCallDiagnostic>? running;
    private bool consumed, cancelled, closed, dispatching;
    public AiConnectionDiagnosticReview Review { get; }
    public AiConnectionDiagnosticSession(AiConnectionSnapshot connection, AiSessionCredential? credential = null)
        : this(connection, credential, null) { }
    internal AiConnectionDiagnosticSession(AiConnectionSnapshot connection, AiSessionCredential? credential,
        HttpMessageHandler? testHandler, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!connection.Owner.IsCurrent(connection) || (connection.Profile.Credential is null) != (credential is null) ||
            credential is not null && !credential.IsCurrent(connection.Profile)) throw new ArgumentException("diagnostic_configuration_or_credential");
        this.connection = connection; this.credential = credential; this.time = time ?? TimeProvider.System;
        Review = new(connection); preparedAt = this.time.GetTimestamp(); client = AiHttpPolicy.Create(testHandler);
    }
    private void Verify() { if (owner != Environment.CurrentManagedThreadId || dispatching) throw new InvalidOperationException("diagnostic_owner_or_reentry"); }
    private bool Current => connection.ValidForDispatch && (credential?.IsCurrent(connection.Profile) ?? true);
    public string Status { get { Verify(); return closed ? "closed" : running is { IsCompleted: false } ? cancelled ? "diagnostic-draining" : "diagnostic-pending" :
        cancelled ? "diagnostic-cancelled" : running is null ? "diagnostic-review" : LastDiagnostic?.Code == "diagnostic_ok" ? "diagnostic-complete" : "diagnostic-failed"; } }
    public AiCallDiagnostic? LastDiagnostic { get { Verify(); return running is { IsCompletedSuccessfully: true } ? running.Result : null; } }
    public void Begin(AiConnectionDiagnosticReview review, string fingerprint, bool humanReviewed)
    {
        Verify();
        if (closed || consumed || cancelled || !Current || !ReferenceEquals(review, Review) || fingerprint != Review.Fingerprint || !humanReviewed ||
            time.GetElapsedTime(preparedAt) is var age && (age < TimeSpan.Zero || age >= TimeSpan.FromSeconds(60)))
            throw new InvalidOperationException("diagnostic_approval_stale_or_required");
        consumed = true; cancellation = new(); dispatching = true;
        try { running = RunAsync(cancellation.Token); }
        finally { dispatching = false; }
    }
    private async Task<AiCallDiagnostic> RunAsync(CancellationToken cancelRequest)
    {
        long started = Stopwatch.GetTimestamp(); int responseBytes = 0; int? status = null; string code = "diagnostic_failed";
        string? exceptionType = null; AiTokenUsage? usage = null;
        try {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancelRequest);
            deadline.CancelAfter(Review.TimeoutMilliseconds); var cancel = deadline.Token;
            if (!Current) throw new AiProtocolException("configuration_stale");
            using var message = new HttpRequestMessage(HttpMethod.Post, Review.Endpoint) {
                Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact,
                Content = new ByteArrayContent(Review.Body.CopyBytes())
            };
            message.Content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
            message.Headers.Accept.Add(new("application/json")); message.Headers.ConnectionClose = false;
            if (credential is not null) message.Headers.Authorization = new("Bearer", credential.Acquire(connection.Profile));
            cancel.ThrowIfCancellationRequested(); if (!Current) throw new AiProtocolException("configuration_stale");
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            status = (int)response.StatusCode;
            if (status != 200) throw new AiProtocolException(status switch {
                >= 300 and < 400 => "redirect_denied", 401 or 403 => "authentication_failed", 429 => "rate_limited",
                >= 500 => "service_unavailable", _ => "http_rejected" }); // Error body never read.
            if (response.Content.Headers.ContentEncoding.Count != 0 || response.Content.Headers.ContentType?.MediaType != "application/json" ||
                response.Content.Headers.ContentType?.CharSet is string charset && !charset.Trim('"').Equals("utf-8", StringComparison.OrdinalIgnoreCase))
                throw new AiProtocolException("response_encoding_denied");
            if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw new AiProtocolException("response_byte_budget");
            await using var stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            byte[] bytes = new byte[MaximumResponseBytes + 1];
            while (true) {
                cancel.ThrowIfCancellationRequested();
                int read = await stream.ReadAsync(bytes.AsMemory(responseBytes, Math.Min(4096, bytes.Length - responseBytes)), cancel).ConfigureAwait(false);
                if (read == 0) break; responseBytes += read;
                if (responseBytes > MaximumResponseBytes) throw new AiProtocolException("response_byte_budget");
            }
            var decoded = AiChatCodec.DecodeCompletion(bytes.AsSpan(0, responseBytes).ToArray()); usage = decoded.Usage;
            if (usage?.CompletionTokens > Review.MaximumOutputTokens) throw new AiProtocolException("completion_token_budget");
            using var body = AiJson.Parse(decoded.Content.CopyBytes()); AiJson.Members(body.RootElement, ["ncmaDiagnostic"]);
            if (body.RootElement.GetProperty("ncmaDiagnostic").GetString() != "ok") throw new AiProtocolException("diagnostic_reply_mismatch");
            cancel.ThrowIfCancellationRequested();
            if (!Current) throw new AiProtocolException("configuration_stale");
            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= Review.TimeoutMilliseconds) throw new AiProtocolException("timed_out");
            code = "diagnostic_ok"; // One protocol response, NOT persistent connectivity or domain acceptance.
        }
        catch (OperationCanceledException) { code = cancelRequest.IsCancellationRequested ? "cancelled" : "timed_out"; }
        catch (AiProtocolException e) { code = e.Code; }
        catch (HttpRequestException) { code = "transport_failed"; }
        catch (Exception e) when (e is not OutOfMemoryException) { code = "invalid_response_or_request"; exceptionType = e.GetType().Name; }
        return new(Review.RequestId, code, status, Review.Body.Length, responseBytes, Stopwatch.GetElapsedTime(started).TotalMilliseconds, usage)
            { ExceptionType = exceptionType };
    }
    public void Cancel()
    {
        Verify(); if (closed) return; cancelled = true; dispatching = true;
        try { cancellation?.Cancel(); }
        finally { dispatching = false; }
    }
    public bool TryClose()
    {
        Verify(); if (closed) return true; Cancel();
        if (running is { IsCompleted: false }) return false; // Ignored cancellation retains all transport ownership.
        client.Dispose(); cancellation?.Dispose(); closed = true; return true;
    }
}
