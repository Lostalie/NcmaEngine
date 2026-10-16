using System.Security.Cryptography;
using System.Text;
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

internal static unsafe partial class Program
{
    private static int ShadowHostTests(string plugins,string output)
    {

        using var playerRunOwner0 = new Ncma.Player.App.PlayerRunner();
        using var playerRunOwner1 = new Ncma.Player.App.PlayerRunner();

        using var playerRunOwner2 = new Ncma.Player.App.PlayerRunner();
        using var playerRunOwner3 = new Ncma.Player.App.PlayerRunner();

        using var playerRunOwner4 = new Ncma.Player.App.PlayerRunner();
        using var playerRunOwner5 = new Ncma.Player.App.PlayerRunner();
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);int cases=0;
        void Pass(bool ok,string label){Check(ok,"M7.5 shadow hosts: "+label);cases++;}
        var repo=new DirectoryInfo(Path.GetFullPath(plugins));while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent??throw new Exception("Repo missing");string build=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        using var staged=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repo.FullName,"out/verification/m2-7",build,"packages.json")));string deployed=staged.RootElement.GetProperty("editor").GetString()!;DeploymentManifest.Validate(deployed);
        var fixture=new SceneGpuFixture(output);var(document,camera,_,_)=fixture.Scene();global::Ncma.Runtime.Entity policy=document.World.CreateEntity("Shadow configuration");document.World.Set(policy, new SceneShadowData(1,SceneShadowQuality.High));Guid id=document.World.GetUuid(policy);
        var shaders=ShaderPackageSelection.Decode(File.ReadAllBytes(Path.Combine(deployed,RuntimeShaderFileSet.DefaultSelectionPath)));
        byte[] highIndex=File.ReadAllBytes(Path.Combine(deployed,ShadowLibrarySelection.IndexPath));var high=ShadowLibrarySelection.Decode(highIndex);
        foreach(string path in shaders.Packages.Select(p=>p.Path).Concat(high.Programs.Select(p=>p.Path)).Append(ShadowLibrarySelection.IndexPath)){
            string destination=Path.Combine(fixture.Root,path);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(Path.Combine(deployed,path),destination);
        }
        string gameplay=Path.Combine(repo.FullName,"out/managed/Ncma.Gameplay.Sample.dll");File.Copy(gameplay,Path.Combine(fixture.Root,"gameplay.dll"));File.Copy(Path.ChangeExtension(gameplay,".deps.json"),Path.Combine(fixture.Root,"gameplay.deps.json"));
        string scenePath=Path.Combine(fixture.Root,"start.ncmascene"),projectPath=Path.Combine(fixture.Root,"project.ncmaproject");SceneDocumentFiles.Save(document,scenePath);
        var config=new ProjectConfiguration(1,fixture.Project,"Shadow hosts","start.ncmascene","gameplay.dll","Direct3D11",[],SceneCamera:camera,ShaderPackages:shaders,ShadowLibrary:new(1,Convert.ToHexString(SHA256.HashData(highIndex))));
        void Project(ProjectConfiguration value)=>File.WriteAllBytes(projectPath,JsonSerializer.SerializeToUtf8Bytes(value,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));Project(config);
        PlayerOptions Options(bool cpu=false)=>new(projectPath,cpu,null,3,1d/60,30,Path.Combine(output,Guid.NewGuid()+".json"),false,false);
        byte[]? highImage=null;var report=playerRunOwner0.RunForVerification(Options(),Path.GetFullPath(plugins),(image,w,h)=>highImage??=image);
        Pass(report.ExitCode==0&&report.Tick>=3&&highImage is not null&&report.ShutdownErrors.Length==0&&report.ValidationErrors==0&&report.ValidationWarnings==0,"formal Player High before gameplay preflight and actual render");
        document.World.Set(policy, new SceneShadowData(1,SceneShadowQuality.Off));SceneDocumentFiles.Save(document,scenePath);byte[]? offImage=null;report=playerRunOwner1.RunForVerification(Options(),Path.GetFullPath(plugins),(image,w,h)=>offImage??=image);
        Pass(report.ExitCode==0&&offImage is not null&&!offImage.SequenceEqual(highImage!)&&report.ShutdownErrors.Length==0,"Player Off removes visible shadow");
        document.World.Set(policy, new SceneShadowData(1,SceneShadowQuality.High));SceneDocumentFiles.Save(document,scenePath);
        Project(config with{ShadowLibrary=new(1,new string('0',64))});report=playerRunOwner2.Run(Options(),pluginRoot:Path.GetFullPath(plugins),visible:false);
        Pass(report.ExitCode==3&&report.Tick==0&&report.SessionId==Guid.Empty&&report.Modules.Length==0&&report.ShutdownErrors.Length==0,"bad explicit high index rejected before native/gameplay, no fallback");
        var cpu=playerRunOwner3.Run(Options(true),pluginRoot:Path.GetFullPath(plugins),visible:false);Pass(cpu.ExitCode==0&&cpu.Tick==3&&cpu.Modules.Length==0&&cpu.RenderedFrames==0,"Null only validates scene values, no GPU package dependency");Project(config);
        report=playerRunOwner4.Run(Options(),pluginRoot:Path.GetFullPath(plugins),visible:false,pipelineFactory:(e,a,s)=>new Scene3DPipeline(e,a,256,s));Pass(report.ExitCode!=0&&report.Tick==0&&report.SessionId==Guid.Empty&&report.ShutdownErrors.Length==0,"custom graph mismatching explicit High rejected before gameplay");
        report=playerRunOwner5.Run(Options(),pluginRoot:Path.GetFullPath(plugins),visible:false,pipelineFactory:(e,a,s)=>new Scene3DPipeline(e,a,2048,s));Pass(report.ExitCode==0&&report.Tick>=3&&report.ShutdownErrors.Length==0,"matching public custom pipeline supports High");
        using(var editor=new CandidatePresentation(Path.GetFullPath(plugins),ProjectContext.Load(projectPath),true,false,false)){
            editor.Start();ulong frame=0;byte[] Draw(){byte[]? pixels=null;editor.CaptureNextViewportForVerification((p,w,h)=>pixels=p);Pass(editor.PumpPlatform(),"Editor pump");var f=new FrameContext(++frame,1d/60);editor.ApplyEditorIntents(f);editor.PumpAgent(f);editor.BeginPresentation(f);editor.SubmitInput(f);editor.AdvancePlay(f);editor.Present(f);return pixels??throw new Exception("No Editor capture");}
            var owner=editor.VerificationEditor;var workspace=new EditorWorkspace(owner);workspace.Select(workspace.Stamp,id);byte[] original=Draw();var edit=owner.Edit!;var view=editor.VerificationPresenter;
            void Click(string label){var item=view.Items.ToArray().Single(i=>Encoding.UTF8.GetString(view.Text.Slice((int)i.LabelOffset,(int)i.LabelLength))==label);var f=view.Frame;
                view.Apply([new Ncma.Gui.GuiEvent{Kind=item.Kind,WidgetHigh=item.WidgetHigh,WidgetLow=item.WidgetLow,Phase=3,Frame=f.Frame,ViewGeneration=f.ViewGeneration,DocumentGeneration=f.DocumentGeneration,Revision=f.Revision}],[]);}
            int count=edit.State.UndoCount;Click("阴影：Off");byte[] off=Draw();Pass(!off.SequenceEqual(original)&&edit.State.UndoCount==count+1&&SceneShadowState.Read(owner.Document.CaptureSnapshot())?.Quality==SceneShadowQuality.Off,"Inspector Off changes pixels through one history entry");
            workspace.History(workspace.Stamp,false);Pass(Draw().SequenceEqual(original),"Undo restores exact High pixels");workspace.History(workspace.Stamp,true);Pass(Draw().SequenceEqual(off),"Redo restores exact Off pixels");
            Click("阴影：High");Pass(Draw().SequenceEqual(original),"Inspector High uses already admitted library");
            var permissions=new CapabilityPermissions(["ncma.scene.transaction","ncma.history.undo","ncma.history.redo"],objectScope:[id],componentScope:[SceneShadowData.TypeId]);
            CapabilityRequest Request(SceneShadowQuality value)=>new(EditSession.ContractVersion,Guid.NewGuid(),edit.SessionId,edit.Revision,"ncma.scene.transaction",JsonSerializer.SerializeToElement(new{operations=new[]{new{op="set_component",objectId=id,typeId=SceneShadowData.TypeId,version=1,data=new{version=1,quality=(int)value}}}}));
            Pass(edit.Invoke(Request(SceneShadowQuality.Low)).Status=="denied","Agent default cannot change shadow quality");Pass(edit.Invoke(Request(SceneShadowQuality.Low),permissions).Changed,"exact scoped original Agent transaction");Draw();
            Pass(!edit.Invoke(Request((SceneShadowQuality)4),permissions).Changed&&SceneShadowState.Read(owner.Document.CaptureSnapshot())?.Quality==SceneShadowQuality.Low,"bad Agent quality rejected without mutation");
            Click("阴影：High");Draw();byte[] editBytes=owner.Document.CaptureBytes();
            for(int cycle=0;cycle<3;cycle++){
                var play=owner.StartPlay();Draw();Pass(play.Document.World.Identity!=owner.Document.World.Identity&&SceneShadowState.Read(play.Document.CaptureSnapshot())?.Quality==SceneShadowQuality.High,"independent Play High");
                var disabled=view.Items.ToArray().Single(i=>Encoding.UTF8.GetString(view.Text.Slice((int)i.LabelOffset,(int)i.LabelLength))=="阴影：Off");Pass(disabled.Enabled==0,"Play freezes editor shadow controls");
                play.Pause();owner.ReloadGameplay(Path.Combine(fixture.Root,"gameplay.dll"));Draw();owner.StopPlay();Pass(Draw().SequenceEqual(original)&&editBytes.AsSpan().SequenceEqual(owner.Document.CaptureBytes()),"Play Reload Stop preserve Edit High");
            }
            Pass(editor.VerificationStats.ValidationErrors==0&&editor.VerificationStats.ValidationWarnings==0,"Editor API0/0");
        }
        File.WriteAllText(Path.Combine(output,"shadow-host-results.json"),JsonSerializer.Serialize(new{cases,apiErrors=0,apiWarnings=0,sourceHosts=true,relocatedProducts=false,animatedOracle=false,manualOsInput=false}));Console.WriteLine($"PASS M7.5 formal source shadow hosts: {cases} checks; API0/0; {output}");return 0;
    }
}
