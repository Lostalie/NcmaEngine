using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Animation;
using Ncma.Assets;
using Ncma.Assets.Runtime;
using Ncma.Editor.Core;
using Ncma.Editor.Transport;
using Ncma.Scene.Rendering;
namespace Ncma.Editor.Services;

public sealed record AnimationSkeletonReview(AnimationGraphEditorStamp Stamp,string GraphHash,Guid SkeletonId,string SkeletonHash,
    Guid Publication,AnimatorResourceIdentity[] Resources,Guid Endpoint,ulong AudienceEpoch,Guid[] Audience,ulong ReadPermission,string Fingerprint);
public sealed record AnimationSkeletonPage(Guid SkeletonId,string Hash,int Total,int Offset,int? NextOffset,AnimationBoneIdentity[] Bones);

// Copied bone identity metadata, no pose/World memory. Preparation is a trusted off-frame human action, never a tool input.
public sealed class AnimationGraphSkeletons:IDisposable
{
    private readonly EditorWorkspace _workspace;private readonly string _root;private readonly Guid _project;
    private readonly AnimationGraphInspections _reads;private readonly Func<AnimationGraphEditorStamp> _stamp;
    private readonly Func<AnimationGraphDefinition?> _definition;private readonly Func<bool> _draft;private readonly TimeProvider _time;
    private RuntimeAssetSnapshot? _snapshot;private RuntimeAssetLease? _lease;private AnimationSkeletonDescriptor? _layout;
    private AnimationGraphEditorStamp _prepared;private string _graphHash="";private AnimatorResourceIdentity[] _resources=[];
    private AnimationSkeletonReview? _approval;private long _start;private bool _disposed;
    private EditSession Edit=>_workspace.Owner.Edit!;
    public AnimationGraphSkeletons(EditorWorkspace workspace,string root,Guid project,AnimationGraphInspections reads,
        Func<AnimationGraphEditorStamp> stamp,Func<AnimationGraphDefinition?> definition,Func<bool> draft,TimeProvider? time=null)
    {
        _workspace=workspace;_root=new Ncma.Assets.Authoring.AssetProjectPaths(root).Root;_project=project;_reads=reads;_stamp=stamp;_definition=definition;_draft=draft;_time=time??TimeProvider.System;
        Edit.RegisterInspection(AnimationSkeletonSchemas.Descriptor,Inspect);
    }
    private void Verify(){_=Edit.State;ObjectDisposedException.ThrowIf(_disposed,this);}
    private void LocalCurrent(){Verify();var current=_stamp();if(_layout is null||current.Edit!=_prepared.Edit||current.Graph!=_prepared.Graph||current.Assets!=_prepared.Assets||Edit.State.Frozen||Edit.State.HistoryInvalidated||_definition()?.SkeletonId!=_layout.Id)throw new EditRejectedException("skeleton_source_stale");}
    private void ExactCurrent(){LocalCurrent();if(_draft()||_stamp()!=_prepared||Edit.State.EditBusy)throw new EditRejectedException("skeleton_source_stale");}
    public bool Prepared{get{try{LocalCurrent();return true;}catch(InvalidOperationException){return false;}}}
    public AnimationSkeletonDescriptor LocalCopy(){LocalCurrent();return _layout! with{Bones=_layout.Bones.ToArray()};}
    public AnimationSkeletonPage LocalPage(int offset,int limit=4){LocalCurrent();return Page(offset,limit);}
    public void PrepareTrusted()
    {
        Verify();if(_draft()||Edit.State.Frozen||Edit.State.EditBusy||Edit.State.HistoryInvalidated)throw new EditRejectedException("skeleton_prepare_busy");
        var graph=_definition()??throw new EditRejectedException("skeleton_graph_missing");
        var candidate=RuntimeAssetLoader.Prepare(_root,_project,[new(new(graph.SkeletonId),AssetKind.Skeleton)]);RuntimeAssetLease? lease=null;
        try {lease=candidate.AcquireLease();var rig=(RuntimeDataAsset)lease.Require(graph.SkeletonId,AssetKind.Skeleton);var layout=RuntimeAnimationGraphAsset.DescribeSkeleton(rig);
            if(layout.Bones.Length is <1 or >1024||layout.Bones.Any(b=>b.Path.Length>4096)||layout.Bones.Select(b=>b.Path).Distinct(StringComparer.Ordinal).Count()!=layout.Bones.Length)throw new ArgumentException("Unique bounded actual bone paths required.");
            var proof=lease.List().Select(r=>new AnimatorResourceIdentity(r.Id,r.Kind.ToString(),r.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture),r.ContentHash)).OrderBy(r=>r.Id).ToArray();if(proof.Length>4096)throw new ArgumentException("Skeleton closure proof budget.");
            Close();_snapshot=candidate;candidate=null!;_lease=lease;lease=null;_layout=layout;_resources=proof;_prepared=_stamp();_graphHash=_reads.LocalSummary.Hash;
        }finally{lease?.Dispose();candidate?.Dispose();}
    }
    private static Guid[] Audience(EditorEndpoint e)=>e.View.Connections.Where(c=>c.Paired&&c.Connected).Select(c=>c.ConnectionId).Order().ToArray();
    private static string Fingerprint(AnimationSkeletonReview r)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(r with{Fingerprint=""})));
    private static AnimationSkeletonReview Copy(AnimationSkeletonReview r)=>r with{Resources=r.Resources.ToArray(),Audience=r.Audience.ToArray()};
    public AnimationSkeletonReview CaptureReview()
    {
        ExactCurrent();if(!_reads.RetainsGrant()||_reads.LocalSummary.Hash!=_graphHash)throw new EditRejectedException("skeleton_read_scope_missing");
        var endpoint=_workspace.Owner.Endpoint??throw new EditRejectedException("skeleton_endpoint_missing");var audience=Audience(endpoint);if(audience.Length==0)throw new EditRejectedException("skeleton_audience_missing");
        var review=new AnimationSkeletonReview(_prepared,_graphHash,_layout!.Id,_layout.ContentHash,_lease!.Identity,_resources.ToArray(),endpoint.View.InstanceId,endpoint.AudienceRevision,audience,_reads.PermissionRevision,"");return review with{Fingerprint=Fingerprint(review)};
    }
    public bool IsCurrent(AnimationSkeletonReview review){try{return review.Fingerprint==Fingerprint(review)&&CaptureReview().Fingerprint==review.Fingerprint;}catch(Exception e)when(e is ArgumentException or InvalidOperationException){return false;}}
    public void Approve(AnimationSkeletonReview review,string fingerprint,bool reviewed){ExactCurrent();if(!reviewed||fingerprint!=review.Fingerprint||!IsCurrent(review))throw new EditRejectedException("skeleton_review_stale");_approval=Copy(review);_start=_time.GetTimestamp();}
    public void Revoke(){Verify();_approval=null;}
    private AnimationSkeletonPage Page(int offset,int limit){if(offset is <0 or >1024||limit is <1 or >8)throw new ArgumentException("Bone page budget.");return new(_layout!.Id,_layout.ContentHash,_layout.Bones.Length,offset,offset+limit<_layout.Bones.Length?offset+limit:null,_layout.Bones.Skip(offset).Take(limit).ToArray());}
    private object Inspect(JsonElement input)
    {
        ExactCurrent();AnimationGraphEdits.Closed(input,"graphId","offset","limit");if(AnimationGraphEdits.Uuid(input,"graphId")!=_prepared.Graph||_approval is null||_time.GetElapsedTime(_start).TotalSeconds>=60||!IsCurrent(_approval))throw new EditRejectedException("skeleton_not_approved");
        if(!input.GetProperty("offset").TryGetInt32(out int offset)||!input.GetProperty("limit").TryGetInt32(out int limit))throw new ArgumentException("Integer bone page.");
        return new{graphId=_prepared.Graph,graphHash=_graphHash,publication=_lease!.Identity,resourcesPrepared=true,livePlay=false,poseMemory=false,page=Page(offset,limit)};
    }
    private void Close(){_approval=null;_layout=null;_resources=[];_lease?.Dispose();_lease=null;_snapshot?.Dispose();_snapshot=null;}
    public void Dispose(){if(_disposed)return;Verify();Close();_disposed=true;}
}
