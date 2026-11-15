using System.Text.Json;
using Ncma.Editor.Core;

namespace Ncma.Editor.Services;

// Called ONLY by the local host/UI. Bounded reads, no capture creation, restore,
// network or execution. The production lease independently re-pins these bytes.
internal static class WorkflowLocalInputs
{
    internal static (WorkflowVerificationTask Task, WorkflowBuildEnvironment Environment) Offline(
        string workspace, Guid captureId, string cacheRoot, Guid workflowId, string planHash,
        EditorViewStamp stamp, string configuration, string variant, string? expectedReviewedBundle = null, WorkflowCreatedSource? created = null,
        WorkflowAssetSource? assets = null)
    {
        workspace = WorkflowBuildCommand.CommandPath(workspace); cacheRoot = WorkflowBuildCommand.CommandPath(cacheRoot);
        using var pins = new WorkflowPhysicalPins(256, 1024);
        pins.Directory(workspace); pins.Directory(cacheRoot);
        string manifestPath = Path.Combine(workspace, "out", "captured-build", captureId.ToString("N"), "capture.json");
        var manifestHash = pins.Observe(manifestPath, 4194304);
        byte[] bytes = pins.ReadFile(manifestPath, manifestHash.Sha256, manifestHash.Bytes, 4194304);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 }); var manifest = document.RootElement;
        RequireReviewedCapture(manifest, expectedReviewedBundle); // BEFORE tool/cache scan or preparer startup
        if (created is not null && (created.CaptureId != captureId || created.ManifestSha256 != manifestHash.Sha256 ||
            created.SourceSha256 != manifest.GetProperty("sourceInputsSha256").GetString() || created.RecipeSha256 != manifest.GetProperty("recipeSha256").GetString() ||
            created.Files != manifest.GetProperty("files").GetArrayLength())) throw new IOException("workflow_created_capture_changed");
        string tool = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        string runner = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var toolHash = pins.Observe(tool, 64 * 1024 * 1024); var runnerHash = pins.Observe(runner, 64 * 1024 * 1024);
        var helpers = new List<WorkflowEvidenceFile>();
        foreach (string relative in WorkflowCaptureBridge.RequiredHelpers()) {
            var observed = pins.Observe(Path.Combine(workspace, relative.Replace('/', Path.DirectorySeparatorChar)), 64 * 1024 * 1024);
            helpers.Add(new(relative, observed.Bytes, observed.Sha256));
        }
        var files = new List<WorkflowEvidenceFile>(); long total = 0; int directories = 0;
        foreach (string id in Directory.EnumerateDirectories(cacheRoot)) {
            if (++directories > 512) throw new IOException("workflow_offline_cache_directory_budget"); pins.Directory(id);
            foreach (string version in Directory.EnumerateDirectories(id)) {
                if (++directories > 512) throw new IOException("workflow_offline_cache_directory_budget"); pins.Directory(version);
                string archiveName = Path.GetFileName(id) + "." + Path.GetFileName(version) + ".nupkg";
                string archive = Path.Combine(version, archiveName);
                // No recursive scan, links or fallback sources. Each enumerated
                // package version must provide its original archive.
                if (files.Count == WorkflowOfflineFeed.MaximumArchives) throw new IOException("workflow_offline_archive_budget");
                var hash = pins.Observe(archive, 64 * 1024 * 1024);
                if (hash.Bytes > 256L * 1024 * 1024 - total) throw new IOException("workflow_offline_archive_bytes"); total += hash.Bytes;
                files.Add(new("offline-feed/" + Path.GetFileName(id) + "/" + Path.GetFileName(version) + "/" + archiveName, hash.Bytes, hash.Sha256));
            }
        }
        var binding = new WorkflowVerificationBinding(1, Guid.NewGuid(), stamp.SessionId, workflowId, planHash, stamp, captureId,
            manifest.GetProperty("sourceInputsSha256").GetString()!, manifestHash.Sha256, manifest.GetProperty("recipeSha256").GetString()!,
            manifest.GetProperty("reviewBundleSha256").GetString(), "editor-services-restore-offline-v1", configuration, variant,
            WorkflowToolchain.InspectInstalledVersion(tool), toolHash.Sha256) { Assets = assets };
        return (new(binding, files, []), new(workspace, tool, runner, runnerHash.Sha256, helpers, TimeSpan.FromMinutes(10)));
    }
    internal static void RequireReviewedCapture(JsonElement manifest, string? expectedReviewedBundle)
    {
        // Reject duplicate envelope fields, including conflicting review hashes.
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in manifest.EnumerateObject()) if (!keys.Add(field.Name)) throw new IOException("workflow_capture_duplicate_field");
        if (manifest.GetProperty("kind").GetString() != "ncma.captured-build.v1" || manifest.GetProperty("schemaVersion").GetInt32() != 1)
            throw new IOException("workflow_capture_manifest_kind");
        string? actual = manifest.GetProperty("reviewBundleSha256").GetString();
        if (expectedReviewedBundle is not null) WorkflowEvidenceRules.Hash(expectedReviewedBundle);
        if (actual != expectedReviewedBundle) throw new IOException("workflow_capture_review_mismatch");
    }
}
