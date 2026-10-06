using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Ncma.Interop;
using Ncma.Physics;
static class Program {
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Reject(Action action,PluginResult? expected=null) {
        try{action();}catch(PluginException e){Check(expected is null || e.Result==expected,"Unexpected result: "+e.Result);return;}
        catch(Exception e) when(expected is null && e is ArgumentException or InvalidOperationException or FileNotFoundException or AggregateException){return;}
        throw new Exception("Expected rejection.");
    }
    static void Layouts() {
        Check(Marshal.SizeOf<Box2D>()==32 && Marshal.SizeOf<Box3D>()==40,"Box layouts");
        Check(Marshal.SizeOf<Velocity2D>()==16 && Marshal.SizeOf<Velocity3D>()==24,"Velocity layouts");
        Check(Marshal.SizeOf<BodyState2D>()==48 && Marshal.SizeOf<BodyState3D>()==64 && Marshal.SizeOf<PhysicsStats>()==88 && Marshal.SizeOf<PhysicsCounters>()==72,"State/stats layouts");
        Check(typeof(PhysicsWorld).Assembly.GetReferencedAssemblies().All(a=>!a.Name!.StartsWith("Ncma.") || a.Name=="Ncma.Interop"),"No scene/runtime/editor dependency");
    }
    static void Reference(PluginModule module) {
        using var two=PhysicsWorld.Create2D(module);using var three=PhysicsWorld.Create3D(module);
        Box2D[] boxes2=[new(){Position=new(0,-.5f),HalfExtents=new(8,.5f),Density=1},new(){Position=new(0,5),HalfExtents=new(.5f,.5f),Density=2,Dynamic=1,Correlation=42}];
        Box3D[] boxes3=[new(){Position=new(0,-.5f,0),HalfExtents=new(8,.5f,8),Density=1},new(){Position=new(0,5,0),HalfExtents=new(.5f,.5f,.5f),Density=2,Dynamic=1,Correlation=43}];
        ulong[] ids2=new ulong[2],ids3=new ulong[2];BodyState2D[] states2=new BodyState2D[2];BodyState3D[] states3=new BodyState3D[2];
        two.CreateBoxes2D(boxes2,ids2);three.CreateBoxes3D(boxes3,ids3);
        Check(module.Status.LiveResources==6 && two.Stats.LiveWorlds==2 && three.Stats.LiveWorlds==2,"simultaneous worlds/body counts");
        ulong seq2=0,seq3=0;
        for(int i=0;i<180;i++){seq2=two.Step(1f/60);seq3=three.Step(1f/60);}
        two.ReadBodyStates2D(seq2,ids2,states2);three.ReadBodyStates3D(seq3,ids3,states3);
        Check(Math.Abs(states2[1].Position.Y-.5f)<.08f && states2[1].Velocity.Length()<.1f,"2D floor settling");
        Check(Math.Abs(states3[1].Position.Y-.5f)<.08f && states3[1].Velocity.Length()<.1f,"3D floor settling");
        Check(states2[1].Correlation==42 && states3[1].Correlation==43 && states3[1].Rotation.LengthSquared()>.99f,"correlation/rotation");
        two.SetVelocities2D([new(){Body=ids2[1],Velocity=new(2,1)}]);three.SetVelocities3D([new(){Body=ids3[1],Velocity=new(2,1,0)}]);
        two.ReadBodyStates2D(seq2,ids2,states2);three.ReadBodyStates3D(seq3,ids3,states3);
        Check(Math.Abs(states2[1].Velocity.X-2)<.01f && Math.Abs(states3[1].Velocity.X-2)<.01f,"batched velocities");
        two.DestroyBodies(ids2);three.DestroyBodies(ids3);Check(module.Status.LiveResources==2,"destroy body batches");
    }
    static void Negatives(PluginModule module) {
        Reject(()=>PhysicsWorld.Create2D(module,maximumBodies:4097),PluginResult.InvalidArgument);
        Reject(()=>PhysicsWorld.Create3D(module,new(float.NaN,0,0)),PluginResult.InvalidArgument);
        using var two=PhysicsWorld.Create2D(module,Vector2.Zero,2);using var other=PhysicsWorld.Create2D(module);
        using var three=PhysicsWorld.Create3D(module);
        Box2D good=new(){Position=new(0,4),HalfExtents=new(.5f,.5f),Density=1,Dynamic=1};
        ulong[] ids=new ulong[2],foreign=new ulong[1];BodyState2D[] states=new BodyState2D[2];
        Reject(()=>two.CreateBoxes2D([good],Array.Empty<ulong>()),PluginResult.BufferTooSmall);
        Check(two.Stats.LiveBodies==0,"small create no mutation");
        foreach(var invalid in new[]{good with{Density=0},good with{Density=float.NaN},good with{HalfExtents=new(-1,1)},good with{Position=new(float.PositiveInfinity,0)},good with{Dynamic=2}}) {
            Reject(()=>two.CreateBoxes2D([good,invalid],ids),PluginResult.InvalidArgument);Check(two.Stats.LiveBodies==0,"validate whole batch");
        }
        two.CreateBoxes2D([good,good with{Position=new(2,4)}],ids);other.CreateBoxes2D([good],foreign);
        Reject(()=>two.CreateBoxes2D([good],foreign),PluginResult.Busy);Check(two.Stats.LiveBodies==2,"capacity no mutation");
        foreach(float dt in new[]{0,-1,float.NaN,float.PositiveInfinity,.26f})Reject(()=>two.Step(dt),PluginResult.InvalidArgument);
        Check(two.Stats.Sequence==0,"invalid dt no mutation");
        two.SetVelocities2D([new(){Body=ids[0],Velocity=new(1,2)}]);
        Reject(()=>two.SetVelocities2D([new(){Body=ids[0],Velocity=new(99,99)},new(){Body=foreign[0],Velocity=Vector2.Zero}]),PluginResult.InvalidHandle);
        Reject(()=>two.SetVelocities2D([new(){Body=ids[0],Velocity=new(99,99)},new(){Body=ids[0],Velocity=Vector2.Zero}]),PluginResult.InvalidArgument);
        Reject(()=>two.SetVelocities2D([new(){Body=ids[0],Velocity=new(float.NaN,0)}]),PluginResult.InvalidArgument);
        two.ReadBodyStates2D(0,ids,states);Check(states[0].Velocity==new Vector2(1,2),"set preflight atomic");
        Reject(()=>three.ReadBodyStates3D(0,ids,new BodyState3D[2]),PluginResult.InvalidHandle);
        Reject(()=>three.CreateBoxes2D([good],foreign));
        ulong seq=two.Step(1f/60);
        Reject(()=>two.ReadBodyStates2D(seq,ids,new BodyState2D[1]),PluginResult.BufferTooSmall);
        Reject(()=>two.ReadBodyStates2D(0,ids,states),PluginResult.InvalidArgument);
        Check(two.Stats.Sequence==seq,"read no extra step");
        Reject(()=>two.DestroyBodies([ids[0],foreign[0]]),PluginResult.InvalidHandle);
        Reject(()=>two.DestroyBodies([ids[0],ids[0]]),PluginResult.InvalidArgument);
        Check(two.Stats.LiveBodies==2,"destroy preflight atomic");
        two.DestroyBodies([ids[0]]);Reject(()=>two.ReadBodyStates2D(seq,[ids[0]],states),PluginResult.InvalidHandle);
        two.CreateBoxes2D([good],foreign);Check(foreign[0]!=ids[0],"body handles never reused");
        Exception? threadError=null;var thread=new Thread(()=>{try{_ = two.Stats;}catch(Exception e){threadError=e;}});thread.Start();thread.Join();
        Check(threadError is InvalidOperationException,"managed owner thread rejection");
        two.Dispose();two.Dispose();Reject(()=>two.Step(1f/60));
    }
    static object Measure(PluginModule module,int dimension,int count) {
        using var w=dimension==2 ? PhysicsWorld.Create2D(module,Vector2.Zero) : PhysicsWorld.Create3D(module,Vector3.Zero);
        var ids=new ulong[count];var b2=new Box2D[count];var b3=new Box3D[count];var s2=new BodyState2D[count];var s3=new BodyState3D[count];
        for(int i=0;i<count;i++){
            b2[i]=new(){Position=new(i%64*2,i/64*2),HalfExtents=new(.25f,.25f),Density=1,Dynamic=1,Correlation=(ulong)i};
            b3[i]=new(){Position=new(i%64*2,i/64*2,0),HalfExtents=new(.25f,.25f,.25f),Density=1,Dynamic=1,Correlation=(ulong)i};
        }
        long started=Stopwatch.GetTimestamp();
        if(dimension==2)w.CreateBoxes2D(b2,ids);else w.CreateBoxes3D(b3,ids);
        double create=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        // Warm JIT/delegates then measure caller-owned buffer path; thread bytes do not include native allocators.
        for(int i=0;i<4;i++){ulong seq=w.Step(1f/60);if(dimension==2)w.ReadBodyStates2D(seq,ids,s2);else w.ReadBodyStates3D(seq,ids,s3);}
        double[] times=new double[32];long bytes=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<times.Length;i++){
            started=Stopwatch.GetTimestamp();ulong seq=w.Step(1f/60);
            if(dimension==2)w.ReadBodyStates2D(seq,ids,s2);else w.ReadBodyStates3D(seq,ids,s3);
            times[i]=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        long allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;Array.Sort(times);
        var stats=w.Stats;Check(stats.LiveBodies==(ulong)count && stats.Sequence==36 && stats.LiveJobs==0,"benchmark live counts");
        Check(allocated==0,"reused step/read path allocated managed bytes");
        w.DestroyBodies(ids);Check(w.Stats.LiveBodies==0,"benchmark drained");
        return new{dimension,count,createMilliseconds=create,roundTripMedianMilliseconds=times[15],roundTripP95Milliseconds=times[30],roundTripMaxMilliseconds=times[^1],managedThreadBytes=allocated,stats};
    }
    public static int Main(string[] args) {
        try {
            Layouts();string root=args[0],report=args[1];Directory.CreateDirectory(report);
            using(var disabled=new PhysicsModuleHost(Path.Combine(root,"missing"),false))Check(disabled.Module is null,"disabled path no DLL required");
            Reject(()=>new PhysicsModuleHost(Path.Combine(root,"missing")));
            string empty=Path.Combine(report,"empty-plugins");Directory.CreateDirectory(empty);
            Reject(()=>new PhysicsModuleHost(empty));
            using(var loader=new PluginLoader())Reject(()=>loader.Load(root,[new("wrong-kind",ModuleKind.Physics,"NcmaRenderer.dll","NcmaRenderer.dll",1,0,[])]));
            using(var loader=new PluginLoader())Reject(()=>loader.Load(root,[new("bad",ModuleKind.Physics,"NcmaPhysics.dll","NcmaPhysics.dll",2,0,[])]));
            using(var loader=new PluginLoader())Reject(()=>loader.Load(root,[new("bad",ModuleKind.Physics,"NcmaPhysics.dll","NcmaPhysics.dll",1,0,["absent"])]));
            using(var host=new PhysicsModuleHost(root)) {
                var module=host.Module!;Check((PhysicsCapabilities)module.Capabilities==(PhysicsCapabilities)63,"honest capabilities");
                Check(module.AbiMinor==1,"raw counters negotiated");
                Reference(module);Check(module.Status.LiveResources==0 && module.Status.LiveJobs==0,"reference drained");
                Negatives(module);Check(module.Status.LiveResources==0,"negatives drained");
                using(var world=PhysicsWorld.Create2D(module))Reject(host.Dispose);
                var measures=new List<object>();
                foreach(int dimension in new[]{2,3})foreach(int count in new[]{0,64,1024,4096})measures.Add(Measure(module,dimension,count));
                File.WriteAllText(Path.Combine(report,"physics-results.json"),JsonSerializer.Serialize(new{units="metres/seconds; 2D kg/m2, 3D kg/m3; floor tolerance .08m",measurements=measures},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
                for(int cycle=0;cycle<32;cycle++){Reference(module);Check(module.Status.LiveResources==0 && module.Status.LiveJobs==0,"32 world cycles drained");}
            }
            // Module init/shutdown generations and unload are tested separately from world reuse.
            for(int cycle=0;cycle<32;cycle++){using var host=new PhysicsModuleHost(root);using var w=PhysicsWorld.Create3D(host.Module!);w.Step(1f/60);}
            PhysicsServiceTests.Run(root,report);
            CharacterNumericsTests.Run(root);
            Console.WriteLine("Physics H6: layouts, optional load, fixtures, preflight, handles, owner thread, 8 workloads, 32 world cycles, 32 module cycles passed.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
