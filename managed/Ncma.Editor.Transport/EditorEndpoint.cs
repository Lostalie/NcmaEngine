using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Ncma.Editor.Core;
using Ncma.Editor.Protocol;
namespace Ncma.Editor.Transport;

public sealed record ConnectionView(Guid ConnectionId, string ClientName, bool Paired, bool Connected, int PendingCount);
public sealed record EndpointView(Guid InstanceId, ulong DocumentGeneration, string DescriptorPath, ConnectionView[] Connections, int QueueCount);
public sealed partial class EditorEndpoint : IDisposable
{
    public const int MaxConnections = 4, QueueCapacity = 64, PerConnectionCapacity = 16, RequestsPerPump = 4;
    private sealed class Connection(Guid id, string name)
    {
        internal readonly Guid Id = id;
        internal readonly string Name = name;
        internal readonly string Secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        internal readonly TaskCompletionSource<bool> Pairing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Paired, Connected = true;
        internal readonly Queue<Pending> Queue = [];
        internal readonly Dictionary<Guid, Pending> Waiting = [];
    }
    private sealed class Pending(Connection connection, IpcRequest request)
    {
        internal readonly Connection Connection = connection;
        internal readonly IpcRequest Request = request;
        internal readonly string Fingerprint = Convert.ToHexString(SHA256.HashData(Wire.Encode(request with { CallId = null })));
        internal readonly Guid? Key = RequestKey(request) ?? Guid.NewGuid();
        internal readonly long Deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
        internal readonly TaskCompletionSource<IpcResponse> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly object _gate = new();
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly EditSession _edit;
    private readonly Guid _sessionId;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Connection> _connections = [];
    private readonly HashSet<NamedPipeServerStream> _pipes = [];
    private readonly Task _accept;
    private readonly EndpointDescriptor _descriptor;
    private int _count, _cursor;
    private ulong _generation;
    private Guid _catalogGeneration;
    private bool _disposed;
    public string DescriptorPath { get; }
    public EditorEndpoint(EditSession edit, string projectRoot)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Local editor IPC is Windows-only.");
        _edit = edit; _ = edit.State; _sessionId = edit.SessionId; _generation = edit.DocumentGeneration; _catalogGeneration = edit.BehaviourCatalogGeneration;
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        if (!Directory.Exists(root)) throw new ArgumentException("Project root not found.");
        _descriptor = new(Wire.Version, Guid.NewGuid(), root, "ncma-editor-" + Guid.NewGuid().ToString("N"));
        string directory = Path.Combine(root, "out", "sessions");
        for (var item = new DirectoryInfo(directory); item is not null && item.FullName.Length >= root.Length; item = item.Parent)
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Endpoint discovery cannot traverse a reparse point.");
        Directory.CreateDirectory(directory);
        DescriptorPath = Path.Combine(directory, _descriptor.InstanceId.ToString("N") + ".json");
        var first = WindowsPipe.Create(_descriptor.PipeName, true);
        try
        {
            using var output = new FileStream(DescriptorPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            output.Write(Wire.Encode(_descriptor));
        }
        catch { first.Dispose(); throw; }
        lock (_gate) _pipes.Add(first);
        _accept = Task.Run(() => Accept(first));
    }
    public EndpointView View
    {
        get { Verify(); lock (_gate) return new(_descriptor.InstanceId, _generation, DescriptorPath,
            _connections.Select(c => new ConnectionView(c.Id, c.Name, c.Paired, c.Connected, c.Queue.Count)).ToArray(), _count); }
    }
    public void Pair(Guid id, bool approve)
    {
        Verify(); lock (_gate)
        {
            var connection = _connections.Single(c => c.Id == id);
            if (connection.Paired) throw new InvalidOperationException("Already paired.");
            connection.Paired = approve; connection.Pairing.TrySetResult(approve);
        }
    }
    public void Revoke(Guid id)
    {
        Verify(); lock (_gate)
        {
            var connection = _connections.Single(c => c.Id == id);
            connection.Paired = false; connection.Pairing.TrySetResult(false);
            _grants.Remove(id); _proposals.RemoveAll(p => p.Connection.Id == id);
            CancelQueue(connection, "connection_revoked");
        }
    }
    public int Pump()
    {
        Verify(); Synchronize();
        var clock = Stopwatch.StartNew(); int executed = 0;
        while (executed < RequestsPerPump && clock.Elapsed.TotalMilliseconds < 2)
        {
            Pending? pending = null;
            lock (_gate)
            {
                for (int attempt = 0; attempt < _connections.Count; attempt++)
                {
                    var c = _connections[_cursor % _connections.Count];
                    _cursor = (_cursor + 1) % _connections.Count;
                    if (c.Queue.Count == 0) continue;
                    pending = c.Queue.Dequeue(); _count--; if (pending.Key is Guid key) c.Waiting.Remove(key); break;
                }
            }
            if (pending is null) break;
            IpcResponse result;
            lock (_gate)
            {
                if (!pending.Connection.Paired || !pending.Connection.Connected) result = new("connection_revoked");
                else if (pending.Request.DocumentGeneration != _generation) result = new("document_generation_mismatch");
                else if (Stopwatch.GetTimestamp() >= pending.Deadline) result = new("request_expired");
                else result = Execute(pending.Connection, pending.Request);
            }
            pending.Result.TrySetResult(result); executed++;
        }
        return executed;
    }
    private IpcResponse Execute(Connection connection, IpcRequest request)
    {
        // Owner thread only. IO tasks never access the document, validators or editor state.
        if (request.Method == "invoke" && request.Request is JsonElement envelope)
        {
            return Invoke(connection, envelope);
        }
        if (request.Method == "session") return new("ok", JsonSerializer.SerializeToElement(new { sessionId = _sessionId, documentGeneration = _generation }, Wire.Json));
        return new("unknown_method");
    }
    private async Task Accept(NamedPipeServerStream first)
    {
        var listener = first;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await listener.WaitForConnectionAsync(_stop.Token);
                _ = Serve(listener);
                for (;;)
                {
                    _stop.Token.ThrowIfCancellationRequested();
                    lock (_gate) if (_pipes.Count < MaxConnections) break;
                    await Task.Delay(25, _stop.Token);
                }
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                listener = WindowsPipe.Create(_descriptor.PipeName, false);
                lock (_gate) _pipes.Add(listener);
            }
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or OperationCanceledException or ObjectDisposedException) { }
    }
    private async Task Serve(NamedPipeServerStream pipe)
    {
        Connection? connection = null;
        try
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); idle.CancelAfter(TimeSpan.FromSeconds(30));
            byte[]? bytes = await Wire.ReadAsync(pipe, idle.Token);
            if (bytes is null) return;
            var hello = Wire.Decode<Hello>(bytes);
            if (hello.IpcVersion != Wire.Version || hello.InstanceId != _descriptor.InstanceId ||
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(hello.ProjectRoot)) != _descriptor.ProjectRoot ||
                string.IsNullOrWhiteSpace(hello.ClientName) || hello.ClientName.Length > 80)
            { await Wire.WriteAsync(pipe, Wire.Encode(new IpcResponse("endpoint_mismatch")), idle.Token); return; }
            lock (_gate)
            {
                if (_connections.Count >= MaxConnections)
                    _connections.RemoveAll(c => !c.Connected && c.Queue.Count == 0);
                if (hello.ConnectionId is Guid id && hello.Credential is string secret)
                {
                    connection = _connections.SingleOrDefault(c => c.Id == id && !c.Connected && c.Paired &&
                        SecretEquals(c.Secret, secret)) ?? throw new ArgumentException("connection_revoked");
                    connection.Connected = true;
                }
                else
                {
                    if (_connections.Count >= MaxConnections) throw new ArgumentException("connection_capacity");
                    connection = new(Guid.NewGuid(), hello.ClientName); _connections.Add(connection);
                }
            }
            bool approved = connection.Paired || await connection.Pairing.Task.WaitAsync(idle.Token);
            if (!approved) { await Wire.WriteAsync(pipe, Wire.Encode(new IpcResponse("pairing_denied")), idle.Token); return; }
            ulong generation; lock (_gate) generation = _generation;
            await Wire.WriteAsync(pipe, Wire.Encode(new HelloResult("ok", _descriptor.InstanceId, connection.Id,
                connection.Secret, _sessionId, generation)), idle.Token);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            var replies = Channel.CreateBounded<IpcResponse>(new BoundedChannelOptions(PerConnectionCapacity) { SingleReader = true });
            var deliveries = new HashSet<Task>();
            Task writer = Task.Run(async () => {
                try { await foreach (var response in replies.Reader.ReadAllAsync(lifetime.Token))
                    await Wire.WriteAsync(pipe, Wire.Encode(response), lifetime.Token); }
                catch { lifetime.Cancel(); throw; }
            });
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    idle.CancelAfter(TimeSpan.FromMinutes(5));
                    using var readStop = CancellationTokenSource.CreateLinkedTokenSource(idle.Token, lifetime.Token);
                    bytes = await Wire.ReadAsync(pipe, readStop.Token);
                    if (bytes is null) break;
                    var request = Wire.Decode<IpcRequest>(bytes);
                    if (request.Method == "close") break;
                    if (request.Method == "cancel")
                    {
                        bool cancelled; lock (_gate) cancelled = request.RequestId is Guid key && CancelPending(connection, key, "not_executed");
                        await replies.Writer.WriteAsync(new(cancelled ? "not_executed" : "outcome_unknown", CallId: request.CallId), lifetime.Token);
                        continue;
                    }
                    deliveries.RemoveWhere(t => t.IsCompleted);
                    var proposed = new Pending(connection, request); Pending pending = proposed; IpcResponse? denied = null;
                    lock (_gate)
                    {
                        if (deliveries.Count >= PerConnectionCapacity) denied = new("queue_full");
                        else if (!connection.Paired) denied = new("connection_revoked");
                        else if (proposed.Key is Guid key && connection.Waiting.TryGetValue(key, out var existing))
                        { if (existing.Fingerprint == proposed.Fingerprint) pending = existing; else denied = new("request_id_reused"); }
                        else if (_count >= QueueCapacity || connection.Queue.Count >= PerConnectionCapacity) denied = new("queue_full");
                        else { connection.Queue.Enqueue(pending); _count++; if (pending.Key is Guid id) connection.Waiting[id] = pending; }
                    }
                    if (deliveries.Count >= PerConnectionCapacity)
                    { await replies.Writer.WriteAsync(new("queue_full", CallId: request.CallId), lifetime.Token); continue; }
                    deliveries.Add(Deliver(pending, denied, request.CallId));
                }
            }
            finally
            {
                lifetime.Cancel();
                lock (_gate) { connection.Connected = false; CancelQueue(connection, "not_executed"); }
                replies.Writer.TryComplete();
                try { await Task.WhenAll(deliveries.Append(writer)); } catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
            }
            async Task Deliver(Pending pending, IpcResponse? denied, Guid? callId)
            {
                if (denied is null)
                {
                    try { await pending.Result.Task.WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token); }
                    catch (TimeoutException) { lock (_gate) if (pending.Key is Guid key) CancelPending(connection, key, "request_expired"); }
                }
                var response = denied ?? await pending.Result.Task.WaitAsync(lifetime.Token);
                await replies.Writer.WriteAsync(response with { CallId = callId }, lifetime.Token);
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or JsonException or ArgumentException or InvalidOperationException) { }
        finally
        {
            lock (_gate)
            {
                if (connection is not null) { connection.Connected = false; CancelQueue(connection, "not_executed"); }
                _pipes.Remove(pipe);
            }
            pipe.Dispose();
        }
    }
    private static Guid? RequestKey(IpcRequest request) => request.Request is JsonElement e && e.ValueKind == JsonValueKind.Object &&
        e.TryGetProperty("requestId", out var id) && id.TryGetGuid(out var uuid) ? uuid : request.CallId;
    private bool CancelPending(Connection connection, Guid key, string code)
    {
        if (!connection.Waiting.Remove(key, out var pending)) return false;
        int count = connection.Queue.Count;
        for (int i = 0; i < count; i++) { var item = connection.Queue.Dequeue(); if (item == pending) _count--; else connection.Queue.Enqueue(item); }
        pending.Result.TrySetResult(new(code)); return true;
    }
    private static bool SecretEquals(string expected, string actual)
    {
        if (actual.Length != expected.Length) return false;
        return CryptographicOperations.FixedTimeEquals(Wire.Utf8.GetBytes(expected), Wire.Utf8.GetBytes(actual));
    }
    private void CancelQueue(Connection connection, string code)
    { while (connection.Queue.TryDequeue(out var item)) { _count--; item.Result.TrySetResult(new(code)); } connection.Waiting.Clear(); }
    private void Verify()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Endpoint control requires its owner thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Endpoint disposal requires its owner thread.");
        if (_disposed) return;
        Verify();
        lock (_gate)
        {
            _disposed = true; _stop.Cancel();
            foreach (var connection in _connections) { connection.Paired = false; connection.Pairing.TrySetResult(false); CancelQueue(connection, "endpoint_closed"); }
            foreach (var pipe in _pipes) pipe.Dispose();
        }
        // Exact owned descriptor only; never enumerate or recursively clean the discovery directory.
        if (File.Exists(DescriptorPath)) File.Delete(DescriptorPath);
    }
}
