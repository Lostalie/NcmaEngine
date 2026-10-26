using System.Text;
using Ncma.AI;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Ui;

namespace Ncma.Editor.Modules;

internal sealed class AiUiDraftStage(EditorDomainProposals domains, EditorUiWorkspace workspace)
{
    private UiDomainObservation? observation;
    private Guid? proposal;
    internal UiDomainObservation Bind(DomainProposalSource source, Guid exactDocument)
        => observation = workspace.SelectDomainObservation(source, exactDocument);
    // Domain review itself owns typed observation/content revalidation. This host guard is
    // cancellation identity only: original Publish legitimately advances the asset clock.
    internal bool IsBound => observation is not null;
    internal DomainProposalReview? Stage(ApprovedDomainRequest approved, byte[] bytes)
    {
        if (observation?.IsCurrent() != true || proposal is not null || !domains.RequestCurrent(approved))
            throw new EditRejectedException("ui_draft_stale");
        var draft = AiUiDraft.Decode(bytes);
        if (draft.DocumentId != observation!.Stamp.Document) throw new EditRejectedException("ui_draft_document_mismatch");
        // Original closed UI schema, UUIDs, hierarchy, resources and registered C# Actions.
        // No new resources are loaded/approved here; missing resources reject the result.
        var candidate = UiCodec.Decode(draft.CopyDefinition());
        var envelope = workspace.PrepareDomainProposal(observation.Stamp, candidate);
        proposal = envelope.GetProperty("input").GetProperty("proposalId").GetGuid();
        try { return domains.ReceiveModuleResult(approved, Encoding.UTF8.GetBytes(envelope.GetRawText())); }
        catch { Cancel(); throw; }
    }
    internal void Cancel()
    {
        observation = null;
        if (proposal is Guid id) {
            try { workspace.CancelDomainProposal(id); }
            catch (Exception e) when (e is EditRejectedException or ObjectDisposedException) { }
        }
        proposal = null;
    }
}
