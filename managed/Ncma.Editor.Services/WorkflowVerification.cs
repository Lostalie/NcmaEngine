using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Editor.Protocol;

namespace Ncma.Editor.Services;

public enum WorkflowVerificationKind { Compile, Tests, OfflineRestore, NativePrepare }
public enum WorkflowProcessOutcome { NotStarted, Exited, TimedOut, Cancelled, EnvironmentBlocked, ObservationLost }
public enum WorkflowCaseOutcome { Passed, Failed, NotRun, Skipped, EnvironmentBlocked, Cancelled }
public enum WorkflowVerificationConclusion { CompileSucceeded, TestsPassed, Failed, Incomplete, NotRun, EnvironmentBlocked, Cancelled, TimedOut, InputsChanged, RestorePrepared, NativeInputsPrepared }
public sealed record WorkflowEvidenceFile(string Path, long Bytes, string Sha256);
public sealed record WorkflowCaseEvidence(string Id, WorkflowCaseOutcome Outcome, string DiagnosticCode);
public sealed record WorkflowVerificationBinding(int Version, Guid JobId, Guid WorkspaceSession, Guid WorkflowId,
    string PlanHash, EditorViewStamp Source, Guid CaptureId, string SourceInputsSha256, string CaptureManifestSha256,
    string CaptureRecipeSha256, string? ReviewBundleSha256, string RecipeId, string Configuration, string Variant,
    string ToolVersion, string ToolSha256)
{
    public WorkflowAssetSource? Assets { get; init; }
}

// Fixed identities, not user-supplied shell commands. Actual host execution belongs to M8.6-C.
public sealed class WorkflowVerificationRecipe
{
    public string Id { get; }
    public WorkflowVerificationKind Kind { get; }
    public string Project => "managed/Ncma.Editor.Services.Tests/Ncma.Editor.Services.Tests.csproj";
    public string? TestSwitch { get; }
    public string Fingerprint => WorkflowEvidenceRules.Digest(new { version = 2, Id, Kind, Project, TestSwitch, RequiredOutputs });
    private WorkflowVerificationRecipe(string id, WorkflowVerificationKind kind, string? testSwitch)
        => (Id, Kind, TestSwitch) = (id, kind, testSwitch);
    public static WorkflowVerificationRecipe Resolve(string id) => id switch {
        "editor-services-build-v1" => new(id, WorkflowVerificationKind.Compile, null),
        "editor-services-restore-offline-v1" => new(id, WorkflowVerificationKind.OfflineRestore, null),
        "workflow-native-prepare-v1" => new(id, WorkflowVerificationKind.NativePrepare, "--workflow-native-build"),
        "workflow-contracts-v1" => new(id, WorkflowVerificationKind.Tests, "--workflow-evidence"),
        "workflow-host-v1" => new(id, WorkflowVerificationKind.Tests, "--workflow-evidence"),
        _ => throw new EditRejectedException("workflow_recipe_unknown") };
    public string[] RequiredOutputs => Kind switch {
        WorkflowVerificationKind.Compile => ["Ncma.Editor.Services.Tests.dll", "Ncma.Editor.Services.Tests.deps.json", "Ncma.Editor.Services.Tests.runtimeconfig.json"],
        WorkflowVerificationKind.OfflineRestore => ["project.assets.json", "Ncma.Editor.Services.Tests.csproj.nuget.g.props", "Ncma.Editor.Services.Tests.csproj.nuget.g.targets"],
        WorkflowVerificationKind.NativePrepare => ["NcmaPlatform.dll", "NcmaRenderer.dll", "NcmaGui.dll", "native-result.json"],
        _ => ["test-results.jsonl"] };
}

// Pure bounded data contract. Host-supplied hashes/versions are NOT evidence of disk or process observation.
// No approval, IO, process execution, persistent recovery, or installation token is created here.
public sealed class WorkflowVerificationTask
{
    private readonly WorkflowEvidenceFile[] inputs;
    private readonly string[] tests;
    public WorkflowVerificationBinding Binding { get; }
    public WorkflowVerificationRecipe Recipe { get; }
    public string Fingerprint { get; }
    public string InventorySha256 { get; }
    public string InputFilesSha256 { get; }
    public WorkflowEvidenceFile[] CopyInputs() => (WorkflowEvidenceFile[])inputs.Clone();
    public string[] CopyTestInventory() => (string[])tests.Clone();
    public WorkflowVerificationTask(WorkflowVerificationBinding binding, IEnumerable<WorkflowEvidenceFile> inputFiles, IEnumerable<string> testInventory)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.Version != 1 || binding.JobId == Guid.Empty || binding.WorkspaceSession == Guid.Empty ||
            binding.WorkflowId == Guid.Empty || binding.CaptureId == Guid.Empty || binding.Source.SessionId == Guid.Empty)
            throw new EditRejectedException("workflow_evidence_identity");
        foreach (string hash in new[] { binding.PlanHash, binding.SourceInputsSha256, binding.CaptureManifestSha256,
            binding.CaptureRecipeSha256, binding.ToolSha256 }) WorkflowEvidenceRules.Hash(hash);
        if (binding.ReviewBundleSha256 is { } review) WorkflowEvidenceRules.Hash(review);
        if (binding.Assets is { } assets && (assets.ProjectId == Guid.Empty ? assets != default : assets.Generation == 0))
            throw new EditRejectedException("workflow_evidence_asset_source");
        if (binding.Configuration is not ("Debug" or "Release") || binding.Variant is not ("default" or "ai" or "extensions" or "ai-extensions"))
            throw new EditRejectedException("workflow_evidence_configuration");
        WorkflowEvidenceRules.Text(binding.ToolVersion, 96);
        Binding = binding; Recipe = WorkflowVerificationRecipe.Resolve(binding.RecipeId);
        inputs = WorkflowEvidenceRules.Files(inputFiles);
        tests = WorkflowEvidenceRules.Inventory(testInventory);
        if (inputs.Length == 0 || (Recipe.Kind != WorkflowVerificationKind.Tests ? tests.Length != 0 : tests.Length == 0))
            throw new EditRejectedException("workflow_evidence_scope");
        InputFilesSha256 = WorkflowEvidenceRules.Digest(inputs); InventorySha256 = WorkflowEvidenceRules.Digest(tests);
        Fingerprint = WorkflowEvidenceRules.Digest(new { binding, InputFilesSha256, InventorySha256, recipeFingerprint = Recipe.Fingerprint });
    }
}

public sealed class WorkflowVerificationReceipt
{
    private readonly WorkflowEvidenceFile[] inputs, outputs, logs;
    private readonly WorkflowCaseEvidence[] cases;
    public int Version => 2;
    public string TaskFingerprint { get; }
    public string Fingerprint { get; }
    public WorkflowProcessOutcome ProcessOutcome { get; }
    public int? ExitCode { get; }
    public int? WarningCount { get; }
    public int? ErrorCount { get; }
    public bool EvidenceComplete { get; }
    public string DiagnosticCode { get; }
    public bool InputsObserved { get; }
    public string DiagnosticCountScope { get; }
    public bool InputFilesMatch { get; }
    public WorkflowVerificationConclusion Conclusion { get; }
    public bool InstallationAuthorized => false;
    public WorkflowEvidenceFile[] CopyObservedInputs() => (WorkflowEvidenceFile[])inputs.Clone();
    public WorkflowEvidenceFile[] CopyOutputs() => (WorkflowEvidenceFile[])outputs.Clone();
    public WorkflowEvidenceFile[] CopyLogs() => (WorkflowEvidenceFile[])logs.Clone();
    public WorkflowCaseEvidence[] CopyCases() => (WorkflowCaseEvidence[])cases.Clone();
    public void RequireTask(WorkflowVerificationTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (TaskFingerprint != task.Fingerprint) throw new EditRejectedException("workflow_evidence_task_mismatch");
    }
    public WorkflowVerificationReceipt(WorkflowVerificationTask task, string observedTaskFingerprint,
        WorkflowProcessOutcome outcome, int? exitCode, int? warnings, int? errors,
        IEnumerable<WorkflowEvidenceFile> observedInputs, IEnumerable<WorkflowEvidenceFile> outputFiles,
        IEnumerable<WorkflowEvidenceFile> logFiles, IEnumerable<WorkflowCaseEvidence> caseResults,
        bool evidenceComplete = true, string diagnosticCode = "", bool inputsObserved = true)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (observedTaskFingerprint != task.Fingerprint) throw new EditRejectedException("workflow_evidence_task_mismatch");
        if (!Enum.IsDefined(outcome) || warnings is < 0 || errors is < 0 ||
            (outcome == WorkflowProcessOutcome.Exited && exitCode is null) ||
            (outcome is WorkflowProcessOutcome.NotStarted or WorkflowProcessOutcome.EnvironmentBlocked && exitCode is not null))
            throw new EditRejectedException("workflow_evidence_process");
        inputs = WorkflowEvidenceRules.Files(observedInputs); outputs = WorkflowEvidenceRules.Files(outputFiles);
        logs = WorkflowEvidenceRules.Files(logFiles); cases = WorkflowEvidenceRules.Cases(caseResults);
        string[] inventory = task.CopyTestInventory();
        if (!inventory.SequenceEqual(cases.Select(c => c.Id), StringComparer.Ordinal)) throw new EditRejectedException("workflow_evidence_inventory");
        if (outcome is WorkflowProcessOutcome.NotStarted or WorkflowProcessOutcome.EnvironmentBlocked &&
            (outputs.Length != 0 || cases.Any(c => c.Outcome is WorkflowCaseOutcome.Passed or WorkflowCaseOutcome.Failed)))
            throw new EditRejectedException("workflow_evidence_unexecuted_results");
        if (diagnosticCode is null || diagnosticCode.Length > 96 || diagnosticCode.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new EditRejectedException("workflow_evidence_diagnostic");
        if (outcome == WorkflowProcessOutcome.Exited && logs.Length == 0 && evidenceComplete) throw new EditRejectedException("workflow_evidence_logs_missing");
        EvidenceComplete = evidenceComplete; DiagnosticCode = diagnosticCode;
        InputsObserved = inputsObserved; DiagnosticCountScope = task.Recipe.Kind switch {
            WorkflowVerificationKind.Tests => "case_harness", WorkflowVerificationKind.NativePrepare => "native_tool_exit_counts_unobserved", _ => "compiler_or_restore" };
        TaskFingerprint = task.Fingerprint; ProcessOutcome = outcome; ExitCode = exitCode; WarningCount = warnings; ErrorCount = errors;
        InputFilesMatch = WorkflowEvidenceRules.Digest(inputs) == task.InputFilesSha256;
        bool outputsComplete = task.Recipe.RequiredOutputs.All(path => outputs.Any(f => f.Path == path && f.Bytes > 0));
        Conclusion = outcome switch {
            WorkflowProcessOutcome.NotStarted => WorkflowVerificationConclusion.NotRun,
            WorkflowProcessOutcome.EnvironmentBlocked => WorkflowVerificationConclusion.EnvironmentBlocked,
            WorkflowProcessOutcome.Cancelled => WorkflowVerificationConclusion.Cancelled,
            WorkflowProcessOutcome.TimedOut => WorkflowVerificationConclusion.TimedOut,
            WorkflowProcessOutcome.ObservationLost => errors is > 0 || cases.Any(c => c.Outcome == WorkflowCaseOutcome.Failed) ? WorkflowVerificationConclusion.Failed : WorkflowVerificationConclusion.Incomplete,
            _ when !inputsObserved => exitCode != 0 || cases.Any(c => c.Outcome == WorkflowCaseOutcome.Failed) ? WorkflowVerificationConclusion.Failed : WorkflowVerificationConclusion.Incomplete,
            _ when !InputFilesMatch => WorkflowVerificationConclusion.InputsChanged,
            _ when exitCode != 0 || errors is > 0 || cases.Any(c => c.Outcome == WorkflowCaseOutcome.Failed) => WorkflowVerificationConclusion.Failed,
            _ when !evidenceComplete || (task.Recipe.Kind != WorkflowVerificationKind.NativePrepare && (warnings is not 0 || errors is not 0)) ||
                warnings is > 0 || !outputsComplete || cases.Any(c => c.Outcome != WorkflowCaseOutcome.Passed) => WorkflowVerificationConclusion.Incomplete,
            _ => task.Recipe.Kind switch {
                WorkflowVerificationKind.Compile => WorkflowVerificationConclusion.CompileSucceeded,
                WorkflowVerificationKind.OfflineRestore => WorkflowVerificationConclusion.RestorePrepared,
                WorkflowVerificationKind.NativePrepare => WorkflowVerificationConclusion.NativeInputsPrepared,
                _ => WorkflowVerificationConclusion.TestsPassed } };
        Fingerprint = WorkflowEvidenceRules.Digest(new { Version, TaskFingerprint, outcome, exitCode, warnings, errors, DiagnosticCountScope, EvidenceComplete, InputsObserved, DiagnosticCode, inputs, outputs, logs, cases, Conclusion });
    }
}

internal static class WorkflowEvidenceRules
{
    internal const int MaxFiles = 2048, MaxCases = 4096;
    internal static void Hash(string value)
    {
        if (value is null || value.Length != 64 || value.Any(c => c is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))
            throw new EditRejectedException("workflow_evidence_hash");
    }
    internal static void Text(string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(char.IsControl)) throw new EditRejectedException("workflow_evidence_text");
        try { _ = new System.Text.UTF8Encoding(false, true).GetByteCount(value); }
        catch (System.Text.EncoderFallbackException) { throw new EditRejectedException("workflow_evidence_text"); }
    }
    internal static string Digest<T>(T value)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, Wire.Json);
        if (bytes.Length > 4 * 1024 * 1024) throw new EditRejectedException("workflow_evidence_byte_budget");
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
    private static void Path(string path)
    {
        Text(path, 240);
        if (path.Any(c => c is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and not '_' and not '-' and not '.' and not '/'))
            throw new EditRejectedException("workflow_evidence_path");
        foreach (string part in path.Split('/')) {
            string stem = part.Split('.')[0].ToUpperInvariant();
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || stem is "CON" or "PRN" or "AUX" or "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '1' and <= '9'))
                throw new EditRejectedException("workflow_evidence_path");
        }
    }
    internal static WorkflowEvidenceFile[] Files(IEnumerable<WorkflowEvidenceFile> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var rows = new List<WorkflowEvidenceFile>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in source) {
            if (row is null || rows.Count == MaxFiles) throw new EditRejectedException("workflow_evidence_file_budget");
            Path(row.Path); Hash(row.Sha256);
            if (row.Bytes is < 0 or > 4294967296 || !names.Add(row.Path)) throw new EditRejectedException("workflow_evidence_file");
            rows.Add(row);
        }
        foreach (var row in rows) {
            for (int slash = row.Path.IndexOf('/'); slash >= 0; slash = row.Path.IndexOf('/', slash + 1))
                if (names.Contains(row.Path[..slash])) throw new EditRejectedException("workflow_evidence_path_collision");
        }
        return rows.OrderBy(r => r.Path, StringComparer.Ordinal).ToArray();
    }
    internal static string[] Inventory(IEnumerable<string> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in source) {
            if (names.Count == MaxCases) throw new EditRejectedException("workflow_evidence_case_budget");
            Text(name, 128); if (!names.Add(name)) throw new EditRejectedException("workflow_evidence_case_duplicate");
        }
        return names.Order(StringComparer.Ordinal).ToArray();
    }
    internal static WorkflowCaseEvidence[] Cases(IEnumerable<WorkflowCaseEvidence> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var rows = new List<WorkflowCaseEvidence>();
        foreach (var row in source) {
            if (row is null || rows.Count == MaxCases) throw new EditRejectedException("workflow_evidence_case_budget");
            Text(row.Id, 128);
            if (!Enum.IsDefined(row.Outcome) || row.DiagnosticCode is null || row.DiagnosticCode.Length > 64 ||
                row.DiagnosticCode.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_') ||
                (row.Outcome != WorkflowCaseOutcome.Passed && row.DiagnosticCode.Length == 0)) throw new EditRejectedException("workflow_evidence_case");
            rows.Add(row);
        }
        _ = Inventory(rows.Select(r => r.Id));
        return rows.OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
    }
}
