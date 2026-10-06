using System.Numerics;
using Ncma.Assets;
using Ncma.Editor.Services;
using Ncma.Gui;

namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private readonly EditorAssetWorkflow? _assetWorkflow=assetWorkflow;
    private int _assetOffset,_subassetOffset,_assetReportOffset;
    private readonly Dictionary<Guid,ulong> _assetWidgetIds=[];
    private ulong _nextAssetWidget,_assetRevision=ulong.MaxValue;
    private AssetRecord? _selectedAsset;
    private string _assetSource="assets/Character.fbx";
    private int _assetRate=30;
    private bool _assetStatic,_sourceReviewed,_importReviewed,_identityConfirmed;
    private Guid _sourceReview,_importReview;
    private string[] _importReport=[];
    private Guid _importReportTicket;
    private EditorViewStamp _importReportStamp;
    private System.Numerics.Vector3 _placement;
    private ulong AssetWidget(Guid id)
    {if(!_assetWidgetIds.TryGetValue(id,out ulong value)){if(_assetWidgetIds.Count>=65536)throw new ArgumentException("Asset widget identity budget.");_assetWidgetIds.Add(id,value=checked(++_nextAssetWidget));}return value;}
    private void BuildAssets(ref ulong labelId,bool writable)
    {
        if(_assetWorkflow is null)return;
        ulong revision=workspace.Owner.Assets!.Clock.Revision;
        if(_assetRevision!=revision) {
            if(_selectedAsset is { } prior)try{_selectedAsset=_assetWorkflow.Inspect(prior.AssetId,prior.Kind);}catch(InvalidOperationException){_selectedAsset=null;}
            _assetRevision=revision;_generation=checked(_generation+1);CancelInteraction();
        }
        Add(GuiItemKind.Label,3,labelId++,$"Assets / revision {revision} / exact local grants {_assetWorkflow.ApprovalCount}");
        var retained=_assetWorkflow.RetainedUsage;
        BuildAssetReadAuthorization(ref labelId,writable);
        Add(GuiItemKind.Label,3,labelId++,$"Generation pins {retained.Count}/128 / retained {retained.Bytes}/1073741824 bytes; history Undo {workspace.Owner.Edit!.State.UndoCount}/64 / Redo {workspace.Owner.Edit.State.RedoCount}");
        _assetOffset=Math.Clamp(_assetOffset,0,Math.Max(0,(_assetWorkflow.Count-1)/16*16));
        Add(GuiItemKind.Button,17,1,"Previous assets",new("asset_previous"),enabled:_assetOffset>0);Line();
        Add(GuiItemKind.Button,17,2,"Next assets",new("asset_next"),enabled:_assetOffset+16<_assetWorkflow.Count);
        foreach(var row in _assetWorkflow.List(_assetOffset)) {
            Add(row.Kind is AssetKind.Character or AssetKind.StaticMesh&&row.Generation>0?GuiItemKind.AssetButton:GuiItemKind.Button,16,AssetWidget(row.Id),
                (_selectedAsset?.AssetId==row.Id?"> ":"")+row.Kind+" / "+System.IO.Path.GetFileName(row.Source),new("asset_select",row.Id,Index:(int)row.Kind),
                value:row.Id.ToString("D"));
            Add(GuiItemKind.Label,3,labelId++,$"{row.Id:D} / g{row.Generation} / children {row.Subassets} / dependencies {row.Dependencies}");
            AssetReadSelector(row.Id,row.Kind.ToString(),writable);
        }
        Add(GuiItemKind.Text,17,3,"Project FBX (assets/ relative)",new("asset_source"),value:_assetSource,enabled:writable);
        Add(GuiItemKind.Number,17,4,"Samples/sec",new("asset_rate"),number:_assetRate,min:1,max:120,enabled:writable);
        Add(GuiItemKind.Checkbox,17,5,"Static-only import",new("asset_static"),number:_assetStatic?1:0,max:1,enabled:writable);
        Add(GuiItemKind.Button,17,6,"Prepare read-only source plan",new("asset_source_plan"),enabled:writable&&!_assetWorkflow.PreparingSource);
        if(_assetWorkflow.SourcePlan is { } plan) {
            if(_sourceReview!=plan.PlanId){_sourceReview=plan.PlanId;_sourceReviewed=false;}
            foreach(string line in new[]{plan.Record.SourcePath,plan.MetadataPath,plan.Record.AssetId.ToString("D"),plan.Record.SourceHash,$"{plan.Record.Kind}; {plan.Record.Settings.SampleRate} Hz; asset revision {plan.AssetRevision}"})Add(GuiItemKind.Label,3,labelId++,line);
            Add(GuiItemKind.Checkbox,17,7,"Approve only the displayed source / UUID / metadata",new("asset_source_review"),number:_sourceReviewed?1:0,max:1,enabled:writable);
            Add(GuiItemKind.Button,17,8,"Start approved import",new("asset_import_begin",plan.PlanId),enabled:writable&&_sourceReviewed&&_assetWorkflow.ToolsAvailable&&_assetWorkflow.Job is null);
        }
        Add(GuiItemKind.Label,3,labelId++,"Asset workflow: "+_assetWorkflow.Code);
        if(!_assetWorkflow.ToolsAvailable)Add(GuiItemKind.Label,3,labelId++,"Import requires the validated Editor package worker/kernel; browser remains read-only.");
        if(_assetWorkflow.Job is { } job) {
            Add(GuiItemKind.Label,3,labelId++,$"{job.JobId:D}: {job.State} / {job.Code}");
            Add(GuiItemKind.Label,3,labelId++,$"Worker phase {job.Progress.Phase}: {job.Progress.BytesRead}/{job.Progress.BytesTotal} source bytes (not a completion percentage)");
            if(job.Candidate is { } candidate)Add(GuiItemKind.Label,3,labelId++,$"Candidate {candidate.ResultHash}; meshes={candidate.Meshes}; bones={candidate.Bones}; clips={candidate.Clips}");
            Add(GuiItemKind.Button,17,9,"Prepare candidate publication",new("asset_import_prepare",job.JobId),enabled:writable&&job.State==Ncma.Assets.Authoring.ImportJobState.Ready&&_assetWorkflow.Prepared is null&&!_assetWorkflow.PreparingCommit);
            Add(GuiItemKind.Button,17,10,"Cancel import / revoke candidate",new("asset_import_cancel",job.JobId));
            Add(GuiItemKind.Button,17,11,"Forget completed job diagnostics",new("asset_import_forget",job.JobId),enabled:_assetWorkflow.CanForget);
        }
        if(_assetWorkflow.Prepared is { } prepared) {
            if(_importReview!=prepared.Ticket){_importReview=prepared.Ticket;_importReviewed=false;_identityConfirmed=false;_assetReportOffset=0;}
            if(_importReportTicket!=prepared.Ticket||_importReportStamp!=workspace.Stamp){_importReport=prepared.Changes.Select(c=>c.ToString()).Concat(prepared.Diagnostics.Select(d=>d.Code+": "+d.Message)).Concat(_assetWorkflow.AffectedRenderObjects(prepared).Select(id=>"Affected built-in render object: "+id.ToString("D"))).Take(8192).ToArray();_importReportTicket=prepared.Ticket;_importReportStamp=workspace.Stamp;}
            Add(GuiItemKind.Label,3,labelId++,"Prepared source SHA256: "+prepared.Record.SourceHash);
            Add(GuiItemKind.Label,3,labelId++,"Derived generation SHA256: "+prepared.Record.Generation!.ContentHash);
            Add(GuiItemKind.Label,3,labelId++,$"Report {_importReport.Length} rows (bounded 8192; built-in render references only)");
            var report=_importReport;
            _assetReportOffset=Math.Clamp(_assetReportOffset,0,Math.Max(0,(report.Length-1)/8*8));
            Add(GuiItemKind.Button,17,12,"Previous import report",new("asset_report_previous"),enabled:_assetReportOffset>0);Line();
            Add(GuiItemKind.Button,17,13,"Next import report",new("asset_report_next"),enabled:_assetReportOffset+8<report.Length);
            foreach(string line in report.Skip(_assetReportOffset).Take(8))foreach(string chunk in InspectionText.Split(line,4096))Add(GuiItemKind.Label,3,labelId++,chunk);
            Add(GuiItemKind.Checkbox,17,14,"I reviewed all identity changes / diagnostics",new("asset_import_review"),number:_importReviewed?1:0,max:1,enabled:writable);
            Add(GuiItemKind.Checkbox,17,15,"Confirm new identities / tombstones",new("asset_identity_confirm"),number:_identityConfirmed?1:0,max:1,enabled:writable);
            Add(GuiItemKind.Button,17,16,"Commit imported generation (Undoable)",new("asset_import_commit",prepared.Ticket),enabled:writable&&_importReviewed&&(!prepared.RequiresIdentityConfirmation||_identityConfirmed));
        }
        if(_selectedAsset is { } selected) {
            Add(GuiItemKind.Label,3,labelId++,"Selected source: "+selected.SourcePath);
            Add(GuiItemKind.Number,17,17,"Placement X",new("asset_place_x"),number:_placement.X,min:-1000,max:1000);
            Add(GuiItemKind.Number,17,18,"Placement Y",new("asset_place_y"),number:_placement.Y,min:-1000,max:1000);
            Add(GuiItemKind.Number,17,19,"Placement Z",new("asset_place_z"),number:_placement.Z,min:-1000,max:1000);
            Add(GuiItemKind.Button,17,20,"Prepare model placement / drag model to viewport",new("asset_place_prepare",selected.AssetId,Index:(int)selected.Kind),enabled:writable&&selected.Generation is not null&&!_assetWorkflow.PreparingPlacement&&selected.Kind is AssetKind.Character or AssetKind.StaticMesh);
            _subassetOffset=Math.Clamp(_subassetOffset,0,Math.Max(0,(selected.Subassets.Length-1)/16*16));
            Add(GuiItemKind.Button,17,21,"Previous subassets",new("asset_sub_previous"),enabled:_subassetOffset>0);Line();
            Add(GuiItemKind.Button,17,22,"Next subassets",new("asset_sub_next"),enabled:_subassetOffset+16<selected.Subassets.Length);
            foreach(var sub in selected.Subassets.Skip(_subassetOffset).Take(16)) {
                Add(GuiItemKind.Label,3,labelId++,$"{sub.Kind} {sub.Name} / {sub.AssetId:D}{(sub.Tombstone?" / tombstone":"")}");
                AssetReadSelector(sub.AssetId,sub.Kind.ToString(),writable);
                if(sub.Kind is AssetKind.StaticMesh or AssetKind.SkinnedMesh or AssetKind.MaterialSet or AssetKind.Clip)
                    Add(GuiItemKind.Button,18,AssetWidget(sub.AssetId),"Assign to selected object",new("asset_assign",sub.AssetId,Index:(int)sub.Kind),enabled:writable&&!sub.Tombstone&&_page!.Selected is not null);
            }
        }
        if(_assetWorkflow.PlacementPlan is { } placement) {
            Add(GuiItemKind.Label,3,labelId++,$"Placement {placement.PlanId:D} / {placement.Objects.Length} independent flat objects / asset revision {placement.AssetRevision}");
            Add(GuiItemKind.Button,17,23,"Create prepared model objects (one Undo)",new("asset_place_commit",placement.PlanId),enabled:writable);
        }
    }
    private bool ApplyAssetAction(ActionView action,double value,string text,EditorViewStamp stamp)
    {
        if(!action.Kind.StartsWith("asset_",StringComparison.Ordinal))return false;
        if(_assetWorkflow is null)throw new InvalidOperationException("Asset browser not configured.");
        if(action.Kind is "asset_read_revoke" or "asset_read_clear")return ApplyAssetReadAction(action,value);
        if(_assetRevision!=workspace.Owner.Assets!.Clock.Revision)throw new InvalidOperationException("stale_asset_view");
        if(ApplyAssetReadAction(action,value))return true;
        switch(action.Kind) {
            case "asset_previous":_assetOffset=Math.Max(0,_assetOffset-16);break;
            case "asset_next":_assetOffset+=16;break;
            case "asset_sub_previous":_subassetOffset=Math.Max(0,_subassetOffset-16);break;
            case "asset_sub_next":_subassetOffset+=16;break;
            case "asset_select":_selectedAsset=_assetWorkflow.Inspect(action.Object,(AssetKind)action.Index);_assetSource=_selectedAsset.SourcePath;_assetRate=_selectedAsset.Settings.SampleRate;_assetStatic=_selectedAsset.Kind==AssetKind.StaticMesh;_subassetOffset=0;break;
            case "asset_source":_assetSource=text;break;
            case "asset_rate":if(value!=Math.Floor(value)||value is <1 or >120)throw new ArgumentException("Invalid sample rate.");_assetRate=(int)value;break;
            case "asset_static":_assetStatic=value!=0;break;
            case "asset_source_plan":_sourceReviewed=false;_assetWorkflow.PrepareSource(stamp,_assetSource,_assetRate,_assetStatic);break;
            case "asset_source_review":_sourceReviewed=value!=0;break;
            case "asset_import_begin":if(!_sourceReviewed)throw new InvalidOperationException("source_review_required");_assetWorkflow.BeginImport(action.Object);break;
            case "asset_import_prepare":_assetWorkflow.PrepareCommit(action.Object);break;
            case "asset_import_cancel":_assetWorkflow.CancelImport(action.Object);break;
            case "asset_import_forget":_assetWorkflow.ForgetImport(action.Object);break;
            case "asset_report_previous":_assetReportOffset=Math.Max(0,_assetReportOffset-8);break;
            case "asset_report_next":_assetReportOffset+=8;break;
            case "asset_import_review":_importReviewed=value!=0;break;
            case "asset_identity_confirm":_identityConfirmed=value!=0;break;
            case "asset_import_commit":if(!_importReviewed)throw new InvalidOperationException("import_review_required");_assetWorkflow.CommitImport(stamp,action.Object,_identityConfirmed);_refreshRenderAssets=true;break;
            case "asset_place_x":_placement.X=(float)value;break;
            case "asset_place_y":_placement.Y=(float)value;break;
            case "asset_place_z":_placement.Z=(float)value;break;
            case "asset_place_prepare":_assetWorkflow.PreparePlacement(stamp,action.Object,(AssetKind)action.Index,_placement);break;
            case "asset_place_commit":_assetWorkflow.CommitPlacement(action.Object);_refreshRenderAssets=true;break;
            case "asset_assign":_assetWorkflow.Assign(stamp,_page!.Selected!.Id,action.Object,(AssetKind)action.Index);_refreshRenderAssets=true;break;
            default:throw new ArgumentException("Unknown local asset intent.");
        }
        return true;
    }
}
