using System.Text.Json;
using Ncma.Application;
using Ncma.Editor.App;
using Ncma.Gameplay;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Runtime;
using Ncma.Scene;

internal static unsafe partial class Program
{
    private static int PlayGraphicsRecoveryTests(string plugins,string output)
    {
        plugins=Path.GetFullPath(plugins);output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        var repo=new DirectoryInfo(plugins);while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent??throw new Exception("Repo missing");
        var fixture=new SkinFixture(output);var(document,camera,_)=fixture.Scene();global::Ncma.Runtime.Entity probe=document.World.CreateEntity("Script lifetime probe");document.World.Set(probe, TransformData.Identity);
        document.SetBindings(document.World.GetUuid(probe),[new(Guid.NewGuid(),"Ncma.Gameplay.UiFixture.RecoveryProbe",true,[])]);
        string configuration=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string gameplay=Path.Combine(repo.FullName,"managed/Ncma.Gameplay.UiFixture/bin",configuration,"net8.0/Ncma.Gameplay.UiFixture.dll");
        File.Copy(gameplay,Path.Combine(fixture.Root,"gameplay.dll"));File.Copy(Path.ChangeExtension(gameplay,".deps.json"),Path.Combine(fixture.Root,"gameplay.deps.json"));
        SceneDocumentFiles.Save(document,Path.Combine(fixture.Root,"start.ncmascene"));string path=Path.Combine(fixture.Root,"project.ncmaproject");
        File.WriteAllBytes(path,JsonSerializer.SerializeToUtf8Bytes(new ProjectConfiguration(1,fixture.Project,"Play recovery","start.ncmascene","gameplay.dll","Direct3D11",[],SceneCamera:camera),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        int cases=0;void Pass(bool ok,string label){Check(ok,"M7.6 Play graphics: "+label);cases++;}
        bool sabotage=false;PlaySession? captured=null;
        using var app=new ApplicationLifetime();var host=new CandidatePresentation(plugins,ProjectContext.Load(path),true,false,false,pipelineFactory:(e,a,s)=>{
            if(sabotage)Reject(()=>captured!.Pause());return new Scene3DPipeline(e,a,shadows:s);
        });app.Start([host]);var clock=new RecoveryClock();app.Run(clock,host,2);var owner=host.VerificationEditor;
        byte[] editBytes=owner.Document.CaptureBytes();var editWorld=owner.Document.World;var editState=owner.Edit!.State;
        foreach(bool paused in new[]{false,true}) {
            var play=captured=owner.StartPlay();app.Run(clock,host,18);if(paused)play.Pause();app.Run(clock,host,2);
            var world=play.Document.World;var status=play.Status;var history=owner.Edit.State;
            var animation=EnvironmentProductEvidence.Field<SceneRenderSession>(host,"_playScene").Animation!;
            TransformData Probe()=>world.Get<TransformData>(world.FindEntity(document.World.GetUuid(probe)));
            Pass(Probe().Position.X==play.Tick&&Probe().Position.Z==1&&Probe().Scale.Y==2,$"original private script and lifecycle counters: tick={play.Tick}, value={Probe()}");
            var stale=host.ReviewGraphicsRecovery();if(paused){play.Resume();play.Pause();}else play.AdvanceFrame(.005);
            // Running changes debt; Paused Pause/Resume returns equal numerical status, so use
            // an actual committed single step to invalidate its exact review as well.
            if(paused)play.Step();Reject(()=>host.RebuildGraphics(stale));
            status=play.Status;sabotage=true;Reject(()=>host.RebuildGraphics(host.ReviewGraphicsRecovery()));sabotage=false;
            Pass(play.Status==status&&ReferenceEquals(world,play.Document.World)&&host.GraphicsPhase==EditorGraphicsPhase.Failed,"caught Play control reentry poisons preparation without changing gameplay");
            host.RebuildGraphics(host.ReviewGraphicsRecovery());
            Pass(play.Status==status&&ReferenceEquals(animation,EnvironmentProductEvidence.Field<SceneRenderSession>(host,"_playScene").Animation),"fresh approved retry retains exact animation and accumulator");
            byte[]? before=null;host.CaptureNextCompositeViewportForVerification((p,_,_)=>{before=p;throw new InvalidOperationException("test-frame-failure");});
            int prompts=0;PlayStatus? approved=null;
            var original=EnvironmentProductEvidence.Field<RendererSession>(host,"_renderer");var held=paused?original.CreateViewTarget(16,16):null;
            ulong attempts=EditorRunLoop.Run(app,host,clock,4,request=>{
                prompts++;approved??=play.Status;
                Pass(request.Review.Play==play.Status&&ReferenceEquals(play,owner.Play)&&owner.Edit.State==history,"exact active Play review and frozen Edit history");
                Pass(play.Status==approved&&request.Message.Contains("不重启脚本"),"confirmation/retry does not advance or restart Play");
                if(request.Retry){Pass(paused&&prompts==2&&!original.IsClosed,"actual retained GPU lease needs fresh retry");held!.Dispose();}
                clock.Value+=600;return true;
            });
            Pass(attempts==4&&prompts==(paused?2:1)&&app.State==ApplicationState.Running&&original.IsClosed,"same formal run loop resumes after explicit approval");
            Pass(ReferenceEquals(play,owner.Play)&&ReferenceEquals(world,play.Document.World)&&ReferenceEquals(editWorld,owner.Document.World)&&editBytes.SequenceEqual(owner.Document.CaptureBytes()),"independent Play/Edit World instances preserved");
            Pass(Probe().Position.X==play.Tick&&Probe().Position.Z==1&&Probe().Scale.Y==2,"no script reset or duplicate lifecycle after resumed frames");
            if(paused){
                Pass(play.Status==approved,"paused frame count/tick/debt unchanged across fault and resumed presentation");
                byte[]? after=null;host.CaptureNextCompositeViewportForVerification((p,_,_)=>after=p);app.Run(clock,host,1);
                Pass(before is not null&&after is not null&&before.SequenceEqual(after),"paused animated final viewport exact after replacement");
            }else Pass(play.Tick>=approved!.Tick&&play.Tick-approved.Tick<=4&&play.Status.TotalDroppedSeconds==approved.TotalDroppedSeconds,"no modal time jump or repeated failed simulation frame");
            play.Pause();var oldTick=play.Tick;play.Step();app.Run(clock,host,1);Pass(play.Tick==oldTick+1&&Probe().Position.X==play.Tick,"retained script and animation observer single-step once");
            var renderer=EnvironmentProductEvidence.Field<RendererSession>(host,"_renderer");Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"API0/0");
            owner.StopPlay();app.Run(clock,host,2);Pass(owner.Play is null&&owner.Edit.State==editState&&editBytes.SequenceEqual(owner.Document.CaptureBytes()),"ordinary Stop still restores original Edit state");
        }
        app.Dispose();Pass(app.RetainedServiceCount==0&&!PinLocked(fixture.GenerationPath),"all final resources and CPU pins drained");
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{cases,actualDx11=true,formalLoop=true,runningAndPaused=true,privateScriptState=true,interactiveHud=false,physicalDeviceLoss=false,wholeStageAccepted=false}));
        Console.WriteLine($"PASS M7.6 Play graphics recovery {cases} checks; {output}");return 0;
    }
}
