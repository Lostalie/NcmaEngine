using System.Text;
using System.Xml.Linq;

namespace Ncma.Editor.Services;

internal sealed record WorkflowRestoreMapping(string Project, string RestoreDirectory, string IntermediateDirectory);

// Owns every inspected project before Prepare can acquire handles. The map is a
// proposed, byte-bound MSBuild input, not an executed/evaluated layout or permit.
// Package bytes, generated-import closure, SDK selection and restore provenance
// remain prerequisites of the production adapter.
internal sealed class WorkflowRestoreGraph : IDisposable
{
    internal const int MaximumProjects = 128;
    private readonly int thread = System.Environment.CurrentManagedThreadId;
    private readonly WorkflowVerificationTask task;
    private readonly WorkflowBuildCommand command;
    private readonly WorkflowRestoreInspection[] inspections;
    private WorkflowRestoreMapping[] mappings = [];
    private byte[] props = [];
    private bool attempted, ready, closed;
    internal bool CompilationAuthorized => false;
    internal string TaskFingerprint => task.Fingerprint;
    internal string? Fingerprint { get; private set; }
    internal WorkflowRestoreGraph(WorkflowVerificationTask task, WorkflowBuildEnvironment environment, Guid restoreId)
    {
        this.task = task; command = new(task, environment);
        if (restoreId == Guid.Empty) throw new IOException("workflow_restore_scope");
        string prefix = "restore/" + restoreId.ToString("N") + "/obj/";
        var projects = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in task.CopyInputs()) {
            if (!file.Path.StartsWith(prefix, StringComparison.Ordinal)) continue;
            string[] parts = file.Path[prefix.Length..].Split('/');
            if (parts.Length != 2) throw new IOException("workflow_restore_layout_input");
            string name = WorkflowRestoreAssets.ProjectName("managed/" + parts[0] + "/" + parts[0] + ".csproj");
            if (parts[1] != "project.assets.json" && parts[1] != name + ".csproj.nuget.g.props" && parts[1] != name + ".csproj.nuget.g.targets")
                throw new IOException("workflow_restore_layout_input");
            projects.Add("managed/" + name + "/" + name + ".csproj");
            if (projects.Count > MaximumProjects) throw new IOException("workflow_restore_graph_budget");
        }
        if (!projects.Contains(task.Recipe.Project)) throw new IOException("workflow_restore_root_missing");
        inspections = projects.Select(p => new WorkflowRestoreInspection(task, environment, restoreId, p)).ToArray();
    }
    private void Owner()
    {
        if (System.Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("workflow_restore_graph_thread");
        ObjectDisposedException.ThrowIf(closed, this);
    }
    internal void Prepare()
    {
        Owner(); if (attempted) throw new InvalidOperationException("workflow_restore_graph_single_use"); attempted = true;
        // Ownership already published. On ANY failure all acquired pins remain
        // retained until explicit Dispose; no restore, copy or fallback occurs.
        foreach (var inspection in inspections) inspection.Prepare();
        var byPath = inspections.ToDictionary(i => i.Read().ProjectPath, StringComparer.OrdinalIgnoreCase);
        var states = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        void Visit(string path) {
            if (!byPath.TryGetValue(path, out var inspection)) throw new IOException("workflow_restore_referenced_project_missing");
            if (states.TryGetValue(path, out int state)) {
                if (state == 1) throw new IOException("workflow_restore_project_cycle");
                return;
            }
            states[path] = 1;
            foreach (string child in inspection.Read().CopyProjects()) Visit(child);
            states[path] = 2;
        }
        Visit(Path.Combine(command.CaptureRoot, "source", task.Recipe.Project.Replace('/', Path.DirectorySeparatorChar)));
        if (states.Count != inspections.Length) throw new IOException("workflow_restore_unreachable_project");
        mappings = inspections.Select(i => new WorkflowRestoreMapping(i.Read().ProjectPath, i.RestoreOutput,
            Path.Combine(command.ArtifactsRoot, "obj", WorkflowRestoreAssets.ProjectName(i.RelativeProject)))).ToArray();
        props = Render();
        Fingerprint = WorkflowEvidenceRules.Digest(new { version = 1, task = task.Fingerprint, command = command.Fingerprint,
            mappings, propsSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(props)), compilationAuthorized = false });
        ready = true;
    }
    private byte[] Render()
    {
        var project = new XElement("Project", new XElement("PropertyGroup", new XElement("_NcmaWorkflowRestoreMapped", "false")));
        // XML escaping alone does not protect the embedded MSBuild condition.
        foreach (var mapping in mappings) {
            foreach (string path in new[] { mapping.Project, mapping.RestoreDirectory, mapping.IntermediateDirectory })
                if (WorkflowBuildCommand.CommandPath(path).Contains('\'')) throw new IOException("workflow_restore_condition_path");
            project.Add(new XElement("PropertyGroup", new XAttribute("Condition", "'$(MSBuildProjectFullPath)' == '" + mapping.Project + "'"),
                new XElement("_NcmaWorkflowRestoreMapped", "true"),
                new XElement("_NcmaWorkflowRestoreExpected", mapping.RestoreDirectory + "\\"),
                new XElement("_NcmaWorkflowIntermediateExpected", mapping.IntermediateDirectory + "\\"),
                new XElement("BaseIntermediateOutputPath", mapping.IntermediateDirectory + "\\"),
                new XElement("MSBuildProjectExtensionsPath", mapping.RestoreDirectory + "\\"),
                new XElement("ProjectAssetsFile", Path.Combine(mapping.RestoreDirectory, "project.assets.json"))));
        }
        // The future adapter must pass this as CustomAfterDirectoryBuildProps,
        // bind its bytes into human review, and inspect actual evaluated values.
        // This target supplements, but cannot replace, those pre-launch checks.
        project.Add(new XElement("Target", new XAttribute("Name", "NcmaWorkflowRequireRestoreMapping"), new XAttribute("BeforeTargets", "PrepareForBuild"),
            new XElement("Error", new XAttribute("Condition", "'$(_NcmaWorkflowRestoreMapped)' != 'true' Or '$(MSBuildProjectExtensionsPath)' != '$(_NcmaWorkflowRestoreExpected)' Or '$(BaseIntermediateOutputPath)' != '$(_NcmaWorkflowIntermediateExpected)'"),
                new XAttribute("Code", "NCMAWORKFLOW001"), new XAttribute("Text", "workflow_restore_output_mapping_required"))));
        return new UTF8Encoding(false, true).GetBytes(project.ToString(SaveOptions.DisableFormatting));
    }
    private void RequireReady()
    {
        Owner(); if (!ready) throw new IOException("workflow_restore_graph_not_ready");
        foreach (var inspection in inspections) _ = inspection.Read();
    }
    internal WorkflowRestoreMapping[] CopyMappings() { RequireReady(); return (WorkflowRestoreMapping[])mappings.Clone(); }
    internal byte[] CopyMappingProps() { RequireReady(); return (byte[])props.Clone(); }
    internal WorkflowRestoreAssets[] CopyAssets() { RequireReady(); return inspections.Select(i => i.Read()).ToArray(); }
    public void Dispose()
    {
        if (System.Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("workflow_restore_graph_thread");
        if (closed) return;
        for (int i = inspections.Length - 1; i >= 0; i--) inspections[i].Dispose();
        ready = false; closed = true;
    }
}
