using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;

namespace Ncma.Editor.Services;

// Execution owner exists before Begin. A lost IPC channel never authorizes input
// release; the native Job/root AND both transport tasks must finish first.
internal sealed class WorkflowJobProcess
{
    private readonly int thread = System.Environment.CurrentManagedThreadId;
    private readonly WorkflowVerificationTask task;
    private readonly WorkflowBuildEnvironment environment;
    private readonly WorkflowWindowsProcess process = new();
    private readonly WorkflowPhysicalPins pins = new();
    private readonly Guid nonce = Guid.NewGuid();
    private readonly Channel<(bool Error, byte[] Bytes)> messages = Channel.CreateBounded<(bool, byte[])>(new BoundedChannelOptions(32) { SingleReader = true, SingleWriter = true });
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly CancellationTokenSource stopLoop = new();
    private readonly CancellationTokenSource transportStop = new();
    private WorkflowBuildObservation? terminal;
    private NamedPipeServerStream? pipe;
    private Task exchange = Task.CompletedTask, controls = Task.CompletedTask;
    private WorkflowBuildOutput? output;
    private int stopRequested, timedOut, sdkStarted, sdkExitSeen, sdkExitCode;
    private string failure = "";
    private bool attempted, closed;
    private long startedAt;
    internal bool TimedOut => Volatile.Read(ref timedOut) != 0;
    internal bool SdkStarted => Volatile.Read(ref sdkStarted) != 0;
    internal string Failure => Volatile.Read(ref failure);
    internal WorkflowJobProcess(WorkflowVerificationTask task, WorkflowBuildEnvironment environment) => (this.task, this.environment) = (task, environment);
    private void Owner() { if (System.Environment.CurrentManagedThreadId != thread) throw new IOException("workflow_process_thread"); }
    private void Fail(string code) => Interlocked.CompareExchange(ref failure, code, "");
    internal void Begin(WorkflowBuildOutput owner, WorkflowToolchain tools, WorkflowCaptureBridge capture)
    {
        Owner(); if (attempted || closed) throw new IOException("workflow_process_single_use"); attempted = true; output = owner;
        if (!owner.Reserved || owner.TaskFingerprint != task.Fingerprint || capture.TaskFingerprint != task.Fingerprint ||
            capture.EnvironmentFingerprint != environment.Fingerprint || owner.Command.EnvironmentFingerprint != environment.Fingerprint)
            throw new IOException("workflow_process_binding");
        _ = capture.ReadPreparedFacts(); _ = tools.CopyFiles();
        if (tools.ToolSha256 != task.Binding.ToolSha256 || tools.SdkVersion != task.Binding.ToolVersion ||
            !string.Equals(Path.Combine(tools.Root, "dotnet.exe"), owner.Command.Executable, StringComparison.OrdinalIgnoreCase)) throw new IOException("workflow_process_tool_binding");
        // Per-job SDK selection. This never edits the repository's global.json.
        byte[] selection = JsonSerializer.SerializeToUtf8Bytes(new { sdk = new { version = tools.SdkVersion, rollForward = "disable", allowPrerelease = tools.SdkVersion.Contains('-') } });
        string global = Path.Combine(owner.Command.OutputRoot, "global.json");
        using (var file = new FileStream(global, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) {
            WorkflowPhysicalPins.ValidateHandle(file.SafeFileHandle, false); file.Write(selection); file.Flush(true);
        }
        pins.File(global, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(selection)), selection.Length);
        string name = "ncma-workflow-job-" + nonce.ToString("N");
        pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 8192, 8192);
        process.Start(environment.RunnerPath, Path.Combine(environment.WorkspaceRoot, "scripts", "WorkflowJobWorker.ps1"), name, owner.Command.TemporaryRoot);
        startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        exchange = Task.Run(() => Exchange(pipe, owner.Command));
    }
    private async Task Exchange(NamedPipeServerStream channel, WorkflowBuildCommand command)
    {
        try {
            using var connectionDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await channel.WaitForConnectionAsync(connectionDeadline.Token).ConfigureAwait(false);
            if (!GetNamedPipeClientProcessId(channel.SafePipeHandle, out uint id) || id != process.ProcessId || process.Observe().RootExited)
                throw new IOException("workflow_process_peer");
            if (Volatile.Read(ref stopRequested) != 0) throw new OperationCanceledException("workflow_process_cancelled_before_request");
            byte[] request = JsonSerializer.SerializeToUtf8Bytes(new { kind = "run", nonce = nonce.ToString("N"), tool = command.Executable,
                arguments = string.Join(' ', command.CopyArguments().Select(WorkflowWindowsProcess.Quote)), working = command.OutputRoot, cli = command.CliHome, temp = command.TemporaryRoot });
            if (request.Length > 32768) throw new IOException("workflow_process_request_budget");
            byte[] header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, request.Length);
            await channel.WriteAsync(header).ConfigureAwait(false); await channel.WriteAsync(request).ConfigureAwait(false); await channel.FlushAsync().ConfigureAwait(false);
            controls = ControlLoop(channel);
            long stdout = 0, stderr = 0;
            while (true) {
                await channel.ReadExactlyAsync(header, transportStop.Token).ConfigureAwait(false); int size = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (size is < 2 or > 20000) throw new IOException("workflow_process_frame_budget");
                byte[] body = new byte[size]; await channel.ReadExactlyAsync(body, transportStop.Token).ConfigureAwait(false);
                using var json = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 4 }); var frame = json.RootElement;
                string? kind = frame.GetProperty("kind").GetString();
                if (kind is "stdout" or "stderr") {
                    WorkflowCaptureBridge.Shape(frame, "kind", "data");
                    if (!SdkStarted || Volatile.Read(ref sdkExitSeen) != 0) throw new IOException("workflow_process_event_order");
                    byte[] bytes = frame.GetProperty("data").GetBytesFromBase64();
                    if (bytes.Length is < 1 or > 8192) throw new IOException("workflow_process_chunk_budget");
                    if (kind == "stderr") stderr += bytes.Length; else stdout += bytes.Length;
                    if (stdout > WorkflowBuildOutput.MaximumLogBytes || stderr > WorkflowBuildOutput.MaximumLogBytes) throw new IOException("workflow_process_log_budget");
                    await messages.Writer.WriteAsync((kind == "stderr", bytes), transportStop.Token).ConfigureAwait(false);
                } else if (kind == "started") {
                    WorkflowCaptureBridge.Shape(frame, "kind"); if (Interlocked.Exchange(ref sdkStarted, 1) != 0) throw new IOException("workflow_process_started_twice");
                } else if (kind == "exit") {
                    WorkflowCaptureBridge.Shape(frame, "kind", "exitCode"); if (!SdkStarted || Interlocked.Exchange(ref sdkExitSeen, 1) != 0) throw new IOException("workflow_process_exit_order");
                    Volatile.Write(ref sdkExitCode, frame.GetProperty("exitCode").GetInt32()); await SendControl(channel, 2).ConfigureAwait(false); break;
                } else if (kind is "log-overflow" or "transport-failed" or "stop-unsupported") {
                    WorkflowCaptureBridge.Shape(frame, "kind"); Fail("workflow_process_" + kind);
                } else throw new IOException("workflow_process_event");
            }
        } catch (Exception error) when (error is not OutOfMemoryException) { Fail(error.GetType().Name); }
        finally {
            stopLoop.Cancel(); try { await controls.ConfigureAwait(false); } catch (OperationCanceledException) { }
            messages.Writer.TryComplete(); channel.Dispose();
        }
    }
    private async Task SendControl(Stream channel, byte control)
    {
        await writeGate.WaitAsync().ConfigureAwait(false);
        try { await channel.WriteAsync(new[] { control }).ConfigureAwait(false); await channel.FlushAsync().ConfigureAwait(false); }
        finally { writeGate.Release(); }
    }
    private async Task ControlLoop(Stream channel)
    {
        bool sent = false;
        try {
            while (!stopLoop.IsCancellationRequested) {
                if (System.Diagnostics.Stopwatch.GetElapsedTime(startedAt) >= environment.Timeout) { Volatile.Write(ref timedOut, 1); Interlocked.Exchange(ref stopRequested, 1); }
                if (!sent && Volatile.Read(ref stopRequested) != 0) {
                    await SendControl(channel, 1).ConfigureAwait(false); sent = true;
                    // Cancellation cannot wait on a UI-consumed full queue. Stop
                    // accepting logs, disconnect and mark evidence incomplete;
                    // the worker still drains child stdout/stderr and retains
                    // its Job until natural SDK/descendant exit. Never force kill.
                    Fail("workflow_process_cancelled_log_incomplete"); transportStop.Cancel();
                }
                await Task.Delay(25, stopLoop.Token).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) when (stopLoop.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException) { Fail(error.GetType().Name); }
    }
    internal void RequestStop() { Owner(); Interlocked.Exchange(ref stopRequested, 1); }
    internal WorkflowBuildObservation Observe()
    {
        Owner();
        if (terminal is not null) return terminal;
        if (!attempted) return new(false, true, null);
        if (!process.Started && exchange.IsCompleted) messages.Writer.TryComplete();
        for (int i = 0; i < 32 && messages.Reader.TryRead(out var message); i++) {
            try { output!.Append(message.Error, message.Bytes); } catch (Exception error) when (error is not OutOfMemoryException) { Fail(error.GetType().Name); }
        }
        var state = process.Observe(); bool drained = state.RootExited && state.ActiveProcesses == 0 && exchange.IsCompleted && controls.IsCompleted && messages.Reader.Completion.IsCompleted;
        if (drained && process.Started && (Volatile.Read(ref sdkExitSeen) == 0 || !SdkStarted)) Fail("workflow_process_missing_sdk_exit");
        if (drained && process.Started && Volatile.Read(ref sdkExitSeen) != 0 && state.ExitCode != Volatile.Read(ref sdkExitCode)) Fail("workflow_process_worker_exit_mismatch");
        // Worker exit is not the actual SDK/test exit. OS Job drain can still
        // allow explicit release when transport lost the child result; the
        // failure code prevents success and the nullable result stays unknown.
        int? code = Volatile.Read(ref sdkExitSeen) != 0 ? Volatile.Read(ref sdkExitCode) : null;
        return new(process.Started, drained, code, Failure, TimedOut);
    }
    internal void CloseDrained()
    {
        Owner(); if (closed) return;
        var final = Observe();
        if (!final.ExecutionDrained) throw new IOException("workflow_process_not_drained");
        process.CloseDrained(); pipe?.Dispose(); pins.Dispose(); writeGate.Dispose(); stopLoop.Dispose(); transportStop.Dispose(); terminal = final; closed = true;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
}
