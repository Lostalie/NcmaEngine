using System.Security.Cryptography;
using Ncma.Assets;
using Ncma.Assets.Authoring;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Ui;
using Ncma.Ui.Rendering;

internal static class UiPackagePreparationTests
{
    private static void Check(bool value){if(!value)throw new Exception("UI package preparation assertion");}
    private static void Reject(Action action){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or IOException or EditCommandRejectedException or EditRejectedException or OperationCanceledException or NotSupportedException){return;}throw new Exception("UI preparation unexpectedly accepted");}
    private sealed class Time:TimeProvider{private long _time;public override long TimestampFrequency=>1000;public override long GetTimestamp()=>_time;internal void Expire()=>_time+=61000;}
    private sealed class Fixture:IDisposable
    {
        internal readonly string Root;internal readonly Guid Project=Guid.NewGuid();internal readonly EditorSessionOwner Owner;
        internal readonly EditorWorkspace Workspace;internal readonly EditorUiWorkspace Ui;internal readonly EditorUiPackagePreparation Preparation;internal readonly Time Clock=new();
        internal Fixture(string output){Root=Path.Combine(output,"ui-package-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(Root,"assets"));
            Owner=new("UI package author");Workspace=new(Owner);Ui=new(Workspace,Root);Owner.ConfigureAssets(Root,Project,1,uiScope:Ui.Scope);
            var d=UiDefinition.Create(Guid.NewGuid(),"HUD");File.WriteAllBytes(Path.Combine(Root,"assets/ui.ncmaui"),UiCodec.Encode(d));
            var r=Ui.ReviewFile(Workspace.Stamp,"assets/ui.ncmaui",false);Ui.ConfirmFile(r.Review,r.Document,true);Preparation=new(Ui,Root,Project,Clock);}
        internal UiPackageReview Review(ulong generation=1)=>Preparation.Review(Ui.Stamp,generation);
        internal UiPackageReference Approve(UiPackageReview r)=>Preparation.Approve(r,r.Fingerprint,true);
        public void Dispose(){Preparation.Dispose();Ui.Dispose();Owner.Dispose();}
    }
    private static void Validate(ReadOnlySpan<byte> bytes,Guid project,UiPackageReference r){using var package=RuntimeUiPackage.Decode(bytes,project,r.DocumentId,r.Generation,r.Hash);}
    internal static IEnumerable<(string,Action)> Cases(string output)
    {
        yield return("D3-C UI preparation approval is exact, immutable, CPU-only and not selection/history",()=>{
            using var f=new Fixture(output);byte[] scene=f.Owner.Document.CaptureBytes();int history=f.Owner.Edit!.State.UndoCount;
            Reject(()=>new EditorUiPackagePreparation(f.Ui,f.Root,Guid.NewGuid()).Dispose());Reject(()=>new EditorUiPackagePreparation(f.Ui,Path.Combine(f.Root,"assets"),f.Project).Dispose());
            var r=f.Review();Check(Directory.GetFiles(Path.Combine(f.Root,"assets"),"*.ncupak").Length==0);
            Reject(()=>f.Preparation.Approve(r,r.Fingerprint,false));Reject(()=>f.Preparation.Approve(r with{Project=Guid.NewGuid()},r.Fingerprint,true));Reject(()=>f.Preparation.Approve(r,"forged",true));
            var p=f.Approve(r);f.Preparation.RequirePrepared(p);using(var opened=RuntimeUiPackage.Open(f.Root,f.Project,p))Check(opened.DocumentId==r.Stamp.Document);
            Reject(()=>File.WriteAllBytes(Path.Combine(f.Root,p.Path),[0]));Reject(()=>File.Move(Path.Combine(f.Root,p.Path),Path.Combine(f.Root,"moved")));
            Check(f.Owner.Document.CaptureBytes().SequenceEqual(scene)&&f.Owner.Edit.State.UndoCount==history);Reject(()=>f.Approve(r));
            Check(!System.Diagnostics.Process.GetCurrentProcess().Modules.Cast<System.Diagnostics.ProcessModule>().Any(m=>m.ModuleName=="NcmaRenderer.dll"||m.ModuleName=="NcmaText.dll"));
        });
        yield return("D3-C UI review rejects stale resources, expiry, dirty author source and revoked scope",()=>{
            using var f=new Fixture(output);var r=f.Review();f.Clock.Expire();Reject(()=>f.Approve(r));r=f.Review();f.Ui.Resources.Revoke();Reject(()=>f.Approve(r));
            r=f.Review();var d=f.Ui.Capture()!;File.WriteAllBytes(Path.Combine(f.Root,"assets/ui.ncmaui"),UiCodec.Encode(d with{Name="external"}));Reject(()=>f.Approve(r));
            File.WriteAllBytes(Path.Combine(f.Root,"assets/ui.ncmaui"),UiCodec.Encode(d));f.Ui.Revoke();Reject(()=>f.Review());
        });
        yield return("D3-C generation immutable old/new coexist and duplicate generation conflicts",()=>{
            using var f=new Fixture(output);var a=f.Approve(f.Review());byte[] first=File.ReadAllBytes(Path.Combine(f.Root,a.Path));
            var b=f.Approve(f.Review(2));Check(a.Path!=b.Path&&first.SequenceEqual(File.ReadAllBytes(Path.Combine(f.Root,a.Path))));
            byte[] changed=RuntimeUiPackage.Encode(f.Project,1,f.Ui.Capture()! with{Name="changed"},[],[]);
            using var store=new UiPackageGenerationStore(new(f.Root),f.Project,_=>true,Validate);Reject(()=>store.Prepare(a.DocumentId,1,changed));
            f.Preparation.Revoke();Reject(()=>f.Preparation.RequirePrepared(a));Check(File.Exists(Path.Combine(f.Root,a.Path)));
        });
        yield return("D3-C generation rejects malformed package, unauthorized, cancelled and foreign-thread work",()=>{
            using var f=new Fixture(output);byte[] bytes=f.Ui.Resources.CookPackage(f.Project,1,f.Ui.Capture()!);Guid id=f.Ui.DocumentId;
            using(var denied=new UiPackageGenerationStore(new(f.Root),f.Project,_=>false,Validate))Reject(()=>denied.Prepare(id,1,bytes));
            using var store=new UiPackageGenerationStore(new(f.Root),f.Project,_=>true,Validate);
            Reject(()=>store.Prepare(id,1,new byte[52]));Reject(()=>store.Prepare(id,1,bytes,new CancellationToken(true)));
            Task.Run(()=>Reject(()=>store.Prepare(id,1,bytes))).GetAwaiter().GetResult();Check(Directory.GetFiles(Path.Combine(f.Root,"assets"),"hudgen-*").Length==0);
        });
        yield return("D3-C failed generation write retained and exact approved recovery",()=>{
            using var f=new Fixture(output);byte[] bytes=f.Ui.Resources.CookPackage(f.Project,1,f.Ui.Capture()!);Guid id=f.Ui.DocumentId;
            var r=UiPackageGenerationStore.Reference(f.Project,id,1,Convert.ToHexString(SHA256.HashData(bytes)));bool allowed=true;
            using var store=new UiPackageGenerationStore(new(f.Root),f.Project,_=>allowed,Validate);
            Reject(()=>store.Prepare(id,1,bytes,fault:phase=>{if(phase=="ui_generation_written")throw new IOException("fixture");}));
            string stage=Path.GetRelativePath(f.Root,Directory.GetFiles(Path.Combine(f.Root,"assets"),"*.prepared").Single()).Replace('\\','/');
            Check(!File.Exists(Path.Combine(f.Root,r.Path)));Reject(()=>store.Prepare(id,1,bytes));allowed=false;Reject(()=>store.RecoverPrepared(r,stage));allowed=true;
            Reject(()=>store.RecoverPrepared(r,stage+".wrong"));Check(store.RecoverPrepared(r,stage)==r);store.RequirePinned(r);Check(!File.Exists(Path.Combine(f.Root,stage)));
        });
        yield return("D3-C post-rename failure retains verified generation for explicit retry",()=>{
            using var f=new Fixture(output);byte[] bytes=f.Ui.Resources.CookPackage(f.Project,1,f.Ui.Capture()!);Guid id=f.Ui.DocumentId;
            using var store=new UiPackageGenerationStore(new(f.Root),f.Project,_=>true,Validate);
            Reject(()=>store.Prepare(id,1,bytes,fault:phase=>{if(phase=="ui_generation_published")throw new IOException("fixture");}));
            var r=store.Prepare(id,1,bytes);store.RequirePinned(r);Check(Directory.GetFiles(Path.Combine(f.Root,"assets"),"*.ncupak").Length==1);
        });
        yield return("D3-C semantic validation cannot reenter generation owner or bypass revoked final grant",()=>{
            using var f=new Fixture(output);byte[] bytes=f.Ui.Resources.CookPackage(f.Project,1,f.Ui.Capture()!);Guid id=f.Ui.DocumentId;
            UiPackageGenerationStore? store=null;bool allowed=true;
            store=new(new(f.Root),f.Project,_=>allowed,(b,p,r)=>{Reject(()=>store!.Prepare(id,1,bytes));Validate(b,p,r);allowed=false;});
            using(store)Reject(()=>store.Prepare(id,1,bytes));Check(Directory.GetFiles(Path.Combine(f.Root,"assets"),"hudgen-*").Length==0);
        });
        yield return("D3-C generation count is bounded without deleting retained user data",()=>{
            using var f=new Fixture(output);using var store=new UiPackageGenerationStore(new(f.Root),f.Project,_=>true,Validate);var d=f.Ui.Capture()!;
            for(ulong n=1;n<=32;n++)store.Prepare(d.AssetId,n,RuntimeUiPackage.Encode(f.Project,n,d,[],[]));
            Reject(()=>store.Prepare(d.AssetId,33,RuntimeUiPackage.Encode(f.Project,33,d,[],[])));Check(Directory.GetFiles(Path.Combine(f.Root,"assets"),"*.ncupak").Length==32);
        });
        yield return("D3-C local UI recovery requires a fresh exact human review and preserves sole history",()=>{
            using var f=new Fixture(output);var r=f.Review();int undo=f.Owner.Edit!.State.UndoCount;
            Reject(()=>f.Preparation.Approve(r,r.Fingerprint,true,fault:_=>throw new IOException("retained UI stage")));
            string stage=Path.GetRelativePath(f.Root,Directory.GetFiles(Path.Combine(f.Root,"assets"),"*.prepared").Single()).Replace('\\','/');
            f.Clock.Expire();Reject(()=>f.Preparation.RecoverPrepared(r,r.Fingerprint,true,stage));r=f.Review();
            Reject(()=>f.Preparation.RecoverPrepared(r,r.Fingerprint,false,stage));
            var restored=f.Preparation.RecoverPrepared(r,r.Fingerprint,true,stage);f.Preparation.RequirePrepared(restored);Check(f.Owner.Edit.State.UndoCount==undo);
        });
        yield return("D3-C expired approval at write boundary cannot publish and resource exports are copied",()=>{
            using var f=new Fixture(output);var r=f.Review();Reject(()=>f.Preparation.Approve(r,r.Fingerprint,true,fault:_=>f.Clock.Expire()));
            Check(!File.Exists(Path.Combine(f.Root,r.Package.Path)));Check(Directory.GetFiles(Path.Combine(f.Root,"assets"),"*.prepared").Length==1);
            byte[] fontBytes=new byte[12];var font=new Ncma.Text.FontAsset(Guid.NewGuid(),fontBytes,"test",true);var copied=font.CopyBytes();copied[0]=99;Check(font.CopyBytes()[0]==0);
            var image=new UiImageAsset(Guid.NewGuid(),1,1,[1,2,3,4]);var rgba=image.CopyRgba();rgba[0]=99;Check(image.CopyRgba()[0]==1);
        });
        yield return("D3-C prepared interaction requires the exact trusted runtime action kinds",()=>{
            using var f=new Fixture(output);var d=f.Ui.Capture()!;var button=UiElement.Create(Guid.NewGuid(),"Action",UiKind.Button,d.Root)with{Action="preview.test"};
            f.Ui.Edit(f.Ui.Stamp,[new(UiEditKind.Add,button.Id,button)]);Reject(()=>f.Review());
            using(var wrong=new EditorUiPackagePreparation(f.Ui,f.Root,f.Project,actions:new(new Ncma.Application.Runtime.SceneUiAction("preview.test",Ncma.GameplayUiActionKind.ValueChanged))))Reject(()=>wrong.Review(f.Ui.Stamp,1));
            using var prepared=new EditorUiPackagePreparation(f.Ui,f.Root,f.Project,actions:new(new Ncma.Application.Runtime.SceneUiAction("preview.test",Ncma.GameplayUiActionKind.Activate)));
            var review=prepared.Review(f.Ui.Stamp,1);var package=prepared.Approve(review,review.Fingerprint,true);prepared.RequirePrepared(package);
        });
    }
}
