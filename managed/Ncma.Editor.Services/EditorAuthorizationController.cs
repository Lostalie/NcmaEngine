using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Editor.Transport;

namespace Ncma.Editor.Services;

public sealed record AuthorizationProposal(ProposalView Scope, string Input, string Fingerprint);
public sealed record AuthorizationPage(EditorViewStamp Stamp, Guid InstanceId, EndpointView Endpoint,
    AuthorizationProposal[] Proposals, GrantView[] Grants);

// Trusted human UI only. Not an IPC operation, permission factory or Agent capability.
public sealed class EditorAuthorizationController(EditorWorkspace workspace)
{
    public void Configure(EditorViewStamp stamp, bool enabled, string projectRoot)
    {
        if (stamp != workspace.Stamp) throw new EditRejectedException("stale_view");
        workspace.CancelDraft(); workspace.Owner.ConfigureEndpoint(enabled, projectRoot);
    }
    private static string Fingerprint(ProposalView proposal, string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(proposal) + "\n" + input)));
    public AuthorizationPage? Capture()
    {
        var endpoint = workspace.Owner.Endpoint;
        if (endpoint is null) return null;
        var view = endpoint.View;
        return new(workspace.Stamp, view.InstanceId, view, endpoint.Proposals.Select(p => {
            string input = endpoint.ProposalInput(p.Id); return new AuthorizationProposal(p, input, Fingerprint(p, input));
        }).ToArray(), endpoint.Grants);
    }
    private EditorEndpoint Current(AuthorizationPage page)
    {
        if (page.Stamp != workspace.Stamp || workspace.Owner.Endpoint is not { } endpoint || endpoint.View.InstanceId != page.InstanceId)
            throw new EditRejectedException("stale_authorization_view");
        return endpoint;
    }
    public void Pair(AuthorizationPage page, Guid connection, bool approved) => Current(page).Pair(connection, approved);
    public void Revoke(AuthorizationPage page, Guid connection) => Current(page).Revoke(connection);
    public void Approve(AuthorizationPage page, Guid proposalId, string displayedFingerprint, bool allowHistory, Guid? confirmedDelete)
    {
        var endpoint = Current(page);
        var displayed = page.Proposals.Single(p => p.Scope.Id == proposalId);
        var actual = endpoint.Proposals.Single(p => p.Id == proposalId);
        string input = endpoint.ProposalInput(proposalId);
        if (displayed.Fingerprint != displayedFingerprint || Fingerprint(actual,input) != displayedFingerprint || displayed.Input != input)
            throw new EditRejectedException("proposal_fingerprint_mismatch");
        endpoint.Approve(proposalId,allowHistory,confirmedDelete);
    }
}
