using System.Text.Json;
using Ncma.Animation;
using Ncma.Characters;
using Ncma.Gameplay;
using Ncma.Physics;
using Ncma.Player.App;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Scene.Rendering;
using Ncma.Runtime;
using Ncma.Platform;
using Ncma.Ui;
using Ncma.Ui.Rendering;
using Ncma.Application.Runtime;

internal static class GraphicsRecoveryJointPlayerProbe
{
    internal static void Run(string requestPath)
    {
        using var e=new UiPublicationProductEvidence(requestPath);string[] native=[];var costs=new RelocatedPresentationCosts(e);
        var quality=GraphicsRecoveryJointState.ShadowQuality(e.Project);uint shadowResolution=SceneShadowPresets.Get(quality).Budget.Resolution;
        if(e.Kind=="nullPhysics"){
            using var runner=new PlayerRunner();bool advanced=false;
            var result=runner.Run(new(e.Project,true,"null",20,1d/60,60,Path.Combine(e.Output,"null-physics.json"),false,false){PresentationCosts=true},pluginRoot:e.Plugins,visible:false,trustedInput:(play,_)=>{
                if(play.Tick>=8){global::Ncma.Runtime.Entity actor=play.Document.World.GetEntities().Single(o=>play.Document.World.Has<AnimatorData>(o));e.Pass(play.Document.World.Get<TransformData>(actor).Position.X>-.95f,"source-free physics Null graph root advances known displacement");advanced=true;native=EnvironmentProductEvidence.NativePaths(e.Root);}
            });
            e.Pass(result.ExitCode==0&&result.Tick==20&&result.RenderedFrames==0&&advanced&&result.Modules.Length==1&&result.Modules[0].Id=="ncma.physics","physics Null exact ticks and only independent solver");
            e.Pass(native.Length==1&&Path.GetFileName(native[0])=="NcmaPhysics.dll"&&result.PresentationCosts is null,"no graphics/platform/pose DLL or GPU observation in physics Null");e.Finish(native);return;
        }
        if(e.Kind=="null"){
            using var runner=new PlayerRunner();var options=new PlayerOptions(e.Project,true,"null",12,1d/60,60,Path.Combine(e.Output,"null.json"),false,false){PresentationCosts=true};
            var result=runner.Run(options,pluginRoot:e.Plugins,visible:false);
            e.Pass(result.ExitCode==0&&result.Tick==12&&result.Modules.Length==0&&result.RenderedFrames==0&&result.PresentationCosts is null,"explicit non-physics Null counterpart exact ticks/no native/no GPU observation even with opt-in");
            var rejected=runner.Run(options with{Project=Path.Combine(Path.GetDirectoryName(e.Project)!,"project.ncmaproject"),Report=Path.Combine(e.Output,"physics-missing.json")},pluginRoot:e.Plugins,visible:false);
            e.Pass(rejected.ExitCode!=0&&rejected.Tick==0&&rejected.SessionId==Guid.Empty,"Null product missing solver refuses physics project, no silent fallback");
            e.Pass(runner.LastNativeFailureDiagnostic is null,"CPU dependency refusal never fabricates GPU fault");e.Finish([]);return;
        }
        foreach(int mode in new[]{0,1,2}){
            using var runner=new PlayerRunner();int stage=0,prompts=0;bool preparationFailure=false,sliderSent=false;PlaySession? play=null;PlayerPresentation? host=null;RendererSession? old=null;GpuViewTarget? held=null;SceneAnimationSession? animation=null;SceneAnimatorRuntime? animator=null;CharacterPlayRuntime? movement=null;PhysicsService? physics=null;UiRuntime? hudRuntime=null;Guid slider=default,actor=default;string? expected=null;byte[]? before=null;
            string State()=>JsonSerializer.Serialize(new{play!.Status,world=play.Document.World.Identity,scene=Convert.ToHexString(play.Document.CaptureBytes()),graph=animator!.ReadFrame(actor),root=movement!.InspectRootMotion(actor),movement=movement.Status,numericalEpoch=movement.NumericalEpoch,physics=physics!.Inspect()});
            RenderPipeline Factory(float exposure,float ambient,bool shadows){if(preparationFailure)throw new InvalidOperationException("test-only replacement preparation");return ShadowJointPipelines.Create(mode,exposure,ambient,shadows,shadowResolution);}
            var result=runner.RunForVerification(new(GraphicsRecoveryJointState.Interactive(e.Project),false,null,20,1d/60,60,Path.Combine(e.Output,$"player-{mode}.json"),false,false){PresentationCosts=true},e.Plugins,
                (pixels,_,_)=>{
                    if(stage==1){before=pixels;expected=State();stage=2;throw new InvalidOperationException("joint final Player capture fault");}
                    if(stage==2){e.Pass(State()==expected&&before!.SequenceEqual(pixels),"same paused graph/solver and exact final Player pixels after new device");
                        GraphicsRecoveryJointState.CheckShadow(e,EnvironmentProductEvidence.Field<SceneRenderSession>(host!,"_scene"),EnvironmentProductEvidence.Field<RendererSession>(host!,"_renderer"),quality);
                        e.Pass(ReferenceEquals(hudRuntime,EnvironmentProductEvidence.Field<SceneUiPresentation>(host!,"_ui").Runtime)&&hudRuntime!.Widget(slider).Value==.5f&&GraphicsRecoveryJointState.GameplayValue(play!)==.5f&&GraphicsRecoveryJointState.PrivateProgress(play!),"same interactive Player HUD and private behaviour progress after recovery");
                        e.Pass(ReferenceEquals(animation,EnvironmentProductEvidence.Field<SceneRenderSession>(host!,"_scene").Animation)&&ReferenceEquals(animator,EnvironmentProductEvidence.Field<SceneAnimatorRuntime>(host!,"_animators")),"Player keeps exact animation and graph runtime instances");stage=3;}
                    native=EnvironmentProductEvidence.NativePaths(e.Root);
                },trustedInput:(p,_)=>{
                    try {
                    var observed=EnvironmentProductEvidence.Field<PlayerPresentation>(runner,"presentation").LastPresentationCosts;
                    if(observed is{IncludesDiagnosticCapture:true})costs.Diagnostic(observed);
                    play??=p;e.Pass(ReferenceEquals(play,p),"same Player session instance");
                    e.Pass(GraphicsRecoveryJointState.PrivateProgress(p),"same private C# counter and single enable/create");
                    if(!sliderSent&&p.Tick>=6){host=EnvironmentProductEvidence.Field<PlayerPresentation>(runner,"presentation");var state=EnvironmentProductEvidence.Field<PlatformWindow>(host,"_window").State;state.Focused=1;state.InputReset=state.Overflow=0;state.PointerX=58;state.PointerY=66;ulong sequence=0;InputEvent Pointer(uint action)=>new(){Kind=2,Action=action,Sequence=++sequence,X=58,Y=66};var events=new[]{Pointer(1),Pointer(0)};state.Sequence=sequence;var ui=EnvironmentProductEvidence.Field<SceneUiPresentation>(host,"_ui");var route=EnvironmentProductEvidence.Field<SceneUiInput>(host,"_uiInput");e.Pass(route.Observe(),"trusted UI observation before test input");var adapter=new UiWindowInput();adapter.Pump(ui.Runtime,state,events,System.Numerics.Vector2.Zero);route.Submit(adapter.Drain(ui.Runtime));ulong inputTick=p.Tick;p.AdvanceFrame(1d/60);e.Pass(p.Tick==inputTick+1,"one explicit fixed quantum consumes bounded synthetic UI observation");sliderSent=true;}
                    if(stage==0&&p.Tick>=8){p.Pause();host=EnvironmentProductEvidence.Field<PlayerPresentation>(runner,"presentation");old=EnvironmentProductEvidence.Field<RendererSession>(host,"_renderer");
                        animator=EnvironmentProductEvidence.Field<SceneAnimatorRuntime>(host,"_animators");movement=(CharacterPlayRuntime)EnvironmentProductEvidence.Field<IRootMotionPresentation>(host,"_rootMotion");physics=EnvironmentProductEvidence.Field<PhysicsService>(runner,"physics");
                        actor=p.Document.World.GetUuid(p.Document.World.GetEntities().Single(o=>p.Document.World.Has<AnimatorData>(o)));animation=EnvironmentProductEvidence.Field<SceneRenderSession>(host,"_scene").Animation;
                        hudRuntime=EnvironmentProductEvidence.Field<SceneUiPresentation>(host,"_ui").Runtime;slider=GraphicsRecoveryJointState.Slider(hudRuntime);e.Pass(hudRuntime.Widget(slider).Value==.5f&&GraphicsRecoveryJointState.GameplayValue(p)==.5f,$"actual Player slider input: UI={hudRuntime.Widget(slider).Value}, C#={GraphicsRecoveryJointState.GameplayValue(p)}, tick={p.Tick}");hudRuntime.CancelInput();
                        e.Pass(movement.InspectRootMotion(actor).DesiredDisplacement.X>.008f&&physics.Inspect().Worlds==1,"nonzero graph root with unique live Jolt world");
                        e.Pass(old.SkinStats.Meshes>=2&&old.EnvironmentStats.Live>0&&old.PostStats.Bindings>0,"real FBX/root/IBL/Bloom are active before fault");GraphicsRecoveryJointState.CheckShadow(e,EnvironmentProductEvidence.Field<SceneRenderSession>(host,"_scene"),old,quality);held=old.CreateViewTarget(16,16);stage=1;
                    }else if(stage==3){costs.Diagnostic(host!.LastPresentationCosts);GraphicsRecoveryJointPlayerWindow.Check(e,host,p,State,before!,costs,mode);GraphicsRecoveryJointState.CheckShadow(e,EnvironmentProductEvidence.Field<SceneRenderSession>(host,"_scene"),EnvironmentProductEvidence.Field<RendererSession>(host,"_renderer"),quality);p.Resume();stage=4;}
                    else if(stage==4)e.Pass(movement!.Status.CommittedSequence==p.Tick&&animator!.ReadFrame(actor).Context.Tick==p.Tick,"resumed graph and solver use one committed tick");
                    }catch(Exception error){Console.Error.WriteLine("Test-only joint input diagnostic: "+error);throw;}
                },pipelineFactory:mode==0?null:Factory,uiActions:GraphicsRecoveryJointState.Routes(),confirmGraphicsRecovery:request=>{
                    costs.Diagnostic(host!.LastPresentationCosts);
                    prompts++;e.Pass(State()==expected&&request.Review.Play==play!.Status,"modal approval/retry cannot tick graph or solver");
                    if(prompts==2){e.Pass(!old!.IsClosed&&request.NativeDiagnostic is{Operation:(uint)GpuFailureOperation.ExplicitAbandon},"actual held lease fails close and preserves copied native diagnostic");held!.Dispose();held=null;preparationFailure=mode!=0;}
                    if(prompts==3){e.Pass(mode!=0&&old!.IsClosed,"failed replacement requires fresh approval");preparationFailure=false;}
                    if(prompts>3)throw new InvalidOperationException("Unexpected repeated recovery failure");return true;
                });
            e.Pass(result.ExitCode==0&&stage==4&&result.Tick>=20&&prompts==(mode==0?2:3),"actual PlayerRunner resumes full joint after approved close/preparation retries: "+JsonSerializer.Serialize(result));
            e.Pass(result.ValidationErrors==0&&result.ValidationWarnings==0&&runner.LastNativeFailureDiagnostic is{Operation:(uint)GpuFailureOperation.ExplicitAbandon},"cumulative API0/0 and immutable diagnostic after shutdown");
            e.Pass(result.PresentationCosts is{Completed:true,IncludesDiagnosticCapture:true}&&result.PresentationCosts==host!.LastPresentationCosts,"Player durable report preserves copied final cost across shutdown");
            e.Pass(EnvironmentProductEvidence.NativePaths(e.Root).Length==0,"Player graph/physics/render ownership drains on final close");
        }
        foreach(string name in new[]{"bad-model","bad-environment","bad-ui","bad-post-module","bad-shadow-library"}){
            using var runner=new PlayerRunner();var rejected=runner.Run(new(Path.Combine(Path.GetDirectoryName(e.Project)!,name+".ncmaproject"),false,null,2,1d/60,30,Path.Combine(e.Output,name+".json"),false,false),pluginRoot:e.Plugins,visible:false);
            e.Pass(rejected.ExitCode!=0&&rejected.Tick==0&&rejected.SessionId==Guid.Empty&&rejected.RenderedFrames==0&&rejected.ShutdownErrors.Length==0,"broken complete joint rejected before gameplay: "+name);e.Pass(EnvironmentProductEvidence.NativePaths(e.Root).Length==0,"broken Player joint drains native: "+name);
        }
        costs.Save("Player");File.WriteAllText(Path.Combine(e.Output,"recovery-scope.json"),JsonSerializer.Serialize(new{actualProduct=true,shadowQuality=quality.ToString(),shadowResolution,realAsciiOrBinaryFbx=true,syntheticRootCompanion=true,physicalDeviceLoss=false,interactiveHud=true,syntheticPlatformEvents=true,privateBehaviourState=true,windowTransitions=true,secondRecoveryTrustedSdk=true,wholeM76Accepted=false}));e.Finish(native);
    }
}
