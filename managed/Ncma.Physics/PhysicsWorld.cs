using System.Numerics;
using System.Runtime.InteropServices;
using Ncma.Interop;
namespace Ncma.Physics;
public enum PhysicsDimension : uint { Two = 2, Three = 3 }
[Flags] public enum PhysicsCapabilities : ulong { Boxes = 1, Velocity = 2, Step = 4, CopiedStates = 8, TwoD = 16, ThreeD = 32 }
[StructLayout(LayoutKind.Sequential)]
internal struct WorldDescription { public uint Size, Dimension, Maximum, SubSteps; public Vector3 Gravity; public uint Reserved; }
[StructLayout(LayoutKind.Sequential)]
public struct Box2D { public Vector2 Position, HalfExtents; public float Density; public uint Dynamic; public ulong Correlation; }
[StructLayout(LayoutKind.Sequential)]
public struct Box3D { public Vector3 Position, HalfExtents; public float Density; public uint Dynamic; public ulong Correlation; }
[StructLayout(LayoutKind.Sequential)]
public struct Velocity2D { public ulong Body; public Vector2 Velocity; }
[StructLayout(LayoutKind.Sequential)]
public struct Velocity3D { public ulong Body; public Vector3 Velocity; internal uint Reserved; }
[StructLayout(LayoutKind.Sequential)]
public struct BodyState2D { public ulong Body, Correlation, Sequence; public Vector2 Position, Velocity; public float Angle; internal uint Reserved; }
[StructLayout(LayoutKind.Sequential)]
public struct BodyState3D { public ulong Body, Correlation, Sequence; public Vector3 Position, Velocity; public Quaternion Rotation; }
[StructLayout(LayoutKind.Sequential)]
public struct PhysicsStats {
    public uint StructSize, State; public PhysicsDimension Dimension; public uint LiveWorlds;
    public ulong LiveBodies, Sequence, Batches, CopiedBytes, Errors;
    public double StepMedianMilliseconds, StepP95Milliseconds, StepMaxMilliseconds;
    public ulong LiveJobs;
}
[StructLayout(LayoutKind.Sequential)]
public struct PhysicsCounters {
    public uint StructSize, State; public PhysicsDimension Dimension; public uint LiveWorlds;
    public ulong LiveBodies, Sequence, Batches, CopiedBytes, Errors, LiveJobs;
    public double LastStepMilliseconds;
}
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint ReadCounters(ulong module,ulong world,PhysicsCounters* output,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CreateWorld(ulong module,WorldDescription* desc,ulong* output,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CreateBoxes2(ulong module,ulong world,Box2D* boxes,uint count,ulong* output,uint capacity,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CreateBoxes3(ulong module,ulong world,Box3D* boxes,uint count,ulong* output,uint capacity,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint DestroyBodies(ulong module,ulong world,ulong* bodies,uint count,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint Set2(ulong module,ulong world,Velocity2D* velocities,uint count,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint Set3(ulong module,ulong world,Velocity3D* velocities,uint count,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint StepWorld(ulong module,ulong world,float dt,ulong* sequence,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint Read2(ulong module,ulong world,ulong sequence,ulong* bodies,uint count,BodyState2D* output,uint capacity,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint Read3(ulong module,ulong world,ulong sequence,ulong* bodies,uint count,BodyState3D* output,uint capacity,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint GetStats(ulong module,ulong world,PhysicsStats* output,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint DestroyWorld(ulong module,ulong world,PluginError* error);
// No World/Editor/behaviour references. All arrays belong to the caller and may be reused;
// one cached delegate call per batch, no per-body P/Invoke or native memory exposure.
public sealed unsafe class PhysicsWorld : IDisposable {
    public const int MaximumBatch = 4096;
    private readonly PluginLease _lease;
    private readonly CreateBoxes2 _create2;
    private readonly CreateBoxes3 _create3;
    private readonly DestroyBodies _destroyBodies;
    private readonly Set2 _set2;
    private readonly Set3 _set3;
    private readonly StepWorld _step;
    private readonly Read2 _read2;
    private readonly Read3 _read3;
    private readonly GetStats _stats;
    private readonly DestroyWorld _destroy;
    private readonly ReadCounters? _counters;
    private ulong _handle;
    private PluginModule Module => _lease.Module;
    public PhysicsDimension Dimension { get; }
    public PhysicsCapabilities Capabilities { get; }
    public static PhysicsWorld Create2D(PluginModule module,Vector2? gravity=null,uint maximumBodies=4096,uint subSteps=4) =>
        new(module,PhysicsDimension.Two,new Vector3(gravity ?? new Vector2(0,-9.81f),0),maximumBodies,subSteps);
    public static PhysicsWorld Create3D(PluginModule module,Vector3? gravity=null,uint maximumBodies=4096) =>
        new(module,PhysicsDimension.Three,gravity ?? new Vector3(0,-9.81f,0),maximumBodies,1);
    private PhysicsWorld(PluginModule module,PhysicsDimension dimension,Vector3 gravity,uint maximum,uint subSteps) {
        if(module.Kind!=ModuleKind.Physics)throw new ArgumentException("Physics module required.");
        _lease=module.AcquireLease();Dimension=dimension;Capabilities=(PhysicsCapabilities)module.Capabilities;
        try {
            if(module.Capabilities!=63)throw new InvalidOperationException("Unsupported physics capability table.");
            var create=module.ReadFunction<CreateWorld>(56);_create2=module.ReadFunction<CreateBoxes2>(64);_create3=module.ReadFunction<CreateBoxes3>(72);
            _destroyBodies=module.ReadFunction<DestroyBodies>(80);_set2=module.ReadFunction<Set2>(88);_set3=module.ReadFunction<Set3>(96);
            _step=module.ReadFunction<StepWorld>(104);_read2=module.ReadFunction<Read2>(112);_read3=module.ReadFunction<Read3>(120);
            _stats=module.ReadFunction<GetStats>(128);_destroy=module.ReadFunction<DestroyWorld>(136);
            if(module.AbiMinor>=1)_counters=module.ReadFunction<ReadCounters>(144);
            WorldDescription desc=new(){Size=32,Dimension=(uint)dimension,Maximum=maximum,SubSteps=subSteps,Gravity=gravity};
            PluginError error=default;ulong handle=0;
            PluginModule.Check(module.Id,"create_physics_world",create(module.Context,&desc,&handle,&error),error);_handle=handle;
        }catch{_lease.Dispose();throw;}
    }
    private void Verify() { _=Module;ObjectDisposedException.ThrowIf(_handle==0,this); }
    private void Verify(PhysicsDimension dimension,int count,int capacity=0) {
        Verify();if(Dimension!=dimension || count>MaximumBatch || capacity>MaximumBatch)throw new ArgumentException("Wrong physics dimension or batch budget.");
    }
    public void CreateBoxes2D(ReadOnlySpan<Box2D> boxes,Span<ulong> output) {
        Verify(PhysicsDimension.Two,boxes.Length,output.Length);PluginError error=default;
        fixed(Box2D* b=boxes)fixed(ulong* o=output)PluginModule.Check(Module.Id,"create_boxes_2d",_create2(Module.Context,_handle,b,(uint)boxes.Length,o,(uint)output.Length,&error),error);
    }
    public void CreateBoxes3D(ReadOnlySpan<Box3D> boxes,Span<ulong> output) {
        Verify(PhysicsDimension.Three,boxes.Length,output.Length);PluginError error=default;
        fixed(Box3D* b=boxes)fixed(ulong* o=output)PluginModule.Check(Module.Id,"create_boxes_3d",_create3(Module.Context,_handle,b,(uint)boxes.Length,o,(uint)output.Length,&error),error);
    }
    public void DestroyBodies(ReadOnlySpan<ulong> bodies) {
        Verify(Dimension,bodies.Length);PluginError error=default;
        fixed(ulong* b=bodies)PluginModule.Check(Module.Id,"destroy_physics_bodies",_destroyBodies(Module.Context,_handle,b,(uint)bodies.Length,&error),error);
    }
    public void SetVelocities2D(ReadOnlySpan<Velocity2D> velocities) {
        Verify(PhysicsDimension.Two,velocities.Length);PluginError error=default;
        fixed(Velocity2D* v=velocities)PluginModule.Check(Module.Id,"set_velocities_2d",_set2(Module.Context,_handle,v,(uint)velocities.Length,&error),error);
    }
    public void SetVelocities3D(ReadOnlySpan<Velocity3D> velocities) {
        Verify(PhysicsDimension.Three,velocities.Length);PluginError error=default;
        fixed(Velocity3D* v=velocities)PluginModule.Check(Module.Id,"set_velocities_3d",_set3(Module.Context,_handle,v,(uint)velocities.Length,&error),error);
    }
    public ulong Step(float seconds) {
        Verify();PluginError error=default;ulong sequence=0;
        PluginModule.Check(Module.Id,"step_physics",_step(Module.Context,_handle,seconds,&sequence,&error),error);return sequence;
    }
    public void ReadBodyStates2D(ulong sequence,ReadOnlySpan<ulong> bodies,Span<BodyState2D> output) {
        Verify(PhysicsDimension.Two,bodies.Length,output.Length);PluginError error=default;
        fixed(ulong* b=bodies)fixed(BodyState2D* o=output)PluginModule.Check(Module.Id,"read_states_2d",_read2(Module.Context,_handle,sequence,b,(uint)bodies.Length,o,(uint)output.Length,&error),error);
    }
    public void ReadBodyStates3D(ulong sequence,ReadOnlySpan<ulong> bodies,Span<BodyState3D> output) {
        Verify(PhysicsDimension.Three,bodies.Length,output.Length);PluginError error=default;
        fixed(ulong* b=bodies)fixed(BodyState3D* o=output)PluginModule.Check(Module.Id,"read_states_3d",_read3(Module.Context,_handle,sequence,b,(uint)bodies.Length,o,(uint)output.Length,&error),error);
    }
    public PhysicsCounters NativeCounters {
        get {Verify();if(_counters is null)throw new PluginException(Module.Id,"physics_counters",PluginResult.UnsupportedFeature,"Raw counters require Physics ABI 1.1.");
            PluginError error=default;PhysicsCounters counters=default;
            PluginModule.Check(Module.Id,"physics_counters",_counters(Module.Context,_handle,&counters,&error),error);
            if(counters.StructSize!=72 || counters.Dimension!=Dimension || counters.State is <1 or >2 ||
                !double.IsFinite(counters.LastStepMilliseconds) || counters.LastStepMilliseconds<0)
                throw new InvalidOperationException("Invalid raw physics counters.");
            return counters;}
    }
    public PhysicsStats Stats {
        get {Verify();PluginError error=default;PhysicsStats stats=default;
            PluginModule.Check(Module.Id,"physics_stats",_stats(Module.Context,_handle,&stats,&error),error);
            if(stats.StructSize!=88 || stats.Dimension!=Dimension || stats.State is <1 or >2)throw new InvalidOperationException("Invalid physics stats.");
            return stats;}
    }
    public void Dispose() {
        if(_handle==0)return;Verify();PluginError error=default;
        PluginModule.Check(Module.Id,"destroy_physics_world",_destroy(Module.Context,_handle,&error),error);_handle=0;_lease.Dispose();
    }
}
// Explicit optional path: disabled never loads a DLL. Enabled failures are reported,
// not silently downgraded; application/headless runtime does not reference this package.
public sealed class PhysicsModuleHost : IDisposable {
    private readonly PluginLoader _loader=new();
    public PluginModule? Module { get; }
    public PhysicsModuleHost(string pluginRoot,bool enabled=true) {
        if(!enabled)return;
        try {
            _loader.Load(pluginRoot,[new("ncma.physics",ModuleKind.Physics,"NcmaPhysics.dll","NcmaPhysics.dll",1,1,[])]);
            Module=_loader.Modules.Single();
        }catch{_loader.Dispose();throw;}
    }
    public void Dispose()=>_loader.Dispose();
}
