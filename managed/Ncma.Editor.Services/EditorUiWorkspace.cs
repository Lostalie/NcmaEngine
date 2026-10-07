using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Assets.Authoring;
using Ncma.Editor.Core;
using Ncma.Ui;

namespace Ncma.Editor.Services;

public readonly record struct EditorUiStamp(EditorViewStamp Edit,Guid Document,ulong Content,ulong Resources);
public readonly record struct UiResourceReference(Guid Id,UiResourceKind Kind);
public sealed record UiFileReview(Guid Review,string Path,Guid Document,string Hash,bool Create,EditorViewStamp Edit,ulong AssetRevision,ulong Resources);
// Sole command history remains Editor.Core. All file/resource authority originates in local UI.
public sealed class EditorUiWorkspace : IDisposable
{
    private readonly EditorWorkspace _workspace; private readonly string _root; private readonly Guid _session;
    private readonly int _thread=Environment.CurrentManagedThreadId;
    private readonly Dictionary<string,Guid> _grants=[];
    private readonly HashSet<string> _actions;
    public string[] RegisteredActions=>_actions.Order(StringComparer.Ordinal).ToArray();
    private UiDefinition? _definition,_reviewDefinition; private UiDraft? _draft;
    private ulong _editGeneration,_assetRevision; private bool _disposed;
    private string? _path;
    public UiWriteScope Scope { get; }
    public UiAuthoringResourceCatalog Resources { get; }
    public UiFileReview? Review { get; private set; }
    public UiResourceReference[] ReviewDependencies=>_reviewDefinition?.Elements.SelectMany(e=>new[]{new UiResourceReference(e.Font,UiResourceKind.Font),new UiResourceReference(e.Image,UiResourceKind.Image)}).Where(e=>e.Id!=Guid.Empty).Distinct().ToArray()??[];
    public ulong Revision { get; private set; }=1;
    public bool HasDraft=>_draft is not null;
    public string? Path=>_path;
    public Guid DocumentId=>_definition?.AssetId??Guid.Empty;
    public bool Writable=>Current()&&_path is not null&&_grants.GetValueOrDefault(_path)==DocumentId&&!_workspace.Owner.Edit!.State.Frozen&&!_workspace.Owner.Edit.State.HistoryInvalidated;
    public EditorUiStamp Stamp { get { Verify();return new(_workspace.Stamp,DocumentId,Revision,Resources.Revision); } }
    private AssetProjectAuthoring Assets=>_workspace.Owner.Assets??throw new InvalidOperationException("Configure project authoring before UI files.");
    public EditorUiWorkspace(EditorWorkspace workspace,string root,IEnumerable<string>? registeredActions=null)
    {
        _actions=new(registeredActions??["preview.test"],StringComparer.Ordinal);
        if(_actions.Count>128||_actions.Any(a=>a.Length is <1 or >128||a.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not '.' and not '_' and not '-')))throw new ArgumentException("Trusted bounded semantic action registration required.");
        _workspace=workspace;_root=System.IO.Path.GetFullPath(root);_session=workspace.Stamp.SessionId;_editGeneration=workspace.Stamp.Generation;Resources=new(_root);
        Scope=UiWriteScope.ForHost((path,id)=>_grants.GetValueOrDefault(path)==id,Resources.Contains,Current);
    }
    private bool Current()=>!_disposed&&_workspace.Stamp.SessionId==_session&&_workspace.Stamp.Generation==_editGeneration;
    private void Verify(){ObjectDisposedException.ThrowIf(_disposed,this);if(_thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("UI workspace owner required.");}
    private void Check(EditorUiStamp expected,bool write=false) { Verify();if(expected!=Stamp||!Current()||(write&&!Writable))throw new EditRejectedException("ui_view_stale_or_readonly"); }
    public UiDefinition? Capture() { Verify();return _definition is null?null:UiCodec.Copy(_definition); }
    public void Synchronize()
    {
        Verify();if(_workspace.Stamp.SessionId!=_session) { Close();return; }
        if(_workspace.Stamp.Generation!=_editGeneration){Close();_editGeneration=_workspace.Stamp.Generation;return;}
        if(_draft is not null&&(_workspace.Owner.Edit!.State.Frozen||!_workspace.Owner.Edit.State.EditBusy))Cancel();
        if(_assetRevision==Assets.Clock.Revision)return;
        Cancel();Review=null;_reviewDefinition=null;_assetRevision=Assets.Clock.Revision;
        if(_path is not null) {
            // Explicit authoring revision notifications only, never per-frame file/hash polling.
            try { Install(UiAuthoringSource.Open(_root,_path)); }
            catch(FileNotFoundException){_definition=null;Revision=checked(Revision+1);} // Undo creation; retain exact grant for authorized Redo.
            catch(Exception e) when(e is IOException or ArgumentException or UnauthorizedAccessException){_definition=null;Revision=checked(Revision+1);throw;}
        }
    }
    public UiFileReview ReviewFile(EditorViewStamp expected,string path,bool create,string name="UI")
    {
        Verify();if(expected!=_workspace.Stamp||!Current()||HasDraft)throw new EditRejectedException("ui_review_stale_or_busy");
        path=UiAuthoringSource.ValidatePath(path);UiDefinition candidate;
        if(create){new AssetProjectPaths(_root).Resolve(path);if(File.Exists(System.IO.Path.Combine(_root,path)))throw new ArgumentException("UI creation never overwrites a file.");candidate=UiDefinition.Create(Guid.NewGuid(),name);}
        else candidate=UiAuthoringSource.Open(_root,path);
        byte[] bytes=UiCodec.Encode(candidate);
        var review=new UiFileReview(Guid.NewGuid(),path,candidate.AssetId,Convert.ToHexString(SHA256.HashData(bytes)),create,expected,Assets.Clock.Revision,Resources.Revision);
        _reviewDefinition=candidate;Review=review;return review;
    }
    public void ConfirmFile(Guid reviewId,Guid exactDocument,bool writeApproved)
    {
        Verify();var review=Review??throw new EditRejectedException("ui_review_missing");var candidate=_reviewDefinition!;
        if(review.Review!=reviewId||review.Document!=exactDocument||review.Edit!=_workspace.Stamp||review.AssetRevision!=Assets.Clock.Revision||review.Resources!=Resources.Revision||!Current()||HasDraft)throw new EditRejectedException("ui_review_stale");
        if(writeApproved){Resources.Require(candidate);RequireActions(candidate);}
        if(!review.Create&&Convert.ToHexString(SHA256.HashData(UiCodec.Encode(UiAuthoringSource.Open(_root,review.Path))))!=review.Hash)throw new EditRejectedException("ui_document_conflict");
        if(review.Create&&!writeApproved)throw new EditRejectedException("ui_creation_write_approval_required");
        if(writeApproved&&_workspace.Owner.Edit!.State.Frozen)throw new EditRejectedException("ui_play_frozen");
        var oldGrants=_grants.ToArray();_grants.Clear();if(writeApproved)_grants.Add(review.Path,exactDocument);
        try {
            if(review.Create)CommitCandidate(review.Path,candidate);
            _path=review.Path;Install(candidate);_assetRevision=Assets.Clock.Revision;Review=null;_reviewDefinition=null;
        } catch { _grants.Clear();foreach(var grant in oldGrants)_grants.Add(grant.Key,grant.Value);throw; }
    }
    private void CommitCandidate(string path,UiDefinition definition)
    {
        RequireActions(definition);
        var edit=_workspace.Owner.Edit!;Guid proposal=Assets.Ui.PrepareProposal(path,definition,Assets.Clock.Revision);
        try {
            var result=edit.Invoke(new(2,Guid.NewGuid(),edit.SessionId,edit.Revision,UiCommands.CapabilityName,
                JsonSerializer.SerializeToElement(new{proposalId=proposal,expectedAssetRevision=Assets.Clock.Revision})),new([UiCommands.CapabilityName]));
            if(result.Status!="ok")throw new EditRejectedException(result.Code);
        } finally { Assets.Ui.CancelProposal(proposal); }
    }
    private void Install(UiDefinition definition) { _definition=UiCodec.Copy(definition);Revision=checked(Revision+1); }
    public void Begin(EditorUiStamp expected)
    {
        Check(expected,true);if(_draft is not null)throw new EditRejectedException("ui_draft_busy");
        // Check the displayed committed document at interaction start, not every frame.
        // UiDraft separately verifies its frozen file at commit, covering later changes.
        ValidateFile();_draft=new(_workspace.Owner.Edit!,Assets.Ui,Assets.Clock,_path!);
    }
    public void Update(IReadOnlyList<UiEdit> edits)
    {
        Verify();var draft=_draft??throw new EditRejectedException("ui_draft_missing");draft.Update(edits);var candidate=draft.Capture();Resources.Require(candidate);RequireActions(candidate);_ = new UiRuntime(new(candidate));_definition=candidate;Revision=checked(Revision+1);
    }
    private void RequireActions(UiDefinition d){if(d.Elements.Any(e=>e.Action.Length>0&&!_actions.Contains(e.Action)))throw new ArgumentException("Unregistered C# semantic UI Action.");}
    public void Confirm()
    {
        Verify();var draft=_draft??throw new EditRejectedException("ui_draft_missing");
        try { var candidate=draft.Capture();Resources.Require(candidate);RequireActions(candidate);_ = new UiRuntime(new(candidate));var result=draft.Commit(new([UiCommands.CapabilityName]));if(result is not null&&result.Status!="ok")throw new EditRejectedException(result.Code); }
        finally { draft.Dispose();_draft=null;_assetRevision=Assets.Clock.Revision;if(_path is not null)Install(UiAuthoringSource.Open(_root,_path)); }
    }
    public void Edit(EditorUiStamp expected,IReadOnlyList<UiEdit> edits)
    { Begin(expected);try{Update(edits);Confirm();}catch{Cancel();throw;} }
    public void Cancel()
    {
        Verify();if(_draft is null)return;_draft.Dispose();_draft=null;
        if(_path is not null)Install(UiAuthoringSource.Open(_root,_path));
    }
    public void ValidateFile() { Verify();if(_path is not null){var current=UiAuthoringSource.Open(_root,_path);if(_definition is not null&& !UiCodec.Encode(current).AsSpan().SequenceEqual(UiCodec.Encode(_definition)))throw new EditRejectedException("ui_document_conflict");} }
    public void Revoke() { Verify();Cancel();_grants.Clear();Review=null;_reviewDefinition=null;Revision=checked(Revision+1); }
    public void Close() { Verify();Revoke();_path=null;_definition=null;Revision=checked(Revision+1); }
    public void Dispose() { if(_disposed)return;Close();Resources.Revoke();_disposed=true; }
}
