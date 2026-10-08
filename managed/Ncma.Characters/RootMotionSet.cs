using System.Numerics;
using Ncma.Animation;
using Ncma.Assets;
using Ncma.Assets.Runtime;
using Ncma.Runtime;
using Ncma.Scene.Rendering;
namespace Ncma.Characters;
using Vector3 = System.Numerics.Vector3;

public readonly record struct RootMotionStatus(Guid ObjectId, ulong Tick, Guid ClipId, double Time,
    Vector3 DesiredDisplacement, float DesiredYaw, Vector3 AcceptedDisplacement);
// Pins/prepares immutable NCA off-frame; one committed ClipClock feeds motion AND presentation.
internal sealed class RootMotionSet : IDisposable
{
    private sealed class Entry(Guid id, ClipPlaybackData startup, Dictionary<Guid,RootMotionTrack> tracks)
    {
        internal readonly Guid Id=id;
        internal readonly ClipPlaybackData Startup=startup;
        internal readonly Dictionary<Guid,RootMotionTrack> Tracks=tracks;
        internal ClipPlaybackData Requested=startup, Committed=startup, Prepared=startup;
        internal ClipClock? Clock;
        internal Vector3 Start, Desired, Accepted;
        internal float Yaw;
        internal bool Restart, PreparedRestart;
    }
    private readonly Dictionary<Guid,Entry> _entries=[];
    private readonly Dictionary<(Guid Clip,int Root),RootMotionTrack> _tracks=[];
    private int _retainedKeys;
    private RuntimeAssetLease? _lease;
    private World? _world;
    internal RootMotionSet(World world,PreparedSceneAssetLease? assets)
    {
        try {
            foreach(var obj in world.GetObjects().Where(o=>o.Has<RootMotionData>()&&!o.Has<AnimatorData>())) {
                if(assets is null)throw new ArgumentException("Root motion requires off-frame immutable asset preparation, including Headless.");
                _lease??=assets.Assets.AcquireLease();
                var skin=obj.Get<SkinnedMeshData>();var settings=obj.Get<ClipPlaybackData>();var root=obj.Get<RootMotionData>();
                var mesh=_lease.RequireMesh(skin.MeshId,AssetKind.SkinnedMesh);
                var rig=(RuntimeDataAsset)_lease.Require(skin.SkeletonId,AssetKind.Skeleton);
                var model=(RuntimeDataAsset)_lease.Require(skin.CharacterId,AssetKind.Character);
                if(mesh.ModelId!=skin.CharacterId||mesh.SkeletonId!=skin.SkeletonId||rig.ModelId!=skin.CharacterId||rig.Generation!=mesh.Generation||model.Generation!=mesh.Generation)
                    throw new ArgumentException("Exact root motion model/mesh/rig generation required.");
                var skeleton=ModelPayloadCodec.DecodeSkeleton(rig.CopyData());
                var manifest=ModelAssetManifestCodec.Decode(model.CopyData());var tracks=new Dictionary<Guid,RootMotionTrack>();
                foreach(Guid id in manifest.Clips) {
                    if(tracks.Count>=128)throw new ArgumentException("Root motion prepared clip budget exceeded.");
                    var clip=(RuntimeDataAsset)_lease.Require(id,AssetKind.Clip);
                    if(clip.ModelId!=skin.CharacterId||clip.SkeletonId!=skin.SkeletonId||clip.Generation!=mesh.Generation)throw new ArgumentException("Root clip generation mismatch.");
                    var key=(id,root.RootBoneIndex);
                    if(!_tracks.TryGetValue(key,out var track)) {
                        if(_tracks.Count>=128)throw new ArgumentException("Scene root clip budget is 128.");
                        track=new(skeleton,ModelPayloadCodec.DecodeClip(clip.CopyData()),root.RootBoneIndex);
                        if(track.KeyCount>262144-_retainedKeys)throw new ArgumentException("Scene root key budget is 262144.");
                        _retainedKeys+=track.KeyCount;_tracks.Add(key,track);
                    }
                    tracks.Add(id,track);
                }
                if(!tracks.ContainsKey(settings.ClipId))throw new ArgumentException("Root playback is not in the pinned model generation.");
                _entries.Add(obj.PersistentId,new(obj.PersistentId,settings,tracks));
            }
        }catch{Dispose();throw;}
    }
    internal Guid[] Ids => _entries.Keys.ToArray();
    internal int TrackCount => _tracks.Count;
    internal int RetainedKeyCount => _retainedKeys;
    internal bool Contains(Guid id)=>_entries.ContainsKey(id);
    internal void Initialize(World world)
    {
        _world=world;
        foreach(var e in _entries.Values) {
            e.Requested=e.Prepared=e.Committed=e.Startup;
            e.Clock=new(world,e.Startup,e.Tracks[e.Startup.ClipId].Duration);
            e.Start=e.Desired=e.Accepted=Vector3.Zero;e.Yaw=0;
            e.Restart=e.PreparedRestart=false;
        }
    }
    internal void Request(Guid id,ClipPlaybackData settings)
    {
        var e=_entries[id];_=ClipPlaybackData.Validate(settings);
        if(!e.Tracks.TryGetValue(settings.ClipId,out var track)||settings.StartTime>track.Duration)throw new ArgumentException("Playback must select a bounded pinned clip.");
        e.Requested=settings;
    }
    internal double Duration(Guid id,Guid clip)=>_entries.TryGetValue(id,out var e)&&e.Tracks.TryGetValue(clip,out var track)?track.Duration:throw new ArgumentException("Action clip must belong to this pinned model generation.");
    internal double Time(Guid id)=>_entries[id].Clock!.UnwrappedTime;
    internal ClipInterval Interval(Guid id,ClipPlaybackData settings,double h,bool restart)=>_entries[id].Clock!.PrepareNext(_world!,settings,Duration(id,settings.ClipId),h,restart);
    internal void SelectForStep(Guid id,ClipPlaybackData settings,bool restart){Request(id,settings);_entries[id].Restart=restart;}
    internal RootMotionDelta Prepare(Guid id,TransformData start,double h)
    {
        var e=_entries[id];var track=e.Tracks[e.Requested.ClipId];
        var interval=e.Clock!.PrepareNext(_world!,e.Requested,track.Duration,h,e.Restart);
        var delta=track.Extract(interval,e.Requested.Loop);
        e.Prepared=e.Requested;e.PreparedRestart=e.Restart;e.Start=start.Position;e.Desired=Vector3.Transform(delta.Translation,start.Rotation);e.Yaw=delta.Yaw;
        return new(e.Desired,e.Yaw);
    }
    internal void Commit(World world,double h)
    {
        if(world!=_world)throw new ArgumentException("Root motion world identity mismatch.");
        foreach(var e in _entries.Values) {
            e.Clock!.ObserveCommitted(world,e.Prepared,e.Tracks[e.Prepared.ClipId].Duration,h,e.PreparedRestart);
            e.Restart=e.PreparedRestart=false;
            e.Committed=e.Prepared;e.Accepted=world.FindObject(e.Id).Get<TransformData>().Position-e.Start;
        }
    }
    internal ClipPlaybackData Playback(Guid id)=>_entries[id].Committed;
    internal ClipSampleTimes Sample(Guid id,float alpha,bool paused)=>_entries[id].Clock!.Sample(alpha,paused);
    internal void RemoveRoot(Guid id,Span<Matrix4x4> models) {var e=_entries[id];e.Tracks[e.Committed.ClipId].RemoveRoot(models);}
    internal RootMotionStatus Inspect(Guid id) {
        var e=_entries[id];return new(id,e.Clock!.CommittedTick,e.Committed.ClipId,e.Clock.UnwrappedTime,e.Desired,e.Yaw,e.Accepted);
    }
    public void Dispose(){_lease?.Dispose();_lease=null;}
}
