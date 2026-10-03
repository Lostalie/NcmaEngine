using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using Ncma.Editor.Protocol;
namespace Ncma.Editor.Mcp;

internal sealed class IpcClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly SemaphoreSlim _write = new(1);
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<IpcResponse>> _calls = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _reader;
    internal HelloResult Hello { get; private set; }
    private IpcClient(NamedPipeClientStream pipe, HelloResult hello)
    { _pipe = pipe; Hello = hello; _reader = Read(); }
    internal static async Task<IpcClient> Connect(EndpointDescriptor descriptor, CancellationToken stop)
    {
        var pipe = new NamedPipeClientStream(".", descriptor.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop); deadline.CancelAfter(TimeSpan.FromSeconds(30));
            await pipe.ConnectAsync(deadline.Token);
            await Wire.WriteAsync(pipe, Wire.Encode(new Hello(Wire.Version, descriptor.InstanceId, descriptor.ProjectRoot, "Ncma MCP stdio")), deadline.Token);
            var hello = Wire.Decode<HelloResult>(await Wire.ReadAsync(pipe, deadline.Token) ?? throw new IOException("endpoint_unavailable"));
            if (hello.Code != "ok" || hello.InstanceId != descriptor.InstanceId) throw new IOException("pairing_denied");
            return new(pipe, hello);
        }
        catch { pipe.Dispose(); throw; }
    }
    private async Task Read()
    {
        try
        {
            while (await Wire.ReadAsync(_pipe, _stop.Token) is byte[] bytes)
            {
                var response = Wire.Decode<IpcResponse>(bytes);
                if (response.CallId is Guid id && _calls.TryRemove(id, out var completion)) completion.TrySetResult(response);
            }
        }
        catch (Exception e) when (e is IOException or ArgumentException or JsonException or OperationCanceledException or ObjectDisposedException) { }
        finally { foreach (var item in _calls.Values) item.TrySetException(new IOException("endpoint_unavailable")); _calls.Clear(); }
    }
    private async Task<IpcResponse> Send(IpcRequest request, CancellationToken cancel)
    {
        Guid id = Guid.NewGuid(); var completion = new TaskCompletionSource<IpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_calls.Count >= 32) throw new IOException("queue_full");
        _calls[id] = completion;
        try
        {
            await _write.WaitAsync(cancel);
            try { await Wire.WriteAsync(_pipe, Wire.Encode(request with { CallId = id }), cancel); } finally { _write.Release(); }
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(10), cancel);
        }
        finally { _calls.TryRemove(id, out _); }
    }
    internal async Task Refresh(CancellationToken cancel)
    {
        var response = await Send(new("session", Hello.DocumentGeneration), cancel);
        if (response.Code != "ok" || response.Result is not JsonElement state) throw new IOException(response.Code);
        Hello = Hello with { DocumentGeneration = state.GetProperty("documentGeneration").GetUInt64() };
    }
    internal async Task<JsonElement> Invoke(JsonElement envelope, CancellationToken cancel)
    {
        Guid requestId = envelope.GetProperty("requestId").GetGuid();
        try
        {
            cancel.ThrowIfCancellationRequested();
            var response = await Send(new("invoke", Hello.DocumentGeneration, envelope), cancel);
            if (response.Code != "ok" || response.Result is null)
            {
                // Transport failures are honest failed tool results; unknown outcome is never retried automatically.
                return JsonSerializer.SerializeToElement(new { contractVersion = 2, requestId,
                    sessionId = Hello.SessionId, revision = envelope.GetProperty("expectedRevision").ValueKind == JsonValueKind.Number ?
                    envelope.GetProperty("expectedRevision").GetUInt64() : 0, status = "error", code = response.Code,
                    changed = false, data = new { }, executionRevision = 0, replayed = false }, Wire.Json);
            }
            return response.Result.Value;
        }
        catch (Exception error) when (error is IOException or TimeoutException)
        { return JsonSerializer.SerializeToElement(new { contractVersion = 2, requestId, sessionId = Hello.SessionId, revision = 0,
            status = "error", code = error.Message == "queue_full" ? "queue_full" : "outcome_unknown", changed = false, data = new { }, executionRevision = 0, replayed = false }, Wire.Json); }
        catch (OperationCanceledException)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { _ = await Send(new("cancel", Hello.DocumentGeneration, RequestId: requestId), deadline.Token); }
            catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException) { }
            throw;
        }
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); _pipe.Dispose();
        try { await _reader; } catch (IOException) { }
        _stop.Dispose(); _write.Dispose();
    }
}
