using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Editor.Services;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    private sealed class SceneGpuFixture
    {
        internal readonly Guid Project=Guid.NewGuid(), Model=Guid.NewGuid(),Mesh=Guid.NewGuid(),ImportedSet=Guid.NewGuid(),Set=Guid.NewGuid(),Material=Guid.NewGuid();
        internal readonly string Root;
        internal string GenerationPath="";
        internal SceneGpuFixture(string output) {
            Root=Path.Combine(output,"scene-fixture",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(Root,"assets"));
            File.WriteAllBytes(Path.Combine(Root,"assets/default.ncmaterial"),MaterialCodec.Encode(MaterialDefinition.Default(Material) with{BaseColor=new(.6f,.25f,.1f,1),Roughness=.6f}));
            File.WriteAllBytes(Path.Combine(Root,"assets/default.ncmatset"),MaterialCodec.Encode(new MaterialSetDefinition(1,Set,[Material])));
            WriteModel(1);
        }
        internal void WriteModel(ulong generation,float size=1) {
            var settings=new ImportSettings(1,30,true);string sourceHash=new('C',64);
            var manifest=new ModelAssetManifest(1,Model,true,sourceHash,settings,null,[new(Mesh,ImportedSet)],[]);
            ImportVertex V(float x,float y,float u,float v)=>new(new(x*size,y*size,.5f),Vector3.UnitZ,new(u,v),default,Vector4.Zero);
            var payload=new MeshPayload(false,0,[V(-1,1,0,0),V(1,1,1,0),V(-1,-1,0,1),V(1,-1,1,1)],Enumerable.Repeat(new Vector4(1,0,0,-1),4).ToArray(),[0,1,2,2,1,3],[0,0],[],1);
            byte[] bytes=DerivedAssetCodec.Encode([new(Model,AssetKind.StaticMesh,ModelAssetManifestCodec.Encode(manifest)),new(Mesh,AssetKind.StaticMesh,ModelPayloadCodec.Encode(payload)),new(ImportedSet,AssetKind.MaterialSet,ModelPayloadCodec.Encode(new MaterialSlotsPayload(["source slot"])))]);
            string hash=Convert.ToHexString(SHA256.HashData(bytes)),relative=$"out/assets/{Project:N}/{Model:N}/{generation}-{hash}.nca";
            GenerationPath=Path.Combine(Root,relative);Directory.CreateDirectory(Path.GetDirectoryName(GenerationPath)!);File.WriteAllBytes(GenerationPath,bytes);
            File.WriteAllBytes(Path.Combine(Root,"assets/model.fbx.ncmeta"),AssetRecordCodec.Encode(new(1,Model,AssetKind.StaticMesh,"assets/model.fbx",sourceHash,"ufbx",1,settings,
                [new(Mesh,AssetKind.StaticMesh,"mesh/0","Quad",false),new(ImportedSet,AssetKind.MaterialSet,"materials/0","Imported",false)],[],new(generation,hash,relative))));
        }
        internal (SceneDocument Document,Guid Camera,Guid Ground,Guid Caster) Scene() {
            var d=new SceneDocument("Scene GPU",RenderComponentRegistry.Register(RenderConfiguration.CreateRegistry()),SceneRenderValidation.RequireComposition);
            var ground=d.World.CreateObject("Ground");ground.Set(TransformData.Identity with{Position=new(0,0,-4.5f),Scale=new(4,4,1)});ground.Set(new StaticMeshData(Mesh,Set,true,true,uint.MaxValue));
            var caster=d.World.CreateObject("Off-camera caster");caster.Set(TransformData.Identity with{Position=new(3,0,-.5f)});caster.Set(new StaticMeshData(Mesh,Set,true,true,uint.MaxValue));
            var camera=d.World.CreateObject("Explicit camera");camera.Set(TransformData.Identity);camera.Set(CameraData.Default with{Projection=CameraProjection.Orthographic,OrthographicHeight=4});
            var light=d.World.CreateObject("Primary light");light.Set(TransformData.Identity with{Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/4)});light.Set(DirectionalLightData.Default);
            d.ValidateAuthoring();return(d,camera.PersistentId,ground.PersistentId,caster.PersistentId);
        }
    }
    private static bool PinLocked(string path) { try{using var file=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite|FileShare.Delete);return false;}catch(IOException){return true;} }
    static void TestSceneResources(RendererSession renderer,ref ulong frame,string output)
    {
        var fixture=new SceneGpuFixture(output);var (document,camera,ground,caster)=fixture.Scene();
        using var editor=new EditorSessionOwner("GPU leases",components:RenderComponentRegistry.Register(RenderConfiguration.CreateRegistry()),validateComposition:SceneRenderValidation.RequireComposition);
        editor.Document.RestoreSnapshot(document.CaptureSnapshot());editor.PrepareRenderAssets(fixture.Root,fixture.Project);
        using var cache=new RenderResourceCache(renderer);using var target=renderer.CreateViewTarget(256,256);
        byte[] original=new byte[256*256*4],changed=new byte[original.Length];
        var edit=new SceneRenderSession(renderer,cache,editor.Document.World,editor.RenderAssets!,editor.Document.CaptureSnapshot());
        try {
            Check(edit.Submit(frame++,256,256,camera,target:target),"Production scene submitted no GPU geometry");renderer.CaptureTarget(target,original);renderer.Present();
            Check(edit.Costs.GeometryDraws==1&&edit.Costs.ShadowDraws==2&&edit.View!.Geometry.All(d=>d.ObjectId!=caster),"Camera culling incorrectly removed off-camera shadow caster");
            Check(edit.Pick(frame-1,edit.View!.FrameIdentity,.5f,.5f)==ground,"Exact-frame viewport bounds picking");
            ulong pickFrame=frame-1;Guid pickView=edit.View.FrameIdentity;
            Reject(()=>edit.Pick(pickFrame+1,pickView,.5f,.5f));Reject(()=>edit.Pick(pickFrame,Guid.NewGuid(),.5f,.5f));
            ulong resources=renderer.ResourceStats.Creates,plans=renderer.PipelineStats.Creates;
            var cameraData=editor.Document.World.FindObject(camera).Get<CameraData>();
            editor.Document.World.FindObject(camera).Set(cameraData with{ViewportX=.5f,ViewportWidth=.5f});
            Reject(()=>edit.Pick(pickFrame,pickView,.5f,.5f)); // Old pixels/Undo-like revision cannot select a current World.
            edit.Submit(frame++,256,256,camera,target:target);renderer.CaptureTarget(target,changed);renderer.Present();
            Check(changed[(128*256+64)*4]==0&&changed[(64*256+192)*4]>0,"Normalized camera viewport/offscreen letterbox not deterministic");
            editor.Document.World.FindObject(camera).Set(cameraData);edit.Submit(frame++,256,256,camera,target:target);renderer.Present();
            for(int i=0;i<8;i++) {
                var play=editor.StartPlay();
                using(var session=new SceneRenderSession(renderer,cache,play.Document.World,editor.PlayRenderAssets!,play.Document.CaptureSnapshot())) {
                    Check(renderer.ResourceStats.Creates==resources,"Edit/Play failed to share immutable GPU resources");
                    session.Submit(frame++,256,256,camera,target:target);renderer.CaptureTarget(target,changed);renderer.Present();Check(changed.SequenceEqual(original),"Play/Editor source parity");
                    var objectTransform=play.Document.World.FindObject(ground).Get<TransformData>();
                    play.Document.World.FindObject(ground).Set(objectTransform with{Position=new(1,0,-4.5f),Scale=new(2,1,1)});
                    session.Submit(frame++,256,256,camera,target:target);renderer.CaptureTarget(target,changed);renderer.Present();Check(!changed.SequenceEqual(original),"Committed nonuniform object transform did not affect GPU scene");
                }
                editor.StopPlay();edit.Submit(frame++,256,256,camera,target:target);renderer.CaptureTarget(target,changed);renderer.Present();Check(changed.SequenceEqual(original),"Play/Stop mutated Edit");
                Check(renderer.PipelineStats.Pipelines==1,"Play pipeline leaked on Stop");
            }
            Check(renderer.ResourceStats.Creates==resources,"Play/Stop resource re-upload");
            plans=renderer.PipelineStats.Creates;
            edit.Submit(frame++,256,256,camera,exposure:2,target:target);renderer.CaptureTarget(target,changed);renderer.Present();
            Check(!changed.SequenceEqual(original)&&renderer.PipelineStats.Creates==plans,"Exposure update recreated GPU allocation");
            edit.Submit(frame++,256,256,camera,target:target);renderer.Present();
            var playPinned=editor.StartPlay();
            using(var play=new SceneRenderSession(renderer,cache,playPinned.Document.World,editor.PlayRenderAssets!,playPinned.Document.CaptureSnapshot())) {
                string oldPath=fixture.GenerationPath;fixture.WriteModel(2,.5f);editor.RefreshRenderAssets();
                using(var candidate=new SceneRenderSession(renderer,cache,editor.Document.World,editor.RenderAssets!,editor.Document.CaptureSnapshot())) {
                    candidate.Submit(frame++,256,256,camera,target:target);renderer.CaptureTarget(target,changed);renderer.Present();Check(!changed.SequenceEqual(original),"Reimport did not install new GPU generation");
                    play.Submit(frame++,256,256,camera,target:target);renderer.CaptureTarget(target,changed);renderer.Present();Check(changed.SequenceEqual(original)&&PinLocked(oldPath),"Play failed to pin old CPU/GPU generation");
                }
                editor.StopPlay();Check(PinLocked(oldPath),"GPU/CPU independent lease lifetime lost");edit.Dispose();Check(PinLocked(oldPath),"Active render lease prematurely released source");
            }
            Check(renderer.PipelineStats.Pipelines==0,"Scene GPU groups retained after final lease");
            var empty=new SceneDocument("Pure flat",RenderComponentRegistry.CreateRegistry());
            using var emptyAssets=SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,empty.CaptureSnapshot(),true);
            ulong groups=renderer.PipelineStats.Creates;
            using(var flat=new SceneRenderSession(renderer,cache,empty.World,emptyAssets,empty.CaptureSnapshot()))
                Check(!flat.Submit(frame,256,256,Guid.Empty)&&renderer.PipelineStats.Creates==groups,"Empty/pure-2D initialized 3D pipeline");
            Check(renderer.PipelineStats.ResidentBytes==0,"Scene framebuffer memory leaked");
            File.WriteAllBytes(Path.Combine(output,"scene-production.png"),Png(256,256,original));
            File.WriteAllText(Path.Combine(output,"scene-resources.json"),JsonSerializer.Serialize(new{playCycles=8,reimport=true,explicitCamera=camera,sceneValidation=renderer.Stats.ValidationErrors,warning=renderer.Stats.ValidationWarnings}));
            Console.WriteLine("PASS production NCA->C# scene->GPU/camera culling/nonuniform transform/Edit-Play sharing/8 Stop cycles/reimport pin/empty 2D");
        } finally { edit.Dispose(); }
    }
    static void TestSceneProfiles(RendererSession renderer,ref ulong frame,string output)
    {
        var fixture=new SceneGpuFixture(output);using var cache=new RenderResourceCache(renderer);using var target=renderer.CreateViewTarget(256,256);
        var rows=new List<object>();
        foreach(int count in new[]{0,1,256,4096}) {
            var document=new SceneDocument("Profile "+count,RenderComponentRegistry.CreateRegistry(),SceneRenderValidation.RequireComposition);
            var cam=new SceneCameraView(Guid.Empty,Matrix4x4.CreateOrthographic(4,4,.1f,1000),Vector3.Zero,CameraData.Default with{Projection=CameraProjection.Orthographic,OrthographicHeight=4});
            int side=Math.Max(1,(int)Math.Ceiling(Math.Sqrt(count)));
            for(int i=0;i<count;i++) {
                var obj=document.World.CreateObject("Instance "+i);obj.Set(TransformData.Identity with{Position=new(-1.9f+i%side*3.8f/side,-1.9f+i/side*3.8f/side,-4.5f),Scale=new(.018f,.018f,.018f)});
                obj.Set(new StaticMeshData(fixture.Mesh,fixture.Set,true,true,uint.MaxValue));
            }
            using var prepared=SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,document.CaptureSnapshot(),true);
            using var session=new SceneRenderSession(renderer,cache,document.World,prepared,document.CaptureSnapshot());
            ulong plans=renderer.PipelineStats.Creates;
            for(int i=0;i<4;i++){if(session.Submit(frame++,256,256,Guid.Empty,cam,target:target))renderer.Present();}
            ulong creates=renderer.ResourceStats.Creates,upload=renderer.ResourceStats.UploadedBytes,calls=renderer.SubmitCalls;
            var before=renderer.PipelineStats;long bytes=GC.GetAllocatedBytesForCurrentThread(),started=System.Diagnostics.Stopwatch.GetTimestamp();
            for(int i=0;i<8;i++){if(session.Submit(frame++,256,256,Guid.Empty,cam,target:target))renderer.Present();}
            double elapsed=System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
            renderer.WaitIdle();var gpu=renderer.Stats;var after=renderer.PipelineStats;
            Check(bytes<=1024,"Cached production extraction/encoding allocated: "+bytes);
            Check(creates==renderer.ResourceStats.Creates&&upload==renderer.ResourceStats.UploadedBytes,"Profile frame re-uploaded immutable geometry");
            Check(count==0?after.Creates==plans:renderer.SubmitCalls-calls==8&&session.Costs.GeometryDraws==count,"Profile batch/camera count");
            rows.Add(new{objects=count,frames=8,allocatedBytes=bytes,totalMilliseconds=elapsed,extractionMilliseconds=session.Costs.ExtractionMilliseconds,encodeMilliseconds=session.Costs.EncodeMilliseconds,
                abiCalls=renderer.SubmitCalls-calls,abiBytes=after.CopiedBytes-before.CopiedBytes,constantUploadBytes=after.ConstantUploadBytes-before.ConstantUploadBytes,
                gpuSampleValid=count==0?0:gpu.GpuSampleValid,gpuMilliseconds=count==0?(double?)null:gpu.GpuMilliseconds,nativeSubmitMilliseconds=count==0?(double?)null:gpu.SubmitMilliseconds,validationErrors=gpu.ValidationErrors,validationWarnings=gpu.ValidationWarnings});
        }
        File.WriteAllText(Path.Combine(output,"scene-profiles.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS production scene 0/1/256/4096 profiles: bounded single ABI batches, cached managed allocation and upload counters");
    }
}
