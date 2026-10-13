using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Application;
using Ncma.Assets;
using Ncma.Editor.App;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Player.App;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    private static int PostHostTests(string plugins,string output,bool authoring=false)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);int cases=0;
        void Pass(bool ok,string label){Check(ok,"C2 formal post "+label);cases++;}
        var repo=new DirectoryInfo(Path.GetFullPath(plugins));while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent??throw new Exception("Repository missing");
        string deployed=Path.Combine(repo.FullName,"out/bin");var fixture=new SceneGpuFixture(output);var(document,camera,_,_)=fixture.Scene();
        var light=document.World.GetObjects().Single(o=>o.Has<DirectionalLightData>());light.Set(light.Get<DirectionalLightData>() with{CastShadow=false});
        var obj=document.World.CreateObject("Post configuration");var active=PostProcessData.Off with{Enabled=true,StackJson=PostProcessData.EncodeStack([PostProcessModules.Bloom(new(Levels:2,Threshold:0,Intensity:.8f))])};obj.Set(active);Guid settingId=obj.PersistentId;
        byte[] shaderIndex=File.ReadAllBytes(Path.Combine(deployed,RuntimeShaderFileSet.DefaultSelectionPath));var shaders=ShaderPackageSelection.Decode(shaderIndex);
        byte[] postIndex=File.ReadAllBytes(Path.Combine(deployed,PostProcessLibrarySelection.IndexPath));var post=PostProcessLibrarySelection.Decode(postIndex);
        foreach(string path in shaders.Packages.Select(p=>p.Path).Concat(post.Programs.Select(p=>p.Path)).Concat(post.Tones.Select(p=>p.Path)).Append(PostProcessLibrarySelection.IndexPath)){
            string destination=Path.Combine(fixture.Root,path);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(Path.Combine(deployed,path),destination);
        }
        string gameplay=Path.Combine(repo.FullName,"out/managed/Ncma.Gameplay.Sample.dll");File.Copy(gameplay,Path.Combine(fixture.Root,"gameplay.dll"));File.Copy(Path.ChangeExtension(gameplay,".deps.json"),Path.Combine(fixture.Root,"gameplay.deps.json"));
        string scenePath=Path.Combine(fixture.Root,"start.ncmascene"),projectPath=Path.Combine(fixture.Root,"project.ncmaproject");SceneDocumentFiles.Save(document,scenePath);
        var config=new ProjectConfiguration(1,fixture.Project,"Post hosts","start.ncmascene","gameplay.dll","Direct3D11",[],SceneCamera:camera,ShaderPackages:shaders,
            PostProcessLibrary:new(1,Convert.ToHexString(SHA256.HashData(postIndex))));var json=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
        void Project(ProjectConfiguration value)=>File.WriteAllBytes(projectPath,JsonSerializer.SerializeToUtf8Bytes(value,json));Project(config);
        PlayerOptions Options(bool headless=false)=>new(projectPath,headless,null,4,1d/60,30,Path.Combine(output,Guid.NewGuid()+".json"),false,false);
        byte[]? playerBloom=null;var report=PlayerRunner.RunForVerification(Options(),Path.GetFullPath(plugins),(pixels,w,h)=>playerBloom??=pixels);
        Pass(report.ExitCode==0&&report.Tick>=4&&playerBloom is not null&&report.ValidationErrors==0&&report.ValidationWarnings==0&&report.ShutdownErrors.Length==0,"formal Player renders configured post");
        obj.Set(PostProcessData.Off);SceneDocumentFiles.Save(document,scenePath);byte[]? playerOff=null;
        report=PlayerRunner.RunForVerification(Options(),Path.GetFullPath(plugins),(pixels,w,h)=>playerOff??=pixels);
        Pass(report.ExitCode==0&&playerOff is not null&&!playerOff.SequenceEqual(playerBloom!),"formal Player Off distinct from Bloom");obj.Set(active);SceneDocumentFiles.Save(document,scenePath);
        var headless=PlayerRunner.Run(Options(true),pluginRoot:Path.GetFullPath(plugins),visible:false);
        Pass(headless.ExitCode==0&&headless.Tick==4&&headless.RenderedFrames==0&&headless.Modules.Length==0,"Headless validates values and explicit file closure without graphics service");
        Project(config with{PostProcessLibrary=new(1,new string('0',64))});
        foreach(bool cpu in new[]{false,true}){var bad=PlayerRunner.Run(Options(cpu),pluginRoot:Path.GetFullPath(plugins),visible:false);Pass(bad.ExitCode==3&&bad.Tick==0&&bad.SessionId==Guid.Empty&&bad.ShutdownErrors.Length==0,"wrong explicit library hash before gameplay, no default fallback");}Project(config);
        obj.Set(active with{ToneModule=Guid.NewGuid()});SceneDocumentFiles.Save(document,scenePath);var invalid=PlayerRunner.Run(Options(),pluginRoot:Path.GetFullPath(plugins),visible:false);
        Pass(invalid.ExitCode==2&&invalid.SessionId==Guid.Empty&&invalid.Tick==0,"unknown registered module before gameplay");obj.Set(active);SceneDocumentFiles.Save(document,scenePath);
        string packed="assets/game.ncpak";File.WriteAllBytes(Path.Combine(fixture.Root,packed),SceneAssetPreparation.CreateRuntimePackage(fixture.Root,fixture.Project,document.CaptureSnapshot(),[]));Project(config with{AssetPackage=packed});
        report=PlayerRunner.RunForVerification(Options(),Path.GetFullPath(plugins),(pixels,w,h)=>Pass(pixels.SequenceEqual(playerBloom!),"NCP scene same post pixels"));Pass(report.ExitCode==0&&report.ShutdownErrors.Length==0,"runtime package post binding");Project(config);
        int captured=0;byte[]? last=null;
        report=PlayerRunner.RunForVerification(Options() with{Ticks=16},Path.GetFullPath(plugins),(pixels,w,h)=>{captured++;last=pixels;},
            (play,tick)=>{if(captured>0)play.Document.World.FindObject(settingId).Set(active with{ToneModule=Guid.NewGuid()});});
        Pass(report.ExitCode==5&&captured==2&&last is not null&&last.Where((b,i)=>b!=last[i%4]).Any()==false&&report.ShutdownErrors.Length==0,"Player runtime corruption clears then faults without stale output");
        using(var editor=new CandidatePresentation(Path.GetFullPath(plugins),ProjectContext.Load(projectPath),true,false,false)){
            editor.Start();ulong frame=0;byte[] Draw(){byte[]? pixels=null;editor.CaptureNextViewportForVerification((p,w,h)=>pixels=p);Pass(editor.PumpPlatform(),"Editor pump");var f=new FrameContext(++frame,1d/60);editor.ApplyEditorIntents(f);editor.PumpAgent(f);editor.BeginPresentation(f);editor.SubmitInput(f);editor.AdvancePlay(f);editor.Present(f);return pixels??throw new Exception("No viewport capture");}
            byte[] baseline=Draw();var owner=editor.VerificationEditor;var edit=owner.Edit!;
            if(authoring){
                var view=editor.VerificationPresenter;var workspace=new EditorWorkspace(owner);workspace.Select(workspace.Stamp,settingId);baseline=Draw();
                void Change(string label,double number){var item=view.Items.ToArray().Single(i=>System.Text.Encoding.UTF8.GetString(view.Text.Slice((int)i.LabelOffset,(int)i.LabelLength))==label);var f=view.Frame;
                    Ncma.Gui.GuiEvent E(uint phase)=>new(){Kind=item.Kind,WidgetHigh=item.WidgetHigh,WidgetLow=item.WidgetLow,Phase=phase,Value=number,Frame=f.Frame,ViewGeneration=f.ViewGeneration,DocumentGeneration=f.DocumentGeneration,Revision=f.Revision};view.Apply([E(1),E(2),E(3)],[]);}
                Pass(view.PostPreparationStatus.Contains("就绪"),"post ready status matches rendered world");int history=edit.State.UndoCount;
                Change("启用后处理 / 关闭对照",0);byte[] panelOff=Draw();Pass(!panelOff.SequenceEqual(baseline)&&edit.State.UndoCount==history+1&&view.PostPreparationStatus.Contains("关闭"),"panel Off compare actual pixels and one history");
                workspace.History(workspace.Stamp,false);Pass(Draw().SequenceEqual(baseline),"panel Undo exact rendered Bloom");workspace.History(workspace.Stamp,true);Pass(Draw().SequenceEqual(panelOff),"panel Redo exact rendered Off");
                Change("启用后处理 / 关闭对照",1);Pass(Draw().SequenceEqual(baseline),"panel reactivate exact settings");Change("Bloom / intensity",0);byte[] zero=Draw();Pass(!zero.SequenceEqual(baseline),"registered Bloom parameter changes actual viewport");
                workspace.History(workspace.Stamp,false);Pass(Draw().SequenceEqual(baseline),"parameter Undo exact viewport");Change("曝光 EV",-1);Pass(!Draw().SequenceEqual(baseline),"panel EV actual viewport");workspace.History(workspace.Stamp,false);Pass(Draw().SequenceEqual(baseline),"EV Undo actual viewport");
            }
            var permission=new CapabilityPermissions(["ncma.scene.transaction","ncma.history.undo","ncma.history.redo"],objectScope:[settingId],componentScope:[PostProcessData.TypeId]);
            CapabilityRequest Request(PostProcessData value)=>new(EditSession.ContractVersion,Guid.NewGuid(),edit.SessionId,edit.Revision,"ncma.scene.transaction",
                JsonSerializer.SerializeToElement(new{operations=new[]{new{op="set_component",objectId=settingId,typeId=PostProcessData.TypeId,version=1,data=owner.Document.World.Components.Encode(value)}}}));
            Pass(edit.Invoke(Request(PostProcessData.Off)).Status=="denied","Agent default read-only");Pass(edit.Invoke(Request(PostProcessData.Off),permission).Changed,"original scoped scene command");byte[] off=Draw();Pass(!off.SequenceEqual(baseline),"Editor actual Off command pixels");
            CapabilityRequest History(string name)=>new(EditSession.ContractVersion,Guid.NewGuid(),edit.SessionId,edit.Revision,name,JsonSerializer.SerializeToElement(new{}));
            Pass(edit.Invoke(History("ncma.history.undo"),permission).Changed,"sole Undo");Pass(Draw().SequenceEqual(baseline),"Undo exact viewport");
            Pass(edit.Invoke(History("ncma.history.redo"),permission).Changed,"sole Redo");Pass(Draw().SequenceEqual(off),"Redo exact viewport");
            Pass(edit.Invoke(Request(active),permission).Changed,"restore active");byte[] beforePlay=Draw();var play=owner.StartPlay();Pass(Draw().SequenceEqual(beforePlay),"Edit/Play shared post");play.Pause();owner.ReloadGameplay(Path.Combine(fixture.Root,"gameplay.dll"));Pass(Draw().SequenceEqual(beforePlay),"reload post binding");owner.StopPlay();Pass(Draw().SequenceEqual(beforePlay),"stop preserves Edit");
            owner.Document.World.FindObject(settingId).Set(active with{ToneModule=Guid.NewGuid()});byte[] failed=Draw();Pass(!failed.SequenceEqual(beforePlay)&&failed.Where((b,i)=>b!=failed[i%4]).Any()==false,"invalid scene clears viewport, no stale effect");
            if(authoring)Pass(editor.VerificationPresenter.PostPreparationStatus.Contains("失败"),"failed prepared status exact world stamp");
            owner.Document.World.FindObject(settingId).Set(active);Pass(Draw().SequenceEqual(beforePlay),"explicit recovery restores pixels");Pass(editor.VerificationStats.ValidationErrors==0&&editor.VerificationStats.ValidationWarnings==0,"Editor API0/0");
        }
        // A trusted user module, its exact UUID/file binding and public graph delegate traverse
        // the same formal host path. No special official-module dispatch or source compilation.
        Guid userModule=Guid.NewGuid();var userIndex=post with{Programs=post.Programs.Select(p=>p with{ModuleId=userModule}).ToArray()};byte[] userBytes=userIndex.Encode();
        File.WriteAllBytes(Path.Combine(fixture.Root,PostProcessLibrarySelection.IndexPath),userBytes);Project(config with{PostProcessLibrary=new(1,Convert.ToHexString(SHA256.HashData(userBytes)))});
        obj.Set(active with{StackJson=PostProcessData.EncodeStack([new(userModule,3,[new("gain",.8f)])])});SceneDocumentFiles.Save(document,scenePath);int builds=0;
        var custom=new PostProcessModules([new(PostProcessLibrarySelection.AcesModule,1)],
            [new(userModule,3,10,[new("gain",0,4)],[],(w,h,p,program)=>{builds++;return BloomFeature.Build(w,h,new(Levels:2,Threshold:0,Intensity:p["gain"]),program);},"Host user Bloom")]);
        byte[]? userPixels=null;report=PlayerRunner.RunForVerification(Options(),Path.GetFullPath(plugins),(pixels,w,h)=>userPixels??=pixels,postModules:custom);
        Pass(report.ExitCode==0&&userPixels is not null&&userPixels.SequenceEqual(playerBloom!)&&builds==2,"formal user module same pixels, bounded preflight and viewport builds");
        using(var editor=new CandidatePresentation(Path.GetFullPath(plugins),ProjectContext.Load(projectPath),true,false,false,postProcessModules:custom)){
            editor.Start();byte[]? image=null;editor.CaptureNextViewportForVerification((p,w,h)=>image=p);Pass(editor.PumpPlatform(),"user Editor pump");var frame=new FrameContext(1,1d/60);
            editor.ApplyEditorIntents(frame);editor.PumpAgent(frame);editor.BeginPresentation(frame);editor.SubmitInput(frame);editor.AdvancePlay(frame);editor.Present(frame);
            Pass(image is not null&&image.Distinct().Count()>4&&editor.VerificationStats.ValidationErrors==0&&editor.VerificationStats.ValidationWarnings==0,"formal Editor accepts same trusted custom registry");
            if(authoring){
                var workspace=new EditorWorkspace(editor.VerificationEditor);workspace.Select(workspace.Stamp,settingId);var view=editor.VerificationPresenter;view.Build(2,1280,720);
                var item=view.Items.ToArray().Single(i=>System.Text.Encoding.UTF8.GetString(view.Text.Slice((int)i.LabelOffset,(int)i.LabelLength))=="Host user Bloom / gain");var f=view.Frame;
                Ncma.Gui.GuiEvent E(uint phase)=>new(){Kind=item.Kind,WidgetHigh=item.WidgetHigh,WidgetLow=item.WidgetLow,Phase=phase,Value=0,Frame=f.Frame,ViewGeneration=f.ViewGeneration,DocumentGeneration=f.DocumentGeneration,Revision=f.Revision};view.Apply([E(1),E(2),E(3)],[]);
                byte[] before=image!;image=null;editor.CaptureNextViewportForVerification((p,w,h)=>image=p);Pass(editor.PumpPlatform(),"custom changed Editor pump");frame=new FrameContext(2,1d/60);editor.ApplyEditorIntents(frame);editor.PumpAgent(frame);editor.BeginPresentation(frame);editor.SubmitInput(frame);editor.AdvancePlay(frame);editor.Present(frame);
                Pass(image is not null&&!image.SequenceEqual(before)&&editor.VerificationStats.ValidationErrors==0&&editor.VerificationStats.ValidationWarnings==0,"user module panel actual changed pixels API0/0");
            }
        }
        File.WriteAllText(Path.Combine(output,"post-host-results.json"),JsonSerializer.Serialize(new{cases,authoring,manualAccepted=false}));Console.WriteLine($"PASS M7.4-{(authoring?"C3":"C2")} formal post hosts: {cases} checks; API0/0; {output}");return 0;
    }
}
