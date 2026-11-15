using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Editor.Core;

namespace Ncma.Editor.Services;

// Local trusted presenter handoff, NOT build/restore/install approval. No public
// constructor, deserialization, remote capability or retained callback. The host
// owns exact object identity; SHA alone never recreates this handoff.
public sealed class WorkflowSourceReview
{
    private readonly WorkflowEvidenceFile[] helpers;
    internal WorkflowEvidenceFile[] CopyHelpers() => (WorkflowEvidenceFile[])helpers.Clone();
    public Guid WorkflowId { get; }
    public string Workspace { get; }
    public string PlanHash { get; }
    public EditorViewStamp Source { get; }
    public WorkflowAssetSource? Assets { get; }
    public string PatchFingerprint { get; }
    public string BundleSha256 { get; }
    public string Fingerprint { get; }
    internal bool Consumed, Revoked, CaptureRequested;
    internal Guid CaptureId;
    internal WorkflowSourceReview(Guid workflow, string workspace, string planHash, EditorViewStamp source, ReadOnlySpan<byte> bytes,
        WorkflowAssetSource? assets = null)
    {
        if (bytes.Length is < 2 or > 524288) throw new EditRejectedException("workflow_source_bundle_budget");
        using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        WorkflowCaptureBridge.Shape(root, "schemaVersion", "kind", "patchFingerprint", "originFingerprint", "candidateOriginFingerprint", "files");
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("kind").GetString() != "ncma.extension.captured-sources.v1" ||
            root.GetProperty("files").ValueKind != JsonValueKind.Array || root.GetProperty("files").GetArrayLength() is < 1 or > 16)
            throw new EditRejectedException("workflow_source_bundle_shape");
        PatchFingerprint = root.GetProperty("patchFingerprint").GetString()!;
        WorkflowEvidenceRules.Hash(PatchFingerprint);
        WorkflowEvidenceRules.Hash(root.GetProperty("originFingerprint").GetString()!);
        WorkflowEvidenceRules.Hash(root.GetProperty("candidateOriginFingerprint").GetString()!);
        // Original ExtensionSourceCapture and CapturedBuild retain full file/path
        // validation. This boundary binds the ENTIRE exact serialized byte array.
        WorkflowId = workflow; Workspace = WorkflowBuildCommand.CommandPath(workspace); PlanHash = planHash; Source = source; Assets = assets;
        BundleSha256 = Convert.ToHexString(SHA256.HashData(bytes));
        // Observe the complete executable helper closure at review time. Begin
        // must compare against these bytes, never adopt a later file replacement.
        using var pins = new WorkflowPhysicalPins();
        helpers = WorkflowSourceCaptureJob.RequiredHelpers().Select(relative => {
            var observed = pins.Observe(Path.Combine(Workspace, relative.Replace('/', Path.DirectorySeparatorChar)), 64 * 1024 * 1024);
            return new WorkflowEvidenceFile(relative, observed.Bytes, observed.Sha256);
        }).ToArray();
        Fingerprint = WorkflowEvidenceRules.Digest(new { version = 3, WorkflowId, Workspace, PlanHash, Source, Assets, PatchFingerprint, BundleSha256, helpers });
    }
}
