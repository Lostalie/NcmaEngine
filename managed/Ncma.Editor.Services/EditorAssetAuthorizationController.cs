using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Editor.Transport;

namespace Ncma.Editor.Services;

public sealed record AssetReadApprovalPage(EditorViewStamp Stamp, Guid ProjectId, ulong ProjectGeneration,
    Guid EndpointId, ulong AssetRevision, AssetReadRow[] Assets, ConnectionView[] Audience, string Fingerprint);

// Trusted visible UI only. No Agent approval operation and no file/scene mutation grant.
public sealed class EditorAssetAuthorizationController(EditorWorkspace workspace)
{
    private AssetInspectionService Service => workspace.Owner.AssetInspections ?? throw new EditRejectedException("asset_project_missing");
    private static ConnectionView[] Audience(EditorEndpoint endpoint) => endpoint.View.Connections.Where(c => c.Paired && c.Connected)
        .OrderBy(c => c.ConnectionId).Select(c => c with { PendingCount = 0 }).ToArray();
    private static string Fingerprint(AssetReadApprovalPage page) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(page with { Fingerprint = "" })));
    public AssetReadApprovalPage Capture(Guid[] exactIds)
    {
        var service = Service; var state = workspace.Owner.Edit!.State;
        if (state.Frozen || state.EditBusy || state.HistoryInvalidated || workspace.HasDraft) throw new EditRejectedException("asset_read_review_unavailable");
        var endpoint = workspace.Owner.Endpoint ?? throw new EditRejectedException("asset_read_endpoint_missing");
        var audience = Audience(endpoint);
        if (audience.Length == 0) throw new EditRejectedException("asset_read_audience_missing");
        var page = new AssetReadApprovalPage(workspace.Stamp, service.ProjectId, service.ProjectGeneration, endpoint.View.InstanceId,
            service.PublishedRevision, service.ReviewRows(exactIds), audience, "");
        return page with { Fingerprint = Fingerprint(page) };
    }
    public bool IsCurrent(AssetReadApprovalPage page)
    {
        try { return Capture(page.Assets.Select(a => a.AssetId).ToArray()).Fingerprint == page.Fingerprint && Fingerprint(page) == page.Fingerprint; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return false; }
    }
    public void Approve(AssetReadApprovalPage displayed, string displayedFingerprint, bool reviewed)
    {
        if (!reviewed || displayedFingerprint != displayed.Fingerprint || !IsCurrent(displayed)) throw new EditRejectedException("asset_read_review_stale");
        var endpoint = workspace.Owner.Endpoint!; Guid[] ids = displayed.Assets.Select(a => a.AssetId).ToArray();
        Guid[] audience = displayed.Audience.Select(c => c.ConnectionId).ToArray(); Guid instance = displayed.EndpointId;
        Service.ApproveForPairedClients(ids, () => ReferenceEquals(workspace.Owner.Endpoint, endpoint) && endpoint.View.InstanceId == instance &&
            Audience(endpoint).Select(c => c.ConnectionId).SequenceEqual(audience));
    }
    public void Revoke() => Service.Revoke(); // Always available, including Play/draft/stale review.
}
