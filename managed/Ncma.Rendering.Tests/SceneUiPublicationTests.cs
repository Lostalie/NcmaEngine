using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Application;
using Ncma.Assets;
using Ncma.Editor.App;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Player.App;
using Ncma.Rendering;
using Ncma.Scene;
using Ncma.Scene.Rendering;
using Ncma.Ui;
using Ncma.Ui.Rendering;

internal static unsafe partial class Program
{
    private static int SceneUiPublicationTests(string plugins,string output,bool relocated=false)
    {

        using var playerRunOwner0 = new Ncma.Player.App.PlayerRunner();
        using var playerRunOwner1 = new Ncma.Player.App.PlayerRunner();
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        var repo=new DirectoryInfo(Path.GetFullPath(plugins));while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent!;
        int checks=0;void Pass(bool value,string name){Check(value,"UI publication "+name);checks++;}
        var fixture=new SceneGpuFixture(output);var(doc,camera,_,_)=fixture.Scene();Guid target=doc.World.GetUuid(doc.World.CreateEntity("Scene HUD"));
        string runtime=Path.Combine(output,"runtime");Directory.CreateDirectory(Path.Combine(runtime,"assets"));
        File.WriteAllBytes(Path.Combine(runtime,"assets/scene.ncpak"),SceneAssetPreparation.CreateRuntimePackage(fixture.Root,fixture.Project,doc.CaptureSnapshot(),[]));
        var ui=UiDefinition.Create(Guid.NewGuid(),"Published HUD");var rect=UiElement.Create(Guid.NewGuid(),"Color",UiKind.Rectangle,ui.Root)with{Layout=UiLayout.Fixed(8,8,64,40),Style=UiStyle.Default with{Fill=new(0,0,1,1)}};
        ui=ui with{Elements=[ui.Elements[0],rect]};byte[] blue=RuntimeUiPackage.Encode(fixture.Project,1,ui,[],[]);File.WriteAllBytes(Path.Combine(runtime,"assets/default.ncupak"),blue);
        var baseline=new UiPackageReference(1,ui.AssetId,1,"assets/default.ncupak",Convert.ToHexString(SHA256.HashData(blue)));
        ui=ui with{Elements=[ui.Elements[0],rect with{Style=rect.Style with{Fill=new(1,0,0,1)}}]};File.WriteAllBytes(Path.Combine(runtime,"assets/hud.ncmaui"),UiCodec.Encode(ui));
        SceneDocumentFiles.Save(doc,Path.Combine(runtime,"start.ncmascene"));string script=Path.Combine(repo.FullName,"out/managed/Ncma.Gameplay.Sample.dll");File.Copy(script,Path.Combine(runtime,"gameplay.dll"));File.Copy(Path.ChangeExtension(script,".deps.json"),Path.Combine(runtime,"gameplay.deps.json"));
        var config=new ProjectConfiguration(1,fixture.Project,"UI publication","start.ncmascene","gameplay.dll","Direct3D11",[],false,camera,"assets/scene.ncpak",UiPackage:baseline);
        string project=Path.Combine(runtime,"project.ncmaproject");File.WriteAllBytes(project,JsonSerializer.SerializeToUtf8Bytes(config,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        UiPackageReference published;
        using(var editor=new CandidatePresentation(plugins,ProjectContext.Load(project),true,false,false)){
            editor.Start();var owner=editor.VerificationEditor;var workspace=new EditorWorkspace(owner);
            var author=EnvironmentProductEvidence.Field<EditorUiWorkspace>(editor,"_uiWorkspace");var prepare=EnvironmentProductEvidence.Field<EditorUiPackagePreparation>(editor,"_uiPackagePreparation");var publication=EnvironmentProductEvidence.Field<EditorUiPublication>(editor,"_uiPublication");
            ulong frame=0;uint width=0;byte[] Draw(){byte[]? image=null;editor.CaptureNextCompositeViewportForVerification((p,w,h)=>{image=p;width=w;});editor.PumpPlatform();var f=new FrameContext(++frame,0);editor.ApplyEditorIntents(f);editor.PumpAgent(f);editor.BeginPresentation(f);editor.SubmitInput(f);editor.AdvancePlay(f);editor.Present(f);return image!;}
            Draw();byte[] original=Draw();int Pixel()=>checked(((int)(20*width/editor.VerificationPresenter.Viewport.Width)*(int)width+(int)(20*width/editor.VerificationPresenter.Viewport.Width))*4);
            int at=Pixel();Pass(original[at]<2&&original[at+2]>253,"project default blue visible");
            var file=author.ReviewFile(workspace.Stamp,"assets/hud.ncmaui",false);author.ConfirmFile(file.Review,file.Document,true);var review=prepare.Review(author.Stamp,2);published=prepare.Approve(review,review.Fingerprint,true);
            Pass(Draw().SequenceEqual(original),"preparation alone never changes HUD");int history=owner.Edit!.State.UndoCount;
            void Apply(UiPackageReference? p){var r=publication.Review(workspace.Stamp,target,p);publication.ApplyLocal(publication.Approve(r,r.Fingerprint,true));}
            Apply(published);byte[] red=Draw();at=Pixel();Pass(red[at]>253&&red[at+2]<2,"new prepared scene generation reaches final GUI");Pass(owner.Edit.State.UndoCount==history+1,"one history entry");
            workspace.History(workspace.Stamp,false);Pass(Draw().SequenceEqual(original),"Undo restores project default");workspace.History(workspace.Stamp,true);Pass(Draw().SequenceEqual(red),"Redo restores published generation");
            var retainedState=owner.Edit.State;var retainedWorld=owner.Document.World;
            editor.RebuildGraphics(editor.ReviewGraphicsRecovery());Draw();
            Pass(Draw().SequenceEqual(red)&&ReferenceEquals(retainedWorld,owner.Document.World)&&owner.Edit.State==retainedState,"same-session graphics rebuild preserves published HUD pixels and sole history");
            Pass(ReferenceEquals(author,EnvironmentProductEvidence.Field<EditorUiWorkspace>(editor,"_uiWorkspace"))&&ReferenceEquals(publication,EnvironmentProductEvidence.Field<EditorUiPublication>(editor,"_uiPublication"))&&ReferenceEquals(prepare,EnvironmentProductEvidence.Field<EditorUiPackagePreparation>(editor,"_uiPackagePreparation")),"graphics rebuild preserves exact author/preparation/publication participants");
            workspace.History(workspace.Stamp,false);Pass(Draw().SequenceEqual(original),"original publication Undo works after GPU rebuild");workspace.History(workspace.Stamp,true);Pass(Draw().SequenceEqual(red),"original publication Redo works after GPU rebuild");
            Apply(null);byte[] off=Draw();Pass(!off.SequenceEqual(red)&&!off.SequenceEqual(original),"explicit Off suppresses project default");workspace.History(workspace.Stamp,false);Pass(Draw().SequenceEqual(red),"Undo Off restores exact red image");
            var renderer=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");ulong uploaded=renderer.UiStats.UploadedBytes,productions=renderer.UiTargetStats.Productions;
            for(int i=0;i<16;i++)Draw();Pass(renderer.UiStats.UploadedBytes==uploaded&&renderer.UiTargetStats.Productions==productions&&renderer.UiTargetStats.Leases==0,"unchanged16 frames cache no upload/production/leases");
            // Deliberately corrupt only through an exact scoped original command. The host must
            // not label a retained old image as this failed generation; Undo is the recovery.
            var bad=SceneUiData.From(published with{Path="assets/missing.ncupak"});var edit=owner.Edit;
            var request=new CapabilityRequest(EditSession.ContractVersion,Guid.NewGuid(),edit.SessionId,edit.Revision,"ncma.scene.transaction",JsonSerializer.SerializeToElement(new{operations=new[]{new{op="set_component",objectId=target,typeId=SceneUiData.TypeId,version=1,data=owner.Document.World.Components.Encode(bad)}}}));
            Pass(edit.Invoke(request,new(["ncma.scene.transaction"],objectScope:[target],componentScope:[SceneUiData.TypeId])).Changed,"scoped malformed dependency fixture");Pass(Draw().SequenceEqual(off),"failed new dependency clears old HUD");
            workspace.History(workspace.Stamp,false);Pass(Draw().SequenceEqual(red),"Undo invalid reference restores current HUD");
            owner.RefreshAssets(true);owner.Assets?.PrepareStoredGenerationsAsync().GetAwaiter().GetResult();
            for(int i=0;i<3;i++){owner.StartPlay();Pass(Draw().SequenceEqual(red),"Play selected generation");owner.StopPlay();Pass(Draw().SequenceEqual(red),"Stop selected generation");}
            SceneDocumentFiles.Save(owner.Document,Path.Combine(runtime,"start.ncmascene"));File.WriteAllBytes(Path.Combine(output,"published-editor.rgba"),red);
            Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"Editor API0/0");
        }
        using var sourceLock=new FileStream(Path.Combine(runtime,"assets/hud.ncmaui"),FileMode.Open,FileAccess.Read,FileShare.None);
        var options=new PlayerOptions(project,false,"d3d11",2,1d/60,30,Path.Combine(output,"player.json"),false,false);byte[]? pixels=null;uint playerWidth=0;
        var player=playerRunOwner0.RunForVerification(options,plugins,(p,w,h)=>{pixels=p;playerWidth=w;});int index=(int)((20*playerWidth+20)*4);
        Pass(player.ExitCode==0&&pixels is not null&&pixels[index]>253&&pixels[index+2]<2&&player.ValidationErrors==0&&player.ValidationWarnings==0,"Player reads saved scene override with author source locked");
        var headless=playerRunOwner1.Run(options with{Headless=true,Renderer="null",Report=Path.Combine(output,"null.json")},pluginRoot:"unused",visible:false);
        Pass(headless.ExitCode==0&&headless.Tick==2&&headless.Modules.Length==0,"Null validates saved selected closure without native");
        using(var editor=new CandidatePresentation(plugins,ProjectContext.Load(project),true,false,false)){editor.Start();Pass(EnvironmentProductEvidence.Field<RuntimeUiPackage>(editor,"_sceneHudPackage").Generation==published.Generation,"Editor reopens saved scene-selected generation");}
        if(relocated)SceneUiPublicationProducts(repo.FullName,runtime,output,Pass);
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{checks,wholeD3Accepted=false,formalToolbar=false,relocated}));Console.WriteLine($"PASS UI publication hosts: {checks}; {output}");return 0;
    }
}
