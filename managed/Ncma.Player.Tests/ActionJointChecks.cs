using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;
using Ncma.Animation.Native;
using Ncma.Characters;
using Ncma.Gameplay;
using Ncma.Interop;
using Ncma.Physics;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Runtime;
using Ncma.Samples;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static class ActionJointChecks
{
    private const int Warmup=32,Samples=32;
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Reject(Action action){try{action();}catch(InvalidOperationException){return;}throw new Exception("Expected profile rejection");}
    private sealed class FaultSystem(Action action):IWorldSystem{public void FixedUpdate(World world,double h)=>action();}
    private sealed record Sample(double SimulationMs,long AllocatedBytes,CharacterStepProfile? Character,
        SceneRenderCosts Render,double PoseMs,double PaletteMs,double SkinAbiMs,double SubmitMs,double PresentMs,
        double? SkinGpuMs,double? SceneGpuMs,ulong PaletteBytes,ulong ConstantBytes,ulong VertexUploadBytes,
        int GeometryDraws,int ShadowDraws,bool Submitted,ulong LiveJobs,ulong LiveBodies);
    private static object Summary(IEnumerable<double> source)
    {var sorted=source.Order().ToArray();return new{samples=sorted.Length,median=sorted.Length==0?(double?)null:sorted[sorted.Length/2],p95=sorted.Length==0?(double?)null:sorted[(int)Math.Ceiling(sorted.Length*.95)-1],max=sorted.Length==0?(double?)null:sorted[^1]};}
    private static object Hardware()
    {
        if(!OperatingSystem.IsWindows())return new{os=Environment.OSVersion.ToString(),processor="unavailable",adapters=Array.Empty<object>()};
        using var cpu=Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        using var display=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        var adapters=new List<object>();
        foreach(var name in display?.GetSubKeyNames().Where(n=>n.Length==4&&n.All(char.IsDigit))??[]) {
            using var entry=display!.OpenSubKey(name);if(entry?.GetValue("DriverDesc") is string desc)adapters.Add(new{description=desc,driver=entry.GetValue("DriverVersion")?.ToString()??"unavailable"});
        }
        return new{os=Environment.OSVersion.ToString(),processor=cpu?.GetValue("ProcessorNameString")?.ToString()??"unavailable",adapters=adapters.ToArray()};
    }
    internal static string[] Run(string repository,string output,string plugins)
    {
        string assembly=Path.Combine(repository,"out/managed/Ncma.Gameplay.Sample.dll");
        string evidence=Path.Combine(output,"m4-joint");Directory.CreateDirectory(evidence);
        using var loader=new PluginLoader();loader.Load(plugins,[new("platform",ModuleKind.Platform,"NcmaPlatform.dll","NcmaPlatform.dll",1,0,[]),new("renderer",ModuleKind.Renderer,"NcmaRenderer.dll","NcmaRenderer.dll",1,2,["platform"])]);
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"K7 bounded benchmark (not manual acceptance)",320,240,false);
        using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,320,240);
        var clear=new ClearPipeline().Build(320,240).Compile(renderer.Capabilities);
        using var cache=new RenderResourceCache(renderer);
        string kernelPath=Path.Combine(plugins,"NcmaAnimationKernel.dll");
        using var kernel=new PoseKernel(kernelPath,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(kernelPath))));
        var rows=new List<object>();ulong frame=0;
        foreach(int count in new[]{0,1,8,32}) {
            var sample=ActionSample.Create(evidence,assembly,count);
            var doc=Load(sample);var camera=doc.World.FindObject(sample.Camera);
            camera.Set(TransformData.Identity with{Position=new(Math.Max(0,(count-1)*1.5f),1,7)});
            camera.Set(CameraData.Default with{Projection=CameraProjection.Orthographic,OrthographicHeight=100});
            byte[] startup=doc.CaptureBytes();
            using var assets=SceneAssetPreparation.Prepare(sample.Root,sample.ProjectId,doc.CaptureSnapshot(),true,"assets/action.ncpak");
            using var physics=new PhysicsService(plugins,enabled:count!=0,characterSupport:count!=0);
            using var play=new PlaySession(doc,FrameTimePolicy.Strict,advanceMode:PlayAdvanceMode.FixedSteps);
            using var runtime=CharacterPlayRuntime.Compose(play,physics,assets,profile:true);
            play.Start(_=>throw new Exception("No behaviours"));
            var values=new List<Sample>(Samples);int[] gc=[GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)];
            try {
                using var scene=new SceneRenderSession(renderer,cache,doc.World,assets,doc.CaptureSnapshot(),poseKernel:count==0?null:kernel,play:play,rootMotion:runtime);
                for(int i=0;i<Warmup+Samples;i++) {
                    if(i==Warmup) {
                        foreach(Guid actor in sample.Actors)runtime!.RequestAction(actor,ActionRequest.Attack);
                        gc=[GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)];
                    }
                    long bytes=GC.GetAllocatedBytesForCurrentThread(),time=Stopwatch.GetTimestamp();
                    Check(play.AdvanceFixedStep().State==PlayState.Running,"Joint benchmark step");
                    double simulation=Stopwatch.GetElapsedTime(time).TotalMilliseconds;
                    var skinBefore=count==0?default:renderer.SkinStats;var pipelineBefore=count==0?default:renderer.PipelineStats;
                    var meshBefore=count==0?default:renderer.SceneStats;
                    bool submitted=scene.Submit(++frame,320,240,sample.Camera);if(!submitted)renderer.Submit(clear,null,RenderFrame.Reference(frame,320,240));renderer.Present();
                    long allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;
                    var skin=count==0?default:renderer.SkinStats;var pipeline=count==0?default:renderer.PipelineStats;var mesh=count==0?default:renderer.SceneStats;
                    var stats=renderer.Stats;var profile=runtime?.ReadStepProfile();
                    if(profile is {} committed)Check(committed.Tick==play.Tick&&committed.PlaySessionId==play.SessionId&&committed.WorldId==doc.World.Identity&&committed.Native.Sequence==runtime!.Status.CommittedSequence,"Profile must match committed numerical identity/sequence");
                    Check(stats.ValidationErrors==0&&stats.ValidationWarnings==0,"K7 validation 0/0");
                    if(count==0)Check(!submitted&&physics.Inspect().Worlds==0&&kernel.Statistics.Rigs==0&&cache.Count==0,"Empty workload must not allocate unused physics/pose/3D stages");
                    else Check(scene.Costs.GeometryDraws==count&&scene.Costs.ShadowDraws==count&&scene.Animation!.Costs.Characters==count,"Every measured actor must be rendered/posed, not culled");
                    if(i>=Warmup)values.Add(new(simulation,allocated,profile,scene.Costs,scene.Animation?.Costs.SampleMilliseconds??0,scene.Animation?.Costs.PaletteMilliseconds??0,scene.SkinAbiMilliseconds,
                        stats.SubmitMilliseconds,stats.PresentMilliseconds,skin.GpuSampleValid==1?skin.GpuMilliseconds:null,stats.GpuSampleValid==1?stats.GpuMilliseconds:null,
                        skin.PaletteBytes-skinBefore.PaletteBytes,pipeline.ConstantUploadBytes-pipelineBefore.ConstantUploadBytes,mesh.UploadedBytes-meshBefore.UploadedBytes,
                        scene.Costs.GeometryDraws,scene.Costs.ShadowDraws,submitted,profile?.Native.LiveJobs??0,profile?.Native.LiveBodies??0));
                }
                Check(count==0||values.Any(v=>v.Character!.Value.QueryMilliseconds>0),"Measured workload includes real combat ray queries");
                Check(values.All(v=>v.VertexUploadBytes==0&&v.LiveJobs==0),"No per-frame source vertex uploads or leaked numerical jobs");
                rows.Add(new{characters=count,fixtureHash=Convert.ToHexString(SHA256.HashData(startup)),packageHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(sample.Root,"assets/action.ncpak")))),
                    samples=values,simulation=Summary(values.Select(v=>v.SimulationMs)),allocation=Summary(values.Select(v=>(double)v.AllocatedBytes)),
                    gcCollections=Enumerable.Range(0,3).Select(g=>GC.CollectionCount(g)-gc[g]).ToArray(),scene.SkinBackpressureFrames,
                    skinGpu=Summary(values.Where(v=>v.SkinGpuMs is not null).Select(v=>v.SkinGpuMs!.Value)),sceneGpu=Summary(values.Where(v=>v.SceneGpuMs is not null).Select(v=>v.SceneGpuMs!.Value))});
            }finally{play.Stop();}
            Check(physics.Inspect().Worlds==0&&kernel.Statistics.Rigs==0&&kernel.Statistics.Clips==0&&cache.Count==0&&renderer.SkinStats.Meshes==0&&renderer.PipelineStats.Pipelines==0&&renderer.SceneStats.LiveMeshes==0&&renderer.ResourceStats.Textures==0&&renderer.ResourceStats.Materials==0&&renderer.ResourceStats.Targets==0,"Full coupled workload returns to numerical/pose/GPU baseline");
        }
        // Reuse one service/cache/kernel across 32 fault/reload/Stop cycles, not 32 fresh processes hiding retained resources.
        var cycleSample=ActionSample.Create(evidence,assembly,1);var original=Load(cycleSample);
        using(var assets=SceneAssetPreparation.Prepare(cycleSample.Root,cycleSample.ProjectId,original.CaptureSnapshot(),true,"assets/action.ncpak"))
        using(var physics=new PhysicsService(plugins,characterSupport:true)) {
            var identities=new HashSet<Guid>();
            for(int cycle=0;cycle<32;cycle++) {
                var doc=original.CreateIsolatedCopy();byte[] startup=doc.CaptureBytes();
                using var play=new PlaySession(doc,FrameTimePolicy.Strict,advanceMode:PlayAdvanceMode.FixedSteps);
                using var runtime=CharacterPlayRuntime.Compose(play,physics,assets,profile:cycle%2==0)!;bool fail=false;
                play.AddSystem(new FaultSystem(()=>{if(fail){var pending=play.Commands.SpawnEmpty("fault");play.Commands.AttachBehaviour(pending.ObjectId,new(Guid.NewGuid(),"InjectedMissing",true,[]));}}));
                play.Start(_=>throw new Exception("Injected missing behaviour factory"));
                Check(identities.Add(play.SessionId)&&identities.Add(doc.World.Identity)&&identities.Add(runtime.NumericalEpoch),"Fresh coupled identities");
                SceneRenderSession? scene=null;
                try {
                    scene=new(renderer,cache,doc.World,assets,doc.CaptureSnapshot(),poseKernel:kernel,play:play,rootMotion:runtime);
                    runtime.RequestAction(cycleSample.Actors[0],ActionRequest.Attack);
                    for(int i=0;i<12;i++){Check(play.AdvanceFixedStep().State==PlayState.Running,"Cycle committed attack");if(!scene.Submit(++frame,320,240,cycleSample.Camera))renderer.Submit(clear,null,RenderFrame.Reference(frame,320,240));renderer.Present();}
                    Check(doc.World.FindObject(cycleSample.Targets[0]).Get<HealthData>().Current==75,"Cycle real damage");
                    if(cycle%2!=0)Reject(()=>runtime.ReadStepProfile());
                    fail=true;byte[] before=doc.CaptureBytes();ulong tick=play.Tick;
                    Check(play.AdvanceFixedStep().State==PlayState.Faulted&&play.Tick==tick&&before.SequenceEqual(doc.CaptureBytes()),"Post-solver injected failure never installs candidate movement/health");
                    Check(!runtime.ReadDebugFrame().SnapshotValid,"Fault invalidates presentation");Reject(()=>runtime.ReadStepProfile());
                    scene.Dispose();scene=null; // GPU/pose before numerical rebuild.
                    Reject(()=>play.Reload(_=>throw new Exception("No behaviours"))); // A faulted session cannot silently resume/reload.
                    play.Stop();runtime.Dispose();
                    var recoveryDoc=original.CreateIsolatedCopy();
                    using var recovered=new PlaySession(recoveryDoc,FrameTimePolicy.Strict,advanceMode:PlayAdvanceMode.FixedSteps);
                    using var recoveredRuntime=CharacterPlayRuntime.Compose(recovered,physics,assets,profile:true)!;
                    recovered.Start(_=>throw new Exception("No behaviours"));
                    Check(identities.Add(recovered.SessionId)&&identities.Add(recoveryDoc.World.Identity)&&identities.Add(recoveredRuntime.NumericalEpoch),"Recovery rotates identities");
                    try {
                        Check(recoveryDoc.World.FindObject(cycleSample.Targets[0]).Get<HealthData>().Current==100&&recoveredRuntime.ReadCombatEvents().Length==0&&recoveredRuntime.InspectRootMotion(cycleSample.Actors[0]).Time==0,"Recovery from frozen startup resets damage/action/root/events");Reject(()=>recoveredRuntime.ReadStepProfile());
                        recovered.AdvanceFixedStep();Check(recoveredRuntime.ReadStepProfile().Tick==recovered.Tick,"Recovered committed profile");
                        recovered.Reload(_=>throw new Exception("No behaviours"));
                        Check(recovered.State==PlayState.Paused&&identities.Add(recovered.SessionId)&&identities.Add(recoveryDoc.World.Identity)&&identities.Add(recoveredRuntime.NumericalEpoch),"Active reload rotates identities");Reject(()=>recoveredRuntime.ReadStepProfile());
                        recovered.Step();using var recoveryScene=new SceneRenderSession(renderer,cache,recoveryDoc.World,assets,recoveryDoc.CaptureSnapshot(),poseKernel:kernel,play:recovered,rootMotion:recoveredRuntime);if(!recoveryScene.Submit(++frame,320,240,cycleSample.Camera))renderer.Submit(clear,null,RenderFrame.Reference(frame,320,240));renderer.Present();
                    }finally{recovered.Stop();}
                } finally{scene?.Dispose();play.Stop();}
                Check(physics.Inspect().Worlds==0&&kernel.Statistics.Rigs==0&&kernel.Statistics.Clips==0&&cache.Count==0&&renderer.SkinStats.Meshes==0&&renderer.PipelineStats.Pipelines==0&&renderer.SceneStats.LiveMeshes==0&&renderer.ResourceStats.Textures==0&&renderer.ResourceStats.Materials==0&&renderer.ResourceStats.Targets==0,"Cycle resource baselines");
                Check(original.CaptureBytes().SequenceEqual(startup),"Edit startup is isolated from cycles");
            }
        }
        Check(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"Joint final API validation 0/0");
        string report=Path.Combine(evidence,"profile.json");using(var stream=new FileStream(report,FileMode.CreateNew,FileAccess.Write,FileShare.None))JsonSerializer.Serialize(stream,new{
            schemaVersion=1,configuration=plugins.Contains("windows-ninja-release",StringComparison.OrdinalIgnoreCase)?"Release":"Debug",warmup=Warmup,samples=Samples,
            width=320,height=240,fixedSeconds=1d/60,vsync=false,hardware=Hardware(),runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,Environment.ProcessorCount,
            adapterSelection="DX11 default adapter; registry inventory only, active-adapter attribution pending",synthetic=true,budgetAccepted=false,manualAccepted=false,longRunAccepted=false,
            gpuSemantics="Nonblocking asynchronous last-valid observations; the existing ABI has no GPU sample-frame ID, observations may repeat. Not 32 independent GPU samples or per-frame attribution.",
            numericalFailure="Injected managed failure after irreversible solver execution, NOT real hardware failure/solver rollback",cycles=32,
            plugins=Directory.GetFiles(plugins,"*.dll").Order().Select(p=>new{name=Path.GetFileName(p),sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))}),workloads=rows},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true});
        Console.WriteLine("K7 bounded evidence: "+report);
        return ["K7 actual coupled 0/1/8/32 actor workloads: 32 warmup/32 samples, nested CPU scopes, raw solver/query/GPU/GC/upload evidence and resource baselines",
            "K7 32 shared-service coupled action/skin/shadow/fault/recovery/Stop cycles: fresh identities, frozen startup, validation 0/0, numerical/pose/GPU drained (NOT one-hour/manual acceptance)"];
    }
    private static SceneDocument Load(ActionSampleResult sample)
    {var doc=new SceneDocument("K7 bounded sample",CharacterComponents.Register(RenderComponentRegistry.CreateRegistry()),CharacterComponents.RequireComposition);SceneDocumentFiles.Load(doc,Path.Combine(sample.Root,"start.ncmascene"));return doc;}
}
