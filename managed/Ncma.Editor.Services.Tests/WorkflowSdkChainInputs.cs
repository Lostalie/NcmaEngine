using System.Text.Json;
using System.Text.Json.Serialization;
using Ncma.Editor.Services;

// Trusted stage-E input declaration, NOT a build receipt or an execution grant.
// Deliberately no defaults, environment lookup, credentials, command text or URL.
internal sealed record WorkflowSdkChainInputs(int Version, string Workspace, string Configuration,
    Guid BaselineCaptureId, string BaselineManifestSha256, string BaselineSourceSha256,
    string HarnessSha256, string McpRoot, string McpFingerprint, string DotnetExecutable,
    string GitExecutable, string OfflineCache, string VisualStudioRoot, string ToolsetVersion,
    string WindowsSdkRoot, string WindowsSdkVersion)
{
    internal static WorkflowSdkChainInputs Parse(byte[] bytes, string workspace)
    {
        if (bytes.Length is < 2 or > 16384) throw new IOException("sdk_chain_input_budget");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            if (!names.Add(property.Name)) throw new IOException("sdk_chain_duplicate_input");
        if (names.Count != 16) throw new IOException("sdk_chain_input_fields");
        var value = JsonSerializer.Deserialize<WorkflowSdkChainInputs>(bytes, new JsonSerializerOptions {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw new IOException("sdk_chain_inputs_missing");
        if (value.Version != 1 || value.Configuration is not ("Debug" or "Release") || value.BaselineCaptureId == Guid.Empty)
            throw new IOException("sdk_chain_identity");
        foreach (string hash in new[] { value.BaselineManifestSha256, value.BaselineSourceSha256, value.HarnessSha256, value.McpFingerprint }) WorkflowEvidenceRules.Hash(hash);
        foreach (string path in new[] { value.Workspace, value.McpRoot, value.DotnetExecutable, value.GitExecutable, value.OfflineCache, value.VisualStudioRoot, value.WindowsSdkRoot })
            _ = WorkflowPhysicalPins.Absolute(path);
        if (!string.Equals(value.Workspace, WorkflowPhysicalPins.Absolute(workspace), StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(value.DotnetExecutable) != "dotnet.exe" || Path.GetFileName(value.GitExecutable) != "git.exe")
            throw new IOException("sdk_chain_explicit_host");
        foreach (string version in new[] { value.ToolsetVersion, value.WindowsSdkVersion })
            if (string.IsNullOrEmpty(version) || version.Length > 32 || !System.Version.TryParse(version, out _) || version.Any(c => !char.IsAsciiDigit(c) && c != '.'))
                throw new IOException("sdk_chain_sdk_version");
        return value;
    }
    internal static IEnumerable<(string, Action)> Cases(string workspace)
    {
        foreach (var item in WorkflowSdkInputPreparation.Cases(workspace)) yield return item;
        static void Reject(Action action) {
            try { action(); } catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or JsonException) { return; }
            throw new Exception("sdk_chain_expected_rejection");
        }
        var valid = new WorkflowSdkChainInputs(1, workspace, "Debug", Guid.NewGuid(), new('A',64), new('B',64), new('C',64),
            Path.Combine(workspace,"out","sdk-mcp"), new('D',64), @"C:\Program Files\dotnet\dotnet.exe", @"C:\Program Files\Git\cmd\git.exe",
            Path.Combine(workspace,"out","cache"), @"C:\VS", "14.44.35207", @"C:\SDK", "10.0.26100.0");
        yield return ("M8.6-D SDK chain explicit input shape and missing prerequisite rejection", () => {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(valid);
            if (Parse(bytes,workspace) != valid) throw new Exception("sdk_chain_roundtrip");
            Reject(() => Parse([],workspace)); Reject(() => Parse(new byte[16385],workspace));
            foreach (var bad in new[] { valid with { Version=2 }, valid with { Configuration="default" }, valid with { BaselineCaptureId=Guid.Empty },
                valid with { HarnessSha256="" }, valid with { DotnetExecutable="dotnet" }, valid with { GitExecutable="git" }, valid with { OfflineCache="https://nuget.org" },
                valid with { ToolsetVersion=".." }, valid with { WindowsSdkVersion="" } }) Reject(() => Parse(JsonSerializer.SerializeToUtf8Bytes(bad),workspace));
        });
        yield return ("M8.6-D SDK chain duplicate unknown and foreign workspace declarations reject", () => {
            string json=JsonSerializer.Serialize(valid);
            Reject(() => Parse(System.Text.Encoding.UTF8.GetBytes(json.Insert(1,"\"Version\":1,")),workspace));
            Reject(() => Parse(System.Text.Encoding.UTF8.GetBytes(json.Replace("\"Version\":1", "\"Unknown\":1",StringComparison.Ordinal)),workspace));
            Reject(() => Parse(JsonSerializer.SerializeToUtf8Bytes(valid with { Workspace=Path.Combine(workspace,"foreign") }),workspace));
        });
    }
}
