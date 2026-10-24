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
using Ncma.Scene;
using Ncma.Scene.Rendering;
using Ncma.Runtime;

internal static unsafe partial class Program
{
    private static void TestAnimatorInterruptions(RendererSession renderer,ref ulong frame,string native,string output)
    {
        var f=new SkinFixture(output);var baseGraph=AnimatorGraph(f);
        var parameter=new AnimationParameter(Guid.NewGuid(),"Switch",AnimationParameterKind.Bool,0,0,false);
        var machine=AnimationGraphNode.Create(Guid.NewGuid(),"Machine",AnimationNodeKind.StateMachine);
        var a=new AnimationGraphState(Guid.NewGuid(),"Right",baseGraph.Nodes[0].Id);
        var b=new AnimationGraphState(Guid.NewGuid(),"Left",baseGraph.Nodes[1].Id);
        var graph=baseGraph with{InterruptTransitions=true,Events=[new(Guid.NewGuid(),baseGraph.Nodes[0].ClipId,.1,"Right step"),new(Guid.NewGuid(),baseGraph.Nodes[1].ClipId,.1,"Left step")],EntryState=a.Id,Parameters=[parameter],Nodes=[baseGraph.Nodes[0],baseGraph.Nodes[1],machine,baseGraph.Nodes[4]],
            Links=[new(Guid.NewGuid(),machine.Id,"pose",baseGraph.Nodes[4].Id,"pose")],States=[a,b],
            Transitions=[new(Guid.NewGuid(),a.Id,b.Id,0,1,null,[new(parameter.Id,AnimationComparison.Equal,0,0,true)]),
                new(Guid.NewGuid(),b.Id,a.Id,0,1,null,[new(parameter.Id,AnimationComparison.Equal,0,0,false)])]};
        File.WriteAllBytes(Path.Combine(f.Root,"assets/interrupt.ncmaanim"),AnimationGraphCodec.Encode(graph));
        SceneDocument Document(int count,bool root) {
            var d=new SceneDocument("Interruptions",CharacterComponents.Register(RenderComponentRegistry.Register(RenderConfiguration.CreateRegistry())),CharacterComponents.RequireComposition);
            for(int i=0;i<count;i++) {
                Guid o=d.CreateObject("Actor");d.SetComponent(o, TransformData.Identity with{Position=new(0,0,i*3)});
                d.SetComponent(o, new SkinnedMeshData(f.Model,f.Manifest.Meshes[0].Mesh,graph.SkeletonId,f.Manifest.Meshes[0].Materials,true,true,uint.MaxValue));
                d.SetComponent(o, new AnimatorData(graph.AssetId,graph.SkeletonId));
                if(root){d.SetComponent(o, new RootMotionData(0));d.SetComponent(o, CharacterData.Default with{Controlled=false});}
            }
            if(root){Guid ground=d.CreateObject("Ground");d.SetComponent(ground, TransformData.Identity with{Position=new(0,-.2f,0)});d.SetComponent(ground, new BoxColliderData(100,.2f,100,1000,1,false));}
            Guid camera=d.CreateObject("Camera");d.SetComponent(camera, TransformData.Identity with{Position=new(0,0,5)});d.SetComponent(camera, CameraData.Default with{Projection=CameraProjection.Orthographic,OrthographicHeight=4});
            Guid light=d.CreateObject("Light");d.SetComponent(light, TransformData.Identity with{Rotation=Quaternion.CreateFromYawPitchRoll(.3f,-.5f,0)});d.SetComponent(light, DirectionalLightData.Default);return d;
        }
        using var physics=new PhysicsService(Path.Combine(native,"m2/plugins"),characterSupport:true);
        string path=Path.Combine(native,"NcmaAnimationKernel.dll");using var kernel=new PoseKernel(path,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),blendSupport:true);
        double maxError=0;
        foreach(bool root in new[]{false,true}) {
            var d=Document(1,root);Guid id=d.GetObjectIds().Single(o=>d.HasComponent<AnimatorData>(o)),camera=d.GetObjectIds().Single(o=>d.HasComponent<CameraData>(o));
            using var assets=SceneAssetPreparation.Prepare(f.Root,f.Project,d.CaptureSnapshot(),true);
            using var play=new PlaySession(d.BakeRuntime(d.Revision).Image.Instantiate(d.Components),FrameTimePolicy.Strict,fixedDeltaSeconds:.1,advanceMode:PlayAdvanceMode.FixedSteps);
            using var runtime=ScenePlayRuntime.Compose(play,physics,assets);
            play.Start(_=>throw new Exception("No behaviours"));play.Pause();
            try {
                using var cache=new RenderResourceCache(renderer);using var scene=new SceneRenderSession(renderer,cache,play.Scene.World);scene.Prepare(assets,play.Scene.CaptureSnapshot(),poseKernel:kernel,play:play,rootMotion:runtime.Characters,animators:runtime.Animators);
                using var target=renderer.CreateViewTarget(256,256);var captured=new byte[4*48];var frozen=new AnimationLocalTransform[2];
                float expectedX=0,expectedYaw=.2f;
                for(int step=0;step<128;step++) {
                    runtime.Animators!.SetBool(id,parameter.Id,step%2==0);float targetX=step%2==0?-.1f:.1f,targetYaw=step%2==0?.2f:.26f;
                    float previousX=expectedX,previousYaw=expectedYaw;
                    // First normal transition advances BOTH clip sources; interrupted sources stop at the previous committed boundary.
                    if(step==0){expectedX=.08f;expectedYaw=.254f;}else {expectedX=previousX+(targetX-previousX)*.1f;expectedYaw=previousYaw+(targetYaw-previousYaw)*.1f;}
                    Check(play.Step().State==PlayState.Paused,"Pinned NCA interruption quantum: "+play.Fault?.Code);
                    var debug=runtime.Animators.ReadDebug(id);Check(debug.Frame.Context.Tick==(ulong)step+1&&debug.Instructions.Count==3,"One clock/constant interrupted recipe budget");
                    Check(debug.Events.Count==1&&debug.Events[0].Name==(step%2==0?"Left step":"Right step"),"Persistent v2 target-only event committed once on every reentry");
                    if(step>0) {
                        Check(debug.Frame.FrozenPoseGeneration==(ulong)step,"Exact interruption generation");runtime.Animators.CopyFrozenPose(id,(ulong)step,frozen);
                        Check(Math.Abs(frozen[0].Position.X-previousX)<1e-5&&Math.Abs(2*Math.Atan2(frozen[1].Rotation.Y,frozen[1].Rotation.W)-previousYaw)<1e-5,"Independent committed whole-pose cache oracle");
                        if(step>1)Reject(()=>runtime.Animators.CopyFrozenPose(id,(ulong)step-1,frozen));
                    }
                    if(root) {
                        var movement=runtime.Characters!.InspectRootMotion(id);float desired=step==0?.08f:targetX*.1f;
                        Check(Math.Abs(movement.DesiredDisplacement.X-desired)<1e-5&&movement.Tick==play.Tick&&runtime.Characters.Status.CommittedSequence==play.Tick,"Frozen source zero root interval / unique Jolt step");
                    }
                    if(step is not (0 or 1 or 31 or 127))continue;
                    Check(scene.Submit(frame++,256,256,camera,target:target),"Interrupted native skin submit");renderer.Present();scene.CaptureCharacterVertices(id,captured);
                    var floats=System.Runtime.InteropServices.MemoryMarshal.Cast<byte,float>(captured);var vertices=SyntheticModel().Meshes[0].Vertices;
                    for(int v=0;v<4;v++) {
                        Vector3 source=vertices[v].Position;
                        Vector3 expected=source*.7f+Vector3.Transform(source,Matrix4x4.CreateRotationY(expectedYaw-.2f))*.3f+new Vector3(root?0:expectedX,0,0);
                        var actual=new Vector3(floats[v*12],floats[v*12+1],floats[v*12+2]);double error=Vector3.Distance(expected,actual);maxError=Math.Max(maxError,error);
                        Check(error<1e-4,$"Independent interrupted GPU skin/root-strip oracle root={root} step={step} error={error}");
                    }
                    Check(scene.Costs.GeometryDraws==1&&scene.Costs.ShadowDraws==1,"Same interrupted skin palette in geometry and shadow");
                }
                // Reload replaces the instance and retires every frozen pose without advancing chronology.
                var oldInstance=runtime.Animators!.ReadFrame(id).InstanceId;play.Reload(_=>throw new Exception("No behaviours"));
                Check(play.Tick==128&&runtime.Animators.ReadFrame(id).InstanceId!=oldInstance&&runtime.Animators.ReadFrame(id).FrozenPoseGeneration==0,"Frozen pose reload reset");
                Reject(()=>runtime.Animators.CopyFrozenPose(id,127,frozen));
            }finally{play.Stop();}
            Check(kernel.Statistics.Rigs==0&&kernel.Statistics.Clips==0&&renderer.SkinStats.Meshes==0&&physics.Inspect().Worlds==0,"Interrupted resources close baseline");
        }
        var previewDocument=Document(1,false);var previewId=previewDocument.GetObjectIds().Single(o=>previewDocument.HasComponent<AnimatorData>(o));
        using(var assets=SceneAssetPreparation.Prepare(f.Root,f.Project,previewDocument.CaptureSnapshot(),true)) {
            var program=((RuntimeAnimationGraphAsset)assets.Assets.Require(graph.AssetId,AssetKind.AnimationGraph)).PrepareProgram(assets.Assets);
            var source=GraphPoseSnapshotPreparation.Prepare(program,assets.Assets);var instance=new AnimationGraphInstance(program,new(Guid.NewGuid(),Guid.NewGuid(),0),source);
            for(int n=0;n<512;n++){instance.SetBool(parameter.Id,n%2==0);var t=instance.Prepare(instance.Frame.Context,.1);instance.Commit(t,instance.Frame.Context with{Tick=instance.Frame.Context.Tick+1});}
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int n=0;n<1024;n++){instance.SetBool(parameter.Id,n%2==0);var t=instance.Prepare(instance.Frame.Context,.1);instance.Commit(t,instance.Frame.Context with{Tick=instance.Frame.Context.Tick+1});}
            Check(GC.GetAllocatedBytesForCurrentThread()==before,"Warmed actual NCA cache evaluation allocates zero");
            var rows=instance.ReadDebug(instance.Frame.Context).Instructions.ToArray();var locals=new AnimationLocalTransform[2];instance.CopyFrozenPose(instance.Frame.FrozenPoseGeneration,locals);
            var untouched=new[]{new AnimationLocalTransform(new(7,8,9),Quaternion.Identity,1),new AnimationLocalTransform(new(7,8,9),Quaternion.Identity,1)};
            var invalid=(AnimationPoseInstruction[])rows.Clone();invalid[^1]=invalid[^1] with{Weight=float.NaN};Reject(()=>source.Evaluate(invalid,instance.Frame.Output,locals,instance.Frame.FrozenPoseGeneration,untouched));
            Check(untouched.All(v=>v.Position==new Vector3(7,8,9)),"Rejected complete pose must not copy partial outputs");
            Reject(()=>source.Evaluate(rows,instance.Frame.Output,locals,instance.Frame.FrozenPoseGeneration+1,untouched));
            byte[] original=previewDocument.CaptureBytes();var previewRuntime=BakeRenderScene(previewDocument);
            byte[] runtimeBefore=previewRuntime.CaptureCheckpoint().CopyBytes();
            using(var cache=new RenderResourceCache(renderer))using(var scene=new SceneRenderSession(renderer,cache,previewRuntime.World))using(var target=renderer.CreateViewTarget(256,256)) {scene.Prepare(assets,previewRuntime.CaptureSnapshot(),poseKernel:kernel);
                scene.Animation!.ControlPreview(previewId,parameter.Id,AnimationParameterKind.Bool,1);scene.Animation.AdvancePreview(1d/60);
                scene.Animation.ControlPreview(previewId,parameter.Id,AnimationParameterKind.Bool,0);scene.Animation.AdvancePreview(1d/60);
                Check(scene.Animation.PreviewFrame(previewId).FrozenPoseGeneration==1&&previewRuntime.World.Tick==0,"Independent preview interruption uses no Play/World tick");
                Check(scene.Submit(frame++,256,256,previewDocument.GetObjectIds().Single(o=>previewDocument.HasComponent<CameraData>(o)),target:target),"Independent interrupted preview native submit");renderer.Present();
                Check(original.SequenceEqual(previewDocument.CaptureBytes())&&runtimeBefore.SequenceEqual(previewRuntime.CaptureCheckpoint().CopyBytes()),"Preview leaves authoring and runtime ECS values intact");
            }
            Check(kernel.Statistics.Rigs==0&&renderer.SkinStats.Meshes==0,"Independent preview resource drain");
        }
        foreach(int count in new[]{0,1,8,32}) {
            var d=Document(count,false);var ids=d.GetObjectIds().Where(o=>d.HasComponent<AnimatorData>(o)).ToArray();
            using var assets=SceneAssetPreparation.Prepare(f.Root,f.Project,d.CaptureSnapshot(),true);using var play=new PlaySession(d.BakeRuntime(d.Revision).Image.Instantiate(d.Components),FrameTimePolicy.Strict,fixedDeltaSeconds:.1,advanceMode:PlayAdvanceMode.FixedSteps);
            using var runtime=ScenePlayRuntime.Compose(play,physics,assets);play.Start(_=>throw new Exception("No behaviours"));
            try {
                foreach(Guid id in ids)runtime.Animators!.SetBool(id,parameter.Id,true);Check(play.AdvanceFixedStep().State==PlayState.Running,"Many interruption first quantum");
                for(int i=0;i<ids.Length;i++)if(i%2==0)runtime.Animators!.SetBool(ids[i],parameter.Id,false);
                Check(play.AdvanceFixedStep().State==PlayState.Running,"Many interruption second quantum");
                for(int i=0;i<ids.Length;i++)Check(runtime.Animators!.ReadFrame(ids[i]).FrozenPoseGeneration==(i%2==0?1UL:0UL),"Independent actor interruption caches");
                Check(kernel.Statistics.Rigs==0,"Headless interruptions must not create native pose/GPU");
            }finally{play.Stop();}
        }
        var failedDocument=Document(1,false);var failedId=failedDocument.GetObjectIds().Single(o=>failedDocument.HasComponent<AnimatorData>(o));
        using(var assets=SceneAssetPreparation.Prepare(f.Root,f.Project,failedDocument.CaptureSnapshot(),true))using(var play=new PlaySession(failedDocument.BakeRuntime(failedDocument.Revision).Image.Instantiate(failedDocument.Components),FrameTimePolicy.Strict,fixedDeltaSeconds:.1,advanceMode:PlayAdvanceMode.FixedSteps))using(var runtime=ScenePlayRuntime.Compose(play,physics,assets)) {
            bool fail=false;play.AddSystem(new GraphFailure(_=>{if(fail)throw new InvalidOperationException("Prepared interruption must not publish");}));play.Start(_=>throw new Exception("No behaviours"));
            try{runtime.Animators!.SetBool(failedId,parameter.Id,true);Check(play.AdvanceFixedStep().State==PlayState.Running,"Failed cache baseline");runtime.Animators.SetBool(failedId,parameter.Id,false);fail=true;
                Check(play.AdvanceFixedStep().State==PlayState.Faulted&&play.Tick==1,"Failed interruption quantum preserves committed tick");Reject(()=>runtime.Animators.CopyFrozenPose(failedId,1,new AnimationLocalTransform[2]));
            }finally{play.Stop();}
        }
        Check(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"Interrupted DX11 API validation");
        TestAnimatorJointAcceptance(renderer,ref frame,native,output,f,graph,Document,physics,kernel);
        File.WriteAllText(Path.Combine(output,"animator-interruption-results.json"),JsonSerializer.Serialize(new{schema=1,realNca=true,repeatedInterruptions=128,cacheOracle=true,gpuSkinMaxError=maxError,rootStripped=true,frozenRootSource="zero",uniqueJolt=true,headlessActors=new[]{0,8,32},nativeSnapshotWarmIterations=1024,numericAllocation=0,independentPreview=true,failedCandidate=true,reload=true,manualAccepted=false}));
        Console.WriteLine("PASS M6.5-B pinned NCA 128 interruptions/cache/native GPU oracle/root-strip/Jolt/Headless isolation/fault/reload");
    }
}
