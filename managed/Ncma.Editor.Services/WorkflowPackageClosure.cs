using System.Globalization;

namespace Ncma.Editor.Services;

internal sealed record WorkflowPackageFile(string Path, string PhysicalPath, long Bytes, string Sha256);

// Trusted-local byte inspection ONLY. Roots are supplied by the local host, not
// obtained from model arguments. Archive SHA512 is checked against assets; file
// SHA256 against the exact task declaration. This is NOT a package signature or
// archive-to-extracted-file equivalence proof, SDK/import closure, or launch grant.
internal sealed class WorkflowPackageClosure : IDisposable
{
    internal const int MaximumFiles = 2048;
    internal const long MaximumBytes = 512L * 1024 * 1024;
    private readonly int thread = System.Environment.CurrentManagedThreadId;
    private readonly WorkflowVerificationTask task;
    private readonly WorkflowRestoreGraph graph;
    private readonly string[] roots;
    private readonly WorkflowPhysicalPins pins = new(MaximumFiles, 4096);
    private WorkflowPackageFile[] files = [];
    private bool attempted, ready, closed;
    internal string? Fingerprint { get; private set; }
    internal bool CompilationAuthorized => false;
    internal WorkflowPackageClosure(WorkflowVerificationTask task, WorkflowRestoreGraph graph, IEnumerable<string> approvedRoots)
    {
        this.task = task; this.graph = graph;
        if (task.Fingerprint != graph.TaskFingerprint) throw new IOException("workflow_package_task_binding");
        var list = new List<string>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in approvedRoots) {
            if (list.Count == 8) throw new IOException("workflow_package_root_budget");
            string path = WorkflowBuildCommand.CommandPath(root);
            if (Path.GetPathRoot(path) == path || !seen.Add(path)) throw new IOException("workflow_package_root_alias");
            list.Add(path);
        }
        if (list.Count == 0) throw new IOException("workflow_package_roots_missing");
        roots = list.ToArray(); // order is explicit and fingerprinted, never sorted silently
    }
    private void Owner()
    {
        if (System.Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("workflow_package_thread");
        ObjectDisposedException.ThrowIf(closed, this);
    }
    internal void Prepare()
    {
        Owner(); if (attempted) throw new IOException("workflow_package_single_use"); attempted = true;
        var assets = graph.CopyAssets();
        var requiredRoots = assets.SelectMany(a => a.CopyPackageFolders()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!requiredRoots.SetEquals(roots)) throw new IOException("workflow_package_roots_binding");
        var packages = new Dictionary<string, WorkflowRestorePackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets) foreach (var package in asset.CopyPackages()) {
            if (packages.TryGetValue(package.RelativePath, out var previous)) {
                if (previous.Sha512 != package.Sha512 || !previous.Files.SequenceEqual(package.Files, StringComparer.Ordinal))
                    throw new IOException("workflow_package_project_disagreement");
            } else {
                if (packages.Count == WorkflowRestoreAssets.MaximumLibraries) throw new IOException("workflow_package_budget");
                packages.Add(package.RelativePath, package);
            }
        }
        // Root identities are held before selecting paths; unavailable roots fail
        // explicitly rather than silently changing NuGet fallback precedence.
        foreach (string root in roots) pins.Directory(root);
        var declared = task.CopyInputs().Where(f => f.Path.StartsWith("packages/", StringComparison.Ordinal)).ToDictionary(f => f.Path, StringComparer.Ordinal);
        var selected = new List<(WorkflowPackageFile File, string? ArchiveSha512)>(); long bytes = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in packages.Values.OrderBy(p => p.RelativePath, StringComparer.Ordinal)) {
            string directory = package.RelativePath.Replace('/', Path.DirectorySeparatorChar); int rootIndex = -1;
            for (int i = 0; i < roots.Length; i++) {
                string path = Path.Combine(roots[i], directory);
                FileAttributes attributes;
                try { attributes = File.GetAttributes(path); }
                catch (DirectoryNotFoundException) { continue; }
                catch (FileNotFoundException) { continue; }
                if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("workflow_package_directory_identity");
                if (rootIndex >= 0) throw new IOException("workflow_package_ambiguous_root");
                rootIndex = i;
            }
            if (rootIndex < 0) throw new IOException("workflow_package_missing");
            string[] identity = package.RelativePath.Split('/');
            string archive = identity[0] + "." + identity[1] + ".nupkg";
            foreach (string relative in package.Files.Append(archive).Distinct(StringComparer.OrdinalIgnoreCase)) {
                string logical = "packages/" + rootIndex.ToString(CultureInfo.InvariantCulture) + "/" + package.RelativePath + "/" + relative;
                if (!declared.TryGetValue(logical, out var file) || !seen.Add(logical)) throw new IOException("workflow_package_input_missing");
                if (selected.Count == MaximumFiles || file.Bytes > 64L * 1024 * 1024 || file.Bytes > MaximumBytes - bytes)
                    throw new IOException("workflow_package_input_budget");
                bytes += file.Bytes;
                selected.Add((new(logical, Path.Combine(roots[rootIndex], directory, relative.Replace('/', Path.DirectorySeparatorChar)), file.Bytes, file.Sha256),
                    string.Equals(relative, archive, StringComparison.OrdinalIgnoreCase) ? package.Sha512 : null));
            }
        }
        if (declared.Count != seen.Count) throw new IOException("workflow_package_extra_input");
        // Validate the whole declared set before pinning payload. Any partial pins
        // remain owned on failure until explicit Dispose, never automatic retries.
        foreach (var item in selected) {
            var file = item.File;
            if (item.ArchiveSha512 is { } sha512) pins.FileWithSha512(file.PhysicalPath, file.Sha256, file.Bytes, sha512);
            else pins.File(file.PhysicalPath, file.Sha256, file.Bytes);
        }
        _ = graph.CopyAssets(); files = selected.Select(i => i.File).ToArray();
        Fingerprint = WorkflowEvidenceRules.Digest(new { version = 1, task = task.Fingerprint, graph = graph.Fingerprint, roots, files }); ready = true;
    }
    internal WorkflowPackageFile[] CopyFiles()
    {
        Owner(); if (!ready) throw new IOException("workflow_package_not_ready");
        _ = graph.CopyAssets(); return (WorkflowPackageFile[])files.Clone();
    }
    public void Dispose()
    {
        if (System.Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("workflow_package_thread");
        if (closed) return; pins.Dispose(); ready = false; closed = true;
        // The graph is borrowed, never disposed here. Its owner closes it LAST.
    }
}
