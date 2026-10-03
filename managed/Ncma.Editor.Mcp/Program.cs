using System.Collections.Concurrent;
using System.Text.Json;
using Ncma.Editor.Protocol;
using Ncma.Editor.Mcp;

// This process owns only protocol DTOs. It never loads a World, SceneDocument, validators or a native DLL.
try
{
    if (args.Length != 2 || args[0] != "--descriptor") throw new ArgumentException("Specify --descriptor <selected-instance.json>.");
    string path = Path.GetFullPath(args[1]); var info = new FileInfo(path);
    if (!info.Exists || info.Length > 4096) throw new ArgumentException("Invalid descriptor.");
    for (FileSystemInfo? item = info; item is not null; item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent)
        if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Reparse descriptor.");
    var descriptor = Wire.Decode<EndpointDescriptor>(File.ReadAllBytes(path));
    if (descriptor.IpcVersion != Wire.Version || descriptor.InstanceId == Guid.Empty ||
        !descriptor.PipeName.StartsWith("ncma-editor-", StringComparison.Ordinal) ||
        !Guid.TryParseExact(descriptor.PipeName["ncma-editor-".Length..], "N", out _)) throw new ArgumentException("Invalid instance.");
    using var input = Console.OpenStandardInput(); using var output = Console.OpenStandardOutput();
    using var stop = new CancellationTokenSource(); using var connectGate = new SemaphoreSlim(1); using var outputGate = new SemaphoreSlim(1);
    IpcClient? client = null; JsonElement[] capabilities = [];
    bool initialized = false, ready = false;
    var pending = new ConcurrentDictionary<string, CancellationTokenSource>(); var jobs = new HashSet<Task>();
    var lines = new BoundedLines(input);
    try
    {
        while (await lines.Next(stop.Token) is byte[] bytes)
        {
            JsonElement request;
            try { using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 }); Wire.RejectDuplicates(doc.RootElement); request = doc.RootElement.Clone(); }
            catch (Exception e) when (e is JsonException or ArgumentException)
            { await Write(new { jsonrpc = "2.0", id = (object?)null, error = new { code = -32700, message = "Parse error" } }); continue; }
            if (request.ValueKind != JsonValueKind.Object) { await Error(JsonSerializer.SerializeToElement<object?>(null), -32600, "Invalid Request"); continue; }
            bool hasId = request.ValueKind == JsonValueKind.Object && request.TryGetProperty("id", out _);
            JsonElement id = hasId ? request.GetProperty("id") : default;
            string method = request.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()! : "";
            var parameters = request.TryGetProperty("params", out var p) ? p : JsonSerializer.SerializeToElement(new { });
            if (!request.TryGetProperty("jsonrpc", out var version) || version.ValueKind != JsonValueKind.String || version.GetString() != "2.0" ||
                method.Length == 0 || hasId && (id.ValueKind is not (JsonValueKind.String or JsonValueKind.Number) || id.GetRawText().Length > 128))
            { await Error(hasId ? id : JsonSerializer.SerializeToElement<object?>(null), -32600, "Invalid Request"); continue; }
            if (method == "initialize")
            {
                if (!hasId || initialized) { if (hasId) await Error(id, -32600, "Duplicate or invalid initialize"); continue; }
                if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty("protocolVersion", out var pv) || pv.ValueKind != JsonValueKind.String || pv.GetString() != "2025-11-25")
                { await Error(id, -32602, "Supported protocol: 2025-11-25"); continue; }
                initialized = true;
                await Write(new { jsonrpc = "2.0", id, result = new { protocolVersion = "2025-11-25",
                    capabilities = new { tools = new { listChanged = false } }, serverInfo = new { name = "NcmaEditor", version = "1.0.0" } } });
                continue;
            }
            if (!hasId)
            {
                if (method == "notifications/initialized" && initialized) ready = true;
                if (method == "notifications/cancelled" && parameters.TryGetProperty("requestId", out var cancelled) &&
                    pending.TryGetValue(cancelled.GetRawText(), out var cancellation)) cancellation.Cancel();
                continue;
            }
            if (!ready) { await Error(id, -32002, "Initialize first"); continue; }
            if (method != "tools/list" && method != "tools/call" && method != "ping") { await Error(id, -32601, "Method not found"); continue; }
            jobs.RemoveWhere(t => t.IsCompleted);
            if (pending.Count >= 16) { await Error(id, -32000, "queue_full"); continue; }
            string key = id.GetRawText(); var requestStop = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            if (!pending.TryAdd(key, requestStop)) { requestStop.Dispose(); await Error(id, -32600, "Duplicate in-flight id"); continue; }
            jobs.Add(Handle(id, method, parameters, key, requestStop));
        }
    }
    finally
    {
        stop.Cancel(); foreach (var c in pending.Values) c.Cancel();
        try { await Task.WhenAll(jobs); } catch (OperationCanceledException) { }
        if (client is not null) await client.DisposeAsync();
    }
    return 0;
    async Task EnsureClient(CancellationToken cancellation)
    {
        await connectGate.WaitAsync(cancellation);
        try
        {
            if (client is null)
            {
                client = await IpcClient.Connect(descriptor, cancellation);
                var list = await client.Invoke(JsonSerializer.SerializeToElement(new { contractVersion = 2, requestId = Guid.NewGuid(),
                    sessionId = client.Hello.SessionId, expectedRevision = (ulong?)null, capability = "ncma.capabilities.list", input = new { } }, Wire.Json), cancellation);
                capabilities = list.GetProperty("data").GetProperty("capabilities").EnumerateArray().Select(c => c.Clone()).ToArray();
            }
            await client.Refresh(cancellation);
        }
        finally { connectGate.Release(); }
    }
    async Task Handle(JsonElement id, string method, JsonElement parameters, string key, CancellationTokenSource cancellation)
    {
        try
        {
            object result;
            if (method == "ping") result = new { };
            else
            {
                await EnsureClient(cancellation.Token);
                if (method == "tools/list")
                {
                    Closed(parameters, []);
                    result = new { tools = capabilities.Select(c => new { name = c.GetProperty("name").GetString(),
                        description = c.GetProperty("description").GetString(), inputSchema = EnvelopeSchema(c.GetProperty("inputSchema"), client!.Hello.SessionId),
                        outputSchema = c.GetProperty("outputSchema"), annotations = new { readOnlyHint = c.GetProperty("risk").GetString() == "read_only",
                            destructiveHint = c.GetProperty("risk").GetString() == "destructive", idempotentHint = true, openWorldHint = false } }).ToArray() };
                }
                else
                {
                    Closed(parameters, ["name", "arguments"]);
                    string name = parameters.GetProperty("name").GetString() ?? "";
                    if (!capabilities.Any(c => c.GetProperty("name").GetString() == name)) throw new RpcFailure(-32602, "Unknown tool");
                    var arguments = parameters.GetProperty("arguments"); Closed(arguments, ["contractVersion", "requestId", "sessionId", "expectedRevision", "input"]);
                    var envelope = JsonSerializer.SerializeToElement(new { contractVersion = arguments.GetProperty("contractVersion"),
                        requestId = arguments.GetProperty("requestId"), sessionId = arguments.GetProperty("sessionId"),
                        expectedRevision = arguments.GetProperty("expectedRevision"), capability = name, input = arguments.GetProperty("input") }, Wire.Json);
                    var value = await client!.Invoke(envelope, cancellation.Token);
                    result = new { content = new[] { new { type = "text", text = value.GetRawText() } }, structuredContent = value, isError = value.GetProperty("status").GetString() != "ok" };
                }
            }
            await Write(new { jsonrpc = "2.0", id, result });
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            int code = e is RpcFailure failure ? failure.Code : e is OperationCanceledException ? -32800 : -32602;
            await Error(id, code, e is RpcFailure ? e.Message : e is OperationCanceledException ? "Request cancelled; committed work is not undone" : "Invalid request or unavailable endpoint");
        }
        finally { pending.TryRemove(key, out _); cancellation.Dispose(); }
    }
    async Task Write(object value)
    {
        byte[] message = Wire.Encode(value);
        await outputGate.WaitAsync();
        try { await output.WriteAsync(message); await output.WriteAsync(new byte[] { 10 }); await output.FlushAsync(); }
        finally { outputGate.Release(); }
    }
    Task Error(JsonElement id, int code, string message) => Write(new { jsonrpc = "2.0", id, error = new { code, message } });
}
catch (Exception e) when (e is not OutOfMemoryException)
{ Console.Error.WriteLine("Ncma MCP stopped: " + e.GetType().Name); return 1; }

static void Closed(JsonElement element, string[] fields)
{
    if (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Any(p => !fields.Contains(p.Name)))
        throw new RpcFailure(-32602, "Unknown business field");
}
static object EnvelopeSchema(JsonElement input, Guid sessionId) => new {
    type = "object", additionalProperties = false, required = new[] { "contractVersion", "requestId", "sessionId", "expectedRevision", "input" },
    properties = new { contractVersion = new { type = "integer", @const = 2 }, requestId = new { type = "string", format = "uuid" },
        sessionId = new { type = "string", format = "uuid", @const = sessionId },
        expectedRevision = new { type = new[] { "integer", "null" }, minimum = 0 }, input }
};
sealed class RpcFailure(int code, string message) : Exception(message) { public int Code { get; } = code; }
sealed class BoundedLines(Stream input)
{
    private readonly byte[] _buffer = new byte[8192]; private int _offset, _length;
    internal async Task<byte[]?> Next(CancellationToken cancellation)
    {
        using var message = new MemoryStream();
        for (;;)
        {
            if (_offset == _length) { _length = await input.ReadAsync(_buffer, cancellation); _offset = 0;
                if (_length == 0) { if (message.Length != 0) throw new IOException("Incomplete stdio frame"); return null; } }
            int newline = Array.IndexOf(_buffer, (byte)10, _offset, _length - _offset);
            int end = newline < 0 ? _length : newline;
            if (message.Length + end - _offset > Wire.MaxMessageBytes) throw new ArgumentException("message_too_large");
            message.Write(_buffer, _offset, end - _offset); _offset = end + (newline >= 0 ? 1 : 0);
            if (newline >= 0) { byte[] bytes = message.ToArray(); _ = Wire.Utf8.GetString(bytes); return bytes; }
        }
    }
}
