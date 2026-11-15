namespace Ncma.Editor.Services;

internal static class WorkflowTestLayout
{
    internal static string RuntimeRoot(WorkflowVerificationTask task, string workspace)
    {
        if (task.Recipe.Kind is not (WorkflowVerificationKind.Tests or WorkflowVerificationKind.NativePrepare)) throw new IOException("workflow_test_layout_recipe");
        Guid? producer = null, nativeProducer = null;
        foreach (var file in task.CopyInputs()) {
            if (task.Recipe.Kind == WorkflowVerificationKind.NativePrepare && (file.Path == "native-selection.json" || file.Path.StartsWith("native-tools/", StringComparison.Ordinal))) continue;
            string[] parts = file.Path.Split('/');
            if (task.Recipe.Id == "workflow-host-v1" && parts.Length >= 4 && parts[0] == "native" && parts[2] == "plugins") {
                if (!Guid.TryParseExact(parts[1], "N", out var nativeId) || nativeId == Guid.Empty || nativeId == task.Binding.JobId ||
                    nativeProducer is { } prior && prior != nativeId) throw new IOException("workflow_test_native_producer");
                nativeProducer = nativeId; continue;
            }
            if (parts.Length < 4 || parts[0] != "compiled" ||
                (parts[2] != "runtime" && (task.Recipe.Id != "workflow-host-v1" || parts[2] is not ("mcp" or "gameplay"))) ||
                !Guid.TryParseExact(parts[1], "N", out var id) || id == Guid.Empty ||
                id == task.Binding.JobId || producer is { } seen && seen != id) throw new IOException("workflow_test_compiled_inputs_required");
            producer = id;
        }
        if (producer is null) throw new IOException("workflow_test_compiled_inputs_required");
        string prefix = "compiled/" + producer.Value.ToString("N") + "/runtime/";
        foreach (string file in new[] { "Ncma.Editor.Services.Tests.dll", "Ncma.Editor.Services.Tests.deps.json", "Ncma.Editor.Services.Tests.runtimeconfig.json" })
            if (!task.CopyInputs().Any(f => f.Path == prefix + file && f.Bytes > 0)) throw new IOException("workflow_test_compiled_inputs_required");
        if (task.Recipe.Id == "workflow-host-v1") {
            string hostPrefix = "compiled/" + producer.Value.ToString("N") + "/";
            foreach (string file in new[] { "mcp/Ncma.Editor.Mcp.dll", "mcp/Ncma.Editor.Mcp.deps.json", "mcp/Ncma.Editor.Mcp.runtimeconfig.json",
                "gameplay/Ncma.Gameplay.Sample.dll", "gameplay/Ncma.Gameplay.Sample.deps.json" })
                if (!task.CopyInputs().Any(f => f.Path == hostPrefix + file && f.Bytes > 0)) throw new IOException("workflow_test_managed_host_inputs_required");
            if (nativeProducer is null || nativeProducer == producer) throw new IOException("workflow_test_native_inputs_required");
            string nativePrefix = "native/" + nativeProducer.Value.ToString("N") + "/plugins/";
            foreach (string file in new[] { "NcmaPlatform.dll", "NcmaRenderer.dll", "NcmaGui.dll" })
                if (!task.CopyInputs().Any(f => f.Path == nativePrefix + file && f.Bytes > 0)) throw new IOException("workflow_test_native_inputs_required");
        }
        return WorkflowBuildCommand.CommandPath(Path.Combine(workspace, "out", "verification", "m8-6", "jobs", producer.Value.ToString("N"),
            "artifacts", "bin", "Ncma.Editor.Services.Tests", task.Binding.Configuration.ToLowerInvariant()));
    }
}
