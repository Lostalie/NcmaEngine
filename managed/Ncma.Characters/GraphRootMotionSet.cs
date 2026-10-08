using System.Numerics;
using Ncma.Animation;
using Ncma.Assets;
using Ncma.Assets.Runtime;
using Ncma.Runtime;
using Ncma.Scene.Rendering;
namespace Ncma.Characters;
using Vector3 = System.Numerics.Vector3;

// Exact prepared publication, no ClipClock: every interval comes from the same pending graph token.
internal sealed class GraphRootMotionSet : IDisposable
{
    private sealed class Entry(Guid id, GraphRootMotionRecipe recipe, RootMotionTrack anchor, int capacity)
    {
        internal readonly Guid Id = id;
        internal readonly GraphRootMotionRecipe Recipe = recipe;
        internal readonly RootMotionTrack Anchor = anchor;
        internal readonly AnimationPoseInstruction[] Plan = new AnimationPoseInstruction[capacity];
        internal AnimationGraphFrame Pending;
        internal Vector3 Start, Desired, Accepted;
        internal float Yaw;
        internal RootMotionStatus Committed;
    }
    private readonly Dictionary<Guid, Entry> _entries = [];
    private readonly SceneAnimatorRuntime? _animators;
    private RuntimeAssetLease? _lease;
    internal GraphRootMotionSet(World world, PreparedSceneAssetLease? assets, SceneAnimatorRuntime? animators,int remainingTracks,int remainingKeys)
    {
        _animators = animators;
        var tracks = new Dictionary<(Guid, int), RootMotionTrack>(); int keys = 0;
        try {
            foreach (var obj in world.GetObjects().Where(o => o.Has<RootMotionData>() && o.Has<AnimatorData>())) {
                if (assets is null || animators is null) throw new ArgumentException("Graph root requires the shared prepared Animator host.");
                _lease ??= assets.Assets.AcquireLease();
                var source = obj.Get<AnimatorData>(); var skin = obj.Get<SkinnedMeshData>(); var root = obj.Get<RootMotionData>();
                var program = animators.PreparedProgram(obj.PersistentId, _lease);
                var graph = (RuntimeAnimationGraphAsset)_lease.Require(source.GraphId, AssetKind.AnimationGraph);
                var d = graph.CopyDefinition(); var rig = (RuntimeDataAsset)_lease.Require(source.SkeletonId, AssetKind.Skeleton);
                var model = (RuntimeDataAsset)_lease.Require(skin.CharacterId, AssetKind.Character); var mesh = _lease.RequireMesh(skin.MeshId, AssetKind.SkinnedMesh);
                if (program.ContentHash != graph.ContentHash || program.SkeletonId != skin.SkeletonId || program.ResourceGeneration != rig.Generation ||
                    rig.ModelId != model.Id || mesh.ModelId != model.Id || mesh.SkeletonId != rig.Id || model.Generation != rig.Generation || mesh.Generation != rig.Generation)
                    throw new ArgumentException("Exact graph/skin/model/rig generation required for root extraction.");
                var skeleton = ModelPayloadCodec.DecodeSkeleton(rig.CopyData()); var selected = new Dictionary<Guid, RootMotionTrack>(); RootMotionTrack? anchor = null;
                foreach (Guid id in AnimationGraphValidation.ClipIds(d)) {
                    var clip = (RuntimeDataAsset)_lease.Require(id, AssetKind.Clip);
                    if (clip.ModelId != model.Id || clip.SkeletonId != rig.Id || clip.Generation != rig.Generation) throw new ArgumentException("Exact graph root clip closure required.");
                    if (!tracks.TryGetValue((id, root.RootBoneIndex), out var track)) {
                        if (tracks.Count >= remainingTracks) throw new ArgumentException("Combined clip/graph root track budget.");
                        track = new(skeleton, ModelPayloadCodec.DecodeClip(clip.CopyData()), root.RootBoneIndex);
                        keys = checked(keys + track.KeyCount); if (keys > remainingKeys) throw new ArgumentException("Combined clip/graph root key budget.");
                        tracks.Add((id, root.RootBoneIndex), track);
                    }
                    if (anchor is not null && !Same(anchor.InitialPlanar, track.InitialPlanar)) throw new ArgumentException("Graph root clips must share their initial planar anchor.");
                    anchor ??= track; selected.Add(id, track);
                }
                _entries.Add(obj.PersistentId, new(obj.PersistentId, new(selected, program.MaximumPlanInstructions), anchor!, program.MaximumPlanInstructions));
            }
        } catch { Dispose(); throw; }
    }
    private static bool Same(Matrix4x4 a, Matrix4x4 b)
    {
        ReadOnlySpan<Matrix4x4> left = [a], right = [b];
        var x = System.Runtime.InteropServices.MemoryMarshal.Cast<Matrix4x4, float>(left); var y = System.Runtime.InteropServices.MemoryMarshal.Cast<Matrix4x4, float>(right);
        for (int i = 0; i < x.Length; i++) if (Math.Abs(x[i] - y[i]) > 1e-5f) return false;
        return true;
    }
    internal Guid[] Ids => _entries.Keys.ToArray();
    internal bool Contains(Guid id) => _entries.ContainsKey(id);
    internal void Initialize(World world)
    { foreach (var e in _entries.Values) { e.Start = e.Desired = e.Accepted = Vector3.Zero; e.Yaw = 0; e.Pending = default; e.Committed = new(e.Id, world.Tick, Guid.Empty, 0, Vector3.Zero, 0, Vector3.Zero); } }
    internal RootMotionDelta Prepare(Guid id, TransformData start, Guid session, World world)
    {
        var e = _entries[id]; var frame = _animators!.CopyPreparedRootPlan(id, new(session, world.Identity, world.Tick), e.Plan);
        var delta = e.Recipe.Evaluate(e.Plan.AsSpan(0, frame.InstructionCount), frame.Output);
        e.Pending = frame; e.Start = start.Position; e.Desired = Vector3.Transform(delta.Translation, start.Rotation); e.Yaw = delta.Yaw;
        return new(e.Desired, e.Yaw);
    }
    internal void Commit(World world)
    {
        foreach (var e in _entries.Values) {
            var frame = _animators!.ReadFrame(e.Id);
            if (frame.Context.Tick != world.Tick || frame.Context != e.Pending.Context || frame.InstanceId != e.Pending.InstanceId) throw new InvalidOperationException("Graph root commit identity mismatch.");
            e.Accepted = world.FindObject(e.Id).Get<TransformData>().Position - e.Start;
            e.Committed = new(e.Id, world.Tick, Guid.Empty, 0, e.Desired, e.Yaw, e.Accepted); // Composite graph has no single clip clock.
        }
    }
    internal RootMotionStatus Inspect(Guid id) => _entries[id].Committed;
    internal void RemoveRoot(Guid id, Span<Matrix4x4> models) => _entries[id].Anchor.RemoveRoot(models);
    public void Dispose() { _lease?.Dispose(); _lease = null; }
}
