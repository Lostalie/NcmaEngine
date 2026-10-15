using System.Text.Json;
using Ncma.Application;
using Ncma.Interop;
using Ncma.Player.App;
using Ncma.Rendering;
using Ncma.Scene;

internal static unsafe partial class Program
{
    private static int PlayerRunnerLifetimeTests(string plugins,string output)
    {
        plugins=Path.GetFullPath(plugins);output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        var repo=new DirectoryInfo(plugins);while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent??throw new Exception("Repo missing");
        string config=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var fixture=new SceneGpuFixture(output);var(document,camera,character,_)=fixture.Scene();
        string gameplay=Path.Combine(repo.FullName,"managed/Ncma.Player.Tests/bin",config,"net8.0/Ncma.Player.Tests.dll");
        File.Copy(gameplay,Path.Combine(fixture.Root,"gameplay.dll"));File.Copy(Path.ChangeExtension(gameplay,".deps.json"),Path.Combine(fixture.Root,"gameplay.deps.json"));
        string scenePath=Path.Combine(fixture.Root,"start.ncmascene"),projectPath=Path.Combine(fixture.Root,"project.ncmaproject");
        Guid binding=Guid.NewGuid();void Save(bool fail){document.SetBindings(character,[new(binding,"PlayerProbe",true,[new("CloseFails",ExportKind.Boolean,fail?1:0)])]);SceneDocumentFiles.Save(document,scenePath);}
        var project=new ProjectConfiguration(1,fixture.Project,"Runner owner","start.ncmascene","gameplay.dll","Direct3D11",[],PhysicsEnabled:true,SceneCamera:camera);
        File.WriteAllBytes(projectPath,JsonSerializer.SerializeToUtf8Bytes(project,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        PlayerOptions Options(bool headless)=>new(projectPath,headless,null,2,1d/60,30,Path.Combine(output,Guid.NewGuid()+".json"),false,false);
        int cases=0;void Pass(bool ok,string label){Check(ok,"M7.6 runner lifetime: "+label);cases++;}
        bool Empty(PlayerRunnerStatus s)=>!s.PresentationRetained&&!s.RuntimeRetained&&!s.PhysicsRetained&&!s.AssetPinsRetained&&!s.ProgramPinsRetained;
        foreach(bool headless in new[]{true,false}) {
            Save(true);using var runner=new PlayerRunner();var options=Options(headless);
            Pass(runner.Status.Phase==PlayerRunnerPhase.Idle&&Empty(runner.Status),"constructor has no plugins/World/resources");
            var report=runner.Run(options,pluginRoot:plugins,visible:false);byte[] bytes=File.ReadAllBytes(options.Report);var retained=runner.Status;
            Pass(report.ExitCode==6&&report.ShutdownErrors.SequenceEqual(new[]{"runtime_shutdown_failed"})&&report.Tick>=2,"ordinary full Run surfaces actual gameplay shutdown failure");
            Pass(retained is{Phase:PlayerRunnerPhase.Retained,RuntimeRetained:true,PhysicsRetained:true,PresentationRetained:false,ShutdownAttempts:1}&&retained.FailedService=="runtime_shutdown_failed"&&retained.AssetPinsRetained==!headless,"caller retains runtime and downstream native/assets after report return");
            Pass(PinLocked(fixture.GenerationPath)==!headless,"actual graphical source generation remains pinned after failed Run; Null has no render pin");
            Pass(runner.LastPresentationFailure is null,"gameplay shutdown failure is not misclassified as a presentation fault");
            var denied=Options(headless);Reject(()=>runner.Run(denied,pluginRoot:plugins,visible:false));
            Pass(!File.Exists(denied.Report)&&runner.Status==retained,"failed lifetime forbids next run without overwriting report/resources");
            Task.Run(()=>Reject(runner.RetryShutdown)).GetAwaiter().GetResult();Pass(runner.Status==retained,"foreign-thread close rejected before changing ownership");
            runner.RetryShutdown();Pass(runner.Status.Phase==PlayerRunnerPhase.Idle&&runner.Status.ShutdownAttempts==2&&Empty(runner.Status),"explicit full-Run close retry drains dependency chain");
            Pass(!PinLocked(fixture.GenerationPath),"successful close releases actual asset generation pin");
            Pass(ReferenceEquals(report,runner.LastReport)&&bytes.SequenceEqual(File.ReadAllBytes(options.Report))&&!System.Text.Encoding.UTF8.GetString(bytes).Contains("private-secret"),"retry preserves immutable original failure evidence and redaction");
            Save(false);var healthy=runner.Run(Options(headless),pluginRoot:plugins,visible:false);
            Pass(healthy.ExitCode==0&&(headless?healthy.Tick==2:healthy.Tick>=2)&&healthy.SessionId!=report.SessionId&&healthy.WorldId!=report.WorldId&&runner.Status.Generation==2&&Empty(runner.Status),"fresh run gets new World/session only after complete drain");
            Pass(healthy.ValidationErrors==0&&healthy.ValidationWarnings==0&&healthy.ShutdownErrors.Length==0,"healthy actual graphical/null acceptance");
            runner.Dispose();Reject(()=>runner.Run(Options(headless),pluginRoot:plugins,visible:false));Pass(runner.Status.Phase==PlayerRunnerPhase.Disposed,"disposed owner never restarts");
        }
        foreach(int action in new[]{0,1,2,3}) {
            Save(false);using var runner=new PlayerRunner();var nested=Options(true);bool fired=false;
            var report=runner.Run(Options(true),()=>{fired=true;try{if(action==0)runner.Run(nested);else if(action==1)runner.Dispose();else if(action==2)runner.RetryShutdown();else runner.AbandonFrameAndRetryShutdown();}catch(InvalidOperationException){}return false;},pluginRoot:plugins,visible:false);
            Pass(fired&&report.ExitCode!=0&&report.Reason=="host_callback_reentry"&&report.Tick==0&&!File.Exists(nested.Report),"caught control reentry invalidates outer run before simulation");
            Pass(runner.Status.Phase==PlayerRunnerPhase.Idle&&Empty(runner.Status)&&report.ShutdownErrors.Length==0,"rejected callback still has ordered clean shutdown");
        }
        // Whole Run, ordinary bounded screenshot hook throws after a real Submit.
        // No owner-field attachment is used. Native active-frame Busy is retained.
        // The public explicit shutdown operation abandons without Present, then
        // follows normal bounded resource retirement; no reflection recovery.
        Save(false);using(var runner=new PlayerRunner()) {
            int captures=0;var report=runner.RunForVerification(Options(false),plugins,(_,_,_)=>{captures++;throw new InvalidOperationException("private capture failure");});
            var retained=runner.Status;Pass(captures==1&&report.ExitCode==5&&report.ShutdownErrors.SequenceEqual(new[]{"presentation_shutdown_failed"})&&retained is{Phase:PlayerRunnerPhase.Retained,PresentationRetained:true,RuntimeRetained:true,PhysicsRetained:true,AssetPinsRetained:true,NativeResult:PluginResult.Busy},"actual full Run keeps graphical owner after pending-frame close fails");
            var primary=runner.LastPresentationFailure;
            Pass(primary is{RendererGeneration:>0,Frame:>0,NativeResult:null,Operation:"presentation",Code:"host_presentation_failed"}&&!JsonSerializer.Serialize(primary).Contains("private capture"),"full Run copies first presentation fault without callback text or later native Busy");
            Pass(PinLocked(fixture.GenerationPath),"pending native frame retains the exact CPU asset pin after Run returns");
            PluginException? failure=null;try{runner.RetryShutdown();}catch(PluginException error){failure=error;}
            Pass(failure is{Result:PluginResult.Busy}&&runner.Status.PresentationRetained&&runner.Status.ShutdownAttempts==2,"retry does not bypass native active-frame guard");
            Pass(runner.LastPresentationFailure==primary,"failed close retry preserves copied primary presentation evidence");
            Task.Run(()=>Reject(runner.AbandonFrameAndRetryShutdown)).GetAwaiter().GetResult();Pass(runner.Status.Phase==PlayerRunnerPhase.Retained&&runner.Status.ShutdownAttempts==2,"foreign thread cannot abandon retained frame");
            runner.AbandonFrameAndRetryShutdown();Pass(runner.Status.Phase==PlayerRunnerPhase.Idle&&Empty(runner.Status)&&runner.Status.ShutdownAttempts==3,"public same-owner abandon and close drains full Run without Present");
            Pass(!PinLocked(fixture.GenerationPath),"terminal frame close releases actual asset generation pin");
            Pass(runner.LastPresentationFailure==primary,"successful terminal abandon and close do not erase the original presentation failure");
            Reject(runner.AbandonFrameAndRetryShutdown);Pass(ReferenceEquals(report,runner.LastReport),"no arbitrary abandon/reset and original report remains failure evidence");
            var healthy=runner.Run(Options(false),pluginRoot:plugins,visible:false);Pass(healthy.ExitCode==0&&healthy.ValidationErrors==0&&healthy.ValidationWarnings==0&&healthy.WorldId!=report.WorldId&&Empty(runner.Status),"fresh renderer/World after explicit terminal abandonment; API0/0");
            Pass(runner.LastPresentationFailure is null,"new run resets previous generation presentation diagnostic");
        }
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{cases,ordinaryRunShutdownRetry=true,graphicalFrameCompletion="public explicit terminal abandon; no Present",deviceLossRecovery=false,fullStageAccepted=false}));
        Console.WriteLine($"PASS M7.6 PlayerRunner lifetime {cases} checks; {output}");return 0;
    }
}
