using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Ncma.Modules;
using Ncma.Modules.Grpc;

internal static partial class Program
{
    private sealed class ReviewClock : TimeProvider
    {
        private long value;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Interlocked.Read(ref value);
        internal void Advance(long milliseconds) => Interlocked.Add(ref value, milliseconds);
    }

    private static void OwnedHost(string repository)
    {
        var before = checks;
        var bundle = PrepareOwnedBundle(repository);
        Console.WriteLine("Retained test installation: " + bundle);
        var installation = WorkerInstallation.Inspect(bundle);
        Check(installation.FileCount > 50 && installation.Bytes > 10_000_000, "Real isolated Python/stdlib/grpc installation inspected.");
        var copied = installation.CopyFiles(); var expectedFirst = copied[0];
        copied[0] = copied[0] with { Sha256 = new string('A', 64) };
        Check(installation.CopyFiles()[0] == expectedFirst, "Installation approval inventory is copied.");
        var context = new ModuleContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 0, Guid.Empty, 0);
        var allowed = true; var clock = new ReviewClock();
        using (var host = new LocalWorkerHost(installation, context, () => allowed, clock))
        {
            void Reject(Action action, string label)
            {
                try { action(); throw new Exception("Expected refusal: " + label); }
                catch (InvalidOperationException) { Check(true, label); }
            }
            void Until(LocalWorkerState state)
            {
                Wait(() => { host.Pump(context); return host.Capture().State == state; });
            }
            void Start()
            {
                var review = host.ReviewStart(context);
                host.ApproveStart(review, review.Fingerprint, true, context);
                Wait(() => { host.Pump(context); return host.Capture().State != LocalWorkerState.Starting; });
                Check(host.Capture().State == LocalWorkerState.Ready, "Approved startup ready: " + host.Capture());
            }
            Check(host.Capture().ProcessId == 0 && host.Capture().State == LocalWorkerState.Stopped, "Passive host does not launch.");
            Check(host.Submit(Guid.NewGuid(), "{\"text\":\"passive\"}"u8, context) == ModuleAdmission.Stopped, "No implicit launch from a request.");
            var review = host.ReviewStart(context);
            Check(review.InstallationRoot == bundle && review.Files == installation.FileCount && review.Bytes == installation.Bytes,
                "Human review exposes exact installation root, byte/file budget and fingerprint.");
            Reject(() => host.ApproveStart(review, review.Fingerprint, false, context), "Human review required.");
            review = host.ReviewStart(context);
            Reject(() => host.ApproveStart(review, "wrong", true, context), "Exact fingerprint required.");
            review = host.ReviewStart(context);
            Reject(() => host.ApproveStart(review, review.Fingerprint, true, context with { Revision = 1 }), "Exact context required.");
            review = host.ReviewStart(context);
            clock.Advance(60000);
            Reject(() => host.ApproveStart(review, review.Fingerprint, true, context), "60-second review expiry.");
            review = host.ReviewStart(context); host.Revoke();
            Reject(() => host.ApproveStart(review, review.Fingerprint, true, context), "Revoked review cannot resurrect.");
            allowed = false;
            Reject(() => host.ReviewStart(context), "Read-only permission refuses review.");
            allowed = true;
            review = host.ReviewStart(context); allowed = false;
            Reject(() => host.ReviewStart(context), "Denied permission observation revokes pending review.");
            allowed = true;
            Reject(() => host.ApproveStart(review, review.Fingerprint, true, context), "Restored permission does not resurrect old review.");
            var ownerRejected = Task.Run(() => { try { host.Capture(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult();
            Check(ownerRejected, "Owner thread only.");
            try
            {
                // Source installation and original dependency code cannot be reopened by the child.
                using var originalSource = File.Open(Path.Combine(repository, "python/worker/ncma_module_worker.py"), FileMode.Open, FileAccess.Read, FileShare.None);
                using var originalGrpc = File.Open(Path.Combine(repository, "out/modules/python312/Lib/site-packages/grpc/__init__.py"), FileMode.Open, FileAccess.Read, FileShare.None);
                Start();
                var first = host.Capture();
                Check(first.ProcessId > 0 && first.ProcessId != Environment.ProcessId && first.ResourcesRetained, "Actual owned direct Python process.");
                try
                {
                    using var write = File.Open(Path.Combine(bundle, "ncma_module_worker.py"), FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                    throw new Exception("Running approved worker file was writable.");
                }
                catch (IOException) { Check(true, "Approved files stay pinned through process lifetime."); }
                var id = Guid.NewGuid();
                Check(host.Submit(id, "{\"text\":\"真正的独立模块 🤖\"}"u8, context) == ModuleAdmission.Accepted, "Owned host accepts copied input.");
                ModuleOutcome? result = null;
                Wait(() => host.TryTake(id, context, out result));
                Check(result!.Status == ModuleOutcomeStatus.NeedsDomainValidation, "Owned host returns proposal only.");
                var pending = Guid.NewGuid();
                Check(host.Submit(pending, "{\"text\":\"never replay\"}"u8, context) == ModuleAdmission.Accepted, "Pre-crash request admitted.");
                using (var owned = Process.GetProcessById(first.ProcessId))
                {
                    _ = owned.Handle;
                    Check(string.Equals(Path.GetFullPath(owned.MainModule!.FileName), Path.GetFullPath(Path.Combine(bundle, "python.exe")),
                        StringComparison.OrdinalIgnoreCase), "Fault injection targets the exact verified test installation.");
                    owned.Kill(entireProcessTree: false);
                    Check(owned.WaitForExit(5000), "Owned crash test process terminated.");
                }
                Until(LocalWorkerState.Stopped);
                Check(!host.TryTake(pending, context, out _) && host.Capture().Fault == "worker_exited", "Crash revokes old session/results.");
                Start();
                Check(host.Capture().Instance != first.Instance && host.CopyHistory().Length == 1 && host.CopyHistory()[0].Fault == "worker_exited",
                    "Explicit restart changes instance and retains failure record.");
                Check(!host.TryTake(pending, context, out _), "Restart does not replay prior requests.");
                Check(host.Submit(pending, "{\"text\":\"explicit new request\"}"u8, context) == ModuleAdmission.Accepted, "Only explicit new submission enters new session.");
                Wait(() => host.TryTake(pending, context, out result));
                Check(result!.Status == ModuleOutcomeStatus.NeedsDomainValidation, "New instance healthy.");
                host.Stop(); Until(LocalWorkerState.Stopped);
                Check(!host.Capture().ResourcesRetained && host.Capture().ExitCode == 0, "Normal EOF closes process and all pins.");
                Start(); allowed = false; Until(LocalWorkerState.Stopped); allowed = true;
                Check(host.Capture().Code == "revoked", "Permission revoke closes active host.");
                Reject(() => host.ReviewStart(context), "Three-attempt lifetime bound, no infinite restart.");
                Check(host.CopyHistory().Length == 2, "Bounded historical attempt evidence.");
            }
            finally { allowed = true; host.Stop(); Until(LocalWorkerState.Stopped); }
        }

        // Generated fixture only: old approval must reject changed bytes BEFORE process launch.
        var script = Path.Combine(bundle, "ncma_module_worker.py");
        File.AppendAllText(script, "\n# changed generated test installation\n", new UTF8Encoding(false));
        using (var changed = new LocalWorkerHost(installation, context, () => true))
        {
            var review = changed.ReviewStart(context); changed.ApproveStart(review, review.Fingerprint, true, context);
            Wait(() => { changed.Pump(context); return changed.Capture().State == LocalWorkerState.Stopped; });
            Check(changed.Capture().ProcessId == 0 && changed.Capture().Fault == "startup_failed" && !changed.Capture().ResourcesRetained,
                "Changed installation rejected before process creation; acquired pins released.");
        }

        // Exact explicitly inspected test code deliberately ignores EOF AFTER closing gRPC.
        File.AppendAllText(script, "\nimport time\nwhile True:\n    time.sleep(1)\n", new UTF8Encoding(false));
        var stuckInstallation = WorkerInstallation.Inspect(bundle);
        Check(stuckInstallation.Fingerprint != installation.Fingerprint, "Changed code needs a new exact installation approval.");
        var recoveryClock = new ReviewClock();
        using (var stuck = new LocalWorkerHost(stuckInstallation, context, () => true, recoveryClock))
        {
            var review = stuck.ReviewStart(context); stuck.ApproveStart(review, review.Fingerprint, true, context);
            Wait(() => { stuck.Pump(context); return stuck.Capture().State == LocalWorkerState.Ready; });
            var identity = stuck.Capture();
            stuck.Stop();
            Wait(() => { stuck.Pump(context); return stuck.Capture().State == LocalWorkerState.FailedRetained; });
            Check(stuck.Capture().Instance == identity.Instance && stuck.Capture().ResourcesRetained && stuck.Capture().ExitCode is null,
                "Uncooperative process is retained, not falsely unloaded or silently killed.");
            try { stuck.Dispose(); throw new Exception("Retained host was disposed."); }
            catch (InvalidOperationException) { Check(true, "Dispose cannot abandon owned process."); }
            var recovery = stuck.ReviewRecovery(terminate: true);
            try { stuck.ApproveRecovery(recovery, recovery.Fingerprint, false); throw new Exception("Termination without review."); }
            catch (InvalidOperationException) { Check(true, "Termination needs distinct local review."); }
            recovery = stuck.ReviewRecovery(terminate: true);
            recoveryClock.Advance(60000);
            try { stuck.ApproveRecovery(recovery, recovery.Fingerprint, true); throw new Exception("Expired termination review."); }
            catch (InvalidOperationException) { Check(true, "Retained-process review also expires after 60 seconds."); }
            recovery = stuck.ReviewRecovery(terminate: true);
            stuck.ApproveRecovery(recovery, recovery.Fingerprint, true);
            Wait(() => { stuck.Pump(context); return stuck.Capture().State == LocalWorkerState.Stopped; });
            Check(!stuck.Capture().ResourcesRetained && stuck.Capture().ExitCode is not null && stuck.Capture().Fault == "process_or_output_drain_failed",
                "Approved exact-handle termination completes without erasing failure evidence.");
        }
        // Generated standalone fixture only: bounded malformed readiness must not leave a pipe
        // stalled or an untracked child. No product switch introduces this behavior.
        var actualScript = File.ReadAllText(Path.Combine(repository, "python/worker/ncma_module_worker.py"));
        File.WriteAllText(script, "import sys\nsys.stdout.write('x' * 5000 + '\\n')\nsys.stdout.flush()\n" + actualScript, new UTF8Encoding(false));
        using (var noisy = new LocalWorkerHost(WorkerInstallation.Inspect(bundle), context, () => true))
        {
            var review = noisy.ReviewStart(context); noisy.ApproveStart(review, review.Fingerprint, true, context);
            Wait(() => { noisy.Pump(context); return noisy.Capture().State == LocalWorkerState.Stopped; });
            Check(noisy.Capture().Fault == "startup_failed" && noisy.Capture().StdoutBytes > 0 && !noisy.Capture().ResourcesRetained,
                "Overlong ready line rejected; remaining output drained and process closed.");
        }
        File.WriteAllText(script, actualScript, new UTF8Encoding(false));
        using (var cancelled = new LocalWorkerHost(WorkerInstallation.Inspect(bundle), context, () => true))
        {
            var review = cancelled.ReviewStart(context); cancelled.ApproveStart(review, review.Fingerprint, true, context);
            cancelled.Revoke();
            Wait(() => { cancelled.Pump(context); return cancelled.Capture().State == LocalWorkerState.Stopped; });
            Check(!cancelled.Capture().ResourcesRetained && cancelled.Capture().Code == "revoked", "Startup cancellation retains then drains all resources.");
        }
        // Permission callback reentry cannot manufacture approval or access mutable owner state.
        LocalWorkerHost? recursive = null;
        recursive = new LocalWorkerHost(WorkerInstallation.Inspect(bundle), context, () => { recursive!.Capture(); return true; });
        using (recursive)
        {
            try { recursive.ReviewStart(context); throw new Exception("Reentrant permission callback was accepted."); }
            catch (InvalidOperationException) { Check(true, "Permission callback reentry fails closed without process launch."); }
            Check(recursive.Capture().ProcessId == 0, "No process from reentrant approval.");
        }
        Console.WriteLine($"PASS approved exact installation/owned process/crash/restart/retained recovery: {checks - before} checks.");
    }

    private static string PrepareOwnedBundle(string repository)
    {
        // Generated test distribution, not an installed shipping product or redistribution claim.
        var sourcePython = File.ReadLines(Path.Combine(repository, "out/modules/python312/pyvenv.cfg"))
            .Single(line => line.StartsWith("executable = ", StringComparison.Ordinal))[13..];
        var pythonRoot = Path.GetDirectoryName(sourcePython)!;
        var destination = Path.GetFullPath(Path.Combine(repository, "out/verification/m8-2/owned-worker", Guid.NewGuid().ToString("N"), "独立 worker"));
        Directory.CreateDirectory(destination);
        foreach (var file in new[] { "python.exe", "python3.dll", "python312.dll", "vcruntime140.dll", "vcruntime140_1.dll", "LICENSE.txt" })
            File.Copy(Path.Combine(pythonRoot, file), Path.Combine(destination, file), overwrite: false);
        CopyTree(Path.Combine(pythonRoot, "DLLs"), Path.Combine(destination, "DLLs"));
        using (var stream = File.Open(Path.Combine(destination, "python312.zip"), FileMode.CreateNew, FileAccess.Write))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var library = Path.Combine(pythonRoot, "Lib");
            var pending = new Queue<string>(); pending.Enqueue(library);
            while (pending.TryDequeue(out var directory))
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Test source link not allowed.");
                    if (Directory.Exists(path))
                    {
                        if (Path.GetFileName(path) is "site-packages" or "__pycache__" or "test" or "tests" or "idlelib" or "tkinter" or "turtledemo" or "ensurepip") continue;
                        pending.Enqueue(path);
                    }
                    else if (!path.EndsWith(".pyc", StringComparison.OrdinalIgnoreCase))
                        zip.CreateEntryFromFile(path, Path.GetRelativePath(library, path).Replace('\\', '/'), CompressionLevel.Fastest);
                }
            }
        }
        var site = Path.Combine(repository, "out/modules/python312/Lib/site-packages");
        foreach (var name in new[] { "grpc", "grpcio-1.84.0.dist-info", "typing_extensions-4.16.0.dist-info" })
            CopyTree(Path.Combine(site, name), Path.Combine(destination, "site-packages", name));
        File.Copy(Path.Combine(site, "typing_extensions.py"), Path.Combine(destination, "site-packages/typing_extensions.py"), false);
        File.Copy(Path.Combine(repository, "python/worker/ncma_module_worker.py"), Path.Combine(destination, "ncma_module_worker.py"), false);
        File.WriteAllText(Path.Combine(destination, "python312._pth"), WorkerInstallation.PythonPathFile, new UTF8Encoding(false));
        return destination;
    }

    private static void CopyTree(string source, string destination)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException("Test source link not allowed.");
        Directory.CreateDirectory(destination);
        foreach (var path in Directory.EnumerateFileSystemEntries(source))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Test source link not allowed.");
            if (Directory.Exists(path))
            { if (Path.GetFileName(path) != "__pycache__") CopyTree(path, Path.Combine(destination, Path.GetFileName(path))); }
            else if (!path.EndsWith(".pyc", StringComparison.OrdinalIgnoreCase)) File.Copy(path, Path.Combine(destination, Path.GetFileName(path)), false);
        }
    }
}
