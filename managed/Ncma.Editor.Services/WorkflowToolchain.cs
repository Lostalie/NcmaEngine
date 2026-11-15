using System.Globalization;

namespace Ncma.Editor.Services;

internal sealed record WorkflowToolchainFile(string Relative, long Bytes, string Sha256);

// Read-only file observation of the .NET toolchain. It opens and hashes files only:
// it does NOT execute the tool, does NOT prove which SDK the host would select, and
// is NOT a repository-wide SDK pin. No build/restore authority is created.
internal sealed class WorkflowToolchain : IDisposable
{
    internal const int MaximumSdkVersions = 8;
    internal const long MaximumComponentBytes = 64 * 1024 * 1024;
    private static readonly string[] components = ["MSBuild.dll", "NuGet.Build.Tasks.dll", "Roslyn/bincore/csc.dll"];
    private readonly int ownerThread = System.Environment.CurrentManagedThreadId;
    private readonly WorkflowPhysicalPins pins = new();
    private readonly WorkflowToolchainFile[] files;
    private bool closed;
    internal string Root { get; }
    internal string SdkVersion { get; }
    internal string ToolSha256 { get; }
    internal string Fingerprint { get; }
    internal bool BuildAuthorized => false;
    internal bool RestoreAuthorized => false;
    internal WorkflowToolchainFile[] CopyFiles()
    {
        Owner(); ObjectDisposedException.ThrowIf(closed, this); return (WorkflowToolchainFile[])files.Clone();
    }
    // The declared version is a CALLER claim. It is only accepted when it equals the
    // version observed in the SDK directory; it is never used as the observation.
    internal WorkflowToolchain(string toolPath, string expectedToolSha256, string declaredVersion)
    {
        WorkflowEvidenceRules.Hash(expectedToolSha256); WorkflowEvidenceRules.Text(declaredVersion, 96);
        string tool = WorkflowPhysicalPins.Absolute(toolPath);
        if (!string.Equals(Path.GetFileName(tool), "dotnet.exe", StringComparison.Ordinal)) throw new IOException("workflow_toolchain_dotnet_required");
        Root = Path.GetDirectoryName(tool) ?? throw new IOException("workflow_toolchain_root");
        try {
        var (toolBytes, toolHash) = pins.Observe(tool, MaximumComponentBytes);
        if (toolHash != expectedToolSha256) throw new IOException("workflow_toolchain_bytes_changed");
        ToolSha256 = toolHash;
        SdkVersion = Select(Path.Combine(Root, "sdk"));
        if (!string.Equals(declaredVersion, SdkVersion, StringComparison.Ordinal)) throw new IOException("workflow_toolchain_version_mismatch");
        var rows = new List<WorkflowToolchainFile>(components.Length + 1) { new("dotnet.exe", toolBytes, toolHash) };
        foreach (string relative in components) {
            string full = Path.Combine(Root, "sdk", SdkVersion, relative.Replace('/', Path.DirectorySeparatorChar));
            var (bytes, sha256) = pins.Observe(full, MaximumComponentBytes);
            rows.Add(new("sdk/" + SdkVersion + "/" + relative, bytes, sha256));
        }
        files = rows.ToArray();
        Fingerprint = WorkflowEvidenceRules.Digest(new { version = 1, Root, SdkVersion, ToolSha256, files });
        } catch {
            // No child or external borrower exists during this constructor. Failed
            // construction cannot return an owner, so release its acquired pins.
            pins.Dispose(); throw;
        }
    }
    private void Owner()
    {
        if (System.Environment.CurrentManagedThreadId != ownerThread) throw new InvalidOperationException("workflow_toolchain_thread");
    }
    // Highest version wins, with a release preferred over an equal-numbered preview.
    // Two distinct builds of the same number cannot be ordered and are refused.
    private static string Select(string sdkRoot)
    {
        if (!Directory.Exists(sdkRoot)) throw new IOException("workflow_toolchain_sdk_missing");
        var names = new List<string>();
        foreach (string directory in Directory.EnumerateDirectories(sdkRoot)) {
            // Check before adding: exactly MaximumSdkVersions are allowed; reject the next directory.
            if (names.Count == MaximumSdkVersions) throw new IOException("workflow_toolchain_sdk_budget");
            names.Add(Path.GetFileName(directory));
        }
        if (names.Count == 0) throw new IOException("workflow_toolchain_sdk_budget");
        names.Sort(StringComparer.Ordinal);
        (int Major, int Minor, int Patch, string Preview) best = default; string? selected = null; bool ambiguous = false;
        foreach (string name in names) {
            var parsed = Parse(name);
            if (selected is null) { best = parsed; selected = name; continue; }
            int order = Compare(parsed, best);
            if (order > 0) { best = parsed; selected = name; ambiguous = false; }
            else if (order == 0) ambiguous = true;
        }
        return ambiguous || selected is null ? throw new IOException("workflow_toolchain_sdk_ambiguous") : selected;
    }
    internal static string InspectInstalledVersion(string toolPath)
        => Select(Path.Combine(Path.GetDirectoryName(WorkflowPhysicalPins.Absolute(toolPath))!, "sdk"));
    private static int Compare((int Major, int Minor, int Patch, string Preview) first, (int Major, int Minor, int Patch, string Preview) second)
    {
        if (first.Major != second.Major) return first.Major.CompareTo(second.Major);
        if (first.Minor != second.Minor) return first.Minor.CompareTo(second.Minor);
        if (first.Patch != second.Patch) return first.Patch.CompareTo(second.Patch);
        bool firstRelease = first.Preview.Length == 0, secondRelease = second.Preview.Length == 0;
        return firstRelease == secondRelease ? 0 : firstRelease ? 1 : -1;
    }
    private static (int Major, int Minor, int Patch, string Preview) Parse(string name)
    {
        int dash = name.IndexOf('-');
        string core = dash < 0 ? name : name[..dash];
        string preview = dash < 0 ? "" : name[(dash + 1)..];
        if (dash >= 0 && (preview.Length is < 1 or > 32 || preview.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-')))
            throw new IOException("workflow_toolchain_sdk_name");
        string[] parts = core.Split('.');
        if (parts.Length != 3) throw new IOException("workflow_toolchain_sdk_name");
        return (Part(parts[0]), Part(parts[1]), Part(parts[2]), preview);
    }
    private static int Part(string value)
    {
        if (value.Length is < 1 or > 5 || value.Any(c => !char.IsAsciiDigit(c)) ||
            !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
            throw new IOException("workflow_toolchain_sdk_name");
        return parsed;
    }
    public void Dispose()
    {
        Owner();
        if (closed) return;
        pins.Dispose(); closed = true;
    }
}
