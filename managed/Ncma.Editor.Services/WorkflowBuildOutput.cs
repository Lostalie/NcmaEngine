namespace Ncma.Editor.Services;

// Resource owner exists before reservation. Failed reservations leave evidence;
// no recursive cleanup, automatic retry or overwrite. NOT a real build adapter.
internal sealed class WorkflowBuildOutput
{
    internal const int MaximumLogBytes = 4 * 1024 * 1024; // each stream
    private readonly int thread = System.Environment.CurrentManagedThreadId;
    private readonly IWorkflowBuildLease lease;
    private readonly WorkflowVerificationTask task;
    private readonly WorkflowBuildEnvironment environment;
    private readonly WorkflowPhysicalPins pins = new();
    private FileStream? standardOutput, standardError;
    private bool entered, attempted, reserved, closed, logFailed, logsSealed;
    private WorkflowEvidenceFile[] sealedLogs = [];
    private readonly System.Security.Cryptography.IncrementalHash stdoutHash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
    private readonly System.Security.Cryptography.IncrementalHash stderrHash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
    private bool sealingAttempted;
    private long stdoutBytes, stderrBytes;
    internal WorkflowBuildCommand Command { get; }
    internal string TaskFingerprint => task.Fingerprint;
    internal bool Reserved => reserved && !closed;
    internal bool Closed => closed;
    internal bool LogFailed => logFailed;
    internal long StandardOutputBytes => stdoutBytes;
    internal long StandardErrorBytes => stderrBytes;
    internal WorkflowBuildOutput(IWorkflowBuildLease lease)
    {
        this.lease = lease; task = lease.Task; environment = lease.Environment;
        Command = new(task, environment);
    }
    private readonly struct Guard(WorkflowBuildOutput owner) : IDisposable { public void Dispose() => owner.entered = false; }
    private Guard Enter()
    {
        if (System.Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("workflow_output_thread");
        if (entered) throw new InvalidOperationException("workflow_output_reentry");
        entered = true; return new(this);
    }
    internal void Reserve(WorkflowBuildStartPermit permit)
    {
        using var guard = Enter();
        if (attempted || closed) throw new IOException("workflow_output_single_use");
        attempted = true; ArgumentNullException.ThrowIfNull(permit);
        permit.Consume(lease); // requires exact live Start callback, before any output IO
        if (!ReferenceEquals(task, lease.Task) || !ReferenceEquals(environment, lease.Environment)) throw new IOException("workflow_output_binding");
        string root = pins.CreateJobOutput(environment.WorkspaceRoot, task.Binding.JobId, Command.IsOfflineRestore);
        if (!string.Equals(root, Command.OutputRoot, StringComparison.Ordinal)) throw new IOException("workflow_output_path");
        foreach (string name in new[] { "artifacts", "logs", "temp", "cli-home" }) pins.CreateOutputChild(root, name);
        if (task.Recipe.Kind == WorkflowVerificationKind.Tests) pins.CreateOutputChild(root, "cases");
        if (task.Recipe.Kind == WorkflowVerificationKind.NativePrepare) pins.CreateOutputChild(root, "native");
        if (Command.IsOfflineRestore) {
            pins.CreateOutputChild(root, "feed"); pins.CreateOutputChild(root, "packages");
            byte[] config = System.Text.Encoding.UTF8.GetBytes(WorkflowBuildCommand.OfflineNugetConfig);
            using (var file = new FileStream(Command.NugetConfig, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) {
                WorkflowPhysicalPins.ValidateHandle(file.SafeFileHandle, false); file.Write(config); file.Flush(true);
            }
            pins.File(Command.NugetConfig, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(config)), config.Length);
        }
        standardOutput = NewLog(Path.Combine(root, "logs", "stdout.log"));
        standardError = NewLog(Path.Combine(root, "logs", "stderr.log"));
        reserved = true;
    }
    private static FileStream NewLog(string path)
    {
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        try { WorkflowPhysicalPins.ValidateHandle(stream.SafeFileHandle, false); return stream; }
        catch { stream.Dispose(); throw; }
    }
    // Future process reader must use a bounded pump. No unbounded ReadToEnd,
    // binary log, or silent tail truncation; overflow invalidates this receipt.
    internal void Append(bool error, ReadOnlySpan<byte> bytes)
    {
        using var guard = Enter();
        if (!Reserved || logFailed || sealingAttempted) throw new IOException("workflow_output_not_writable");
        long length = error ? stderrBytes : stdoutBytes;
        if (bytes.Length > MaximumLogBytes - length) { logFailed = true; throw new IOException("workflow_log_budget"); }
        try {
            (error ? standardError! : standardOutput!).Write(bytes);
            (error ? stderrHash : stdoutHash).AppendData(bytes);
            if (error) stderrBytes += bytes.Length; else stdoutBytes += bytes.Length;
        } catch { logFailed = true; throw; }
    }
    internal WorkflowEvidenceFile[] SealLogs()
    {
        using var guard = Enter();
        if (!Reserved || logFailed || !lease.Observe().ExecutionDrained) throw new IOException("workflow_logs_not_sealable");
        if (!logsSealed) {
            if (sealingAttempted) throw new IOException("workflow_log_seal_failed");
            sealingAttempted = true;
            standardOutput?.Flush(true); standardError?.Flush(true);
            standardOutput?.Dispose(); standardOutput = null;
            standardError?.Dispose(); standardError = null;
            var result = new List<WorkflowEvidenceFile>();
            foreach (string name in new[] { "stdout.log", "stderr.log" }) {
                var hash = pins.Observe(Path.Combine(Command.OutputRoot, "logs", name), MaximumLogBytes, allowEmpty: true);
                if (hash.Bytes != (name == "stdout.log" ? stdoutBytes : stderrBytes)) throw new IOException("workflow_log_size_changed");
                string expected = Convert.ToHexString((name == "stdout.log" ? stdoutHash : stderrHash).GetCurrentHash());
                if (hash.Sha256 != expected) throw new IOException("workflow_log_bytes_changed");
                result.Add(new(name, hash.Bytes, hash.Sha256));
            }
            sealedLogs = result.ToArray(); logsSealed = true;
        }
        return (WorkflowEvidenceFile[])sealedLogs.Clone();
    }
    internal byte[] ReadSealedLog(string name)
    {
        using var guard = Enter();
        if (!logsSealed || closed || name is not ("stdout.log" or "stderr.log")) throw new IOException("workflow_log_not_sealed");
        var file = sealedLogs.Single(f => f.Path == name);
        return pins.ReadFile(Path.Combine(Command.OutputRoot, "logs", name), file.Sha256, file.Bytes, MaximumLogBytes);
    }
    internal void Close()
    {
        using var guard = Enter(); if (closed) return;
        // The production adapter must implement Observe with root AND Job drain.
        // A synthetic lease here is not proof of any OS process lifetime.
        if (!lease.Observe().ExecutionDrained) throw new IOException("workflow_output_borrowers_active");
        standardOutput?.Flush(true); standardError?.Flush(true);
        standardOutput?.Dispose(); standardOutput = null;
        standardError?.Dispose(); standardError = null;
        pins.Dispose(); stdoutHash.Dispose(); stderrHash.Dispose(); closed = true;
    }
}
