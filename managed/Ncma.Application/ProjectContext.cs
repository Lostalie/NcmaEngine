using System.Text.Json;
using System.Text.Json.Serialization;
namespace Ncma.Application;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProjectPlugin(string Id, string Path);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProjectConfiguration(int SchemaVersion, Guid ProjectId, string Name,
    string StartupScene, string GameplayAssembly, string Renderer, ProjectPlugin[] Plugins, bool PhysicsEnabled = false, Guid? SceneCamera = null, string? AssetPackage = null);
public sealed class ProjectContext
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private readonly ProjectConfiguration _configuration;
    public string Root { get; }
    public ProjectConfiguration Configuration => _configuration with { Plugins = (ProjectPlugin[])_configuration.Plugins.Clone() };
    public string StartupScenePath { get; }
    public string GameplayAssemblyPath { get; }
    public string? AssetPackagePath { get; }
    private ProjectContext(string root, ProjectConfiguration configuration)
    {
        Root = Path.GetFullPath(root); RejectReparse(Root);
        for (var ancestor = new DirectoryInfo(Root); ancestor is not null; ancestor = ancestor.Parent)
            if ((ancestor.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Project root traverses a reparse point.");
        if (configuration.SchemaVersion != 1 || configuration.ProjectId == Guid.Empty ||
            string.IsNullOrWhiteSpace(configuration.Name) || configuration.Name.Length > 256 ||
            configuration.Renderer is not ("Direct3D11" or "Vulkan" or "Null") || configuration.Plugins is null ||
            configuration.Plugins.Length > 64 || configuration.SceneCamera == Guid.Empty) throw new ArgumentException("Invalid project configuration.");
        _configuration = configuration with { Plugins = (ProjectPlugin[])configuration.Plugins.Clone() };
        StartupScenePath = Resolve(configuration.StartupScene, ".ncmascene");
        if (new FileInfo(StartupScenePath).Length > Ncma.Scene.SceneDocumentCodec.MaxBytes) throw new ArgumentException("Startup scene exceeds budget.");
        _ = Ncma.Scene.SceneDocumentCodec.Decode(File.ReadAllBytes(StartupScenePath));
        GameplayAssemblyPath = Resolve(configuration.GameplayAssembly, ".dll");
        if (configuration.AssetPackage is { } package) {
            if (!package.StartsWith("assets/", StringComparison.Ordinal) || package.Contains('\\') || !package.EndsWith(".ncpak", StringComparison.Ordinal))
                throw new ArgumentException("Expected explicit assets/ runtime package.");
            AssetPackagePath = Resolve(package, ".ncpak");
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plugin in _configuration.Plugins)
        {
            if (plugin is null || string.IsNullOrWhiteSpace(plugin.Id) || plugin.Id.Length > 128 ||
                !plugin.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-') || !ids.Add(plugin.Id))
                throw new ArgumentException("Invalid or duplicate plugin ID.");
            _ = Resolve(plugin.Path, ".dll");
        }
    }
    public string Resolve(string relative, string extension)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 1024 || Path.IsPathRooted(relative) ||
            relative.Contains(':') || relative.Split('/', '\\').Any(p => p is "" or "." or "..") ||
            !relative.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Expected a bounded project-relative asset path.");
        string path = Path.GetFullPath(Path.Combine(Root, relative));
        if (!path.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Project path escapes its root.");
        string current = Root;
        foreach (string part in Path.GetRelativePath(Root, path).Split(Path.DirectorySeparatorChar))
        { current = Path.Combine(current, part); RejectReparse(current); }
        if (!File.Exists(path)) throw new FileNotFoundException("Configured project asset is missing.", path);
        return path;
    }
    private static void RejectReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Project paths may not traverse reparse points.");
    }
    public static ProjectContext Load(string path)
    {
        string full = Path.GetFullPath(path);
        if (!full.EndsWith(".ncmaproject", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Expected .ncmaproject.");
        RejectReparse(full);
        if (new FileInfo(full).Length > 65536) throw new ArgumentException("Project exceeds 64 KiB.");
        byte[] bytes = File.ReadAllBytes(full);
        if (bytes.Length > 65536) throw new ArgumentException("Project exceeds 64 KiB.");
        using var doc = JsonDocument.Parse(bytes);
        RejectDuplicates(doc.RootElement);
        var config = JsonSerializer.Deserialize<ProjectConfiguration>(bytes, Json) ?? throw new ArgumentException("Empty project.");
        return new(Path.GetDirectoryName(full)!, config);
    }
    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            { if (!names.Add(property.Name)) throw new ArgumentException("Duplicate project field."); RejectDuplicates(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }
    // Explicit trusted save; never writes engine preferences or silently changes asset paths.
    public void Save(string projectFileName)
    {
        if (Path.GetFileName(projectFileName) != projectFileName || !projectFileName.EndsWith(".ncmaproject", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Expected a project file name, not a path.");
        string target = Path.Combine(Root, projectFileName);
        if (File.Exists(target)) RejectReparse(target);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(_configuration, Json);
        if (bytes.Length > 65536) throw new ArgumentException("Project exceeds 64 KiB.");
        string temporary = Path.Combine(Root, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
