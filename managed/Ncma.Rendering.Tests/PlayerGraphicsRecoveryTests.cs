using System.Text.Json;
using Ncma.Application;
using Ncma.Gameplay;
using Ncma.Player.App;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    private static int PlayerGraphicsRecoveryTests(string plugins,string output)
    {
        plugins=Path.GetFullPath(plugins);output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        var repo=new DirectoryInfo(plugins);while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent??throw new Exception("Repo missing");
        int cases=0;void Pass(bool ok,string name){Check(ok,"M7.6 Player recovery: "+name);cases++;}
        var fixture=new SkinFixture(output);var(document,camera,_)=fixture.Scene();
        using(var assets=SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,document.CaptureSnapshot(),true))
        using(var play=new PlaySession(document)) {
            bool reenter=false;
            using var host=new PlayerPresentation(plugins,false,(e,a,s)=>{if(reenter)Reject(()=>play.Pause());return new Scene3DPipeline(e,a,shadows:s);});
            host.Start();host.PrepareBeforeGameplay(document,assets);play.Start(_=>throw new Exception("No behaviour expected"));host.BindScene(play,assets,camera);
            for(int i=0;i<36;i++)play.AdvanceFrame(1d/60);play.Pause();
            byte[] Draw(){byte[]? pixels=null;host.Pump(play);host.Present(play,(p,_,_)=>pixels=p);return pixels??throw new Exception("No Player capture");}
            var before=Draw();var scene=EnvironmentProductEvidence.Field<SceneRenderSession>(host,"_scene");var animation=scene.Animation;
            var status=play.Status;var bytes=document.CaptureBytes();var world=document.World;
            var stale=host.ReviewGraphicsRecovery();play.Step();Reject(()=>host.RebuildGraphics(stale));
            Pass(play.Tick==status.Tick+1,"stale review cannot reset committed gameplay");status=play.Status;before=Draw();
            var review=host.ReviewGraphicsRecovery();Task.Run(()=>Reject(()=>host.RebuildGraphics(review))).GetAwaiter().GetResult();
            host.RebuildGraphics(review);Pass(ReferenceEquals(animation,EnvironmentProductEvidence.Field<SceneRenderSession>(host,"_scene").Animation)&&play.Status==status,"exact CPU animation and paused clock retained");
            Pass(before.SequenceEqual(Draw())&&ReferenceEquals(world,document.World),"exact final skinned pixels and original World");Reject(()=>host.RebuildGraphics(review));
            reenter=true;Reject(()=>host.RebuildGraphics(host.ReviewGraphicsRecovery()));
            Pass(play.Status==status,"caught Play control reentry rejects preparation without advancing simulation");reenter=false;
            host.RebuildGraphics(host.ReviewGraphicsRecovery());Pass(before.SequenceEqual(Draw()),"fresh explicit approval drains partial replacement and restores pixels");
            var old=EnvironmentProductEvidence.Field<RendererSession>(host,"_renderer");using var held=old.CreateViewTarget(16,16);
            host.Pump(play);Reject(()=>host.Present(play,(_,_,_)=>throw new InvalidOperationException("private-player-frame")));
            var diagnostic=host.FirstGraphicsFailure;Pass(diagnostic is not null&&old.ReadFrameState().Active,"first fault copied from actual submitted frame");
            Reject(()=>host.Pump(play));Reject(()=>host.ReviewGraphicsRecovery());
            Reject(()=>host.RebuildGraphics(host.ReviewGraphicsRecovery(true)));
            var nativeDiagnostic=old.ReadFirstFailure();
            Pass(nativeDiagnostic is {Operation:(uint)GpuFailureOperation.ExplicitAbandon,NativeCode:null,DeviceReason:0}&&host.FirstNativeFailure==nativeDiagnostic,"approved abandonment copied before failed-close retry");
            Pass(!old.IsClosed&&ReferenceEquals(animation,EnvironmentProductEvidence.Field<SceneAnimationSession>(host,"_retainedAnimation"))&&play.Status==status,"failed retirement retains original device and gameplay");
            held.Dispose();host.RebuildGraphics(host.ReviewGraphicsRecovery(true));
            Pass(host.FirstNativeFailure==nativeDiagnostic,"first numeric diagnosis survives fresh renderer generation");
            Pass(old.IsClosed&&host.FirstGraphicsFailure==diagnostic&&before.SequenceEqual(Draw()),"fresh retry retains first fault and exact animated image");
            play.Step();Draw();Pass(play.Tick==status.Tick+1,"original animation observer continues once");
            var final=EnvironmentProductEvidence.Field<RendererSession>(host,"_renderer");using var finalLease=final.CreateViewTarget(16,16);
            Reject(host.Dispose);Reject(()=>host.ReviewGraphicsRecovery());Pass(!final.IsClosed,"terminal failed shutdown never reopens gameplay presentation");
            finalLease.Dispose();host.Dispose();Pass(final.IsClosed,"explicit terminal close retry drains renderer");
            Pass(host.FirstNativeFailure==nativeDiagnostic,"owned numeric diagnosis survives plugin unload");
        }
        // Actual catalog-loaded SDK-only private-state behaviour through the ordinary runner.
        var(runDocument,runCamera,_)=fixture.Scene();var probe=runDocument.World.CreateObject("Private recovery state");probe.Set(TransformData.Identity);
        runDocument.SetBindings(probe.PersistentId,[new(Guid.NewGuid(),"Ncma.Gameplay.UiFixture.RecoveryProbe",true,[])]);
        string config=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string script=Path.Combine(repo.FullName,"managed/Ncma.Gameplay.UiFixture/bin",config,"net8.0/Ncma.Gameplay.UiFixture.dll");
        File.Copy(script,Path.Combine(fixture.Root,"gameplay.dll"));File.Copy(Path.ChangeExtension(script,".deps.json"),Path.Combine(fixture.Root,"gameplay.deps.json"));
        SceneDocumentFiles.Save(runDocument,Path.Combine(fixture.Root,"start.ncmascene"));string project=Path.Combine(fixture.Root,"project.ncmaproject");
        File.WriteAllBytes(project,JsonSerializer.SerializeToUtf8Bytes(new ProjectConfiguration(1,fixture.Project,"Player recovery","start.ncmascene","gameplay.dll","Direct3D11",[],SceneCamera:runCamera),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        using(var runner=new PlayerRunner()){
            PlaySession? original=null;Guid session=default,world=default;ulong tick=0;bool failed=false;int prompts=0;GpuViewTarget? held=null;PlayStatus? reviewed=null;
            var report=runner.RunForVerification(new(project,false,null,18,1d/60,60,Path.Combine(output,"runner.json"),false,false),plugins,
                (_,_,_)=>{if(!failed&&held is not null){failed=true;throw new Exception("runner-private-capture");}},
                trustedInput:(play,_)=>{original??=play;if(session==Guid.Empty){session=play.SessionId;world=play.Document.World.Identity;}var value=play.Document.World.FindObject(probe.PersistentId).Get<TransformData>();Check(ReferenceEquals(original,play)&&play.SessionId==session&&play.Document.World.Identity==world&&play.Tick>=tick&&value.Position.X==play.Tick&&value.Position.Z==1&&value.Scale.Y==2,"Runner retained private script state");tick=play.Tick;
                    if(!failed&&held is null&&play.Tick>=6)held=EnvironmentProductEvidence.Field<RendererSession>(EnvironmentProductEvidence.Field<PlayerPresentation>(runner,"presentation"),"_renderer").CreateViewTarget(16,16);},
                confirmGraphicsRecovery:request=>{prompts++;reviewed??=original!.Status;Pass(request.Review.Play==original!.Status&&original.Status==reviewed&&request.Message.Contains("不重启脚本"),"runner exact approval preserves status during retry");if(request.Retry){held!.Dispose();held=null;}return true;});
            Pass(report.ExitCode==0&&failed&&prompts==2&&report.Tick>=18&&report.SessionId==session,$"actual PlayerRunner resumes same session after explicit failed-close retry: exit={report.ExitCode}, failed={failed}, prompts={prompts}, tick={report.Tick}, session={report.SessionId==session}");
            Pass(report.ValidationErrors==0&&report.ValidationWarnings==0&&runner.LastPresentationFailure is not null&&runner.Status.Phase==PlayerRunnerPhase.Idle,"cumulative API0/0, original failure retained and normal final shutdown");
            Pass(runner.LastNativeFailureDiagnostic is {Operation:(uint)GpuFailureOperation.ExplicitAbandon,NativeCode:null,DeviceReason:0},"runner retains numeric diagnosis after final shutdown without retaining exception or DLL");
            Pass(!PinLocked(fixture.GenerationPath),"runner final CPU source pins drained");
        }
        foreach(string mode in new[]{"decline","throw","reenter"}) {
            using var runner=new PlayerRunner();int prompts=0,captures=0;
            var report=runner.RunForVerification(new(project,false,null,2,1d/60,30,Path.Combine(output,mode+".json"),false,false),plugins,
                (_,_,_)=>{captures++;throw new InvalidOperationException("negative-player-frame");},
                confirmGraphicsRecovery:_=>{prompts++;if(mode=="throw")throw new InvalidOperationException("private-confirmation");if(mode=="reenter"){Reject(runner.Dispose);return true;}return false;});
            Pass(report.ExitCode!=0&&prompts==1&&captures==1&&runner.Status.Phase==PlayerRunnerPhase.Retained,"declined/throwing/reentrant approval never resumes or hides shutdown owner "+mode);
            runner.AbandonFrameAndRetryShutdown();Pass(runner.Status.Phase==PlayerRunnerPhase.Idle&&!PinLocked(fixture.GenerationPath),"negative approval explicit shutdown drains original owners "+mode);
            Pass(runner.LastNativeFailureDiagnostic is {Operation:(uint)GpuFailureOperation.ExplicitAbandon,NativeCode:null,DeviceReason:0},"shutdown-only retry also retains first native observation "+mode);
        }
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{cases,actualDx11=true,privateScriptState=true,samePlayerSession=true,scriptedConfirmation=true,physicalDeviceLoss=false,wholeStageAccepted=false}));
        Console.WriteLine($"PASS M7.6 Player graphics recovery {cases} checks; {output}");return 0;
    }
}
