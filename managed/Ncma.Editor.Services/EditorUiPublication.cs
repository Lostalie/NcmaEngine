using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Editor.Core;
using Ncma.Scene.Rendering;
using Ncma.Ui.Rendering;
using Ncma.Application.Runtime;

namespace Ncma.Editor.Services;

public sealed record UiSelectionReview(Guid Id,EditorViewStamp Edit,Guid Target,SceneUiData? Before,SceneUiData After,string Fingerprint);

// Same scene history, with participant reauthorization on original commit AND Undo/Redo.
// Payloads are already durable immutable artifacts. The only committed mutable state is the
// scene reference; scene Save remains the existing atomic file operation. No second file/history.
public sealed class EditorUiPublication:IEditCommandSceneParticipant,IDisposable
{
    public const string Capability="ncma.ui.selection.apply";
    private readonly EditorWorkspace _workspace;private readonly EditorUiPackagePreparation _prepared;
    private readonly string _root;private readonly Guid _project,_session;private ulong _generation;
    private readonly SceneUiActions? _actions;private readonly TimeProvider _time;
    private readonly Dictionary<UiPackageReference,RuntimeUiPackage> _pins=[];
    private readonly Dictionary<string,UiPackageReference?> _history=[];
    private UiSelectionReview? _review,_proposal;private Guid _token;private long _reviewTime;
    private readonly int _owner=Environment.CurrentManagedThreadId;private bool _disposed;
    private EditSession Edit=>_workspace.Owner.Edit!;
    private sealed record Entry(Guid Target,SceneUiData? Value);
    public EditorUiPublication(EditorWorkspace workspace,EditorUiWorkspace ui,EditorUiPackagePreparation prepared,string root,Guid project,SceneUiActions? actions=null,TimeProvider? time=null)
    {
        ui.RequireProject(root,project);_workspace=workspace;_prepared=prepared;_root=root;_project=project;_actions=actions;_time=time??TimeProvider.System;
        _session=workspace.Stamp.SessionId;_generation=workspace.Stamp.Generation;
        Edit.RegisterCommandParticipant(Descriptor,this);
    }
    private static JsonElement Schema(string text){using var d=JsonDocument.Parse(text);return d.RootElement.Clone();}
    public static CapabilityDescriptor Descriptor=>new(Capability,"Select a human-approved immutable UI generation through the sole reversible scene history.",MutationRisk.Reversible,
        Schema("""{"type":"object","additionalProperties":false,"required":["proposalId"],"properties":{"proposalId":{"type":"string","format":"uuid"}}}"""),Ncma.Assets.Authoring.UiCommands.Descriptor.OutputSchema);
    private void Verify(){if(_owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("UI publication owner required.");ObjectDisposedException.ThrowIf(_disposed,this);var stamp=_workspace.Stamp;if(stamp.SessionId!=_session)throw new EditRejectedException("ui_publication_session_stale");
        // New/Open/Undo replacement keeps the sole EditSession participant. Pending approvals
        // expire; prepared-package grants separately require the new document-generation epoch.
        if(stamp.Generation!=_generation){_generation=stamp.Generation;_review=_proposal=null;_token=Guid.Empty;}}
    private void Writable(EditorViewStamp stamp){Verify();if(stamp!=_workspace.Stamp||Edit.State.Frozen||Edit.State.HistoryInvalidated||Edit.State.EditBusy||_workspace.HasDraft)throw new EditRejectedException("ui_selection_stale_or_busy");}
    private static string Fingerprint(UiSelectionReview r)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(r with{Fingerprint=""})));
    private void Pin(UiPackageReference? reference)
    {
        if(reference is null||_pins.ContainsKey(reference))return;
        if(_pins.Count>=32)throw new EditRejectedException("ui_selection_pin_budget");
        var package=RuntimeUiPackage.Open(_root,_project,reference);
        try{SceneUiPresentation.Validate(package,_actions is not null);_actions?.Validate(package.Capture());_pins.Add(reference,package);}catch{package.Dispose();throw;}
    }
    public UiSelectionReview Review(EditorViewStamp stamp,Guid target,UiPackageReference? package)
    {
        Writable(stamp);if(target==Guid.Empty)throw new ArgumentException("Exact target UUID required.");
        var selected=SceneUiState.Read(Edit.Document.CaptureSnapshot());global::Ncma.Runtime.Entity obj=Edit.Document.World.FindEntity(target);
        SceneUiData? before=Edit.Document.World.Has<SceneUiData>(obj)?Edit.Document.World.Get<SceneUiData>(obj):null;
        if(selected is not null&&before is null)throw new EditRejectedException("ui_selection_other_object");
        package=package?.Validate();if(package is not null)_prepared.RequirePrepared(package);
        var after=package is null?SceneUiData.Off:SceneUiData.From(package);
        _proposal=null;_token=Guid.Empty;var review=new UiSelectionReview(Guid.NewGuid(),stamp,target,before,after,"");
        _review=review with{Fingerprint=Fingerprint(review)};_reviewTime=_time.GetTimestamp();return _review;
    }
    public Guid Approve(UiSelectionReview displayed,string fingerprint,bool reviewed)
    {
        Writable(displayed.Edit);if(!reviewed||_review!=displayed||fingerprint!=displayed.Fingerprint||Fingerprint(displayed)!=fingerprint||Expired())throw new EditRejectedException("ui_selection_review_stale");
        if(displayed.After.Package is {} next)_prepared.RequirePrepared(next);
        Pin(displayed.Before?.Package);Pin(displayed.After.Package);Writable(displayed.Edit);if(Expired())throw new EditRejectedException("ui_selection_review_stale");
        _proposal=displayed;_review=null;_token=Guid.NewGuid();return _token;
    }
    private bool Expired()=>_time.GetElapsedTime(_reviewTime,_time.GetTimestamp())>=TimeSpan.FromSeconds(60);
    public CapabilityRequest Request(Guid proposal){Verify();return new(EditSession.ContractVersion,Guid.NewGuid(),Edit.SessionId,Edit.Revision,Capability,JsonSerializer.SerializeToElement(new{proposalId=proposal}));}
    public CapabilityResult ApplyLocal(Guid proposal)
    {
        var request=Request(proposal);var p=Proposal(request.Input);var result=Edit.Invoke(request,new([Capability],objectScope:[p.Target],componentScope:[SceneUiData.TypeId]));
        if(result.Status!="ok")throw new EditRejectedException(result.Code);return result;
    }
    private UiSelectionReview Proposal(JsonElement input)
    {
        Verify();if(input.ValueKind!=JsonValueKind.Object||input.EnumerateObject().Count()!=1||!input.TryGetProperty("proposalId",out var id)||id.GetGuid()!=_token||_proposal is null||_proposal.Edit!=_workspace.Stamp||Expired())throw new EditCommandRejectedException("ui_selection_proposal_stale");
        if(_proposal.After.Package is {} next)_prepared.RequirePrepared(next);return _proposal;
    }
    public JsonElement SceneOperations(JsonElement input)
    {var p=Proposal(input);return JsonSerializer.SerializeToElement(new[]{new{op="set_component",objectId=p.Target,typeId=SceneUiData.TypeId,version=1,data=Edit.Document.World.Components.Encode(p.After)}});}
    private static string Key(ParticipantMemento m)=>Convert.ToHexString(SHA256.HashData([..m.Before,..m.After]));
    public ParticipantMemento Prepare(JsonElement input)
    {
        var p=Proposal(input);var m=new ParticipantMemento(JsonSerializer.SerializeToUtf8Bytes(new Entry(p.Target,p.Before)),JsonSerializer.SerializeToUtf8Bytes(new Entry(p.Target,p.After)));
        string key=Key(m);if(!_history.ContainsKey(key)&&_history.Count>=128)throw new EditCommandRejectedException("ui_selection_history_budget");
        _history[key]=p.After.Package;return m;
    }
    public void Authorize(ParticipantMemento m)
    {
        Verify();if(m.Before.Length>4096||m.After.Length>4096||!_history.TryGetValue(Key(m),out var required))throw new EditCommandRejectedException("ui_selection_scope_denied",denied:true);
        if(required is not null)_prepared.RequirePrepared(required);
        foreach(byte[] bytes in new[]{m.Before,m.After}){var e=JsonSerializer.Deserialize<Entry>(bytes)!;if(e.Value?.Package is {} p&&!_pins.ContainsKey(p))throw new EditCommandRejectedException("ui_selection_not_pinned");}
    }
    public void Validate(ParticipantMemento m,bool forward)=>Authorize(m);
    public void Publish(ParticipantMemento m,bool forward)=>Authorize(m); // No mutable IO or GPU publication.
    public void Complete(ParticipantMemento m,bool forward){Verify();_proposal=null;_token=Guid.Empty;}
    public void Compensate(ParticipantMemento m,bool forward){Verify();} // Nothing external was mutated.
    public void Revoke(){Verify();_history.Clear();_review=_proposal=null;_token=Guid.Empty;}
    public void Dispose(){if(_disposed)return;if(_owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("UI publication owner required.");foreach(var p in _pins.Values)p.Dispose();_pins.Clear();_history.Clear();_review=_proposal=null;_disposed=true;}
}
