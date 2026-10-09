using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Animation;
using Ncma.Animation.Native;
using Ncma.Assets;
using Ncma.Assets.Runtime;
using Ncma.Characters;
using Ncma.Gameplay;
using Ncma.Physics;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    private static void TestLayerScenes(RendererSession renderer,ref ulong frame,string native,string output)
    {
        var f=new SkinFixture(output);var m=f.Manifest;var source=f.Source;
        using var raw=RuntimeAssetLoader.Prepare(f.Root,f.Project,[new(new(m.Skeleton!.Value),AssetKind.Skeleton)]);using var pinned=raw.AcquireLease();
        var rigAsset=(RuntimeDataAsset)pinned.Require(m.Skeleton.Value,AssetKind.Skeleton);var layout=RuntimeAnimationGraphAsset.DescribeSkeleton(rigAsset);
        using var physics=new PhysicsService(Path.Combine(native,"m2/plugins"),characterSupport:true);string path=Path.Combine(native,"NcmaAnimationKernel.dll");using var kernel=new PoseKernel(path,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),blendSupport:true,layerSupport:true);
        double maxError=0;var evidence=new List<object>();AnimationGraphDefinition? lastGraph=null;SceneDocument? lastDocument=null;
        foreach(bool additive in new[]{false,true})foreach(bool root in new[]{false,true}){
            var basis=AnimationGraphNode.Create(Guid.NewGuid(),"Base right",AnimationNodeKind.Clip) with{ClipId=m.Clips[0],Speed=1,Loop=true};var upper=basis with{Id=Guid.NewGuid(),Name="Upper left",ClipId=m.Clips[1]};
            var cache=AnimationGraphNode.Create(Guid.NewGuid(),"Cache base",AnimationNodeKind.CachePose);var blend=AnimationGraphNode.Create(Guid.NewGuid(),"Mixed layer",AnimationNodeKind.Blend) with{Weight=.5};var end=AnimationGraphNode.Create(Guid.NewGuid(),"Output",AnimationNodeKind.Output);
            var mask=new AnimationBoneMask(Guid.NewGuid(),m.Skeleton.Value,rigAsset.ContentHash,[new(layout.Bones[1].Path,1)]);
            var layer=AnimationGraphNode.Create(Guid.NewGuid(),"Upper",additive?AnimationNodeKind.LayerAdditive:AnimationNodeKind.LayerOverride) with{Weight=.5,Layer=new(mask,additive?basis.ClipId:Guid.Empty,additive?.25:0)};
            var graph=new AnimationGraphDefinition(AnimationGraphCodec.CurrentVersion,Guid.NewGuid(),"Actual cached layer",m.Skeleton.Value,Guid.Empty,[],[basis,upper,cache,layer,blend,end],
                [new(Guid.NewGuid(),basis.Id,"pose",cache.Id,"pose"),new(Guid.NewGuid(),cache.Id,"pose",layer.Id,"a"),new(Guid.NewGuid(),upper.Id,"pose",layer.Id,"b"),new(Guid.NewGuid(),cache.Id,"pose",blend.Id,"a"),new(Guid.NewGuid(),layer.Id,"pose",blend.Id,"b"),new(Guid.NewGuid(),blend.Id,"pose",end.Id,"pose")],[],[]){Events=[new(Guid.NewGuid(),m.Clips[0],.125,"Base foot"),new(Guid.NewGuid(),m.Clips[1],.125,"Ignored upper foot")]};
            string relative="assets/layer-"+additive+"-"+root+".ncmaanim";File.WriteAllBytes(Path.Combine(f.Root,relative),AnimationGraphCodec.Encode(graph));
            var d=new SceneDocument("Layer",CharacterComponents.Register(RenderComponentRegistry.CreateRegistry()),CharacterComponents.RequireComposition);var actor=d.World.CreateObject("Actor");actor.Set(TransformData.Identity);actor.Set(new SkinnedMeshData(f.Model,m.Meshes[0].Mesh,m.Skeleton.Value,m.Meshes[0].Materials,true,true,uint.MaxValue));actor.Set(new AnimatorData(graph.AssetId,graph.SkeletonId));
            if(root){actor.Set(new RootMotionData(0));actor.Set(CharacterData.Default with{Controlled=false});var floor=d.World.CreateObject("Ground");floor.Set(TransformData.Identity with{Position=new(0,-.2f,0)});floor.Set(new BoxColliderData(100,.2f,100,1000,1,false));}
            var camera=d.World.CreateObject("Camera");camera.Set(TransformData.Identity with{Position=new(0,0,5)});camera.Set(CameraData.Default with{Projection=CameraProjection.Orthographic,OrthographicHeight=40});var light=d.World.CreateObject("Light");light.Set(TransformData.Identity with{Rotation=Quaternion.CreateFromYawPitchRoll(.3f,-.5f,0)});light.Set(DirectionalLightData.Default);
            Guid id=actor.PersistentId;using var assets=SceneAssetPreparation.Prepare(f.Root,f.Project,d.CaptureSnapshot(),true);var program=((RuntimeAnimationGraphAsset)assets.Assets.Require(graph.AssetId,AssetKind.AnimationGraph)).PrepareProgram(assets.Assets);var numeric=GraphPoseSnapshotPreparation.Prepare(program,assets.Assets);
            using var play=new PlaySession(d,FrameTimePolicy.Strict,fixedDeltaSeconds:.125,advanceMode:PlayAdvanceMode.FixedSteps);using var runtime=ScenePlayRuntime.Compose(play,physics,assets);play.Start(_=>throw new Exception("No behaviours"));play.Pause();
            try{using var resource=new RenderResourceCache(renderer);using var target=renderer.CreateViewTarget(256,256);using var scene=new SceneRenderSession(renderer,resource,d.World,assets,d.CaptureSnapshot(),poseKernel:kernel,play:play,animators:runtime.Animators,rootMotion:runtime.Characters);var vertices=new byte[4*48];var locals=new AnimationLocalTransform[2];
                for(int step=0;step<128;step++){
                    Check(play.Step().State==PlayState.Paused,"Layer successful quantum: "+play.Fault?.Code);var debug=runtime.Animators!.ReadDebug(id);var plan=debug.Instructions.ToArray();numeric.Evaluate(plan,debug.Frame.Output,[],0,locals);double t=((step+1)*.125)%1;float yaw=additive?(float)(.1625+.6*t):(float)(.2+.45*t);
                    Check(Math.Abs(locals[0].Position.X-t)<1e-6&&Math.Abs(2*Math.Atan2(locals[1].Rotation.Y,locals[1].Rotation.W)-yaw)<1e-5,"Independent whole local layer oracle");
                    Check(debug.Events.Count==(step%8==0?1:0)&&debug.Events.All(e=>e.ClipId==basis.ClipId),"Only base layer emits events");if(root)Check(Math.Abs(runtime.Characters!.InspectRootMotion(id).DesiredDisplacement.X-.125)<1e-5&&runtime.Characters.Status.CommittedSequence==play.Tick,"Layer cannot obtain root authority");
                    if(step is not (0 or 1 or 7 or 31 or 127))continue;
                    ulong samples=kernel.Statistics.SampleCalls,layers=kernel.LayerStatistics.LayerCalls;Check(scene.Submit(frame++,256,256,camera.PersistentId,target:target),"Layer GPU submit");renderer.Present();scene.CaptureCharacterVertices(id,vertices);
                    Check(kernel.Statistics.SampleCalls-samples==(additive?3ul:2ul)&&kernel.LayerStatistics.LayerCalls-layers==1,$"Cached base sampled once despite two consumers, explicit static reference once: additive={additive} root={root} samples={kernel.Statistics.SampleCalls-samples} layers={kernel.LayerStatistics.LayerCalls-layers}");
                    var actual=System.Runtime.InteropServices.MemoryMarshal.Cast<byte,float>(vertices);for(int v=0;v<4;v++){var pos=source.Meshes[0].Vertices[v].Position;var expected=pos*.7f+Vector3.Transform(pos,Matrix4x4.CreateRotationY(yaw-.2f))*.3f+new Vector3(root?0:(float)t,0,0);double error=Vector3.Distance(expected,new(actual[v*12],actual[v*12+1],actual[v*12+2]));maxError=Math.Max(maxError,error);Check(error<1e-4,"Independent layer GPU skin/root strip oracle "+error);}
                    Check(scene.Costs.GeometryDraws==1&&scene.Costs.ShadowDraws==1,"Shared layered geometry/shadow");
                }
                scene.Dispose();ulong tick=play.Tick;Guid instance=runtime.Animators!.ReadFrame(id).InstanceId;play.Reload(_=>throw new Exception("No behaviours"));play.Pause();Check(play.Tick==tick&&runtime.Animators.ReadFrame(id).InstanceId!=instance&&play.Step().State==PlayState.Paused,"Layer cache reload new identity/frozen-startup");Check(runtime.Animators.ReadDebug(id).Instructions.Where(r=>r.Operation==AnimationPoseOperation.Clip&&r.Previous!=r.Current).All(r=>r.Previous==0),"Layer clocks reset after Reload");
            }finally{play.Stop();}
            Check(kernel.Statistics.Rigs==0&&kernel.Statistics.Clips==0&&renderer.SkinStats.Meshes==0&&physics.Inspect().Worlds==0,"Layer resources drain");
            byte[] packed=SceneAssetPreparation.CreateRuntimePackage(f.Root,f.Project,d.CaptureSnapshot(),[]);string moved=Path.Combine(output,"layer-moved",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(moved,"assets"));File.WriteAllBytes(Path.Combine(moved,"assets/game.ncpak"),packed);
            using(var lease=SceneAssetPreparation.Prepare(moved,f.Project,d.CaptureSnapshot(),true,"assets/game.ncpak"))Check(((RuntimeAnimationGraphAsset)lease.Assets.Require(graph.AssetId,AssetKind.AnimationGraph)).PrepareProgram(lease.Assets).CopyLayers().Single().Weights.SequenceEqual(new[]{0f,1f}),"Source-free NCP1 keeps strict v5 mask/hash/reference and real rig closure");
            var bad=graph with{AssetId=Guid.NewGuid(),Nodes=graph.Nodes.Select(n=>n.Layer is{} l?n with{Layer=l with{Mask=l.Mask with{SkeletonHash=new('B',64)}}}:n).ToArray()};File.WriteAllBytes(Path.Combine(f.Root,"assets/bad-"+bad.AssetId.ToString("N")+".ncmaanim"),AnimationGraphCodec.Encode(bad));
            var rejected=d.CreateIsolatedCopy();rejected.World.FindObject(id).Set(new AnimatorData(bad.AssetId,bad.SkeletonId));Reject(()=>{
                using var candidate=SceneAssetPreparation.Prepare(f.Root,f.Project,rejected.CaptureSnapshot(),true);using var failed=new PlaySession(rejected,FrameTimePolicy.Strict,advanceMode:PlayAdvanceMode.FixedSteps);using var composed=ScenePlayRuntime.Compose(failed,physics,candidate);
            });Check(physics.Inspect().Worlds==0&&kernel.Statistics.Rigs==0,"Mask changed content rejects before native solver/pose startup");
            var failedDocument=d.CreateIsolatedCopy();using(var prepared=SceneAssetPreparation.Prepare(f.Root,f.Project,failedDocument.CaptureSnapshot(),true))using(var failed=new PlaySession(failedDocument,FrameTimePolicy.Strict,advanceMode:PlayAdvanceMode.FixedSteps))using(var composed=ScenePlayRuntime.Compose(failed,physics,prepared)){
                bool fault=false;failed.AddSystem(new GraphFailure(_=>{if(fault)throw new InvalidOperationException("Failed prepared layer");}));failed.Start(_=>throw new Exception("No behaviours"));
                try{Check(failed.AdvanceFixedStep().State==PlayState.Running,"Layer fault baseline");byte[] before=failedDocument.CaptureBytes();fault=true;Check(failed.AdvanceFixedStep().State==PlayState.Faulted&&failed.Tick==1&&before.SequenceEqual(failedDocument.CaptureBytes()),"Layer fault preserves committed World/tick, never rollback solver");Reject(()=>composed.Animators!.ReadFrame(id));}finally{failed.Stop();}
            }Check(physics.Inspect().Worlds==0,"Failed layer explicitStop drains");
            evidence.Add(new{additive,root,ticks=128,sharedBaseSampleOnce=true,baseEventsRootOnly=true,cacheReload=true,packed=true});
            lastGraph=graph;lastDocument=d.CreateIsolatedCopy();
        }
        var original=lastGraph!;var baseline=original.Nodes.Single(n=>n.Kind==AnimationNodeKind.CachePose);var mixed=original.Nodes.Single(n=>n.Kind==AnimationNodeKind.Blend);var machine=AnimationGraphNode.Create(Guid.NewGuid(),"Layer states",AnimationNodeKind.StateMachine);var outNode=original.Nodes.Single(n=>n.Kind==AnimationNodeKind.Output);
        var a=new AnimationGraphState(Guid.NewGuid(),"Base",baseline.Id);var b=new AnimationGraphState(Guid.NewGuid(),"Layer",mixed.Id);var flag=new AnimationParameter(Guid.NewGuid(),"Automatic",AnimationParameterKind.Bool,0,0,false);
        var joint=original with{Parameters=[flag],InterruptTransitions=true,EntryState=a.Id,Nodes=[..original.Nodes,machine],States=[a,b],Transitions=[new(Guid.NewGuid(),a.Id,b.Id,0,1,null,[new(flag.Id,AnimationComparison.Equal,0,0,false)]),new(Guid.NewGuid(),b.Id,a.Id,0,1,null,[new(flag.Id,AnimationComparison.Equal,0,0,false)])],Links=original.Links.Select(l=>l.To==outNode.Id?l with{From=machine.Id}:l).ToArray(),Events=original.Events.Where(e=>e.ClipId==original.Nodes.Single(n=>n.Name=="Base right").ClipId).ToArray()};
        SceneDocument Many(int count,bool root){var d=lastDocument!.CreateIsolatedCopy();var template=d.World.GetObjects().Single(o=>o.Has<AnimatorData>());var skin=template.Get<SkinnedMeshData>();template.Destroy();if(!root)foreach(var floor in d.World.GetObjects().Where(o=>o.Has<BoxColliderData>()).ToArray())floor.Destroy();
            for(int i=0;i<count;i++){var actor=d.World.CreateObject("Layer actor");actor.Set(TransformData.Identity);actor.Set(skin);actor.Set(new AnimatorData(joint.AssetId,joint.SkeletonId));if(root){actor.Set(new RootMotionData(0));actor.Set(CharacterData.Default with{Controlled=false});}}return d;}
        TestAnimatorJointAcceptance(renderer,ref frame,native,output,f,joint,Many,physics,kernel,"m6-7-b","M6.7-B");
        Check(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"Layer API validation");File.WriteAllText(Path.Combine(output,"m6-7-b-layer-results.json"),JsonSerializer.Serialize(new{schema=1,graphVersion=AnimationGraphCodec.CurrentVersion,realNca=true,rows=evidence,maxGpuVertexError=maxError,reimportHashMismatchRejected=true,failedCandidate=true,joint=true,manualAccepted=false}));
        Console.WriteLine("PASS M6.7-B actual pinned NCA masked override/additive/cached sample-once/base events-root/512quanta/CPU-GPU oracle/reload/NCP1");
    }
}
