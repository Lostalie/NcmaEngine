namespace Ncma.Assets.Authoring;

public sealed class AssetProjectPaths
{
    public AssetProjectPaths(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        ValidateRoot();
    }
    public string Root { get; }
    public string Resolve(string relative, bool requireFile = false)
    {
        AssetPaths.Validate(relative);
        return ResolveValidated(relative, requireFile);
    }
    internal string ResolveCache(string relative, bool requireFile = false)
    {
        AssetPaths.Validate(relative, "out/asset-authoring");
        return ResolveValidated(relative, requireFile);
    }
    internal string ResolveDerived(string relative, bool requireFile = false)
    {
        AssetPaths.Validate(relative, "out/assets"); return ResolveValidated(relative, requireFile);
    }
    private string ResolveValidated(string relative, bool requireFile)
    {
        ValidateRoot();
        string current = Root;
        string[] parts = relative.Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            current = Path.Combine(current, parts[i]);
            if (!Directory.Exists(Path.GetDirectoryName(current))) throw new DirectoryNotFoundException("Asset parent directory is missing.");
            var matches = new List<string>(2);
            int entries = 0;
            foreach (string entry in Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(current)!))
            {
                if (++entries > AssetCatalogScanner.MaxEntries) throw new ArgumentException("Asset parent exceeds entry budget.");
                if (string.Equals(Path.GetFileName(entry), parts[i], StringComparison.OrdinalIgnoreCase)) matches.Add(entry);
                if (matches.Count == 2) break;
            }
            if (matches.Count > 1 || (matches.Count == 1 && !string.Equals(Path.GetFileName(matches[0]), parts[i], StringComparison.Ordinal)))
                throw new ArgumentException("Asset path has a case collision or noncanonical casing.");
            if (matches.Count == 1)
            {
                RejectReparse(current);
                if (i < parts.Length - 1 && !Directory.Exists(current)) throw new ArgumentException("Asset parent is not a directory.");
            }
            else if (i < parts.Length - 1) throw new DirectoryNotFoundException("Asset parent directory is missing.");
        }
        if (requireFile && !File.Exists(current)) throw new FileNotFoundException("Asset file is missing.", current);
        return current;
    }
    internal void EnsureCacheDirectory(string relative, bool derived = false)
    {
        AssetPaths.Validate(relative + "/.probe", derived ? "out/assets" : "out/asset-authoring"); ValidateRoot();
        var leases = new List<Storage.WindowsAssetFile>();
        try
        {
            for (DirectoryInfo? directory = new(Root); directory is not null; directory = directory.Parent)
                leases.Add(Storage.WindowsAssetFile.Open(directory.FullName, directory: true));
            string current = Root;
            foreach (string part in relative.Split('/'))
            {
                current = Path.Combine(current, part);
                if (!Directory.Exists(current)) Directory.CreateDirectory(current);
                leases.Add(Storage.WindowsAssetFile.Open(current, directory: true));
            }
            if (derived) ResolveDerived(relative + "/.probe"); else ResolveCache(relative + "/.probe");
        }
        finally { foreach (var lease in leases) lease.Dispose(); }
    }
    private void ValidateRoot()
    {
        if (!Directory.Exists(Root)) throw new DirectoryNotFoundException("Asset project root does not exist.");
        for (DirectoryInfo? directory = new(Root); directory is not null; directory = directory.Parent)
            RejectReparse(directory.FullName);
    }
    internal static void RejectReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Asset paths may not traverse links or reparse points.");
    }
}
