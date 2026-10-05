using System.Runtime.InteropServices;
using System.Text.Json;

namespace Ncma.Assets.Authoring;

// Called only by trusted Editor bootstrap AFTER validating its deployment manifest. Not an Agent input.
// Hashes are package consistency evidence, not signatures or permission to load third-party tools.
public static class ImportToolDeployment
{
    public static ImportWorkerLaunch FromValidatedEditorPackage(string root)
    {
        root = Path.GetFullPath(root);
        string manifestPath = Path.Combine(root, "deployment-manifest.json");
        if (new FileInfo(manifestPath).Length is <= 0 or > 4 * 1024 * 1024) throw new ArgumentException("Import manifest budget.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath), new JsonDocumentOptions { MaxDepth = 16 });
        var manifest = document.RootElement;
        if (manifest.GetProperty("product").GetString() is not ("NcmaEngine-editor-candidate" or "NcmaEngine-editor")) throw new ArgumentException("Import tools require Editor package.");
        const string prefix = "tools/import-worker/";
        var files = manifest.GetProperty("files").EnumerateArray().Where(f => (f.GetProperty("path").GetString() ?? "").StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        if (files.Length is < 6 or > 16) throw new ArgumentException("Import tools missing/budget.");
        var entries = new Dictionary<string, ImportToolFile>(StringComparer.Ordinal);
        foreach (var file in files) {
            string relative = file.GetProperty("path").GetString()!; AssetPaths.Validate(relative, "tools/import-worker");
            if (relative.Split('/').Length != 3) throw new ArgumentException("Nested import dependency denied.");
            string hash = file.GetProperty("sha256").GetString()!; AssetRecordCodec.ValidateHash(hash);
            if (!entries.TryAdd(Path.GetFileName(relative), new(Path.Combine(root, relative), hash))) throw new ArgumentException("Duplicate import dependency.");
        }
        foreach (string required in new[] { "Ncma.Asset.ImportWorker.dll", "Ncma.Asset.ImportWorker.deps.json", "Ncma.Asset.ImportWorker.runtimeconfig.json", "Ncma.Asset.Import.dll", "Ncma.Assets.dll", "NcmaImportKernel.dll" })
            if (!entries.ContainsKey(required)) throw new ArgumentException("Required import dependency missing.");
        var worker = entries["Ncma.Asset.ImportWorker.dll"]; var kernel = entries["NcmaImportKernel.dll"];
        string dotnet = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
        return new(dotnet, worker.Path, worker.Hash, kernel.Path, kernel.Hash, entries.Values.Where(f => f.Path != worker.Path).ToArray());
    }
}
