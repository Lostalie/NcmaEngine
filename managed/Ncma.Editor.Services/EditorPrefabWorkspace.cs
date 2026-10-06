using System.Security.Cryptography;
using Ncma.Editor.Core;
using Ncma.Scene;
using Ncma.Scene.Prefabs;

namespace Ncma.Editor.Services;

// Inspection evidence only. Not a command ticket, file grant or publishable instance.
// CandidateBytes excludes the future membership/OverrideSet data; publication must revalidate.
public sealed record PrefabPlacementInspection(EditorViewStamp Stamp, Guid WorldId, string TemplateHash,
    int CandidateBytes, int ReservedOperations, PrefabExpansionPreview Preview);

// Trusted local edit-host service, not an MCP tool. No file IO, live writes or second history.
public sealed class EditorPrefabWorkspace
{
    private readonly EditorWorkspace _workspace;
    private readonly PrefabPolicy _policy;
    private bool _inspecting;

    public EditorPrefabWorkspace(EditorWorkspace workspace, PrefabPolicy policy)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        if (!ReferenceEquals(policy.Components, workspace.Owner.Document.World.Components))
            throw new ArgumentException("Prefab policy must use the edit document's registry.");
    }

    private bool Current(EditorViewStamp stamp)
    {
        var state = _workspace.Owner.Edit?.State ?? throw new InvalidOperationException("Editor required.");
        return stamp == _workspace.Stamp && !state.Frozen && !state.EditBusy &&
            !state.HistoryInvalidated && !_workspace.HasDraft && _workspace.Owner.Play is null;
    }

    private void Require(EditorViewStamp stamp)
    {
        if (!Current(stamp)) throw new EditRejectedException("prefab_requires_current_idle_edit");
    }

    public PrefabDocument Extract(EditorViewStamp stamp, Guid[] approvedObjects, Guid prefabId,
        string name, PrefabPoint pivot)
    {
        Require(stamp);
        if (_inspecting) throw new EditRejectedException("prefab_inspection_reentry");
        ArgumentNullException.ThrowIfNull(approvedObjects);
        var ids = (Guid[])approvedObjects.Clone();
        var document = _workspace.Owner.Document;
        var scope = new PrefabExtractionScope(document, ids, () => Current(stamp));
        _inspecting = true;
        try
        {
            // Exact UUIDs are supplied by the trusted local review, not an Agent's claimed approval.
            var result = PrefabTemplates.Extract(document, ids, scope, _policy, prefabId, name, pivot,
                stamp.Revision, document.World.Identity);
            Require(stamp);
            return result;
        }
        finally { _inspecting = false; }
    }

    public PrefabPlacementInspection InspectPlacement(EditorViewStamp stamp, ReadOnlySpan<byte> templateBytes,
        ulong expectedBaseVersion, PrefabPoint placement, Func<PrefabDependency, bool> dependencyAvailable)
    {
        Require(stamp);
        if (_inspecting) throw new EditRejectedException("prefab_inspection_reentry");
        ArgumentNullException.ThrowIfNull(dependencyAvailable);
        _inspecting = true;
        try
        {
            var document = _workspace.Owner.Document;
            using var read = document.World.ReadOnly();
            var prefab = PrefabDocumentCodec.Decode(templateBytes, _policy);
            if (prefab.BaseVersion != expectedBaseVersion) throw new EditRejectedException("prefab_template_stale");
            string hash = Convert.ToHexString(SHA256.HashData(PrefabDocumentCodec.Encode(prefab, _policy)));
            var source = document.CaptureSnapshot();
            var occupied = source.Objects.Select(o => o.Id)
                .Concat(source.Objects.SelectMany(o => o.Behaviours.Select(b => b.Id))).ToHashSet();
            if (occupied.Contains(prefab.AssetId) || prefab.Dependencies.Any(d => occupied.Contains(d.AssetId)))
                throw new EditRejectedException("prefab_asset_identity_collision");
            // The trusted adapter must use a copied, cached catalog, not load files or wait for IPC.
            foreach (var dependency in prefab.Dependencies)
                if (!dependencyAvailable(dependency)) throw new EditRejectedException("prefab_dependency_unavailable");
            Require(stamp);
            var preview = PrefabTemplates.PreviewExpansion(prefab, _policy, placement, occupied);
            int reserved = checked(preview.OperationCount + preview.Objects.Length); // Reserve membership writes.
            if (reserved > EditSession.MaxOperations) throw new EditRejectedException("prefab_operation_budget");
            // Preserve the edit host's composition policy, including cross-object scene constraints.
            var isolated = document.CreateIsolatedCopy();
            isolated.RestoreSnapshot(source with { Objects = source.Objects.Concat(preview.Objects).ToArray() });
            int bytes = isolated.CaptureBytes().Length;
            Require(stamp);
            return new(stamp, document.World.Identity, hash, bytes, reserved, preview);
        }
        finally { _inspecting = false; }
    }
}
