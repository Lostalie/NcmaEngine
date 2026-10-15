using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Application;
using Ncma.Assets;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Player.App;
using Ncma.Rendering;
using Ncma.Scene.Rendering;

internal sealed class EnvironmentProductEvidence
{
    internal int Checks,MaxPixelError;internal readonly List<object> Frames=[];
    internal void Check(bool ok,string label){if(!ok)throw new Exception("C3-C product: "+label);Checks++;}
    internal void Capture(byte[] data,uint w,uint h,string label,Program.IblImageOracle? oracle,float strength=1,float rotation=0)
    {
        if(oracle is not null){int x=(int)w/2,y=(int)h/2;var position=new Vector3(((x+.5f)/w*2-1)*2*w/h,(1-(y+.5f)/h*2)*2,-4);var expected=oracle.Evaluate(position,Vector3.UnitZ,new(.6f,.25f,.1f),0,.6f,1,strength,rotation);int error=0;for(int c=0;c<3;c++)error=Math.Max(error,Math.Abs(data[(y*(int)w+x)*4+c]-(int)MathF.Round(expected[c]*255)));MaxPixelError=Math.Max(MaxPixelError,error);Check(error<=3,label+" independent pixel "+error);}
        Check(data.Length==checked((int)(w*h*4)),"capture dimensions");Frames.Add(new{label,width=w,height=h,sha256=Convert.ToHexString(SHA256.HashData(data))});
    }
    internal static T Field<T>(object owner,string name)=>(T)(owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)??throw new Exception("Missing "+name));
    internal static Program.IblImageOracle Oracle(JsonElement request,string name){string path=request.GetProperty(name).GetString()!;byte[] bytes=File.ReadAllBytes(path);return new(EnvironmentPackage.Decode(bytes,Convert.ToHexString(SHA256.HashData(bytes))));}
    internal static string[] NativePaths(string root){
        // Process.Modules also enumerates managed PE mappings, including this test harness.
        // Those assemblies are independently checked by the strict product LoadContext.
        bool Native(string path){using var stream=File.OpenRead(path);using var pe=new System.Reflection.PortableExecutable.PEReader(stream);return !pe.HasMetadata;}
        var modules=Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Where(m=>(m.ModuleName.StartsWith("Ncma",StringComparison.OrdinalIgnoreCase)||m.ModuleName=="glfw3.dll")&&!m.ModuleName.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)).Select(m=>m.FileName).Where(Native).ToArray();
        if(modules.Any(p=>!Path.GetFullPath(p).StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new Exception("Native module outside checked product: "+string.Join(";",modules));return modules;
    }
}

internal static class EnvironmentEditorProductProbe
{
    internal static void Run(string requestPath)
    {
        using var json=JsonDocument.Parse(File.ReadAllBytes(requestPath));var r=json.RootElement;string root=r.GetProperty("productRoot").GetString()!,project=r.GetProperty("project").GetString()!,output=r.GetProperty("output").GetString()!;
        bool skin=r.GetProperty("skin").GetBoolean(),dynamic=r.GetProperty("dynamic").GetBoolean();Guid environment=r.GetProperty("environment").GetGuid();var evidence=new EnvironmentProductEvidence();
        DeploymentManifest.Validate(root);var context=ProjectContext.Load(project);var oracle=skin?null:EnvironmentProductEvidence.Oracle(r,"oracle");
        object? costs=null;string[] native=[];
        using(var editor=new CandidatePresentation(Path.Combine(root,"plugins"),context,true,false,false)){
            editor.Start();ulong frame=0;
            byte[] Draw(bool capture=true,double delta=0){byte[]? pixels=null;if(capture)editor.CaptureNextViewportForVerification((p,w,h)=>pixels=p);if(!editor.PumpPlatform())throw new Exception("Window closed");var f=new FrameContext(++frame,delta);editor.ApplyEditorIntents(f);editor.PumpAgent(f);editor.BeginPresentation(f);editor.SubmitInput(f);editor.AdvancePlay(f);editor.Present(f);return pixels??[];}
            var baseline=Draw();var size=editor.VerificationViewport;var owner=editor.VerificationEditor;var workspace=new EditorWorkspace(owner);var initial=owner.Document.World.FindObject(environment).Get<EnvironmentLightingData>();
            evidence.Capture(baseline,size.Width,size.Height,"default",oracle);File.WriteAllBytes(Path.Combine(output,"baseline.rgba"),baseline);
            evidence.Check(editor.VerificationPresenter.EnvironmentPreparationStatus.Contains("就绪"),"prepared current product environment");
            void Set(EnvironmentLightingData value){var edit=owner.Edit!;var request=RenderingEditorAdapter.EnvironmentRequest(edit,environment,value,edit.Revision);var result=edit.Invoke(request,new(["ncma.scene.transaction"],objectScope:[environment],componentScope:[EnvironmentLightingData.TypeId]));evidence.Check(result.Changed,"committed environment operation: "+JsonSerializer.Serialize(result));}
            Set(initial with{Strength=.25f,RotationRadians=.5f});var quarter=Draw();evidence.Capture(quarter,size.Width,size.Height,"quarter rotated",oracle,.25f,.5f);evidence.Check(!quarter.SequenceEqual(baseline),"environment strength affects image");
            workspace.History(workspace.Stamp,false);evidence.Check(Draw().SequenceEqual(baseline),"exact Undo image");workspace.History(workspace.Stamp,true);evidence.Check(Draw().SequenceEqual(quarter),"exact Redo image");
            Set(EnvironmentLightingData.Off);var off=Draw();evidence.Check(!off.SequenceEqual(quarter),"Off image");Set(initial);evidence.Check(Draw().SequenceEqual(baseline),"on image restored");
            if(dynamic){
                // Generation replacement is a trusted, explicit Off/refresh/on boundary, not
                // an in-place edit of a pinned immutable resource or an Agent file operation.
                Set(EnvironmentLightingData.Off);Draw();
                byte[] descriptor=File.ReadAllBytes(r.GetProperty("replacementDescriptor").GetString()!);var d=EnvironmentAssetDescriptor.Decode(descriptor);File.WriteAllBytes(Path.Combine(context.Root,"assets/environment.ncenv"),descriptor);
                owner.RefreshRenderAssets();
                Set(initial with{Generation=d.Generation,ContentHash=d.ContentHash});var changed=Draw();var nextOracle=EnvironmentProductEvidence.Oracle(r,"replacementOracle");evidence.Capture(changed,size.Width,size.Height,"generation2",nextOracle);evidence.Check(!changed.SequenceEqual(baseline),"new generation actual image");
                Set(initial with{Generation=d.Generation,ContentHash=d.ContentHash,RotationRadians=MathF.PI});var rotated=Draw();evidence.Capture(rotated,size.Width,size.Height,"direction rotated",nextOracle,1,MathF.PI);evidence.Check(!rotated.SequenceEqual(changed),"directional Y rotation affects image");
                var denied=owner.Edit!.Invoke(RenderingEditorAdapter.EnvironmentRequest(owner.Edit,environment,initial with{Generation=999},owner.Edit.Revision),new(["ncma.scene.transaction"],objectScope:[environment],componentScope:[EnvironmentLightingData.TypeId]));
                evidence.Check(!denied.Changed&&denied.Status=="error"&&Draw().SequenceEqual(rotated),"bad generation command rejected without changing image");
                // Separate fault injection from ordinary commands: emulate a trusted extension
                // corrupting live state. This is not an editor/Agent mutation capability.
                owner.Document.World.FindObject(environment).Set(initial with{Generation=999});var failed=Draw();evidence.Check(failed.Where((b,i)=>b!=failed[i%4]).Any()==false&&editor.VerificationPresenter.EnvironmentPreparationStatus.Contains("失败"),"externally corrupted generation themed clear and error");
                owner.Document.World.FindObject(environment).Set(initial with{Generation=d.Generation,ContentHash=d.ContentHash});evidence.Check(Draw().SequenceEqual(changed),"recover valid generation");baseline=changed;oracle=nextOracle;
            }
            // Resize the actual viewport through the existing workspace splitter contract.
            var view=editor.VerificationPresenter;view.AttachLayout(new(Path.Combine(output,"workspace.json")));Draw();var splitter=view.Items.ToArray().Single(i=>i.Kind==(uint)GuiItemKind.Splitter&&i.WidgetHigh==32&&i.WidgetLow==1);var vf=view.Frame;
            GuiEvent Split(uint phase)=>new(){Kind=splitter.Kind,WidgetHigh=32,WidgetLow=1,Phase=phase,Value=320,Frame=vf.Frame,ViewGeneration=vf.ViewGeneration,DocumentGeneration=vf.DocumentGeneration,Revision=vf.Revision};
            view.Apply([Split(1),Split(2),Split(3)],[]);var resized=Draw();var resizedSize=editor.VerificationViewport;evidence.Check(resizedSize!=size,"formal viewport resized");evidence.Capture(resized,resizedSize.Width,resizedSize.Height,"resized",oracle);
            owner.RefreshAssets(true);owner.Assets?.PrepareStoredGenerationsAsync().GetAwaiter().GetResult();
            for(int cycle=0;cycle<3;cycle++){var play=owner.StartPlay();var playImage=Draw();evidence.Check(playImage.Length>0,"Play presents");play.Pause();owner.ReloadGameplay(context.GameplayAssemblyPath);evidence.Check(Draw().Length>0,"Reload presents");owner.StopPlay();evidence.Check(Draw().SequenceEqual(resized),"Stop restores Edit image");}
            var renderer=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");
            evidence.Check(renderer.EnvironmentStats.Live>0&&renderer.PipelineStats.GeometryDraws>0,"actual environment/geometry execution");
            if(skin){evidence.Check(renderer.SkinStats.Meshes>0&&renderer.SkinStats.Batches>0&&renderer.PipelineStats.ShadowDraws>0,"actual FBX skin and shadow draws");var play=owner.StartPlay();Draw();play.Pause();var pose1=Draw();bool moved=false;for(int i=0;i<64;i++){play.Step();var pose=Draw();moved|=!pose1.SequenceEqual(pose);}evidence.Check(moved,"actual animated FBX poses change image; tick="+play.Document.World.Tick+" state="+play.State);evidence.Check(play.Document.World.Tick==64,"animation uses explicit committed single steps");owner.StopPlay();Draw();}
            for(int i=0;i<16;i++)Draw(false);
            var beforeEnvironment=renderer.EnvironmentStats;var beforeResource=renderer.ResourceStats;var beforeScene=renderer.SceneStats;var beforePipeline=renderer.PipelineStats;
            long allocated=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();for(int i=0;i<32;i++)Draw(false);watch.Stop();allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
            var stats=renderer.Stats;var afterEnvironment=renderer.EnvironmentStats;
            evidence.Check(beforeEnvironment.UploadedBytes==afterEnvironment.UploadedBytes&&beforeEnvironment.Publications==afterEnvironment.Publications&&beforeEnvironment.Captures==afterEnvironment.Captures,"warm no environment publication/upload/readback");
            evidence.Check(beforeScene.UploadedBytes==renderer.SceneStats.UploadedBytes&&beforeResource.UploadedBytes==renderer.ResourceStats.UploadedBytes&&beforePipeline.Creates==renderer.PipelineStats.Creates,"warm no immutable mesh/texture upload or scene rebuild");
            evidence.Check(stats.ValidationErrors==0&&stats.ValidationWarnings==0,"DX11 API0/0");native=EnvironmentProductEvidence.NativePaths(root);
            costs=new{frames=32,allocatedBytes=allocated,cpuMilliseconds=watch.Elapsed.TotalMilliseconds,stats.SubmitMilliseconds,stats.PresentMilliseconds,stats.GpuSampleValid,stats.GpuMilliseconds,environment=afterEnvironment,resource=renderer.ResourceStats,pipeline=renderer.PipelineStats,skin=renderer.SkinStats};
        }
        evidence.Check(!Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Any(m=>m.ModuleName is "NcmaRenderer.dll" or "NcmaPlatform.dll" or "NcmaGui.dll"),"native product resources unloaded");DeploymentManifest.Validate(root);
        File.WriteAllText(Path.Combine(output,"probe.json"),JsonSerializer.Serialize(new{kind="editor",evidence.Checks,evidence.MaxPixelError,evidence.Frames,costs,native,sourceFree=!dynamic},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
    }
}

internal static class EnvironmentPlayerProductProbe
{
    internal static void Run(string requestPath)
    {

        using var playerRunOwner0 = new Ncma.Player.App.PlayerRunner();
        using var playerRunOwner1 = new Ncma.Player.App.PlayerRunner();
        using var json=JsonDocument.Parse(File.ReadAllBytes(requestPath));var r=json.RootElement;string root=r.GetProperty("productRoot").GetString()!,project=r.GetProperty("project").GetString()!,output=r.GetProperty("output").GetString()!;
        bool headless=r.GetProperty("kind").GetString()=="null",skin=r.GetProperty("skin").GetBoolean();var evidence=new EnvironmentProductEvidence();DeploymentManifest.Validate(root);var oracle=headless||skin?null:EnvironmentProductEvidence.Oracle(r,"oracle");
        var options=new PlayerOptions(project,headless,null,8,1d/60,30,Path.Combine(output,"player-report.json"),false,false);int captures=0;string[] native=[];
        var result=headless?playerRunOwner0.Run(options,visible:false):playerRunOwner1.RunForVerification(options,Path.Combine(root,"plugins"),(pixels,w,h)=>{if(captures++<8)evidence.Capture(pixels,w,h,"player "+captures,oracle);native=EnvironmentProductEvidence.NativePaths(root);});
        evidence.Check(result.ExitCode==0&&(headless?result.Tick==8:result.Tick>=8)&&result.ShutdownErrors.Length==0,"healthy fixed ticks/close; Null exact ticks");evidence.Check(result.ValidationErrors==0&&result.ValidationWarnings==0,"Player API0/0");
        if(headless)evidence.Check(result.Modules.Length==0&&result.RenderedFrames==0&&!Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Any(m=>m.ModuleName is "NcmaRenderer.dll" or "NcmaPlatform.dll" or "NcmaAnimationKernel.dll"),"Null has no native 3D/pose module");
        else evidence.Check(captures>0&&result.RenderedFrames>0,"actual Player image");
        DeploymentManifest.Validate(root);File.WriteAllText(Path.Combine(output,"probe.json"),JsonSerializer.Serialize(new{kind=headless?"null":"player",evidence.Checks,evidence.MaxPixelError,evidence.Frames,native,report=result},new JsonSerializerOptions{WriteIndented=true}));
    }
}
