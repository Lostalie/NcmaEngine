using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.Modules;
using Ncma.Modules.Grpc;

internal static partial class Program
{
    private static readonly string[] ClientAssemblies = ["Ncma.Modules", "Ncma.Modules.Grpc", "Grpc.Core.Api",
        "Grpc.Net.Client", "Grpc.Net.Common", "Microsoft.Extensions.Logging.Abstractions", "Microsoft.Extensions.DependencyInjection.Abstractions"];

    private static void RelocatedClient(string repository, string configuration)
    {
        if (configuration is not ("Debug" or "Release")) throw new ArgumentException("Configuration required.");
        using var index = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repository, "out/verification/m8-2", configuration, "client-package.json")));
        var source = Path.GetFullPath(index.RootElement.GetProperty("client").GetString()!);
        Check(source.StartsWith(Path.GetFullPath(Path.Combine(repository, "out/package/modules", configuration)) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase), "Actual optional library package, not test output.");
        var manifestSha = index.RootElement.GetProperty("manifestSha256").GetString()!;
        var output = Path.GetFullPath(Path.Combine(repository, "out/verification/m8-2/relocated-client", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(output);
        var moved = Path.Combine(output, "独立 client"); CopyTree(source, moved);
        var worker = PrepareOwnedBundle(repository);
        var pins = new List<FileStream>();
        try
        {
            // Source *published* client and every dependency are inaccessible throughout the probe.
            // No dependency loading from default ALC/test output/NuGet is permitted by CheckedClientContext.
            foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                pins.Add(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None));
            foreach (var path in new[] { "python/worker/ncma_module_worker.py", "out/modules/python312/Lib/site-packages/grpc/__init__.py",
                         "out/modules/python312/Scripts/python.exe" })
                pins.Add(File.Open(Path.Combine(repository, path), FileMode.Open, FileAccess.Read, FileShare.None));
            var basePython = File.ReadLines(Path.Combine(repository, "out/modules/python312/pyvenv.cfg"))
                .Single(line => line.StartsWith("executable = ", StringComparison.Ordinal))[13..];
            pins.Add(File.Open(basePython, FileMode.Open, FileAccess.Read, FileShare.None));
            foreach (var name in ClientAssemblies)
            {
                // Retain separate failed fixtures; never delete or modify original/published files.
                foreach (var kind in new[] { "missing", "changed" })
                {
                    var negative = Path.Combine(output, kind + "-" + name);
                    CopyClientFixture(moved, negative, kind == "missing" ? name + ".dll" : null);
                    if (kind == "changed")
                    {
                        using var file = File.Open(Path.Combine(negative, name + ".dll"), FileMode.Open, FileAccess.Write);
                        file.Position = file.Length; file.WriteByte(0x7f);
                    }
                    try { using var bad = new CheckedClientContext(negative, manifestSha); throw new Exception("Bad client closure accepted."); }
                    catch (InvalidDataException) { Check(true, kind + " dependency refused before probe/worker launch: " + name); }
                }
            }
            foreach (var kind in new[] { "changed-manifest", "extra-file" })
            {
                var negative = Path.Combine(output, kind); CopyClientFixture(moved, negative, null);
                if (kind == "changed-manifest") File.AppendAllText(Path.Combine(negative, "module-client-manifest.json"), " ");
                else File.WriteAllText(Path.Combine(negative, "unlisted.dll"), "not an approved dependency");
                try { using var bad = new CheckedClientContext(negative, manifestSha); throw new Exception("Bad manifest/closure accepted."); }
                catch (InvalidDataException) { Check(true, kind + " refused before probe/worker launch."); }
            }
            using var loader = new CheckedClientContext(moved, manifestSha);
            try { loader.LoadFromAssemblyName(new AssemblyName("Ncma.Runtime")); throw new Exception("Unexpected dependency resolved."); }
            catch (FileNotFoundException) { Check(true, "Unknown/non-framework assembly cannot fall back to default/test/global resolver."); }
            // SDK-only driver loaded separately; it is deliberately absent from the library manifest.
            // Only BCL strings cross this test ALC boundary, never World or module object references.
            using var driverBytes = File.OpenRead(typeof(Program).Assembly.Location);
            var probe = loader.LoadFromStream(driverBytes);
            string report;
            try { report = (string)probe.GetType("Program", true)!.GetMethod(nameof(RelocatedClientProbe), BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [worker])!; }
            catch (TargetInvocationException error) { throw new Exception("Relocated optional product API probe failed.", error.InnerException); }
            File.WriteAllText(Path.Combine(output, "worker-costs.json"), report);
            using var result = JsonDocument.Parse(report);
            var productChecks = result.RootElement.GetProperty("checks").GetInt32();
            Check(productChecks == 1154, "Actual checked-client request/result/close assertions executed.");
            checks += productChecks;
            var assemblies = loader.VerifyAssemblies(probe);
            Check(assemblies.Length == ClientAssemblies.Length, "Entire seven-assembly optional runtime closure loaded and checked.");
            File.WriteAllText(Path.Combine(output, "loaded-client-assemblies.json"), JsonSerializer.Serialize(assemblies));
            Console.WriteLine($"PASS actual published optional library -> isolated pinned ALC -> real copied Python; {pins.Count} inaccessible source files; retained {output}.");
        }
        finally { foreach (var pin in pins) pin.Dispose(); }
    }

    private static void CopyClientFixture(string source, string destination, string? omit)
    {
        Directory.CreateDirectory(destination);
        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, path);
            if (relative == omit) continue;
            var target = Path.Combine(destination, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target, false);
        }
    }

    // Verification harness ONLY; not a production arbitrary-assembly/plugin loading service.
    private sealed class CheckedClientContext : AssemblyLoadContext, IDisposable
    {
        private readonly string root;
        private readonly Dictionary<string, (string Path, long Size, string Sha)> entries = new(StringComparer.Ordinal);
        private readonly List<FileStream> pins = [];
        private readonly string runtimeRoot = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        internal CheckedClientContext(string root, string manifestSha) : base("checked-optional-module-client", isCollectible: true)
        {
            this.root = Path.GetFullPath(root);
            try
            {
                var manifestPath = Path.Combine(root, "module-client-manifest.json");
                var manifest = Pin(manifestPath);
                if (Convert.ToHexString(SHA256.HashData(manifest)) != manifestSha) throw new InvalidDataException("Client manifest digest changed.");
                manifest.Position = 0;
                using var json = JsonDocument.Parse(manifest);
                var value = json.RootElement;
                if (value.GetProperty("schemaVersion").GetInt32() != 1 || value.GetProperty("product").GetString() != "Ncma.Modules.Grpc" ||
                    value.GetProperty("mode").GetString() != "optional-library") throw new InvalidDataException("Client manifest identity.");
                long bytes = 0;
                foreach (var entry in value.GetProperty("files").EnumerateArray())
                {
                    var relative = entry.GetProperty("path").GetString()!;
                    var path = Path.GetFullPath(Path.Combine(root, relative));
                    if (!path.StartsWith(this.root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        !entries.TryAdd(relative, (path, entry.GetProperty("size").GetInt64(), entry.GetProperty("sha256").GetString()!)))
                        throw new InvalidDataException("Client path outside package/duplicate.");
                    if (entries.Count > 64 || (bytes += entry.GetProperty("size").GetInt64()) > 33554432) throw new InvalidDataException("Client budget.");
                    if (!File.Exists(path)) throw new InvalidDataException("Missing client file: " + relative);
                    var pin = Pin(path);
                    if (pin.Length != entries[relative].Size || Convert.ToHexString(SHA256.HashData(pin)) != entries[relative].Sha)
                        throw new InvalidDataException("Client file hash/size: " + relative);
                }
                if (Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count() != entries.Count + 1 ||
                    !entries.Keys.Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).Order()
                        .SequenceEqual(ClientAssemblies.Select(n => n + ".dll").Order())) throw new InvalidDataException("Client file closure.");
                // Load every declared dependency now, including lazily referenced logging abstractions.
                foreach (var name in ClientAssemblies) LoadFromAssemblyName(AssemblyName.GetAssemblyName(entries[name + ".dll"].Path));
            }
            catch { Dispose(); throw; }
        }
        private FileStream Pin(string path)
        {
            for (var part = new FileInfo(path) as FileSystemInfo; part is not null; part = part is FileInfo file ? file.Directory : ((DirectoryInfo)part).Parent)
                if ((part.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Client link refused.");
            var pin = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read); pins.Add(pin); return pin;
        }
        protected override Assembly? Load(AssemblyName name)
        {
            if (entries.TryGetValue(name.Name + ".dll", out var entry)) return LoadFromAssemblyPath(entry.Path);
            // Only the actual shared .NET framework may be shared. Not Microsoft.Extensions.*,
            // Grpc.*, Ncma.* from the source test app or global NuGet cache.
            var framework = Path.Combine(runtimeRoot, name.Name + ".dll");
            if (File.Exists(framework)) return Default.LoadFromAssemblyPath(framework);
            throw new FileNotFoundException("No optional client fallback: " + name.FullName);
        }
        internal object[] VerifyAssemblies(Assembly probe)
        {
            return Assemblies.Where(a => a != probe).Select(a =>
            {
                var name = a.GetName().Name!;
                if (!entries.TryGetValue(name + ".dll", out var entry) || !string.Equals(a.Location, entry.Path, StringComparison.OrdinalIgnoreCase) ||
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Location))) != entry.Sha)
                    throw new InvalidDataException("Loaded assembly escaped exact client closure.");
                return (object)new { name, path = a.Location, sha256 = entry.Sha };
            }).ToArray();
        }
        public void Dispose() { foreach (var pin in pins) pin.Dispose(); pins.Clear(); Unload(); }
    }

    private static string RelocatedClientProbe(string worker)
    {
        var installation = WorkerInstallation.Inspect(worker);
        var context = new ModuleContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 0, Guid.Empty, 0);
        using var host = new LocalWorkerHost(installation, context, () => true);
        var timer = Stopwatch.StartNew();
        var review = host.ReviewStart(context); host.ApproveStart(review, review.Fingerprint, true, context);
        var samples = new List<object>();
        try
        {
            WaitHost(host, context, LocalWorkerState.Ready);
            var startupMilliseconds = timer.Elapsed.TotalMilliseconds;
            using var process = Process.GetProcessById(host.Capture().ProcessId);
            Check(Path.GetFullPath(process.MainModule!.FileName).Equals(Path.Combine(worker, "python.exe"), StringComparison.OrdinalIgnoreCase), "Copied actual Python image.");
            foreach (var length in new[] { 16, 32000, 65525 })
            {
                var text = new string('x', length);
                var payload = JsonSerializer.SerializeToUtf8Bytes(new { text });
                var durations = new double[64];
                long inputBytes = 0, outputBytes = 0;
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                for (var iteration = 0; iteration < durations.Length; iteration++)
                {
                    var id = Guid.NewGuid(); var watch = Stopwatch.StartNew();
                    Check(host.Submit(id, payload, context) == ModuleAdmission.Accepted, "Copied relocated request admitted.");
                    ModuleOutcome? result = null;
                    Wait(() => { host.Pump(context); return host.TryTake(id, context, out result); });
                    durations[iteration] = watch.Elapsed.TotalMilliseconds;
                    Check(result!.Status == ModuleOutcomeStatus.NeedsDomainValidation, "Copied proposal only.");
                    var output = result.Proposal!.CopyBytes();
                    using var document = JsonDocument.Parse(output);
                    Check(document.RootElement.GetProperty("codePoints").GetInt32() == length, "Python code points match.");
                    Check(document.RootElement.GetProperty("utf8Bytes").GetInt32() == length, "Python byte count matches.");
                    Check(document.RootElement.GetProperty("sha256").GetString() == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))), "Actual cross-process result digest.");
                    Check(!host.TryTake(id, context, out _), "Copied reply consumed once.");
                    inputBytes += payload.Length; outputBytes += output.Length;
                }
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                Array.Sort(durations);
                samples.Add(new { requests = 64, inputPayloadBytes = inputBytes, outputPayloadBytes = outputBytes,
                    pollingRoundTripP50Ms = durations[31], pollingRoundTripP95Ms = durations[60], pollingRoundTripP99Ms = durations[63],
                    ownerThreadTestAndHostAllocatedBytes = allocated });
            }
            process.Refresh();
            var workerWorkingSetBytes = process.WorkingSet64;
            host.Stop(); WaitHost(host, context, LocalWorkerState.Stopped);
            Check(host.Capture().ExitCode == 0 && !host.Capture().ResourcesRetained && host.Capture().StderrBytes == 0, "Copied worker actual graceful close, no leaked owner resources.");
            return JsonSerializer.Serialize(new { checks, startupMilliseconds, workerWorkingSetBytes, samples,
                scope = "192 sequential requests; polling/harness included; payload not HTTP2/TLS wire bytes; owner allocation excludes worker/background threads; not full performance acceptance",
                inferenceImplemented = false, editorIntegrated = false });
        }
        finally { FinishTestHost(host, context); }
    }
}
