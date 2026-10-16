using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Ncma.Modules;
using Ncma.Modules.Grpc;

internal static partial class Program
{
    private static void InstallationRefusals(string repository)
    {
        var before = checks;
        var root = Path.GetFullPath(Path.Combine(repository, "out/verification/m8-2/install-refusals", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        void Reject(Action action, string label)
        {
            try { action(); throw new Exception("Accepted invalid installation: " + label); }
            catch (Exception error) when (error is ArgumentException or IOException or InvalidDataException or OperationCanceledException)
            { Check(true, label); }
        }
        foreach (var path in new[] { "", "relative", "F:\\", "\\\\localhost\\not-a-worker", root + ":stream", new string('x', 1025) })
            Reject(() => WorkerInstallation.Inspect(path), "Nonlocal/ambiguous/oversized root rejected.");
        Reject(() => WorkerInstallation.Inspect(root), "Missing interpreter/closure rejected.");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            Reject(() => WorkerInstallation.Inspect(root, cancelled.Token), "Cancelled inspection does not scan.");
        }
        string Minimal(string name)
        {
            var directory = Path.Combine(root, name); Directory.CreateDirectory(Path.Combine(directory, "site-packages/grpc"));
            foreach (var path in new[] { "python.exe", "python312.dll", "python312.zip", "ncma_module_worker.py",
                "site-packages/grpc/__init__.py", "site-packages/typing_extensions.py" })
                File.WriteAllText(Path.Combine(directory, path), "unexecuted test placeholder", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "python312._pth"), WorkerInstallation.PythonPathFile, new UTF8Encoding(false));
            return directory;
        }
        var wrongPath = Minimal("search-path");
        File.AppendAllText(Path.Combine(wrongPath, "python312._pth"), "import site\n");
        Reject(() => WorkerInstallation.Inspect(wrongPath), "Extra search path/site import rejected.");
        var badUtf8 = Minimal("utf8-path"); File.WriteAllBytes(Path.Combine(badUtf8, "python312._pth"), [255, 254]);
        Reject(() => WorkerInstallation.Inspect(badUtf8), "Malformed path file rejected without unbounded decode.");
        var venv = Minimal("venv"); File.WriteAllText(Path.Combine(venv, "pyvenv.cfg"), "home = forbidden");
        Reject(() => WorkerInstallation.Inspect(venv), "Venv/global interpreter routing rejected.");
        var big = Minimal("byte-budget");
        using (var file = File.Open(Path.Combine(big, "oversize.bin"), FileMode.CreateNew, FileAccess.Write)) file.SetLength(WorkerInstallation.MaximumBytes + 1);
        Reject(() => WorkerInstallation.Inspect(big), "Byte budget checked before hash/allocation.");
        var files = Minimal("file-budget");
        for (var i = 0; i <= WorkerInstallation.MaximumFiles; i++)
            using (File.Open(Path.Combine(files, $"extra-{i:D4}"), FileMode.CreateNew, FileAccess.Write)) { }
        Reject(() => WorkerInstallation.Inspect(files), "File inventory count remains bounded.");
        var directories = Minimal("directory-budget");
        for (var i = 0; i <= WorkerInstallation.MaximumFiles; i++) Directory.CreateDirectory(Path.Combine(directories, $"d-{i:D4}"));
        Reject(() => WorkerInstallation.Inspect(directories), "Directory traversal count remains bounded.");
        var hardlink = Minimal("hardlink");
        Check(CreateHardLinkW(Path.Combine(hardlink, "alias.py"), Path.Combine(hardlink, "ncma_module_worker.py"), 0), "Created link only between generated private fixture files.");
        Reject(() => WorkerInstallation.Inspect(hardlink), "Hard-linked executable content rejected.");
        Console.WriteLine($"PASS bounded installation refusal matrix: {checks - before} checks; retained {root}.");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string name, string existing, nint security);

    private static void WaitHost(LocalWorkerHost host, ModuleContext context, params LocalWorkerState[] expected)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            host.Pump(context);
            var current = host.Capture();
            if (expected.Contains(current.State)) return;
            // Test bound covers the product's 10s startup + 5s process + two 2s pipe guards.
            if (watch.Elapsed > TimeSpan.FromSeconds(25)) throw new TimeoutException("Host lifecycle test deadline: " + current);
            Thread.Sleep(1);
        }
    }

    private static void FinishTestHost(LocalWorkerHost host, ModuleContext context)
    {
        // TEST cleanup of this fixture's exact owner. Production never auto-approves termination.
        host.Stop(); WaitHost(host, context, LocalWorkerState.Stopped, LocalWorkerState.FailedRetained);
        if (host.Capture().State == LocalWorkerState.FailedRetained)
        {
            Console.WriteLine("Test cleanup retains failure evidence: " + host.Capture());
            var review = host.ReviewRecovery(true); host.ApproveRecovery(review, review.Fingerprint, true);
            WaitHost(host, context, LocalWorkerState.Stopped);
        }
        host.Dispose();
    }

    private static void HostFaults(string repository)
    {
        var before = checks;
        var bundle = PrepareOwnedBundle(repository);
        var script = Path.Combine(bundle, "ncma_module_worker.py");
        var original = File.ReadAllText(script);
        var context = new ModuleContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 0, Guid.Empty, 0);
        void Fault(string program, Action<LocalWorkerHost> test)
        {
            File.WriteAllText(script, program, new UTF8Encoding(false));
            var host = new LocalWorkerHost(WorkerInstallation.Inspect(bundle), context, () => true);
            try
            {
                var review = host.ReviewStart(context); host.ApproveStart(review, review.Fingerprint, true, context);
                test(host);
            }
            finally { FinishTestHost(host, context); }
        }
        Fault("import sys\nsys.stdin.buffer.readline()\nsys.stdin.buffer.read(1)\n", host =>
        {
            var started = Stopwatch.StartNew();
            WaitHost(host, context, LocalWorkerState.Stopped);
            Check(host.Capture().Fault == "startup_cancelled_or_timed_out" && started.Elapsed >= TimeSpan.FromSeconds(8),
                "Real silent startup expires through product deadline, not a test cancellation.");
            Check(host.Capture().ExitCode == 0 && !host.Capture().ResourcesRetained, "Deadline closes owned stdin and observes actual process exit.");
        });
        Fault("import sys,json\nb=json.loads(sys.stdin.buffer.readline())\nprint(json.dumps({'version':1,'instance':b['instance'],'port':1,'processId':" +
            Environment.ProcessId + "}),flush=True)\nsys.stdin.buffer.read(1)\n", host =>
        {
            WaitHost(host, context, LocalWorkerState.Stopped);
            Check(host.Capture().Fault == "startup_failed" && host.Capture().ProcessId != Environment.ProcessId && host.Capture().ExitCode == 0,
                "Worker cannot substitute caller PID for exact owned process.");
        });
        string Inject(string trustedTestCode) => original.Replace("if __name__ == \"__main__\":",
            trustedTestCode + "\nif __name__ == \"__main__\":", StringComparison.Ordinal);
        Fault(Inject("""
            _test_original = Worker.calculate
            async def _test_noisy(self, text, utf8):
                sys.stderr.write('SECRET_TEST_' + 'x' * 10000)
                sys.stderr.flush()
                return await _test_original(self, text, utf8)
            Worker.calculate = _test_noisy
            """), host =>
        {
            WaitHost(host, context, LocalWorkerState.Ready);
            var id = Guid.NewGuid(); Check(host.Submit(id, "{\"text\":\"noise\"}"u8, context) == ModuleAdmission.Accepted, "Output probe admitted.");
            WaitHost(host, context, LocalWorkerState.Stopped);
            var snapshot = host.Capture();
            Check(snapshot.Fault == "worker_output_fault" && snapshot.StderrBytes >= 10000, "Actual over-budget stderr invalidates host.");
            Check(!JsonSerializer.Serialize(snapshot).Contains("SECRET_TEST_", StringComparison.Ordinal), "Diagnostics retain no secret-bearing raw output.");
            Check(!host.TryTake(id, context, out _), "Output-faulted session cannot yield a late proposal.");
        });
        Fault(Inject("""
            _test_factory = grpc.aio.server
            _test_server = None
            def _test_server_factory(*args, **kwargs):
                global _test_server
                _test_server = _test_factory(*args, **kwargs)
                return _test_server
            grpc.aio.server = _test_server_factory
            async def _test_disconnect(self, raw, context):
                await self.authenticate(context)
                asyncio.create_task(_test_server.stop(0))
                await asyncio.Event().wait()
            Worker.invoke = _test_disconnect
            """), host =>
        {
            WaitHost(host, context, LocalWorkerState.Ready);
            var id = Guid.NewGuid(); Check(host.Submit(id, "{\"text\":\"disconnect\"}"u8, context) == ModuleAdmission.Accepted, "Disconnect probe admitted.");
            WaitHost(host, context, LocalWorkerState.Stopped);
            Check(host.Capture().Fault == "worker_connection_lost" && host.Capture().ExitCode == 0, "Actual server shutdown invalidates connection while owner closes surviving process.");
            Check(!host.TryTake(id, context, out _), "Disconnected session cannot yield late replies.");
        });
        // Fixed safe-boundary context change closes the existing session, not only queued input.
        Fault(original, host =>
        {
            WaitHost(host, context, LocalWorkerState.Ready);
            var id = Guid.NewGuid(); Check(host.Submit(id, "{\"text\":\"stale\"}"u8, context) == ModuleAdmission.Accepted, "Context probe admitted.");
            var replacement = context with { DocumentId = Guid.NewGuid(), Generation = 2 };
            WaitHost(host, replacement, LocalWorkerState.Stopped);
            Check(!host.TryTake(id, replacement, out _) && host.Capture().Code == "context_changed", "Document replacement revokes actual worker session.");
        });
        Console.WriteLine($"PASS actual startup timeout/output/PID/disconnect/context refusals: {checks - before} checks; retained {bundle}.");
    }
}
