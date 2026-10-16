using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    private static int SceneExchangeSafetyTests(string plugins,string output)
    { EnvironmentExchangeSafetyTests(plugins,output);EnvironmentStartupSafetyTests(plugins,output);EnvironmentStartupHostSafetyTests(plugins,output);PipelineStartupSafetyTests(plugins,output);return PostExchangeSafetyTests(plugins,output); }
    private static int EnvironmentExchangeSafetyTests(string plugins,string output)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        int cases=0;void Pass(bool ok,string label){Check(ok,"M7.6 exchange: "+label);cases++;}
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(plugins),Specs().Take(2).ToArray());
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Scene exchange safety",64,64,false);
        using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,64,64);
        using var cook=new EnvironmentCookService(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),()=>true);
        var package=cook.Cook(Guid.NewGuid(),1,HdrEnvironmentSource.Neutral(Guid.NewGuid()),new(8,4,8,64));
        var fixture=new SceneGpuFixture(output);var(document,camera,_,_)=fixture.Scene();
        global::Ncma.Runtime.Entity light=document.World.GetEntities().Single(o=>document.World.Has<DirectionalLightData>(o));
        document.World.Set(light, document.World.Get<DirectionalLightData>(light) with{CastShadow=false});
        global::Ncma.Runtime.Entity environment=document.World.CreateEntity("Environment");document.World.Set(environment, new EnvironmentLightingData(1,package.AssetId,package.Generation,package.ContentHash,1,0,true));
        var descriptor=EnvironmentAssetDescriptor.FromPackage(fixture.Project,package);string path=Path.Combine(fixture.Root,descriptor.Path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path,package.CopyBytes());File.WriteAllBytes(Path.Combine(fixture.Root,"assets/environment.ncenv"),descriptor.Encode());
        using var assets=SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,document.CaptureSnapshot(),true);
        using var cache=new RenderResourceCache(renderer);ulong frame=1;bool fault=false,fired=false,framePending=false;
        bool Approved(){if(fault&&!fired&&renderer.PipelineStats.Pipelines==2){fired=true;renderer.SubmitResources(frame++,[],new(0,0,64,64),Vector4.Zero,new(Vector3.Zero,Vector3.UnitY,Vector4.Zero));framePending=true;return false;}return true;}
        using var scene=new SceneRenderSession(renderer,cache,document.World);scene.Prepare(assets,document.CaptureSnapshot(),environmentPreparationAllowed:Approved);
        using var target=renderer.CreateViewTarget(64,64);
        byte[] Draw(){var pixels=new byte[64*64*4];Check(scene.Submit(frame++,64,64,camera,target:target),"scene draw");renderer.CaptureTarget(target,pixels);renderer.Present();return pixels;}
        void FailCandidate(){fault=true;fired=false;AggregateException? failure=null;
            try{scene.PrepareEnvironmentView(32,32,camera);}catch(AggregateException error){failure=error;}
            Pass(failure?.InnerExceptions.Count==2&&failure.InnerExceptions[1] is PluginException{Result:PluginResult.Busy,Phase:"destroy_scene_pipeline"},"preparation error and exact native retirement Busy retained together");
            Pass(fired&&renderer.PipelineStats.Pipelines==2,"one installed and one failed candidate retained");
        }
        try {
            scene.PrepareEnvironmentView(64,64,camera);Check(renderer.PipelineStats.Pipelines==1,"initial pipeline");
            var original=Draw();var snapshot=document.CaptureBytes();var originalPlan=scene.Plan;var resourceStats=renderer.ResourceStats;
            Pass(original.Distinct().Count()>4,"reference scene has nonempty pixels");FailCandidate();
            Pass(scene.ResourceExchangeStatus is{RecoveryRequired:true,Closing:false,CandidateRetained:true,Failure:SceneResourceExchangeFailure.CandidateRetirement}&&ReferenceEquals(scene.Plan,originalPlan),"failure stops submission without discarding old plan or candidate");
            Reject(()=>scene.Submit(frame++,64,64,camera,target:target));Reject(()=>scene.PrepareEnvironmentView(64,64,camera));Reject(()=>scene.RefreshEnvironment(assets,()=>true));
            Pass(renderer.PipelineStats.Pipelines==2&&!scene.Costs.GpuSubmitted&&scene.ReadAnimationPresentation() is null,"fail-stop blocks submit/prepare/refresh and stale submitted stamps");
            int approvals=0;Reject(()=>scene.RecoverResourceExchange(()=>{approvals++;return true;}));
            Pass(approvals==0&&scene.ResourceExchangeStatus.RecoveryRequired,"active frame rejects recovery before approval");
            renderer.Present();framePending=false;
            Reject(()=>scene.RecoverResourceExchange(()=>false));
            Task.Run(()=>Reject(()=>scene.RecoverResourceExchange(()=>true))).GetAwaiter().GetResult();
            foreach(Action reentry in new Action[]{scene.Dispose,()=>scene.PrepareEnvironmentView(64,64,camera),()=>scene.RecoverResourceExchange(()=>true)})
                Reject(()=>scene.RecoverResourceExchange(()=>{try{reentry();}catch(InvalidOperationException){}return true;}));
            Pass(scene.ResourceExchangeStatus.RecoveryRequired&&renderer.PipelineStats.Pipelines==2,"denied/foreign/caught reentrant recovery preserves both owners");
            scene.RecoverResourceExchange(()=>true);
            Pass(!scene.ResourceExchangeStatus.RecoveryRequired&&!scene.ResourceExchangeStatus.CandidateRetained&&scene.Plan is null&&renderer.PipelineStats.Pipelines==0,"explicit approved off-frame recovery drains both pipeline generations");
            Reject(()=>scene.Submit(frame++,64,64,camera,target:target));
            fault=false;scene.PrepareEnvironmentView(64,64,camera);
            Pass(original.SequenceEqual(Draw())&&snapshot.SequenceEqual(document.CaptureBytes()),"reprepare reproduces exact original image without World mutation");
            Pass(renderer.ResourceStats.Creates==resourceStats.Creates&&renderer.ResourceStats.UploadedBytes==resourceStats.UploadedBytes,"recovery retains immutable geometry/material/texture generation");
            FailCandidate();Reject(scene.Dispose);
            Pass(scene.ResourceExchangeStatus is{RecoveryRequired:true,Closing:true,CandidateRetained:true},"failed close retains candidate and becomes close-only");
            renderer.Present();framePending=false;Reject(()=>scene.RecoverResourceExchange(()=>true));
            scene.Dispose();Pass(renderer.PipelineStats.Pipelines==0&&renderer.EnvironmentStats.Live==0,"explicit close retry drains failed candidate and installed pipeline");
            // Promote an existing plain scene: candidate owns a NEW environment
            // GPU lease and CPU asset pin, not just a borrowed installed binding.
            var enabled=document.World.Get<EnvironmentLightingData>(environment);document.World.Set(environment, EnvironmentLightingData.Off);fault=false;
            using(var plain=new SceneRenderSession(renderer,cache,document.World)) {plain.Prepare(assets,document.CaptureSnapshot(),environmentPreparationAllowed:Approved);
                Pass(plain.Submit(frame++,64,64,camera,target:target),"plain scene before promotion");renderer.Present();
                document.World.Set(environment, enabled);fault=true;fired=false;AggregateException? failure=null;
                try{plain.RefreshEnvironment(assets,Approved);}catch(AggregateException error){failure=error;}
                Pass(failure?.InnerExceptions.Count==2&&plain.ResourceExchangeStatus is{RecoveryRequired:true,CandidateRetained:true,EnvironmentGpuRetained:true,EnvironmentAssetsRetained:true},"promotion failure retains complete candidate GPU/CPU bundle");
                Pass(plain.PreparedEnvironment==EnvironmentLightingData.Off&&renderer.EnvironmentStats.Live==1&&PinLocked(fixture.GenerationPath),"failed promotion retains old committed selection and pinned assets");
                renderer.Present();framePending=false;plain.RecoverResourceExchange(()=>true);
                Pass(renderer.PipelineStats.Pipelines==0&&renderer.EnvironmentStats.Live==0&&!plain.ResourceExchangeStatus.EnvironmentGpuRetained&&!plain.ResourceExchangeStatus.EnvironmentAssetsRetained,"recovery releases pending environment bundle in dependency order");
                fault=false;plain.RefreshEnvironment(assets,()=>true);plain.PrepareEnvironmentView(64,64,camera);
                var pixels=new byte[64*64*4];plain.Submit(frame++,64,64,camera,target:target);renderer.CaptureTarget(target,pixels);renderer.Present();
                Pass(original.SequenceEqual(pixels)&&plain.PreparedEnvironment==enabled,"explicit environment refresh after recovery restores exact reference image");
            }
            target.Dispose();Pass(renderer.ResourceStats.Targets==0&&renderer.ResourceStats.Materials==0&&renderer.ResourceStats.Textures==0&&renderer.SceneStats.LiveMeshes==0&&renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"complete resource drain and API0/0");
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{cases,apiErrors=0,apiWarnings=0,realBusy=true,deviceLossRecovery=false,fullStageAccepted=false}));
            Console.WriteLine($"PASS M7.6 scene exchange {cases} checks; API0/0; {output}");return 0;
        } finally {
            if(framePending)renderer.Present();
            // Failure-only test teardown: inspect native-owner bookkeeping without changing it,
            // then use normal public releases. Never used as product recovery or a passing oracle.
            var pipelines=(IEnumerable<ScenePipelineSession>)typeof(RendererSession).GetField("_scenePipelines",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(renderer)!;
            foreach(var retained in pipelines.ToArray())retained.Dispose();
        }
    }

    private static int PostExchangeSafetyTests(string plugins,string output)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);int cases=0;
        void Pass(bool ok,string label){Check(ok,"M7.6 post exchange: "+label);cases++;}
        var repo=new DirectoryInfo(Path.GetFullPath(plugins));while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent??throw new Exception("Repository missing");
        using var files=RuntimeShaderFileSet.ForHost(Path.Combine(repo.FullName,"out/bin"),null,null)??throw new Exception("Checked shader files required");
        using var library=RuntimePostProcessLibrary.LoadDeployment(Path.Combine(repo.FullName,"out/bin"));
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(plugins),Specs().Take(2).ToArray());
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Post exchange safety",64,64,false);
        using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,64,64);files.InstallSelection(renderer,()=>true);
        bool fault=false,fired=false,pendingFrame=false;ulong frame=1;ulong faultPipelineCount=2;
        bool Approved(){if(fault&&!fired&&renderer.PipelineStats.Pipelines==faultPipelineCount){fired=true;renderer.SubmitResources(frame++,[],new(0,0,64,64),Vector4.Zero,new(Vector3.Zero,Vector3.UnitY,Vector4.Zero));pendingFrame=true;return false;}return true;}
        using var runtime=new PostProcessRuntime(renderer,files,library,PostProcessModules.Official(),Approved);
        var fixture=new SceneGpuFixture(output);var(document,camera,_,_)=fixture.Scene();
        global::Ncma.Runtime.Entity light=document.World.GetEntities().Single(o=>document.World.Has<DirectionalLightData>(o));document.World.Set(light, document.World.Get<DirectionalLightData>(light) with{CastShadow=false});
        global::Ncma.Runtime.Entity settings=document.World.CreateEntity("Post");document.World.Set(settings, PostProcessData.Off with{Enabled=true,StackJson=PostProcessData.EncodeStack([PostProcessModules.Bloom(new(Levels:2,Threshold:0,Intensity:.8f))])});
        using var assets=SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,document.CaptureSnapshot(),true);
        using var cache=new RenderResourceCache(renderer);using var target=renderer.CreateViewTarget(64,64);
        using var scene=new SceneRenderSession(renderer,cache,document.World);scene.Prepare(assets,document.CaptureSnapshot(),postProcessing:runtime);
        byte[] Draw(){var pixels=new byte[64*64*4];scene.Submit(frame++,64,64,camera,target:target);renderer.CaptureTarget(target,pixels);renderer.Present();return pixels;}
        try {
            scene.PrepareEnvironmentView(64,64,camera);var original=Draw();var snapshot=document.CaptureBytes();var installed=scene.Plan;
            Pass(scene.IsPostProcessReady&&renderer.PostStats.Bindings==1,"initial actual Bloom binding");fault=true;
            AggregateException? failure=null;try{scene.PrepareEnvironmentView(32,32,camera);}catch(AggregateException error){failure=error;}
            Pass(fired&&failure?.InnerExceptions.Count==2&&failure.InnerExceptions[1] is PluginException{Result:PluginResult.Busy},"post preparation and cleanup failures both reported");
            Pass(scene.ResourceExchangeStatus is{RecoveryRequired:true,CandidateRetained:true}&&!scene.IsPostProcessReady&&ReferenceEquals(installed,scene.Plan)&&renderer.PipelineStats.Pipelines==2&&renderer.PostStats.Bindings==1,"post runtime retains candidate while old Bloom remains installed, never reports ready");
            Reject(()=>scene.Submit(frame++,64,64,camera,target:target));Reject(()=>scene.PrepareEnvironmentView(64,64,camera));
            renderer.Present();pendingFrame=false;scene.RecoverResourceExchange(()=>true);
            Pass(renderer.PipelineStats.Pipelines==0&&renderer.PostStats.Bindings==0&&renderer.PostStats.ResidentBytes==0,"recovery drains all retained pipelines and post targets");
            fault=false;scene.PrepareEnvironmentView(64,64,camera);
            Pass(scene.IsPostProcessReady&&original.SequenceEqual(Draw())&&snapshot.SequenceEqual(document.CaptureBytes()),"fresh post preparation restores exact image without world/history mutation");
            scene.Submit(frame++,64,64,camera,target:target);pendingFrame=true;Reject(scene.Dispose);
            Pass(scene.ResourceExchangeStatus is{Closing:true,RecoveryRequired:true,CandidateRetained:false}&&renderer.PipelineStats.Pipelines==1,"installed-pipeline close Busy retains ownership and blocks recovery-as-resume");
            renderer.Present();pendingFrame=false;Reject(()=>scene.RecoverResourceExchange(()=>true));scene.Dispose();
            Pass(renderer.PipelineStats.Pipelines==0&&renderer.PostStats.ResidentBytes==0&&renderer.PostStats.Bindings==0&&renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"close retry drains installed post resources; API0/0");
            runtime.Dispose();faultPipelineCount=1;
            foreach(bool customStartup in new[]{false,true}) {
                fault=false;fired=false;
                using(var probe=new PostProcessRuntime(renderer,files,library,PostProcessModules.Official(),Approved)) {
                    using var startup=new ScenePipelineStartup(renderer);
                    void PrepareProbe(){if(customStartup)startup.Prepare(document.World,document.CaptureSnapshot(),(e,a,s)=>new Scene3DPipeline(e,a,shadows:s),probe,()=>true);
                        else startup.PreparePost(document.World,document.CaptureSnapshot(),probe);}
                    fault=true;AggregateException? probeFailure=null;
                    try{PrepareProbe();}catch(AggregateException error){probeFailure=error;}
                    Pass(fired&&probeFailure?.InnerExceptions.Count==2&&probeFailure.InnerExceptions[1] is PluginException{Result:PluginResult.Busy}&&startup.HasRetainedProbe&&renderer.PipelineStats.Pipelines==1,"startup preflight retains probe on approval and retirement failure");
                    Reject(startup.Dispose);renderer.Present();pendingFrame=false;
                    ulong creates=renderer.PipelineStats.Creates;Reject(PrepareProbe);
                    Pass(startup.HasRetainedProbe&&renderer.PipelineStats.Creates==creates,"retained startup probe refuses new work until startup owner close");
                    startup.Dispose();Pass(renderer.PipelineStats.Pipelines==0&&renderer.PostStats.Bindings==0,"host-owned startup close retry drains probe");
                }
                fault=false;
                using(var fresh=new PostProcessRuntime(renderer,files,library,PostProcessModules.Official(),Approved)) {
                    using var startup=new ScenePipelineStartup(renderer);
                    if(customStartup)startup.Prepare(document.World,document.CaptureSnapshot(),(e,a,s)=>new Scene3DPipeline(e,a,shadows:s),fresh,()=>true);
                    else startup.PreparePost(document.World,document.CaptureSnapshot(),fresh);
                    Pass(!startup.HasRetainedProbe&&renderer.PipelineStats.Pipelines==0&&renderer.PostStats.Bindings==0,"fresh startup owner passes complete original preflight");
                }
            }
            Pass(snapshot.SequenceEqual(document.CaptureBytes())&&renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"startup failure/retry preserves exact document and API0/0");
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{cases,apiErrors=0,apiWarnings=0,deviceLossRecovery=false,fullStageAccepted=false}));
            Console.WriteLine($"PASS M7.6 post exchange {cases} checks; API0/0; {output}");return 0;
        }finally{if(pendingFrame)renderer.Present();}
    }
}
