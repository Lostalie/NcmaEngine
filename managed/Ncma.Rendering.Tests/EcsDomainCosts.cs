using System.Diagnostics;
using System.Text.Json;
using Ncma.Characters;
using Ncma.Gameplay;
using Ncma.Rendering;

// Development evidence only. The shipping Play/renderer own all work and identity.
// No forced GC, GPU wait, screenshots or per-frame JSON in these running windows.
internal sealed class EcsDomainCosts(UiPublicationProductEvidence evidence)
{
    internal const int Warmup=12, Samples=100;
    internal const string Contract="ecs-e5-d-running-development-v3-gpu-snapshots";
    private readonly List<object> _windows=[];
    internal readonly record struct Row(double HostMilliseconds,long HostOwnerAllocatedBytes,
        SceneSimulationCostReport Simulation,PresentationCostReport Presentation);
    private readonly record struct Resources(ulong Groups,ulong Meshes,ulong SkinMeshes,ulong UiTargets,
        ulong Environments,ulong Textures,ulong Materials,ulong Targets,ulong PostPrograms,int PhysicsWorlds);
    private static Resources ReadResources(RendererSession renderer,int worlds)=>new(renderer.Stats.LiveGroups,
        renderer.SceneStats.LiveMeshes,renderer.SkinStats.Meshes,renderer.UiTargetStats.Targets,
        renderer.EnvironmentStats.Live,renderer.ResourceStats.Textures,renderer.ResourceStats.Materials,
        renderer.ResourceStats.Targets,renderer.PostStats.Programs,worlds);
    private sealed record Memory(long WorkingSetBytes,long PrivateBytes,long RawManagedHeapBytes,bool RawManagedHeapValid,
        long LastGcIndex,long LastGcHeapSizeBytes,long LastGcFragmentedBytes,int Gen0,int Gen1,int Gen2)
    {
        internal static Memory Read(){using var process=Process.GetCurrentProcess();process.Refresh();
            long raw=GC.GetTotalMemory(false);var info=GC.GetGCMemoryInfo();
            return new(process.WorkingSet64,process.PrivateMemorySize64,raw,raw>=0,info.Index,info.HeapSizeBytes,
                info.FragmentedBytes,GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2));}
    }
    internal void RunningWindow(string label,RendererSession renderer,PlaySession play,Action draw,
        Func<SceneSimulationCostReport?> simulation,Func<PresentationCostReport?> presentation,Func<int> physicsWorlds)
    {
        if(_windows.Count>=3||play.State!=PlayState.Running)throw new InvalidOperationException("Bounded running window required");
        for(int i=0;i<Warmup;i++)draw();
        var before=simulation()??throw new Exception("Missing warm simulation");
        var beforePresentation=presentation()??throw new Exception("Missing warm presentation");
        var rows=new Row[Samples];
        var resourcesBefore=ReadResources(renderer,physicsWorlds());var memoryBefore=Memory.Read();
        int completed=0;
        void PreserveRaw(Memory? after,string? failure){
            string path=Path.Combine(evidence.Output,$"ecs-running-raw-{_windows.Count}.json");
            using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            JsonSerializer.Serialize(stream,new{schema=3,contractId=Contract,label,baselinePresentation=beforePresentation,
                completedSamples=completed,rows=rows.Take(completed).ToArray(),memoryBefore,memoryAfter=after,failure,
                developmentOnly=true,targetPerformanceAccepted=false,memoryAcceptance="pending"});
        }
        Memory? memoryAfter=null;Resources resourcesAfter=default;
        try{for(int i=0;i<rows.Length;i++){
            long allocated=GC.GetAllocatedBytesForCurrentThread(),time=Stopwatch.GetTimestamp();
            draw();
            var sim=simulation()??throw new Exception("Missing current simulation");
            var present=presentation()??throw new Exception("Missing current presentation");
            rows[i]=new(Stopwatch.GetElapsedTime(time).TotalMilliseconds,GC.GetAllocatedBytesForCurrentThread()-allocated,sim,present);
            completed++;
        }
        memoryAfter=Memory.Read();resourcesAfter=ReadResources(renderer,physicsWorlds());
        }catch(Exception error){
            try{PreserveRaw(memoryAfter,error.ToString());}
            catch(Exception persistence){throw new AggregateException("Window failed and raw evidence could not be persisted.",error,persistence);}
            throw;
        }
        PreserveRaw(memoryAfter,null); // Outside all timings, before acceptance checks can reject a window.
        evidence.Pass(resourcesBefore==resourcesAfter&&renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,
            "running workload retains bounded live resources and API0/0 (not byte residency acceptance)");
        ulong tick=before.Advance.TickAfter,sequence=before.Advance.AdvanceSequence,frame=beforePresentation.HostFrame;
        var measuredFrames=rows.Select(r=>r.Presentation.HostFrame).ToHashSet();
        var ledger=new EcsGpuSnapshotLedger(beforePresentation,measuredFrames);
        foreach(var row in rows){
            var sim=row.Simulation.Advance;var p=row.Presentation;
            evidence.Pass(sim.Completed&&sim.Kind==SimulationAdvanceKind.Frame&&sim.State==PlayState.Running&&sim.IncludesUpdate&&
                sim.SessionId==before.Advance.SessionId&&sim.WorldId==before.Advance.WorldId&&sim.TickBefore==tick&&sim.TickAfter==tick+1&&
                sim.AdvanceSequence==sequence+1&&sim.CommittedSteps==1&&sim.OwnerThreadAllocatedBytes>=0&&double.IsFinite(sim.CpuWallMilliseconds)&&sim.CpuWallMilliseconds>=0,
                "every running frame advances one real committed quantum with unique current identity");
            evidence.Pass(row.Simulation.LastCharacterStep is{} character&&character.Tick==sim.TickAfter&&character.PlaySessionId==sim.SessionId&&character.WorldId==sim.WorldId&&character.Native.LiveJobs==0,
                "running FBX/root/Jolt observation belongs to this exact committed step");
            evidence.Pass(p.Completed&&!p.IncludesDiagnosticCapture&&p.Generation==beforePresentation.Generation&&p.HostFrame>frame&&
                double.IsFinite(row.HostMilliseconds)&&row.HostMilliseconds>=0&&row.HostOwnerAllocatedBytes>=0&&
                p.Counters is{MainSubmissions:1,Presents:1,MeshUploadedBytes:0,ResourceUploadedBytes:0,EnvironmentUploadedBytes:0,UiUploadedBytes:0},
                "complete non-capture host workload; no immutable warm source reuploads");
            ledger.Add(p);
            tick=sim.TickAfter;sequence=sim.AdvanceSequence;frame=p.HostFrame;
        }
        evidence.Pass(play.Tick==before.Advance.TickAfter+Samples&&play.SessionId==before.Advance.SessionId,
            "active domain window has no paused/fake/replayed fixed steps");
        _windows.Add(new{label,warmup=Warmup,samples=Samples,fixedInputDeltaSeconds=1d/60,baselinePresentation=beforePresentation,rows,gpu=ledger.Summary(),
            resourcesBefore,resourcesAfter,memoryBefore,memoryAfter,
            hostScope="trusted product SDK frame callback plus copied report reads; simulation/input/presentation; excludes startup, loop scheduling, screenshot, observer allocation and IO",
            memoryScope="two process snapshots outside timing; includes runtime/harness; last-GC fields not synchronized current heap; no private-minus-managed native estimate",
            cpuScopesAreNested=true,gpuDomainsAreNotAdditive=true,syntheticInputClock=true,framePacingMeasured=false,
            targetPerformanceAccepted=false,memoryAcceptance="pending"});
    }
    internal void Save(string host)=>File.WriteAllText(Path.Combine(evidence.Output,"ecs-running-domain-costs.json"),
        JsonSerializer.Serialize(new{schema=3,contractId=Contract,host,developmentOnly=true,formalCohort=false,windows=_windows,targetPerformanceAccepted=false,memoryAcceptance="pending"}));
}
