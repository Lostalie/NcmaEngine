using System.Text.Json;
using System.Numerics;
using Ncma.Application;
using Ncma.Application.Runtime;
using Ncma.Editor.App;
using Ncma.Gui;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Player.App;
using Ncma.Rendering;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    private static int WindowSurfaceTests(string plugins,string output)
    {
        plugins=Path.GetFullPath(plugins);output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        var repo=new DirectoryInfo(plugins);while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent??throw new Exception("Repo missing");
        var fixture=new SceneGpuFixture(output);var(document,camera,_,_)=fixture.Scene();
        string gameplay=Path.Combine(repo.FullName,"out/managed/Ncma.Gameplay.Sample.dll");
        File.Copy(gameplay,Path.Combine(fixture.Root,"gameplay.dll"));File.Copy(Path.ChangeExtension(gameplay,".deps.json"),Path.Combine(fixture.Root,"gameplay.deps.json"));
        string scenePath=Path.Combine(fixture.Root,"start.ncmascene"),projectPath=Path.Combine(fixture.Root,"project.ncmaproject");
        SceneDocumentFiles.Save(document,scenePath);
        File.WriteAllBytes(projectPath,JsonSerializer.SerializeToUtf8Bytes(new ProjectConfiguration(1,fixture.Project,"Window surface","start.ncmascene","gameplay.dll","Direct3D11",[],SceneCamera:camera),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        int cases=0;void Pass(bool ok,string label){Check(ok,"M7.6 window surface: "+label);cases++;}
        using(var loader=new PluginLoader()) {
            loader.Load(plugins,Specs().Take(2).ToArray());var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
            using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Window policy",64,64,false);
            var state=window.State;state.FramebufferWidth=state.FramebufferHeight=64;state.Minimized=0;
            ulong oldGeneration;
            using(var renderer=new RendererSession(native,window,64,64)) {
                var surface=new RendererWindowSurface(renderer);oldGeneration=surface.Status.RendererGeneration;
                Pass(surface.Prepare(state)&&surface.Status is{Phase:RenderWindowPhase.Ready,Width:64,Height:64},"shared policy binds exact current generation");
                Task.Run(()=>Reject(()=>surface.Prepare(state))).GetAwaiter().GetResult();
                Pass(surface.Status.Phase==RenderWindowPhase.Ready,"foreign-thread request changes no owner state");Reject(surface.CompleteShutdown);
                var plan=new ClearPipeline(.2f,.3f,.4f).Build(64,64).Compile(renderer.Capabilities);
                renderer.Submit(plan,null,RenderFrame.Reference(1,64,64));renderer.Present();
                var before=renderer.Stats;var resources=renderer.ResourceStats;
                var zero=state;zero.FramebufferWidth=0;
                for(int i=0;i<32;i++)Pass(!surface.Prepare(zero)&&surface.Status is{Phase:RenderWindowPhase.Suspended,Width:64,Height:64},"zero width suspends without zero-size resize");
                Pass(renderer.Stats.Presents==before.Presents&&renderer.ResourceStats.Creates==resources.Creates,"suspend has no GPU work or resource construction");
                Pass(surface.Prepare(state),"same-size resume uses the existing generation");
                for(int i=0;i<32;i++)surface.Prepare(state);
                long allocated=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<64;i++)surface.Prepare(state);allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
                Pass(allocated==0,"warm shared surface policy allocates zero managed bytes");
                var bad=state;bad.FramebufferWidth=4097;PluginException? invalid=null;
                try{surface.Prepare(bad);}catch(PluginException error){invalid=error;}
                var failed=surface.Status;
                Pass(invalid is{Result:PluginResult.InvalidArgument,Phase:"resize"}&&failed is{Phase:RenderWindowPhase.Faulted,Width:64,Height:64,RequestedWidth:4097,RequiresNewGeneration:false},"actual native resize error retains committed dimensions and requested diagnostic");
                Pass(failed.Failure is{NativeResult:PluginResult.InvalidArgument,Operation:"resize"}&&renderer.Stats.Width==64,"copied exact native cause survives failed preparation");
                Reject(()=>surface.Prepare(state));Pass(surface.Status==failed,"normal loop does not retry a retained fault");
                renderer.Submit(plan,null,RenderFrame.Reference(2,64,64));Reject(()=>surface.Recover(state));
                Pass(surface.Status.Failure==failed.Failure&&surface.Status.Phase==RenderWindowPhase.Faulted,"frame-active retry preserves the original diagnostic");renderer.Present();
                var resized=state;resized.FramebufferWidth=311;resized.FramebufferHeight=193;
                Pass(surface.Recover(resized)&&surface.Status is{Width:311,Height:193,Phase:RenderWindowPhase.Ready}&&surface.Status.Failure==failed.Failure,"explicit healthy off-frame retry performs actual odd-size resize without losing evidence");
                var large=new ClearPipeline(.2f,.3f,.4f).Build(311,193).Compile(renderer.Capabilities);
                renderer.Submit(large,null,RenderFrame.Reference(3,311,193));renderer.Capture(new byte[311*193*4]);renderer.Present();
                renderer.Submit(large,null,RenderFrame.Reference(4,311,193));surface.RecordFailure(new InvalidOperationException("private-secret-render-callback"));
                var original=surface.Status.Failure;Reject(()=>surface.Recover(resized));renderer.AbandonFrameForShutdown(4);Reject(()=>surface.Recover(resized));
                Pass(surface.Status.RequiresNewGeneration&&surface.Status.Failure==original&&!JsonSerializer.Serialize(surface.Status).Contains("private-secret"),"terminal renderer cannot resume and diagnostic does not retain callback text");
                surface.BeginShutdown();Reject(()=>surface.Prepare(state));Reject(surface.CompleteShutdown);
                renderer.Dispose();surface.CompleteShutdown();Pass(surface.Status.Phase==RenderWindowPhase.Closed&&surface.Status.Failure==original,"ordinary resource shutdown preserves copied first fault");Reject(()=>surface.Recover(state));
            }
            using(var fresh=new RendererSession(native,window,64,64,pureUi:true)) {
                var surface=new RendererWindowSurface(fresh);
                Pass(surface.Status.RendererGeneration!=oldGeneration&&surface.Prepare(state),"fresh native renderer generation, not resurrection of the failed device");
                var hidden=state;hidden.Minimized=1;
                Pass(!surface.Prepare(hidden)&&surface.Prepare(state)&&fresh.Stats.LiveGroups==0&&fresh.ResourceStats.Targets==0&&fresh.Stats.SubmittedFrames==0,"pure 2D surface lifecycle initializes no 3D groups or target/frame");
                Pass(fresh.Stats.ValidationErrors==0&&fresh.Stats.ValidationWarnings==0,"pure 2D lifecycle API0/0");
                surface.BeginShutdown();fresh.Dispose();surface.CompleteShutdown();
            }
            Pass(native.Status.LiveResources==0,"native renderer resources drained before module unload");
        }
        using(var editor=new CandidatePresentation(plugins,ProjectContext.Load(projectPath),true,false,false)) {
            editor.Start();ulong frame=0;
            void Draw(WindowState? input=null) {
                Pass(editor.PumpPlatform(),"actual Editor pump");if(input is {} state)editor.SetWindowInputForVerification(state,[]);
                var f=new FrameContext(++frame,0);editor.ApplyEditorIntents(f);editor.PumpAgent(f);editor.BeginPresentation(f);editor.SubmitInput(f);editor.AdvancePlay(f);editor.Present(f);
            }
            Draw();Draw();
            var renderer=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");
            var target=EnvironmentProductEvidence.Field<GpuViewTarget>(editor,"_viewportTarget");
            var window=EnvironmentProductEvidence.Field<PlatformWindow>(editor,"_window");
            var state=window.State;var before=renderer.Stats;var resources=renderer.ResourceStats;var token=target.ImageToken;
            var surface=EnvironmentProductEvidence.Field<RendererWindowSurface>(editor,"_windowSurface");
            byte[] initial=editor.VerificationEditor.Document.CaptureBytes();
            state.Minimized=1;state.FramebufferWidth=state.FramebufferHeight=0;Draw(state);
            Pass(renderer.ResourceStats.Targets==resources.Targets,"minimize retains Editor viewport resources instead of destroying them");
            Pass(ReferenceEquals(target,EnvironmentProductEvidence.Field<GpuViewTarget>(editor,"_viewportTarget"))&&target.ImageToken==token,
                "minimize retains the same Editor viewport target instead of destroying its last valid resources");
            Pass(renderer.Stats.SubmittedFrames==before.SubmittedFrames&&renderer.Stats.Presents==before.Presents&&renderer.ResourceStats.Targets==resources.Targets,"minimize submits/presents/allocates no GPU frame or target");
            Pass(surface.Status.Phase==RenderWindowPhase.Suspended,"Editor consumes the shared suspension policy");
            state.Minimized=0;state.FramebufferHeight=720;Draw(state); // zero width without the minimized flag
            Pass(renderer.Stats.Presents==before.Presents&&target.ImageToken==token,"Editor zero framebuffer also suspends and retains targets");
            state=window.State;Draw(state);
            Pass(surface.Status.Phase==RenderWindowPhase.Ready&&target.ImageToken==token&&renderer.Stats.Presents==before.Presents+1,"Editor resume keeps target identity and presents once");
            Pass(initial.SequenceEqual(editor.VerificationEditor.Document.CaptureBytes()),"surface transitions do not mutate the World or history");
            // Real GUI image pin makes retiring the old viewport fail. No field
            // attachment, synthetic Dispose exception or reflective recovery.
            var gui=EnvironmentProductEvidence.Field<GuiSession>(editor,"_gui");
            state.Width=640;state.Height=360;state.FramebufferWidth=960;state.FramebufferHeight=540;state.ScaleX=state.ScaleY=1.5f;
            editor.SetWindowInputForVerification(state,[]);var f=new FrameContext(++frame,0);editor.ApplyEditorIntents(f);editor.PumpAgent(f);editor.BeginPresentation(f);editor.SubmitInput(f);editor.AdvancePlay(f);
            GuiItem[] items=[new(){Kind=(uint)GuiItemKind.PanelBegin,Enabled=1,WidgetHigh=11,WidgetLow=1},GuiItem.Image(11,2,token,0,0,32,32),new(){Kind=(uint)GuiItemKind.PanelEnd}];
            items[0].Rect[2]=items[0].Rect[3]=64;
            var pinFrame=editor.VerificationPresenter.Frame;pinFrame.Frame=frame;pinFrame.ItemCount=3;pinFrame.TextBytes=0;
            gui.Draw(pinFrame,items,[]);
            PluginException? pinned=null;try{editor.Present(f);}catch(PluginException error){pinned=error;}
            var pending=EnvironmentProductEvidence.Field<GpuViewTarget>(editor,"_pendingViewportTarget");var pendingToken=pending.ImageToken;
            Pass(pinned is{Result:PluginResult.Busy}&&ReferenceEquals(target,EnvironmentProductEvidence.Field<GpuViewTarget>(editor,"_viewportTarget"))&&renderer.ResourceStats.Targets==resources.Targets+1,"real pinned old viewport close retains both old and candidate GPU owners");
            var fault=surface.Status.Failure;Reject(()=>editor.Present(f));
            Pass(pending.ImageToken==pendingToken&&renderer.ResourceStats.Targets==resources.Targets+1&&surface.Status.Failure==fault,"failed host cannot overwrite retained candidate or original cause");
            Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"Editor resized/pinned-failure API0/0");
            editor.Dispose();Pass(surface.Status.Phase==RenderWindowPhase.Closed&&surface.Status.Failure==fault,"Editor orderly shutdown releases GUI pin before both targets");Reject(()=>_=target.ImageToken);Reject(()=>_=pending.ImageToken);
        }
        using(var pins=SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,document.CaptureSnapshot(),true))
        using(var owner=new RuntimeSessionOwner(document))
        using(var player=new PlayerPresentation(plugins,false)) {
            player.Start();player.PrepareBeforeGameplay(document,pins);owner.LoadGameplay(Path.Combine(fixture.Root,"gameplay.dll"));var play=owner.StartPlay();player.BindScene(play,pins,camera);
            var window=EnvironmentProductEvidence.Field<PlatformWindow>(player,"_window");var state=window.State;state.Focused=1;
            player.ProcessInput(play,state,[]);byte[]? baseline=null;player.Present(play,(p,_,_)=>baseline=p);var before=player.Stats;
            state.Minimized=1;state.FramebufferWidth=state.FramebufferHeight=0;state.Held[1]=1UL<<(87%64); // forced focus/held W while hidden
            player.ProcessInput(play,state,[]);player.Present(play,(_,_,_)=>throw new Exception("Hidden capture"));
            Pass(player.SurfaceStatus is{Phase:RenderWindowPhase.Suspended}&&player.Stats.Presents==before.Presents&&EnvironmentProductEvidence.Field<ulong[]>(player,"_held").All(v=>v==0),"Player hidden frame suppresses rendering and physical gameplay input without forcing Play pause");
            state=window.State;player.ProcessInput(play,state,[]);byte[]? restored=null;player.Present(play,(p,_,_)=>restored=p);
            Pass(baseline is not null&&restored is not null&&baseline.SequenceEqual(restored)&&player.Stats.Presents==before.Presents+1,"Player resume restores exact static scene pixels in the same generation");
            state.FramebufferWidth=311;state.FramebufferHeight=193;player.ProcessInput(play,state,[]);player.Present(play,(p,w,h)=>Pass(w==311&&h==193&&p.Length==311*193*4,"Player actual odd-size render/capture"));
            Pass(player.SurfaceStatus is{Width:311,Height:193,Phase:RenderWindowPhase.Ready}&&player.Stats.ValidationErrors==0&&player.Stats.ValidationWarnings==0,"Player shared resize policy/API0/0");
            player.Dispose();Pass(player.SurfaceStatus?.Phase==RenderWindowPhase.Closed,"Player surface closes only after renderer shutdown");
        }
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{cases,syntheticWindowObservations=true,manualWindowAcceptance=false,deviceRecoveryAccepted=false}));
        Console.WriteLine($"PASS M7.6 window surface {cases} checks; {output}");return 0;
    }
}
