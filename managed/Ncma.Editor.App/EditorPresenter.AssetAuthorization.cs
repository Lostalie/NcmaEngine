using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Gui;

namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private readonly EditorAssetAuthorizationController _assetAuthorizationController = new(workspace);
    private readonly HashSet<Guid> _assetReadSelection = [];
    private AssetReadApprovalPage? _assetReadReview;
    private bool _assetReadReviewed;
    private EditorViewStamp _assetReadStamp;
    private ulong _assetReadRevision = ulong.MaxValue;
    private void ResetAssetReadReview() { _assetReadReview = null; _assetReadReviewed = false; }
    private void AssetReadSelector(Guid id, string kind, bool enabled)
    {
        Add(GuiItemKind.Checkbox, 20, AssetWidget(id), $"Select exact MCP read UUID ({kind})", new("asset_read_select", id),
            number: _assetReadSelection.Contains(id) ? 1 : 0, max: 1, enabled: enabled);
    }
    private void BuildAssetReadAuthorization(ref ulong labelId, bool writable)
    {
        var service = workspace.Owner.AssetInspections;
        if (service is null) return;
        if (_assetReadStamp != workspace.Stamp || _assetReadRevision != service.PublishedRevision) {
            _assetReadSelection.Clear(); ResetAssetReadReview(); _assetReadStamp = workspace.Stamp; _assetReadRevision = service.PublishedRevision;
        }
        if (_assetReadReview is { } old && !_assetAuthorizationController.IsCurrent(old)) ResetAssetReadReview();
        var grant = service.Grant;
        Add(GuiItemKind.Label, 3, labelId++, $"MCP metadata-only: selected {_assetReadSelection.Count}/64; approved {grant.AssetIds.Length}; expires in {grant.RemainingSeconds}s");
        Add(GuiItemKind.Label, 3, labelId++, "Root selection does not include children. No file access, import, scene edits or Undo permission.");
        Add(GuiItemKind.Button, 21, 1, "Review exact asset-read scope / paired audience", new("asset_read_prepare"),
            enabled: writable && _assetReadSelection.Count > 0 && workspace.Owner.Endpoint is { } endpoint && endpoint.View.Connections.Any(c => c.Paired && c.Connected));
        Add(GuiItemKind.Button, 21, 4, "Revoke all asset-read grants", new("asset_read_revoke"));
        Add(GuiItemKind.Button, 21, 5, "Clear asset-read selection", new("asset_read_clear"));
        if (_assetReadReview is not { } review) return;
        Add(GuiItemKind.Label, 3, labelId++, $"Project {review.ProjectId:D} / project generation {review.ProjectGeneration} / asset revision {review.AssetRevision}");
        Add(GuiItemKind.Label, 3, labelId++, $"Endpoint {review.EndpointId:D}. Shared read scope for these paired clients; not per-client ACL.");
        foreach (var client in review.Audience) Add(GuiItemKind.Label, 3, labelId++, $"Client {client.ConnectionId:D} / {client.ClientName}");
        foreach (var row in review.Assets) Add(GuiItemKind.Label, 3, labelId++, $"Read ONLY {row.AssetId:D} / {row.Kind} / {row.State}");
        Add(GuiItemKind.Checkbox, 21, 2, "I reviewed every UUID and the complete paired audience", new("asset_read_reviewed"), number: _assetReadReviewed ? 1 : 0, max: 1, enabled: writable);
        Add(GuiItemKind.Button, 21, 3, "Approve displayed metadata-only scope for 60 seconds", new("asset_read_approve", Field: review.Fingerprint),
            enabled: writable && _assetReadReviewed);
    }
    private bool ApplyAssetReadAction(ActionView action, double value)
    {
        if (!action.Kind.StartsWith("asset_read_", StringComparison.Ordinal)) return false;
        switch (action.Kind) {
            case "asset_read_select":
                if (value is not (0 or 1)) throw new EditRejectedException("asset_read_selection_invalid");
                if (value == 0) _assetReadSelection.Remove(action.Object);
                else { if (_assetReadSelection.Count >= 64 && !_assetReadSelection.Contains(action.Object)) throw new EditRejectedException("asset_read_selection_budget"); _assetReadSelection.Add(action.Object); }
                ResetAssetReadReview(); break;
            case "asset_read_prepare": _assetReadReview = _assetAuthorizationController.Capture(_assetReadSelection.ToArray()); _assetReadReviewed = false; break;
            case "asset_read_reviewed": _assetReadReviewed = value == 1; break;
            case "asset_read_approve":
                if (_assetReadReview is not { } review || !_assetReadSelection.SetEquals(review.Assets.Select(a => a.AssetId))) throw new EditRejectedException("asset_read_review_stale");
                _assetAuthorizationController.Approve(review, action.Field, _assetReadReviewed); ResetAssetReadReview(); break;
            case "asset_read_revoke": _assetAuthorizationController.Revoke(); ResetAssetReadReview(); break;
            case "asset_read_clear": _assetReadSelection.Clear(); ResetAssetReadReview(); break;
            default: throw new EditRejectedException("asset_read_intent_invalid");
        }
        return true;
    }
}
