using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ncma.Assets;
using Ncma.Assets.Authoring;
using Ncma.Editor.Core;
using Ncma.Editor.Transport;
using Ncma.Scene.Rendering;

namespace Ncma.Editor.Services;

public sealed record EnvironmentPresetReview(Guid Review, EditorViewStamp Edit, ulong AssetRevision, Guid ObjectId,
    Guid AssetId, ulong Generation, string ContentHash, string Path, bool Synthetic, string Fingerprint);
public sealed record EnvironmentCatalogRow(Guid AssetId, ulong Generation, string ContentHash, string State, Guid ProposalId);
public sealed record EnvironmentReadReview(EditorViewStamp Edit, ulong Revision, Guid EndpointId, ulong AudienceRevision, Guid[] Assets, ConnectionView[] Audience, string Fingerprint);

// Trusted local workspace. No IO in inspection/proposal projection; immutable preparation occurs
// only on explicit human review/approval. GPU refresh remains the formal host's safe-boundary job.
public sealed class EditorEnvironmentWorkspace : IEditCommandSceneParticipant, IDisposable
{
    public const string ApplyCapability="ncma.render.environment.apply", ListCapability="ncma.environments.list";
    private readonly EditorWorkspace _workspace;private readonly AssetProjectPaths _paths;private readonly Guid _project,_session;
    private readonly Func<EnvironmentPresetLibrary> _loadPreset;private readonly TimeProvider _time;private readonly EnvironmentPublication _publication;
    private readonly Dictionary<Guid,(string Path,EnvironmentPackage Package)> _known=[];
    private readonly Dictionary<string,EnvironmentAssetDescriptor> _fileGrants=[];
    private readonly Dictionary<Guid,(EnvironmentPresetReview Review,EnvironmentAssetDescriptor Descriptor,long Time)> _proposals=[];
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private ulong _generation,_revision=ulong.MaxValue;private bool _disposed;private long _reviewTime,_readTime;
    private EnvironmentPresetReview? _review;private EnvironmentAuthoringEntry[] _entries=[];
    private HashSet<Guid> _readApproved=[];private Func<bool>? _audience;private ulong _readGeneration,_readRevision;private EditorViewStamp _readStamp;
    public EnvironmentPresetReview? Review { get { Verify();return _review; } }
    public ulong Revision { get { Verify();return _revision; } }
    private EditSession Edit=>_workspace.Owner.Edit!;
    private AssetProjectAuthoring Assets=>_workspace.Owner.Assets??throw new EditRejectedException("environment_project_missing");
    public EditorEnvironmentWorkspace(EditorWorkspace workspace,string root,Guid project,Func<EnvironmentPresetLibrary> loadPreset,TimeProvider? time=null,Action<string>? fault=null)
    {
        _workspace=workspace;_paths=new(root);_project=project;_loadPreset=loadPreset;_time=time??TimeProvider.System;
        _session=workspace.Stamp.SessionId;_generation=workspace.Stamp.Generation;
        if(Assets.ProjectId!=project)throw new ArgumentException("Environment authoring project mismatch.");
        _publication=new(_paths,project,Assets.Clock,FileApproved,fault);
        try { Synchronize();Edit.RegisterCommandParticipant(ApplyDescriptor,this,(ListDescriptor,Inspect)); }
        catch{_publication.Dispose();throw;}
    }
    private static JsonElement Schema(string text){using var d=JsonDocument.Parse(text);return d.RootElement.Clone();}
    private static JsonElement ListOutput(string data)
    {
        var success=JsonNode.Parse(MaterialCommands.Descriptor.OutputSchema.GetRawText())!.AsObject();
        var properties=success["properties"]!.AsObject();properties["status"]=JsonNode.Parse("{\"const\":\"ok\"}");properties["code"]=JsonNode.Parse("{\"const\":\"ok\"}");
        properties["changed"]=JsonNode.Parse("{\"const\":false}");properties["replayed"]=JsonNode.Parse("{\"const\":false}");properties["data"]=JsonNode.Parse(data);
        var failure=success.DeepClone().AsObject();var errors=failure["properties"]!.AsObject();errors["status"]=JsonNode.Parse("{\"enum\":[\"error\",\"denied\",\"conflict\"]}");errors["code"]=JsonNode.Parse("{\"type\":\"string\",\"maxLength\":128}");
        errors["data"]=JsonNode.Parse("{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"message\":{\"type\":\"string\",\"maxLength\":4194304}}}");
        return JsonSerializer.SerializeToElement(new JsonObject{["type"]="object",["oneOf"]=new JsonArray(success,failure)});
    }
    public static CapabilityDescriptor ApplyDescriptor=>new(ApplyCapability,"Apply a human-prepared exact environment proposal; publish descriptor and scene in one reversible history entry.",MutationRisk.Reversible,
        Schema("""{"type":"object","additionalProperties":false,"required":["proposalId","expectedAssetRevision"],"properties":{"proposalId":{"type":"string","format":"uuid"},"expectedAssetRevision":{"type":"integer","minimum":0}}}"""),MaterialCommands.Descriptor.OutputSchema);
    public static CapabilityDescriptor ListDescriptor=>new(ListCapability,"Read approved environment UUID metadata and current prepared proposal tokens only; no paths, source or GPU access.",MutationRisk.ReadOnly,
        Schema("""{"type":"object","additionalProperties":false,"properties":{"offset":{"type":"integer","minimum":0,"maximum":256},"limit":{"type":"integer","minimum":1,"maximum":32}}}"""),
        ListOutput("""{"type":"object","additionalProperties":false,"required":["revision","total","items","nextOffset"],"properties":{"revision":{"type":"integer","minimum":0},"total":{"type":"integer","minimum":0,"maximum":256},"nextOffset":{"anyOf":[{"type":"integer","minimum":0,"maximum":288},{"type":"null"}]},"items":{"type":"array","maxItems":32,"items":{"type":"object","additionalProperties":false,"required":["assetId","generation","contentHash","state","proposalId"],"properties":{"assetId":{"type":"string","format":"uuid"},"generation":{"type":"integer","minimum":1},"contentHash":{"type":"string","maxLength":64},"state":{"enum":["registered","prepared"]},"proposalId":{"type":"string","format":"uuid"}}}}}}"""));
    private void Verify(){if(_owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Environment workspace owner required.");ObjectDisposedException.ThrowIf(_disposed,this);_=Edit.State;}
    private bool Current()=>!_disposed&&_workspace.Stamp.SessionId==_session&&_workspace.Stamp.Generation==_generation;
    private void Writable(EditorViewStamp stamp){Verify();if(!Current()||stamp!=_workspace.Stamp||Edit.State.Frozen||Edit.State.HistoryInvalidated||Edit.State.EditBusy||_workspace.HasDraft)throw new EditRejectedException("environment_view_stale_or_frozen");}
    private bool FileApproved(string path,EnvironmentAssetDescriptor d)=>Current()&&_fileGrants.TryGetValue(path,out var allowed)&&allowed==d;
    public void Synchronize()
    {
        Verify();if(_workspace.Stamp.SessionId!=_session)throw new EditRejectedException("environment_session_stale");
        if(_generation!=_workspace.Stamp.Generation){Revoke();_generation=_workspace.Stamp.Generation;_revision=ulong.MaxValue;}
        if(_revision==Assets.Clock.Revision)return;
        var candidate=EnvironmentAuthoringCatalog.Scan(_paths,_project);_entries=candidate.Entries.ToArray();_revision=Assets.Clock.Revision;RevokeRead();
    }
    public EnvironmentCatalogRow[] Rows(int offset=0,int limit=16)
    {
        Verify();if(offset is <0 or >256||limit is <1 or >32)throw new ArgumentException("Environment page bounds.");
        return Catalog().Skip(offset).Take(limit).ToArray();
    }
    private EnvironmentCatalogRow[] Catalog()
    {
        var entries=_entries.ToDictionary(e=>e.Descriptor.AssetId,e=>new EnvironmentCatalogRow(e.Descriptor.AssetId,e.Descriptor.Generation,e.Descriptor.ContentHash,"registered",Guid.Empty));
        foreach(var proposal in _proposals)if(ProposalCurrent(proposal.Value.Review,proposal.Value.Time)){
            var d=proposal.Value.Descriptor;entries[d.AssetId]=new(d.AssetId,d.Generation,d.ContentHash,entries.ContainsKey(d.AssetId)?"registered":"prepared",proposal.Key);
        }
        return entries.Values.OrderBy(e=>e.AssetId).ToArray();
    }
    public EnvironmentPresetReview ReviewNeutral(EditorViewStamp stamp,Guid target)
    {
        Writable(stamp);Synchronize();RequireTarget(target);
        using var library=_loadPreset();var package=library.Package.CloneForAsset(Guid.NewGuid(),1);
        return InstallReview(stamp,target,$"assets/environment-{package.AssetId:N}.ncenv",package,true);
    }
    public EnvironmentPresetReview ReviewAsset(EditorViewStamp stamp,Guid target,Guid asset)
    {
        Writable(stamp);Synchronize();RequireTarget(target);
        var row=_entries.SingleOrDefault(e=>e.Descriptor.AssetId==asset)??throw new EditRejectedException("environment_asset_missing");
        var package=EnvironmentAuthoringFiles.ReadPackage(_paths,row.Descriptor);
        return InstallReview(stamp,target,row.Path,package,false);
    }
    private EnvironmentPresetReview InstallReview(EditorViewStamp stamp,Guid target,string path,EnvironmentPackage package,bool synthetic)
    {
        CancelReview();
        if(!_known.ContainsKey(package.AssetId)&&_known.Count>=32)throw new EditRejectedException("environment_review_budget");
        _known[package.AssetId]=(path,package);
        var review=new EnvironmentPresetReview(Guid.NewGuid(),stamp,Assets.Clock.Revision,target,package.AssetId,package.Generation,package.ContentHash,path,synthetic,"");
        _review=review with{Fingerprint=Fingerprint(review)};_reviewTime=_time.GetTimestamp();return _review;
    }
    private static string Fingerprint<T>(T value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private void RequireTarget(Guid id){if(id==Guid.Empty)throw new ArgumentException("Target UUID required.");_=Edit.Document.World.FindObject(id);}
    private bool ProposalCurrent(EnvironmentPresetReview review,long time)=>Current()&&review.Edit==_workspace.Stamp&&review.AssetRevision==Assets.Clock.Revision&&_time.GetElapsedTime(time,_time.GetTimestamp())<TimeSpan.FromSeconds(60);
    // Local human approval only. Prepared data is not project-visible until the command succeeds.
    public Guid Approve(EnvironmentPresetReview displayed,string fingerprint,bool reviewed)
    {
        Writable(displayed.Edit);
        if(!reviewed||_review!=displayed||displayed.Fingerprint!=fingerprint||Fingerprint(displayed with{Fingerprint=""})!=fingerprint||!ProposalCurrent(displayed,_reviewTime))throw new EditRejectedException("environment_review_stale");
        var known=_known[displayed.AssetId];var d=EnvironmentAssetDescriptor.FromPackage(_project,known.Package);
        EnvironmentAuthoringCatalog.Scan(_paths,_project).RequireIdentity(known.Path,d.AssetId);
        var had=_fileGrants.TryGetValue(known.Path,out var old);_fileGrants[known.Path]=d;
        try {
            _publication.Generations.Prepare(known.Package);if(!ProposalCurrent(displayed,_reviewTime))throw new EditRejectedException("environment_review_stale");
            // Bound proposal registry; no arbitrary payload/file path is accepted by the Agent tool.
            _proposals.Clear();Guid token=Guid.NewGuid();_proposals.Add(token,(displayed,d,_time.GetTimestamp()));_review=null;return token;
        }catch{if(had)_fileGrants[known.Path]=old!;else _fileGrants.Remove(known.Path);throw;}
    }
    public CapabilityRequest Request(Guid proposal)=>new(EditSession.ContractVersion,Guid.NewGuid(),Edit.SessionId,Edit.Revision,ApplyCapability,JsonSerializer.SerializeToElement(new{proposalId=proposal,expectedAssetRevision=Assets.Clock.Revision}));
    public CapabilityResult ApplyLocal(Guid proposal)
    {
        Verify();var p=RequireProposal(Request(proposal).Input);
        var result=Edit.Invoke(Request(proposal),new([ApplyCapability],objectScope:[p.Review.ObjectId],componentScope:[EnvironmentLightingData.TypeId]));
        if(result.Status!="ok")throw new EditRejectedException(result.Code);return result;
    }
    private (EnvironmentPresetReview Review,EnvironmentAssetDescriptor Descriptor,long Time) RequireProposal(JsonElement input)
    {
        Verify();if(input.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Environment proposal input.");
        var fields=input.EnumerateObject().ToArray();if(fields.Length!=2||fields.Select(p=>p.Name).Distinct().Count()!=2||fields.Any(p=>p.Name is not "proposalId" and not "expectedAssetRevision"))throw new ArgumentException("Closed environment proposal.");
        if(!_proposals.TryGetValue(input.GetProperty("proposalId").GetGuid(),out var proposal)||input.GetProperty("expectedAssetRevision").GetUInt64()!=Assets.Clock.Revision||!ProposalCurrent(proposal.Review,proposal.Time))throw new EditCommandRejectedException("environment_proposal_stale");
        if(!FileApproved(proposal.Review.Path,proposal.Descriptor))throw new EditCommandRejectedException("environment_scope_denied",denied:true);
        return proposal;
    }
    public JsonElement SceneOperations(JsonElement input)
    {
        var p=RequireProposal(input);var d=p.Descriptor;var value=new EnvironmentLightingData(1,d.AssetId,d.Generation,d.ContentHash,1,0,true);
        return JsonSerializer.SerializeToElement(new[]{new{op="set_component",objectId=p.Review.ObjectId,typeId=EnvironmentLightingData.TypeId,version=1,data=Edit.Document.World.Components.Encode(value)}});
    }
    public ParticipantMemento Prepare(JsonElement input){var p=RequireProposal(input);return _publication.Prepare(p.Review.Path,p.Descriptor,Assets.Clock.Revision);}
    public void Authorize(ParticipantMemento m)=>_publication.Authorize(m);
    public void Validate(ParticipantMemento m,bool forward)=>_publication.Validate(m,forward);
    public void Publish(ParticipantMemento m,bool forward)=>_publication.Publish(m,forward);
    public void Complete(ParticipantMemento m,bool forward)=>_publication.Complete(m,forward);
    public void Compensate(ParticipantMemento m,bool forward)=>_publication.Compensate(m,forward);

    private static ConnectionView[] Audience(EditorEndpoint endpoint)=>endpoint.View.Connections.Where(c=>c.Paired&&c.Connected).OrderBy(c=>c.ConnectionId).Select(c=>c with{PendingCount=0}).ToArray();
    public EnvironmentReadReview ReviewRead(Guid[] ids)
    {
        Writable(_workspace.Stamp);if(_revision!=Assets.Clock.Revision)throw new EditRejectedException("environment_catalog_stale");
        var rows=Catalog();if(ids.Length is <1 or >64||ids.Distinct().Count()!=ids.Length||ids.Any(id=>rows.All(r=>r.AssetId!=id)))throw new EditRejectedException("environment_read_scope_invalid");
        var endpoint=_workspace.Owner.Endpoint??throw new EditRejectedException("environment_endpoint_missing");var audience=Audience(endpoint);
        if(audience.Length==0)throw new EditRejectedException("environment_audience_missing");
        var review=new EnvironmentReadReview(_workspace.Stamp,_revision,endpoint.View.InstanceId,endpoint.AudienceRevision,ids.Order().ToArray(),audience,"");return review with{Fingerprint=Fingerprint(review)};
    }
    public void ApproveRead(EnvironmentReadReview shown,string fingerprint,bool reviewed)
    {
        Verify();var actual=ReviewRead(shown.Assets);if(!reviewed||shown.Fingerprint!=fingerprint||actual.Fingerprint!=fingerprint||Fingerprint(shown with{Fingerprint=""})!=fingerprint)throw new EditRejectedException("environment_read_review_stale");
        var endpoint=_workspace.Owner.Endpoint!;var audience=shown.Audience.Select(a=>a.ConnectionId).ToArray();Guid instance=shown.EndpointId;
        _readApproved=new(shown.Assets);_readTime=_time.GetTimestamp();_readRevision=_revision;_readGeneration=Edit.DocumentGeneration;_readStamp=shown.Edit;
        _audience=()=>ReferenceEquals(_workspace.Owner.Endpoint,endpoint)&&endpoint.View.InstanceId==instance&&endpoint.AudienceRevision==shown.AudienceRevision&&Audience(endpoint).Select(a=>a.ConnectionId).SequenceEqual(audience);
    }
    private bool ReadCurrent()
    {
        bool active=Current()&&!Edit.State.Frozen&&!Edit.State.HistoryInvalidated&&_readStamp==_workspace.Stamp&&_readApproved.Count>0&&_readRevision==_revision&&_revision==Assets.Clock.Revision&&_readGeneration==Edit.DocumentGeneration&&
            _time.GetElapsedTime(_readTime,_time.GetTimestamp())<TimeSpan.FromSeconds(60)&&(_audience?.Invoke()??false);
        if(!active)RevokeRead();return active;
    }
    private object Inspect(JsonElement input)
    {
        Verify();if(input.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Environment page input.");var fields=input.EnumerateObject().ToArray();
        if(fields.Select(p=>p.Name).Distinct().Count()!=fields.Length||fields.Any(p=>p.Name is not "offset" and not "limit"))throw new ArgumentException("Closed environment inspection.");
        int offset=input.TryGetProperty("offset",out var o)?o.GetInt32():0,limit=input.TryGetProperty("limit",out var l)?l.GetInt32():16;
        if(offset is <0 or >256||limit is <1 or >32)throw new ArgumentException("Environment page bounds.");
        var rows=ReadCurrent()?Catalog().Where(r=>_readApproved.Contains(r.AssetId)).ToArray():[];
        return new{revision=_revision,total=rows.Length,items=rows.Skip(offset).Take(limit).ToArray(),nextOffset=offset+limit<rows.Length?(int?)(offset+limit):null};
    }
    public void CancelReview(){Verify();if(_review is {} review&&!_fileGrants.ContainsKey(review.Path))_known.Remove(review.AssetId);_review=null;}
    public void RevokeRead(){Verify();_readApproved=[];_audience=null;}
    public void Revoke(){Verify();_fileGrants.Clear();_proposals.Clear();_review=null;RevokeRead();}
    public void Dispose(){if(_disposed)return;Verify();Revoke();_publication.Dispose();_disposed=true;}
}
