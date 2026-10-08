using System.Numerics;
using System.Diagnostics;
using Ncma.Animation;
using Ncma.Animation.Native;
using Ncma.Assets;
using Ncma.Gameplay;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;
namespace Ncma.Rendering.Scene;
using Vector3 = System.Numerics.Vector3;
using GameObject = Ncma.Runtime.GameObject;

public readonly record struct AnimationSceneCosts(int Characters,int Bones,int PaletteEntries,double SampleMilliseconds,double PaletteMilliseconds,ulong PoseGeneration);
// Application-owned derived state: immutable NCA leases, per-object C# clocks and bounded pose buffers.
// No FBX/file IO/World serialization/CPU skin vertices inside sampling or fixed-step callbacks.
public sealed class SceneAnimationSession : IDisposable,ICommittedStepObserver
{
    internal sealed class Character
    {
        internal readonly Guid Id;internal readonly GameObject Object;internal readonly SkinnedMeshData Source;
        internal readonly PoseRig Rig;internal readonly MeshBindingPalette Bindings;internal readonly SkinUploadData Upload;
        internal readonly SkinInfluenceBounds InfluenceBounds;
        internal readonly int BoneOffset;internal readonly Vector3 BindRoot;
        internal ClipClock? Clock;internal bool Active;internal Vector3 RootDisplacement;
        internal AnimationGraphPose? GraphPose; internal AnimationGraphInstance? PreviewGraph;
        internal AnimatorData? AnimatorSource;
        internal Character(GameObject obj,SkinnedMeshData source,PoseRig rig,SkeletonPayload skeleton,MeshPayload mesh,int offset)
        {Id=obj.PersistentId;Object=obj;Source=source;Rig=rig;Bindings=new(mesh,rig.BoneCount);Upload=SkinUploadData.Prepare(mesh);InfluenceBounds=new(mesh);BoneOffset=offset;BindRoot=skeleton.Bones[0].BindLocal.Position;}
    }
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private readonly World _world;private readonly PoseKernel _kernel;private readonly List<IDisposable> _leases=new(192);
    private readonly Dictionary<Guid,(PoseRig Rig,SkeletonPayload Skeleton)> _rigs=new(64);
    private readonly Dictionary<Guid,PoseClip> _clips=new(128);
    private readonly List<Character> _characters=new(32);
    private readonly List<SceneRenderDiagnostic> _diagnostics=new(32);
    public IReadOnlyList<SceneRenderDiagnostic> Diagnostics { get; }
    private readonly PoseSample[] _samples=new PoseSample[32];private readonly PoseTrs[] _locals;private readonly Matrix4x4[] _models;
    private readonly Matrix4x4[] _bindingMatrices;private readonly GpuSkinPalette[] _palette;
    private Ncma.Assets.Runtime.RuntimeAssetLease? _assets;private PlaySession? _play;private bool _disposed;
    private double _previewSeconds;
    private readonly IRootMotionPresentation? _rootMotion;
    private readonly SceneAnimatorRuntime? _animators;
    private double _previewDebt;
    public Guid WorldId {get;}
    public AnimationSceneCosts Costs {get;private set;}
    internal IReadOnlyList<Character> Characters=>_characters;
    public SceneAnimationSession(World world,PreparedSceneAssetLease prepared,SceneDocumentSnapshot startup,PoseKernel kernel,IRootMotionPresentation? rootMotion=null,SceneAnimatorRuntime? animators=null)
    {
        _world=world;WorldId=world.Identity;_kernel=kernel;Diagnostics=_diagnostics.AsReadOnly();_rootMotion=rootMotion;_animators=animators;
        try{
            animators?.VerifyPresentation(world,prepared.Assets);
            _assets=prepared.Assets.AcquireLease();var registry=RenderComponentRegistry.CreateRegistry();int bones=0,palettes=0;
            foreach(var obj in startup.Objects){var component=obj.Components.SingleOrDefault(c=>c.TypeId==SkinnedMeshData.TypeId);if(component is null)continue;
                var source=registry.Decode<SkinnedMeshData>(component);
                if(!_assets.TryResolve(source.MeshId,AssetKind.SkinnedMesh,out _)||!_assets.TryResolve(source.SkeletonId,AssetKind.Skeleton,out _)||!_assets.TryResolve(source.CharacterId,AssetKind.Character,out _))continue;
                var playback=obj.Components.SingleOrDefault(c=>c.TypeId==ClipPlaybackData.TypeId);
                if(playback is not null&&!_assets.TryResolve(registry.Decode<ClipPlaybackData>(playback).ClipId,AssetKind.Clip,out _)) {
                    _diagnostics.Add(new(obj.Id,"animation_clip_missing",registry.Decode<ClipPlaybackData>(playback).ClipId));continue;
                }
                if(_characters.Count>=32)throw new ArgumentException("Scene animation supports at most 32 prepared characters per World.");
                var mesh=_assets.RequireMesh(source.MeshId,AssetKind.SkinnedMesh);var skeletonAsset=(Ncma.Assets.Runtime.RuntimeDataAsset)_assets.Require(source.SkeletonId,AssetKind.Skeleton);
                var characterAsset=(Ncma.Assets.Runtime.RuntimeDataAsset)_assets.Require(source.CharacterId,AssetKind.Character);
                if(mesh.ModelId!=source.CharacterId||mesh.SkeletonId!=source.SkeletonId||mesh.Generation!=skeletonAsset.Generation||skeletonAsset.ModelId!=source.CharacterId||characterAsset.Generation!=mesh.Generation)throw new ArgumentException("Animation exact model/skeleton/mesh generation required.");
                if(!_rigs.TryGetValue(source.SkeletonId,out var rig)){
                    var skeleton=ModelPayloadCodec.DecodeSkeleton(skeletonAsset.CopyData());var nativeRig=kernel.CreateRig(skeleton);_leases.Add(nativeRig);rig=(nativeRig,skeleton);_rigs.Add(source.SkeletonId,rig);
                    var manifest=ModelAssetManifestCodec.Decode(characterAsset.CopyData());
                    foreach(Guid id in manifest.Clips){if(_clips.Count>=128)throw new ArgumentException("Prepared scene clip budget is 128.");var data=(Ncma.Assets.Runtime.RuntimeDataAsset)_assets.Require(id,AssetKind.Clip);
                        if(data.ModelId!=source.CharacterId||data.SkeletonId!=source.SkeletonId||data.Generation!=mesh.Generation)throw new ArgumentException("Clip generation/model mismatch.");
                        var clip=kernel.CreateClip(nativeRig,ModelPayloadCodec.DecodeClip(data.CopyData()));_leases.Add(clip);_clips.Add(id,clip);}
                }
                var live=world.FindObject(obj.Id);var instance=new Character(live,source,rig.Rig,rig.Skeleton,mesh.CopyPayload(),bones);
                if (live.Has<AnimatorData>()) {
                    var settings = live.Get<AnimatorData>(); var graph = (Ncma.Assets.Runtime.RuntimeAnimationGraphAsset)_assets.Require(settings.GraphId,AssetKind.AnimationGraph);
                    if (settings.SkeletonId != graph.CopyDefinition().SkeletonId || settings.SkeletonId != source.SkeletonId) throw new ArgumentException("Graph presentation skeleton mismatch.");
                    var program = graph.PrepareProgram(_assets);
                    instance.AnimatorSource = settings; instance.GraphPose = new(program,rig.Rig,_clips,kernel); instance.PreviewGraph = new(program,new(Guid.NewGuid(),WorldId,0));
                }
                if(live.Has<ClipPlaybackData>() && (rootMotion is null || !live.Has<RootMotionData>())){var settings=live.Get<ClipPlaybackData>();instance.Clock=new(world,settings,RequireClip(instance,settings).Duration);}
                _characters.Add(instance);bones=checked(bones+rig.Rig.BoneCount);palettes=checked(palettes+instance.Bindings.BindingCount);
            }
            if(bones>32768||palettes>32768)throw new ArgumentException("Scene pose/palette capacity.");
            _locals=new PoseTrs[bones];_models=new Matrix4x4[bones];_bindingMatrices=new Matrix4x4[palettes];_palette=new GpuSkinPalette[palettes];
        }catch{Dispose();throw;}
    }
    private PoseClip RequireClip(Character c,ClipPlaybackData settings)
    {if(!_clips.TryGetValue(settings.ClipId,out var clip)||clip.Rig!=c.Rig)throw new ArgumentException("Clip is not pinned to this prepared character generation; Stop/Refresh required.");return clip;}
    private bool Live(Character c)
    {
        try{
            if(!c.Object.Has<SkinnedMeshData>())return false;var current=c.Object.Get<SkinnedMeshData>();
            if(c.Object.Has<AnimatorData>() != c.AnimatorSource.HasValue || c.AnimatorSource is { } source && c.Object.Get<AnimatorData>() != source) throw new InvalidOperationException("Animator references changed; explicit preparation required.");
            if(current.CharacterId!=c.Source.CharacterId||current.MeshId!=c.Source.MeshId||current.SkeletonId!=c.Source.SkeletonId||current.MaterialSetId!=c.Source.MaterialSetId)throw new InvalidOperationException("Animation asset references changed; explicit off-frame preparation required.");
            return true;
        }catch(InvalidOperationException){throw;}catch(ArgumentException){return false;}
    }
    public void Attach(PlaySession play)
    {
        Verify();ArgumentNullException.ThrowIfNull(play);if(_play is not null||play.Document.World!=_world)throw new ArgumentException("Attach animation at its prepared committed Play boundary.");
        if (_characters.Any(c=>c.GraphPose is not null) && _animators is null) throw new ArgumentException("Graph presentation requires the shared committed Animator runtime.");
        play.AddCommittedObserver(this);_play=play;
        if(_characters.Any(c=>c.Object.Has<RootMotionData>()) && _rootMotion is null)throw new ArgumentException("Root motion Play presentation requires its sole committed motion source.");
    }
    public void StepCommitted(World world,double fixedDeltaSeconds)
    {
        Verify();if(world!=_world)throw new ArgumentException("Animation World owner mismatch.");
        foreach(var c in _characters){if(!Live(c)||!c.Object.Has<ClipPlaybackData>()||c.Object.Has<RootMotionData>())continue;var settings=c.Object.Get<ClipPlaybackData>();var clip=RequireClip(c,settings);
            c.Clock??=new(world,settings,clip.Duration);c.Clock.ObserveCommitted(world,settings,clip.Duration,fixedDeltaSeconds);}
    }
    public void ResynchronizeAfterReload(PlaySession play)
    {
        Verify();if(_play!=play||play.Document.World!=_world)throw new ArgumentException("Reload must retain the original animation World and leases.");
        // Reload changes Play identity but not its successful tick count. Retain elapsed clocks/resources;
        // reconcile only explicitly changed persistent settings at this trusted committed boundary.
        foreach(var c in _characters)if(Live(c)&&c.Object.Has<ClipPlaybackData>()) {
            var settings=c.Object.Get<ClipPlaybackData>();var clip=RequireClip(c,settings);
            c.Clock??=new(_world,settings,clip.Duration);c.Clock.ObserveCommitted(_world,settings,clip.Duration,play.FixedDeltaSeconds);
        }
    }
    public void AdvancePreview(double deltaSeconds)
    {
        Verify();if(_play is not null||!double.IsFinite(deltaSeconds)||deltaSeconds is <0 or >.25)throw new ArgumentException("Independent Edit preview has a bounded clock, never Play tick.");_previewSeconds+=deltaSeconds;
        _previewDebt += deltaSeconds;
        while (_previewDebt + 1e-12 >= 1.0/60) {
            foreach (var c in _characters) if(c.PreviewGraph is { } graph) {
                var context=graph.Frame.Context; var token=graph.Prepare(context,1.0/60); graph.Commit(token,context with {Tick=context.Tick+1});
            }
            _previewDebt=Math.Max(0,_previewDebt-1.0/60);
        }
    }
    public void SetPreviewTime(double seconds)
    {Verify();if(_play is not null||!double.IsFinite(seconds)||seconds is <0 or >600)throw new ArgumentException("Edit preview time range.");if(_characters.Any(c=>c.GraphPose is not null))throw new NotSupportedException("Graph preview seeking is not implemented; sequential preview only.");_previewSeconds=seconds;}
    public AnimationGraphFrame PreviewFrame(Guid objectId) { Verify();if(_play is not null)throw new InvalidOperationException("Not an independent preview.");return _characters.Single(c=>c.Id==objectId).PreviewGraph?.Frame??throw new ArgumentException("Preview graph missing."); }
    public void ControlPreview(Guid objectId,Guid parameter,AnimationParameterKind kind,double value)
    {
        Verify();if(_play is not null||!double.IsFinite(value))throw new ArgumentException("Independent preview control only.");var graph=_characters.Single(c=>c.Id==objectId).PreviewGraph??throw new ArgumentException("Preview graph missing.");
        switch(kind){case AnimationParameterKind.Float:graph.SetFloat(parameter,value);break;case AnimationParameterKind.Int:if(value!=Math.Truncate(value)||value<int.MinValue||value>int.MaxValue)throw new ArgumentException("Int preview value.");graph.SetInt(parameter,(int)value);break;case AnimationParameterKind.Bool:if(value is not (0 or 1))throw new ArgumentException("Bool preview value.");graph.SetBool(parameter,value==1);break;case AnimationParameterKind.Trigger:if(value!=1)throw new ArgumentException("Trigger preview value.");graph.SetTrigger(parameter);break;default:throw new ArgumentException("Preview parameter kind.");}
    }
    public void Evaluate(float alpha,bool paused)
    {
        Verify();if(!float.IsFinite(alpha)||alpha is <0 or >1)throw new ArgumentException("Animation alpha range.");long start=Stopwatch.GetTimestamp();int active=0;
        for(int i=0;i<_characters.Count;i++){
            var c=_characters[i];c.Active=Live(c);if(c.Active)active++;PoseClip? clip=null;ClipSampleTimes times=new(0,0,1);
            if(c.Active&&c.Object.Has<ClipPlaybackData>()){
                var settings=_play is not null && c.Object.Has<RootMotionData>() ? _rootMotion!.Playback(c.Id) : c.Object.Get<ClipPlaybackData>();clip=RequireClip(c,settings);
                if(_play is not null && c.Object.Has<RootMotionData>())times=_rootMotion!.Sample(c.Id,alpha,paused);
                else if(_play is not null){if(c.Clock is null||c.Clock.CommittedTick!=_world.Tick)throw new InvalidOperationException("Animation missed its committed-step observer.");times=c.Clock.Sample(alpha,paused);}
                else {double time=settings.StartTime+(settings.Playing?_previewSeconds*settings.Speed:0);time=settings.Loop?time%clip.Duration:Math.Min(time,clip.Duration);times=new(time,time,1);}
            }
            _samples[i]=new(c.Rig,clip,times);
        }
        if(active==0){Costs=new(0,0,0,0,0,Costs.PoseGeneration);return;}
        _kernel.Sample(_samples.AsSpan(0,_characters.Count),_locals,_models);
        foreach(var c in _characters) if(c.Active && c.GraphPose is { } pose) {
            AnimationGraphFrame frame; int count;
            if(_play is null){frame=c.PreviewGraph!.Frame;count=c.PreviewGraph.CopyCommittedPlan(pose.Plan);}
            else {frame=_animators!.ReadFrame(c.Id);count=_animators.CopyCommittedPlan(c.Id,pose.Plan);}
            pose.Evaluate(count,frame.Output,paused || _play is null ? 1 : alpha,_locals.AsSpan(c.BoneOffset,c.Rig.BoneCount),_models.AsSpan(c.BoneOffset,c.Rig.BoneCount));
        }
        double sampled=Stopwatch.GetElapsedTime(start).TotalMilliseconds;start=Stopwatch.GetTimestamp();int offset=0;
        foreach(var c in _characters)if(c.Active){
            c.RootDisplacement=_models[c.BoneOffset].Translation-c.BindRoot;
            if(_play is not null && c.Object.Has<RootMotionData>())_rootMotion!.RemoveRoot(c.Id,_models.AsSpan(c.BoneOffset,c.Rig.BoneCount));
            c.Bindings.Compose(_models.AsSpan(c.BoneOffset,c.Rig.BoneCount),_bindingMatrices.AsSpan(offset,c.Bindings.BindingCount));
            for(int i=0;i<c.Bindings.BindingCount;i++)_palette[offset+i]=GpuSkinPalette.Create(_bindingMatrices[offset+i]);offset+=c.Bindings.BindingCount;
            // Authored visual displacement preserved; no GameObject position/physics authority is changed.
        }
        Costs=new(active,_models.Length,offset,sampled,Stopwatch.GetElapsedTime(start).TotalMilliseconds,checked(Costs.PoseGeneration+1));
    }
    internal ReadOnlySpan<GpuSkinPalette> Palette=>_palette.AsSpan(0,Costs.PaletteEntries);
    internal bool TryBounds(Guid objectId,ulong pose,Matrix4x4 model,out MeshBounds bounds)
    {
        Verify();if(Costs.PoseGeneration!=pose)throw new InvalidOperationException("stale_pick_pose");int offset=0;
        foreach(var c in _characters)if(c.Active){if(c.Id==objectId){bounds=c.InfluenceBounds.Evaluate(_bindingMatrices.AsSpan(offset,c.Bindings.BindingCount),model);return true;}offset+=c.Bindings.BindingCount;}
        bounds=default;return false;
    }
    public Vector3 RootDisplacement(Guid objectId)
    {Verify();foreach(var c in _characters)if(c.Id==objectId)return c.RootDisplacement;throw new ArgumentException("Unknown prepared character.");}
    private void Verify(){ObjectDisposedException.ThrowIf(_disposed,this);if(_owner!=Environment.CurrentManagedThreadId||_world.Identity!=WorldId)throw new InvalidOperationException("Animation session owner/World identity invalidated; prepare a new session.");}
    public void Dispose()
    {
        if(_disposed)return;if(_owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Animation release requires owner thread.");
        _play?.RemoveCommittedObserver(this);_play=null;
        // Clip dependencies first, then rigs. A failed release retains the rest and the asset pins.
        for(int i=_leases.Count-1;i>=0;i--)if(_leases[i] is PoseClip){_leases[i].Dispose();_leases.RemoveAt(i);}
        for(int i=_leases.Count-1;i>=0;i--){_leases[i].Dispose();_leases.RemoveAt(i);}
        _assets?.Dispose();_assets=null;_disposed=true;
    }
}
