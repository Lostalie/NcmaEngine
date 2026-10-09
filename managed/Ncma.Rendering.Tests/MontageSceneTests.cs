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
    private static AnimationGraphDefinition AddMontage(AnimationGraphDefinition graph,AnimationMontageDefinition montage,bool startup=true)
    {
        var output=graph.Nodes.Single(n=>n.Kind==AnimationNodeKind.Output);var edge=graph.Links.Single(l=>l.To==output.Id);
        var nodes=graph.Nodes.ToList();var links=graph.Links.Where(l=>l.Id!=edge.Id).ToList();Guid previous=edge.From;
        foreach(var slot in montage.Slots){var node=AnimationGraphNode.Create(Guid.NewGuid(),slot.Name,AnimationNodeKind.Slot) with{SlotId=slot.Id,PlayOnStart=startup};nodes.Add(node);links.Add(new(Guid.NewGuid(),previous,"pose",node.Id,"pose"));previous=node.Id;}
        links.Add(new(Guid.NewGuid(),previous,"pose",output.Id,"pose"));return graph with{Montage=montage,Nodes=nodes.ToArray(),Links=links.ToArray()};
    }
    private static void TestMontageScenes(RendererSession renderer,ref ulong frame,string native,string output)
    {
        native=Path.GetDirectoryName(native)!;string path=Path.Combine(native,"NcmaAnimationKernel.dll");
        using var kernel=new PoseKernel(path,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),blendSupport:true,layerSupport:true);
        using var physics=new PhysicsService(Path.Combine(native,"m2/plugins"),characterSupport:true);
        var f=new SkinFixture(output);Guid slot=Guid.NewGuid(),attack=Guid.NewGuid(),recovery=Guid.NewGuid();var basis=AnimatorGraph(f);
        var montage=new AnimationMontageDefinition(1,Guid.NewGuid(),"Persistent montage",basis.SkeletonId,[new(slot,"Body",attack,10,true,true,.05,.1)],
            [new(attack,"Attack",slot,f.Manifest.Clips[0],.1,.25,recovery),new(recovery,"Recovery",slot,f.Manifest.Clips[1],.4,.65,Guid.Empty)]);
        var graph=AddMontage(basis,montage) with{Events=[new(Guid.NewGuid(),f.Manifest.Clips[0],.2,"Attack mark"),new(Guid.NewGuid(),f.Manifest.Clips[1],.6,"Recovery mark")]};
        File.WriteAllBytes(Path.Combine(f.Root,"assets/montage.ncmaanim"),AnimationGraphCodec.Encode(graph));
        SceneDocument Document(int count,bool root){
            var d=new SceneDocument("Persistent Slot",CharacterComponents.Register(RenderComponentRegistry.Register(RenderConfiguration.CreateRegistry())),CharacterComponents.RequireComposition);
            for(int i=0;i<count;i++){var o=d.World.CreateObject("Actor "+i);o.Set(TransformData.Identity with{Position=new(0,0,i*3)});o.Set(new SkinnedMeshData(f.Model,f.Manifest.Meshes[0].Mesh,graph.SkeletonId,f.Manifest.Meshes[0].Materials,true,true,uint.MaxValue));o.Set(new AnimatorData(graph.AssetId,graph.SkeletonId));if(root){o.Set(new RootMotionData(0));o.Set(CharacterData.Default with{Controlled=false});}}
            if(root){var floor=d.World.CreateObject("Ground");floor.Set(TransformData.Identity with{Position=new(0,-.2f,0)});floor.Set(new BoxColliderData(100,.2f,100,1000,1,false));}
            var camera=d.World.CreateObject("Camera");camera.Set(TransformData.Identity with{Position=new(0,0,5)});camera.Set(CameraData.Default with{Projection=CameraProjection.Orthographic,OrthographicHeight=40});
            var light=d.World.CreateObject("Light");light.Set(TransformData.Identity with{Rotation=Quaternion.CreateFromYawPitchRoll(.3f,-.5f,0)});light.Set(DirectionalLightData.Default);return d;
        }
        var evidence=new List<object>();double maximumError=0;
        foreach(bool root in new[]{false,true})foreach(int count in new[]{1,8,32}){
            var d=Document(count,root);using var assets=SceneAssetPreparation.Prepare(f.Root,f.Project,d.CaptureSnapshot(),true);
            var retained=(RuntimeAnimationGraphAsset)assets.Assets.Require(graph.AssetId,AssetKind.AnimationGraph);var program=retained.PrepareProgram(assets.Assets);
            Check(program.Montage is not null&&program.Montage.ResourceGeneration==f.Plan.Record.Generation!.Number,"Persistent graph owns actual same NCA Montage closure");
            using var play=new PlaySession(d,FrameTimePolicy.Strict,fixedDeltaSeconds:.1,advanceMode:PlayAdvanceMode.FixedSteps);using var runtime=ScenePlayRuntime.Compose(play,physics,assets);
            play.Start(_=>throw new Exception("No behaviours"));play.Pause();var ids=d.World.GetObjects().Where(o=>o.Has<AnimatorData>()).Select(o=>o.PersistentId).ToArray();Guid camera=d.World.GetObjects().Single(o=>o.Has<CameraData>()).PersistentId;
            try{using var cache=new RenderResourceCache(renderer);using var target=renderer.CreateViewTarget(256,256);
                using(var scene=new SceneRenderSession(renderer,cache,d.World,assets,d.CaptureSnapshot(),poseKernel:kernel,play:play,animators:runtime.Animators,rootMotion:runtime.Characters)){
                    var vertices=new byte[4*48];var intervals=new MontageInterval[528];
                    for(int step=0;step<5;step++){
                        var status=play.Step();Check(status.State==PlayState.Paused,"Persistent Slot quantum: "+status.Fault+" root="+root+" count="+count+" step="+step);var identities=new HashSet<Guid>();
                        foreach(Guid id in ids){var current=runtime.Animators!.ReadFrame(id);identities.Add(current.InstanceId);var playback=runtime.Animators.ReadMontageFrame(id);Check(playback.Context==current.Context&&current.Context.Tick==play.Tick,"Joint Slot and graph chronology");
                            int n=runtime.Animators.CopyCommittedMontageIntervals(id,intervals);if(step==1)Check(n==2&&intervals[0].ClipId!=intervals[1].ClipId,"All crossed Section clips");if(step==3)Check(n==1&&!runtime.Animators.ReadMontageSlot(id,slot).Active,"Terminal inactive Slot retains traversal");
                            var events=runtime.Animators.ReadDebug(id).Events;foreach(var e in events)Check(e.Context.Tick==play.Tick,"Same committed Notify tick");
                            if(step==0||step==3)Check(events.Any(e=>e.NodeId==graph.Nodes.Single(n=>n.Kind==AnimationNodeKind.Slot).Id),"Actual Slot Notify, including terminal traversal");
                            if(root){double expected=step==0?.0875:step==1?0:step==2?-.1:step==3?-.025:.05;Check(Math.Abs(runtime.Characters!.InspectRootMotion(id).DesiredDisplacement.X-expected)<1e-5,"Unique Movement weighted interval root oracle");}}
                        Check(identities.Count==count&&scene.Submit(frame++,256,256,camera,target:target),"Every distinct Slot actor submits");renderer.Present();Check(scene.Costs.GeometryDraws==count&&scene.Costs.ShadowDraws==count,"Actual Slot geometry and shadow");
                        scene.CaptureCharacterVertices(ids[0],vertices);double t=(step+1)*.1;var s=runtime.Animators!.ReadMontageSlot(ids[0],slot);float weight=s.Weight;
                        float baseX=(float)(.5*t),helper=(float)(.2+.45*t);float slotX=s.ClipId==f.Manifest.Clips[0]?(float)s.Time:-(float)s.Time;float slotYaw=s.ClipId==f.Manifest.Clips[0]?(float)(.2+.6*s.Time):.2f;
                        float visualX=baseX*(1-weight)+slotX*weight,yaw=helper*(1-weight)+slotYaw*weight;
                        var floats=System.Runtime.InteropServices.MemoryMarshal.Cast<byte,float>(vertices);
                        for(int v=0;v<4;v++){var pos=f.Source.Meshes[0].Vertices[v].Position;var expected=pos*.7f+Vector3.Transform(pos,Matrix4x4.CreateRotationY(yaw-.2f))*.3f+new Vector3(root?0:visualX,0,0);var actual=new Vector3(floats[v*12],floats[v*12+1],floats[v*12+2]);double error=Vector3.Distance(actual,expected);maximumError=Math.Max(maximumError,error);Check(error<2e-5,"Independent Slot GPU vertex oracle: "+error);}
                    }
                }
                Guid old=runtime.Animators!.ReadFrame(ids[0]).InstanceId;ulong tick=play.Tick;play.Reload(_=>throw new Exception("No behaviours"));Check(play.Tick==tick&&runtime.Animators.ReadFrame(ids[0]).InstanceId!=old,"Fresh joint owner on Reload");
                Check(play.Step().State==PlayState.Paused&&runtime.Animators.ReadMontageSlot(ids[0],slot).Active,"Persistent startup survives Reload without host binding");
                evidence.Add(new{count,root,ticks=5,terminalRoot=true,notify=true,reload=true});
            }finally{play.Stop();}
            Check(kernel.Statistics.Rigs==0&&kernel.Statistics.Clips==0&&physics.Inspect().Worlds==0,"Slot resources drain");
        }
        // The isolated sequence uses the identical interval recipe without World/solver/GPU authority.
        using(var assets=SceneAssetPreparation.Prepare(f.Root,f.Project,Document(1,false).CaptureSnapshot(),true)){
            var p=((RuntimeAnimationGraphAsset)assets.Assets.Require(graph.AssetId,AssetKind.AnimationGraph)).PrepareProgram(assets.Assets);
            var result=AnimationGraphSequence.Run(p,new(.1,5,[],[]),rootSource:new AnimationSequenceRootSource(p,assets.Assets));
            double[] expected=[.0875,0,-.1,-.025,.05];for(int n=0;n<5;n++)Check(Math.Abs(result.Timeline[n].Root!.Value.Translation.X-expected[n])<1e-5,"Isolated actual NCA sequence shares root interval oracle");
            var i=new AnimationGraphInstance(p,new(Guid.NewGuid(),Guid.NewGuid(),0));var token=i.Prepare(i.Frame.Context,.1);i.Commit(token,i.Frame.Context with{Tick=1});var recipe=new AnimationPoseInstruction[p.MaximumPlanInstructions];int rows=i.CopyCommittedPlan(recipe);var traversals=new MontageInterval[528];int nIntervals=i.CopyCommittedMontageIntervals(traversals);
            var numeric=GraphPoseSnapshotPreparation.Prepare(p,assets.Assets);var destination=new AnimationLocalTransform[2];destination[0]=new(new(77),Quaternion.Identity,1);int at=Array.FindIndex(recipe,0,rows,r=>r.Operation==AnimationPoseOperation.Slot);recipe[at]=recipe[at] with{SlotId=Guid.NewGuid()};Reject(()=>numeric.Evaluate(recipe.AsSpan(0,rows),0,[],0,destination));Check(destination[0].Position.X==77,"Unused malformed Slot cannot partially publish CPU pose");Reject(()=>new AnimationSequenceRootSource(p,assets.Assets).Evaluate(recipe.AsSpan(0,rows),0,traversals.AsSpan(0,nIntervals),.1));
        }
        // Global Slot overlays are NOT part of a state-machine interruption's frozen base.
        var parameter=new AnimationParameter(Guid.NewGuid(),"Switch",AnimationParameterKind.Bool,0,0,false);var machine=AnimationGraphNode.Create(Guid.NewGuid(),"Machine",AnimationNodeKind.StateMachine);
        var stateA=new AnimationGraphState(Guid.NewGuid(),"Right",basis.Nodes[0].Id);var stateB=new AnimationGraphState(Guid.NewGuid(),"Left",basis.Nodes[1].Id);
        var stateGraph=basis with{AssetId=Guid.NewGuid(),InterruptTransitions=true,EntryState=stateA.Id,Parameters=[parameter],Nodes=[basis.Nodes[0],basis.Nodes[1],machine,basis.Nodes[4]],Links=[new(Guid.NewGuid(),machine.Id,"pose",basis.Nodes[4].Id,"pose")],States=[stateA,stateB],Transitions=[new(Guid.NewGuid(),stateA.Id,stateB.Id,0,1,null,[new(parameter.Id,AnimationComparison.Equal,0,0,true)]),new(Guid.NewGuid(),stateB.Id,stateA.Id,0,1,null,[new(parameter.Id,AnimationComparison.Equal,0,0,false)])]};
        stateGraph=AddMontage(stateGraph,montage);File.WriteAllBytes(Path.Combine(f.Root,"assets/montage-state.ncmaanim"),AnimationGraphCodec.Encode(stateGraph));
        var stateDocument=Document(1,false);Guid actor=stateDocument.World.GetObjects().Single(o=>o.Has<AnimatorData>()).PersistentId;stateDocument.World.FindObject(actor).Set(new AnimatorData(stateGraph.AssetId,stateGraph.SkeletonId));
        using(var assets=SceneAssetPreparation.Prepare(f.Root,f.Project,stateDocument.CaptureSnapshot(),true))using(var play=new PlaySession(stateDocument,FrameTimePolicy.Strict,fixedDeltaSeconds:.1,advanceMode:PlayAdvanceMode.FixedSteps))using(var animator=SceneAnimatorRuntime.Compose(play,assets)!){
            play.Start(_=>throw new Exception("No behaviours"));try{animator.SetBool(actor,parameter.Id,true);Check(play.AdvanceFixedStep().State==PlayState.Running,"Slot transition first step");animator.SetBool(actor,parameter.Id,false);Check(play.AdvanceFixedStep().State==PlayState.Running,"Slot transition interruption");var frozen=new AnimationLocalTransform[2];animator.CopyFrozenPose(actor,animator.ReadFrame(actor).FrozenPoseGeneration,frozen);Check(Math.Abs(frozen[0].Position.X-.08)<1e-5,"Freeze pre-Slot base .08, never the already overlaid .2");}finally{play.Stop();}
        }
        foreach(int fault in new[]{0,1,2}){
            var d=Document(1,true);Guid id=d.World.GetObjects().Single(o=>o.Has<AnimatorData>()).PersistentId;using var assets=SceneAssetPreparation.Prepare(f.Root,f.Project,d.CaptureSnapshot(),true);using var play=new PlaySession(d,FrameTimePolicy.Strict,fixedDeltaSeconds:.1,advanceMode:PlayAdvanceMode.FixedSteps);using var runtime=ScenePlayRuntime.Compose(play,physics,assets);
            play.AddSystem(new GraphFailure(w=>{if(fault==0)throw new InvalidOperationException("Prepared Slot downstream failure");if(fault==1){var current=runtime.Animators!.DescribeBindings();try{runtime.Animators.RequestMontage(id,new(Guid.NewGuid(),Guid.NewGuid(),new(play.SessionId,w.Identity,w.Tick),slot,MontageRequestKind.Cancel,Guid.Empty,10));}catch(InvalidOperationException){}}else{var created=play.Commands.SpawnEmpty("late failure");play.Commands.AttachBehaviour(created.ObjectId,new(Guid.NewGuid(),"Missing",true,[]));}}));
            byte[] before=d.CaptureBytes();play.Start(_=>throw new Exception("Injected factory"));try{Check(play.AdvanceFixedStep().State==PlayState.Faulted&&play.Tick==0,"Slot failure cannot publish managed tick");Check(before.SequenceEqual(d.CaptureBytes()),"Failed Slot candidate leaves committed World");Reject(()=>runtime.Animators!.ReadMontageFrame(id));Reject(()=>runtime.Characters!.InspectRootMotion(id));Check(runtime.Characters!.Status.NumericalExecutionStarted==(fault==2),"Irreversible solver remains fail-stop, not rollback");}finally{play.Stop();}
        }
        TestAnimatorJointAcceptance(renderer,ref frame,native,output,f,graph,Document,physics,kernel,"m6-8-b2b2","M6.8-B2b-2");
        TestM6FinalAcceptance(renderer,ref frame,native,output,f,graph,Document,kernel);
        Check(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"Slot DX11 validation");
        File.WriteAllText(Path.Combine(output,"m6-8-b2b2-montage-results.json"),JsonSerializer.Serialize(new{schema=1,graphVersion=graph.Version,persistentMontage=true,rows=evidence,maxGpuVertexError=maximumError,manualAccepted=false}));
        Console.WriteLine("PASS M6.8-B2b-2 persistent Slot/actual NCA/1-8-32/root/terminal Notify/GPU oracle/Reload");
    }
}
