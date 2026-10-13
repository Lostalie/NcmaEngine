using System.Numerics;
using System.Text;
using System.Text.Json;
using Ncma.Application;
using Ncma.Assets;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Player.App;
using Ncma.Rendering;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    private static int EnvironmentAuthoringHostTests(string root,string output)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);int cases=0,maxError=0;
        void Pass(bool value,string label){Check(value,"M7.3-C3-B2 "+label);cases++;}
        var repository=new DirectoryInfo(Path.GetFullPath(root));while(!File.Exists(Path.Combine(repository.FullName,"Build.bat")))repository=repository.Parent??throw new Exception("Repository missing");
        string deployed=Path.Combine(repository.FullName,"out/bin");var fixture=new SceneGpuFixture(output);var(document,camera,_,_)=fixture.Scene();
        var light=document.World.GetObjects().Single(o=>o.Has<DirectionalLightData>());light.Set(light.Get<DirectionalLightData>() with{Intensity=0,CastShadow=false});
        document.World.CreateObject("Rendering").Set(RenderConfiguration.Default(Guid.NewGuid()) with{Ambient=0});Guid environment=document.World.CreateObject("Environment").PersistentId;
        string scenePath=Path.Combine(fixture.Root,"start.ncmascene"),projectPath=Path.Combine(fixture.Root,"project.ncmaproject");SceneDocumentFiles.Save(document,scenePath);
        var selection=ShaderPackageSelection.Decode(File.ReadAllBytes(Path.Combine(deployed,RuntimeShaderFileSet.DefaultSelectionPath)));
        foreach(var entry in selection.Packages){string destination=Path.Combine(fixture.Root,entry.Path);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(Path.Combine(deployed,entry.Path),destination);}
        string gameplay=Path.Combine(repository.FullName,"out/managed/Ncma.Gameplay.Sample.dll");File.Copy(gameplay,Path.Combine(fixture.Root,"gameplay.dll"));File.Copy(Path.ChangeExtension(gameplay,".deps.json"),Path.Combine(fixture.Root,"gameplay.deps.json"));
        var config=new ProjectConfiguration(1,fixture.Project,"Environment authoring","start.ncmascene","gameplay.dll","Direct3D11",[],SceneCamera:camera,ShaderPackages:selection);
        void SaveProject()=>File.WriteAllBytes(projectPath,JsonSerializer.SerializeToUtf8Bytes(config,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));SaveProject();
        EnvironmentPackage package;using(var library=EnvironmentPresetLibrary.LoadDeployment(deployed))package=library.Package;
        var oracle=new IblImageOracle(package);
        void Pixels(byte[] data,uint width,uint height){int x=(int)width/2,y=(int)height/2;var position=new Vector3(((x+.5f)/width*2-1)*2*width/height,(1-(y+.5f)/height*2)*2,-4);var expected=oracle.Evaluate(position,Vector3.UnitZ,new(.6f,.25f,.1f),0,.6f,1,1,0);int error=0;for(int c=0;c<3;c++)error=Math.Max(error,Math.Abs(data[(y*(int)width+x)*4+c]-(int)MathF.Round(expected[c]*255)));maxError=Math.Max(maxError,error);Pass(error<=3,"independent preset pixel error="+error);}
        using(var editor=new CandidatePresentation(Path.GetFullPath(root),ProjectContext.Load(projectPath),true,false,false,environmentPresets:()=>EnvironmentPresetLibrary.LoadDeployment(deployed))){
            editor.Start();ulong frame=0;byte[] Draw(){byte[]? pixels=null;editor.CaptureNextViewportForVerification((data,w,h)=>pixels=data);Check(editor.PumpPlatform(),"window pump");var context=new FrameContext(++frame,1d/60);editor.ApplyEditorIntents(context);editor.PumpAgent(context);editor.BeginPresentation(context);editor.SubmitInput(context);editor.AdvancePlay(context);editor.Present(context);return pixels??throw new Exception("Missing frame");}
            var before=Draw();var owner=editor.VerificationEditor;var workspace=new EditorWorkspace(owner);workspace.Select(workspace.Stamp,environment);Draw();var view=editor.VerificationPresenter;
            void Click(string label,double value=0){var items=view.Items.ToArray().Where(i=>Encoding.UTF8.GetString(view.Text.Slice((int)i.LabelOffset,(int)i.LabelLength))==label).ToArray();if(items.Length!=1)throw new Exception("Missing UI: "+label+"; review="+(owner.Environments!.Review is not null));var item=items[0];var f=view.Frame;view.Apply([new GuiEvent{Kind=item.Kind,WidgetHigh=item.WidgetHigh,WidgetLow=item.WidgetLow,Phase=3,Value=value,Frame=f.Frame,ViewGeneration=f.ViewGeneration,DocumentGeneration=f.DocumentGeneration,Revision=f.Revision}],[]);}
            Pass(owner.Environments is not null&&!owner.Document.World.FindObject(environment).Has<EnvironmentLightingData>(),"source host idle no environment attachment");int history=owner.Edit!.State.UndoCount;
            // Hidden native windows continuously cancel unfocused interactions. Drive the copied
            // review frames explicitly; retain the real host pipeline for the committed image.
            Click("选择合成中性预设…");view.Build(++frame,1280,720);Click("已核对目标、资源和文件发布范围",1);view.Build(++frame,1280,720);var review=owner.Environments!.Review!;Click("批准并应用预设（一次撤销）");var lit=Draw();var size=editor.VerificationViewport;Pixels(lit,size.Width,size.Height);
            Pass(!lit.SequenceEqual(before)&&owner.Edit.State.UndoCount==history+1&&view.EnvironmentPreparationStatus.Contains("就绪"),"formal Inspector publishes and changes viewport once");
            string descriptor=Path.Combine(fixture.Root,review.Path);var d=EnvironmentAssetDescriptor.Decode(File.ReadAllBytes(descriptor));string generation=Path.Combine(fixture.Root,d.Path);Pass(PinLocked(generation),"active NCE immutable pin");
            for(int i=0;i<3;i++){workspace.History(workspace.Stamp,false);Pass(Draw().SequenceEqual(before)&&!File.Exists(descriptor)&&File.Exists(generation),"Undo exact image and descriptor, retain pinned generation");workspace.History(workspace.Stamp,true);Pass(Draw().SequenceEqual(lit)&&File.Exists(descriptor),"Redo exact image descriptor");}
            owner.RefreshAssets(true);var play=owner.StartPlay();Pass(Draw().SequenceEqual(lit),"Play image");play.Pause();owner.ReloadGameplay(Path.Combine(fixture.Root,"gameplay.dll"));Pass(Draw().SequenceEqual(lit),"Reload image");owner.StopPlay();Pass(Draw().SequenceEqual(lit),"Stop image");
            workspace.Save(workspace.Stamp,scenePath);File.WriteAllBytes(Path.Combine(fixture.Root,"assets/game.ncpak"),SceneAssetPreparation.CreateRuntimePackage(fixture.Root,fixture.Project,owner.Document.CaptureSnapshot(),[]));
            Pass(editor.VerificationStats.ValidationErrors==0&&editor.VerificationStats.ValidationWarnings==0,"DX11 API0/0");File.WriteAllBytes(Path.Combine(output,"authored-preset.png"),Png(size.Width,size.Height,lit));
        }
        var options=new PlayerOptions(projectPath,false,null,4,1d/60,30,Path.Combine(output,"player.json"),false,false);int captures=0;
        var player=PlayerRunner.RunForVerification(options,Path.GetFullPath(root),(pixels,w,h)=>{Pixels(pixels,w,h);captures++;});Pass(player.ExitCode==0&&captures>0&&player.ValidationErrors==0&&player.ValidationWarnings==0&&player.ShutdownErrors.Length==0,"saved source Player exact environment");
        config=config with{AssetPackage="assets/game.ncpak"};SaveProject();captures=0;var packed=PlayerRunner.RunForVerification(options with{Report=Path.Combine(output,"packed-player.json")},Path.GetFullPath(root),(pixels,w,h)=>{Pixels(pixels,w,h);captures++;});Pass(packed.ExitCode==0&&captures>0&&packed.ValidationErrors==0&&packed.ValidationWarnings==0&&packed.ShutdownErrors.Length==0,"NCP authored environment Player");
        File.WriteAllText(Path.Combine(output,"authoring-host-results.json"),JsonSerializer.Serialize(new{cases,maxError}));Console.WriteLine($"PASS M7.3-C3-B2 formal authoring hosts: {cases} cases; pixelError={maxError}; API0/0");return 0;
    }
}
