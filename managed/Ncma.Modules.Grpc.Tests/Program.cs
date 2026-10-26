using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text;
using System.Text.Json;
using Ncma.Modules;
using Ncma.Modules.Grpc;
using Grpc.Core;

internal static partial class Program
{
    private static int checks;
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); checks++; }
    private static void Wait(Func<bool> completed)
    {
        var watch = Stopwatch.StartNew();
        while (!completed())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Test wait failed.");
            Thread.Sleep(1);
        }
    }

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[1] == "--relocated-client")
            {
                RelocatedClient(Path.GetFullPath(args[0]), args[2]);
                Console.WriteLine($"PASS relocated optional client: {checks} checks.");
                return 0;
            }
            if (args.Length == 3 && args[1] == "--no-python-products")
            {
                NoPythonProducts(Path.GetFullPath(args[0]), args[2]);
                Console.WriteLine($"PASS no-Python default products: {checks} checks.");
                return 0;
            }
            if (args.Length == 2 && args[1] == "--host-negatives")
            {
                return CaseHarness.Run("Worker refusals", [
                    ("installation", () => InstallationRefusals(Path.GetFullPath(args[0]))),
                    ("host faults", () => HostFaults(Path.GetFullPath(args[0])))
                ]);
            }
            if (args.Length == 2 && args[1] == "--owned-host")
            {
                OwnedHost(Path.GetFullPath(args[0]));
                Console.WriteLine($"PASS owned worker host: {checks} checks.");
                return 0;
            }
            if (args.Length != 1) throw new ArgumentException("Repository path required.");
            string root = Path.GetFullPath(args[0]);
            return CaseHarness.Run("Real TLS C# / Python gRPC (no inference or World mutation)", [
                ("authentication", () => Authentication(root)), ("protocol refusals", () => ProtocolRefusals(root)),
                ("retained worker work", () => RetainedWorkerWork(root)), ("malformed replies", () => MalformedReplies(root)),
                ("real worker", () => RealWorker(root)), ("owned host", () => OwnedHost(root)),
                ("installation refusals", () => InstallationRefusals(root)), ("host faults", () => HostFaults(root))
            ]);
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Authentication(string root)
    {
        using var worker = new WorkerFixture(root);
        Check(worker.ProcessId != Environment.ProcessId, "Worker is a separate process.");
        var wrongSecret = (byte[])worker.Secret.Clone(); wrongSecret[0] ^= 1;
        try { worker.Connect(wrongSecret); throw new Exception("Wrong credential accepted."); }
        catch (RpcException error) { Check(error.StatusCode == StatusCode.Unauthenticated, "TLS peer refuses wrong credential: " + error.Status); }
        var wrongPin = (byte[])worker.LeafHash.Clone(); wrongPin[0] ^= 1;
        try { worker.Connect(pin: wrongPin); throw new Exception("Wrong certificate accepted."); }
        catch (RpcException error)
        {
            // grpc-dotnet reports this pre-RPC TLS failure as Internal, not application Unavailable.
            var cause = error.Status.DebugException;
            while (cause is not null && cause is not AuthenticationException) cause = cause.InnerException;
            Check(error.StatusCode == StatusCode.Internal && cause is AuthenticationException,
                "Certificate pin mismatch must fail TLS authentication: " + error.Status);
        }
        var adapter = worker.Connect();
        adapter.Stop(); Wait(adapter.TryClose);
        worker.Close();
        Check(worker.ExitCode == 0, "Rejected TLS/auth calls do not break graceful worker exit.");
        Check(!worker.ErrorText.Contains(Convert.ToHexString(worker.Secret), StringComparison.OrdinalIgnoreCase) &&
            !worker.ErrorText.Contains("PRIVATE KEY", StringComparison.Ordinal), "Negative TLS diagnostics do not expose bootstrap credentials.");
        Console.WriteLine($"PASS real wrong-token/wrong-pin refusals followed by valid handshake; bounded negative-peer stderr={worker.ErrorBytes} bytes.");
    }

    private static void RealWorker(string root)
    {
        using var worker = new WorkerFixture(root);
        var adapter = worker.Connect();
        var context = new ModuleContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            ulong.MaxValue, ulong.MaxValue, Guid.NewGuid(), ulong.MaxValue, Guid.NewGuid(), Guid.NewGuid(), ulong.MaxValue);
        var session = new ModuleSession(new ModuleCatalog([TextStatisticsModule.Operation]), adapter, context);
        try
        {
            foreach (var text in new[] { "", "Ncma", "动作 AI 🤖 café", "\"quote\"\n\\path\t", new string('x', 32_000) })
            {
                var id = Guid.NewGuid();
                Check(session.Submit(id, TextStatisticsModule.Operation.Name,
                    JsonSerializer.SerializeToUtf8Bytes(new { text })) == ModuleAdmission.Accepted, "Request admitted.");
                ModuleOutcome? result = null;
                Wait(() => session.TryTake(id, context, out result));
                Check(result!.Status == ModuleOutcomeStatus.NeedsDomainValidation, "Real RPC returns an unapproved copied proposal.");
                using var document = JsonDocument.Parse(result.Proposal!.CopyBytes());
                var data = document.RootElement;
                Check(data.GetProperty("codePoints").GetInt32() == text.EnumerateRunes().Count(), "Python Unicode scalar count.");
                Check(data.GetProperty("utf8Bytes").GetInt32() == Encoding.UTF8.GetByteCount(text), "Python UTF-8 byte count.");
                Check(data.GetProperty("sha256").GetString() == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))), "Actual Python text digest.");
                Check(!session.TryTake(id, context, out _), "Single delivery retained across RPC.");
            }
            Check(typeof(LoopbackModuleAdapter).Assembly.GetReferencedAssemblies().All(a =>
                a.Name != "Ncma.Runtime" && !a.Name!.StartsWith("Ncma.Editor") && a.Name != "Ncma.Rendering"), "No live domain/native authority dependency.");
        }
        finally
        {
            session.Stop(); Wait(session.TryClose);
            adapter.Stop(); Wait(adapter.TryClose);
        }
        worker.Close();
        Check(worker.ExitCode == 0, "Worker exits on owned stdin EOF.");
        Check(worker.ErrorBytes == 0, "Fresh healthy worker emits no stderr: " + worker.ErrorText);
    }

    private enum WorkerProbe { None, RetainedWork, MalformedReplies }

    // Protocol fault-injection TEST owner only. Product ownership is LocalWorkerHost/WorkerAttempt;
    // do not confuse this venv fixture with the approved independent installation contract.
    private sealed class WorkerFixture : IDisposable
    {
        private readonly Process process;
        private readonly FileStream sourcePin;
        private readonly FileStream? probePin;
        private readonly Task<byte[]> stderr;
        private readonly byte[] rootDer;
        private readonly int port;
        private Process? workerProcess;
        private bool closed;
        internal Guid Instance { get; } = Guid.NewGuid();
        internal byte[] Secret { get; } = RandomNumberGenerator.GetBytes(32);
        internal byte[] LeafHash { get; }
        internal int ProcessId => workerProcess!.Id;
        internal int ExitCode => process.ExitCode;
        internal int ErrorBytes => stderr.GetAwaiter().GetResult().Length;
        internal string ErrorText => Encoding.UTF8.GetString(stderr.GetAwaiter().GetResult());

        internal WorkerFixture(string root, WorkerProbe probe = WorkerProbe.None)
        {
            var script = Path.Combine(root, "python/worker/ncma_module_worker.py");
            var python = Path.Combine(root, "out/modules/python312/Scripts/python.exe");
            sourcePin = File.Open(script, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (probe != WorkerProbe.None)
            {
                script = Path.Combine(root, probe switch
                {
                    WorkerProbe.RetainedWork => "python/worker/tests/retained_work_probe.py",
                    WorkerProbe.MalformedReplies => "python/worker/tests/malformed_reply_probe.py",
                    _ => throw new ArgumentOutOfRangeException(nameof(probe))
                });
                probePin = File.Open(script, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            using var caKey = RSA.Create(2048);
            var caRequest = new CertificateRequest("CN=Ncma test worker root", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            using var ca = caRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            rootDer = ca.RawData;
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=Ncma test worker", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder(); names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
            using var certificate = request.Create(ca, DateTimeOffset.UtcNow.AddSeconds(-30), DateTimeOffset.UtcNow.AddMinutes(30), RandomNumberGenerator.GetBytes(16));
            LeafHash = SHA256.HashData(certificate.RawData);
            var operation = TextStatisticsModule.Operation;
            var bootstrap = JsonSerializer.Serialize(new
            {
                version = 2, instance = Instance, secret = Convert.ToHexString(Secret),
                keyPem = key.ExportPkcs8PrivateKeyPem(), certificatePem = certificate.ExportCertificatePem() + "\n" + ca.ExportCertificatePem(),
                operation = new
                {
                    moduleId = operation.ModuleId, moduleVersion = operation.ModuleVersion,
                    configurationId = operation.ConfigurationId, name = operation.Name,
                    inputSchema = operation.InputSchema, outputSchema = operation.OutputSchema,
                    maxInputBytes = operation.MaximumInputBytes, maxOutputBytes = operation.MaximumOutputBytes,
                    timeoutMilliseconds = operation.TimeoutMilliseconds
                }
            });
            var start = new ProcessStartInfo(python)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(script)!
            };
            start.ArgumentList.Add("-I"); start.ArgumentList.Add("-B"); start.ArgumentList.Add(script);
            start.Environment.Clear();
            foreach (var name in new[] { "SystemRoot", "TEMP", "TMP" })
                if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
            process = Process.Start(start) ?? throw new InvalidOperationException("Worker process did not start.");
            _ = process.Handle; // Retain the exact owned process; never look it up by name.
            stderr = DrainBounded(process.StandardError.BaseStream);
            try
            {
                process.StandardInput.WriteLine(bootstrap); process.StandardInput.Flush();
                var ready = ReadReady(process.StandardOutput.BaseStream).GetAwaiter().GetResult();
                using var document = JsonDocument.Parse(ready);
                var data = document.RootElement;
                Check(data.GetProperty("version").GetInt32() == 2 && data.GetProperty("instance").GetGuid() == Instance &&
                    data.GetProperty("processId").GetInt32() > 0, "Private ready pipe version/instance identity.");
                var reported = data.GetProperty("processId").GetInt32();
                var candidateProcess = Process.GetProcessById(reported);
                // CPython's Windows venv redirector starts the actual interpreter as a child.
                // Verify the OS relationship and image, never trust an arbitrary reported PID.
                var basePython = File.ReadLines(Path.Combine(root, "out/modules/python312/pyvenv.cfg"))
                    .Single(line => line.StartsWith("executable = ", StringComparison.Ordinal))[13..];
                try
                {
                    _ = candidateProcess.Handle;
                    Check(!process.HasExited && !candidateProcess.HasExited &&
                        candidateProcess.StartTime >= process.StartTime &&
                        (reported == process.Id || ParentOf(reported) == process.Id) &&
                        string.Equals(Path.GetFullPath(candidateProcess.MainModule!.FileName),
                            Path.GetFullPath(reported == process.Id ? python : basePython), StringComparison.OrdinalIgnoreCase),
                        "OS-verified exact launcher/interpreter process identity.");
                    // Only verified ownership may enter cleanup/termination state.
                    workerProcess = candidateProcess;
                }
                catch { candidateProcess.Dispose(); throw; }
                port = data.GetProperty("port").GetInt32();
            }
            catch { Dispose(); throw; }
        }

        internal LoopbackModuleAdapter Connect(byte[]? secret = null, byte[]? pin = null) =>
            LoopbackModuleAdapter.ConnectAsync(port, Instance, ProcessId, secret ?? Secret, rootDer,
                pin ?? LeafHash, TextStatisticsModule.Operation).GetAwaiter().GetResult();

        internal RawPeer OpenRawPeer() => new(port, rootDer, LeafHash, Secret);

        private static async Task<byte[]> ReadReady(Stream stream)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var bytes = new byte[4096];
            for (var count = 0; count < bytes.Length; count++)
            {
                if (await stream.ReadAsync(bytes.AsMemory(count, 1), deadline.Token) == 0) throw new InvalidDataException("Worker exited before ready.");
                if (bytes[count] == (byte)'\n') return bytes.AsSpan(0, count).ToArray();
            }
            throw new InvalidDataException("Ready message too large.");
        }

        private static async Task<byte[]> DrainBounded(Stream stream)
        {
            var buffer = new byte[256]; var total = 0;
            using var output = new MemoryStream();
            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0) return output.ToArray();
                total = checked(total + read);
                if (total > 4096) throw new InvalidDataException("Worker stderr budget.");
                output.Write(buffer, 0, read);
            }
        }

        internal void Close()
        {
            if (closed) return;
            process.StandardInput.Close();
            if (!process.WaitForExit(5000)) throw new TimeoutException("Worker EOF shutdown timed out.");
            if (!workerProcess!.WaitForExit(5000) || workerProcess.ExitCode != process.ExitCode) throw new InvalidOperationException("Worker/launcher exit mismatch.");
            _ = stderr.GetAwaiter().GetResult();
            closed = true;
        }

        public void Dispose()
        {
            try
            {
                if (!process.HasExited)
                {
                    process.StandardInput.Close();
                    if (!process.WaitForExit(5000))
                    {
                        // Test-owned exact child only. No global Python termination or user process lookup.
                        if (workerProcess is not null) KillOwned(workerProcess);
                        KillOwned(process);
                    }
                }
                _ = stderr.GetAwaiter().GetResult();
            }
            finally { workerProcess?.Dispose(); process.Dispose(); probePin?.Dispose(); sourcePin.Dispose(); }
        }

        private static void KillOwned(Process owned)
        {
            try { if (!owned.HasExited) owned.Kill(); }
            catch (InvalidOperationException) when (owned.HasExited) { }
            if (!owned.WaitForExit(5000)) throw new TimeoutException("Owned test process did not exit.");
        }

        private static int ParentOf(int id)
        {
            using var snapshot = CreateToolhelp32Snapshot(2, 0);
            if (snapshot.IsInvalid) throw new InvalidOperationException("Cannot inspect owned process relationship.");
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>(), Image = string.Empty };
            if (Process32First(snapshot, ref entry))
                do { if (entry.Id == id) return checked((int)entry.ParentId); } while (Process32Next(snapshot, ref entry));
            throw new InvalidOperationException("Owned worker process missing from OS snapshot.");
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry
        {
            public uint Size, Usage, Id;
            public nint Heap;
            public uint Module, Threads, ParentId;
            public int Priority;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Image;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint id);
        [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);
    }
}
