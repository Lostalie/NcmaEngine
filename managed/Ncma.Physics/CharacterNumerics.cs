using System.Numerics;
using System.Runtime.InteropServices;
using Ncma.Interop;
namespace Ncma.Physics;

// Copied numeric POD only. Runtime resource handles must never be serialized as scene identity.
// Y-up/metres/seconds, capsule FOOT origin, xyzw quaternion. Native does not integrate
// character gravity: the managed caller supplies velocity, including its vertical policy.
[StructLayout(LayoutKind.Sequential)]
public struct CapsuleDescription {
    public uint StructSize, Category, Mask; internal uint Reserved;
    public Vector3 Foot; public float Radius, HalfHeight, MaximumSlopeRadians, StepHeight, FloorDistance;
    public float Mass, MaximumStrength, Padding; internal float ReservedFloat; public ulong Correlation;
    public static CapsuleDescription Default(Vector3 foot,uint category=2,uint mask=uint.MaxValue) => new() {
        StructSize=72,Category=category,Mask=mask,Foot=foot,Radius=.3f,HalfHeight=.6f,MaximumSlopeRadians=.7f,
        StepHeight=.4f,FloorDistance=.5f,Mass=70,MaximumStrength=1000,Padding=.02f
    };
}
[StructLayout(LayoutKind.Sequential)]
public struct CollisionBox {
    public uint StructSize,Category,Dynamic; internal uint Reserved;
    public Vector3 Position; public float Density; public Vector3 HalfExtents; internal float ReservedFloat;
    public Quaternion Rotation; public ulong Correlation;
    public static CollisionBox Create(Vector3 position,Vector3 extents,uint category=1,bool dynamic=false) => new() {
        StructSize=72,Category=category,Dynamic=dynamic ? 1u : 0u,Position=position,HalfExtents=extents,Density=1000,Rotation=Quaternion.Identity
    };
}
[StructLayout(LayoutKind.Sequential)]
public struct CharacterVelocity {public ulong Character;public Vector3 Velocity;internal uint Reserved;public Quaternion Rotation;}
public enum GroundState : uint { Ground, Steep, Unsupported, Air }
public enum CollisionResourceKind : uint { None, Body, Character }
[StructLayout(LayoutKind.Sequential)]
public struct CharacterState {
    public ulong Character,Correlation,Sequence;public Vector3 Foot,Velocity;public Quaternion Rotation;
    public Vector3 GroundNormal,GroundVelocity;public GroundState Ground;public CollisionResourceKind GroundKind;public ulong GroundResource;
}
[StructLayout(LayoutKind.Sequential)]
public struct CharacterContact {
    public ulong Character,Other,Sequence;public CollisionResourceKind OtherKind;public uint Subshape;
    public Vector3 Position,Normal;public float Separation;public uint Flags;
}
[StructLayout(LayoutKind.Sequential)]
public struct CharacterStepReceipt {public ulong Sequence;public uint States,Contacts;}
[StructLayout(LayoutKind.Sequential)]
public struct PhysicsRay {public uint StructSize,Mask;public Vector3 Origin,Displacement;public ulong Ignore;}
[StructLayout(LayoutKind.Sequential)]
public struct CapsuleSweep {
    public uint StructSize,Mask;public Vector3 Foot,Displacement;public float Radius,HalfHeight;public Quaternion Rotation;public ulong Ignore;
}
[StructLayout(LayoutKind.Sequential)]
public struct PhysicsQueryHit {
    public uint Hit;public CollisionResourceKind Kind;public ulong Resource;public float Fraction;public Vector3 Position,Normal;
    public uint Subshape;public ulong Sequence;internal ulong Reserved;
}
[StructLayout(LayoutKind.Sequential)]
internal struct CharacterApi {public uint Size,Major,Minor,Maximum;public ulong Capabilities;public nint CreateBoxes,CreateCapsules,Destroy,Step,Read,Ray,Sweep;}
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint QueryCharacters(ulong module,uint major,uint minor,CharacterApi* output,uint capacity,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CreateCollisionBoxes(ulong module,ulong world,CollisionBox* input,uint count,ulong* output,uint capacity,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CreateCapsules(ulong module,ulong world,CapsuleDescription* input,uint count,ulong* output,uint capacity,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint DestroyCharacters(ulong module,ulong world,ulong* input,uint count,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint StepCharacters(ulong module,ulong world,ulong expected,float seconds,CharacterVelocity* input,uint count,CharacterState* states,uint capacity,CharacterContact* contacts,uint contactCapacity,CharacterStepReceipt* receipt,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint ReadCharacters(ulong module,ulong world,ulong sequence,ulong* input,uint count,CharacterState* output,uint capacity,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CastRay(ulong module,ulong world,ulong sequence,PhysicsRay* input,PhysicsQueryHit* output,PluginError* error);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CastCapsule(ulong module,ulong world,ulong sequence,CapsuleSweep* input,PhysicsQueryHit* output,PluginError* error);

public sealed unsafe partial class PhysicsWorld {
    public const int MaximumCharacters=32, MaximumContactsPerCharacter=64;
    private CreateCollisionBoxes? _collisionBoxes;private CreateCapsules? _capsules;private DestroyCharacters? _destroyCharacters;
    private StepCharacters? _characterStep;private ReadCharacters? _characterRead;private CastRay? _ray;private CastCapsule? _sweep;
    private void Characters() {
        Verify(PhysicsDimension.Three,0);
        if(Module.AbiMinor!=2)throw new PluginException(Module.Id,"character_api",PluginResult.UnsupportedFeature,"Character numerics require explicitly negotiated Physics 1.2.");
        if(_capsules is not null)return;
        var query=Module.ReadFunction<QueryCharacters>(152);CharacterApi api=default;PluginError error=default;
        PluginModule.Check(Module.Id,"character_api",query(Module.Context,1,0,&api,(uint)sizeof(CharacterApi),&error),error);
        if(api.Size!=80 || api.Major!=1 || api.Minor!=0 || api.Maximum!=32 || api.Capabilities!=31 ||
            api.CreateBoxes==0 || api.CreateCapsules==0 || api.Destroy==0 || api.Step==0 || api.Read==0 || api.Ray==0 || api.Sweep==0)
            throw new PluginException(Module.Id,"character_api",PluginResult.AbiMismatch,"Invalid character numerical table.");
        _collisionBoxes=Marshal.GetDelegateForFunctionPointer<CreateCollisionBoxes>(api.CreateBoxes);
        _destroyCharacters=Marshal.GetDelegateForFunctionPointer<DestroyCharacters>(api.Destroy);
        _characterStep=Marshal.GetDelegateForFunctionPointer<StepCharacters>(api.Step);_characterRead=Marshal.GetDelegateForFunctionPointer<ReadCharacters>(api.Read);
        _ray=Marshal.GetDelegateForFunctionPointer<CastRay>(api.Ray);_sweep=Marshal.GetDelegateForFunctionPointer<CastCapsule>(api.Sweep);
        _capsules=Marshal.GetDelegateForFunctionPointer<CreateCapsules>(api.CreateCapsules); // Install readiness last.
    }
    private static void CharacterBudget(int count,int capacity) {
        if(count>MaximumCharacters || capacity>MaximumCharacters)throw new ArgumentException("Character batch budget exceeded.");
    }
    public void CreateCollisionBoxes(ReadOnlySpan<CollisionBox> input,Span<ulong> output) {
        Characters();Verify(Dimension,input.Length,output.Length);PluginError error=default;
        fixed(CollisionBox* i=input)fixed(ulong* o=output)PluginModule.Check(Module.Id,"collision_boxes",_collisionBoxes!(Module.Context,_handle,i,(uint)input.Length,o,(uint)output.Length,&error),error);
    }
    public void CreateCapsules(ReadOnlySpan<CapsuleDescription> input,Span<ulong> output) {
        Characters();CharacterBudget(input.Length,output.Length);PluginError error=default;
        fixed(CapsuleDescription* i=input)fixed(ulong* o=output)PluginModule.Check(Module.Id,"create_capsules",_capsules!(Module.Context,_handle,i,(uint)input.Length,o,(uint)output.Length,&error),error);
    }
    // Complete set only; frozen topology avoids dangling predictive-contact resource IDs.
    // Close failure keeps the native world and module lease; caller can explicitly retry.
    public void DestroyCharacterSet(ReadOnlySpan<ulong> handles) {
        Characters();CharacterBudget(handles.Length,0);PluginError error=default;
        fixed(ulong* i=handles)PluginModule.Check(Module.Id,"destroy_character_set",_destroyCharacters!(Module.Context,_handle,i,(uint)handles.Length,&error),error);
    }
    public void ReadCharacterStates(ulong sequence,ReadOnlySpan<ulong> handles,Span<CharacterState> output) {
        Characters();CharacterBudget(handles.Length,output.Length);PluginError error=default;
        fixed(ulong* i=handles)fixed(CharacterState* o=output)PluginModule.Check(Module.Id,"read_characters",_characterRead!(Module.Context,_handle,sequence,i,(uint)handles.Length,o,(uint)output.Length,&error),error);
    }
    // Complete canonical ascending set. Bodies step ONCE, then each character. Output buffers
    // require count states and count*64 contacts before executing; no allocating/truncating output.
    // Native execution is irreversible; failures invalidate coupled snapshots (M4.3 owns coupling).
    public CharacterStepReceipt StepCharacters(ulong expectedSequence,float seconds,ReadOnlySpan<CharacterVelocity> input,Span<CharacterState> states,Span<CharacterContact> contacts) {
        Characters();CharacterBudget(input.Length,states.Length);
        if(contacts.Length>MaximumCharacters*MaximumContactsPerCharacter)throw new ArgumentException("Contact budget exceeded.");
        PluginError error=default;CharacterStepReceipt receipt=default;
        fixed(CharacterVelocity* i=input)fixed(CharacterState* s=states)fixed(CharacterContact* c=contacts)
            PluginModule.Check(Module.Id,"step_characters",_characterStep!(Module.Context,_handle,expectedSequence,seconds,i,(uint)input.Length,s,(uint)states.Length,c,(uint)contacts.Length,&receipt,&error),error);
        return receipt;
    }
    public PhysicsQueryHit Ray(ulong sequence,PhysicsRay ray) {
        Characters();PluginError error=default;PhysicsQueryHit hit=default;
        PluginModule.Check(Module.Id,"physics_ray",_ray!(Module.Context,_handle,sequence,&ray,&hit,&error),error);return hit;
    }
    public PhysicsQueryHit Sweep(ulong sequence,CapsuleSweep sweep) {
        Characters();PluginError error=default;PhysicsQueryHit hit=default;
        PluginModule.Check(Module.Id,"physics_sweep",_sweep!(Module.Context,_handle,sequence,&sweep,&hit,&error),error);return hit;
    }
}
