using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Ncma.Editor.Services;

public sealed record WorkflowSourceCaptureView(Guid Id, Guid WorkflowId, bool Ready, bool Closing, bool Closed,
    uint WorkerId, bool RootExited, uint ActiveProcesses, int? ExitCode, int RetainedHandles,
    Guid CaptureId, string SourceSha256, string ManifestSha256, string Failure, string CloseFailure);
internal sealed record WorkflowCreatedSource(Guid CaptureId, string SourceSha256, string ManifestSha256, string RecipeSha256, int Files);

// Original capture creation owner, NOT executable approval. Owned BEFORE Begin.
// Original Windows Job/peer checks and original capture pins; no kill or fallback.
internal sealed class WorkflowSourceCaptureJob
{
    internal static string[] RequiredHelpers() => [.. WorkflowCaptureBridge.RequiredHelpers(), "scripts/WorkflowSourceCaptureWorker.ps1"];
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly object gate = new();
    internal Guid Id { get; } = Guid.NewGuid();
    internal WorkflowSourceReview Review { get; }
    private readonly string bundle, git;
    private readonly WorkflowPhysicalPins pins = new();
    private readonly WorkflowWindowsProcess worker = new();
    private readonly List<SafeFileHandle> capturePins = [];
    private readonly TaskCompletionSource closeSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource timeout = new();
    private NamedPipeServerStream? pipe;
    private Task exchange = Task.CompletedTask;
    private bool begun, ready, closing, closed;
    private int? finalExit;
    private string failure = "", closeFailure = "";
    private WorkflowCreatedSource? result;
    internal WorkflowSourceCaptureJob(WorkflowSourceReview review, string bundlePath, string gitPath)
        => (Review, bundle, git) = (review, WorkflowBuildCommand.CommandPath(bundlePath), WorkflowBuildCommand.CommandPath(gitPath));
    private void Owner() { if (Environment.CurrentManagedThreadId != thread) throw new IOException("workflow_source_thread"); }
    private void Fail(Exception error) { lock (gate) { ready = false; if (failure.Length == 0) failure = WorkflowObservedEvidence.Code(error); } }
    internal void Begin()
    {
        Owner(); if (begun || closing || closed) throw new IOException("workflow_source_single_use"); begun = true;
        try {
            string root = Review.Workspace;
            foreach (var helper in Review.CopyHelpers())
                pins.File(Path.Combine(root, helper.Path.Replace('/', '\\')), helper.Sha256, helper.Bytes);
            pins.File(bundle, Review.BundleSha256);
            if (!string.Equals(Path.GetExtension(bundle), ".nccapture", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(git) != "git.exe")
                throw new IOException("workflow_source_input_kind");
            _ = pins.Observe(git, 64 * 1024 * 1024);
            string runner = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            _ = pins.Observe(runner, 64 * 1024 * 1024);
            string scratch = pins.CreateScratch(root, Id), name = "ncma-workflow-source-" + Id.ToString("N");
            pipe = new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
            worker.Start(runner, Path.Combine(root, "scripts", "WorkflowSourceCaptureWorker.ps1"), name, scratch);
            // New bounded full-copy operation, not an increase to any old entry.
            // Expiry disconnects IPC, never kills a still-copying process.
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            var channel = pipe; exchange = Task.Run(() => Exchange(channel, timeout.Token));
        } catch (Exception error) when (error is not OutOfMemoryException) { Fail(error); throw; }
    }
    private async Task Exchange(NamedPipeServerStream channel, CancellationToken token)
    {
        try {
            await channel.WaitForConnectionAsync(token).ConfigureAwait(false);
            if (!GetNamedPipeClientProcessId(channel.SafePipeHandle, out uint client) || client != worker.ProcessId || worker.Observe().RootExited)
                throw new IOException("workflow_pipe_peer_identity");
            await WorkflowCaptureBridge.WriteFrame(channel, new { kind = "capture-reviewed", version = 1, nonce = Id.ToString("N"),
                workspace = Review.Workspace, bundle, bundleSha256 = Review.BundleSha256, git }, token).ConfigureAwait(false);
            using (var response = await WorkflowCaptureBridge.ReadFrame(channel, token).ConfigureAwait(false)) {
                var body = response.RootElement;
                if (body.GetProperty("kind").GetString() == "failed") {
                    WorkflowCaptureBridge.Shape(body, "kind", "nonce", "exceptionType");
                    string type = body.GetProperty("exceptionType").GetString() ?? "";
                    if (body.GetProperty("nonce").GetString() != Id.ToString("N") || type.Length is < 1 or > 128 || type.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
                        throw new IOException("workflow_source_failure_shape");
                    lock (gate) if (failure.Length == 0) failure = "Worker:" + type;
                    throw new IOException("workflow_source_worker_failed");
                }
                WorkflowCaptureBridge.Shape(body, "kind", "nonce", "facts", "handles");
                if (body.GetProperty("kind").GetString() != "captured" || body.GetProperty("nonce").GetString() != Id.ToString("N")) throw new IOException("workflow_source_reply");
                var created = ValidateFacts(body.GetProperty("facts").GetString()!, Review);
                var handles = body.GetProperty("handles");
                if (handles.ValueKind != JsonValueKind.Array || handles.GetArrayLength() < created.Files + 3 || handles.GetArrayLength() > 81924)
                    throw new IOException("workflow_capture_handle_count");
                var seen = new HashSet<long>();
                foreach (var value in handles.EnumerateArray()) {
                    token.ThrowIfCancellationRequested(); long borrowed = value.GetInt64();
                    if (borrowed <= 0 || !seen.Add(borrowed)) throw new IOException("workflow_capture_handle_alias");
                    var local = worker.DuplicateFile(borrowed); lock (gate) capturePins.Add(local);
                }
                if (worker.Observe().RootExited) throw new IOException("workflow_source_worker_lost");
                token.ThrowIfCancellationRequested(); lock (gate) { result = created; ready = !closing; }
            }
            timeout.CancelAfter(Timeout.InfiniteTimeSpan);
            await closeSignal.Task.ConfigureAwait(false);
            await WorkflowCaptureBridge.WriteFrame(channel, new { kind = "close", nonce = Id.ToString("N") }, CancellationToken.None).ConfigureAwait(false);
            using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var ack = await WorkflowCaptureBridge.ReadFrame(channel, closeTimeout.Token).ConfigureAwait(false);
            WorkflowCaptureBridge.Shape(ack.RootElement, "kind", "nonce");
            if (ack.RootElement.GetProperty("kind").GetString() != "closing" || ack.RootElement.GetProperty("nonce").GetString() != Id.ToString("N")) throw new IOException("workflow_close_reply");
        } catch (Exception error) when (error is not OutOfMemoryException) { Fail(error); }
        finally { lock (gate) ready = false; channel.Dispose(); }
    }
    internal static WorkflowCreatedSource ValidateFacts(string json, WorkflowSourceReview review)
    {
        if (json.Length > 16384) throw new IOException("workflow_source_facts_budget");
        using var document = JsonDocument.Parse(json); var body = document.RootElement;
        WorkflowCaptureBridge.Shape(body, "kind", "captureId", "workspace", "sourceInputsSha256", "manifestSha256", "recipeSha256", "reviewBundleSha256", "sourceFileCount", "buildAuthorized", "installationAuthorized");
        if (body.GetProperty("kind").GetString() != "ncma.workflow.source-created.v1" ||
            !Guid.TryParseExact(body.GetProperty("captureId").GetString(), "N", out var id) || id == Guid.Empty ||
            !string.Equals(WorkflowBuildCommand.CommandPath(body.GetProperty("workspace").GetString()!), review.Workspace, StringComparison.OrdinalIgnoreCase) ||
            body.GetProperty("reviewBundleSha256").GetString() != review.BundleSha256 || body.GetProperty("buildAuthorized").GetBoolean() || body.GetProperty("installationAuthorized").GetBoolean())
            throw new IOException("workflow_source_facts_binding");
        string source = body.GetProperty("sourceInputsSha256").GetString()!, manifest = body.GetProperty("manifestSha256").GetString()!, recipe = body.GetProperty("recipeSha256").GetString()!;
        foreach (string hash in new[] { source, manifest, recipe }) WorkflowEvidenceRules.Hash(hash);
        int count = body.GetProperty("sourceFileCount").GetInt32(); if (count is < 2 or > 65536) throw new IOException("workflow_source_file_budget");
        return new(id, source, manifest, recipe, count);
    }
    internal WorkflowSourceCaptureView Snapshot()
    {
        Owner(); var observed = closed ? (RootExited: true, ActiveProcesses: 0u, ExitCode: finalExit) : worker.Observe();
        lock (gate) return new(Id, Review.WorkflowId, ready && !closing && !closed && !observed.RootExited && observed.ActiveProcesses == 1,
            closing, closed, worker.ProcessId, observed.RootExited, observed.ActiveProcesses, observed.ExitCode, capturePins.Count,
            result?.CaptureId ?? Guid.Empty, result?.SourceSha256 ?? "", result?.ManifestSha256 ?? "", failure, closeFailure);
    }
    internal WorkflowCreatedSource RequireReady()
    {
        Owner(); if (!Snapshot().Ready) throw new IOException("workflow_source_capture_not_ready");
        lock (gate) return result!;
    }
    internal void RequestClose()
    {
        Owner(); if (closed || closing) return;
        bool wasReady; lock (gate) { closing = true; wasReady = ready; ready = false; }
        closeSignal.TrySetResult(); if (!wasReady) { timeout.Cancel(); pipe?.Dispose(); }
    }
    internal bool TryClose()
    {
        Owner(); if (closed) return true;
        try {
            RequestClose(); if (!exchange.IsCompleted) return false;
            var state = worker.Observe(); if (!state.RootExited || state.ActiveProcesses != 0) return false;
            finalExit = state.ExitCode; worker.CloseDrained();
            lock (gate) { foreach (var file in capturePins) file.Dispose(); capturePins.Clear(); }
            pins.Dispose(); pipe?.Dispose(); timeout.Dispose(); closed = true; return true;
        } catch (Exception error) when (error is not OutOfMemoryException) { lock (gate) if (closeFailure.Length == 0) closeFailure = WorkflowObservedEvidence.Code(error); return false; }
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
}
