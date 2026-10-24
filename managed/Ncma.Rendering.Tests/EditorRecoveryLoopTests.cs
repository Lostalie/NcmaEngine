using System.Text.Json;
using Ncma.Application;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Interop;
using Ncma.Rendering;
using Ncma.Scene;

internal static unsafe partial class Program
{
    private static int EditorRecoveryLoopTests(string plugins,string output)
    {
        plugins=Path.GetFullPath(plugins);output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        var repo=new DirectoryInfo(plugins);while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent??throw new Exception("Repo missing");
        var fixture=new SceneGpuFixture(output);var(document,camera,_,_)=fixture.Scene();
        string gameplay=Path.Combine(repo.FullName,"out/managed/Ncma.Gameplay.Sample.dll");
        File.Copy(gameplay,Path.Combine(fixture.Root,"gameplay.dll"));File.Copy(Path.ChangeExtension(gameplay,".deps.json"),Path.Combine(fixture.Root,"gameplay.deps.json"));
        SceneDocumentFiles.Save(document,Path.Combine(fixture.Root,"start.ncmascene"));
        string projectPath=Path.Combine(fixture.Root,"project.ncmaproject");
        File.WriteAllBytes(projectPath,JsonSerializer.SerializeToUtf8Bytes(new ProjectConfiguration(1,fixture.Project,"Recovery loop","start.ncmascene","gameplay.dll","Direct3D11",[],SceneCamera:camera),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        int cases=0;void Pass(bool ok,string label){Check(ok,"M7.6 Editor recovery loop: "+label);cases++;}
        void Refuse(Action action){try{action();}catch(Exception e)when(e is InvalidOperationException or PluginException or AggregateException){return;}throw new Exception("Expected failure");}
        foreach(bool retirementFailure in new[]{false,true}) {
            using var app=new ApplicationLifetime();var editor=new CandidatePresentation(plugins,ProjectContext.Load(projectPath),true,false,false);app.Start([editor]);
            var owner=editor.VerificationEditor;var workspace=new EditorWorkspace(owner);var id=workspace.CreateObject(workspace.Stamp);
            var authoring=owner.Document;var history=owner.Edit!.State;var bytes=owner.Document.CaptureBytes();
            var clock=new RecoveryClock();Pass(app.Run(clock,editor,2)==2,"warm ordinary application frames");var world=OwnedEditProjection(editor).Scene.World;
            var original=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");ulong generation=original.ReadFrameState().Generation;
            var held=retirementFailure?original.CreateViewTarget(16,16):null;
            byte[]? before=null,after=null;editor.CaptureNextCompositeViewportForVerification((pixels,_,_)=>{before=pixels;throw new InvalidOperationException("private-frame-source");});
            int prompts=0;Guid token=Guid.Empty;ApplicationFailure? first=null;
            ulong attempts=EditorRunLoop.Run(app,editor,clock,4,request=>{
                prompts++;first??=app.Failure;
                Pass(app.State==ApplicationState.Failed&&app.ShutdownAttempts==0&&request.Failure is{FrameId:3,Phase:ApplicationFramePhase.Present},"confirmation after loop unwound, exact failed frame, no shutdown");
                Pass(ReferenceEquals(authoring,owner.Document)&&ReferenceEquals(world,OwnedEditProjection(editor).Scene.World)&&OwnedEditProjection(editor).IsCurrent(owner)&&owner.Edit.State==history&&bytes.SequenceEqual(owner.Document.CaptureBytes()),"confirmation cannot advance or replace edit state");
                Pass(!request.Message.Contains("private")&&request.Message.Contains("未保存")&&request.Review.AbandonActiveFrame,"fixed bounded human-facing explanation includes loss/abandon consequences");
                if(request.Retry){Pass(retirementFailure&&prompts==2&&token!=request.Review.Token&&!original.IsClosed&&app.RecoveryAttempts==1,"failed retirement retains device and requires a fresh exact second approval");held!.Dispose();}
                else {Pass(prompts==1&&original.ReadFrameState().Active&&original.ReadFrameState().AbandonedFrames==0,"no implicit incomplete-frame abandonment before approval");token=request.Review.Token;}
                clock.Value+=600;return true; // modal wall time must not become simulation delta
            });
            Pass(attempts==4&&app.LastFrameId==6&&app.State==ApplicationState.Running&&prompts==(retirementFailure?2:1),"formal shared loop resumes exact remaining budget without replay");
            var restored=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");
            Pass(original.IsClosed&&restored.ReadFrameState().Generation!=generation&&restored.ReadFrameState().Frame==6&&app.RecoveryAttempts==(retirementFailure?2u:1u),"fresh actual device observes monotonic application frame IDs");
            Pass(ReferenceEquals(owner,editor.VerificationEditor)&&ReferenceEquals(authoring,owner.Document)&&ReferenceEquals(world,OwnedEditProjection(editor).Scene.World)&&OwnedEditProjection(editor).IsCurrent(owner)&&owner.Edit.State==history&&PinLocked(fixture.GenerationPath),"same owner/World/Undo and CPU pin across scheduling recovery");
            editor.CaptureNextCompositeViewportForVerification((pixels,_,_)=>after=pixels);app.Run(clock,editor,1);
            Pass(before is not null&&after is not null&&before.SequenceEqual(after),"exact final viewport pixels after resumed ordinary application frames");
            workspace.History(workspace.Stamp,false);Pass(!owner.Document.CaptureSnapshot().Objects.Any(o=>o.Id==id),"pre-fault Undo works through same history");
            workspace.History(workspace.Stamp,true);Pass(owner.Document.CaptureSnapshot().Objects.Any(o=>o.Id==id),"pre-fault Redo works through same history");
            Pass(ReferenceEquals(first,app.Failure)&&restored.Stats.ValidationErrors==0&&restored.Stats.ValidationWarnings==0,"first fault preserved and DX11 API0/0");
            app.Dispose();Pass(app.State==ApplicationState.Stopped&&!PinLocked(fixture.GenerationPath),"ordinary application close drains restored generation and pins");
        }
        foreach(int policy in new[]{0,1,2}) {
            using var app=new ApplicationLifetime();var editor=new CandidatePresentation(plugins,ProjectContext.Load(projectPath),true,false,false);app.Start([editor]);var clock=new RecoveryClock();app.Run(clock,editor,2);
            var renderer=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");var world=OwnedEditProjection(editor).Scene.World;var history=editor.VerificationEditor.Edit!.State;
            editor.CaptureNextCompositeViewportForVerification((_,_,_)=>throw new InvalidOperationException("private-frame-source"));int prompts=0;
            Func<EditorRecoveryRequest,bool>? confirm=policy==0?null:request=>{prompts++;if(policy==2)throw new InvalidOperationException("private-confirmation-source");return false;};
            Refuse(()=>EditorRunLoop.Run(app,editor,clock,4,confirm));
            Pass(app.State==ApplicationState.Failed&&app.RecoveryAttempts==0&&prompts==(policy==0?0:1)&&renderer.ReadFrameState().Active&&renderer.ReadFrameState().AbandonedFrames==0,"no prompt/decline/prompt failure cannot approve or abandon "+policy);
            Pass(ReferenceEquals(world,OwnedEditProjection(editor).Scene.World)&&OwnedEditProjection(editor).IsCurrent(editor.VerificationEditor)&&history==editor.VerificationEditor.Edit!.State&&app.LastFrameId==3,"rejected recovery never runs another frame "+policy);
            // Explicit test cleanup is a separate close-only operation, not a user approval.
            Refuse(app.Dispose);editor.AbandonFrameForShutdown();app.Dispose();
            Pass(app.RetainedServiceCount==0&&app.State==ApplicationState.Failed,"declined recovery preserves first fault while explicit close drains "+policy);
        }
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{cases,formalSharedRunLoop=true,schema=2,sameRuntimeProjection=true,sameAuthoring=true,actualDx11=true,scriptedConfirmation=true,manualWindowsDialog=false,physicalDeviceLoss=false,playRecovery=false,wholeStageAccepted=false}));
        Console.WriteLine($"PASS M7.6 Editor recovery loop {cases} checks; {output}");return 0;
    }
    private sealed class RecoveryClock:IFrameClock {public double Value;public double Seconds=>Value+=1d/60;}
}
