using System.Text.Json;
using System.Diagnostics;
using Ncma.Animation;
using Ncma.Application;
using Ncma.Characters;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Gameplay;
using Ncma.Physics;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Runtime;
using Ncma.Ui.Rendering;

internal static class GraphicsRecoveryJointEditorProbe
{
    internal static void Run(string requestPath)
    {
        using var e=new UiPublicationProductEvidence(requestPath);string[] native=[];var costs=new List<object>();
        var quality=GraphicsRecoveryJointState.ShadowQuality(e.Project);uint shadowResolution=SceneShadowPresets.Get(quality).Budget.Resolution;
        foreach(int mode in new[]{0,1,2}){
            bool preparationFailure=false;
            RenderPipeline Factory(float exposure,float ambient,bool shadows){if(preparationFailure)throw new InvalidOperationException("test-only preparation fault");return ShadowJointPipelines.Create(mode,exposure,ambient,shadows,shadowResolution);}
            var context=ProjectContext.Load(GraphicsRecoveryJointState.Interactive(e.Project));
            using var editor=new CandidatePresentation(e.Plugins,context,true,false,false,pipelineFactory:mode==0?null:Factory,uiActions:GraphicsRecoveryJointState.Routes());
            editor.Start();ulong frame=0;
            var inputState=EnvironmentProductEvidence.Field<PlatformWindow>(editor,"_window").State;inputState.Focused=1;inputState.InputReset=inputState.Overflow=0;ulong inputSequence=inputState.Sequence;
            byte[] Draw(bool fault=false,bool sliderInput=false,double delta=0,bool capture=true){byte[]? image=null;if(capture)editor.CaptureNextCompositeViewportForVerification((p,_,_)=>{image=p;if(fault)throw new InvalidOperationException("joint capture fault");});
                InputEvent[] events=[];if(sliderInput){var area=editor.VerificationPresenter.Viewport;inputState.PointerX=area.X+58;inputState.PointerY=area.Y+66;InputEvent Pointer(uint action)=>new(){Kind=2,Action=action,Sequence=++inputSequence,X=inputState.PointerX,Y=inputState.PointerY};events=[Pointer(1),Pointer(0)];}inputState.Sequence=inputSequence;editor.SetWindowInputForVerification(inputState,events);
                var f=new FrameContext(++frame,delta);editor.ApplyEditorIntents(f);editor.PumpAgent(f);editor.BeginPresentation(f);editor.SubmitInput(f);editor.AdvancePlay(f);editor.Present(f);return image??(capture?throw new Exception("No final viewport"):[]);}
            Draw();var editImage=Draw();var owner=editor.VerificationEditor;var edit=owner.Edit!;var editWorld=owner.Document.World;var editBytes=owner.Document.CaptureBytes();var editHistory=edit.State;
            owner.RefreshAssets(true);owner.Assets?.PrepareStoredGenerationsAsync().GetAwaiter().GetResult();
            var play=owner.StartPlay();Draw();Draw(sliderInput:true,delta:1d/60);var hud=EnvironmentProductEvidence.Field<SceneUiPresentation>(editor,"_playHud");var hudRuntime=hud.Runtime;Guid slider=GraphicsRecoveryJointState.Slider(hudRuntime);
            e.Pass(hudRuntime.Widget(slider).Value==.5f&&GraphicsRecoveryJointState.GameplayValue(play)==.5f,"real viewport slider routes through fixed-step C# with graph/root/physics live");hudRuntime.CancelInput();play.Pause();for(int i=0;i<12;i++){e.Pass(play.Step().State==PlayState.Paused,"joint committed step");Draw();}
            e.Pass(GraphicsRecoveryJointState.PrivateProgress(play),"original SDK behaviour private progress before fault");
            var runtime=EnvironmentProductEvidence.Field<ScenePlayRuntime>(editor,"_sceneRuntime");var physics=EnvironmentProductEvidence.Field<PhysicsService>(editor,"_physics");
            var actor=play.Document.World.GetObjects().Single(o=>o.Has<AnimatorData>());Guid actorId=actor.PersistentId;
            e.Pass(runtime.Characters!.InspectRootMotion(actorId).DesiredDisplacement.X>.008f&&actor.Get<TransformData>().Position.X>-.91f,"known synthetic 0.5m/s graph root moves through Jolt alongside real FBX actor");
            string State()=>JsonSerializer.Serialize(new{play.Status,world=play.Document.World.Identity,scene=Convert.ToHexString(play.Document.CaptureBytes()),graph=runtime.Animators!.ReadFrame(actorId),root=runtime.Characters!.InspectRootMotion(actorId),movement=runtime.Characters.Status,numericalEpoch=runtime.Characters.NumericalEpoch,physics=physics.Inspect()});
            var expected=State();var history=edit.State;var old=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");var scene=EnvironmentProductEvidence.Field<SceneRenderSession>(editor,"_playScene");var animation=scene.Animation;
            var pixels=Draw();var vertices=new byte[4*48];scene.CaptureCharacterVertices(actorId,vertices);
            e.Pass(old.SkinStats.Meshes>=2&&old.EnvironmentStats.Live>0&&old.PostStats.Bindings>0&&old.UiTargetStats.Targets>0&&physics.Inspect().Worlds==1,"all graph/root/Jolt/FBX/IBL/Bloom/HUD live together");GraphicsRecoveryJointState.CheckShadow(e,scene,old,quality);
            using var held=old.CreateViewTarget(16,16);Reject(()=>Draw(true));e.Pass(old.ReadFrameState().Active,"actual submitted composite fault is retained");
            Reject(()=>editor.ReviewGraphicsRecovery());var ticket=editor.ReviewGraphicsRecovery(true);Reject(()=>editor.RebuildGraphics(ticket));
            e.Pass(!old.IsClosed&&State()==expected&&edit.State==history,"failed GPU lease retirement preserves graph, solver sequence, World and history");
            Reject(()=>editor.RebuildGraphics(ticket));held.Dispose();
            if(mode!=0){preparationFailure=true;Reject(()=>editor.RebuildGraphics(editor.ReviewGraphicsRecovery(true)));preparationFailure=false;e.Pass(State()==expected,"failed replacement preparation never advances graph/root/physics");}
            editor.RebuildGraphics(editor.ReviewGraphicsRecovery(true));
            e.Pass(old.IsClosed&&State()==expected&&ReferenceEquals(runtime,EnvironmentProductEvidence.Field<ScenePlayRuntime>(editor,"_sceneRuntime"))&&ReferenceEquals(animation,EnvironmentProductEvidence.Field<SceneRenderSession>(editor,"_playScene").Animation),"new GPU generation retains exact CPU animation and coupled runtime instances");
            var recovered=Draw();e.Pass(pixels.SequenceEqual(recovered),"exact paused final FBX+root+IBL+selected shadow+Bloom+HUD image after recovery");
            GraphicsRecoveryJointState.CheckShadow(e,EnvironmentProductEvidence.Field<SceneRenderSession>(editor,"_playScene"),EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer"),quality);
            e.Pass(ReferenceEquals(hudRuntime,EnvironmentProductEvidence.Field<SceneUiPresentation>(editor,"_playHud").Runtime)&&hudRuntime.Widget(slider).Value==.5f&&GraphicsRecoveryJointState.GameplayValue(play)==.5f&&GraphicsRecoveryJointState.PrivateProgress(play),"same interactive HUD value and private behaviour state after failure/rebuild");
            var nextVertices=new byte[vertices.Length];EnvironmentProductEvidence.Field<SceneRenderSession>(editor,"_playScene").CaptureCharacterVertices(actorId,nextVertices);e.Pass(vertices.SequenceEqual(nextVertices),"exact GPU skinned vertices at retained graph clock");
            e.Pass(editor.GraphicsNativeFailure is{Operation:(uint)GpuFailureOperation.ExplicitAbandon,NativeCode:null},"copied first diagnostic survives retirement and preparation failures");
            ulong tick=play.Tick;play.Step();Draw();e.Pass(play.Tick==tick+1&&runtime.Characters.Status.CommittedSequence==play.Tick&&runtime.Animators!.ReadFrame(actorId).Context.Tick==play.Tick,"one resumed quantum advances graph and numerical solver exactly once");
            e.Pass(GraphicsRecoveryJointState.PrivateProgress(play)&&GraphicsRecoveryJointState.GameplayValue(play)==.5f,"script not restarted and old HUD input not replayed");
            var warmRenderer=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");for(int i=0;i<8;i++)Draw(capture:false);
            var uiBefore=warmRenderer.UiStats;var envBefore=warmRenderer.EnvironmentStats;var graphBefore=warmRenderer.PipelineStats;var stateBefore=State();
            long allocated=GC.GetAllocatedBytesForCurrentThread(),started=Stopwatch.GetTimestamp();for(int i=0;i<16;i++)Draw(capture:false);double milliseconds=Stopwatch.GetElapsedTime(started).TotalMilliseconds;allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
            e.Pass(warmRenderer.UiStats.UploadedBytes==uiBefore.UploadedBytes&&warmRenderer.EnvironmentStats.UploadedBytes==envBefore.UploadedBytes&&warmRenderer.PipelineStats.Creates==graphBefore.Creates&&State()==stateBefore,"post-recovery warm frames do not reupload immutable UI/IBL, rebuild graph or advance paused simulation");
            costs.Add(new{mode,frames=16,allocatedBytes=allocated,cpuMilliseconds=milliseconds,ui=warmRenderer.UiStats,environment=warmRenderer.EnvironmentStats,pipeline=warmRenderer.PipelineStats,fullPerformanceAccepted=false,gpuOriginFrameKnown=false});
            // Use the same monotonic verification stream throughout; never mix synthetic
            // observations with an unrelated real window sequence. Simulation remains paused.
            var visible=inputState;var windowImage=Draw();var windowState=State();var stats=warmRenderer.Stats;
            var target=EnvironmentProductEvidence.Field<GpuViewTarget>(editor,"_viewportTarget");var imageToken=target.ImageToken;
            inputState.Minimized=1;inputState.FramebufferWidth=inputState.FramebufferHeight=0;Draw(capture:false);
            inputState.Minimized=0;inputState.FramebufferHeight=visible.FramebufferHeight;Draw(capture:false);
            e.Pass(warmRenderer.Stats.SubmittedFrames==stats.SubmittedFrames&&warmRenderer.Stats.Presents==stats.Presents&&target.ImageToken==imageToken&&State()==windowState,"full joint minimized and zero-width frames neither submit nor advance graph/solver");
            inputState=visible;e.Pass(Draw().SequenceEqual(windowImage)&&State()==windowState,"full joint restore keeps exact paused image and simulation");
            inputState.Width=visible.Width-137;inputState.Height=visible.Height-83;inputState.FramebufferWidth=visible.FramebufferWidth-137;inputState.FramebufferHeight=visible.FramebufferHeight-83;
            var resized=Draw();e.Pass(resized.Length!=windowImage.Length&&State()==windowState,"real full joint targets resize without advancing CPU animation or physics");
            inputState=visible;Draw();e.Pass(Draw().SequenceEqual(windowImage)&&State()==windowState,"restored dimensions reproduce exact complete joint image");
            var firstDiagnostic=editor.GraphicsNativeFailure;var secondOld=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");ulong secondGeneration=secondOld.ReadFrameState().Generation;
            Reject(()=>Draw(true));editor.RebuildGraphics(editor.ReviewGraphicsRecovery(true));
            e.Pass(secondOld.IsClosed&&EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer").ReadFrameState().Generation!=secondGeneration&&Draw().SequenceEqual(windowImage)&&State()==windowState,"second submitted fault after window transitions recovers a fresh generation without state replay");
            e.Pass(editor.GraphicsNativeFailure==firstDiagnostic&&ReferenceEquals(hudRuntime,EnvironmentProductEvidence.Field<SceneUiPresentation>(editor,"_playHud").Runtime)&&GraphicsRecoveryJointState.PrivateProgress(play),"second recovery retains first diagnostic, interactive widgets and private behaviour");
            GraphicsRecoveryJointState.CheckShadow(e,EnvironmentProductEvidence.Field<SceneRenderSession>(editor,"_playScene"),EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer"),quality);
            Guid recoveredWorld=play.Document.World.Identity;owner.StopPlay();Draw();e.Pass(physics.Inspect().Worlds==0&&ReferenceEquals(editWorld,owner.Document.World)&&editBytes.SequenceEqual(owner.Document.CaptureBytes())&&edit.State==editHistory,"Stop drains solver and restores original Edit/history");
            e.Pass(Draw().SequenceEqual(editImage),"original Edit final image restored");
            for(int cycle=0;cycle<2;cycle++){
                var again=owner.StartPlay();Draw();e.Pass(again.Document.World.Identity!=recoveredWorld,"new Play has isolated World after recovered Play stopped");
                Draw(sliderInput:true,delta:1d/60);again.Pause();again.Step();Draw();
                e.Pass(GraphicsRecoveryJointState.PrivateProgress(again)&&GraphicsRecoveryJointState.GameplayValue(again)==.5f&&physics.Inspect().Worlds==1,"repeated Play starts one solver and fresh private scripts with functional HUD");
                owner.ReloadGameplay(context.GameplayAssemblyPath);Draw();var reloaded=owner.Play!;
                e.Pass(reloaded.State is PlayState.Paused or PlayState.Running,"explicit gameplay reload remains healthy after graphics recovery");
                var reloadedRuntime=EnvironmentProductEvidence.Field<ScenePlayRuntime>(editor,"_sceneRuntime");
                reloaded.Pause();ulong beforeTick=reloaded.Tick,sequenceBefore=reloadedRuntime.Characters!.Status.CommittedSequence;var step=reloaded.Step();Draw();
                var committed=reloadedRuntime.Characters.Status;
                e.Pass(step.State==PlayState.Paused&&reloaded.Tick==beforeTick+1&&committed.CommittedSequence==sequenceBefore+1&&committed.CommittedTick==reloaded.Tick&&reloadedRuntime.Animators!.ReadFrame(actorId).Context.Tick==reloaded.Tick&&physics.Inspect().Worlds==1,"post-reload graph and solver resume one committed quantum: "+JsonSerializer.Serialize(new{beforeTick,sequenceBefore,step,committed,physics=physics.Inspect()}));
                owner.StopPlay();Draw();e.Pass(physics.Inspect().Worlds==0&&edit.State==editHistory&&editBytes.SequenceEqual(owner.Document.CaptureBytes())&&Draw().SequenceEqual(editImage),"repeated reload/Stop drains numerical world and restores original Edit image/history");
            }
            var renderer=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");e.Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"replacement API0/0");
            native=EnvironmentProductEvidence.NativePaths(e.Root);File.WriteAllBytes(Path.Combine(e.Output,$"recovered-editor-{mode}.rgba"),recovered);
            editor.Dispose();e.Pass(EnvironmentProductEvidence.NativePaths(e.Root).Length==0,"complete editor close unloads all native dependencies");
        }
        foreach(string name in new[]{"bad-model","bad-environment","bad-ui","bad-post-module","bad-shadow-library"}){
            using var rejected=new CandidatePresentation(e.Plugins,ProjectContext.Load(Path.Combine(Path.GetDirectoryName(e.Project)!,name+".ncmaproject")),true,false,false);
            bool failed=false;try{rejected.Start();}catch(Exception error)when(error is ArgumentException or InvalidOperationException or IOException or NotSupportedException){failed=true;}
            e.Pass(failed,"complete joint rejects broken startup closure: "+name);rejected.Dispose();e.Pass(EnvironmentProductEvidence.NativePaths(e.Root).Length==0,"broken joint closes graphics/physics owners: "+name);
        }
        File.WriteAllText(Path.Combine(e.Output,"costs.json"),JsonSerializer.Serialize(costs,new JsonSerializerOptions{IncludeFields=true}));
        File.WriteAllText(Path.Combine(e.Output,"recovery-scope.json"),JsonSerializer.Serialize(new{actualProduct=true,shadowQuality=quality.ToString(),shadowResolution,realAsciiOrBinaryFbx=true,syntheticRootCompanion=true,physicalDeviceLoss=false,interactiveHud=true,syntheticPlatformEvents=true,privateBehaviourState=true,windowTransitions=true,repeatedRecovery=true,playReloadStopCycles=3,wholeM76Accepted=false}));e.Finish(native);
    }
    private static void Reject(Action action){try{action();}catch(Exception error)when(error is InvalidOperationException or Ncma.Interop.PluginException){return;}throw new Exception("Expected joint recovery rejection");}
}
