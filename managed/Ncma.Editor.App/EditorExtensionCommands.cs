#if NCMA_EXTENSIONS
using System.Text;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Extensions.Hosting;
using Ncma.Runtime;

namespace Ncma.Editor.App;

internal sealed class ExtensionTransformReview(OriginalCommandBatchReview batch, ExtensionSceneSummary source, string changes)
{
    internal OriginalCommandBatchReview Batch { get; } = batch;
    internal ExtensionSceneSummary Source { get; } = source;
    internal string Fingerprint => Batch.Fingerprint;
    internal string Commands { get; } = Encoding.UTF8.GetString(batch.CopyCommandBytes());
    internal string Changes { get; } = changes;
}

// Trusted LOCAL human route. Not a capability provider and never exposed to model/transport.
// One closed sample intent: add the registered default Transform ONLY when absent.
internal sealed class EditorExtensionCommands(ExtensionApplicationService group, EditorWorkspace workspace)
{
    private ExtensionTransformReview? pending;
    private bool busy, violation;
    internal void VerifyIdle()
    {
        _ = group.Status; // original owner-thread check, including before Start/after close
        if(busy){violation=true;throw new EditRejectedException("extension_command_reentry");}
    }
    internal void Cancel(){VerifyIdle();pending=null;}
    private ExtensionAccess Access() => group.GetAccess(EditorTrustedExtensions.Descriptor.Id);
    private ExtensionSceneSummary Source()
    {
        var access=Access();access.RequireActive();
        var state=workspace.Owner.Edit!.State;
        if(state.Frozen||state.EditBusy||state.HistoryInvalidated||workspace.Owner.Play is not null||state.Selection is null)
            throw new EditRejectedException("extension_command_requires_idle_selection");
        return new(access.SessionId,workspace.Stamp,workspace.Owner.Document.SessionId,workspace.Owner.Document.ObjectCount,state.Selection);
    }
    private bool Current(ExtensionTransformReview review)
    {
        try { return !violation && Source()==review.Source &&
            workspace.Owner.Edit!.BehaviourCatalogGeneration==review.Batch.BehaviourCatalogGeneration; }
        catch(InvalidOperationException){return false;}
    }
    internal bool IsCurrent(ExtensionTransformReview review)
    {VerifyIdle();return ReferenceEquals(pending,review)&&Current(review);}
    internal ExtensionTransformReview PrepareAddTransform()
    {
        VerifyIdle();pending=null;busy=true;violation=false;
        try {
            var source=Source();var document=workspace.Owner.Document;Guid target=source.Selection!.Value;
            if(document.HasComponent<TransformData>(target))throw new EditRejectedException("extension_transform_already_present");
            var initial=document.Components.DefaultSnapshot(TransformData.Generated.TypeId,TransformData.Generated.SchemaVersion);
            var input=JsonSerializer.SerializeToElement(new{operations=new[]{new{op="set_component",objectId=target,
                typeId=initial.TypeId,version=initial.Version,data=initial.Data}}});
            var edit=workspace.Owner.Edit!;
            var batch=edit.ReviewOriginalBatch("Extension: add missing Transform",source.Source.Revision,
                [new("ncma.scene.transaction",input)]);
            if(!batch.PreservesProposedValues)throw new EditRejectedException("extension_normalization_changed");
            var changes=Encoding.UTF8.GetString(edit.DescribeOriginalBatchChanges(batch));
            var review=new ExtensionTransformReview(batch,source,changes);
            if(!Current(review))throw new EditRejectedException("extension_review_stale");
            pending=review;return review;
        } finally {busy=false;}
    }
    internal CapabilityResult Confirm(ExtensionTransformReview review,string confirmedFingerprint)
    {
        VerifyIdle();
        if(!ReferenceEquals(pending,review)||!Current(review))throw new EditRejectedException("extension_review_stale");
        if(!StringComparer.Ordinal.Equals(review.Fingerprint,confirmedFingerprint))throw new EditRejectedException("extension_confirmation_required");
        pending=null;busy=true;violation=false; // consumed BEFORE callbacks, no implicit retry
        try {
            var permissions=new CapabilityPermissions(["ncma.scene.transaction"],objectScope:[review.Source.Selection!.Value],
                createScope:[],componentScope:[TransformData.Generated.TypeId],bindingScope:[],
                allowDocumentHistory:false,requireTrustedBindings:true,isCurrent:()=>Current(review));
            return workspace.Owner.Edit!.ApplyOriginalBatch(review.Batch,Guid.NewGuid(),permissions);
        } finally {busy=false;}
    }
}
#endif
