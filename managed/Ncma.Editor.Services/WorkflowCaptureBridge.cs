using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Ncma.Editor.Services;

internal sealed record WorkflowCaptureBridgeStatus(bool Ready, bool Closing, bool Closed, uint WorkerId,
    bool RootExited, uint ActiveProcesses, int? ExitCode, int RetainedCaptureHandles, string FailureType);

// C2b1 REAL preparation only, not IWorkflowBuildLease and not a build launcher.
// Keep this owner even when Begin/Close fails. No using/implicit cleanup on fault.
internal sealed class WorkflowCaptureBridge
{
    private static readonly string[] helpers = ["scripts/AiCandidatePackage.ps1", "scripts/CapturedBuild.ps1",
        "scripts/EditorDeployment.ps1", "scripts/EditorPackageDependencies.ps1", "scripts/WorkflowBuildInputs.ps1", "scripts/WorkflowCaptureWorker.ps1", "scripts/WorkflowJobWorker.ps1"];
    internal static string[] RequiredHelpers() => (string[])helpers.Clone();
    private readonly int thread = System.Environment.CurrentManagedThreadId;
    private readonly object gate = new();
    private readonly Guid nonce = Guid.NewGuid();
    private readonly WorkflowVerificationTask task;
    private readonly WorkflowBuildEnvironment environment;
    private readonly WorkflowPhysicalPins codePins = new();
    private readonly WorkflowWindowsProcess worker = new();
    private readonly List<SafeFileHandle> capturePins = [];
    private readonly TaskCompletionSource closeSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource preparationTimeout = new();
    private NamedPipeServerStream? pipe;
    private Task exchange = Task.CompletedTask;
    private bool begun, ready, closing, closed;
    private int? finalExitCode;
    private string failure = "", facts = "";
    internal string TaskFingerprint => task.Fingerprint;
    internal string EnvironmentFingerprint => environment.Fingerprint;
    internal WorkflowCaptureBridge(WorkflowVerificationTask task, WorkflowBuildEnvironment environment)
    {
        if (!environment.CopyHelpers().Select(f => f.Path).SequenceEqual(helpers, StringComparer.Ordinal)) throw new IOException("workflow_helper_closure");
        this.task = task; this.environment = environment;
    }
    private void Owner()
    {
        if (System.Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("workflow_bridge_thread");
    }
    internal void BeginPreparation()
    {
        Owner(); if (begun || closed || closing) throw new InvalidOperationException("workflow_bridge_single_use"); begun = true;
        try {
            string root = WorkflowPhysicalPins.Absolute(environment.WorkspaceRoot);
            foreach (var helper in environment.CopyHelpers()) codePins.File(Path.Combine(root, helper.Path.Replace('/', '\\')), helper.Sha256, helper.Bytes);
            codePins.File(environment.RunnerPath, environment.RunnerSha256);
            codePins.File(environment.ToolPath, task.Binding.ToolSha256);
            if (Path.GetFileName(environment.RunnerPath) is not ("powershell.exe" or "pwsh.exe") || Path.GetFileName(environment.ToolPath) != "dotnet.exe")
                throw new IOException("workflow_fixed_tool_names");
            string scratch = codePins.CreateScratch(root, nonce);
            string name = "ncma-workflow-capture-" + nonce.ToString("N");
            pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
            worker.Start(environment.RunnerPath, Path.Combine(root, "scripts", "WorkflowCaptureWorker.ps1"), name, scratch);
            preparationTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            var channel = pipe;
            exchange = Task.Run(() => ExchangeAsync(channel, preparationTimeout.Token));
        } catch (Exception error) when (error is not OutOfMemoryException) { RecordFailure(error); throw; }
    }
    private void RecordFailure(Exception error)
    {
        lock (gate) { ready = false; if (failure.Length == 0) failure = error.GetType().Name.Length <= 128 ? error.GetType().Name : error.GetType().Name[..128]; }
    }
    private async Task ExchangeAsync(NamedPipeServerStream channel, CancellationToken token)
    {
        try {
            await channel.WaitForConnectionAsync(token).ConfigureAwait(false);
            if (!GetNamedPipeClientProcessId(channel.SafePipeHandle, out uint client) || client != worker.ProcessId || worker.Observe().RootExited)
                throw new IOException("workflow_pipe_peer_identity");
            var b = task.Binding;
            await WriteFrame(channel, new { kind = "prepare", version = 1, nonce = nonce.ToString("N"), workspace = environment.WorkspaceRoot,
                captureId = b.CaptureId, jobId = b.JobId, manifestSha256 = b.CaptureManifestSha256, sourceInputsSha256 = b.SourceInputsSha256,
                recipeSha256 = b.CaptureRecipeSha256, reviewBundleSha256 = b.ReviewBundleSha256, recipeId = b.RecipeId,
                configuration = b.Configuration, variant = b.Variant }, token).ConfigureAwait(false);
            using (var reply = await ReadFrame(channel, token).ConfigureAwait(false)) {
                var body = reply.RootElement;
                if (body.GetProperty("kind").GetString() == "failed") {
                    Shape(body, "kind", "nonce", "exceptionType");
                    string type = body.GetProperty("exceptionType").GetString() ?? "";
                    if (body.GetProperty("nonce").GetString() != nonce.ToString("N") || type.Length is < 1 or > 128 || type.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
                        throw new IOException("workflow_failure_shape");
                    lock (gate) if (failure.Length == 0) failure = "Worker:" + type;
                    throw new IOException("workflow_worker_prepare_failed");
                }
                Shape(body, "kind", "nonce", "facts", "handles");
                if (body.GetProperty("kind").GetString() != "prepared" || body.GetProperty("nonce").GetString() != nonce.ToString("N")) throw new IOException("workflow_prepare_reply");
                string received = body.GetProperty("facts").GetString() ?? throw new IOException("workflow_prepare_facts");
                int sourceFiles = ValidateFacts(received, task, environment);
                var handles = body.GetProperty("handles");
                if (handles.ValueKind != JsonValueKind.Array || handles.GetArrayLength() < sourceFiles + 2 || handles.GetArrayLength() > 81924)
                    throw new IOException("workflow_capture_handle_count");
                var seen = new HashSet<long>();
                foreach (var value in handles.EnumerateArray()) {
                    token.ThrowIfCancellationRequested(); long handle = value.GetInt64();
                    if (handle <= 0 || !seen.Add(handle)) throw new IOException("workflow_capture_handle_alias");
                    var local = worker.DuplicateFile(handle);
                    lock (gate) capturePins.Add(local); // retain partial transfer on any later failure
                }
                if (worker.Observe().RootExited) throw new IOException("workflow_preparer_lost");
                token.ThrowIfCancellationRequested();
                lock (gate) { facts = received; ready = !closing; }
            }
            preparationTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
            await closeSignal.Task.ConfigureAwait(false);
            await WriteFrame(channel, new { kind = "close", nonce = nonce.ToString("N") }, CancellationToken.None).ConfigureAwait(false);
            using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var acknowledgement = await ReadFrame(channel, closeTimeout.Token).ConfigureAwait(false);
            Shape(acknowledgement.RootElement, "kind", "nonce");
            if (acknowledgement.RootElement.GetProperty("kind").GetString() != "closing" || acknowledgement.RootElement.GetProperty("nonce").GetString() != nonce.ToString("N"))
                throw new IOException("workflow_close_reply");
            // Root/job drain, not this ACK, governs release.
        } catch (Exception error) when (error is not OutOfMemoryException) { RecordFailure(error); }
        finally { lock (gate) ready = false; channel.Dispose(); }
    }
    internal static int ValidateFacts(string json, WorkflowVerificationTask task, WorkflowBuildEnvironment environment)
    {
        if (json.Length > 16384) throw new IOException("workflow_facts_budget");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 }); var f = document.RootElement; var b = task.Binding;
        Shape(f, "kind", "schemaVersion", "workspace", "captureId", "jobId", "recipeId", "configuration", "variant", "captureRoot", "sourceRoot", "project", "outputRoot",
            "sourceInputsSha256", "captureManifestSha256", "captureRecipeSha256", "reviewBundleSha256", "sourceFileCount", "processStarted", "launchAuthorized", "outputReserved");
        string root = environment.WorkspaceRoot;
        string capture = Path.Combine(root, "out", "captured-build", b.CaptureId.ToString("N"));
        void Text(string key, string? expected) { if (f.GetProperty(key).GetString() != expected) throw new IOException("workflow_facts_binding"); }
        void PathValue(string key, string expected) {
            if (!string.Equals(Path.GetFullPath(f.GetProperty(key).GetString()!), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase)) throw new IOException("workflow_facts_path");
        }
        Text("kind", "ncma.workflow.capture-preparation.v1");
        if (f.GetProperty("schemaVersion").GetInt32() != 1 || f.GetProperty("processStarted").GetBoolean() || f.GetProperty("launchAuthorized").GetBoolean() || f.GetProperty("outputReserved").GetBoolean()) throw new IOException("workflow_facts_authority");
        Text("captureId", b.CaptureId.ToString("D")); Text("jobId", b.JobId.ToString("D")); Text("recipeId", b.RecipeId); Text("configuration", b.Configuration); Text("variant", b.Variant);
        Text("sourceInputsSha256", b.SourceInputsSha256); Text("captureManifestSha256", b.CaptureManifestSha256); Text("captureRecipeSha256", b.CaptureRecipeSha256); Text("reviewBundleSha256", b.ReviewBundleSha256);
        Text("project", task.Recipe.Project); PathValue("workspace", root); PathValue("captureRoot", capture); PathValue("sourceRoot", Path.Combine(capture, "source"));
        PathValue("outputRoot", new WorkflowBuildCommand(task, environment).OutputRoot);
        int count = f.GetProperty("sourceFileCount").GetInt32(); if (count is < 2 or > 65536) throw new IOException("workflow_facts_file_count"); return count;
    }
    internal string ReadPreparedFacts()
    {
        Owner(); var status = Snapshot();
        if (!status.Ready || status.RootExited || status.ActiveProcesses != 1) throw new IOException("workflow_preparer_not_ready");
        lock (gate) return facts; // diagnostic copy only, not executable authorization
    }
    internal WorkflowCaptureBridgeStatus Snapshot()
    {
        Owner(); if (closed) return new(false, true, true, worker.ProcessId, true, 0, finalExitCode, 0, failure);
        var observation = worker.Observe();
        lock (gate) return new(ready && !closing && !observation.RootExited && observation.ActiveProcesses == 1, closing, false, worker.ProcessId,
            observation.RootExited, observation.ActiveProcesses, observation.ExitCode, capturePins.Count, failure);
    }
    internal void RequestClose()
    {
        Owner(); if (closed || closing) return;
        bool wasReady; lock (gate) { closing = true; wasReady = ready; ready = false; }
        closeSignal.TrySetResult();
        if (!wasReady) { preparationTimeout.Cancel(); pipe?.Dispose(); }
    }
    internal bool TryClose()
    {
        Owner(); if (closed) return true;
        RequestClose();
        if (!exchange.IsCompleted) return false;
        var observation = worker.Observe();
        if (!observation.RootExited || observation.ActiveProcesses != 0) return false;
        // Keep all parent-held duplicates and helper pins until FULL job drain.
        finalExitCode = observation.ExitCode; worker.CloseDrained();
        lock (gate) { foreach (var handle in capturePins) handle.Dispose(); capturePins.Clear(); }
        pipe?.Dispose(); codePins.Dispose(); preparationTimeout.Dispose(); closed = true; return true;
    }
    internal static void Shape(JsonElement value, params string[] keys)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new IOException("workflow_frame_shape");
        var expected = new HashSet<string>(keys, StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject()) if (!expected.Remove(property.Name)) throw new IOException("workflow_frame_field");
        if (expected.Count != 0) throw new IOException("workflow_frame_missing");
    }
    internal static async Task WriteFrame(Stream pipe, object value, CancellationToken token)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > 16384) throw new IOException("workflow_request_budget");
        byte[] header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await pipe.WriteAsync(header, token).ConfigureAwait(false); await pipe.WriteAsync(bytes, token).ConfigureAwait(false); await pipe.FlushAsync(token).ConfigureAwait(false);
    }
    internal static async Task<JsonDocument> ReadFrame(Stream pipe, CancellationToken token)
    {
        byte[] header = new byte[4]; await pipe.ReadExactlyAsync(header, token).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 2 or > 2097152) throw new IOException("workflow_response_budget");
        byte[] bytes = new byte[length]; await pipe.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
}
