using System.Text.Json;
using Ncma.Application;
using Ncma.Editor.App;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Interop;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Scene;

internal static unsafe partial class Program
{
    private static int EditorGraphicsRecoveryTests(string plugins,string output)
    {
        plugins=Path.GetFullPath(plugins);output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        var repo=new DirectoryInfo(plugins);while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent??throw new Exception("Repo missing");
        var fixture=new SceneGpuFixture(output);var(document,camera,_,_)=fixture.Scene();
        string gameplay=Path.Combine(repo.FullName,"out/managed/Ncma.Gameplay.Sample.dll");
        File.Copy(gameplay,Path.Combine(fixture.Root,"gameplay.dll"));File.Copy(Path.ChangeExtension(gameplay,".deps.json"),Path.Combine(fixture.Root,"gameplay.deps.json"));
        SceneDocumentFiles.Save(document,Path.Combine(fixture.Root,"start.ncmascene"));
        string projectPath=Path.Combine(fixture.Root,"project.ncmaproject");
        File.WriteAllBytes(projectPath,JsonSerializer.SerializeToUtf8Bytes(new ProjectConfiguration(1,fixture.Project,"Graphics recovery","start.ncmascene","gameplay.dll","Direct3D11",[],SceneCamera:camera),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        int cases=0;void Pass(bool ok,string label){Check(ok,"M7.6 same-session graphics: "+label);cases++;}
        using(var editor=new CandidatePresentation(plugins,ProjectContext.Load(projectPath),true,false,false)) {
            editor.Start();ulong frame=0;
            byte[] Draw() {
                byte[]? pixels=null;editor.CaptureNextCompositeViewportForVerification((p,_,_)=>pixels=p);
                editor.PumpPlatform();var f=new FrameContext(++frame,0);editor.ApplyEditorIntents(f);editor.PumpAgent(f);editor.BeginPresentation(f);editor.SubmitInput(f);editor.AdvancePlay(f);editor.Present(f);
                return pixels??throw new Exception("Missing viewport capture");
            }
            Draw();var before=Draw();var owner=editor.VerificationEditor;var edit=owner.Edit!;var world=owner.Document.World;var doc=owner.Document;var presenter=editor.VerificationPresenter;
            var workspace=new EditorWorkspace(owner);var id=workspace.CreateObject(workspace.Stamp);Draw();
            var stamp=workspace.Stamp;var history=edit.State;var bytes=doc.CaptureBytes();
            var old=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");
            var target=EnvironmentProductEvidence.Field<GpuViewTarget>(editor,"_viewportTarget");var generation=old.ReadFrameState().Generation;
            var stale=editor.ReviewGraphicsRecovery();workspace.Select(workspace.Stamp,camera); // selection alone does not change scene revision
            workspace.Transaction(workspace.Stamp,"Name",[new{op="rename",objectId=id,name="retained"}]);Reject(()=>editor.RebuildGraphics(stale));
            Pass(editor.GraphicsPhase==EditorGraphicsPhase.Ready&&!old.IsClosed,"stale scene review cannot retire resources");
            workspace.History(workspace.Stamp,false);stamp=workspace.Stamp;history=edit.State;bytes=doc.CaptureBytes();
            var review=editor.ReviewGraphicsRecovery();
            Task.Run(()=>Reject(()=>editor.RebuildGraphics(review))).GetAwaiter().GetResult();
            Pass(!old.IsClosed&&editor.GraphicsPhase==EditorGraphicsPhase.Ready,"foreign thread cannot consume review");
            editor.RebuildGraphics(review);var fresh=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");
            Pass(old.IsClosed&&fresh.ReadFrameState().Generation!=generation,"new actual DX11 device generation in same loaded plugin");
            Pass(ReferenceEquals(owner,editor.VerificationEditor)&&ReferenceEquals(world,owner.Document.World)&&ReferenceEquals(doc,owner.Document)&&ReferenceEquals(edit,owner.Edit)&&ReferenceEquals(presenter,editor.VerificationPresenter),"same exact World/document/EditSession/presenter instances");
            Pass(workspace.Stamp==stamp&&edit.State==history&&bytes.SequenceEqual(doc.CaptureBytes())&&PinLocked(fixture.GenerationPath),"history and document unchanged, original CPU asset pin retained");
            Reject(()=>_=target.ImageToken);Reject(()=>_=old.Stats);Reject(()=>editor.RebuildGraphics(review));
            Pass(editor.GraphicsPhase==EditorGraphicsPhase.Ready,"stale objects and replay rejected without harming replacement");
            Draw();Pass(Draw().SequenceEqual(before),"same static scene exact final viewport pixels after new-device reconstruction");
            var profile=edit.Invoke(new(EditSession.ContractVersion,Guid.NewGuid(),edit.SessionId,edit.Revision,"ncma.render.get_profile",JsonSerializer.SerializeToElement(new{})),new CapabilityPermissions(["ncma.render.get_profile"]));
            Pass(profile.Status=="ok","original registered profile handler reads new renderer without re-registering capabilities");
            workspace.History(workspace.Stamp,true);Pass(owner.Document.CaptureSnapshot().Objects.Single(o=>o.Id==id).Name=="retained","original Redo history survives graphics rebuild");
            workspace.History(workspace.Stamp,false);workspace.History(workspace.Stamp,false);Pass(!owner.Document.CaptureSnapshot().Objects.Any(o=>o.Id==id),"original Undo removes pre-rebuild object");
            workspace.History(workspace.Stamp,true);Pass(owner.Document.CaptureSnapshot().Objects.Any(o=>o.Id==id),"original Undo/Redo remains sole history");Draw();
            var active=owner.StartPlay();var playReview=editor.ReviewGraphicsRecovery();active.AdvanceFrame(.01);Reject(()=>editor.RebuildGraphics(playReview));
            var activeStatus=active.Status;editor.RebuildGraphics(editor.ReviewGraphicsRecovery());
            Pass(ReferenceEquals(active,owner.Play)&&active.Status==activeStatus&&editor.GraphicsPhase==EditorGraphicsPhase.Ready,"stale Play review rejects; current exact Play is retained, never restarted or advanced");owner.StopPlay();Draw();
            fresh=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");
            // Actual frame fault leaves a live native resource group. No reflective fault attachment.
            editor.PumpPlatform();var faultFrame=new FrameContext(++frame,0);editor.ApplyEditorIntents(faultFrame);editor.PumpAgent(faultFrame);editor.BeginPresentation(faultFrame);
            Reject(()=>editor.ReviewGraphicsRecovery(true));editor.SubmitInput(faultFrame);editor.AdvancePlay(faultFrame);
            editor.CaptureNextCompositeViewportForVerification((_,_,_)=>throw new InvalidOperationException("private-capture-failure"));Reject(()=>editor.Present(faultFrame));
            Pass(fresh.ReadFrameState().Active,"actual failed frame remains active, no implicit Present/abandon");
            Reject(()=>editor.ReviewGraphicsRecovery());var failedWorld=owner.Document.World;var failedHistory=edit.State;var failedBytes=owner.Document.CaptureBytes();
            editor.RebuildGraphics(editor.ReviewGraphicsRecovery(true));
            var nativeDiagnostic=editor.GraphicsNativeFailure;
            Pass(nativeDiagnostic is {Operation:(uint)GpuFailureOperation.ExplicitAbandon,NativeCode:null,DeviceReason:0}&&nativeDiagnostic.Value.Frame==faultFrame.FrameId,"explicit abandonment diagnostic copied before old renderer closes, not fabricated physical loss");
            Pass(fresh.IsClosed&&ReferenceEquals(failedWorld,owner.Document.World)&&edit.State==failedHistory&&failedBytes.SequenceEqual(owner.Document.CaptureBytes()),"approved terminal abandonment plus fresh device retains same live edit state");
            Draw();Pass(Draw().SequenceEqual(before),"exact viewport after actual failed frame recovery");
            var final=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");
            Pass(final.Stats.ValidationErrors==0&&final.Stats.ValidationWarnings==0,"DX11 validation0/0");
            // A genuine independently held GPU target prevents device close. It must not be
            // discarded/force-released by the Editor's recovery or lose its parent owner.
            var heldTarget=final.CreateViewTarget(16,16);var retainedHistory=edit.State;
            InvalidOperationException? closeFailure=null;try{editor.RebuildGraphics(editor.ReviewGraphicsRecovery());}catch(InvalidOperationException error){closeFailure=error;}
            Pass(closeFailure?.Message=="Render resource leases/preparation remain."&&editor.GraphicsPhase==EditorGraphicsPhase.Failed&&!final.IsClosed,"actual GPU resource lease blocks device retirement and retains old renderer");
            Reject(()=>editor.PumpPlatform());Pass(heldTarget.ImageToken.Generation>0&&edit.State==retainedHistory&&PinLocked(fixture.GenerationPath),"failed retirement preserves external resource, sole history and CPU pins");
            heldTarget.Dispose();editor.RebuildGraphics(editor.ReviewGraphicsRecovery());Draw();
            Pass(editor.GraphicsNativeFailure==nativeDiagnostic,"first native diagnostic survives later failed retirement and replacement");
            Pass(final.IsClosed&&Draw().SequenceEqual(before)&&editor.GraphicsRecoveryFailure=="graphics_retirement_failed","explicit release/review/retry restores exact viewport, preserves first diagnostic");
            editor.Dispose();Pass(!PinLocked(fixture.GenerationPath),"normal final shutdown releases retained CPU pin");
            Pass(editor.GraphicsNativeFailure==nativeDiagnostic,"native diagnostic is an owned value after plugin unload");
        }
        // A real failed submitted frame followed by an explicit scene-owner close attempt
        // leaves that owner Closing/Busy. Read-only field access locates the actual owner;
        // no field replacement, reflective cleanup or synthetic native error is used.
        foreach(bool playing in new[]{false,true}) {
            using var closingHost=new CandidatePresentation(plugins,ProjectContext.Load(projectPath),true,false,false);
            closingHost.Start();var owner=closingHost.VerificationEditor;if(playing)owner.StartPlay();
            closingHost.PumpPlatform();var f=new FrameContext(1,0);closingHost.ApplyEditorIntents(f);closingHost.PumpAgent(f);closingHost.BeginPresentation(f);closingHost.SubmitInput(f);closingHost.AdvancePlay(f);
            closingHost.CaptureNextCompositeViewportForVerification((_,_,_)=>throw new InvalidOperationException("test-closing-frame"));Reject(()=>closingHost.Present(f));
            var scene=EnvironmentProductEvidence.Field<SceneRenderSession>(closingHost,playing?"_playScene":"_editScene");
            var renderer=EnvironmentProductEvidence.Field<RendererSession>(closingHost,"_renderer");
            var world=owner.Document.World;var state=owner.Edit!.State;var play=owner.Play;var status=play?.Status;
            PluginException? failedClose=null;try{scene.Dispose();}catch(PluginException error){failedClose=error;}
            Pass(failedClose?.Result==PluginResult.Busy&&scene.PreparationPhase==ScenePreparationPhase.Closing&&renderer.ReadFrameState().Active,"actual partial scene close retains Closing owner and active GPU frame "+playing);
            var review=closingHost.ReviewGraphicsRecovery(true);closingHost.RebuildGraphics(review);
            Pass(scene.PreparationPhase==ScenePreparationPhase.Closed&&renderer.IsClosed&&closingHost.GraphicsPhase==EditorGraphicsPhase.Ready,"approved host recovery retries Closing scene without Ready-only access "+playing);
            Pass(ReferenceEquals(world,owner.Document.World)&&owner.Edit.State==state&&ReferenceEquals(play,owner.Play)&&play?.Status==status,"partial-close retry preserves original business/Play owners "+playing);
            Reject(()=>closingHost.RebuildGraphics(review));
            var fresh=EnvironmentProductEvidence.Field<RendererSession>(closingHost,"_renderer");
            Pass(fresh.Stats.ValidationErrors==0&&fresh.Stats.ValidationWarnings==0&&PinLocked(fixture.GenerationPath),"replacement device API0/0 and CPU source pin retained "+playing);
            closingHost.Dispose();Pass(!PinLocked(fixture.GenerationPath),"partial-close final shutdown drains owners "+playing);
        }
        // Preparation callbacks are trusted extension code, not a sandbox. Caught host reentry
        // still poisons this operation and leaves all owners available for explicit retry.
        bool reenter=false;CandidatePresentation? host=null;
        using(host=new(plugins,ProjectContext.Load(projectPath),true,false,false,pipelineFactory:(w,h,s)=>{
            if(reenter){Reject(host!.Dispose);}
            return new Scene3DPipeline(w,h,shadows:s);
        })) {
            host.Start();var owner=host.VerificationEditor;var world=owner.Document.World;var bytes=owner.Document.CaptureBytes();
            reenter=true;var review=host.ReviewGraphicsRecovery();Reject(()=>host.RebuildGraphics(review));
            Pass(host.GraphicsPhase==EditorGraphicsPhase.Failed&&host.GraphicsRecoveryFailure=="graphics_preparation_failed","caught reentry poisons outer preparation");
            Reject(()=>host.PumpPlatform());Reject(()=>host.RebuildGraphics(review));
            Pass(ReferenceEquals(world,owner.Document.World)&&bytes.SequenceEqual(owner.Document.CaptureBytes())&&PinLocked(fixture.GenerationPath),"failed candidate retains World, CPU pins and original document");
            reenter=false;host.RebuildGraphics(host.ReviewGraphicsRecovery());
            Pass(host.GraphicsPhase==EditorGraphicsPhase.Ready&&host.GraphicsRecoveryFailure=="graphics_preparation_failed"&&ReferenceEquals(owner,host.VerificationEditor),"exact explicit retry drains partial replacement; preserves first diagnostic and business owner");
        }
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{cases,sameWorld=true,sameHistory=true,actualDx11=true,physicalDeviceLoss=false,basicPlayRetention=true,wholeStageAccepted=false}));
        Console.WriteLine($"PASS M7.6 Editor same-session graphics {cases} checks; {output}");return 0;
    }
}
