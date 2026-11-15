using Ncma.Editor.Services;
using System.Security.Cryptography;

internal static class WorkflowToolchainTests
{
    private static void Check(bool ok) { if (!ok) throw new Exception("M8.6 toolchain observation assertion"); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException or ObjectDisposedException) { return; }
        throw new Exception("Expected toolchain rejection");
    }
    private static (string Tool, string ToolHash) Build(string output, params string[] versions)
    {
        string root = Path.Combine(output, "toolchain-fixture-" + Guid.NewGuid().ToString("N"));
        string sdk = Path.Combine(root, "sdk"); Directory.CreateDirectory(sdk);
        foreach (string version in versions) {
            string directory = Path.Combine(sdk, version);
            Directory.CreateDirectory(Path.Combine(directory, "Roslyn", "bincore"));
            File.WriteAllText(Path.Combine(directory, "MSBuild.dll"), "msbuild " + version);
            File.WriteAllText(Path.Combine(directory, "NuGet.Build.Tasks.dll"), "nuget " + version);
            File.WriteAllText(Path.Combine(directory, "Roslyn", "bincore", "csc.dll"), "csc " + version);
        }
        string tool = Path.Combine(root, "dotnet.exe"); File.WriteAllText(tool, "dotnet");
        return (tool, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(tool))));
    }
    internal static IEnumerable<(string, Action)> Cases(string output)
    {
        yield return ("M8.6 toolchain observation reports the installed SDK and isolates copies", () => {
            var fixture = Build(output, "9.0.315");
            using var toolchain = new WorkflowToolchain(fixture.Tool, fixture.ToolHash, "9.0.315");
            Check(toolchain.SdkVersion == "9.0.315" && toolchain.ToolSha256 == fixture.ToolHash);
            Check(!toolchain.BuildAuthorized && !toolchain.RestoreAuthorized && toolchain.Fingerprint.Length == 64);
            var files = toolchain.CopyFiles();
            Check(files.Length == 4 && files[0].Relative == "dotnet.exe" && files[0].Bytes == 6);
            Check(files[1].Relative == "sdk/9.0.315/MSBuild.dll" && files[3].Relative == "sdk/9.0.315/Roslyn/bincore/csc.dll");
            Check(files.All(f => f.Sha256.Length == 64 && f.Bytes > 0));
            files[0] = files[0] with { Relative = "changed", Sha256 = new string('0', 64) };
            Check(toolchain.CopyFiles()[0].Relative == "dotnet.exe");
        });
        yield return ("M8.6 toolchain observation refuses a declared version that is not observed", () => {
            var fixture = Build(output, "9.0.315");
            Reject(() => new WorkflowToolchain(fixture.Tool, fixture.ToolHash, "9.0.316").Dispose());
            Reject(() => new WorkflowToolchain(fixture.Tool, fixture.ToolHash, "9.0.315.1").Dispose());
        });
        yield return ("M8.6 toolchain observation selects the highest installed stable SDK", () => {
            var fixture = Build(output, "9.0.100", "9.0.315", "8.0.404");
            using var toolchain = new WorkflowToolchain(fixture.Tool, fixture.ToolHash, "9.0.315");
            Check(toolchain.SdkVersion == "9.0.315");
        });
        foreach (int count in new[] { 7, 8 }) {
            yield return ($"M8.6 toolchain observation accepts {count} installed SDKs", () => {
                var fixture = Build(output, Enumerable.Range(1, count).Select(patch => $"9.0.{patch}").ToArray());
                string expected = $"9.0.{count}";
                Check(WorkflowToolchain.InspectInstalledVersion(fixture.Tool) == expected);
                using var toolchain = new WorkflowToolchain(fixture.Tool, fixture.ToolHash, expected);
                Check(toolchain.SdkVersion == expected);
            });
        }
        foreach (int count in new[] { 0, 9 }) {
            yield return ($"M8.6 toolchain observation rejects {count} installed SDKs with the budget error", () => {
                var fixture = Build(output, Enumerable.Range(1, count).Select(patch => $"9.0.{patch}").ToArray());
                void ExpectBudget(Action action)
                {
                    try { action(); }
                    catch (IOException error) when (error.Message == "workflow_toolchain_sdk_budget") { return; }
                    throw new Exception("Expected SDK count budget rejection");
                }
                ExpectBudget(() => WorkflowToolchain.InspectInstalledVersion(fixture.Tool));
                ExpectBudget(() => new WorkflowToolchain(fixture.Tool, fixture.ToolHash, $"9.0.{count}").Dispose());
            });
        }
        yield return ("M8.6 toolchain observation prefers the release SDK over an equal-numbered preview", () => {
            var fixture = Build(output, "9.0.315-preview.1.24000.1", "9.0.315");
            using var toolchain = new WorkflowToolchain(fixture.Tool, fixture.ToolHash, "9.0.315");
            Check(toolchain.SdkVersion == "9.0.315");
        });
        yield return ("M8.6 toolchain observation refuses two indistinguishable SDK builds", () => {
            var fixture = Build(output, "9.0.315-alpha", "9.0.315-beta");
            Reject(() => new WorkflowToolchain(fixture.Tool, fixture.ToolHash, "9.0.315-alpha").Dispose());
        });
        yield return ("M8.6 toolchain observation refuses missing components and a foreign tool name", () => {
            var fixture = Build(output, "9.0.315");
            File.Delete(Path.Combine(Path.GetDirectoryName(fixture.Tool)!, "sdk", "9.0.315", "Roslyn", "bincore", "csc.dll"));
            Reject(() => new WorkflowToolchain(fixture.Tool, fixture.ToolHash, "9.0.315").Dispose());
            var other = Build(output, "9.0.315");
            string msbuild = Path.Combine(Path.GetDirectoryName(other.Tool)!, "msbuild.exe"); File.WriteAllText(msbuild, "x");
            Reject(() => new WorkflowToolchain(msbuild, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(msbuild))), "9.0.315").Dispose());
            Reject(() => new WorkflowToolchain(other.Tool, new string('A', 64), "9.0.315").Dispose());
        });
        yield return ("M8.6 toolchain observation is owner-thread only and releases handles on close", () => {
            var fixture = Build(output, "9.0.315");
            var toolchain = new WorkflowToolchain(fixture.Tool, fixture.ToolHash, "9.0.315");
            System.Threading.Tasks.Task.Run(() => Reject(() => toolchain.CopyFiles())).GetAwaiter().GetResult();
            toolchain.Dispose();
            Reject(() => toolchain.CopyFiles());
            foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(fixture.Tool)!, "*", SearchOption.AllDirectories))
                using (var released = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Check(released.Length > 0);
        });
    }
}
