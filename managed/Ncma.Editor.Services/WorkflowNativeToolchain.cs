using System.Text.Json;

namespace Ncma.Editor.Services;

// Exact locally selected executables, not a complete hermetic VC/Windows SDK.
// Headers, libraries and compiler support DLLs remain trusted installed SDK data.
// Selection and file observation never grant process or installation authority.
internal sealed class WorkflowNativeSelection
{
    private readonly WorkflowEvidenceFile[] files;
    internal string VisualStudioRoot { get; }
    internal string ToolsetVersion { get; }
    internal string WindowsSdkRoot { get; }
    internal string WindowsSdkVersion { get; }
    internal WorkflowNativeSelection(string visualStudioRoot, string toolsetVersion, string windowsSdkRoot, string windowsSdkVersion,
        IEnumerable<WorkflowEvidenceFile> files)
    {
        VisualStudioRoot = WorkflowBuildCommand.CommandPath(visualStudioRoot); WindowsSdkRoot = WorkflowBuildCommand.CommandPath(windowsSdkRoot);
        static string VersionText(string value) {
            if (value is null || value.Length is < 3 or > 32 || value.Any(c => !char.IsAsciiDigit(c) && c != '.') || !Version.TryParse(value, out _))
                throw new IOException("workflow_native_version");
            return value;
        }
        ToolsetVersion = VersionText(toolsetVersion); WindowsSdkVersion = VersionText(windowsSdkVersion);
        this.files = WorkflowEvidenceRules.Files(files);
        if (!this.files.Select(f => f.Path).SequenceEqual(ToolPaths().Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal) || this.files.Any(f => f.Bytes == 0))
            throw new IOException("workflow_native_tool_inventory");
    }
    internal WorkflowEvidenceFile[] CopyFiles() => (WorkflowEvidenceFile[])files.Clone();
    internal string VcRoot => Path.Combine(VisualStudioRoot, "VC", "Tools", "MSVC", ToolsetVersion);
    internal string SdkBin => Path.Combine(WindowsSdkRoot, "bin", WindowsSdkVersion, "x64");
    internal Dictionary<string, string> ToolPaths() => Paths(VisualStudioRoot, ToolsetVersion, WindowsSdkRoot, WindowsSdkVersion);
    private static Dictionary<string, string> Paths(string vs, string vc, string sdk, string version) => new(StringComparer.Ordinal) {
        ["native-tools/cmake.exe"] = Path.Combine(vs, "Common7", "IDE", "CommonExtensions", "Microsoft", "CMake", "CMake", "bin", "cmake.exe"),
        ["native-tools/ninja.exe"] = Path.Combine(vs, "Common7", "IDE", "CommonExtensions", "Microsoft", "CMake", "Ninja", "ninja.exe"),
        ["native-tools/cl.exe"] = Path.Combine(vs, "VC", "Tools", "MSVC", vc, "bin", "Hostx64", "x64", "cl.exe"),
        ["native-tools/link.exe"] = Path.Combine(vs, "VC", "Tools", "MSVC", vc, "bin", "Hostx64", "x64", "link.exe"),
        ["native-tools/lib.exe"] = Path.Combine(vs, "VC", "Tools", "MSVC", vc, "bin", "Hostx64", "x64", "lib.exe"),
        ["native-tools/rc.exe"] = Path.Combine(sdk, "bin", version, "x64", "rc.exe"),
        ["native-tools/mt.exe"] = Path.Combine(sdk, "bin", version, "x64", "mt.exe") };
    internal string[] IncludeDirectories() => [Path.Combine(VcRoot, "include"), .. new[] { "ucrt", "shared", "um", "winrt", "cppwinrt" }.Select(p => Path.Combine(WindowsSdkRoot, "Include", WindowsSdkVersion, p))];
    internal string[] LibraryDirectories() => [Path.Combine(VcRoot, "lib", "x64"), Path.Combine(WindowsSdkRoot, "Lib", WindowsSdkVersion, "ucrt", "x64"), Path.Combine(WindowsSdkRoot, "Lib", WindowsSdkVersion, "um", "x64")];
    internal byte[] Encode() => JsonSerializer.SerializeToUtf8Bytes(new { version = 1, visualStudioRoot = VisualStudioRoot, toolsetVersion = ToolsetVersion,
        windowsSdkRoot = WindowsSdkRoot, windowsSdkVersion = WindowsSdkVersion, files });
    internal WorkflowEvidenceFile Descriptor() {
        byte[] bytes = Encode(); return new("native-selection.json", bytes.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
    }
    internal static WorkflowNativeSelection Observe(string vs, string vc, string sdk, string version)
    {
        // Validate all path/version syntax before any filesystem observation.
        var names = Paths("v", "v", "s", "v").Keys.Select(p => new WorkflowEvidenceFile(p, 1, new string('A', 64)));
        var shape = new WorkflowNativeSelection(vs, vc, sdk, version, names);
        using var pins = new WorkflowPhysicalPins(); var files = new List<WorkflowEvidenceFile>();
        foreach (var pair in shape.ToolPaths()) { var hash = pins.Observe(pair.Value, 64 * 1024 * 1024); files.Add(new(pair.Key, hash.Bytes, hash.Sha256)); }
        foreach (string directory in shape.IncludeDirectories().Concat(shape.LibraryDirectories())) pins.Directory(directory);
        return new(vs, vc, sdk, version, files);
    }
    internal static WorkflowNativeSelection Read(JsonElement value)
    {
        WorkflowCaptureBridge.Shape(value, "version", "visualStudioRoot", "toolsetVersion", "windowsSdkRoot", "windowsSdkVersion", "files");
        if (value.GetProperty("version").GetInt32() != 1 || value.GetProperty("files").GetArrayLength() != 7) throw new IOException("workflow_native_selection_schema");
        var files = new List<WorkflowEvidenceFile>();
        foreach (var file in value.GetProperty("files").EnumerateArray()) {
            WorkflowCaptureBridge.Shape(file, "Path", "Bytes", "Sha256");
            files.Add(new(file.GetProperty("Path").GetString()!, file.GetProperty("Bytes").GetInt64(), file.GetProperty("Sha256").GetString()!));
        }
        return new(value.GetProperty("visualStudioRoot").GetString()!, value.GetProperty("toolsetVersion").GetString()!,
            value.GetProperty("windowsSdkRoot").GetString()!, value.GetProperty("windowsSdkVersion").GetString()!, files);
    }
}

internal sealed class WorkflowNativeToolchain : IDisposable
{
    private readonly int thread = System.Environment.CurrentManagedThreadId;
    private readonly WorkflowPhysicalPins pins = new();
    private readonly WorkflowNativeSelection selection;
    private bool attempted, ready, closed;
    internal WorkflowNativeToolchain(WorkflowNativeSelection selection) => this.selection = selection;
    internal void Prepare()
    {
        Owner(); if (attempted) throw new IOException("workflow_native_tool_single_use"); attempted = true;
        var paths = selection.ToolPaths();
        foreach (var file in selection.CopyFiles()) pins.File(paths[file.Path], file.Sha256, file.Bytes);
        foreach (string directory in selection.IncludeDirectories().Concat(selection.LibraryDirectories())) pins.Directory(directory);
        ready = true;
    }
    private void Owner() {
        if (System.Environment.CurrentManagedThreadId != thread) throw new IOException("workflow_native_tool_thread");
        ObjectDisposedException.ThrowIf(closed, this);
    }
    internal void RequireCurrent() { Owner(); if (!ready) throw new IOException("workflow_native_tool_not_ready"); }
    public void Dispose() {
        if (System.Environment.CurrentManagedThreadId != thread) throw new IOException("workflow_native_tool_thread");
        if (closed) return; pins.Dispose(); closed = true;
    }
}
