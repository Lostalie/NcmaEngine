using System.Text;

namespace Ncma.Editor.Services;

// Closed native prerequisite recipe. No arbitrary CMake flags, target names,
// shell/vcvars scripts, workspace-source fallback or managed restore command.
internal sealed class WorkflowNativePlan
{
    internal WorkflowNativeSelection Selection { get; }
    internal string SourceRoot { get; }
    internal string OutputRoot { get; }
    internal string NativeRoot => Path.Combine(OutputRoot, "native");
    internal string PluginRoot => Path.Combine(NativeRoot, "m2", "plugins");
    internal string Configuration { get; }
    internal string CMake => Selection.ToolPaths()["native-tools/cmake.exe"];
    internal WorkflowNativePlan(string workspace, Guid captureId, Guid jobId, string configuration, WorkflowNativeSelection selection)
    {
        workspace = WorkflowBuildCommand.CommandPath(workspace);
        if (captureId == Guid.Empty || jobId == Guid.Empty || configuration is not ("Debug" or "Release")) throw new IOException("workflow_native_identity");
        Selection = selection; Configuration = configuration;
        SourceRoot = Path.Combine(workspace, "out", "captured-build", captureId.ToString("N"), "source");
        OutputRoot = Path.Combine(workspace, "out", "verification", "m8-6", "jobs", jobId.ToString("N"));
    }
    internal string[] ConfigureArguments()
    {
        var tools = Selection.ToolPaths();
        return ["-S", SourceRoot, "-B", NativeRoot, "-G", "Ninja", "-DCMAKE_BUILD_TYPE=" + Configuration,
            "-DNCMA_BUILD_MANAGED=OFF", "-DNCMA_BUILD_TESTS=OFF", "-DNCMA_VERIFY_MODULE_WORKER=OFF",
            "-DCMAKE_MAKE_PROGRAM=" + tools["native-tools/ninja.exe"], "-DCMAKE_C_COMPILER=" + tools["native-tools/cl.exe"],
            "-DCMAKE_CXX_COMPILER=" + tools["native-tools/cl.exe"], "-DCMAKE_LINKER=" + tools["native-tools/link.exe"],
            "-DCMAKE_AR=" + tools["native-tools/lib.exe"], "-DCMAKE_RC_COMPILER=" + tools["native-tools/rc.exe"],
            "-DCMAKE_MT=" + tools["native-tools/mt.exe"], "-DCMAKE_C_COMPILER_LAUNCHER=", "-DCMAKE_CXX_COMPILER_LAUNCHER=",
            "-DCMAKE_C_FLAGS=/WX", "-DCMAKE_CXX_FLAGS=/WX"];
    }
    internal string[] BuildArguments() => ["--build", NativeRoot, "--config", Configuration, "--target", "NcmaPlatform", "NcmaRenderer", "NcmaGui", "--parallel", "2"];
    internal Dictionary<string, string> EnvironmentOverrides()
    {
        var tools = Selection.ToolPaths();
        return new(StringComparer.OrdinalIgnoreCase) {
            ["INCLUDE"] = string.Join(';', Selection.IncludeDirectories()), ["LIB"] = string.Join(';', Selection.LibraryDirectories()),
            ["PATH"] = string.Join(';', new[] { Path.GetDirectoryName(tools["native-tools/cl.exe"])!, Selection.SdkBin,
                Path.GetDirectoryName(CMake)!, Path.GetDirectoryName(tools["native-tools/ninja.exe"])!,
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.System) }),
            ["VSLANG"] = "1033", ["VSCMD_ARG_TGT_ARCH"] = "x64" };
    }
    internal void VerifyCache(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 1 or > 4194304) throw new IOException("workflow_native_cache_budget");
        string text = new UTF8Encoding(false, true).GetString(bytes); var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in text.Split('\n')) {
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal) || line == "\r") continue;
            int type = line.IndexOf(':'), equals = line.IndexOf('=');
            if (type <= 0 || equals <= type || !values.TryAdd(line[..type], line[(equals + 1)..].TrimEnd('\r'))) throw new IOException("workflow_native_cache_shape");
        }
        void Exact(string key, string expected) {
            if (!values.TryGetValue(key, out string? actual) || actual != expected) throw new IOException("workflow_native_cache_selection");
        }
        void PathValue(string key, string expected) {
            if (!values.TryGetValue(key, out string? actual) || !string.Equals(WorkflowBuildCommand.CommandPath(actual.Replace('/', '\\')), expected, StringComparison.OrdinalIgnoreCase))
                throw new IOException("workflow_native_cache_tool");
        }
        Exact("CMAKE_GENERATOR", "Ninja"); Exact("CMAKE_BUILD_TYPE", Configuration);
        Exact("NCMA_BUILD_MANAGED", "OFF"); Exact("NCMA_BUILD_TESTS", "OFF"); Exact("NCMA_VERIFY_MODULE_WORKER", "OFF");
        Exact("CMAKE_C_FLAGS", "/WX"); Exact("CMAKE_CXX_FLAGS", "/WX");
        Exact("CMAKE_C_COMPILER_LAUNCHER", ""); Exact("CMAKE_CXX_COMPILER_LAUNCHER", "");
        PathValue("CMAKE_HOME_DIRECTORY", SourceRoot);
        var tools = Selection.ToolPaths();
        foreach (var pair in new[] { ("CMAKE_MAKE_PROGRAM", "ninja"), ("CMAKE_C_COMPILER", "cl"), ("CMAKE_CXX_COMPILER", "cl"),
            ("CMAKE_LINKER", "link"), ("CMAKE_AR", "lib"), ("CMAKE_RC_COMPILER", "rc"), ("CMAKE_MT", "mt") })
            PathValue(pair.Item1, tools["native-tools/" + pair.Item2 + ".exe"]);
    }
}
