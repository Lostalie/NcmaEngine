using System.Text.Json;
using Ncma.Application;
using Ncma.Editor.App;
using Ncma.Interop;
using Ncma.Rendering;
using Ncma.Scene;

internal static unsafe partial class Program
{
    private static int EditorLifetimeTests(string plugins,string output)
    {
        plugins=Path.GetFullPath(plugins);output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        var repo=new DirectoryInfo(plugins);while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent??throw new Exception("Repo missing");
        var fixture=new SceneGpuFixture(output);var(document,camera,_,_)=fixture.Scene();
        string gameplay=Path.Combine(repo.FullName,"out/managed/Ncma.Gameplay.Sample.dll");
        File.Copy(gameplay,Path.Combine(fixture.Root,"gameplay.dll"));File.Copy(Path.ChangeExtension(gameplay,".deps.json"),Path.Combine(fixture.Root,"gameplay.deps.json"));
        SceneDocumentFiles.Save(document,Path.Combine(fixture.Root,"start.ncmascene"));
        string projectPath=Path.Combine(fixture.Root,"project.ncmaproject");
        File.WriteAllBytes(projectPath,JsonSerializer.SerializeToUtf8Bytes(new ProjectConfiguration(1,fixture.Project,"Editor lifecycle","start.ncmascene","gameplay.dll","Direct3D11",[],SceneCamera:camera),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        int cases=0;void Pass(bool ok,string label){Check(ok,"M7.6 Editor lifetime: "+label);cases++;}
        void BusyClose(ApplicationLifetime lifetime) {
            AggregateException? failure=null;try{lifetime.Dispose();}catch(AggregateException error){failure=error;}
            Pass(failure?.Flatten().InnerExceptions is{Count:1} errors&&errors[0] is PluginException{Result:PluginResult.Busy},"actual close failure is exactly native Busy, not a synthetic exception");
        }
        byte[]? reference=null;Guid originalWorld;
        using(var app=new ApplicationLifetime()) {
            var editor=new CandidatePresentation(plugins,ProjectContext.Load(projectPath),true,false,false);
            app.Start([editor]);originalWorld=editor.VerificationEditor.Document.World.Identity;
            var renderer=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");
            var surface=EnvironmentProductEvidence.Field<RendererWindowSurface>(editor,"_windowSurface");
            Reject(editor.AbandonFrameForShutdown);Pass(app.State==ApplicationState.Running,"running Editor cannot abandon a frame");
            editor.CaptureNextCompositeViewportForVerification((pixels,_,_)=>{reference=pixels;throw new InvalidOperationException("private-editor-frame-cause");});
            Reject(()=>app.Run(new MonotonicClock(),editor,2));var primary=app.Failure;var presentation=surface.Status.Failure;
            Pass(reference is{Length:>0}&&primary is{Code:"application_frame_failed"}&&renderer.ReadFrameState().Active,"ordinary application Run fails after actual Editor GPU submission");
            Pass(presentation is{Frame:>0,Code:"host_presentation_failed"},"surface records the first presentation fault");
            BusyClose(app);
            Reject(()=>editor.ReviewGraphicsRecovery(true));
            Pass(app.RetainedServiceCount==1&&app.ShutdownAttempts==1&&app.ShutdownFailure is{Code:"application_shutdown_failed"}&&surface.Status.Phase==RenderWindowPhase.Closing,"actual native active-frame Busy retains the entire application service");
            Pass(PinLocked(fixture.GenerationPath),"failed whole-application close retains exact CPU asset pin");
            BusyClose(app);Pass(app.ShutdownAttempts==2&&app.RetainedServiceCount==1&&ReferenceEquals(primary,app.Failure)&&surface.Status.Failure==presentation,"failed close retry preserves owners and both primary diagnostics");
            Task.Run(()=>Reject(app.Dispose)).GetAwaiter().GetResult();
            Task.Run(()=>Reject(editor.AbandonFrameForShutdown)).GetAwaiter().GetResult();
            Pass(app.ShutdownAttempts==2&&renderer.ReadFrameState().Active,"foreign-thread recovery cannot alter retained native frame");
            Reject(()=>app.Run(new MonotonicClock(),editor,1));Reject(()=>app.Start([]));
            editor.AbandonFrameForShutdown();var frame=renderer.ReadFrameState();
            Pass(frame.Failed&&!frame.Active&&frame.AbandonedFrames==1,"explicit public application-owned terminal close path never Presents");
            app.Dispose();app.Dispose();
            Pass(app.RetainedServiceCount==0&&app.ShutdownAttempts==3&&surface.Status.Phase==RenderWindowPhase.Closed,"ordered retry drains actual Editor dependencies exactly once");
            Pass(ReferenceEquals(primary,app.Failure)&&surface.Status.Failure==presentation&&!JsonSerializer.Serialize(new{app.Failure,app.ShutdownFailure,presentation}).Contains("private-editor"),"closed diagnostics preserve first cause without private callback text");
            Pass(!PinLocked(fixture.GenerationPath),"successful whole-application close releases asset pin");
            Reject(()=>_=renderer.Stats);Reject(editor.AbandonFrameForShutdown);
        }
        using(var app=new ApplicationLifetime()) {
            var editor=new CandidatePresentation(plugins,ProjectContext.Load(projectPath),true,false,false);app.Start([editor]);
            byte[]? restored=null;editor.CaptureNextCompositeViewportForVerification((pixels,_,_)=>restored=pixels);
            Pass(app.Run(new MonotonicClock(),editor,2)==2&&reference is not null&&restored is not null&&reference.SequenceEqual(restored),"fresh approved startup reproduces exact viewport after failed generation drains");
            var renderer=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");
            Pass(editor.VerificationEditor.Document.World.Identity!=originalWorld&&renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"new managed World; old owner cannot access new renderer; API0/0");
            app.Dispose();Pass(app.State==ApplicationState.Stopped&&app.Failure is null&&app.RetainedServiceCount==0,"normal application startup/run/close remains clean");
        }
        using(var app=new ApplicationLifetime()) {
            CandidatePresentation? editor=null;RendererSession? renderer=null;int factoryCalls=0;
            editor=new(plugins,ProjectContext.Load(projectPath),true,false,false,pipelineFactory:(_,_,_)=>{
                factoryCalls++;renderer=EnvironmentProductEvidence.Field<RendererSession>(editor!,"_renderer");
                // Use the real already-owned service, not an unattached test resource.
                // A clear-only renderer may close safely after waiting; a live group
                // has the native active-frame retirement guard this case exercises.
                var service=EnvironmentProductEvidence.Field<RenderPipelineService>(editor!,"_renderService");
                service.Configure(RenderConfiguration.Default(Guid.NewGuid()),1280,720);service.Submit(1);
                throw new InvalidOperationException("private-editor-initialize-cause");
            });
            AggregateException? initialization=null;try{app.Start([editor]);}catch(AggregateException error){initialization=error;}
            Pass(initialization?.Flatten().InnerExceptions.Any(e=>e is PluginException{Result:PluginResult.Busy})==true,"startup primary and real native Busy are both reported");var primary=app.Failure;
            Pass(factoryCalls==1&&primary is{Code:"application_initialize_failed"}&&app.ShutdownFailure is not null&&app.RetainedServiceCount==1&&app.ShutdownAttempts==1,"actual trusted startup factory failure plus native Busy retains partial Editor");
            Pass(renderer is not null&&renderer.ReadFrameState().Active,"startup cleanup does not silently abandon an active native frame");
            BusyClose(app);Pass(ReferenceEquals(primary,app.Failure)&&app.ShutdownAttempts==2,"startup cleanup retry never replaces initialize cause");
            editor.AbandonFrameForShutdown();app.Dispose();
            Pass(app.RetainedServiceCount==0&&ReferenceEquals(primary,app.Failure)&&app.ShutdownAttempts==3,"explicit partial-startup close drains all owners without restarting initialization");
        }
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{cases,ordinaryApplicationLifetime=true,actualEditorDx11=true,physicalDeviceLoss=false,cliInjectedFailure=false,wholeStageAccepted=false}));
        Console.WriteLine($"PASS M7.6 Editor application lifetime {cases} checks; {output}");return 0;
    }
}
