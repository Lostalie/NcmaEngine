using System.Security.Cryptography;
using System.Text.Json;
namespace Ncma.Application;

// Consistency verification of a trusted package, not signing or a code-execution sandbox.
public static class DeploymentManifest
{
    public const string FileName = "deployment-manifest.json";
    public static void Validate(string directory)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        for (var item = new DirectoryInfo(root); item is not null; item = item.Parent)
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("manifest_reparse_root");
        string manifestPath = Path.Combine(root, FileName);
        if ((File.GetAttributes(manifestPath) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("manifest_reparse_file");
        if (new FileInfo(manifestPath).Length is <= 0 or > 4 * 1024 * 1024) throw new ArgumentException("manifest_size");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath), new JsonDocumentOptions { MaxDepth = 16 });
        RejectDuplicates(document.RootElement);
        var json = document.RootElement;
        if (json.GetProperty("schemaVersion").GetInt32() != 1 || json.GetProperty("rid").GetString() != "win-x64" ||
            json.GetProperty("tfm").GetString() != "net8.0" || json.GetProperty("configuration").GetString() is not ("Debug" or "Release") ||
            json.GetProperty("publishMode").GetString() is not ("framework-dependent" or "self-contained")) throw new ArgumentException("manifest_version");
        var files = json.GetProperty("files");
        if (files.GetArrayLength() is <= 0 or > 8192) throw new ArgumentException("manifest_file_budget");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.EnumerateArray())
        {
            string relative = file.GetProperty("path").GetString() ?? "";
            if (relative.Length is 0 or > 1024 || Path.IsPathRooted(relative) || relative.Contains(':') ||
                relative.Split('/', '\\').Any(p => p is "" or "." or "..") || !paths.Add(relative)) throw new ArgumentException("manifest_path");
            string path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("manifest_escape");
            string current = root;
            foreach (string part in Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar))
            { current = Path.Combine(current, part); if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("manifest_reparse_file"); }
            using var stream = File.OpenRead(path);
            string expected = file.GetProperty("sha256").GetString() ?? "";
            if (expected.Length != 64 || !expected.All(Uri.IsHexDigit) || stream.Length != file.GetProperty("size").GetInt64() ||
                !Convert.ToHexString(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("manifest_integrity");
        }
    }
    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in element.EnumerateObject()) { if (!names.Add(p.Name)) throw new ArgumentException("manifest_duplicate_field"); RejectDuplicates(p.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }
}
