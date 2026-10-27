using System.Security.Cryptography;
using System.Text;

namespace Ncma.Editor.App;

// Executables, dependencies and package manifests are immutable inputs, never user storage.
// This is host composition only: no model/MCP setting, credential storage or new wire surface.
internal static class EditorUserDataPaths
{
    internal static string ForHost(string? projectRoot, bool transient, Guid run) => Resolve(
        AppContext.BaseDirectory, projectRoot,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Path.GetTempPath(), transient, run);

    internal static string Resolve(string applicationRoot, string? projectRoot,
        string localDataRoot, string temporaryRoot, bool transient, Guid run)
    {
        string app = Absolute(applicationRoot);
        if (run == Guid.Empty) throw new ArgumentException("editor_user_data_run_required");
        string root = transient
            ? Path.Combine(Absolute(temporaryRoot), "NcmaEngine", "EditorRuns", run.ToString("N"))
            : projectRoot is null
                ? Path.Combine(Absolute(localDataRoot), "NcmaEngine", "Editor")
                : Path.Combine(Absolute(projectRoot), "out", "user");
        if (!transient && projectRoot is not null && (Contains(app, root) || Contains(root, app))) {
            // Bundled samples cannot store state next to their immutable project inputs.
            // Use the canonical project location (not a copied project UUID or this run's
            // UUID) so distinct installations stay isolated and subsequent runs reuse state.
            string project = Absolute(projectRoot);
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                OperatingSystem.IsWindows() ? project.ToUpperInvariant() : project)));
            root = Path.Combine(Absolute(localDataRoot), "NcmaEngine", "Projects", key, "Editor");
        }
        root = Path.GetFullPath(root);
        if (Contains(app, root) || Contains(root, app))
            throw new ArgumentException("editor_user_data_overlaps_application");
        // Do not let a user-data junction redirect writes back into the immutable package.
        for (string? part = root; part is not null; part = Path.GetDirectoryName(part))
            if (Path.Exists(part) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("editor_user_data_link_not_supported");
        return root;
    }

    private static string Absolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("editor_user_data_absolute_root_required");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    private static bool Contains(string parent, string child) =>
        string.Equals(parent, child, StringComparison.OrdinalIgnoreCase) ||
        child.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
