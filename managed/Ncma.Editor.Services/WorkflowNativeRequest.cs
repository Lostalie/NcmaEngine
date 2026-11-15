using System.Text.Json;

namespace Ncma.Editor.Services;

internal sealed record WorkflowNativeRequest(WorkflowVerificationTask Task, string Workspace, WorkflowNativeSelection Selection)
{
    internal const int MaximumBytes = 1024 * 1024;
    internal WorkflowNativePlan Plan => new(Workspace, Task.Binding.CaptureId, Task.Binding.JobId, Task.Binding.Configuration, Selection);
    internal void Validate()
    {
        if (Task.Recipe.Kind != WorkflowVerificationKind.NativePrepare) throw new IOException("workflow_native_recipe");
        _ = WorkflowBuildCommand.CommandPath(Workspace);
        _ = WorkflowTestLayout.RuntimeRoot(Task, Workspace);
        var selected = Selection.CopyFiles().Append(Selection.Descriptor()).OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();
        var actual = Task.CopyInputs().Where(f => f.Path.StartsWith("native-tools/", StringComparison.Ordinal) || f.Path == "native-selection.json").ToArray();
        if (!selected.SequenceEqual(actual)) throw new IOException("workflow_native_selection_binding");
    }
    internal byte[] Encode()
    {
        Validate(); using var selection = JsonDocument.Parse(Selection.Encode());
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { version = 1, workspace = Workspace, taskFingerprint = Task.Fingerprint,
            binding = Task.Binding, inputs = Task.CopyInputs(), selection = selection.RootElement });
        if (bytes.Length > MaximumBytes) throw new IOException("workflow_native_request_budget"); return bytes;
    }
    internal static WorkflowNativeRequest Read(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is < 2 or > MaximumBytes) throw new IOException("workflow_native_request_budget");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 }); var value = document.RootElement;
        WorkflowCaptureBridge.Shape(value, "version", "workspace", "taskFingerprint", "binding", "inputs", "selection");
        if (value.GetProperty("version").GetInt32() != 1) throw new IOException("workflow_native_request_version");
        var binding = value.GetProperty("binding");
        WorkflowCaptureBridge.Shape(binding, typeof(WorkflowVerificationBinding).GetProperties().Select(p => p.Name).ToArray());
        WorkflowCaptureBridge.Shape(binding.GetProperty("Source"), "SessionId", "Generation", "Revision");
        if (binding.GetProperty("Assets").ValueKind != JsonValueKind.Null)
            WorkflowCaptureBridge.Shape(binding.GetProperty("Assets"), "ProjectId", "Generation", "Revision");
        if (value.GetProperty("inputs").GetArrayLength() > WorkflowEvidenceRules.MaxFiles) throw new IOException("workflow_native_request_inventory");
        var files = new List<WorkflowEvidenceFile>();
        foreach (var file in value.GetProperty("inputs").EnumerateArray()) {
            WorkflowCaptureBridge.Shape(file, "Path", "Bytes", "Sha256");
            files.Add(new(file.GetProperty("Path").GetString()!, file.GetProperty("Bytes").GetInt64(), file.GetProperty("Sha256").GetString()!));
        }
        var task = new WorkflowVerificationTask(binding.Deserialize<WorkflowVerificationBinding>()!, files, []);
        if (task.Fingerprint != value.GetProperty("taskFingerprint").GetString()) throw new IOException("workflow_native_request_task");
        var result = new WorkflowNativeRequest(task, value.GetProperty("workspace").GetString()!, WorkflowNativeSelection.Read(value.GetProperty("selection")));
        result.Validate(); return result;
    }
}

internal sealed record WorkflowNativeResult(int Version, string TaskFingerprint, int? ConfigureExit, int? BuildExit, bool CacheVerified, string Diagnostic)
{
    internal bool Complete => Version == 1 && ConfigureExit == 0 && BuildExit == 0 && CacheVerified && Diagnostic.Length == 0;
    internal byte[] Encode() => JsonSerializer.SerializeToUtf8Bytes(new { version = Version, taskFingerprint = TaskFingerprint,
        configureExit = ConfigureExit, buildExit = BuildExit, cacheVerified = CacheVerified, diagnostic = Diagnostic });
    internal static WorkflowNativeResult Read(WorkflowVerificationTask task, ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is < 2 or > 16384) throw new IOException("workflow_native_result_budget");
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 3 }); var value = json.RootElement;
        WorkflowCaptureBridge.Shape(value, "version", "taskFingerprint", "configureExit", "buildExit", "cacheVerified", "diagnostic");
        int? Exit(string name) => value.GetProperty(name).ValueKind == JsonValueKind.Null ? null : value.GetProperty(name).GetInt32();
        var result = new WorkflowNativeResult(value.GetProperty("version").GetInt32(), value.GetProperty("taskFingerprint").GetString()!,
            Exit("configureExit"), Exit("buildExit"), value.GetProperty("cacheVerified").GetBoolean(), value.GetProperty("diagnostic").GetString()!);
        if (task.Recipe.Kind != WorkflowVerificationKind.NativePrepare || result.Version != 1 || result.TaskFingerprint != task.Fingerprint ||
            result.Diagnostic is null || result.Diagnostic.Length > 96 || result.Diagnostic.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_') ||
            (result.ConfigureExit != 0 && result.BuildExit is not null)) throw new IOException("workflow_native_result_binding");
        return result;
    }
}
