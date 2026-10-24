using System.Numerics;
using System.Text.Json;
using Ncma.Application;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Gameplay;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Player.App;
using Ncma.Rendering;
using Ncma.Scene;

internal static unsafe partial class Program
{
    private static int PresentationCostTests(string plugins,string output)
    {
        EcsDomainCostTests.Run();
        plugins=Path.GetFullPath(plugins);output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        int checks=0;void Pass(bool ok,string text){Check(ok,"M7.7 costs: "+text);checks++;}
        string[] valueOptions=["--project","--plugins","--frames","--max-fps","--loop-stream"];
        Pass(valueOptions.All(option=>!Ncma.Editor.App.Program.StartupCostsRequested([option,"--startup-costs"])),
            "startup opt-in ignores flag text in every value-option position");
        Pass(valueOptions.All(option=>Ncma.Editor.App.Program.StartupCostsRequested([option,"--startup-costs","--startup-costs"])),
            "real startup opt-in after a flag-shaped value remains recognized");
        Pass(!Ncma.Editor.App.Program.StartupCostsRequested([])&&!Ncma.Editor.App.Program.StartupCostsRequested(["--cpu-only"])&&
            Ncma.Editor.App.Program.StartupCostsRequested(["--startup-costs"]),"startup observation defaults off and requires actual option");
        PresentationCostReport retained=default;long allocation;
        using(var loader=new PluginLoader()){
            loader.Load(plugins,Specs().Take(2).ToArray());var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
            using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Costs",64,64,false);
            using(var renderer=new RendererSession(native,window,64,64,pureUi:true)){
                var capture=new PresentationCostCapture(renderer);
                Pass(capture.Last is null&&renderer.SkinStats.Meshes==0&&renderer.UiTargetStats.Targets==0&&renderer.PipelineStats.Pipelines==0&&renderer.Stats.LiveGroups==0,"initialization does not create workload resources");
                Task.Run(()=>Reject(()=>capture.Begin(1))).GetAwaiter().GetResult();Reject(()=>capture.Begin(0));Reject(()=>capture.End());
                capture.Begin(1,true);Reject(()=>capture.Begin(2));
                using var white=renderer.CreateUiImage(1,1,[255,255,255,255]);var builder=new UiDisplayListBuilder(renderer);
                builder.Quad(new(Guid.NewGuid(),new(0,0,24,24),Matrix3x2.Identity,new(0,0,64,64),1,true,-1),white,new(1,0,0,1));
                using var list=builder.Build();renderer.SubmitUi(list,1,Vector4.Zero);renderer.Present();renderer.WaitIdle();
                retained=capture.End();
                var lateObserver=new PresentationCostCapture(renderer);Reject(()=>lateObserver.Begin(1));
                Pass(lateObserver.Last is null,"new observer rejects already-submitted host frame before measuring");
                Pass(retained.Completed&&retained.Counters is{MainSubmissions:1,Presents:1,UiDrawSubmissions:1,SkinBatches:0,UiProductions:0}&&retained.Counters.Value.UiUploadedBytes>0,"selected counters are deltas including actual uploads");
                Pass(retained.Main is{Attribution:CostSampleAttribution.CurrentSourceFrame}&&retained.Main.Value.Sample.Timing.OriginFrame==1&&retained.Skin?.Attribution==CostSampleAttribution.NoValidSample,"exact same-device source attribution");
                Pass(retained.Version==2&&retained.MainResolved==retained.Main&&retained.SkinResolved?.Sample.Timing.State==GpuSampleState.Unavailable&&retained.UiResolved?.Sample.Timing.State==GpuSampleState.Unavailable,"both snapshot slots copied; absent workloads not fabricated");
                Pass(retained.IncludesDiagnosticCapture&&retained.CpuWallMilliseconds>=0&&retained.OwnerThreadAllocatedBytes>=0&&!retained.FullPerformanceAccepted,"CPU allocation/synchronized diagnostic scope explicit");
                capture.Begin(2);var idle=capture.End();
                Pass(idle.Counters==default(PresentationCounters)&&idle.Main is{Attribution:CostSampleAttribution.EarlierSourceFrame}&&idle.Main.Value.Sample.Timing.OriginFrame==1,"cached valid sample is never relabeled current idle frame");
                Reject(()=>capture.Begin(2));
                for(ulong i=3;i<35;i++){capture.Begin(i);capture.End();}
                long before=GC.GetAllocatedBytesForCurrentThread();for(ulong i=35;i<163;i++){capture.Begin(i);capture.End();}
                allocation=GC.GetAllocatedBytesForCurrentThread()-before;
                Pass(allocation==0,"warm128 bounded observations allocate zero on owner thread, not whole host");
                Pass(renderer.Stats.Presents==1&&renderer.PipelineStats.Pipelines==0&&renderer.SkinStats.Meshes==0&&renderer.EnvironmentStats.Live==0&&renderer.PostStats.Programs==0,"observation adds no draws/3D initialization");
                capture.Begin(200);renderer.SubmitUi(list,200,Vector4.Zero);Reject(()=>capture.End());renderer.AbandonFrameForShutdown(200);capture.Abort();
                Pass(capture.Last is{Completed:false,Counters:null,Main:null,Skin:null,Ui:null,MainResolved:null,SkinResolved:null,UiResolved:null},"failure records unknown cost without native reads or stale success");
                Reject(()=>capture.End());
                Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"pure2D actual API0/0");
            }
            using(var fresh=new RendererSession(native,window,64,64,pureUi:true)){
                var capture=new PresentationCostCapture(fresh);capture.Begin(201);var result=capture.End();
                Pass(result.Generation!=retained.Generation&&result.Main?.Attribution==CostSampleAttribution.NoValidSample,"new device does not inherit old cost sample");
            }
            Pass(native.Status.LiveResources==0,"capture does not own or retain native resources");
        }
        Pass(retained.Main?.Sample.Timing.OriginFrame==1,"owned report survives DLL unload");
        var repo=new DirectoryInfo(plugins);while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent??throw new Exception("Repo missing");
        var fixture=new SceneGpuFixture(output);var(document,camera,_,_)=fixture.Scene();
        string gameplay=Path.Combine(repo.FullName,"out/managed/Ncma.Gameplay.Sample.dll");File.Copy(gameplay,Path.Combine(fixture.Root,"gameplay.dll"));File.Copy(Path.ChangeExtension(gameplay,".deps.json"),Path.Combine(fixture.Root,"gameplay.deps.json"));
        SceneDocumentFiles.Save(document,Path.Combine(fixture.Root,"start.ncmascene"));string project=Path.Combine(fixture.Root,"project.ncmaproject");
        File.WriteAllBytes(project,JsonSerializer.SerializeToUtf8Bytes(new ProjectConfiguration(1,fixture.Project,"Costs","start.ncmascene","gameplay.dll","Direct3D11",[],SceneCamera:camera),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        var options=PlayerOptions.Parse(["--project",project,"--ticks","3","--presentation-costs","--presentation-stages","--simulation-costs","--loop-costs","--report",Path.Combine(output,"player.json")]);
        PlayerWindowChecks(options,plugins,output,Pass);
        Pass(options.PresentationCosts,"explicit CLI opt-in");Reject(()=>PlayerOptions.Parse(["--project",project,"--presentation-costs","--presentation-costs"]));
        using(var runner=new PlayerRunner()){
            var report=runner.RunForVerification(options,plugins,(_,_,_)=>{});
            Pass(report.ExitCode==0&&report.PresentationCosts is{Completed:true,IncludesDiagnosticCapture:true}&&report.PresentationCosts.Value.Counters is{MainSubmissions:1,Presents:1},"formal Player copies last presentation observation into durable report before close");
            Pass(report.SimulationCosts is{Advance:{Completed:true,Kind:SimulationAdvanceKind.Frame,IncludesUpdate:true}}&&report.SimulationCosts.Value.Advance.TickAfter==report.Tick,"actual DX11 Player separately copies running simulation scope");
            Pass(report.LoopCosts is {PresentReturns:>0,OmittedIterations:0}&&report.LoopCosts.PresentReturns==report.LoopCosts.TotalIterations&&
                report.LoopCosts.Samples.All(s=>s.PresentReturnTimestamp is not null&&s.Outcome==LoopCostOutcome.Completed),"actual Player loop records successful native Present returns");
            using var saved=JsonDocument.Parse(File.ReadAllBytes(options.Report));
            var savedCosts=saved.RootElement.GetProperty("presentationCosts");
            Pass(savedCosts.GetProperty("version").GetInt32()==2&&savedCosts.GetProperty("mainResolved").ValueKind==JsonValueKind.Object,"actual versioned snapshot report serialization");
            var disabled=runner.Run(options with{PresentationCosts=false,SimulationCosts=false,LoopCosts=false,Report=Path.Combine(output,"disabled.json")},pluginRoot:plugins,visible:false);
            Pass(disabled.ExitCode==0&&disabled.PresentationCosts is null&&disabled.SimulationCosts is null&&disabled.LoopCosts is null,"default path has no sampler/report");
            var headless=runner.Run(options with{Headless=true,Report=Path.Combine(output,"null.json")},pluginRoot:plugins,visible:false);
            Pass(headless.ExitCode==0&&headless.Tick==3&&headless.Modules.Length==0&&headless.PresentationCosts is null,"Null opt-in never loads GPU for telemetry");
            Pass(headless.SimulationCosts is{LastCharacterStep:null,Advance:{Completed:true,Kind:SimulationAdvanceKind.FixedStep,TickAfter:3,IncludesUpdate:false}},"Null simulation remains separate from rendering");
            Pass(headless.LoopCosts is {TotalIterations:3,PresentReturns:0},"headless real loop has no fabricated Present interval");
            var capped=runner.Run(options with{Ticks=6,FrameRateLimit=30,Report=Path.Combine(output,"player-capped.json")},pluginRoot:plugins,visible:false);
            Pass(capped.ExitCode==0&&capped.Tick>=6&&capped.FixedDeltaSeconds==options.FixedDelta&&capped.StateSha256==report.StateSha256&&
                capped.FrameRateLimit==30&&capped.LoopCosts is {Version:2,FrameRateLimit:30},"capped graphical Player preserves scene and fixed-step policy");
            var rows=capped.LoopCosts!.Samples;
            long minimum=1+(capped.LoopCosts.TimestampFrequency-1)/30;
            Pass(rows.Length>=2&&rows.All(r=>r.PacingAdmissionTimestamp is not null)&&
                rows.Skip(1).Select((r,i)=>r.PacingAdmissionTimestamp-rows[i].PacingAdmissionTimestamp).All(t=>t>=minimum),"actual graphical Player admits no catch-up burst");
            int cancelPolls=0,inputs=0;
            var cancelled=runner.Run(options with{Ticks=600,FrameRateLimit=30,Report=Path.Combine(output,"player-cap-cancelled.json")},
                ()=>++cancelPolls>=4,pluginRoot:plugins,visible:false,trustedInput:(_,_)=>inputs++);
            Pass(cancelled.ExitCode==130&&inputs==1&&cancelled.LoopCosts!.Samples.Last() is {Outcome:LoopCostOutcome.Stopped,PacingAdmissionTimestamp:null},
                "cancellation inside paced wait never submits another input or gameplay frame");
        }
        foreach(bool cpuOnly in new[]{false,true}) {
            var stream=LoopCostStream.Open(Path.Combine(output,cpuOnly?"editor-cpu.ncloop":"editor-dx11.ncloop"),30);
            var loops=new LoopCostCapture(frameRateLimit:30,stream:stream);
            var stages=new PresentationStageCapture();
            var startup=new StartupCostCapture();
            using var lifetime=new ApplicationLifetime();
            var editor=new CandidatePresentation(plugins,ProjectContext.Load(project),true,cpuOnly,false,loopCosts:loops,frameRateLimit:30,stages:stages,startup:startup);
            lifetime.Start([editor]);
            var startupReport=startup.Capture();
            Pass(startupReport is {Version:1,PerformanceAccepted:false,Calls:[{Call:StartupCall.PresentationStart,Completed:true}]},"service startup is separate from Main and loop");
            var startupCall=startupReport.Calls.Single();
            Pass(startupCall.Stages.Length==24&&startupCall.Stages.Sum(s=>s.WallTicks)==startupCall.EndedTimestamp-startupCall.StartedTimestamp&&
                startupCall.Stages.Sum(s=>s.OwnerAllocatedBytes)==startupCall.OwnerAllocatedBytes,"startup wall and raw allocation partition exactly");
            Pass(startupCall.Stages.Single(s=>s.Stage==StartupStage.GuiCreate).Visits==1&&
                startupCall.Stages.Single(s=>s.Stage==StartupStage.Environment).Visits==(cpuOnly?0:1),"startup includes GUI create; CPU-only does not initialize scene environment");
            File.WriteAllText(Path.Combine(output,cpuOnly?"editor-cpu-startup.json":"editor-dx11-startup.json"),JsonSerializer.Serialize(startupReport));
            JsonElement Preparation() {
                var loader=EnvironmentProductEvidence.Field<PluginLoader>(editor,"_loader");
                using var parsed=JsonDocument.Parse(loader.Modules.Single(m=>m.Kind==ModuleKind.Gui).ReadDiagnostics());
                return parsed.RootElement.GetProperty("guiPreparation").Clone();
            }
            var unprepared=Preparation();
            Pass(unprepared.GetProperty("attempts").GetUInt64()==0&&!unprepared.GetProperty("ready").GetBoolean(),"startup does not hide lazy GUI preparation outside Begin");
            Pass(EditorRunLoop.Run(lifetime,editor,new MonotonicClock(),4,costs:loops,pacer:editor.Pacer)==4,"actual capped Editor product loop");
            var result=loops.Capture();
            Pass(result.TotalIterations==4&&result.Samples.All(s=>s.Outcome==LoopCostOutcome.Completed)&&
                result.PresentReturns==(cpuOnly?0UL:4UL)&&result.PresentIntervals==(cpuOnly?0UL:3UL),"Editor CPU-only/graphical return attribution");
            Pass(result.FrameRateLimit==30&&result.Samples.All(r=>r.PacingAdmissionTimestamp is not null)&&
                result.Samples.Skip(1).Select((r,i)=>r.PacingAdmissionTimestamp-result.Samples[i].PacingAdmissionTimestamp).All(t=>t>=1+(result.TimestampFrequency-1)/30),
                "actual Editor cap controls admission, not fixed simulation ticks or physical display");
            var prepared=Preparation();
            Pass(prepared.GetProperty("attempts").GetUInt64()==(cpuOnly?0UL:1UL)&&prepared.GetProperty("failures").GetUInt64()==0&&
                prepared.GetProperty("ready").GetBoolean()==!cpuOnly&&!prepared.GetProperty("performanceAccepted").GetBoolean(),"actual product DLL prepares once, CPU-only never prepares DX11");
            ulong atlasPixels=prepared.GetProperty("atlasWidth").GetUInt64()*prepared.GetProperty("atlasHeight").GetUInt64();
            Pass(atlasPixels>0&&prepared.GetProperty("atlasUploadBytes").GetUInt64()==(cpuOnly?0UL:atlasPixels),
                "actual monochrome GUI reports one byte per atlas pixel; CPU-only has zero upload");
            File.WriteAllText(Path.Combine(output,cpuOnly?"editor-cpu-preparation.json":"editor-dx11-preparation.json"),JsonSerializer.Serialize(new{unprepared,prepared}));
            lifetime.Dispose();
            Pass(JsonSerializer.Serialize(startup.Capture())==JsonSerializer.Serialize(startupReport),"startup report does not include loop or shutdown");
            Pass(stream.Complete() is {CompleteRawCoverage:true,Written:4},"Editor full stream flushes exact real loop records");
            result=loops.Capture();
            var stageReport=stages.Capture();
            Pass(stageReport.TotalCalls==8&&stageReport.Calls.All(c=>c.Completed)&&stageReport.Calls.Count(c=>c.Call==PresentationCall.BeginPresentation)==4,"actual Editor call prefix after shutdown");
            Pass(stageReport.Calls.Sum(c=>c.Stages.Single(s=>s.Stage==PresentationStage.NativePresent).Visits)==(cpuOnly?0:4),"CPU-only has no native present phase");
            Pass(stageReport.Calls.All(c=>c.Stages.Sum(s=>s.WallTicks)==c.EndedTimestamp-c.StartedTimestamp&&c.Stages.Sum(s=>s.OwnerAllocatedBytes)==c.OwnerAllocatedBytes),"stage wall/allocation partitions");
            File.WriteAllText(Path.Combine(output,cpuOnly?"editor-cpu-stages.json":"editor-dx11-stages.json"),JsonSerializer.Serialize(stageReport));
            using var file=new FileStream(Path.Combine(output,cpuOnly?"editor-cpu-loop.json":"editor-dx11-loop.json"),FileMode.CreateNew);
            JsonSerializer.Serialize(file,result);
        }
        var failedStartup=new StartupCostCapture();
        using(var failedLifetime=new ApplicationLifetime()) {
            var failedEditor=new CandidatePresentation(Path.Combine(output,"missing-plugins"),ProjectContext.Load(project),true,true,false,startup:failedStartup);
            Reject(()=>failedLifetime.Start([failedEditor]));
            var failure=failedStartup.Capture();
            Pass(failure.Calls is [{Call:StartupCall.PresentationStart,Completed:false}]&&failedStartup.ActiveCall is null,
                "actual startup failure retains incomplete call");
            Pass(failure.Calls[0].Stages.Single(s=>s.Stage==StartupStage.LogAndPlugins).Visits==1&&
                failure.Calls[0].Stages.Single(s=>s.Stage==StartupStage.Window).Visits==0&&failedLifetime.RetainedServiceCount==0,
                "failure never fabricates later startup phases and normal rollback releases resources");
            File.WriteAllText(Path.Combine(output,"editor-failed-startup.json"),JsonSerializer.Serialize(failure));
        }
        var editorRecoveryStages=new PresentationStageCapture();
        using(var editor=new CandidatePresentation(plugins,ProjectContext.Load(project),true,false,false,presentationCosts:true,simulationCosts:true,stages:editorRecoveryStages)){
            editor.Start();ulong frame=0;byte[] Draw(double delta=0){byte[]? pixels=null;editor.CaptureNextViewportForVerification((p,_,_)=>pixels=p);Pass(editor.PumpPlatform(),"Editor pump");var f=new FrameContext(++frame,delta);editor.ApplyEditorIntents(f);editor.PumpAgent(f);editor.BeginPresentation(f);editor.SubmitInput(f);editor.AdvancePlay(f);editor.Present(f);return pixels??throw new Exception("Missing pixels");}
            JsonElement Preparation() {
                var loader=EnvironmentProductEvidence.Field<PluginLoader>(editor,"_loader");
                using var parsed=JsonDocument.Parse(loader.Modules.Single(m=>m.Kind==ModuleKind.Gui).ReadDiagnostics());
                return parsed.RootElement.GetProperty("guiPreparation").Clone();
            }
            var pixels=Draw();var first=editor.LastPresentationCosts!.Value;
            var firstPreparation=Preparation();
            Pass(first.Completed&&first.IncludesDiagnosticCapture&&first.Counters is{Presents:1}&&first.HostFrame==1,"formal Editor present scope");
            editor.RebuildGraphics(editor.ReviewGraphicsRecovery());
            var newPreparation=Preparation();
            Pass(newPreparation.GetProperty("gui").GetUInt64()!=firstPreparation.GetProperty("gui").GetUInt64()&&newPreparation.GetProperty("attempts").GetUInt64()==0,"recovery replaces GUI identity and starts unprepared");
            Pass(pixels.SequenceEqual(Draw()),"telemetry recovery preserves actual scene image");
            var recoveredPreparation=Preparation();
            Pass(recoveredPreparation.GetProperty("attempts").GetUInt64()==1&&recoveredPreparation.GetProperty("failures").GetUInt64()==0&&recoveredPreparation.GetProperty("ready").GetBoolean(),"recovered product GUI prepares exactly once");
            File.WriteAllText(Path.Combine(output,"editor-recovery-preparation.json"),JsonSerializer.Serialize(new{firstPreparation,newPreparation,recoveredPreparation}));
            var second=editor.LastPresentationCosts!.Value;
            Pass(second.Generation!=first.Generation&&second.HostFrame==2&&second.Main?.Sample.Timing.Generation==second.Generation,"Editor rebuild resets sampler to actual generation");
            Pass(editor.LastSimulationCosts is null,"Edit preview is not a Play simulation sample");
            var owner=EnvironmentProductEvidence.Field<EditorSessionOwner>(editor,"_editor");
            byte[] editBytes=owner.Document.CaptureBytes();var play=owner.StartPlay();Draw(3d/60);
            Pass(editor.LastSimulationCosts is{Advance:{Completed:true,CommittedSteps:3,IncludesUpdate:true}},"actual Editor Play measures all catch-up quanta");
            play.Pause();var paused=editor.LastSimulationCosts;Draw();
            Pass(editor.LastSimulationCosts==paused,"paused render does not manufacture a new simulation sample; sequence is retained");
            play.Step();Draw();
            Pass(editor.LastSimulationCosts is{Advance:{Kind:SimulationAdvanceKind.PausedStep,CommittedSteps:1,IncludesUpdate:false}},"actual UI-style Step is sampled without host AdvanceFrame");
            Guid previousSession=play.SessionId;owner.ReloadGameplay(ProjectContext.Load(project).GameplayAssemblyPath);
            owner.Play!.Pause();Draw();Pass(editor.LastSimulationCosts is null&&owner.Play.SessionId!=previousSession,"Editor reload clears stale sample before any step");
            owner.Play.Step();Draw();Pass(editor.LastSimulationCosts!.Value.Advance.SessionId==owner.Play.SessionId,"new Editor session owns new report");
            owner.StopPlay();Draw();Pass(editor.LastSimulationCosts is null&&editBytes.SequenceEqual(owner.Document.CaptureBytes()),"Stop clears Play report without editing source scene");
            var r=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");Pass(r.Stats.ValidationErrors==0&&r.Stats.ValidationWarnings==0,"Editor cost capture API0/0");var final=editor.LastPresentationCosts;editor.Dispose();Pass(editor.LastPresentationCosts==final,"Editor keeps only copied report after close");
            Pass(editorRecoveryStages.Capture().Calls.All(c=>c.Completed)&&editorRecoveryStages.Capture().TotalCalls==frame*2,"Editor stage sequence survives recovery/Play/reload/Stop");
        }
        var playerRecoveryStages=new PresentationStageCapture();
        var runtimeScene=document.BakeRuntime(document.Revision).Image.Instantiate(document.Components);
        using(var pins=Ncma.Scene.Rendering.SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,document.CaptureSnapshot(),true))
        using(var play=new Ncma.Gameplay.PlaySession(runtimeScene))
        using(var player=new PlayerPresentation(plugins,false,presentationCosts:true,stages:playerRecoveryStages)){
            player.Start();player.PrepareBeforeGameplay(runtimeScene,pins);play.Start(_=>throw new Exception("No behaviours"));player.BindScene(play,pins,camera);
            byte[] Draw(){byte[]? pixels=null;player.Pump(play);player.Present(play,(p,_,_)=>pixels=p);return pixels??throw new Exception("No pixels");}
            var pixels=Draw();var first=player.LastPresentationCosts!.Value;
            player.RebuildGraphics(player.ReviewGraphicsRecovery());Pass(pixels.SequenceEqual(Draw()),"Player cost-enabled new-generation rebuild preserves pixels");
            Pass(player.LastPresentationCosts!.Value.Generation!=first.Generation,"Player old generation never reused");
            player.Pump(play);Reject(()=>player.Present(play,(_,_,_)=>throw new InvalidOperationException("injected trusted capture failure")));
            var failed=player.LastPresentationCosts;
            Pass(failed is{Completed:false,Main:null,Skin:null,Ui:null,Counters:null},"actual host failure replaces previous success with unknown counters/samples");
            player.RebuildGraphics(player.ReviewGraphicsRecovery(true));Pass(pixels.SequenceEqual(Draw())&&player.LastPresentationCosts is{Completed:true},"explicit failure recovery resumes cost capture without resetting gameplay");
            Pass(player.Stats.ValidationErrors==0&&player.Stats.ValidationWarnings==0,"Player failure/recovery API0/0");
            var trace=playerRecoveryStages.Capture();
            Pass(trace.TotalCalls==4&&trace.Calls.Select(c=>c.Completed).SequenceEqual(new[]{true,true,false,true}),"actual fault row retained across recovery");
            Pass(trace.Calls[2].Stages.Single(s=>s.Stage==PresentationStage.NativePresent).Visits==0&&trace.Calls[2].Stages.Single(s=>s.Stage==PresentationStage.DiagnosticCapture).Visits==1,"capture fault does not fabricate native present");
            File.WriteAllText(Path.Combine(output,"player-recovery-stages.json"),JsonSerializer.Serialize(trace));
        }
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{checks,allocation,retained,fullPerformanceAccepted=false}));
        Console.WriteLine($"PASS M7.7 presentation costs {checks} checks; {output}");return 0;
    }
}
