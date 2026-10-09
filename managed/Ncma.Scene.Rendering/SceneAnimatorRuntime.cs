using Ncma.Animation;
using Ncma.Assets;
using Ncma.Assets.Runtime;
using Ncma.Gameplay;
using Ncma.Runtime;
namespace Ncma.Scene.Rendering;

public sealed record AnimatorResourceIdentity(Guid Id,string Kind,string Generation,string ContentHash);
public sealed record AnimatorRuntimeBinding(Guid ObjectId,Guid GraphId,Guid SkeletonId,Guid PublicationId,
    string GraphHash,IReadOnlyList<AnimatorResourceIdentity> Resources);

// Same host composition in Editor, Player and Headless. No native pose/GPU/IO in a step.
// Explicit interruption opt-in uses bounded immutable managed numeric snapshots, never a second clock.
public sealed class SceneAnimatorRuntime : IDisposable, IWorldSystem, ICommittedStepObserver, IPlayCompositionLifetime
{
    private sealed class Actor(Guid id, AnimatorData source, AnimationProgram program)
    {
        internal readonly Guid Id = id;
        internal readonly AnimatorData Source = source;
        internal readonly AnimationProgram Program = program;
        internal AnimationGraphInstance? Instance;
        internal AnimationEvaluationToken? Pending;
        internal AnimatorRuntimeBinding? Binding;
        internal GraphPoseSnapshotSource? PoseSource;
    }
    private readonly PlaySession _play;
    private readonly Actor[] _actors;
    private readonly List<World.ComponentAuthority> _claims = [];
    private RuntimeAssetLease? _assets;
    private Guid _session, _world;
    private bool _disposed;
    private readonly bool _interruptions;
    private World World => _play.Document.World;
    public static SceneAnimatorRuntime? Compose(PlaySession play, PreparedSceneAssetLease? assets,bool interruptTransitions=false)
    {
        var snapshot = play.Document.CaptureSnapshot(); SceneRenderValidation.RequireComposition(snapshot);
        if (!snapshot.Objects.Any(o => o.Components.Any(c => c.TypeId == AnimatorData.TypeId))) return null;
        if (assets is null) throw new ArgumentException("Animator requires prepared assets, including Headless.");
        return new(play, assets,interruptTransitions);
    }
    private SceneAnimatorRuntime(PlaySession play, PreparedSceneAssetLease assets,bool interruptTransitions)
    {
        _play = play;
        _interruptions=interruptTransitions;
        try {
            _assets = assets.Assets.AcquireLease();
            var programs = new Dictionary<Guid, AnimationProgram>(); var actors = new List<Actor>(); int bones = 0;
            var poseSources=new Dictionary<Guid,GraphPoseSnapshotSource>();int poseScratch=0;
            foreach (var obj in World.GetObjects().Where(o => o.Has<AnimatorData>()).OrderBy(o => o.PersistentId)) {
                if (actors.Count >= 32) throw new ArgumentException("Animator actor budget is 32.");
                var source = obj.Get<AnimatorData>(); var skin = obj.Get<SkinnedMeshData>();
                var graph = (RuntimeAnimationGraphAsset)_assets.Require(source.GraphId, AssetKind.AnimationGraph);
                if (graph.CopyDefinition().SkeletonId != source.SkeletonId || skin.SkeletonId != source.SkeletonId) throw new ArgumentException("Animator graph/skin skeleton mismatch.");
                if (!programs.TryGetValue(graph.Id, out var program)) programs.Add(graph.Id, program = graph.PrepareProgram(_assets));
                var rig = (RuntimeDataAsset)_assets.Require(source.SkeletonId, AssetKind.Skeleton);
                if (rig.ModelId != skin.CharacterId) throw new ArgumentException("Animator skin model mismatch.");
                bones = checked(bones + ModelPayloadCodec.DecodeSkeleton(rig.CopyData()).Bones.Length);
                var root=(RuntimeDataAsset)_assets.Require(skin.CharacterId,AssetKind.Character);
                var manifest=ModelAssetManifestCodec.Decode(root.CopyData());
                var required=new HashSet<Guid>{source.GraphId,source.SkeletonId,skin.CharacterId};
                foreach(var m in manifest.Meshes){required.Add(m.Mesh);required.Add(m.Materials);}
                foreach(Guid id in manifest.Clips)required.Add(id);
                var proofs=_assets.List().Where(a=>required.Contains(a.Id)).Select(a=>new AnimatorResourceIdentity(a.Id,a.Kind.ToString(),a.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture),a.ContentHash)).OrderBy(a=>a.Id).ToArray();
                if(proofs.Length!=required.Count)throw new ArgumentException("Animator complete resource identity closure missing.");
                GraphPoseSnapshotSource? poseSource=null;if((interruptTransitions||program.InterruptTransitions) && !poseSources.TryGetValue(graph.Id,out poseSource)){poseSource=GraphPoseSnapshotPreparation.Prepare(program,_assets);poseScratch=checked(poseScratch+poseSource.ScratchTransforms);if(poseScratch>262144)throw new ArgumentException("Scene interruption numeric scratch budget.");poseSources.Add(graph.Id,poseSource);}
                actors.Add(new(obj.PersistentId, source, program){Binding=new(obj.PersistentId,source.GraphId,source.SkeletonId,_assets.Identity,program.ContentHash,Array.AsReadOnly(proofs)),PoseSource=poseSource});
            }
            if (bones > 32768) throw new ArgumentException("Animator scene bone budget.");
            _actors = actors.ToArray();
            play.AttachCompositionLifetime(this); play.AddSystem(this); play.AddCommittedObserver(this);
        } catch { _assets?.Dispose(); _assets = null; throw; }
    }
    void IPlayCompositionLifetime.Start()
    {
        World.VerifyAccess();
        if (_claims.Count != 0) throw new InvalidOperationException("Close previous Animator ownership before Start.");
        _session = _play.SessionId; _world = World.Identity;
        foreach (var actor in _actors) {
            if (World.FindObject(actor.Id).Get<AnimatorData>() != actor.Source) throw new ArgumentException("Animator startup binding changed.");
            actor.Instance = new(actor.Program, new(_session, _world, World.Tick),actor.PoseSource,_interruptions); actor.Pending = null;
        }
        _claims.Add(World.ClaimComponents<AnimatorData>(_actors.Select(a => a.Id).ToArray(), publishRequired: false, freezeMembership: true));
        _claims.Add(World.ClaimComponents<SkinnedMeshData>(_actors.Select(a => a.Id).ToArray(), publishRequired: false));
    }
    void IPlayCompositionLifetime.Stop()
    {
        World.VerifyAccess();
        foreach (var actor in _actors) { if (actor.Pending is { } token) actor.Instance!.Abort(token); actor.Pending = null; actor.Instance = null; }
        for (int i = _claims.Count - 1; i >= 0; i--) { _claims[i].Dispose(); _claims.RemoveAt(i); }
        _session = _world = Guid.Empty;
    }
    public void FixedUpdate(World world, double fixedDeltaSeconds)
    {
        VerifyIdentity(); if (world != World || !World.IsUpdating) throw new InvalidOperationException("Animator quantum context.");
        try { foreach (var actor in _actors) actor.Pending = actor.Instance!.Prepare(new(_session, _world, World.Tick), fixedDeltaSeconds); }
        catch { foreach (var actor in _actors) if (actor.Pending is { } token) { actor.Instance!.Abort(token); actor.Pending = null; } throw; }
    }
    public void StepCommitted(World world, double fixedDeltaSeconds)
    {
        VerifyIdentity(); if (world != World || World.IsUpdating) throw new InvalidOperationException("Animator commit context.");
        foreach (var actor in _actors) { actor.Instance!.Commit(actor.Pending ?? throw new InvalidOperationException("Animator preparation missing."), new(_session, _world, World.Tick)); actor.Pending = null; }
    }
    private void VerifyIdentity()
    {
        World.VerifyAccess(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (_session == Guid.Empty || _play.SessionId != _session || World.Identity != _world) throw new InvalidOperationException("Animator identity expired.");
    }
    private Actor Read(Guid objectId)
    {
        VerifyIdentity();
        if (World.IsUpdating || _play.State is not (PlayState.Running or PlayState.Paused)) throw new InvalidOperationException("Animator requires active committed Play.");
        Actor? found = null; foreach(var candidate in _actors) if(candidate.Id==objectId){found=candidate;break;}
        var actor = found ?? throw new ArgumentException("Unknown Animator object.");
        if (actor.Pending is not null || actor.Instance!.Frame.Context.Tick != World.Tick) throw new InvalidOperationException("Animator snapshot not committed.");
        return actor;
    }
    public AnimationGraphFrame ReadFrame(Guid objectId) => Read(objectId).Instance!.Frame;
    public bool RequestMontage(Guid objectId,MontageRequest request)=>Control(objectId).Instance!.RequestMontage(request);
    public MontagePlaybackFrame ReadMontageFrame(Guid objectId)=>Read(objectId).Instance!.ReadMontageFrame();
    public MontageSlotFrame ReadMontageSlot(Guid objectId,Guid slot)=>Read(objectId).Instance!.ReadMontageSlot(slot);
    public int CopyCommittedMontageIntervals(Guid objectId,Span<MontageInterval> destination)=>Read(objectId).Instance!.CopyCommittedMontageIntervals(destination);
    public int CopyCommittedMontageReceipts(Guid objectId,Span<MontageReceipt> destination)=>Read(objectId).Instance!.CopyCommittedMontageReceipts(destination);
    public int CopyFrozenPose(Guid objectId,ulong generation,Span<AnimationLocalTransform> destination)=>Read(objectId).Instance!.CopyFrozenPose(generation,destination);
    // Immutable prepared resource proof. No disk/native queries or sampling; source leases outlive this host.
    public IReadOnlyList<AnimatorRuntimeBinding> DescribeBindings()
    {World.VerifyAccess();ObjectDisposedException.ThrowIf(_disposed,this);if(World.IsUpdating)throw new InvalidOperationException("Committed boundary required.");return Array.AsReadOnly(_actors.Select(a=>a.Binding!).ToArray());}
    internal AnimationProgram PreparedProgram(Guid objectId, RuntimeAssetLease assets)
    {
        World.VerifyAccess(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (_play.State != PlayState.Stopped || _assets!.Identity != assets.Identity) throw new InvalidOperationException("Exact stopped graph resource publication required.");
        return _actors.Single(a => a.Id == objectId).Program;
    }
    internal AnimationGraphFrame CopyPreparedRootPlan(Guid objectId, AnimationStepContext context, Span<AnimationPoseInstruction> destination)
    {
        VerifyIdentity();
        if (!World.IsUpdating || context != new AnimationStepContext(_session, _world, World.Tick)) throw new InvalidOperationException("Exact collecting graph quantum required.");
        Actor? actor = null; foreach(var candidate in _actors) if(candidate.Id==objectId){actor=candidate;break;}
        if(actor is null)throw new ArgumentException("Unknown graph root actor.");
        var token = actor.Pending ?? throw new InvalidOperationException("Graph must prepare before character intentions.");
        var frame = actor.Instance!.PreparedFrame(token);
        if (actor.Instance.CopyPreparedPlan(token, destination) != frame.InstructionCount) throw new InvalidOperationException("Prepared graph output mismatch.");
        return frame;
    }
    public AnimationGraphDebugFrame ReadDebug(Guid objectId) => Read(objectId).Instance!.ReadDebug(new(_session, _world, World.Tick));
    internal int CopyPreparedRootIntervals(Guid objectId,AnimationStepContext context,Span<MontageInterval> destination)
    {
        VerifyIdentity();if(!World.IsUpdating||context!=new AnimationStepContext(_session,_world,World.Tick))throw new InvalidOperationException("Exact collecting graph root context required.");
        Actor? actor=null;foreach(var candidate in _actors)if(candidate.Id==objectId){actor=candidate;break;}
        if(actor is null||actor.Pending is not{} token)throw new InvalidOperationException("Same pending Animator required for root intervals.");
        return actor.Instance!.CopyPreparedMontageIntervals(token,destination);
    }
    public int CopyCommittedEvents(Guid objectId, Span<AnimationGraphEvent> destination) => Read(objectId).Instance!.CopyCommittedEvents(new(_session, _world, World.Tick), destination);
    public void VerifyPresentation(World world, RuntimeAssetLease assets)
    {
        VerifyIdentity();
        if (World.IsUpdating || world != World || _play.State is not (PlayState.Running or PlayState.Paused) || _assets!.Identity != assets.Identity)
            throw new InvalidOperationException("Animator presentation requires the exact World and prepared asset publication.");
    }
    public int CopyCommittedPlan(Guid objectId, Span<AnimationPoseInstruction> destination) => Read(objectId).Instance!.CopyCommittedPlan(destination);
    private Actor Control(Guid objectId)
    { try { World.VerifyWriteAccess();return Read(objectId); }catch(Exception error){World.RejectStep(error);throw;} }
    public void SetFloat(Guid objectId, Guid parameter, double value) => Control(objectId).Instance!.SetFloat(parameter, value);
    public void SetBool(Guid objectId, Guid parameter, bool value) => Control(objectId).Instance!.SetBool(parameter, value);
    public void SetInt(Guid objectId, Guid parameter, int value) => Control(objectId).Instance!.SetInt(parameter, value);
    public void SetTrigger(Guid objectId, Guid parameter) => Control(objectId).Instance!.SetTrigger(parameter);
    public void ClearTrigger(Guid objectId, Guid parameter) => Control(objectId).Instance!.ClearTrigger(parameter);
    public void Dispose()
    {
        World.VerifyAccess(); if (_disposed) return;
        if (_play.State != PlayState.Stopped || _claims.Count != 0) throw new InvalidOperationException("Stop before releasing Animator ownership.");
        _play.RemoveCommittedObserver(this); _play.DetachCompositionLifetime(this); _assets?.Dispose(); _assets = null; _disposed = true;
    }
}
