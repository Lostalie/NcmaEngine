using System.Numerics;
using Ncma.Animation;
using Ncma.Animation.Native;
using Ncma.Assets;
using Ncma.Assets.Runtime;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;
namespace Ncma.Editor.Services;
using Vector3 = System.Numerics.Vector3;

// Disposable derived authoring scene. No application Play/WorldRunner/Behaviour/solver/IO in Advance/Draw.
public sealed class EditorAnimationGraphPreview(RendererSession renderer, Func<PoseKernel> kernel) : IDisposable
{
    private RenderResourceCache? _cache; private SceneRenderSession? _scene; private PreparedSceneAssetLease? _assets;
    private SceneDocument? _document; private GpuViewTarget? _target; private Guid _camera,_object; private bool _disposed;
    public bool Prepared => _scene is not null;
    public Guid WorldId => _document?.World.Identity??Guid.Empty;
    public ulong Tick => _document?.World.Tick??0;
    public AnimationGraphFrame? GraphFrame => _scene?.Animation?.PreviewFrame(_object);
    public AnimationGraphDebugFrame? GraphDebug => _scene?.Animation?.PreviewDebug(_object);
    public void Prepare(RuntimeAssetSnapshot snapshot,Guid graphId)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);Close();
        _assets=new(snapshot.AcquireLease());
        try {
            var graph=(RuntimeAnimationGraphAsset)_assets.Assets.Require(graphId,AssetKind.AnimationGraph);var d=graph.CopyDefinition();
            var rig=(RuntimeDataAsset)_assets.Assets.Require(d.SkeletonId,AssetKind.Skeleton);var model=(RuntimeDataAsset)_assets.Assets.Require(rig.ModelId,AssetKind.Character);
            var manifest=ModelAssetManifestCodec.Decode(model.CopyData());var binding=manifest.Meshes.First(m=>_assets.Assets.RequireMesh(m.Mesh,AssetKind.SkinnedMesh).SkeletonId==d.SkeletonId);
            var mesh=_assets.Assets.RequireMesh(binding.Mesh,AssetKind.SkinnedMesh);float radius=Math.Max(.5f,(mesh.BoundsMax-mesh.BoundsMin).Length());var center=(mesh.BoundsMax+mesh.BoundsMin)*.5f;
            _document=new("Animation graph independent preview",RenderComponentRegistry.CreateRegistry(),SceneRenderValidation.RequireComposition);
            global::Ncma.Runtime.Entity character=_document.World.CreateEntity("Preview character");_object=_document.World.GetUuid(character);_document.World.Set(character, TransformData.Identity);_document.World.Set(character, new SkinnedMeshData(model.Id,mesh.Id,d.SkeletonId,binding.Materials,true,true,uint.MaxValue));_document.World.Set(character, new AnimatorData(graphId,d.SkeletonId));
            global::Ncma.Runtime.Entity camera=_document.World.CreateEntity("Preview camera");_camera=_document.World.GetUuid(camera);_document.World.Set(camera, TransformData.Identity with{Position=center+new Vector3(0,0,radius*3)});_document.World.Set(camera, CameraData.Default with{Projection=CameraProjection.Orthographic,OrthographicHeight=radius*1.4f});
            global::Ncma.Runtime.Entity light=_document.World.CreateEntity("Preview light");_document.World.Set(light, TransformData.Identity with{Rotation=Quaternion.CreateFromYawPitchRoll(.35f,-.35f,0)});_document.World.Set(light, DirectionalLightData.Default);
            var scene=_document.CaptureSnapshot();_ = SceneRenderValidation.Inspect(scene,_assets.Metadata,true);
            _cache=new(renderer);_scene=new(renderer,_cache,_document.World);_scene.Prepare(_assets,scene,poseKernel:kernel());
        }catch{Close();throw;}
    }
    public void Advance(double delta) { ObjectDisposedException.ThrowIf(_disposed,this);_scene?.Animation?.AdvancePreview(delta); }
    public void Control(Guid parameter,AnimationParameterKind kind,double value) { if(_scene?.Animation is null)throw new InvalidOperationException("Preview not prepared.");_scene.Animation.ControlPreview(_object,parameter,kind,value); }
    public GuiImageToken? Draw(ulong frame,uint width,uint height)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);if(_scene is null||GraphDebug?.SnapshotValid!=true)return null;if(width is <1 or >4096||height is <1 or >4096)throw new ArgumentException("Preview target budget.");
        if(_target is null||_target.Width!=width||_target.Height!=height){var candidate=renderer.CreateViewTarget(width,height);try{_target?.Dispose();}catch{candidate.Dispose();throw;}_target=candidate;}
        return _scene.Submit(frame,width,height,_camera,target:_target)?_target.ImageToken:null;
    }
    public byte[] Capture() { if(_target is null)throw new InvalidOperationException("Preview not drawn.");var bytes=new byte[_target.Width*_target.Height*4];renderer.CaptureTarget(_target,bytes);return bytes; }
    public void Close() { _scene?.Dispose();_scene=null;_target?.Dispose();_target=null;_cache?.Dispose();_cache=null;_assets?.Dispose();_assets=null;_document=null; }
    public void Dispose(){if(_disposed)return;Close();_disposed=true;}
}
