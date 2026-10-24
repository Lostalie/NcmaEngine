using System.Text.Json;
using Ncma.Player.App;

internal static unsafe partial class Program
{
    private static void PlayerWindowChecks(PlayerOptions original,string plugins,string output,Action<bool,string> pass)
    {
        var initial=new PlayerWindowSize(1920,1080);
        var observation=new PlayerOutputObservation(new(1280,720),initial);
        var empty=observation.Capture();
        pass(empty.SuccessfulPresents==0&&empty.LastPresentedSize is null&&!empty.AllPresentSizesMatchRequested,
            "output observation never invents a rendered frame from startup size");
        observation.Presented(1920,1080);var retained=observation.Capture();
        observation.Presented(1280,720);observation.Presented(1280,720);
        var changed=observation.Capture();
        pass(retained is{SuccessfulPresents:1,SizeTransitions:0,PresentsMatchingRequestedWindow:0}&&
            changed is{SuccessfulPresents:3,SizeTransitions:1,PresentsMatchingRequestedWindow:2}&&
            !changed.AllPresentSizesMatchRequested&&!changed.TargetPerformanceAccepted,
            "DPI/resize mismatch remains explicit and snapshots are owned");
        Reject(()=>observation.Presented(0,1080));
        Reject(()=>observation.Presented(1920,8193));
        pass(observation.Capture()==changed,"invalid output size cannot partially change evidence");
        foreach(uint minor in new uint[]{0,2}) {
            using var loader=new Ncma.Interop.PluginLoader();
            Reject(()=>loader.Load(plugins,[new("unknown-platform",Ncma.Interop.ModuleKind.Platform,"NcmaPlatform.dll","NcmaPlatform.dll",1,3,[])]));
            loader.Load(plugins,[new("pixel-platform",Ncma.Interop.ModuleKind.Platform,"NcmaPlatform.dll","NcmaPlatform.dll",1,minor,[])]);
            var module=loader.Modules.Single();
            Reject(()=>new Ncma.Platform.PlatformWindow(module,"invalid mode",64,64,false,(Ncma.Platform.WindowMode)2));
            if(minor==0) {
                Reject(()=>new Ncma.Platform.PlatformWindow(module,"explicit version required",64,64,false,Ncma.Platform.WindowMode.BorderlessPixels));
                pass(module.Status.LiveResources==0&&module.OutstandingLeases==0,"old table never silently substitutes windowed pixels");
            } else {
                Reject(()=>new Ncma.Platform.PlatformWindow(module,"invalid size",0,64,false,Ncma.Platform.WindowMode.BorderlessPixels));
                pass(module.Status.LiveResources==0&&module.OutstandingLeases==0,"invalid pixel creation releases managed lease");
                using(var timer=new Ncma.Platform.PlatformPrecisionWait(module)) {
                    using(var window=new Ncma.Platform.PlatformWindow(module,"explicit pixels",64,64,false,Ncma.Platform.WindowMode.BorderlessPixels)) {
                        pass(window.State.FramebufferWidth==64&&window.State.FramebufferHeight==64&&module.Status.LiveResources==2,
                            "managed Platform1.2 composes independent timer and exact pixel window");
                    }
                    timer.WaitMicroseconds(1);
                }
                pass(module.Status.LiveResources==0&&module.OutstandingLeases==0,"pixel wrapper and timer close without native resources or leases");
            }
        }
        foreach(var size in new[]{new PlayerWindowSize(1920,1080),new PlayerWindowSize(853,479)}) {
            using var runner=new PlayerRunner();
            int captures=0;PlayerWindowSize? pixels=null;
            var options=original with {WindowSize=size,FrameRateLimit=30,Ticks=6,
                Report=Path.Combine(output,$"player-size-{size.Width}-{size.Height}.json")};
            var report=runner.RunForVerification(options,plugins,(bytes,w,h)=>{
                Check(bytes.Length==checked((int)(w*h*4)),"capture byte extent");
                if(pixels is not null)Check(pixels==new PlayerWindowSize(w,h),"bounded fixture dimensions stable");
                pixels=new(w,h);captures++;
            });
            pass(report.ExitCode==0&&report.ValidationErrors==0&&report.ValidationWarnings==0&&captures>0,
                $"actual sized Player {size} presents successfully");
            var observed=report.Output;
            pass(observed is not null&&observed.RequestedWindowSize==size&&
                observed.InitialFramebufferSize==pixels&&observed.LastPresentedSize==pixels&&
                observed.SuccessfulPresents==(ulong)captures&&observed.SuccessfulPresents==report.LoopCosts!.PresentReturns&&
                observed.SizeTransitions==0&&observed.AllPresentSizesMatchRequested==(pixels==size),
                "requested window, actual readback and successful presents agree without inferring DPI");
            using var saved=JsonDocument.Parse(File.ReadAllBytes(options.Report));
            var durable=saved.RootElement.GetProperty("output");
            pass(durable.GetProperty("version").GetInt32()==1&&
                durable.GetProperty("requestedWindowSize").GetProperty("width").GetUInt32()==size.Width&&
                durable.GetProperty("lastPresentedSize").GetProperty("width").GetUInt32()==pixels!.Value.Width&&
                !durable.GetProperty("targetPerformanceAccepted").GetBoolean(),"actual output evidence survives shutdown in JSON");
        }
        foreach(var size in new[]{new PlayerWindowSize(1920,1080),new PlayerWindowSize(853,479)}) {
            using var runner=new PlayerRunner();int captures=0;
            var options=original with{WindowSize=size,WindowMode=Ncma.Platform.WindowMode.BorderlessPixels,
                FrameRateLimit=30,Ticks=6,Report=Path.Combine(output,$"player-borderless-{size.Width}-{size.Height}.json")};
            var report=runner.RunForVerification(options,plugins,(bytes,w,h)=>{
                Check(w==size.Width&&h==size.Height&&bytes.Length==checked((int)(w*h*4)),"exact borderless readback");
                captures++;
            });
            pass(report.ExitCode==0&&report.ValidationErrors==0&&report.ValidationWarnings==0&&captures>0&&
                report.Modules.Single(m=>m.Id=="ncma.platform").AbiMinor==2,"actual pixel Player selects explicit Platform 1.2");
            pass(report.Output is{AllPresentSizesMatchRequested:true,RequestedMode:Ncma.Platform.WindowMode.BorderlessPixels}&&
                report.Output.InitialFramebufferSize==size&&report.Output.LastPresentedSize==size&&
                report.Output.SuccessfulPresents==(ulong)captures&&report.Output.SuccessfulPresents==report.LoopCosts!.PresentReturns,
                "exact framebuffer is observed rather than inferred from request");
            using var saved=JsonDocument.Parse(File.ReadAllBytes(options.Report));
            pass(saved.RootElement.GetProperty("output").GetProperty("requestedMode").GetString()=="BorderlessPixels",
                "explicit requested pixel mode survives durable report");
        }
        using var invalidHost=new PlayerPresentation(plugins,false,windowSize:new(0,720));
        Reject(()=>invalidHost.Start());
        pass(invalidHost.CaptureOutput() is null,"invalid direct host size rejected before module/window allocation");
        var fixture=new SceneGpuFixture(output);var(document,camera,_,_)=fixture.Scene();
        using var assets=Ncma.Scene.Rendering.SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,document.CaptureSnapshot(),true);
        foreach(var mode in new[]{Ncma.Platform.WindowMode.Windowed,Ncma.Platform.WindowMode.BorderlessPixels}) {
        var runtimeScene=document.BakeRuntime(document.Revision).Image.Instantiate(document.Components);
        using var play=new Ncma.Gameplay.PlaySession(runtimeScene);
        using var host=new PlayerPresentation(plugins,false,windowSize:new(512,288),windowMode:mode);
        host.Start();host.PrepareBeforeGameplay(runtimeScene,assets);
        play.Start(_=>throw new Exception("No behaviour expected"));host.BindScene(play,assets,camera);
        play.Pause();
        byte[] Draw() {
            byte[]? result=null;
            host.Pump(play);host.Present(play,(data,w,h)=>{
                Check(host.CaptureOutput()!.InitialFramebufferSize==new PlayerWindowSize(w,h),"recovered framebuffer matches initial extent");
                result=data;
            });
            return result??throw new Exception("No output");
        }
        var before=Draw();var state=play.Status;var first=host.CaptureOutput()!;
        host.RebuildGraphics(host.ReviewGraphicsRecovery());
        pass(before.SequenceEqual(Draw())&&play.Status==state,
            "nondefault output retains exact scene pixels and paused gameplay through explicit graphics recovery");
        var after=host.CaptureOutput()!;
        pass(first.SuccessfulPresents==1&&after.SuccessfulPresents==2&&after.SizeTransitions==0&&
            after.RequestedWindowSize==new PlayerWindowSize(512,288)&&after.LastPresentedSize==first.LastPresentedSize&&
            after.RequestedMode==mode,
            "output evidence accumulates across renderer generations without pretending to restart");
        }
    }
}
