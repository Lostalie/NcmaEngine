using System.Numerics;
using Ncma.Animation;
using Ncma.Assets;
namespace Ncma.Characters;
using Vector3 = System.Numerics.Vector3;

public readonly record struct RootMotionDelta(Vector3 Translation, float Yaw);
// Immutable, bounded managed root sampling. Full poses/GPU work stay in the numerical plugin.
public sealed class RootMotionTrack
{
    private readonly ImportKey[] _keys;
    private readonly double[] _yaw;
    private readonly Matrix4x4 _inverseStart, _cycle;
    public double Duration { get; }
    public int RootIndex { get; }
    internal int KeyCount => _keys.Length;
    internal Matrix4x4 InitialPlanar => Planar(_keys[0].Value);
    public RootMotionTrack(SkeletonPayload skeleton, ClipPayload clip, int rootIndex)
    {
        var rig=ModelPayloadCodec.DecodeSkeleton(ModelPayloadCodec.Encode(skeleton));
        var value=ModelPayloadCodec.DecodeClip(ModelPayloadCodec.Encode(clip));
        if(value.BoneCount!=rig.Bones.Length || rootIndex<0 || rootIndex>=rig.Bones.Length || rig.Bones[rootIndex].Parent!=-1 || rig.Bones.Count(b=>b.Parent==-1)!=1)
            throw new ArgumentException("Root motion requires the exact rig and its single top-level root.");
        RootIndex=rootIndex;Duration=value.Clip.Duration;
        var track=value.Clip.Tracks.SingleOrDefault(t=>t.Bone==rootIndex);
        _keys=track?.Keys ?? [new(0,rig.Bones[rootIndex].BindLocal),new(Duration,rig.Bones[rootIndex].BindLocal)];
        _yaw=new double[_keys.Length];
        for(int i=0;i<_keys.Length;i++) {
            var t=_keys[i].Value;
            if(Vector3.DistanceSquared(t.Scale,Vector3.One)>1e-10f || Math.Abs(t.Rotation.X)>1e-6 || Math.Abs(t.Rotation.Z)>1e-6)
                throw new ArgumentException("XZ/Yaw extraction rejects scaled or pitched/rolled root tracks.");
            double angle=2*Math.Atan2(t.Rotation.Y,t.Rotation.W);
            _yaw[i]=i==0 ? angle : _yaw[i-1]+Math.IEEERemainder(angle-_yaw[i-1],2*Math.PI);
        }
        if(!Matrix4x4.Invert(Planar(_keys[0].Value),out _inverseStart))throw new ArgumentException("Singular root basis.");
        _cycle=Relative(Duration);
    }
    private static Matrix4x4 Planar(ImportTransform t) => Matrix4x4.CreateFromQuaternion(t.Rotation)*Matrix4x4.CreateTranslation(t.Position.X,0,t.Position.Z);
    private (ImportTransform Pose,double Yaw) At(double time)
    {
        if(!double.IsFinite(time) || time<0 || time>Duration)throw new ArgumentException("Root sample time range.");
        int lo=0,hi=_keys.Length-1;
        while(hi-lo>1){int mid=(lo+hi)/2;if(_keys[mid].Time<=time)lo=mid;else hi=mid;}
        var a=_keys[lo];var b=_keys[hi];float f=(float)((time-a.Time)/(b.Time-a.Time));
        return (new(Vector3.Lerp(a.Value.Position,b.Value.Position,f),Quaternion.Slerp(a.Value.Rotation,b.Value.Rotation,f),Vector3.One),_yaw[lo]+(_yaw[hi]-_yaw[lo])*f);
    }
    private Matrix4x4 Relative(double time) => _inverseStart*Planar(At(time).Pose);
    public RootMotionDelta Extract(ClipInterval interval,bool loop)
    {
        double a=interval.Previous,b=interval.Current;
        if(!double.IsFinite(a)||!double.IsFinite(b)||a<0||b<a||b>1e9 || !loop&&b>Duration)throw new ArgumentException("Bounded forward root interval required.");
        double wraps=loop ? Math.Floor(b/Duration)-Math.Floor(a/Duration) : 0;
        if(wraps>32)throw new ArgumentException("Root motion loop crossing budget exceeded.");
        double start=loop?a%Duration:a,end=loop?b%Duration:b;
        Matrix4x4 cycles=Matrix4x4.Identity;for(int i=0;i<(int)wraps;i++)cycles*= _cycle;
        if(!Matrix4x4.Invert(Relative(start),out var inverse))throw new ArgumentException("Invalid root sample.");
        var delta=Relative(end)*cycles*inverse;
        double yaw=At(end).Yaw-At(start).Yaw+wraps*(_yaw[^1]-_yaw[0]);
        var translation=delta.Translation;
        if(!float.IsFinite(translation.LengthSquared())||translation.LengthSquared()>1e6f||!double.IsFinite(yaw)||Math.Abs(yaw)>Math.PI)
            throw new ArgumentException("Root interval displacement/yaw exceeds movement budget.");
        return new(translation,(float)yaw);
    }
    public void RemoveRoot(Span<Matrix4x4> models)
    {
        if(models.Length<=RootIndex || !Matrix4x4.Decompose(models[RootIndex],out var scale,out var rotation,out var position) ||
            Vector3.DistanceSquared(scale,Vector3.One)>1e-8f || Math.Abs(rotation.X)>1e-5 || Math.Abs(rotation.Z)>1e-5)
            throw new ArgumentException("Unsupported visual root pose.");
        var relative=_inverseStart*Planar(new(position,rotation,Vector3.One));
        if(!Matrix4x4.Invert(relative,out var correction))throw new ArgumentException("Singular visual root correction.");
        for(int i=0;i<models.Length;i++)models[i]*=correction;
    }
}
