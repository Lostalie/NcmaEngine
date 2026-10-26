using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

// Test-only entry, never shipped. No engine reference is used before the isolated load.
internal static class AiRelocatedProductBootstrap
{
    private sealed class ProductContext(string root, Dictionary<string, string> files) : AssemblyLoadContext("m8.4-checked-product", false)
    {
        private readonly string framework = Path.GetFullPath(RuntimeEnvironment.GetRuntimeDirectory());
        protected override Assembly? Load(AssemblyName name)
        {
            string leaf = name.Name + ".dll";
            if (files.TryGetValue(leaf, out string? hash)) {
                string path = Path.Combine(root, leaf);
                if (Hash(path) != hash || AssemblyName.GetAssemblyName(path).FullName != name.FullName)
                    throw new InvalidDataException("Product assembly identity/hash mismatch: " + leaf);
                return LoadFromAssemblyPath(path);
            }
            // Only shared runtime framework assemblies may resolve outside the product.
            // In particular, missing Ncma/third-party libraries cannot borrow test-bin DLLs.
            var shared = Default.Assemblies.FirstOrDefault(a => a.GetName().FullName == name.FullName && !a.IsDynamic &&
                Path.GetFullPath(a.Location).StartsWith(framework, StringComparison.OrdinalIgnoreCase));
            if (shared is not null) return shared;
            string runtimeFile = Path.Combine(framework, leaf);
            if (File.Exists(runtimeFile) && AssemblyName.GetAssemblyName(runtimeFile).FullName == name.FullName)
                return Default.LoadFromAssemblyPath(runtimeFile);
            throw new FileNotFoundException("No test/repository product fallback: " + name.FullName);
        }
    }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    internal static int Run(string requestPath)
    {
        try {
            if (new FileInfo(requestPath).Length > 16384) throw new InvalidDataException("Request budget");
            using var request = JsonDocument.Parse(File.ReadAllBytes(requestPath)); var value = request.RootElement;
            string root = Path.GetFullPath(value.GetProperty("productRoot").GetString()!);
            string output = Path.GetFullPath(value.GetProperty("output").GetString()!);
            string manifestPath = Path.Combine(root, "deployment-manifest.json");
            if (Hash(manifestPath) != value.GetProperty("manifestSha256").GetString()) throw new InvalidDataException("Manifest pin");
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in manifest.RootElement.GetProperty("files").EnumerateArray()) {
                string relative = entry.GetProperty("path").GetString()!;
                string path = Path.GetFullPath(Path.Combine(root, relative));
                if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    !files.TryAdd(relative, entry.GetProperty("sha256").GetString()!) ||
                    Hash(path) != files[relative]) throw new InvalidDataException("Checked product payload");
            }
            var context = new ProductContext(root, files);
            // Only this one exact trusted test assembly is permitted outside the checked map.
            var probe = context.LoadFromAssemblyPath(typeof(AiRelocatedProductBootstrap).Assembly.Location);
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", root + Path.DirectorySeparatorChar);
            if (Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory) != root) throw new InvalidDataException("Product resource root");
            probe.GetType("AiRelocatedProductProbe", true)!.GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [requestPath]);
            var assemblies = context.Assemblies.Where(a => a != probe && !a.IsDynamic).Select(a => new {
                name = a.GetName().Name!, path = a.Location, sha256 = Hash(a.Location)
            }).ToArray();
            if (assemblies.Length < 5 || assemblies.Any(a => !a.path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !files.TryGetValue(Path.GetRelativePath(root, a.path).Replace('\\', '/'), out string? sha) || sha != a.sha256))
                throw new InvalidDataException("Loaded assembly escaped checked package");
            if (!value.GetProperty("ai").GetBoolean() && assemblies.Any(a => a.name is "Ncma.AI" or "Ncma.Modules" or "Ncma.Editor.Modules"))
                throw new InvalidDataException("Default product loaded optional AI");
            File.WriteAllText(Path.Combine(output, "loaded-product-assemblies.json"), JsonSerializer.Serialize(assemblies));
            Console.WriteLine("checked_relocated_editor edit_play_undo; resources released"); return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
