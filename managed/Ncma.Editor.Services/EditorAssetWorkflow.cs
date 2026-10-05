using System.Numerics;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Assets.Authoring;
using Ncma.Assets.Runtime;
using Ncma.Editor.Core;
using Ncma.Runtime;
using Ncma.Scene.Rendering;

namespace Ncma.Editor.Services;
using Vector3=System.Numerics.Vector3;

public sealed record EditorAssetRow(Guid Id,AssetKind Kind,string Source,ulong Generation,int Subassets,int Dependencies);
public sealed record ModelPlacementPlan(Guid PlanId,EditorViewStamp Stamp,ulong AssetRevision,Guid RootId,string GenerationHash,Guid[] Objects,object[] Operations);
// Trusted local user workflow, deliberately not an Agent approval/capability endpoint.
// Native sees copied rows/progress/UUID intents; the sole EditSession owns all history.
public sealed class EditorAssetWorkflow : IDisposable
{
    private readonly EditorWorkspace _workspace;
    private readonly string _root;
    private readonly Guid _project;
    private readonly ulong _projectGeneration;
    private readonly int _thread=Environment.CurrentManagedThreadId;
    private readonly Guid _session;
    private readonly Dictionary<Guid,(string Metadata,string Source)> _approvals=[];
    private readonly CancellationTokenSource _closed=new();
    private AssetProjectAuthoring Assets=>_workspace.Owner.Assets??throw new InvalidOperationException("Asset project is not configured.");
    private ImportCoordinator Coordinator=>_workspace.Owner.Imports??throw new InvalidOperationException("Validated import tools are not available.");
    private Task<SourceImportPlan>? _sourceTask;
    private Task<ModelPlacementPlan>? _placementTask;
    private EditorViewStamp _sourceStamp;
    private ulong _rowsRevision=ulong.MaxValue;
    private EditorAssetRow[] _rows=[];
    private SourceImportPlan? _jobPlan;
    private Guid? _ticket;
    private bool _disposed;
    public AssetWriteScope Scope {get;}
    public SourceImportPlan? SourcePlan {get;private set;}
    private ModelPlacementPlan? _placementPlan;
    public ModelPlacementPlan? PlacementPlan {get{Verify();return _placementPlan is { } plan?plan with{Objects=(Guid[])plan.Objects.Clone(),Operations=(object[])plan.Operations.Clone()}:null;}private set=>_placementPlan=value;}
    public ImportJobSnapshot? Job {get;private set;}
    public PreparedImportInfo? Prepared {get;private set;}
    public string Code {get;private set;}="idle";
    public int Count {get{RefreshRows();return _rows.Length;}}
    public bool PreparingSource=>_sourceTask is {IsCompleted:false};
    public bool PreparingPlacement=>_placementTask is {IsCompleted:false};
    public bool PreparingCommit=>_ticket is not null&&Prepared is null;
    public bool CanForget {get{Verify();return Job is { } job&&Coordinator.CanForget(job.JobId);}}
    public int ApprovalCount {get{Verify();return _approvals.Count;}}
    public bool ToolsAvailable=>_workspace.Owner.Imports is not null;
    public (int Count,long Bytes) RetainedUsage {get{Verify();return Assets.Imports.Generations.RetainedUsage;}}
    public EditorAssetWorkflow(EditorWorkspace workspace,string root,Guid project,ulong generation)
    {
        _workspace=workspace;_root=Path.GetFullPath(root);_project=project;_projectGeneration=generation;_session=workspace.Stamp.SessionId;
        if(project==Guid.Empty||generation==0)throw new ArgumentException("Invalid asset project identity.");
        Scope=AssetWriteScope.ForHost((path,record)=>_approvals.TryGetValue(record.AssetId,out var approved)&&approved.Metadata==path&&approved.Source==record.SourcePath,Current);
    }
    private bool Current()=>!_disposed&&_workspace.Stamp.SessionId==_session&&_workspace.Owner.Assets is { } a&&a.ProjectId==_project&&a.Generation==_projectGeneration;
    public bool IsSourceApproved(string source)=>Current()&&_workspace.Owner.Play is null&&_approvals.Values.Any(a=>a.Source==source);
    private void Verify(){ObjectDisposedException.ThrowIf(_disposed,this);if(_thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Asset workflow requires owner thread.");}
    private void Writable(EditorViewStamp stamp)
    {
        Verify();if(!Current()||stamp!=_workspace.Stamp||_workspace.Owner.Play is not null||_workspace.Owner.Edit!.State.Frozen||_workspace.Owner.Edit.State.HistoryInvalidated||_workspace.HasDraft)throw new EditRejectedException("asset_view_stale_or_frozen");
    }
    private void RefreshRows()
    {
        Verify();if(_rowsRevision==Assets.Clock.Revision)return;
        var catalog=Assets.Snapshot.Catalog;var rows=new List<EditorAssetRow>();
        for(int offset=0;offset<catalog.Count;offset+=128)foreach(var r in catalog.List(offset,128))rows.Add(new(r.AssetId,r.Kind,r.SourcePath,r.Generation?.Number??0,r.Subassets.Length,r.Dependencies.Length));
        _rows=rows.ToArray();_rowsRevision=Assets.Clock.Revision;
    }
    public EditorAssetRow[] List(int offset=0,int limit=16)
    {RefreshRows();if(offset<0||limit is <1 or >32)throw new ArgumentException("Invalid browser page.");return _rows.Skip(offset).Take(limit).ToArray();}
    public AssetRecord Inspect(Guid id,AssetKind kind)
    {Verify();return Assets.Snapshot.Catalog.TryResolve(new(new(id),kind),out var root,out string code)?root!:throw new EditRejectedException(code);}
    public void PrepareSource(EditorViewStamp stamp,string source,int rate,bool staticOnly)
    {
        Writable(stamp);if(_sourceTask is {IsCompleted:false})throw new EditRejectedException("asset_source_plan_busy");
        if(_sourceTask is {IsCompleted:true})_= _sourceTask.Exception;
        SourcePlan=null;_sourceStamp=stamp;
        _sourceTask=AssetSourcePlanning.PrepareAsync(_root,_project,_projectGeneration,Assets.Clock.Revision,Assets.Snapshot,source,rate,staticOnly,_closed.Token);Code="source_preparing";
    }
    public void BeginImport(Guid planId)
    {
        Writable(_sourceStamp);var plan=SourcePlan??throw new EditRejectedException("asset_source_plan_missing");
        if(Job is not null)throw new EditRejectedException("asset_previous_job_not_retired");
        if(plan.PlanId!=planId||plan.AssetRevision!=Assets.Clock.Revision)throw new EditRejectedException("asset_source_plan_stale");
        if(_approvals.Count>=128&&!_approvals.ContainsKey(plan.Record.AssetId))throw new EditRejectedException("asset_local_approval_budget");
        _approvals[plan.Record.AssetId]=(plan.MetadataPath,plan.Record.SourcePath);
        Guid job=Coordinator.Enqueue(plan.Record.SourcePath,plan.Record.Settings.SampleRate,plan.AssetRevision,plan.ProjectGeneration,plan.Record.SourceHash,plan.Record.Kind==AssetKind.StaticMesh);
        _jobPlan=plan;_ticket=null;Prepared=null;Job=Coordinator.Inspect(job);SourcePlan=null;Code="import_queued";
    }
    public void PrepareCommit(Guid jobId)
    {
        Writable(_workspace.Stamp);if(Job?.JobId!=jobId||_jobPlan is null||_ticket is not null)throw new EditRejectedException("asset_import_job_mismatch");
        _ticket=Assets.Imports.PrepareAsync(Coordinator,jobId,_jobPlan.MetadataPath,_jobPlan.Record);Code="commit_preparing";
    }
    public void CommitImport(EditorViewStamp stamp,Guid ticket,bool identityConfirmed)
    {
        Writable(stamp);if(_ticket!=ticket||Prepared?.Ticket!=ticket||_jobPlan is null)throw new EditRejectedException("asset_import_ticket_mismatch");
        _workspace.AssetCommand(stamp,AssetImportCommands.CapabilityName,new{ticket,expectedAssetRevision=Assets.Clock.Revision,projectGeneration=_projectGeneration,confirmNewIdentities=identityConfirmed});
        _ticket=null;Prepared=null;_jobPlan=null;Code="import_committed";
    }
    public void CancelImport(Guid jobId)
    {Verify();if(Job?.JobId!=jobId)throw new EditRejectedException("asset_import_job_mismatch");if(_ticket is Guid ticket)Assets.Imports.DiscardPrepared(ticket);else Coordinator.Cancel(jobId);_ticket=null;Prepared=null;_jobPlan=null;Code="import_cancelled";}
    public void ForgetImport(Guid jobId)
    {Verify();if(Job?.JobId!=jobId)throw new EditRejectedException("asset_import_job_mismatch");if(!Coordinator.CanForget(jobId))throw new EditRejectedException("asset_import_busy");if(_ticket is Guid ticket){Assets.Imports.DiscardPrepared(ticket);_ticket=null;Prepared=null;}Coordinator.Forget(jobId);Job=null;_jobPlan=null;Code="idle";}
    public void Pump()
    {
        Verify(); // Only completed tasks are consumed. No waiting, hashing, source/model parsing or file IO.
        if(_sourceTask is {IsCompleted:true} source) {
            _sourceTask=null;try{var plan=source.GetAwaiter().GetResult();Writable(_sourceStamp);if(plan.AssetRevision!=Assets.Clock.Revision)throw new EditRejectedException("asset_source_plan_stale");SourcePlan=plan;Code="source_ready_for_review";}catch(Exception e){Code=Diagnostic(e);}
        }
        if(_placementTask is {IsCompleted:true} placement) {
            _placementTask=null;try{var plan=placement.GetAwaiter().GetResult();Writable(plan.Stamp);if(plan.AssetRevision!=Assets.Clock.Revision)throw new EditRejectedException("asset_placement_stale");PlacementPlan=plan;Code="placement_ready";}catch(Exception e){Code=Diagnostic(e);}
        }
        if(Job is { } job) {
            Job=Coordinator.Inspect(job.JobId);
            if(_ticket is Guid ticket&&Prepared is null&&Assets.Imports.IsPrepared(ticket))try{Prepared=Assets.Imports.InspectPrepared(ticket);Code="commit_ready_for_review";}catch(Exception e){Assets.Imports.DiscardPrepared(ticket);_ticket=null;Code=Diagnostic(e);}
        }
    }
    private static string Diagnostic(Exception e)=>e is EditRejectedException rejected?rejected.Message:e is OperationCanceledException?"cancelled":e.Message.Length>512?e.Message[..512]:e.Message;
    public void PreparePlacement(EditorViewStamp stamp,Guid rootId,AssetKind kind,Vector3 position)
    {
        Writable(stamp);if(_placementTask is {IsCompleted:false})throw new EditRejectedException("asset_placement_busy");
        if(_placementTask is {IsCompleted:true})_= _placementTask.Exception;
        if(kind is not (AssetKind.Character or AssetKind.StaticMesh)||!float.IsFinite(position.X)||!float.IsFinite(position.Y)||!float.IsFinite(position.Z)||Vector3.Abs(position).Length()>1e6)throw new ArgumentException("Invalid model placement.");
        var record=Inspect(rootId,kind);if(record.AssetId!=rootId||record.Generation is null)throw new EditRejectedException("asset_model_root_required");
        Assets.Imports.Generations.RequirePinned(record);ulong revision=Assets.Clock.Revision;var cancellation=_closed.Token;
        PlacementPlan=null;Code="placement_preparing";
        _placementTask=Task.Run(()=> {
            cancellation.ThrowIfCancellationRequested();using var prepared=RuntimeAssetLoader.Prepare(_root,_project,[new(new(rootId),kind)],true,cancellation:cancellation);using var lease=prepared.AcquireLease();
            var root=lease.Require(rootId,kind) as RuntimeDataAsset??throw new ArgumentException("Expected model manifest.");
            var manifest=ModelAssetManifestCodec.Decode(root.CopyData());if(manifest.Meshes.Length>16)throw new ArgumentException("Placement budget is 16 flat objects.");
            var registry=RenderComponentRegistry.CreateRegistry();var ids=new List<Guid>();var operations=new List<object>();
            foreach(var part in manifest.Meshes) {
                Guid id=Guid.NewGuid();ids.Add(id);operations.Add(new{op="create",objectId=id,name=Path.GetFileNameWithoutExtension(record.SourcePath)});
                Component(id,TransformData.Identity with{Position=new(position.X,position.Y,position.Z)},"ncma.transform");
                if(manifest.StaticOnly)Component(id,new StaticMeshData(part.Mesh,part.Materials,true,true,uint.MaxValue),StaticMeshData.TypeId);
                else {Component(id,new SkinnedMeshData(rootId,part.Mesh,manifest.Skeleton!.Value,part.Materials,true,true,uint.MaxValue),SkinnedMeshData.TypeId);
                    if(manifest.Clips.Length>0)Component(id,new Ncma.Animation.ClipPlaybackData(manifest.Clips[0],true,true,1,0),Ncma.Animation.ClipPlaybackData.TypeId);}
            }
            cancellation.ThrowIfCancellationRequested();return new ModelPlacementPlan(Guid.NewGuid(),stamp,revision,rootId,record.Generation.ContentHash,ids.ToArray(),operations.ToArray());
            void Component<T>(Guid id,T data,string type)where T:struct,IComponent=>operations.Add(new{op="set_component",objectId=id,typeId=type,version=1,data=registry.Encode(data)});
        },cancellation);
    }
    public void CommitPlacement(Guid planId)
    {
        var plan=_placementPlan??throw new EditRejectedException("asset_placement_missing");Writable(plan.Stamp);
        if(plan.PlanId!=planId||plan.AssetRevision!=Assets.Clock.Revision)throw new EditRejectedException("asset_placement_stale");
        var record=Assets.Snapshot.Catalog.List(0,128).FirstOrDefault(r=>r.AssetId==plan.RootId);
        // Resolve roots beyond the first browser page too; no filesystem access.
        if(record is null)for(int offset=128;offset<Assets.Snapshot.Catalog.Count;offset+=128){record=Assets.Snapshot.Catalog.List(offset,128).FirstOrDefault(r=>r.AssetId==plan.RootId);if(record is not null)break;}
        if(record?.Generation?.ContentHash!=plan.GenerationHash)throw new EditRejectedException("asset_placement_generation_stale");
        _workspace.Transaction(plan.Stamp,"Place imported model",plan.Operations,plan.Objects[0]);PlacementPlan=null;Code="placement_committed";
    }
    public void Assign(EditorViewStamp stamp,Guid objectId,Guid assetId,AssetKind kind)
    {
        Writable(stamp);var root=Inspect(assetId,kind);var obj=_workspace.Owner.Document.World.FindObject(objectId);
        if(!root.Subassets.Any(s=>s.AssetId==assetId&&s.Kind==kind&&!s.Tombstone)&&kind!=AssetKind.MaterialSet)throw new EditRejectedException("typed_subasset_required");
        var registry=_workspace.Owner.Document.World.Components;object operation;
        if(kind==AssetKind.Clip) {
            if(!obj.Has<SkinnedMeshData>()||obj.Get<SkinnedMeshData>().CharacterId!=root.AssetId)throw new EditRejectedException("clip_rig_mismatch");
            var value=obj.Has<Ncma.Animation.ClipPlaybackData>()?obj.Get<Ncma.Animation.ClipPlaybackData>():new(assetId,true,true,1,0);
            operation=Set(Ncma.Animation.ClipPlaybackData.TypeId,registry.Encode(value with{ClipId=assetId}));
        } else if(kind==AssetKind.StaticMesh&&obj.Has<StaticMeshData>()) {
            var part=Assets.Imports.Generations.CopyManifest(root).Meshes.Single(m=>m.Mesh==assetId);
            operation=Set(StaticMeshData.TypeId,registry.Encode(obj.Get<StaticMeshData>() with{MeshId=assetId,MaterialSetId=part.Materials}));
        }
        else if(kind==AssetKind.SkinnedMesh&&obj.Has<SkinnedMeshData>()) {
            if(obj.Get<SkinnedMeshData>().CharacterId!=root.AssetId)throw new EditRejectedException("asset_character_mismatch");
            var part=Assets.Imports.Generations.CopyManifest(root).Meshes.Single(m=>m.Mesh==assetId);
            var skeleton=root.Subassets.Single(s=>s.Kind==AssetKind.Skeleton&&!s.Tombstone);
            operation=Set(SkinnedMeshData.TypeId,registry.Encode(obj.Get<SkinnedMeshData>() with{CharacterId=root.AssetId,MeshId=assetId,SkeletonId=skeleton.AssetId,MaterialSetId=part.Materials}));
        } else if(kind==AssetKind.MaterialSet&&obj.Has<StaticMeshData>()) {RequireCoverage(obj.Get<StaticMeshData>().MeshId,AssetKind.StaticMesh);operation=Set(StaticMeshData.TypeId,registry.Encode(obj.Get<StaticMeshData>() with{MaterialSetId=assetId}));}
        else if(kind==AssetKind.MaterialSet&&obj.Has<SkinnedMeshData>()) {RequireCoverage(obj.Get<SkinnedMeshData>().MeshId,AssetKind.SkinnedMesh);operation=Set(SkinnedMeshData.TypeId,registry.Encode(obj.Get<SkinnedMeshData>() with{MaterialSetId=assetId}));}
        else throw new EditRejectedException("asset_component_kind_mismatch");
        _workspace.Transaction(stamp,"Assign typed asset",[operation],objectId);
        object Set(string type,JsonElement data)=>new{op="set_component",objectId,typeId=type,version=1,data};
        void RequireCoverage(Guid mesh,AssetKind meshKind){var meshRoot=Inspect(mesh,meshKind);if(Assets.Imports.Generations.MaterialSlots(root,assetId)<Assets.Imports.Generations.MaterialSlots(meshRoot,mesh))throw new EditRejectedException("asset_material_slot_coverage");}
    }
    // Exact built-in render references only; not a claim to inspect arbitrary registered component data.
    public Guid[] AffectedRenderObjects(PreparedImportInfo prepared)
    {
        Verify();if(Prepared?.Ticket!=prepared.Ticket)throw new EditRejectedException("asset_import_ticket_mismatch");
        var ids=prepared.Record.Subassets.Select(s=>s.AssetId).Append(prepared.Record.AssetId).ToHashSet();
        return _workspace.Owner.Document.World.GetObjects().Where(obj=>
            obj.Has<StaticMeshData>()&&(ids.Contains(obj.Get<StaticMeshData>().MeshId)||ids.Contains(obj.Get<StaticMeshData>().MaterialSetId))||
            obj.Has<SkinnedMeshData>()&&(ids.Contains(obj.Get<SkinnedMeshData>().CharacterId)||ids.Contains(obj.Get<SkinnedMeshData>().MeshId)||ids.Contains(obj.Get<SkinnedMeshData>().MaterialSetId))||
            obj.Has<Ncma.Animation.ClipPlaybackData>()&&ids.Contains(obj.Get<Ncma.Animation.ClipPlaybackData>().ClipId)).Select(obj=>obj.PersistentId).ToArray();
    }
    public void Dispose()
    {
        if(_disposed)return;Verify();_closed.Cancel();_approvals.Clear();
        if(Job is { } job&&_workspace.Owner.Imports is not null){if(_ticket is Guid ticket)Assets.Imports.DiscardPrepared(ticket);else Coordinator.Cancel(job.JobId);}
        SourcePlan=null;PlacementPlan=null;Prepared=null;_disposed=true; // Background reads own leases and cancellation; never block UI shutdown.
        _=Task.WhenAll(_sourceTask??Task.CompletedTask,_placementTask??Task.CompletedTask).ContinueWith(t=>{_=t.Exception;_closed.Dispose();},TaskScheduler.Default);
    }
}
