#if NCMA_EXTENSIONS
using Ncma.Editor.App;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Runtime;

internal static class ExtensionCommandTests
{
    private static void Check(bool value){if(!value)throw new Exception("Extension command assertion");}
    private static void Reject(Action action){try{action();}catch(InvalidOperationException){return;}throw new Exception("Expected extension command rejection");}
    internal static IEnumerable<(string,Action)> Cases()
    {
        yield return("M8.5-C2 original composition exact component set default metadata no duplicate registry",()=>{
            var registry=EditorTrustedExtensions.BuildComponents();
            var original=CompositionRegistry.Build(global::NcmaEngine.Generated.ComponentModule.Module);
            Check(registry.Describe().Select(x=>(x.TypeId,x.Version)).SequenceEqual(original.Describe().Select(x=>(x.TypeId,x.Version))));
            using var owner=new EditorSessionOwner("Extension registry",components:registry);
            Check(ReferenceEquals(owner.Document.Components,registry));
        });
        yield return("M8.5-C2 explicit original batch read-only prepare one commit undo redo",()=>{
            using var owner=new EditorSessionOwner("Extension original history");var workspace=new EditorWorkspace(owner);
            Guid id=workspace.CreateObject(workspace.Stamp);using var host=new EditorTrustedExtensions(workspace);host.Start();
            byte[] before=owner.Document.CaptureBytes();int history=owner.Edit!.State.UndoCount;
            var review=host.Commands.PrepareAddTransform();
            Check(before.AsSpan().SequenceEqual(owner.Document.CaptureBytes())&&owner.Edit.State.UndoCount==history);
            Check(review.Commands.Contains("set_component",StringComparison.Ordinal)&&review.Changes.Contains("before",StringComparison.Ordinal));
            Reject(()=>host.Commands.Confirm(review,"wrong"));Check(host.Commands.IsCurrent(review));
            var denied=owner.Edit.ApplyOriginalBatch(review.Batch,Guid.NewGuid(),CapabilityPermissions.ReadOnly);
            Check(denied.Status=="denied"&&!owner.Document.HasComponent<TransformData>(id));
            var result=host.Commands.Confirm(review,review.Fingerprint);Check(result.Status=="ok"&&owner.Edit.State.UndoCount==history+1);
            Check(owner.Document.GetComponent<TransformData>(id)==TransformData.Identity);Reject(()=>host.Commands.Confirm(review,review.Fingerprint));
            workspace.History(workspace.Stamp,false);Check(before.AsSpan().SequenceEqual(owner.Document.CaptureBytes()));
            workspace.History(workspace.Stamp,true);Check(owner.Document.HasComponent<TransformData>(id));Reject(()=>host.Commands.PrepareAddTransform());
        });
        yield return("M8.5-C2 latest ticket selection freeze cancel close and foreign group reject",()=>{
            using var owner=new EditorSessionOwner("Extension ticket");var workspace=new EditorWorkspace(owner);Guid id=workspace.CreateObject(workspace.Stamp);
            using var host=new EditorTrustedExtensions(workspace);host.Start();using var other=new EditorTrustedExtensions(workspace);other.Start();
            using var foreignOwner=new EditorSessionOwner("Foreign editor");
            Reject(()=>new EditorPresenter(new EditorWorkspace(foreignOwner),null).AttachTrustedExtensions(host));
            var old=host.Commands.PrepareAddTransform();var latest=host.Commands.PrepareAddTransform();Reject(()=>host.Commands.Confirm(old,old.Fingerprint));
            Reject(()=>other.Commands.Confirm(latest,latest.Fingerprint));owner.Edit!.Select(null);Check(!host.Commands.IsCurrent(latest));
            owner.Edit.Select(id);owner.Edit.SetFrozen(true);Reject(()=>host.Commands.PrepareAddTransform());owner.Edit.SetFrozen(false);
            latest=host.Commands.PrepareAddTransform();host.Commands.Cancel();Reject(()=>host.Commands.Confirm(latest,latest.Fingerprint));
            latest=host.Commands.PrepareAddTransform();host.Dispose();Reject(()=>host.Commands.Confirm(latest,latest.Fingerprint));
            Check(!owner.Document.HasComponent<TransformData>(id));
        });
        yield return("M8.5-C2 swallowed validator host close reentry rejects preparation and commit",()=>{
            EditorTrustedExtensions? host=null;bool attack=false;
            using var owner=new EditorSessionOwner("Extension reentry",validateComposition:_=>{if(attack){try{host!.Dispose();}catch(InvalidOperationException){}}});
            var workspace=new EditorWorkspace(owner);Guid id=workspace.CreateObject(workspace.Stamp);host=new(workspace);host.Start();
            try{
                attack=true;Reject(()=>host.Commands.PrepareAddTransform());attack=false;
                var review=host.Commands.PrepareAddTransform();byte[] before=owner.Document.CaptureBytes();int history=owner.Edit!.State.UndoCount;
                attack=true;var result=host.Commands.Confirm(review,review.Fingerprint);attack=false;
                Check(result.Status!="ok"&&history==owner.Edit.State.UndoCount&&before.AsSpan().SequenceEqual(owner.Document.CaptureBytes()));
                Reject(()=>host.Commands.Confirm(review,review.Fingerprint));Check(!owner.Document.HasComponent<TransformData>(id));
            }finally{attack=false;host.Dispose();}
        });
        yield return("M8.5-C2 presenter requires every original command and diff page plus explicit confirmation",()=>{
            using var owner=new EditorSessionOwner("Extension UI command");var workspace=new EditorWorkspace(owner);Guid id=workspace.CreateObject(workspace.Stamp);
            using var host=new EditorTrustedExtensions(workspace);host.Start();var view=new EditorPresenter(workspace,null,workspaceStyle:true);view.AttachTrustedExtensions(host);
            ulong frame=0;void Build()=>view.Build(++frame,1280,720);
            GuiEvent E(ulong high,ulong low,double value=0){var item=view.Items.ToArray().Single(i=>i.WidgetHigh==high&&i.WidgetLow==low);return new(){Kind=item.Kind,WidgetHigh=high,WidgetLow=low,Phase=3,Value=value,Frame=view.Frame.Frame,ViewGeneration=view.Frame.ViewGeneration,DocumentGeneration=view.Frame.DocumentGeneration,Revision=view.Frame.Revision};}
            bool Enabled(ulong low)=>view.Items.ToArray().Single(i=>i.WidgetHigh==84&&i.WidgetLow==low).Enabled!=0;
            Build();view.Apply([E(24,12)],[]);Build();view.Apply([E(24,76)],[]);Build();view.Apply([E(84,3)],[]);Build();
            Check(!Enabled(8));view.Apply([E(84,8)],[]);Check(!owner.Document.HasComponent<TransformData>(id));
            int pages=0;while(true){Check(++pages<160);view.Apply([E(84,6,1)],[]);Build();if(!Enabled(5))break;view.Apply([E(84,5)],[]);Build();}
            Check(pages>=2&&!Enabled(8));view.Apply([E(84,7,1)],[]);Build();Check(Enabled(8));var apply=E(84,8);view.Apply([apply],[]);Build();
            Check(owner.Document.HasComponent<TransformData>(id));int history=owner.Edit!.State.UndoCount;view.Apply([apply],[]);Check(owner.Edit.State.UndoCount==history);
        });
    }
}
#endif
