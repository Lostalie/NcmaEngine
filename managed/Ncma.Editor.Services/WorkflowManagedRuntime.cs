using System.Text.Json;

namespace Ncma.Editor.Services;

// A live producer-owned bounded tree, not a caller's DLL list or a JSON claim.
// The external .NET framework remains trusted; RID fallback is deliberately
// unsupported until there is an exact selected-runtime closure implementation.
internal sealed class WorkflowManagedRuntime : IDisposable
{
    private readonly WorkflowPinnedTree tree;
    private readonly string project;
    private readonly bool executable;
    internal string Fingerprint => tree.Fingerprint;
    internal WorkflowManagedRuntime(string root, string project, bool executable)
        => (tree, this.project, this.executable) = (new(root), project, executable);
    internal void Prepare()
    {
        tree.Prepare();
        Validate(project, executable, tree.CopyFiles(), tree.Read(project + ".deps.json", 4194304));
    }
    internal static void Validate(string project, bool executable, WorkflowEvidenceFile[] files, ReadOnlyMemory<byte> dependencies)
    {
        // Closed caller-selected identities. Never interpret project as a path.
        if (project is not ("Ncma.Editor.Services.Tests" or "Ncma.Editor.Mcp" or "Ncma.Gameplay.Sample"))
            throw new IOException("workflow_runtime_project");
        var set = WorkflowEvidenceRules.Files(files).ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var required = executable ? new[] { project + ".dll", project + ".deps.json", project + ".runtimeconfig.json" }
            : new[] { project + ".dll", project + ".deps.json" };
        foreach (string name in required) if (!set.TryGetValue(name, out var file) || file.Bytes == 0)
            throw new IOException("workflow_compiled_required_product");
        using var deps = JsonDocument.Parse(dependencies, new JsonDocumentOptions { MaxDepth = 32 });
        Unique(deps.RootElement);
        string selected = deps.RootElement.GetProperty("runtimeTarget").GetProperty("name").GetString() ?? "";
        if (selected.Length == 0 || !deps.RootElement.GetProperty("targets").TryGetProperty(selected, out var selectedTarget))
            throw new IOException("workflow_runtime_selected_target_missing");
        var main = selectedTarget.EnumerateObject().Where(p => p.Name.StartsWith(project + "/", StringComparison.Ordinal)).ToArray();
        if (main.Length != 1 || !main[0].Value.TryGetProperty("runtime", out var mainRuntime) || !mainRuntime.TryGetProperty(project + ".dll", out _))
            throw new IOException("workflow_runtime_main_missing");
        foreach (var target in deps.RootElement.GetProperty("targets").EnumerateObject()) foreach (var library in target.Value.EnumerateObject()) {
            if (library.Value.TryGetProperty("runtimeTargets", out var targets) && targets.EnumerateObject().Any())
                throw new IOException("workflow_runtime_rid_closure_unsupported");
            foreach (string kind in new[] { "runtime", "native", "resources" }) {
                if (!library.Value.TryGetProperty(kind, out var assets)) continue;
                foreach (var asset in assets.EnumerateObject()) {
                    if (Path.GetFileName(asset.Name) == "_._") continue;
                    string path = kind == "resources" ? asset.Name : Path.GetFileName(asset.Name);
                    if (!set.TryGetValue(path, out var file) || file.Bytes == 0) throw new IOException("workflow_runtime_dependency_missing");
                }
            }
        }
    }
    private static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object) {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject()) {
                if (!names.Add(property.Name)) throw new IOException("workflow_runtime_duplicate_field");
                Unique(property.Value);
            }
        } else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Unique(item);
    }
    internal void RequireCurrent() => tree.RequireCurrent();
    internal WorkflowEvidenceFile[] CopyFiles() => tree.CopyFiles();
    internal void CopyLocked(string relative, Stream destination, byte[] buffer) => tree.CopyLocked(relative, destination, buffer);
    public void Dispose() => tree.Dispose();
}
