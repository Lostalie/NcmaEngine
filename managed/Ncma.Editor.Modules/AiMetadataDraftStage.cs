using Ncma.AI;
using Ncma.Assets;
using Ncma.Editor.Core;
using Ncma.Editor.Services;

namespace Ncma.Editor.Modules;

internal sealed class AiMetadataDraftStage(EditorDomainProposals domains, EditorMetadataContent workspace)
{
    private MetadataDomainObservation? observation;
    internal MetadataDomainObservation Bind(DomainProposalSource source, Guid exactAsset, AssetKind kind)
        => observation = workspace.SelectDomainObservation(source, exactAsset, kind);
    internal bool IsBound => observation is not null;
    internal DomainProposalReview Stage(ApprovedDomainRequest approved, byte[] bytes)
    {
        if (observation?.IsCurrent() != true || !domains.RequestCurrent(approved)) throw new EditRejectedException("metadata_draft_stale");
        var draft = AiMetadataDraft.Decode(bytes);
        if (draft.AssetId != observation!.AssetId) throw new EditRejectedException("metadata_draft_asset_mismatch");
        byte[] original = workspace.PrepareSettingsProposal(observation, draft.Version, draft.SampleRate, draft.GenerateTangents);
        return domains.ReceiveModuleResult(approved, original);
    }
    internal void Cancel() { observation = null; workspace.Invalidate(); }
}
