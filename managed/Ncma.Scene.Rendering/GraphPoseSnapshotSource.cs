using System.Numerics;
using Ncma.Animation;
using Ncma.Assets;
namespace Ncma.Scene.Rendering;
using Vector3=System.Numerics.Vector3;

// Bounded managed numerical snapshot at interruptions ONLY. Prepared immutable payloads; no IO/native/World.
// Presentation still executes the existing native sampling/blending kernel; this is not a second pose clock.
public sealed class GraphPoseSnapshotSource : IAnimationPoseSnapshotSource
{
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private readonly AnimationLocalTransform[] _bind,_scratch;
    private readonly Dictionary<Guid,ClipPayload> _clips=[];
    public Guid GraphId {get;}
    public Guid SkeletonId {get;}
    public string GraphContentHash {get;}
    public ulong ResourceGeneration {get;}
    public int BoneCount=>_bind.Length;
    public int ScratchTransforms=>_scratch.Length;
    public GraphPoseSnapshotSource(AnimationProgram program,SkeletonPayload skeleton,IReadOnlyDictionary<Guid,ClipPayload> clips)
    {
        ArgumentNullException.ThrowIfNull(program);ArgumentNullException.ThrowIfNull(clips);
        GraphId=program.AssetId;SkeletonId=program.SkeletonId;GraphContentHash=program.ContentHash;ResourceGeneration=program.ResourceGeneration;
        var rig=ModelPayloadCodec.DecodeSkeleton(ModelPayloadCodec.Encode(skeleton));_bind=rig.Bones.Select(b=>Value(b.BindLocal)).ToArray();
        int count=checked(program.MaximumPlanInstructions*BoneCount);if(count>65536||clips.Count is <1 or >128)throw new ArgumentException("Frozen pose numeric scratch/clip budget.");
        _scratch=new AnimationLocalTransform[count];int keys=0;
        foreach(var pair in clips){var clip=ModelPayloadCodec.DecodeClip(ModelPayloadCodec.Encode(pair.Value));if(pair.Key==Guid.Empty||clip.BoneCount!=BoneCount)throw new ArgumentException("Exact pose clip/rig required.");
            foreach(var t in clip.Clip.Tracks)foreach(var k in t.Keys){_=Value(k.Value);if(++keys>262144)throw new ArgumentException("Frozen pose key budget.");}_clips.Add(pair.Key,clip);}
    }
    private static AnimationLocalTransform Value(ImportTransform t)
    {if(!float.IsFinite(t.Position.LengthSquared())||!float.IsFinite(t.Rotation.LengthSquared())||Math.Abs(t.Rotation.LengthSquared()-1)>1e-4||!float.IsFinite(t.Scale.LengthSquared())||t.Scale.X<=0||Math.Abs(t.Scale.X-t.Scale.Y)>1e-5||Math.Abs(t.Scale.X-t.Scale.Z)>1e-5)throw new ArgumentException("Canonical finite normalized uniform local pose required.");return new(t.Position,Quaternion.Normalize(t.Rotation),t.Scale.X);}
    public void Evaluate(ReadOnlySpan<AnimationPoseInstruction> plan,int output,ReadOnlySpan<AnimationLocalTransform> frozen,ulong frozenGeneration,Span<AnimationLocalTransform> destination)
    {
        if(Environment.CurrentManagedThreadId!=_owner)throw new InvalidOperationException("Frozen pose numeric owner thread required.");
        if(plan.IsEmpty||checked(plan.Length*BoneCount)>_scratch.Length||output<0||output>=plan.Length||destination.Length<BoneCount)throw new ArgumentException("Frozen pose numeric recipe/output budget.");
        for(int i=0;i<plan.Length;i++) {
            var row=plan[i];var local=_scratch.AsSpan(i*BoneCount,BoneCount);if(row.NodeId==Guid.Empty)throw new ArgumentException("Pose row identity.");
            switch(row.Operation){
                case AnimationPoseOperation.Clip:
                    if(row.CacheGeneration!=0||row.SourceA!=-1||row.SourceB!=-1||row.Weight!=0||!_clips.TryGetValue(row.ClipId,out var clip)||clip.Clip.Duration!=row.Duration||!double.IsFinite(row.Previous)||!double.IsFinite(row.Current)||row.Previous<0||row.Current<row.Previous||!row.Loop&&row.Current>row.Duration)throw new ArgumentException("Exact pose clip interval required.");
                    _bind.CopyTo(local);double time=row.Loop?row.Current%row.Duration:row.Current;
                    foreach(var track in clip.Clip.Tracks){var keys=track.Keys;int bone=checked((int)track.Bone);if(time<=keys[0].Time)local[bone]=Value(keys[0].Value);else if(time>=keys[^1].Time)local[bone]=Value(keys[^1].Value);else {int a=0,b=keys.Length-1;while(b-a>1){int m=(a+b)/2;if(keys[m].Time<=time)a=m;else b=m;}local[bone]=Mix(Value(keys[a].Value),Value(keys[b].Value),(float)((time-keys[a].Time)/(keys[b].Time-keys[a].Time)));}}break;
                case AnimationPoseOperation.Blend:
                    Neutral(row);if(row.CacheGeneration!=0||row.SourceA<0||row.SourceA>=i||row.SourceB<0||row.SourceB>=i||!float.IsFinite(row.Weight)||row.Weight is <0 or >1)throw new ArgumentException("Backward bounded pose blend.");
                    for(int b=0;b<BoneCount;b++)local[b]=Mix(_scratch[row.SourceA*BoneCount+b],_scratch[row.SourceB*BoneCount+b],row.Weight);break;
                case AnimationPoseOperation.Frozen:
                    Neutral(row);if(row.SourceA!=-1||row.SourceB!=-1||row.Weight!=0||row.CacheGeneration==0||row.CacheGeneration!=frozenGeneration||frozen.Length!=BoneCount)throw new ArgumentException("Exact immutable frozen pose generation.");frozen.CopyTo(local);foreach(var v in local)if(!float.IsFinite(v.Position.LengthSquared())||!float.IsFinite(v.Scale)||v.Scale<=0||!float.IsFinite(v.Rotation.LengthSquared())||Math.Abs(v.Rotation.LengthSquared()-1)>1e-4)throw new ArgumentException("Invalid frozen TRS.");break;
                case AnimationPoseOperation.RootSource:
                    Neutral(row);if(row.CacheGeneration!=0||row.Weight!=0||row.SourceA<0||row.SourceA>=i||row.SourceB<0||row.SourceB>=i||plan[row.SourceB].Operation!=AnimationPoseOperation.Clip)throw new ArgumentException("Backward complete primary source required.");_scratch.AsSpan(row.SourceA*BoneCount,BoneCount).CopyTo(local);break;
                default:throw new ArgumentException("Closed pose operation required.");
            }
        }
        _scratch.AsSpan(output*BoneCount,BoneCount).CopyTo(destination);
    }
    private static void Neutral(AnimationPoseInstruction r){if(r.ClipId!=Guid.Empty||r.Previous!=0||r.Current!=0||r.Duration!=0||r.Loop)throw new ArgumentException("Neutral composite pose fields required.");}
    private static AnimationLocalTransform Mix(AnimationLocalTransform a,AnimationLocalTransform b,float weight)=>new(Vector3.Lerp(a.Position,b.Position,weight),Quaternion.Normalize(Quaternion.Slerp(a.Rotation,b.Rotation,weight)),a.Scale+(b.Scale-a.Scale)*weight);
}
