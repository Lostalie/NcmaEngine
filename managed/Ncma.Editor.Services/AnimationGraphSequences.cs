using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Animation;
using Ncma.Assets;
using Ncma.Assets.Runtime;
using Ncma.Assets.Authoring;
using Ncma.Characters;
using Ncma.Editor.Core;
using Ncma.Editor.Transport;
using Ncma.Scene.Rendering;
namespace Ncma.Editor.Services;

public sealed record AnimationSequenceReview(Guid CaseId,AnimationGraphEditorStamp Stamp,Guid GraphId,string GraphHash,string EventHash,
    Guid Publication,AnimatorResourceIdentity[] Resources,string CaseHash,bool RootSupported,int RootBone,Guid Endpoint,ulong AudienceEpoch,
    Guid[] Audience,ulong ReadPermission,string Fingerprint);

// Independent bounded analysis, never live Play or new mutation authority. All Agent execution is exact-case approved.
public sealed class AnimationGraphSequences:IDisposable
{
    private sealed record Candidate(AnimationSequenceCase Input,string Hash,bool Agent);
    private sealed record Approval(AnimationSequenceReview Review,long Start);
    private readonly EditorWorkspace _workspace;private readonly string _root;private readonly Guid _project;
    private readonly AnimationGraphInspections _reads;private readonly Func<AnimationGraphEditorStamp> _stamp;
    private readonly Func<AnimationGraphDefinition?> _definition;private readonly TimeProvider _time;
    private RuntimeAssetSnapshot? _snapshot;private RuntimeAssetLease? _lease;private AnimationProgram? _program;
    private GraphPoseSnapshotSource? _pose;private AnimationSequenceRootSource? _roots;private AnimatorResourceIdentity[] _resources=[];
    private AnimationGraphEditorStamp _prepared;private bool _disposed;
    private readonly Dictionary<Guid,Candidate> _cases=[];private readonly Dictionary<Guid,Approval> _approvals=[];
    private readonly Dictionary<Guid,AnimationSequenceResult> _results=[];private readonly HashSet<Guid> _retired=[];
    private EditSession Edit=>_workspace.Owner.Edit!;
    public AnimationGraphSequences(EditorWorkspace workspace,string root,Guid project,AnimationGraphInspections reads,
        Func<AnimationGraphEditorStamp> stamp,Func<AnimationGraphDefinition?> definition,TimeProvider? time=null)
    {
        _workspace=workspace;_root=new AssetProjectPaths(root).Root;_project=project;_reads=reads;_stamp=stamp;_definition=definition;_time=time??TimeProvider.System;
        Edit.RegisterInspections(AnimationSequenceSchemas.Descriptors().Select(d=>(d,(Func<JsonElement,object>)(input=>Inspect(d.Name,input)))).ToArray());
    }
    private void Verify(){_=Edit.State;ObjectDisposedException.ThrowIf(_disposed,this);}
    private void Current(){Verify();if(_program is null||_prepared!=_stamp()||Edit.State.Frozen||Edit.State.EditBusy||Edit.State.HistoryInvalidated)throw new EditRejectedException("sequence_source_stale");}
    public bool Prepared {get{try{Current();return true;}catch(InvalidOperationException){return false;}}}
    // Human-owned off-frame preparation only; no path/resource metadata is accepted by a capability.
    public void PrepareTrusted(bool includeRoot=true,int rootBone=0)
    {
        Verify();if(Edit.State.Frozen||Edit.State.EditBusy||Edit.State.HistoryInvalidated)throw new EditRejectedException("sequence_prepare_busy");
        var graph=_definition()??throw new EditRejectedException("sequence_graph_missing");
        var dependencies=AnimationGraphValidation.Dependencies(graph);using var input=RuntimeAssetLoader.Prepare(_root,_project,dependencies.Select(d=>new AssetRef(new(d.Id),d.IsSkeleton?AssetKind.Skeleton:AssetKind.Clip)));
        using var inputLease=input.AcquireLease();var candidate=inputLease.CreateGraphPreview(graph);RuntimeAssetLease? lease=null;
        try {
            lease=candidate.AcquireLease();var asset=(RuntimeAnimationGraphAsset)lease.Require(graph.AssetId,AssetKind.AnimationGraph);var program=asset.PrepareProgram(lease);
            var pose=program.InterruptTransitions?GraphPoseSnapshotPreparation.Prepare(program,lease):null;
            var roots=includeRoot?new AnimationSequenceRootSource(program,lease,rootBone):null;
            var resources=lease.List().Select(r=>new AnimatorResourceIdentity(r.Id,r.Kind.ToString(),r.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture),r.ContentHash)).OrderBy(r=>r.Id).ToArray();
            if(resources.Length>4096)throw new ArgumentException("Sequence resource proof budget.");
            CloseSource();_snapshot=candidate;candidate=null!;_lease=lease;lease=null;_program=program;_pose=pose;_roots=roots;_resources=resources;_prepared=_stamp();
        }finally{lease?.Dispose();candidate?.Dispose();}
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    private static string Fingerprint(AnimationSequenceReview review)=>Hash(JsonSerializer.SerializeToUtf8Bytes(review with{Fingerprint=""}));
    private static Guid[] Audience(EditorEndpoint e)=>e.View.Connections.Where(c=>c.Paired&&c.Connected).Select(c=>c.ConnectionId).Order().ToArray();
    private static AnimationSequenceReview Copy(AnimationSequenceReview r)=>r with{Resources=r.Resources.ToArray(),Audience=r.Audience.ToArray()};
    public Guid[] Pending {get{Verify();return _cases.Keys.Order().ToArray();}}
    public AnimationSequenceCase CaseCopy(Guid id){Current();var c=Required(id);return c.Input with{Writes=c.Input.Writes.ToArray(),Assertions=c.Input.Assertions.ToArray()};}
    public Guid ProposeLocal(AnimationSequenceCase input){Current();Guid id=Guid.NewGuid();Store(id,AnimationSequenceCodec.Decode(AnimationSequenceCodec.Encode(input)),false);return id;}
    private void Store(Guid id,AnimationSequenceCase input,bool agent)
    {
        Current();if(id==Guid.Empty||_retired.Contains(id)||_retired.Count>=256)throw new EditRejectedException("sequence_case_retired");
        var owned=AnimationGraphSequence.OwnInput(_program!,input,_roots is not null);string hash=Hash(JsonSerializer.SerializeToUtf8Bytes(owned));
        if(_cases.TryGetValue(id,out var old)){if(old.Hash!=hash||old.Agent!=agent)throw new EditRejectedException("sequence_case_id_reused");return;}
        if(_cases.Count>=4||_cases.Count+_retired.Count>=256)throw new EditRejectedException("sequence_case_budget");_cases.Add(id,new(owned,hash,agent));
    }
    private Candidate Required(Guid id)=>_cases.TryGetValue(id,out var c)?c:throw new EditRejectedException("sequence_case_missing");
    public AnimationSequenceReview CaptureReview(Guid id)
    {
        Current();var c=Required(id);var endpoint=_workspace.Owner.Endpoint??throw new EditRejectedException("sequence_endpoint_missing");
        if(!_reads.RetainsGrant()||_reads.LocalSummary.Hash!=_program!.ContentHash)throw new EditRejectedException("sequence_read_scope_missing");
        var audience=Audience(endpoint);if(audience.Length==0)throw new EditRejectedException("sequence_audience_missing");
        var page=new AnimationSequenceReview(id,_prepared,_program.AssetId,_program.ContentHash,_program.EventContentHash,_lease!.Identity,_resources.ToArray(),c.Hash,_roots is not null,_roots?.RootBoneIndex??-1,endpoint.View.InstanceId,endpoint.AudienceRevision,audience,_reads.PermissionRevision,"");
        return page with{Fingerprint=Fingerprint(page)};
    }
    public bool IsCurrent(AnimationSequenceReview review){try{return Fingerprint(review)==review.Fingerprint&&CaptureReview(review.CaseId).Fingerprint==review.Fingerprint;}catch(Exception e)when(e is InvalidOperationException or ArgumentException){return false;}}
    public void Approve(AnimationSequenceReview review,string fingerprint,bool reviewed)
    {Current();if(!reviewed||fingerprint!=review.Fingerprint||!IsCurrent(review))throw new EditRejectedException("sequence_review_stale");_approvals[review.CaseId]=new(Copy(review),_time.GetTimestamp());}
    private bool Approved(Guid id)=>_approvals.TryGetValue(id,out var a)&&_time.GetElapsedTime(a.Start).TotalSeconds<60&&IsCurrent(a.Review);
    public void Cancel(Guid id){Verify();if(!_cases.ContainsKey(id))throw new EditRejectedException("sequence_case_missing");_cases.Remove(id);_results.Remove(id);_approvals.Remove(id);_retired.Add(id);}
    public void Revoke(){Verify();_approvals.Clear();_results.Clear();}
    public AnimationSequenceResult RunLocal(Guid id)
    {Current();var c=Required(id);if(c.Agent&&!Approved(id))throw new EditRejectedException("sequence_not_approved");return Run(id,c);}
    private AnimationSequenceResult Run(Guid id,Candidate c)
    {if(!_results.TryGetValue(id,out var r)){r=AnimationGraphSequence.Run(_program!,c.Input,_pose,_roots);_results.Add(id,r);}return r;}
    private object Inspect(string name,JsonElement input)
    {
        Current();if(!_reads.RetainsGrant()||_reads.LocalSummary.Hash!=_program!.ContentHash)throw new EditRejectedException("sequence_not_visible");
        if(name==AnimationSequenceSchemas.Propose) {
            AnimationGraphEdits.Closed(input,"graphId","caseId","test");if(AnimationGraphEdits.Uuid(input,"graphId")!=_program.AssetId)throw new EditRejectedException("sequence_not_visible");Guid id=AnimationGraphEdits.Uuid(input,"caseId");
            Store(id,AnimationSequenceCodec.Decode(input.GetProperty("test")),true);var c=Required(id);
            return new{caseId=id,graphId=_program.AssetId,caseHash=c.Hash,graphHash=_program.ContentHash,eventHash=_program.EventContentHash,steps=c.Input.Steps,executionApproved=false};
        }
        AnimationGraphEdits.Closed(input,"graphId","caseId","section","offset","limit");Guid graph=AnimationGraphEdits.Uuid(input,"graphId"),caseId=AnimationGraphEdits.Uuid(input,"caseId");
        if(graph!=_program.AssetId||!Approved(caseId))throw new EditRejectedException("sequence_not_approved");
        var section=input.GetProperty("section");if(section.ValueKind!=JsonValueKind.String)throw new ArgumentException("Sequence section.");
        if(!input.GetProperty("offset").TryGetInt32(out int offset)||offset is <0 or >8192||!input.GetProperty("limit").TryGetInt32(out int limit)||limit is <1 or >8)throw new ArgumentException("Sequence page budget.");
        var result=Run(caseId,Required(caseId));object[] rows=section.GetString() switch {
            "summary"=>[new{passed=result.Passed,steps=result.Timeline.Count,events=result.Timeline.Sum(t=>t.Events.Count),checks=result.Checks.Count}],
            "timeline"=>result.Timeline.Skip(offset).Take(limit).Select(t=>(object)new{frame=t.Frame,sequence=t.Sequence,eventCount=t.Events.Count,root=t.Root}).ToArray(),
            "events"=>result.Timeline.SelectMany(t=>t.Events).Skip(offset).Take(limit).Cast<object>().ToArray(),
            "weights"=>result.Timeline.SelectMany(t=>t.Spaces).Skip(offset).Take(limit).Cast<object>().ToArray(),
            "cache"=>result.Timeline.Skip(offset).Take(limit).Select(t=>(object)t.Cache).ToArray(),
            "checks"=>result.Checks.Skip(offset).Take(limit).Cast<object>().ToArray(),_=>throw new ArgumentException("Closed sequence section.")};
        int total=section.GetString() switch{"summary"=>1,"timeline" or "cache"=>result.Timeline.Count,"events"=>result.Timeline.Sum(t=>t.Events.Count),"weights"=>result.Timeline.Sum(t=>t.Spaces.Count),_=>result.Checks.Count};
        if(section.GetString()=="summary")rows=rows.Skip(offset).Take(limit).ToArray();
        return new{caseId,graphId=graph,caseHash=Required(caseId).Hash,graphHash=result.GraphContentHash,eventHash=result.EventContentHash,publication=_lease!.Identity,resourcesPrepared=true,rootMotionSupported=result.RootMotionSupported,collisionExecuted=false,livePlay=false,section=section.GetString(),total,offset,nextOffset=offset+limit<total?(int?)(offset+limit):null,items=rows};
    }
    private void CloseSource(){foreach(Guid id in _cases.Keys)_retired.Add(id);_cases.Clear();_approvals.Clear();_results.Clear();_lease?.Dispose();_lease=null;_snapshot?.Dispose();_snapshot=null;_program=null;_pose=null;_roots=null;}
    public void Dispose(){if(_disposed)return;Verify();CloseSource();_disposed=true;}
}
