using System.Numerics;
using System.Runtime.InteropServices;
using Ncma.Interop;
using Ncma.Physics;
static class CharacterNumericsTests {
    static void Check(bool v,string name){if(!v)throw new Exception(name);}
    static void Reject(Action action,PluginResult? result=null) {
        try {action();}catch(PluginException e){Check(result is null || result==e.Result,"character error code: "+e.Result);return;}
        catch(AggregateException e) when(e.InnerExceptions.Count==1 && e.InnerException is PluginException native){Check(result is null || result==native.Result,"character aggregate error code");return;}
        catch(Exception e) when(result is null && e is InvalidOperationException or ArgumentException){return;}
        throw new Exception("Expected character rejection.");
    }
    public static void Run(string root) {
        Check(Marshal.SizeOf<CapsuleDescription>()==72 && Marshal.SizeOf<CollisionBox>()==72,"capsule/box layout");
        Check(Marshal.SizeOf<CharacterVelocity>()==40 && Marshal.SizeOf<CharacterState>()==104 && Marshal.SizeOf<CharacterContact>()==64 && Marshal.SizeOf<CharacterStepReceipt>()==16,"state/contact layout");
        Check(Marshal.SizeOf<PhysicsRay>()==40 && Marshal.SizeOf<CapsuleSweep>()==64 && Marshal.SizeOf<PhysicsQueryHit>()==64,"query layout");
        using(var old=new PhysicsModuleHost(root)){using var world=PhysicsWorld.Create3D(old.Module!);Reject(()=>world.CreateCapsules([CapsuleDescription.Default(Vector3.Zero)],new ulong[1]),PluginResult.UnsupportedFeature);}
        using(var disabled=new PhysicsModuleHost("missing",false,characterSupport:true))Check(disabled.Module is null,"optional character no DLL");
        for(int cycle=0;cycle<32;++cycle) {
            using var host=new PhysicsModuleHost(root,characterSupport:true);var module=host.Module!;
            Check(module.AbiMinor==2 && module.Capabilities==255,"explicit new API");
            // Base 2D/3D behaviour still works under negotiated additive API (no character resources).
            using(var two=PhysicsWorld.Create2D(module)){Check(two.Step(1f/60)==1,"2D unaffected");}
            using var w=PhysicsWorld.Create3D(module);
            try {
            ulong[] bodies=new ulong[2],ids=new ulong[1];
            w.CreateCollisionBoxes([CollisionBox.Create(new(0,-.5f,0),new(20,.5f,20)),CollisionBox.Create(new(2,2,0),new(.2f,2,20))],bodies);
            var capsule=CapsuleDescription.Default(new(0,.1f,0));capsule.Correlation=42;w.CreateCapsules([capsule],ids);
            Check(module.Status.LiveResources==4,"world/body/character resources");
            Reject(w.Dispose,PluginResult.Busy);Reject(host.Dispose,PluginResult.Busy);
            var velocities=new[]{new CharacterVelocity{Character=ids[0],Velocity=new(3,-1,1),Rotation=Quaternion.Identity}};
            var states=new CharacterState[1];var contacts=new CharacterContact[64];
            Reject(()=>w.StepCharacters(0,1f/60,velocities,states,new CharacterContact[63]),PluginResult.BufferTooSmall);
            Check(w.NativeCounters.Sequence==0 && states[0].Sequence==0,"preflight no quantum/output");
            Reject(()=>w.StepCharacters(1,1f/60,velocities,states,contacts),PluginResult.InvalidArgument);
            Reject(()=>w.Step(1f/60),PluginResult.Busy);
            for(ulong seq=0;seq<60;++seq){var receipt=w.StepCharacters(seq,1f/60,velocities,states,contacts);Check(receipt.Sequence==seq+1 && receipt.States==1 && receipt.Contacts<=64,"copied receipt");}
            Check(states[0].Character==ids[0] && states[0].Correlation==42 && states[0].Sequence==60 && states[0].Foot.X<1.51f && states[0].Foot.Z>.9f && states[0].Ground==GroundState.Ground,"managed actual Jolt wall/floor");
            var copy=states[0];w.ReadCharacterStates(60,ids,states);Check(states[0].Foot==copy.Foot,"copied read");
            Reject(()=>w.ReadCharacterStates(59,ids,states),PluginResult.InvalidArgument);
            var ray=new PhysicsRay{StructSize=40,Mask=1,Origin=new(0,1,0),Displacement=new(4,0,0)};
            var hit=w.Ray(60,ray);Check(hit.Resource==bodies[1] && hit.Kind==CollisionResourceKind.Body && hit.Sequence==60,"managed ray");
            var sweep=new CapsuleSweep{StructSize=64,Mask=1,Foot=new(0,.1f,0),Displacement=new(4,0,0),Radius=.3f,HalfHeight=.6f,Rotation=Quaternion.Identity};
            hit=w.Sweep(60,sweep);Check(hit.Resource==bodies[1] && hit.Fraction<.4f,"managed sweep");
            Reject(()=>w.Ray(60,ray with{Ignore=ids[0]+100000}),PluginResult.InvalidHandle);
            Exception? wrong=null;var thread=new Thread(()=>{try{w.Ray(60,ray);}catch(Exception e){wrong=e;}});thread.Start();thread.Join();Check(wrong is InvalidOperationException,"managed character owner thread");
            // Warm cached delegates, then verify caller-owned fixed buffers incur no managed allocation.
            ulong current=60;for(int i=0;i<4;++i)current=w.StepCharacters(current,1f/60,velocities,states,contacts).Sequence;
            long start=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<32;++i){current=w.StepCharacters(current,1f/60,velocities,states,contacts).Sequence;w.ReadCharacterStates(current,ids,states);w.Ray(current,ray);w.Sweep(current,sweep);}
            Check(GC.GetAllocatedBytesForCurrentThread()==start,"reused character step/read/query no managed allocations");
            w.DestroyCharacterSet(ids);Reject(()=>w.ReadCharacterStates(current,ids,states),PluginResult.InvalidHandle);
            w.DestroyBodies(bodies);w.Dispose();Check(module.Status.LiveResources==0 && module.Status.LiveJobs==0,"managed character cycle drained");
            }catch(Exception original){Console.Error.WriteLine("Original character test failure: "+original);throw;}
        }
        using(var host=new PhysicsModuleHost(root,characterSupport:true)) {
            using var world=PhysicsWorld.Create3D(host.Module!);
            var dense=Enumerable.Range(0,80).Select(_=>CollisionBox.Create(new(0,-.5f,0),new(20,.5f,20))).ToArray();
            world.CreateCollisionBoxes(dense,new ulong[80]);ulong[] id=new ulong[1];world.CreateCapsules([CapsuleDescription.Default(new(0,.1f,0))],id);
            CharacterState[] output=[new(){Sequence=123}];var contacts=new CharacterContact[64];
            Reject(()=>world.StepCharacters(0,1f/60,[new(){Character=id[0],Velocity=new(0,-1,0),Rotation=Quaternion.Identity}],output,contacts),PluginResult.InternalError);
            Check(world.NativeCounters.State==2 && world.NativeCounters.Sequence==0 && output[0].Sequence==123,"real overflow faulted / copied output unchanged");
            Reject(()=>world.ReadCharacterStates(0,id,output),PluginResult.InternalError);Reject(world.Dispose,PluginResult.Busy);Reject(host.Dispose,PluginResult.Busy);
            world.DestroyCharacterSet(id);world.Dispose();Check(host.Module!.Status.LiveResources==0,"faulted resources closed before unload");
        }
        Console.WriteLine("K2 managed: POD layouts, explicit optional API, old API rejection, actual Jolt floor/wall/ray/sweep, preflight, ownership, real overflow fail-stop, zero-alloc reused buffers, 32 module/world lifecycles passed.");
    }
}
