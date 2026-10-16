using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static partial class Program
{
    private static void NoPythonProducts(string repository, string configuration)
    {
        if (configuration is not ("Debug" or "Release")) throw new ArgumentException("Configuration required.");
        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository, "out/verification/m2-7", configuration, "packages.json")));
        var output = Path.GetFullPath(Path.Combine(repository, "out/verification/m8-2/no-python-products", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(output);
        var locks = new List<FileStream>();
        try
        {
            // Lock known source interpreters when installed; this test also works if absent.
            // This is NOT an assertion that the target machine has no other Python installation.
            var venv = Path.Combine(repository, "out/modules/python312");
            var config = Path.Combine(venv, "pyvenv.cfg");
            var sourceInterpreters = new List<string> { Path.Combine(venv, "Scripts/python.exe") };
            if (File.Exists(config)) sourceInterpreters.Add(File.ReadLines(config).Single(s => s.StartsWith("executable = ", StringComparison.Ordinal))[13..]);
            foreach (var path in sourceInterpreters.Distinct(StringComparer.OrdinalIgnoreCase))
                if (File.Exists(path)) locks.Add(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None));
            foreach (var product in new[] { "editor", "playerNull" })
            {
                var source = Path.GetFullPath(index.RootElement.GetProperty(product).GetString()!);
                Check(source.StartsWith(Path.GetFullPath(Path.Combine(repository, "out/package")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Package source is within checked candidate output.");
                var moved = Path.Combine(output, product + " 无 Python"); CopyTree(source, moved);
                using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(moved, "deployment-manifest.json")));
                foreach (var entry in manifest.RootElement.GetProperty("files").EnumerateArray())
                {
                    var relative = entry.GetProperty("path").GetString()!;
                    var path = Path.GetFullPath(Path.Combine(moved, relative));
                    Check(path.StartsWith(moved + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Manifest path contained.");
                    using var file = File.OpenRead(path);
                    Check(file.Length == entry.GetProperty("size").GetInt64() && Convert.ToHexString(SHA256.HashData(file)) == entry.GetProperty("sha256").GetString(), "Relocated product exact manifest hash.");
                }
                foreach (var path in Directory.EnumerateFiles(moved, "*", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileName(path);
                    Check(!name.StartsWith("Ncma.Modules", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("grpc", StringComparison.OrdinalIgnoreCase) &&
                        !name.StartsWith("python", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".py", StringComparison.OrdinalIgnoreCase), "Default product does not deploy optional worker/runtime/transport.");
                    if (name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
                    {
                        var deps = File.ReadAllText(path);
                        Check(!deps.Contains("Ncma.Modules", StringComparison.Ordinal) && !deps.Contains("Grpc.Net.Client", StringComparison.Ordinal), "Default apphost dependency closure stays independent.");
                    }
                }
                if (product == "editor")
                {
                    RunNoPython(Path.Combine(moved, "NcmaEngine.exe"), output, "--validate-package");
                    RunNoPython(Path.Combine(moved, "NcmaEngine.exe"), output, "--smoke-test");
                }
                else
                {
                    var report = Path.Combine(output, "null-report.json");
                    RunNoPython(Path.Combine(moved, "NcmaPlayer.exe"), output, "--project", Path.Combine(moved, "sample/sample.ncmaproject"),
                        "--headless", "--ticks", "2", "--report", report);
                    using var document = JsonDocument.Parse(File.ReadAllText(report));
                    Check(document.RootElement.GetProperty("exitCode").GetInt32() == 0 && document.RootElement.GetProperty("tick").GetInt64() == 2 &&
                        document.RootElement.GetProperty("modules").GetArrayLength() == 0, "Actual relocated Null player executes exactly two ticks without native/Python modules.");
                }
            }
            Console.WriteLine($"PASS relocated default Editor/Null apphosts with minimal environment, no transport package and {locks.Count} source interpreter locks; evidence {output}.");
        }
        finally { foreach (var file in locks) file.Dispose(); }
    }

    private static void RunNoPython(string executable, string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment.Clear();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        start.Environment["SystemRoot"] = windows; start.Environment["WINDIR"] = windows;
        start.Environment["PATH"] = Path.Combine(windows, "System32");
        start.Environment["TEMP"] = directory; start.Environment["TMP"] = directory;
        start.Environment["USERPROFILE"] = directory; start.Environment["LOCALAPPDATA"] = directory;
        using var process = Process.Start(start) ?? throw new IOException("Product child did not start.");
        _ = process.Handle;
        var stdout = BoundedProductOutput(process.StandardOutput.BaseStream);
        var stderr = BoundedProductOutput(process.StandardError.BaseStream);
        if (!process.WaitForExit(30000))
        {
            process.Kill(entireProcessTree: false); process.WaitForExit(5000);
            throw new TimeoutException("Owned product child timeout.");
        }
        var diagnostics = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        var log = Path.Combine(directory, "apphost-" + Guid.NewGuid().ToString("N") + ".log");
        File.WriteAllText(log, diagnostics);
        Check(process.ExitCode == 0, "Actual minimal-environment apphost failed; evidence " + log + ": " + diagnostics);
    }

    private static async Task<string> BoundedProductOutput(Stream stream)
    {
        using var output = new MemoryStream(); var buffer = new byte[4096];
        while (true)
        {
            var count = await stream.ReadAsync(buffer);
            if (count == 0) return Encoding.UTF8.GetString(output.ToArray());
            if (output.Length + count > 262144) throw new InvalidDataException("Product test diagnostic budget.");
            output.Write(buffer, 0, count);
        }
    }
}
