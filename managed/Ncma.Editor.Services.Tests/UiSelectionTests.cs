using System.Text.Json;
using Ncma.Assets;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Scene;
using Ncma.Scene.Rendering;
using Ncma.Ui;
using Ncma.Editor.App;
using Ncma.Gui;
using System.Text;
using System.Text.RegularExpressions;

internal static unsafe class UiSelectionTests
{
    private static void Check(bool value){if(!value)throw new Exception("UI selection assertion");}
    private static void Reject(Action action){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or EditRejectedException or EditCommandRejectedException or IOException){return;}throw new Exception("UI selection unexpectedly accepted");}
    private sealed class Fixture:IDisposable
    {
        internal readonly string Root;internal readonly Guid Project=Guid.NewGuid();internal readonly EditorSessionOwner Owner;internal readonly EditorWorkspace Workspace;
        internal readonly EditorUiWorkspace Ui;internal readonly EditorUiPackagePreparation Prepared;internal readonly EditorUiPublication Publication;internal readonly Guid Target;
        internal Fixture(string output){Root=Path.Combine(output,"ui-selection-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(Root,"assets"));
            Owner=new("UI selection",components:global::Ncma.Runtime.CompositionRegistry.Build(global::Ncma.Scene.Rendering.Generated.ComponentModule.Module),validateComposition:SceneRenderValidation.RequireComposition);Workspace=new(Owner);Ui=new(Workspace,Root);Owner.ConfigureAssets(Root,Project,1,uiScope:Ui.Scope);
            Prepared=new(Ui,Root,Project);Publication=new(Workspace,Ui,Prepared,Root,Project);Target=Workspace.CreateObject(Workspace.Stamp);
            var d=UiDefinition.Create(Guid.NewGuid(),"HUD");File.WriteAllBytes(Path.Combine(Root,"assets/ui.ncmaui"),UiCodec.Encode(d));var review=Ui.ReviewFile(Workspace.Stamp,"assets/ui.ncmaui",false);Ui.ConfirmFile(review.Review,review.Document,true);}
        internal UiPackageReference Package(ulong gen){var r=Prepared.Review(Ui.Stamp,gen);return Prepared.Approve(r,r.Fingerprint,true);}
        internal Guid Proposal(UiPackageReference? package){var r=Publication.Review(Workspace.Stamp,Target,package);return Publication.Approve(r,r.Fingerprint,true);}
        internal void Apply(UiPackageReference? package)=>Publication.ApplyLocal(Proposal(package));
        internal SceneUiData? Value=>SceneUiState.Read(Owner.Document.CaptureSnapshot());
        public void Dispose(){Publication.Dispose();Prepared.Dispose();Ui.Dispose();Owner.Dispose();}
    }
    internal static IEnumerable<(string,Action)> Cases(string output)
    {
        yield return("UI publication scene replacement revokes old review but fresh approval remains usable",()=>{
            using var f=new Fixture(output);var package=f.Package(1);var old=f.Publication.Review(f.Workspace.Stamp,f.Target,package);
            f.Workspace.New(f.Workspace.Stamp,true);f.Ui.Synchronize();Reject(()=>f.Publication.Approve(old,old.Fingerprint,true));
            Guid target=f.Workspace.CreateObject(f.Workspace.Stamp);var file=f.Ui.ReviewFile(f.Workspace.Stamp,"assets/ui.ncmaui",false);f.Ui.ConfirmFile(file.Review,file.Document,true);
            Reject(()=>f.Prepared.RequirePrepared(package));var current=f.Package(2);var review=f.Publication.Review(f.Workspace.Stamp,target,current);f.Publication.ApplyLocal(f.Publication.Approve(review,review.Fingerprint,true));
            Check(f.Value==SceneUiData.From(current));f.Workspace.History(f.Workspace.Stamp,false);Check(f.Value is null);
        });
        yield return("UI publication formal presenter review, exact confirmation, original history and stale clicks",()=>{
            using var f=new Fixture(output);f.Workspace.Select(f.Workspace.Stamp,f.Target);
            var p=new EditorPresenter(f.Workspace,null,f.Root,workspaceStyle:true);p.AttachUi(f.Ui,null);p.AttachUiPublication(f.Prepared,f.Publication);ulong frame=0;
            void Build(){p.SynchronizeUi();p.Build(++frame,1280,720);}
            GuiItem Item(ulong high,ulong low)=>p.Items.ToArray().Single(i=>i.WidgetHigh==high&&i.WidgetLow==low);
            GuiEvent Event(ulong high,ulong low,int bytes=0)=>new(){Kind=Item(high,low).Kind,Phase=3,WidgetHigh=high,WidgetLow=low,Frame=p.Frame.Frame,ViewGeneration=p.Frame.ViewGeneration,DocumentGeneration=p.Frame.DocumentGeneration,Revision=p.Frame.Revision,TextLength=(uint)bytes};
            void Click(ulong low,string text="",ulong high=31){Build();byte[] bytes=Encoding.UTF8.GetBytes(text);p.Apply([Event(high,low,bytes.Length)],bytes);}
            string Fingerprint(){Build();return Regex.Match(Encoding.UTF8.GetString(p.Text),"审阅指纹 ([A-F0-9]{64})").Groups[1].Value;}
            Click(15,high:24);Click(46,high:24);Check(p.UiWorkspaceActive);
            int history=f.Owner.Edit!.State.UndoCount;
            Click(601);Check(Fingerprint().Length==64);Click(603);Check(f.Value is null&&f.Owner.Edit.State.UndoCount==history); // disabled without explicit confirmation
            Click(602,Fingerprint());Click(603);Check(f.Value is null&&f.Owner.Edit.State.UndoCount==history);
            Click(604);string fingerprint=Fingerprint();Check(fingerprint.Length==64);Click(602,"wrong");Click(606);Check(f.Value is null);
            Click(602,fingerprint);Build();var old=Event(31,606);f.Workspace.Select(f.Workspace.Stamp,null);p.Apply([old],[]);Check(f.Value is null);
            f.Workspace.Select(f.Workspace.Stamp,f.Target);Click(604);Click(602,Fingerprint());Click(606);Check(f.Value?.Enabled==true&&f.Owner.Edit.State.UndoCount==history+1);
            f.Workspace.History(f.Workspace.Stamp,false);Check(f.Value is null);f.Workspace.History(f.Workspace.Stamp,true);Check(f.Value?.Enabled==true);
            Click(605);Click(602,Fingerprint());Click(606);Check(f.Value==SceneUiData.Off);
            f.Owner.Edit.SetFrozen(true);Build();Check(Item(31,601).Enabled==0&&Item(31,604).Enabled==0&&Item(31,605).Enabled==0);Click(605);Check(f.Value==SceneUiData.Off);f.Owner.Edit.SetFrozen(false);
            Click(600,"2");Click(601);fingerprint=Fingerprint();Click(602,fingerprint);Build();var stale=Event(31,603);f.Ui.Revoke();p.Apply([stale],[]);Check(f.Value==SceneUiData.Off);
        });
        yield return("UI selection defaults, explicit Off and strict scene cardinality",()=>{
            var doc=new SceneDocument("UI",global::Ncma.Runtime.CompositionRegistry.Build(global::Ncma.Scene.Rendering.Generated.ComponentModule.Module),SceneRenderValidation.RequireComposition);var p=new UiPackageReference(1,Guid.NewGuid(),1,"assets/a.ncupak",new('A',64));
            Check(SceneUiState.Resolve(doc.CaptureSnapshot(),p)==p);Guid obj=doc.CreateObject("UI");doc.SetComponent(obj, SceneUiData.Off);Check(SceneUiState.Resolve(doc.CaptureSnapshot(),p)==null);
            doc.SetComponent(obj, SceneUiData.From(p));Check(SceneUiState.Resolve(doc.CaptureSnapshot(),null)==p);byte[] bytes=doc.CaptureBytes();var restored=new SceneDocument("UI",global::Ncma.Runtime.CompositionRegistry.Build(global::Ncma.Scene.Rendering.Generated.ComponentModule.Module),SceneRenderValidation.RequireComposition);restored.RestoreBytes(bytes);Check(SceneUiState.Read(restored.CaptureSnapshot())==SceneUiData.From(p));
            Reject(()=>SceneUiData.Validate(SceneUiData.Off with{Generation=1}));Reject(()=>SceneUiData.From(p with{Path="../x.ncupak"}));
            doc.SetComponent(doc.CreateObject("duplicate"), SceneUiData.Off);Reject(()=>SceneUiState.Read(doc.CaptureSnapshot()));
        });
        yield return("UI selection one original history entry, Undo Redo and saved scene reference",()=>{
            using var f=new Fixture(output);var a=f.Package(1);int before=f.Owner.Edit!.State.UndoCount;var token=f.Proposal(a);var request=f.Publication.Request(token);
            Check(f.Owner.Edit.Invoke(request).Status=="denied"&&f.Value==null);
            f.Publication.ApplyLocal(token);Check(f.Value==SceneUiData.From(a)&&f.Owner.Edit.State.UndoCount==before+1);
            f.Workspace.History(f.Workspace.Stamp,false);Check(f.Value==null&&File.Exists(Path.Combine(f.Root,a.Path)));
            f.Workspace.History(f.Workspace.Stamp,true);Check(f.Value==SceneUiData.From(a));
            var b=f.Package(2);f.Apply(b);Check(f.Value==SceneUiData.From(b));f.Workspace.History(f.Workspace.Stamp,false);Check(f.Value==SceneUiData.From(a));f.Workspace.History(f.Workspace.Stamp,true);Check(f.Value==SceneUiData.From(b));
            f.Apply(null);Check(f.Value==SceneUiData.Off);f.Workspace.History(f.Workspace.Stamp,false);Check(f.Value==SceneUiData.From(b));
            string file=Path.Combine(f.Root,"saved.ncmascene");SceneDocumentFiles.Save(f.Owner.Document,file);var loaded=new SceneDocument("loaded",global::Ncma.Runtime.CompositionRegistry.Build(global::Ncma.Scene.Rendering.Generated.ComponentModule.Module),SceneRenderValidation.RequireComposition);SceneDocumentFiles.Load(loaded,file);Check(SceneUiState.Resolve(loaded.CaptureSnapshot(),a)==b);
        });
        yield return("UI selection scopes, stale review, forged input and revoke recheck history",()=>{
            using var f=new Fixture(output);var a=f.Package(1);var review=f.Publication.Review(f.Workspace.Stamp,f.Target,a);
            Reject(()=>f.Publication.Approve(review,review.Fingerprint,false));Reject(()=>f.Publication.Approve(review with{Target=Guid.NewGuid()},review.Fingerprint,true));
            var token=f.Publication.Approve(review,review.Fingerprint,true);var request=f.Publication.Request(token);
            var result=f.Owner.Edit!.Invoke(request,new([EditorUiPublication.Capability],objectScope:[Guid.NewGuid()],componentScope:[SceneUiData.TypeId]));Check(!result.Changed&&f.Value==null);
            Reject(()=>f.Publication.SceneOperations(JsonSerializer.SerializeToElement(new{proposalId=token,arbitrary=true})));
            f.Publication.ApplyLocal(token);var before=f.Owner.Document.CaptureBytes();f.Prepared.Revoke();Reject(()=>f.Workspace.History(f.Workspace.Stamp,false));Check(f.Owner.Document.CaptureBytes().SequenceEqual(before));
        });
        yield return("UI selection missing preparation, another target, stale scene and owner reject",()=>{
            using var f=new Fixture(output);Reject(()=>f.Publication.Review(f.Workspace.Stamp,f.Target,new(1,f.Ui.DocumentId,1,"assets/missing.ncupak",new('A',64))));
            var a=f.Package(1);var review=f.Publication.Review(f.Workspace.Stamp,f.Target,a);f.Workspace.CreateObject(f.Workspace.Stamp);Reject(()=>f.Publication.Approve(review,review.Fingerprint,true));f.Apply(a);
            Guid other=f.Owner.Document.GetObjectIds().Single(id=>id!=f.Target);Reject(()=>f.Publication.Review(f.Workspace.Stamp,other,a));Task.Run(()=>Reject(()=>f.Publication.Review(f.Workspace.Stamp,f.Target,a))).GetAwaiter().GetResult();
        });
    }
}
