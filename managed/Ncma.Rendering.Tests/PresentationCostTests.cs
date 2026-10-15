using System.Numerics;
using System.Text.Json;
using Ncma.Application;
using Ncma.Editor.App;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Player.App;
using Ncma.Rendering;
using Ncma.Scene;

internal static unsafe partial class Program
{
    private static int PresentationCostTests(string plugins,string output)
    {
        plugins=Path.GetFullPath(plugins);output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        int checks=0;void Pass(bool ok,string text){Check(ok,"M7.7 costs: "+text);checks++;}
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
                Pass(capture.Last is{Completed:false,Counters:null,Main:null,Skin:null,Ui:null},"failure records unknown cost without native reads or stale success");
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
        var options=PlayerOptions.Parse(["--project",project,"--ticks","3","--presentation-costs","--report",Path.Combine(output,"player.json")]);
        Pass(options.PresentationCosts,"explicit CLI opt-in");Reject(()=>PlayerOptions.Parse(["--project",project,"--presentation-costs","--presentation-costs"]));
        using(var runner=new PlayerRunner()){
            var report=runner.RunForVerification(options,plugins,(_,_,_)=>{});
            Pass(report.ExitCode==0&&report.PresentationCosts is{Completed:true,IncludesDiagnosticCapture:true}&&report.PresentationCosts.Value.Counters is{MainSubmissions:1,Presents:1},"formal Player copies last presentation observation into durable report before close");
            using var saved=JsonDocument.Parse(File.ReadAllBytes(options.Report));Pass(saved.RootElement.GetProperty("presentationCosts").GetProperty("version").GetInt32()==1,"actual report serialization");
            var disabled=runner.Run(options with{PresentationCosts=false,Report=Path.Combine(output,"disabled.json")},pluginRoot:plugins,visible:false);
            Pass(disabled.ExitCode==0&&disabled.PresentationCosts is null,"default path has no sampler/report");
            var headless=runner.Run(options with{Headless=true,Report=Path.Combine(output,"null.json")},pluginRoot:plugins,visible:false);
            Pass(headless.ExitCode==0&&headless.Tick==3&&headless.Modules.Length==0&&headless.PresentationCosts is null,"Null opt-in never loads GPU for telemetry");
        }
        using(var editor=new CandidatePresentation(plugins,ProjectContext.Load(project),true,false,false,presentationCosts:true)){
            editor.Start();ulong frame=0;byte[] Draw(){byte[]? pixels=null;editor.CaptureNextViewportForVerification((p,_,_)=>pixels=p);Pass(editor.PumpPlatform(),"Editor pump");var f=new FrameContext(++frame,0);editor.ApplyEditorIntents(f);editor.PumpAgent(f);editor.BeginPresentation(f);editor.SubmitInput(f);editor.AdvancePlay(f);editor.Present(f);return pixels??throw new Exception("Missing pixels");}
            var pixels=Draw();var first=editor.LastPresentationCosts!.Value;
            Pass(first.Completed&&first.IncludesDiagnosticCapture&&first.Counters is{Presents:1}&&first.HostFrame==1,"formal Editor present scope");
            editor.RebuildGraphics(editor.ReviewGraphicsRecovery());Pass(pixels.SequenceEqual(Draw()),"telemetry recovery preserves actual scene image");
            var second=editor.LastPresentationCosts!.Value;
            Pass(second.Generation!=first.Generation&&second.HostFrame==2&&second.Main?.Sample.Timing.Generation==second.Generation,"Editor rebuild resets sampler to actual generation");
            var r=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");Pass(r.Stats.ValidationErrors==0&&r.Stats.ValidationWarnings==0,"Editor cost capture API0/0");editor.Dispose();Pass(editor.LastPresentationCosts==second,"Editor keeps only copied report after close");
        }
        using(var pins=Ncma.Scene.Rendering.SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,document.CaptureSnapshot(),true))
        using(var play=new Ncma.Gameplay.PlaySession(document))
        using(var player=new PlayerPresentation(plugins,false,presentationCosts:true)){
            player.Start();player.PrepareBeforeGameplay(document,pins);play.Start(_=>throw new Exception("No behaviours"));player.BindScene(play,pins,camera);
            byte[] Draw(){byte[]? pixels=null;player.Pump(play);player.Present(play,(p,_,_)=>pixels=p);return pixels??throw new Exception("No pixels");}
            var pixels=Draw();var first=player.LastPresentationCosts!.Value;
            player.RebuildGraphics(player.ReviewGraphicsRecovery());Pass(pixels.SequenceEqual(Draw()),"Player cost-enabled new-generation rebuild preserves pixels");
            Pass(player.LastPresentationCosts!.Value.Generation!=first.Generation,"Player old generation never reused");
            player.Pump(play);Reject(()=>player.Present(play,(_,_,_)=>throw new InvalidOperationException("injected trusted capture failure")));
            var failed=player.LastPresentationCosts;
            Pass(failed is{Completed:false,Main:null,Skin:null,Ui:null,Counters:null},"actual host failure replaces previous success with unknown counters/samples");
            player.RebuildGraphics(player.ReviewGraphicsRecovery(true));Pass(pixels.SequenceEqual(Draw())&&player.LastPresentationCosts is{Completed:true},"explicit failure recovery resumes cost capture without resetting gameplay");
            Pass(player.Stats.ValidationErrors==0&&player.Stats.ValidationWarnings==0,"Player failure/recovery API0/0");
        }
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{checks,allocation,retained,fullPerformanceAccepted=false}));
        Console.WriteLine($"PASS M7.7 presentation costs {checks} checks; {output}");return 0;
    }
}
