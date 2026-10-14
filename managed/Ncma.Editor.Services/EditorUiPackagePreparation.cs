using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Assets.Authoring;
using Ncma.Editor.Core;
using Ncma.Ui.Rendering;
using Ncma.Application.Runtime;

namespace Ncma.Editor.Services;

public sealed record UiPackageReview(Guid Id,EditorUiStamp Stamp,Guid Project,UiPackageReference Package,string Fingerprint);

// Explicit local human preparation. This is NOT yet selection publication: no command/history
// entry and no live HUD exchange occurs until the separate reversible selection command commits.
public sealed class EditorUiPackagePreparation : IDisposable
{
    private readonly EditorUiWorkspace _ui;private readonly Guid _project;private readonly UiPackageGenerationStore _store;
    private readonly Dictionary<UiPackageReference,ulong> _grants=[];private readonly TimeProvider _time;
    private readonly int _owner=Environment.CurrentManagedThreadId;private bool _busy,_disposed;
    private UiPackageReview? _review;private byte[]? _bytes;private long _reviewTime;private EditorUiStamp? _preparing;
    public EditorUiPackagePreparation(EditorUiWorkspace ui,string root,Guid project,TimeProvider? time=null,SceneUiActions? actions=null)
    {
        ArgumentNullException.ThrowIfNull(ui);ui.RequireProject(root,project);
        _ui=ui;_project=project;_time=time??TimeProvider.System;
        _actions=actions;
        _store=new(new(root),project,r=>!_disposed&&_ui.Writable&&_ui.DocumentId==r.DocumentId&&!_ui.HasDraft&&_grants.TryGetValue(r,out var epoch)&&epoch==_ui.Stamp.Edit.Generation&&(_preparing is not {} stamp||
            stamp==_ui.Stamp&&_time.GetElapsedTime(_reviewTime,_time.GetTimestamp())<TimeSpan.FromSeconds(60)),Validate);
    }
    private readonly SceneUiActions? _actions;
    private void Validate(ReadOnlySpan<byte> bytes,Guid project,UiPackageReference reference)
    {using var package=RuntimeUiPackage.Decode(bytes,project,reference.DocumentId,reference.Generation,reference.Hash);SceneUiPresentation.Validate(package,_actions is not null);_actions?.Validate(package.Capture());}
    private void Verify(){if(_owner!=Environment.CurrentManagedThreadId||_busy)throw new InvalidOperationException("UI preparation owner/nonreentry boundary.");ObjectDisposedException.ThrowIf(_disposed,this);}
    private static string Fingerprint(UiPackageReview r)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(r with{Fingerprint=""})));
    private void Current(EditorUiStamp stamp)
    {if(stamp!=_ui.Stamp||!_ui.Writable||_ui.HasDraft)throw new EditRejectedException("ui_publication_stale_readonly_or_draft");_ui.ValidateFile();}
    public UiPackageReview Review(EditorUiStamp stamp,ulong generation)
    {
        Verify();Current(stamp);_busy=true;
        try{
            var definition=_ui.Capture()??throw new EditRejectedException("ui_document_missing");
            byte[] bytes=_ui.Resources.CookPackage(_project,generation,definition);Current(stamp);
            var reference=UiPackageGenerationStore.Reference(_project,definition.AssetId,generation,Convert.ToHexString(SHA256.HashData(bytes)));
            Validate(bytes,_project,reference);
            var review=new UiPackageReview(Guid.NewGuid(),stamp,_project,reference,"");
            _review=review with{Fingerprint=Fingerprint(review)};_bytes=bytes;_reviewTime=_time.GetTimestamp();return _review;
        }finally{_busy=false;}
    }
    public UiPackageReference Approve(UiPackageReview displayed,string fingerprint,bool reviewed,CancellationToken cancellation=default,Action<string>? fault=null)
        => ApproveCore(displayed,fingerprint,reviewed,null,cancellation,fault);
    // Human reviews the same exact content again before an explicitly named retained stage can
    // be recovered. This never grants permission to unrelated stale files or another generation.
    public UiPackageReference RecoverPrepared(UiPackageReview displayed,string fingerprint,bool reviewed,string preparedPath,CancellationToken cancellation=default)
        => ApproveCore(displayed,fingerprint,reviewed,preparedPath,cancellation,null);
    private UiPackageReference ApproveCore(UiPackageReview displayed,string fingerprint,bool reviewed,string? preparedPath,CancellationToken cancellation,Action<string>? fault)
    {
        Verify();Current(displayed.Stamp);
        if(!reviewed||_review!=displayed||fingerprint!=displayed.Fingerprint||Fingerprint(displayed)!=fingerprint||
            _time.GetElapsedTime(_reviewTime,_time.GetTimestamp())>=TimeSpan.FromSeconds(60))throw new EditRejectedException("ui_package_review_stale");
        _busy=true;_preparing=displayed.Stamp;bool existing=_grants.TryGetValue(displayed.Package,out ulong previousEpoch);
        try{
            if(!existing&&_grants.Count>=UiPackageGenerationStore.MaxGenerations)throw new EditRejectedException("ui_package_grant_budget");
            _grants[displayed.Package]=displayed.Stamp.Edit.Generation;var result=preparedPath is null?
                _store.Prepare(displayed.Package.DocumentId,displayed.Package.Generation,_bytes!,cancellation,fault):
                _store.RecoverPrepared(displayed.Package,preparedPath,cancellation);
            Current(displayed.Stamp);_review=null;_bytes=null;return result;
        }catch{if(existing)_grants[displayed.Package]=previousEpoch;else _grants.Remove(displayed.Package);throw;}finally{_preparing=null;_busy=false;}
    }
    public void RequirePrepared(UiPackageReference reference){Verify();_store.RequirePinned(reference);}
    public void Revoke(){Verify();_grants.Clear();_review=null;_bytes=null;}
    public void Dispose(){if(_disposed)return;Verify();Revoke();_store.Dispose();_disposed=true;}
}
