using System.Text.Json;
using System.Text.Json.Serialization;
using Ncma.Editor.Services;

// Local stage-E preparation command, never a functional test or SDK job grant.
internal sealed record WorkflowSdkPreparationRequest(int Version, string Workspace, string Configuration,
    Guid BaselineCaptureId, string BaselineManifestSha256, string BaselineSourceSha256,
    string McpRoot, string DotnetExecutable, string GitExecutable, string OfflineCache,
    string VisualStudioRoot, string ToolsetVersion, string WindowsSdkRoot, string WindowsSdkVersion)
{
    internal static WorkflowSdkPreparationRequest Parse(byte[] bytes, string workspace)
    {
        if (bytes.Length is < 2 or > 16384) throw new IOException("sdk_prepare_request_budget");
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in doc.RootElement.EnumerateObject())
            if (!names.Add(field.Name)) throw new IOException("sdk_prepare_duplicate_field");
        if (names.Count != 14) throw new IOException("sdk_prepare_request_fields");
        var value = JsonSerializer.Deserialize<WorkflowSdkPreparationRequest>(bytes, new JsonSerializerOptions {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw new IOException("sdk_prepare_request_missing");
        // Shape validation only. These sentinel hashes never leave this method or become an input declaration.
        _ = value.Declaration(new string('A', 64), new string('B', 64), workspace);
        return value;
    }
    internal WorkflowSdkChainInputs Declaration(string harnessSha, string mcpFingerprint, string workspace)
    {
        var value = new WorkflowSdkChainInputs(Version, Workspace, Configuration, BaselineCaptureId, BaselineManifestSha256,
            BaselineSourceSha256, harnessSha, McpRoot, mcpFingerprint, DotnetExecutable, GitExecutable, OfflineCache,
            VisualStudioRoot, ToolsetVersion, WindowsSdkRoot, WindowsSdkVersion);
        return WorkflowSdkChainInputs.Parse(JsonSerializer.SerializeToUtf8Bytes(value), workspace);
    }
}

internal static class WorkflowSdkInputPreparation
{
    internal static int Run(string requestFile)
    {
        string phase = "entry";
        try { Prepare(requestFile, value => phase = value); return 0; }
        catch (Exception error) when (error is not OutOfMemoryException) {
            string code = SafeCode(error.Message);
            Console.Error.WriteLine($"sdk_input_preparation_failed:{phase}:{error.GetType().Name}:{code}"); return 1;
        }
    }
    internal static string SafeCode(string message) => message.Length is > 0 and <= 96 &&
        (message.StartsWith("sdk_", StringComparison.Ordinal) || message.StartsWith("workflow_", StringComparison.Ordinal)) &&
        message.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_') ? message : "unclassified";
    private static void Prepare(string requestFile, Action<string> phase)
    {
#if !NCMA_EXTENSIONS
        throw new IOException("sdk_prepare_requires_extensions_harness");
#else
        phase("request");
        requestFile = WorkflowPhysicalPins.Absolute(requestFile);
        using var pins = new WorkflowPhysicalPins();
        var requestBytes = pins.Observe(requestFile, 16384);
        byte[] bytes = pins.ReadFile(requestFile, requestBytes.Sha256, requestBytes.Bytes, 16384);
        using var json = JsonDocument.Parse(bytes);
        string workspace = WorkflowPhysicalPins.Absolute(json.RootElement.GetProperty("Workspace").GetString()!);
        var request = WorkflowSdkPreparationRequest.Parse(bytes, workspace);
        string output = Path.GetDirectoryName(requestFile)!;
        string prefix = Path.Combine(workspace, "out", "verification", "m8-6", "sdk-inputs") + Path.DirectorySeparatorChar;
        if (!output.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(output[prefix.Length..], "N", out _) ||
            Path.GetFileName(requestFile) != "request.json") throw new IOException("sdk_prepare_output_identity");
        pins.Directory(output);
        foreach (string name in new[] { "inputs.json", "observation.json" })
            if (File.Exists(Path.Combine(output, name)) || Directory.Exists(Path.Combine(output, name))) throw new IOException("sdk_prepare_output_exists");
        phase("baseline");
        string manifest = Path.Combine(workspace, "out", "captured-build", request.BaselineCaptureId.ToString("N"), "capture.json");
        var observedManifest = pins.Observe(manifest, 4194304);
        if (observedManifest.Sha256 != request.BaselineManifestSha256) throw new IOException("sdk_prepare_capture_changed");
        using var capture = JsonDocument.Parse(pins.ReadFile(manifest, observedManifest.Sha256, observedManifest.Bytes, 4194304));
        if (capture.RootElement.GetProperty("sourceInputsSha256").GetString() != request.BaselineSourceSha256 ||
            capture.RootElement.GetProperty("reviewBundleSha256").ValueKind != JsonValueKind.Null) throw new IOException("sdk_prepare_baseline_identity");
        phase("harness");
        string harnessRoot = Path.GetDirectoryName(typeof(WorkflowSdkInputPreparation).Assembly.Location)!;
        using var harness = new WorkflowManagedRuntime(harnessRoot, "Ncma.Editor.Services.Tests", true); harness.Prepare();
        if (!harness.CopyFiles().Any(f => f.Path == "Ncma.Extensions.Hosting.dll")) throw new IOException("sdk_prepare_extension_dependency");
        var harnessFile = harness.CopyFiles().Single(f => f.Path == "Ncma.Editor.Services.Tests.dll");
        phase("mcp");
        using var mcp = new WorkflowManagedRuntime(request.McpRoot, "Ncma.Editor.Mcp", true); mcp.Prepare();
        phase("tools");
        var dotnet = pins.Observe(request.DotnetExecutable, 64 * 1024 * 1024);
        var git = pins.Observe(request.GitExecutable, 64 * 1024 * 1024);
        pins.Directory(request.OfflineCache); // not a claim that every future restore package is available
        phase("native");
        var selection = WorkflowNativeSelection.Observe(request.VisualStudioRoot, request.ToolsetVersion, request.WindowsSdkRoot, request.WindowsSdkVersion);
        using var native = new WorkflowNativeToolchain(selection); native.Prepare(); native.RequireCurrent();
        phase("declaration");
        var declaration = request.Declaration(harnessFile.Sha256, mcp.Fingerprint, workspace);
        void Save(string name, object value) {
            using var file = new FileStream(Path.Combine(output, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(file, value, new JsonSerializerOptions { WriteIndented = true }); file.Flush(true);
        }
        phase("write");
        harness.RequireCurrent(); mcp.RequireCurrent();
        Save("observation.json", new { kind = "ncma.workflow.sdk-input-observation.v1", requestSha256 = requestBytes.Sha256,
            harnessRoot, harnessFingerprint = harness.Fingerprint, harnessFiles = harness.CopyFiles(),
            mcpRoot = request.McpRoot, mcpFingerprint = mcp.Fingerprint, mcpFiles = mcp.CopyFiles(),
            dotnet = new { path = request.DotnetExecutable, bytes = dotnet.Bytes, sha256 = dotnet.Sha256 },
            git = new { path = request.GitExecutable, bytes = git.Bytes, sha256 = git.Sha256 }, nativeFiles = selection.CopyFiles(),
            sdkSelectionExecuted = false, fullOfflineCacheValidated = false, testsExecuted = false, executionAuthorized = false });
        Save("inputs.json", declaration); // original 16 fields, actual hashes; no observer fields added
        Console.WriteLine("Prepared original SDK input declaration; no tests, jobs or tool execution.");
#endif
    }
    internal static IEnumerable<(string, Action)> Cases(string workspace)
    {
        static void Reject(Action action) {
            try { action(); } catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or JsonException) { return; }
            throw new Exception("sdk_prepare_expected_rejection");
        }
        yield return ("M8.6 SDK preparation diagnostics retain bounded codes without exception text", () => {
            if (SafeCode("workflow_runtime_dependency_missing") != "workflow_runtime_dependency_missing") throw new Exception("sdk_prepare_code_lost");
            foreach (string message in new[] { @"access denied C:\private", "sdk_secret=body", "workflow_bad\nresponse", new string('x', 100) })
                if (SafeCode(message) != "unclassified") throw new Exception("sdk_prepare_message_leaked");
        });
        var request = new WorkflowSdkPreparationRequest(1, workspace, "Release", Guid.NewGuid(), new('A', 64), new('B', 64),
            Path.Combine(workspace, "out", "mcp"), @"C:\dotnet\dotnet.exe", @"C:\git\git.exe", @"C:\cache", @"C:\VS", "14.44.35207", @"C:\SDK", "10.0.26100.0");
        yield return ("M8.6 SDK preparation request uses original declaration contract without caller fingerprints", () => {
            var value = WorkflowSdkPreparationRequest.Parse(JsonSerializer.SerializeToUtf8Bytes(request), workspace);
            if (value != request || value.Declaration(new('C', 64), new('D', 64), workspace).HarnessSha256 != new string('C', 64)) throw new Exception("sdk_prepare_binding");
            foreach (var bad in new[] { request with { Configuration = "default" }, request with { DotnetExecutable = "dotnet" }, request with { BaselineSourceSha256 = "" }, request with { Workspace = Path.Combine(workspace,"foreign") } })
                Reject(() => WorkflowSdkPreparationRequest.Parse(JsonSerializer.SerializeToUtf8Bytes(bad), workspace));
        });
        yield return ("M8.6 SDK preparation duplicate unknown and oversized request refusal", () => {
            string text = JsonSerializer.Serialize(request);
            Reject(() => WorkflowSdkPreparationRequest.Parse(System.Text.Encoding.UTF8.GetBytes(text.Insert(1, "\"Version\":1,")), workspace));
            Reject(() => WorkflowSdkPreparationRequest.Parse(System.Text.Encoding.UTF8.GetBytes(text.Insert(1, "\"HarnessSha256\":\"arbitrary\",")), workspace));
            Reject(() => WorkflowSdkPreparationRequest.Parse(new byte[16385], workspace));
            Reject(() => WorkflowSdkPreparationRequest.Parse([], workspace));
        });
    }
}
