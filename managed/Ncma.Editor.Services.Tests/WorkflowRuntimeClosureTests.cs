using System.Text;
using System.Text.Json;
using Ncma.Editor.Services;

// Synthetic closure/IO checks, NOT evidence that a real MCP/native job ran.
internal static class WorkflowRuntimeClosureTests
{
    private static readonly string H = new('A', 64);
    private static void Check(bool value) { if (!value) throw new Exception("M8.6 managed host closure assertion"); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or JsonException) { return; }
        throw new Exception("Expected managed runtime rejection");
    }
    private static byte[] Deps(string project, bool dependency = false) => JsonSerializer.SerializeToUtf8Bytes(new {
        runtimeTarget = new { name = "net8" }, targets = new Dictionary<string, object> {
            ["net8"] = new Dictionary<string, object> { [project + "/1.0.0"] = new {
                runtime = dependency ? new Dictionary<string, object> { [project + ".dll"] = new { }, ["Dependency.dll"] = new { } }
                    : new Dictionary<string, object> { [project + ".dll"] = new { } } } } } });
    private static WorkflowEvidenceFile[] Files(string project, bool executable) =>
        (executable ? new[] { project + ".dll", project + ".deps.json", project + ".runtimeconfig.json" } : new[] { project + ".dll", project + ".deps.json" })
        .Select(p => new WorkflowEvidenceFile(p, 1, H)).ToArray();
    private static WorkflowVerificationTask Task(string recipe, string[] paths) => new(new(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), H,
        new(Guid.NewGuid(), 1, 1), Guid.NewGuid(), H, H, H, null, recipe, "Debug", "default", "fixture-sdk", H),
        paths.Select(p => new WorkflowEvidenceFile(p, 1, H)), WorkflowTestInventory.Copy(recipe));
    internal static IEnumerable<(string, Action)> Cases(string output)
    {
        yield return ("M8.6-C3 MCP executable and Gameplay library have distinct exact required files", () => {
            WorkflowManagedRuntime.Validate("Ncma.Editor.Mcp", true, Files("Ncma.Editor.Mcp", true), Deps("Ncma.Editor.Mcp"));
            WorkflowManagedRuntime.Validate("Ncma.Gameplay.Sample", false, Files("Ncma.Gameplay.Sample", false), Deps("Ncma.Gameplay.Sample"));
            Reject(() => WorkflowManagedRuntime.Validate("Ncma.Editor.Mcp", true, Files("Ncma.Editor.Mcp", false), Deps("Ncma.Editor.Mcp")));
            Reject(() => WorkflowManagedRuntime.Validate("Ncma.Editor.Mcp", true, Files("Ncma.Editor.Mcp", true).Select(f => f with { Bytes = 0 }).ToArray(), Deps("Ncma.Editor.Mcp")));
            Reject(() => WorkflowManagedRuntime.Validate("Other", true, [], Deps("Other")));
        });
        yield return ("M8.6-C3 missing declared managed dependency never falls back to installed files", () => {
            var files = Files("Ncma.Editor.Mcp", true); var deps = Deps("Ncma.Editor.Mcp", true);
            Reject(() => WorkflowManagedRuntime.Validate("Ncma.Editor.Mcp", true, files, deps));
            WorkflowManagedRuntime.Validate("Ncma.Editor.Mcp", true, [.. files, new("Dependency.dll", 1, H)], deps);
            Reject(() => WorkflowManagedRuntime.Validate("Ncma.Editor.Mcp", true, [.. files, new("nested/Dependency.dll", 1, H)], deps));
        });
        yield return ("M8.6-C3 empty foreign duplicate or RID-dependent runtime descriptions are rejected", () => {
            var files = Files("Ncma.Editor.Mcp", true);
            foreach (string json in new[] {
                "{\"runtimeTarget\":{\"name\":\"net8\"},\"targets\":{}}",
                Encoding.UTF8.GetString(Deps("Foreign")),
                Encoding.UTF8.GetString(Deps("Ncma.Editor.Mcp")).Replace("\"targets\":", "\"targets\":{},\"targets\":"),
                Encoding.UTF8.GetString(Deps("Ncma.Editor.Mcp")).Replace("\"runtime\":", "\"runtimeTargets\":{\"native.dll\":{}},\"runtime\":")
            }) Reject(() => WorkflowManagedRuntime.Validate("Ncma.Editor.Mcp", true, files, Encoding.UTF8.GetBytes(json)));
        });
        yield return ("M8.6-C3 host task requires MCP and Gameplay from the same exact producer", () => {
            string prefix = "compiled/" + Guid.NewGuid().ToString("N") + "/";
            var runtime = Files("Ncma.Editor.Services.Tests", true).Select(f => prefix + "runtime/" + f.Path).ToArray();
            var managedHost = Files("Ncma.Editor.Mcp", true).Select(f => prefix + "mcp/" + f.Path)
                .Concat(Files("Ncma.Gameplay.Sample", false).Select(f => prefix + "gameplay/" + f.Path)).ToArray();
            string root = @"F:\NcmaEngineRelease\source";
            string nativePrefix = "native/" + Guid.NewGuid().ToString("N") + "/plugins/";
            var native = new[] { "NcmaPlatform.dll", "NcmaRenderer.dll", "NcmaGui.dll" }.Select(f => nativePrefix + f).ToArray();
            Reject(() => WorkflowTestLayout.RuntimeRoot(Task("workflow-host-v1", runtime), root));
            Reject(() => WorkflowTestLayout.RuntimeRoot(Task("workflow-host-v1", [.. runtime, .. managedHost]), root));
            _ = WorkflowTestLayout.RuntimeRoot(Task("workflow-host-v1", [.. runtime, .. managedHost, .. native]), root);
            Reject(() => WorkflowTestLayout.RuntimeRoot(Task("workflow-host-v1", [.. runtime, .. native, .. managedHost.Select(p => p.Replace(prefix, "compiled/" + Guid.NewGuid().ToString("N") + "/"))]), root));
            Reject(() => WorkflowTestLayout.RuntimeRoot(Task("workflow-contracts-v1", [.. runtime, .. managedHost]), root));
        });
        yield return ("M8.6-C3 managed runtime retains bytes and detects added files until explicit close", () => {
            string root = Path.Combine(output, "workflow-runtime-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string dll = Path.Combine(root, "Ncma.Editor.Mcp.dll"); File.WriteAllBytes(dll, [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(root, "Ncma.Editor.Mcp.deps.json"), Deps("Ncma.Editor.Mcp"));
            File.WriteAllText(Path.Combine(root, "Ncma.Editor.Mcp.runtimeconfig.json"), "{}");
            using (var runtime = new WorkflowManagedRuntime(root, "Ncma.Editor.Mcp", true)) {
                runtime.Prepare(); Check(runtime.CopyFiles().Length == 3 && runtime.Fingerprint.Length == 64);
                Reject(() => File.WriteAllBytes(dll, [4]));
                File.WriteAllBytes(Path.Combine(root, "unexpected.dll"), [5]); Reject(runtime.RequireCurrent);
                Reject(() => File.WriteAllBytes(dll, [6]));
            }
            File.WriteAllBytes(dll, [7]); // own synthetic file is writable only after owner release
        });
    }
}
