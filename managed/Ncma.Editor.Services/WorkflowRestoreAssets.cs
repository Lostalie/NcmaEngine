using System.Text.Json;

namespace Ncma.Editor.Services;

internal sealed record WorkflowRestorePackage(string Identity, string RelativePath, string Sha512, string[] Files);

// Bounded dependency discovery, NOT restore provenance, SDK selection, package
// byte verification, a complete MSBuild graph, or permission to launch a build.
internal sealed class WorkflowRestoreAssets
{
    internal const int MaximumBytes = 4 * 1024 * 1024, MaximumLibraries = 512, MaximumFiles = 16384;
    private readonly string[] projects, folders;
    private readonly WorkflowRestorePackage[] packages;
    internal string ProjectPath { get; }
    internal bool CompilationAuthorized => false;
    internal string[] CopyProjects() => (string[])projects.Clone();
    internal string[] CopyPackageFolders() => (string[])folders.Clone();
    internal WorkflowRestorePackage[] CopyPackages() => packages.Select(p => p with { Files = (string[])p.Files.Clone() }).ToArray();
    private WorkflowRestoreAssets(string project, string[] projects, string[] folders, WorkflowRestorePackage[] packages)
        => (ProjectPath, this.projects, this.folders, this.packages) = (project, projects, folders, packages);
    private static IOException Reject(string code) => new(code);
    private static string Text(JsonElement value, int limit = 1024)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text || text.Length is < 1 || text.Length > limit || text.Any(char.IsControl))
            throw Reject("workflow_restore_text");
        return text;
    }
    private static JsonElement Property(JsonElement value, string key)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(key, out var result)) throw Reject("workflow_restore_missing_field");
        return result;
    }
    private static JsonElement Object(JsonElement value)
        => value.ValueKind == JsonValueKind.Object ? value : throw Reject("workflow_restore_object");
    private static void UniqueTree(JsonElement value, ref int count)
    {
        if (++count > 100000) throw Reject("workflow_restore_node_budget");
        if (value.ValueKind == JsonValueKind.Object) {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in value.EnumerateObject()) {
                if (p.Name.Length > 2048 || !keys.Add(p.Name)) throw Reject("workflow_restore_duplicate_field");
                UniqueTree(p.Value, ref count);
            }
        } else if (value.ValueKind == JsonValueKind.Array) foreach (var child in value.EnumerateArray()) UniqueTree(child, ref count);
    }
    internal static string ProjectName(string relative)
    {
        _ = Relative(relative);
        string[] parts = relative.Split('/');
        if (parts.Length != 3 || parts[0] != "managed" || parts[1].Length is < 1 or > 96 ||
            parts[1].Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-') ||
            parts[1].StartsWith('.') || parts[1].EndsWith('.') || parts[2] != parts[1] + ".csproj") throw Reject("workflow_restore_project_layout");
        return parts[1];
    }
    private static string DiskPath(string value)
        => WorkflowBuildCommand.CommandPath(value.Replace('/', Path.DirectorySeparatorChar));
    private static bool Same(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
    private static string ProjectUnder(string source, string value)
    {
        string full = DiskPath(value);
        if (!full.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw Reject("workflow_restore_foreign_project");
        string relative = Path.GetRelativePath(source, full).Replace(Path.DirectorySeparatorChar, '/');
        _ = ProjectName(relative); return full;
    }
    private static string Relative(string value)
    {
        if (value.Length is < 1 or > 240 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '/' and not '.' and not '-' and not '_')) throw Reject("workflow_restore_relative_path");
        foreach (string part in value.Split('/')) {
            string stem = part.Split('.')[0].ToUpperInvariant();
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || stem is "CON" or "PRN" or "AUX" or "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '0' and <= '9'))
                throw Reject("workflow_restore_relative_path");
        }
        return value;
    }
    internal static WorkflowRestoreAssets Parse(ReadOnlyMemory<byte> bytes, string captureSource, string relativeProject, string expectedOutput)
    {
        if (bytes.Length is < 2 or > MaximumBytes) throw Reject("workflow_restore_byte_budget");
        string source = DiskPath(captureSource); string name = ProjectName(relativeProject);
        string expectedProject = ProjectUnder(source, Path.Combine(source, relativeProject.Replace('/', Path.DirectorySeparatorChar)));
        string output = DiskPath(expectedOutput);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 }); var root = document.RootElement;
        int nodes = 0; UniqueTree(root, ref nodes);
        var version = Property(root, "version");
        if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int number) || number != 3) throw Reject("workflow_restore_version");
        var project = Property(root, "project"); var restore = Property(project, "restore");
        foreach (string key in new[] { "projectPath", "projectUniqueName" })
            if (!Same(ProjectUnder(source, Text(Property(restore, key))), expectedProject)) throw Reject("workflow_restore_project_binding");
        if (Text(Property(restore, "projectName")) != name || Text(Property(restore, "projectStyle")) != "PackageReference" ||
            !Same(DiskPath(Text(Property(restore, "outputPath"))), output)) throw Reject("workflow_restore_output_binding");
        var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int frameworks = 0;
        foreach (var framework in Object(Property(restore, "frameworks")).EnumerateObject()) {
            if (++frameworks > 16) throw Reject("workflow_restore_framework_budget");
            foreach (var reference in Object(Property(framework.Value, "projectReferences")).EnumerateObject()) {
                string path = ProjectUnder(source, reference.Name);
                if (!Same(path, ProjectUnder(source, Text(Property(reference.Value, "projectPath"))))) throw Reject("workflow_restore_reference_identity");
                refs.Add(path); if (refs.Count > 128) throw Reject("workflow_restore_project_budget");
            }
        }
        if (frameworks == 0) throw Reject("workflow_restore_framework_missing");
        var folderSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in Object(Property(root, "packageFolders")).EnumerateObject()) {
            string path = DiskPath(folder.Name);
            if (!folderSet.Add(path) || folderSet.Count > 8) throw Reject("workflow_restore_package_roots");
        }
        if (folderSet.Count == 0 || !folderSet.Contains(DiskPath(Text(Property(restore, "packagesPath"))))) throw Reject("workflow_restore_package_root_missing");
        var libraries = new Dictionary<string, (string Kind, HashSet<string> Files)>(StringComparer.OrdinalIgnoreCase);
        var packageRows = new List<WorkflowRestorePackage>(); int fileCount = 0;
        Span<byte> packageHash = stackalloc byte[64];
        foreach (var library in Object(Property(root, "libraries")).EnumerateObject()) {
            string identity = Relative(library.Name); if (identity.Split('/').Length != 2 || libraries.Count >= MaximumLibraries) throw Reject("workflow_restore_library_budget");
            string kind = Text(Property(library.Value, "type")); var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (kind == "package") {
                string path = Relative(Text(Property(library.Value, "path")));
                if (!Same(path, identity)) throw Reject("workflow_restore_package_identity");
                string sha512 = Text(Property(library.Value, "sha512"), 128);
                if (!Convert.TryFromBase64String(sha512, packageHash, out int written) || written != 64) throw Reject("workflow_restore_package_hash");
                var listed = Property(library.Value, "files"); if (listed.ValueKind != JsonValueKind.Array) throw Reject("workflow_restore_file_array");
                foreach (var file in listed.EnumerateArray()) {
                    if (++fileCount > MaximumFiles || !files.Add(Relative(Text(file, 240)))) throw Reject("workflow_restore_file_budget_or_alias");
                }
                if (files.Count == 0) throw Reject("workflow_restore_package_files_missing");
                // Case-insensitive duplicate rejection also prevents Windows aliases.
                foreach (string file in files) for (int slash = file.IndexOf('/'); slash >= 0; slash = file.IndexOf('/', slash + 1))
                    if (files.Contains(file[..slash])) throw Reject("workflow_restore_file_directory_collision");
                packageRows.Add(new(identity, path, sha512, files.Order(StringComparer.Ordinal).ToArray()));
            } else if (kind == "project") {
                string relative = Text(Property(library.Value, "msbuildProject"));
                if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Contains('%') || relative.Contains('$')) throw Reject("workflow_restore_project_reference");
                string full = ProjectUnder(source, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(expectedProject)!, relative.Replace('/', Path.DirectorySeparatorChar))));
                string other = Text(Property(library.Value, "path"));
                if (!string.Equals(relative, other, StringComparison.Ordinal)) throw Reject("workflow_restore_project_reference");
                refs.Add(full); if (refs.Count > 128) throw Reject("workflow_restore_project_budget");
            } else throw Reject("workflow_restore_library_kind");
            if (!libraries.TryAdd(identity, (kind, files))) throw Reject("workflow_restore_library_alias");
        }
        int targetCount = 0;
        foreach (var target in Object(Property(root, "targets")).EnumerateObject()) {
            if (++targetCount > 32) throw Reject("workflow_restore_target_budget");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in Object(target.Value).EnumerateObject()) {
                if (!seen.Add(item.Name) || !libraries.TryGetValue(item.Name, out var library) || Text(Property(item.Value, "type")) != library.Kind) throw Reject("workflow_restore_target_library");
                if (library.Kind != "package") continue;
                foreach (string section in new[] { "compile", "runtime", "native", "build", "buildMultiTargeting", "buildTransitive", "contentFiles", "resource", "runtimeTargets" }) {
                    if (!item.Value.TryGetProperty(section, out var assets)) continue;
                    foreach (var asset in Object(assets).EnumerateObject()) {
                        string path = Relative(asset.Name);
                        if (!library.Files.Contains(path)) throw Reject("workflow_restore_target_file_unlisted");
                    }
                }
            }
        }
        if (targetCount == 0) throw Reject("workflow_restore_target_missing");
        return new(expectedProject, refs.Order(StringComparer.Ordinal).ToArray(), folderSet.Order(StringComparer.Ordinal).ToArray(), packageRows.OrderBy(p => p.Identity, StringComparer.Ordinal).ToArray());
    }
}
