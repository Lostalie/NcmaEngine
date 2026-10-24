using System.Numerics;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    private static void EnvironmentStartupSafetyTests(string plugins,string output)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);int cases=0;
        void Pass(bool ok,string label){Check(ok,"M7.6 environment startup: "+label);cases++;}
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(plugins),Specs().Take(2).ToArray());
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Environment startup safety",64,64,false);
        using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,64,64);
        using var cook=new EnvironmentCookService(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),()=>true);
        var package=cook.Cook(Guid.NewGuid(),1,HdrEnvironmentSource.Neutral(Guid.NewGuid()),new(8,4,8,64));
        var fixture=new SceneGpuFixture(output);var(document,_,_,_)=fixture.Scene();
        Guid environment=document.CreateObject("Environment");document.SetComponent(environment, new EnvironmentLightingData(1,package.AssetId,package.Generation,package.ContentHash,1,0,true));
        var descriptor=EnvironmentAssetDescriptor.FromPackage(fixture.Project,package);string path=Path.Combine(fixture.Root,descriptor.Path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path,package.CopyBytes());File.WriteAllBytes(Path.Combine(fixture.Root,"assets/environment.ncenv"),descriptor.Encode());
        using var assets=SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,document.CaptureSnapshot(),true);
        var runtime=BakeRenderScene(document);
        using var cache=new RenderResourceCache(renderer);var snapshot=runtime.CaptureSnapshot();var runtimeBytes=runtime.CaptureCheckpoint().CopyBytes();var bytes=document.CaptureBytes();ulong frame=1;
        bool pending=false,fired=false;
        using(var owner=new SceneEnvironmentPreflight(renderer,cache)) {
            Pass(owner.Status is{Phase:EnvironmentPreflightPhase.Empty,BindingRetained:false,GpuRetained:false,AssetsRetained:false},"host owns empty preflight before fallible work");
            bool FailAfterBinding(){if(!fired&&renderer.PipelineStats.Pipelines==1){fired=true;renderer.SubmitResources(frame++,[],new(0,0,64,64),Vector4.Zero,new(Vector3.Zero,Vector3.UnitY,Vector4.Zero));pending=true;return false;}return true;}
            try {
                Reject(()=>owner.Prepare(runtime.World,assets,snapshot,FailAfterBinding));
                Pass(fired&&owner.Status is{Phase:EnvironmentPreflightPhase.Failed,BindingRetained:true,GpuRetained:true,AssetsRetained:true},"failed Prepare leaves complete bundle reachable by host");
                PluginException? close=null;try{owner.Dispose();}catch(PluginException error){close=error;}
                Pass(close is{Result:PluginResult.Busy,Phase:"destroy_scene_pipeline"}&&owner.Status is{Phase:EnvironmentPreflightPhase.Closing,BindingRetained:true,GpuRetained:true,AssetsRetained:true},"exact native close Busy retains every dependency");
                int approvals=0;Reject(()=>owner.Prepare(runtime.World,assets,snapshot,()=>{approvals++;return true;}));
                Task.Run(()=>Reject(owner.Dispose)).GetAwaiter().GetResult();
                Pass(approvals==0&&renderer.PipelineStats.Pipelines==1&&renderer.EnvironmentStats.Live==1&&PinLocked(fixture.GenerationPath),"no overwrite/reprepare/foreign-thread retirement of retained owner");
            }finally{if(pending){renderer.Present();pending=false;}}
            owner.Dispose();Pass(owner.Status is{Phase:EnvironmentPreflightPhase.Closed,BindingRetained:false,GpuRetained:false,AssetsRetained:false}&&renderer.PipelineStats.Pipelines==0&&renderer.EnvironmentStats.Live==0,"explicit close retry drains pipeline before environment and pins");
            Reject(()=>owner.Prepare(runtime.World,assets,snapshot,()=>true));owner.Dispose();
        }
        using(var healthy=new SceneEnvironmentPreflight(renderer,cache)) {
            healthy.Prepare(runtime.World,assets,snapshot,()=>true);
            Pass(healthy.Status is{Phase:EnvironmentPreflightPhase.Ready,BindingRetained:true,GpuRetained:true,AssetsRetained:true}&&renderer.PipelineStats.Pipelines==1,"fresh explicit owner completes real pre-gameplay binding");
            Reject(()=>healthy.Prepare(runtime.World,assets,snapshot,()=>true));healthy.Dispose();
            Pass(renderer.PipelineStats.Pipelines==0&&renderer.EnvironmentStats.Live==0,"healthy close drains all environment resources");
        }
        foreach(bool nestedPrepare in new[]{false,true}) {
            using var guarded=new SceneEnvironmentPreflight(renderer,cache);
            Reject(()=>guarded.Prepare(runtime.World,assets,snapshot,()=>{
                try{if(nestedPrepare)guarded.Prepare(runtime.World,assets,snapshot,()=>true);else guarded.Dispose();}catch(InvalidOperationException){}
                return true;
            }));
            Pass(guarded.Status.Phase==EnvironmentPreflightPhase.Failed&&renderer.PipelineStats.Pipelines==0,"caught close/reentry refusal poisons approval before allocation");
        }
        using(var wrongThread=new SceneEnvironmentPreflight(renderer,cache)) {
            int approvals=0;Task.Run(()=>Reject(()=>wrongThread.Prepare(runtime.World,assets,snapshot,()=>{approvals++;return true;}))).GetAwaiter().GetResult();
            Pass(approvals==0&&wrongThread.Status.Phase==EnvironmentPreflightPhase.Empty,"owner check precedes all preparation state/callbacks");
            renderer.SubmitResources(frame++,[],new(0,0,64,64),Vector4.Zero,new(Vector3.Zero,Vector3.UnitY,Vector4.Zero));
            try{Reject(()=>wrongThread.Prepare(runtime.World,assets,snapshot,()=>{approvals++;return true;}));}finally{renderer.Present();}
            Pass(approvals==0&&wrongThread.Status.Phase==EnvironmentPreflightPhase.Empty,"frame boundary rejection does not consume an empty owner");
            wrongThread.Prepare(runtime.World,assets,snapshot,()=>true);
        }
        using(var revoked=new SceneEnvironmentPreflight(renderer,cache)) {
            Reject(()=>revoked.Prepare(runtime.World,assets,snapshot,()=>false));
            Pass(revoked.Status.Phase==EnvironmentPreflightPhase.Failed&&renderer.PipelineStats.Pipelines==0&&renderer.EnvironmentStats.Live==0,"revocation leaves no unowned resources");
        }
        Pass(bytes.SequenceEqual(document.CaptureBytes())&&runtimeBytes.SequenceEqual(runtime.CaptureCheckpoint().CopyBytes()),"all preparation/failure/close paths preserve authoring and actual runtime");
        document.SetComponent(environment, EnvironmentLightingData.Off);var offRuntime=BakeRenderScene(document);var stats=renderer.EnvironmentStats;
        using(var empty=new SceneEnvironmentPreflight(renderer,cache)) {
            int callbacks=0;empty.Prepare(offRuntime.World,assets,offRuntime.CaptureSnapshot(),()=>{callbacks++;return true;});
            Pass(empty.Status is{Phase:EnvironmentPreflightPhase.Ready,BindingRetained:false,GpuRetained:false,AssetsRetained:false}&&callbacks==0&&renderer.EnvironmentStats.Publications==stats.Publications,"Off creates no environment resources or approvals");
        }
        Pass(renderer.PipelineStats.Pipelines==0&&renderer.EnvironmentStats.Live==0&&cache.Count==0&&renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"final drain/API0/0");
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{cases,apiErrors=0,apiWarnings=0,realBusy=true,deviceLossRecovery=false,fullStageAccepted=false}));
        Console.WriteLine($"PASS M7.6 environment startup {cases} checks; API0/0; {output}");
    }
}
