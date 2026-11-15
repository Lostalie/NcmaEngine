namespace Ncma.Editor.Services;

// A fixed argument vector, NOT a process launcher, SDK observation or restore
// receipt. The future adapter must establish those prerequisites before Start.
internal sealed class WorkflowBuildCommand
{
    internal const string OfflineNugetConfig = "<?xml version=\"1.0\" encoding=\"utf-8\"?><configuration><packageSources><clear /></packageSources><fallbackPackageFolders><clear /></fallbackPackageFolders><packageSourceMapping><clear /></packageSourceMapping><disabledPackageSources><clear /></disabledPackageSources></configuration>";
    private readonly string[] arguments;
    internal string TaskFingerprint { get; }
    internal string EnvironmentFingerprint { get; }
    internal string Executable { get; }
    internal string CaptureRoot { get; }
    internal string OutputRoot { get; }
    internal string ArtifactsRoot => Path.Combine(OutputRoot, "artifacts");
    internal string TemporaryRoot => Path.Combine(OutputRoot, "temp");
    internal string CliHome => Path.Combine(OutputRoot, "cli-home");
    internal bool IsOfflineRestore { get; }
    internal string FeedRoot => Path.Combine(OutputRoot, "feed");
    internal string PackagesRoot => Path.Combine(OutputRoot, "packages");
    internal string NugetConfig => Path.Combine(OutputRoot, "offline.nuget.config");
    internal string RestoreMappingPath => Path.Combine(OutputRoot, "restore-map.props");
    internal string Fingerprint { get; }
    internal string[] CopyArguments() => (string[])arguments.Clone();
    internal WorkflowBuildCommand(WorkflowVerificationTask task, WorkflowBuildEnvironment environment)
    {
        IsOfflineRestore = task.Recipe.Kind == WorkflowVerificationKind.OfflineRestore;
        string root = CommandPath(environment.WorkspaceRoot);
        Executable = CommandPath(environment.ToolPath);
        if (!string.Equals(Path.GetFileName(Executable), "dotnet.exe", StringComparison.Ordinal)) throw new IOException("workflow_command_dotnet_required");
        CaptureRoot = Path.Combine(root, "out", "captured-build", task.Binding.CaptureId.ToString("N"));
        OutputRoot = Path.Combine(root, "out", "verification", "m8-6", IsOfflineRestore ? "restores" : "jobs", task.Binding.JobId.ToString("N"));
        string source = Path.Combine(CaptureRoot, "source");
        bool ai = task.Binding.Variant is "ai" or "ai-extensions";
        bool extensions = task.Binding.Variant is "extensions" or "ai-extensions";
        arguments = ["build", Path.Combine(source, task.Recipe.Project.Replace('/', Path.DirectorySeparatorChar)),
            "--no-restore", "--nologo", "--disable-build-servers", "-noAutoResponse", "-m:1", "-nodeReuse:false",
            "-p:UseSharedCompilation=false", "-warnaserror", "-c", task.Binding.Configuration,
            "-p:DirectoryBuildPropsPath=" + Path.Combine(CaptureRoot, "capture.props"),
            "-p:DirectoryBuildTargetsPath=" + Path.Combine(source, "managed", "Directory.Build.targets"),
            "-p:ArtifactsPath=" + ArtifactsRoot,
            "-p:NcmaEnableAI=" + (ai ? "true" : "false"),
            "-p:NcmaEnableExtensions=" + (extensions ? "true" : "false"),
            "-p:CustomAfterDirectoryBuildProps=" + RestoreMappingPath];
        if (IsOfflineRestore) arguments = ["restore", arguments[1], "--nologo", "--verbosity", "normal", "--disable-build-servers", "--disable-parallel", "--no-http-cache",
            "--configfile", NugetConfig, "--source", FeedRoot, "--packages", PackagesRoot,
            "-noAutoResponse", "-m:1", "-nodeReuse:false", "-p:UseSharedCompilation=false", "-warnaserror",
            "-p:Configuration=" + task.Binding.Configuration,
            "-p:DirectoryBuildPropsPath=" + Path.Combine(CaptureRoot, "capture.props"),
            "-p:DirectoryBuildTargetsPath=" + Path.Combine(source, "managed", "Directory.Build.targets"),
            "-p:ArtifactsPath=" + ArtifactsRoot, "-p:NcmaEnableAI=" + (ai ? "true" : "false"), "-p:NcmaEnableExtensions=" + (extensions ? "true" : "false"),
            "-p:RestoreFallbackFolders=", "-p:RestoreAdditionalProjectSources=", "-p:RestoreAdditionalProjectFallbackFolders=",
            "-p:RestoreIgnoreFailedSources=false", "-p:NuGetAudit=false"];
        if (task.Recipe.Kind == WorkflowVerificationKind.Tests) {
            _ = WorkflowTestRequestCodec.Encode(task);
            arguments = [Path.Combine(WorkflowTestLayout.RuntimeRoot(task, root), "Ncma.Editor.Services.Tests.dll"), task.Recipe.TestSwitch!, Path.Combine(OutputRoot, "test-request.json")];
        }
        if (task.Recipe.Kind == WorkflowVerificationKind.NativePrepare)
            arguments = [Path.Combine(WorkflowTestLayout.RuntimeRoot(task, root), "Ncma.Editor.Services.Tests.dll"), task.Recipe.TestSwitch!, Path.Combine(OutputRoot, "native-request.json")];
        foreach (string path in new[] { CaptureRoot, OutputRoot, ArtifactsRoot, TemporaryRoot, CliHome, arguments[task.Recipe.Kind is WorkflowVerificationKind.Tests or WorkflowVerificationKind.NativePrepare ? 0 : 1] }) _ = CommandPath(path);
        TaskFingerprint = task.Fingerprint; EnvironmentFingerprint = environment.Fingerprint;
        Fingerprint = WorkflowEvidenceRules.Digest(new { version = 1, TaskFingerprint, EnvironmentFingerprint, Executable, arguments,
            OutputRoot, TemporaryRoot, CliHome, automaticRestore = false, installs = false,
            offlineRestore = IsOfflineRestore, config = IsOfflineRestore ? OfflineNugetConfig : null });
    }
    // Quoting alone cannot neutralize MSBuild's property-list/escape grammar.
    // Reject these path characters before constructing ANY property argument.
    internal static string CommandPath(string path)
    {
        string full = WorkflowPhysicalPins.Absolute(path);
        if (full.IndexOfAny([';', '%', '$', '@', '*', '?', '<', '>', '|', '=']) >= 0)
            throw new IOException("workflow_msbuild_path_grammar");
        return full;
    }
}
