using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Ncma.Modules.Grpc;

// Background-owned resources. A failed attempt remains reachable until actual process, IO and
// transport work terminate. No finalizer, process-name kill, implicit restart, or secret logging.
internal sealed class WorkerAttempt(WorkerInstallation installation)
{
    internal Guid Instance { get; } = Guid.NewGuid();
    internal LoopbackModuleAdapter? Adapter { get; private set; }
    internal string Failure { get; private set; } = "";
    internal bool Released { get; private set; }
    internal int ProcessId => process?.Id ?? 0;
    internal bool Exited => process?.HasExited ?? true;
    internal int? ExitCode => process is { HasExited: true } ? process.ExitCode : null;
    internal long StdoutBytes => Interlocked.Read(ref stdoutBytes);
    internal long StderrBytes => Interlocked.Read(ref stderrBytes);
    internal bool OutputFault => StdoutBytes != 0 || StderrBytes > 8192 || stdout?.IsFaulted == true || stderr?.IsFaulted == true;
    private WorkerInstallationPin? pin;
    private Process? process;
    private Task? stdout, stderr, exit;
    private long stdoutBytes, stderrBytes;
    private bool inputClosed;

    internal async Task StartAsync(CancellationToken cancellation)
    {
        byte[]? secret = null, bootstrap = null;
        try
        {
            pin = installation.Acquire(cancellation);
            using var caKey = RSA.Create(2048);
            var caRequest = new CertificateRequest("CN=Ncma local worker root", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            using var ca = caRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=Ncma local worker", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder(); names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
            using var cert = request.Create(ca, DateTimeOffset.UtcNow.AddSeconds(-30), DateTimeOffset.UtcNow.AddHours(12), RandomNumberGenerator.GetBytes(16));
            secret = RandomNumberGenerator.GetBytes(32);
            bootstrap = WorkerWire.Encode(new
            {
                version = WorkerWire.Version, instance = Instance, secret = Convert.ToHexString(secret), keyPem = key.ExportPkcs8PrivateKeyPem(),
                certificatePem = cert.ExportCertificatePem() + "\n" + ca.ExportCertificatePem(),
                operation = WorkerWire.Describe(TextStatisticsModule.Operation)
            });
            if (bootstrap.Length > 16383) throw new InvalidDataException("Worker bootstrap budget.");
            var python = Path.Combine(installation.Root, "python.exe");
            var start = new ProcessStartInfo(python)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = installation.Root
            };
            foreach (var arg in new[] { "-I", "-S", "-B", Path.Combine(installation.Root, "ncma_module_worker.py") }) start.ArgumentList.Add(arg);
            start.Environment.Clear();
            // OS facilities only. No PATH/PYTHONPATH/user-site/proxy/credential inheritance.
            if (Environment.GetEnvironmentVariable("SystemRoot") is { } systemRoot) start.Environment["SystemRoot"] = systemRoot;
            cancellation.ThrowIfCancellationRequested();
            pin.VerifyNames();
            process = Process.Start(start) ?? throw new IOException("Worker process failed to start.");
            _ = process.Handle;
            exit = process.WaitForExitAsync();
            stderr = Drain(process.StandardError.BaseStream, false);
            using var readiness = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            readiness.CancelAfter(TimeSpan.FromSeconds(10));
            // Drain/read concurrently with the private bootstrap write. A malformed worker can
            // fill stdout BEFORE reading stdin; sequential pipe operations would deadlock.
            var readyRead = ReadReadyAndDrain(process.StandardOutput.BaseStream, readiness.Token);
            byte[] ready;
            try
            {
                await process.StandardInput.BaseStream.WriteAsync(bootstrap, readiness.Token).ConfigureAwait(false);
                await process.StandardInput.BaseStream.WriteAsync(new byte[] { 10 }, readiness.Token).ConfigureAwait(false);
                await process.StandardInput.BaseStream.FlushAsync(readiness.Token).ConfigureAwait(false);
                CryptographicOperations.ZeroMemory(bootstrap);
                ready = await readyRead.ConfigureAwait(false);
            }
            finally
            {
                if (!readyRead.IsCompleted) await readiness.CancelAsync().ConfigureAwait(false);
                try { await readyRead.ConfigureAwait(false); } catch { /* Observed; startup failure is retained below. */ }
            }
            using var document = WorkerWire.Parse(ready, "version", "instance", "port", "processId");
            var value = document.RootElement;
            if (WorkerWire.Integer(value, "version", WorkerWire.Version, WorkerWire.Version) != WorkerWire.Version || WorkerWire.Identity(value, "instance") != Instance ||
                WorkerWire.Integer(value, "processId", 1, int.MaxValue) != process.Id || process.HasExited ||
                !string.Equals(Path.GetFullPath(process.MainModule!.FileName), python, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Worker process identity mismatch.");
            Adapter = await LoopbackModuleAdapter.ConnectAsync((int)WorkerWire.Integer(value, "port", 1, 65535),
                Instance, process.Id, secret, ca.RawData, SHA256.HashData(cert.RawData), TextStatisticsModule.Operation, readiness.Token).ConfigureAwait(false);
            CryptographicOperations.ZeroMemory(secret);
            pin.VerifyNames();
            cancellation.ThrowIfCancellationRequested();
            if (process.HasExited || OutputFault) throw new IOException("Worker exited or exceeded its output budget.");
        }
        catch (Exception error)
        {
            // Exception text can include paths or peer-controlled details. Publish only a fixed code.
            Failure = error is OperationCanceledException ? "startup_cancelled_or_timed_out" : "startup_failed";
            if (process is not null)
            {
                stdout ??= Drain(process.StandardOutput.BaseStream, true);
                stderr ??= Drain(process.StandardError.BaseStream, false);
            }
            Adapter?.Stop();
            CloseInput();
        }
        finally
        {
            if (secret is not null) CryptographicOperations.ZeroMemory(secret);
            if (bootstrap is not null) CryptographicOperations.ZeroMemory(bootstrap);
        }
    }

    private async Task<byte[]> ReadReadyAndDrain(Stream stream, CancellationToken cancellation)
    {
        try { return await ReadReady(stream, cancellation).ConfigureAwait(false); }
        finally { stdout = Drain(stream, true); }
    }

    private static async Task<byte[]> ReadReady(Stream stream, CancellationToken cancellation)
    {
        var buffer = new byte[4096];
        for (var length = 0; length < buffer.Length; length++)
        {
            if (await stream.ReadAsync(buffer.AsMemory(length, 1), cancellation).ConfigureAwait(false) == 0)
                throw new EndOfStreamException("Worker exited before readiness.");
            if (buffer[length] == 10) return buffer.AsSpan(0, length).ToArray();
        }
        throw new InvalidDataException("Worker ready message budget.");
    }

    private async Task Drain(Stream stream, bool output)
    {
        // Retain counts only, never raw logs (which could contain bootstrap secrets). Continue
        // draining after the budget is exceeded; Pump observes the fault without pipe deadlock.
        var buffer = new byte[4096];
        while (true)
        {
            var count = await stream.ReadAsync(buffer).ConfigureAwait(false);
            if (count == 0) return;
            if (output) Interlocked.Add(ref stdoutBytes, count); else Interlocked.Add(ref stderrBytes, count);
        }
    }

    private void CloseInput()
    {
        if (process is null || inputClosed) return;
        try { process.StandardInput.Close(); inputClosed = true; }
        catch (IOException) { Failure = "stdin_close_failed"; }
    }

    internal async Task<bool> CloseAsync(bool terminate)
    {
        if (Released) return true;
        Adapter?.Stop();
        CloseInput();
        if (terminate && process is not null && !process.HasExited)
        {
            // This exact retained Process is the one we created, not a reported/reused PID.
            try { process.Kill(entireProcessTree: false); }
            catch (InvalidOperationException) when (process.HasExited) { }
        }
        var started = Stopwatch.GetTimestamp();
        while (Adapter is not null && !Adapter.TryClose())
        {
            if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(5)) { Failure = "transport_drain_timeout"; return false; }
            await Task.Delay(10).ConfigureAwait(false);
        }
        try
        {
            if (exit is not null) await exit.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            if (stdout is not null) await stdout.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            if (stderr is not null) await stderr.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (TimeoutException) { Failure = "process_or_output_drain_failed"; return false; }
        catch (Exception)
        {
            Failure = "process_or_output_reader_fault";
            if (exit is { IsCompleted: false } || stdout is { IsCompleted: false } || stderr is { IsCompleted: false }) return false;
            _ = stdout?.Exception; _ = stderr?.Exception; _ = exit?.Exception;
        }
        if (process is { HasExited: false }) { Failure = "process_retained"; return false; }
        // All readers and the process are terminal before unpinning executable/dependencies.
        pin?.Dispose(); pin = null;
        Released = true;
        return true;
    }

    internal void DisposeReleased()
    {
        if (!Released) throw new InvalidOperationException("Worker resources are still retained.");
        process?.Dispose();
    }
}
