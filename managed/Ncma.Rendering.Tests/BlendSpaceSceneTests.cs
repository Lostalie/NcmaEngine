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
    private static void TestBlendSpaceScenes(RendererSession renderer,ref ulong frame,string native,string output)
    {
        var model=SyntheticModel();var left=model.Clips[1];model=model with{Clips=[model.Clips[0],left with{Duration=2,Tracks=left.Tracks.Select(t=>t with{Keys=t.Keys.Select(k=>k with{Time=k.Time*2,Value=k.Time==0?k.Value:k.Value with{Position=k.Value.Position*2}}).ToArray()}).ToArray()}]};
        var f=new SkinFixture(output,model);var m=f.Manifest;var x=new AnimationParameter(Guid.NewGuid(),"Speed",AnimationParameterKind.Float,.2,0,false);var y=x with{Id=Guid.NewGuid(),Name="Turn",FloatDefault=.3};
        var samples=new[]{new BlendSpaceSample(Guid.Parse("00000000-0000-4000-8000-000000000001"),m.Clips[0],0,0),new BlendSpaceSample(Guid.Parse("00000000-0000-4000-8000-000000000002"),m.Clips[1],1,0),new BlendSpaceSample(Guid.Parse("00000000-0000-4000-8000-000000000003"),m.Clips[0],0,1)};
        var space=new BlendSpaceDefinition(Guid.NewGuid(),2,new(x.Id,"Speed","m/s",0,1),new(y.Id,"Turn","degree/s",0,1),1,Guid.Empty,samples);
        var node=AnimationGraphNode.Create(Guid.NewGuid(),"Locomotion",AnimationNodeKind.BlendSpace) with{BlendSpace=space,Speed=1,Loop=true};var end=AnimationGraphNode.Create(Guid.NewGuid(),"Output",AnimationNodeKind.Output);
        var graph=new AnimationGraphDefinition(AnimationGraphCodec.CurrentVersion,Guid.NewGuid(),"Actual unequal duration space",m.Skeleton!.Value,Guid.Empty,[x,y],[node,end],[new(Guid.NewGuid(),node.Id,"pose",end.Id,"pose")],[],[]){Events=[new(Guid.NewGuid(),m.Clips[0],.125,"Right foot"),new(Guid.NewGuid(),m.Clips[0],1,"Right end"),new(Guid.NewGuid(),m.Clips[1],.25,"Left foot"),new(Guid.NewGuid(),m.Clips[1],2,"Left end")]};
        File.WriteAllBytes(Path.Combine(f.Root,"assets/space.ncmaanim"),AnimationGraphCodec.Encode(graph));
        SceneDocument Document(int count,bool root){var d=new SceneDocument("Space",CharacterComponents.Register(RenderComponentRegistry.CreateRegistry()),CharacterComponents.RequireComposition);for(int i=0;i<count;i++){var o=d.World.CreateObject("Actor");o.Set(TransformData.Identity with{Position=new(0,0,i*3)});o.Set(new SkinnedMeshData(f.Model,m.Meshes[0].Mesh,graph.SkeletonId,m.Meshes[0].Materials,true,true,uint.MaxValue));o.Set(new AnimatorData(graph.AssetId,graph.SkeletonId));if(root){o.Set(new RootMotionData(0));o.Set(CharacterData.Default with{Controlled=false});}}
            if(root){var ground=d.World.CreateObject("Ground");ground.Set(TransformData.Identity with{Position=new(0,-.2f,0)});ground.Set(new BoxColliderData(100,.2f,100,1000,1,false));}var camera=d.World.CreateObject("Camera");camera.Set(TransformData.Identity with{Position=new(0,0,5)});camera.Set(CameraData.Default with{Projection=CameraProjection.Orthographic,OrthographicHeight=40});var light=d.World.CreateObject("Light");light.Set(TransformData.Identity with{Rotation=Quaternion.CreateFromYawPitchRoll(.3f,-.5f,0)});light.Set(DirectionalLightData.Default);return d;}
        using var physics=new PhysicsService(Path.Combine(native,"m2/plugins"),characterSupport:true);string path=Path.Combine(native,"NcmaAnimationKernel.dll");using var kernel=new PoseKernel(path,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),blendSupport:true);double maxError=0;
        foreach(bool root in new[]{false,true}) {
            var d=Document(1,root);Guid id=d.World.GetObjects().Single(o=>o.Has<AnimatorData>()).PersistentId,camera=d.World.GetObjects().Single(o=>o.Has<CameraData>()).PersistentId;
            using var assets=SceneAssetPreparation.Prepare(f.Root,f.Project,d.CaptureSnapshot(),true);using var play=new PlaySession(d,FrameTimePolicy.Strict,fixedDeltaSeconds:.125,advanceMode:PlayAdvanceMode.FixedSteps);using var runtime=ScenePlayRuntime.Compose(play,physics,assets);play.Start(_=>throw new Exception("No behaviours"));play.Pause();
            try {using var cache=new RenderResourceCache(renderer);using var scene=new SceneRenderSession(renderer,cache,d.World,assets,d.CaptureSnapshot(),poseKernel:kernel,play:play,animators:runtime.Animators,rootMotion:runtime.Characters);using var target=renderer.CreateViewTarget(256,256);var bytes=new byte[4*48];
                for(int step=0;step<128;step++){bool right=step%2==0;runtime.Animators!.SetFloat(id,x.Id,right?.2:.8);runtime.Animators.SetFloat(id,y.Id,right?.3:.1);Check(play.Step().State==PlayState.Paused,"Actual space committed quantum: "+play.Fault?.Code);var debug=runtime.Animators.ReadDebug(id);Check(debug.Instructions.Count==6&&debug.Instructions[^1].Operation==AnimationPoseOperation.RootSource,"Bounded three-sample recipe");
                    var primary=debug.Instructions[debug.Instructions[^1].SourceB];Check(primary.ClipId==m.Clips[right?0:1],"Maximum weight/UUID primary resource");foreach(var row in debug.Instructions.Where(r=>r.Operation==AnimationPoseOperation.Clip))Check(Math.Abs(row.Current/row.Duration-(step+1)*.125)<1e-10,"Same committed normalized phase");
                    int expectedEvents=step%8 is 0 or 7?1:0;Check(debug.Events.Count==expectedEvents&&debug.Events.All(e=>e.ClipId==primary.ClipId&&e.Context.Tick==play.Tick),"Single primary target receipt, no catch-up/repeated source");
                    if(root){var movement=runtime.Characters!.InspectRootMotion(id);Check(Math.Abs(movement.DesiredDisplacement.X-(right?.125f:-.25f))<1e-5&&movement.Tick==play.Tick,"Primary-only root intent through unique Jolt");}
                    if(step is not (0 or 1 or 7 or 31 or 127))continue;Check(scene.Submit(frame++,256,256,camera,target:target),"Actual space GPU submission");renderer.Present();scene.CaptureCharacterVertices(id,bytes);var actual=System.Runtime.InteropServices.MemoryMarshal.Cast<byte,float>(bytes);double phase=((step+1)*.125)%1;float rightWeight=right?.8f:.2f;float expectedX=(float)((rightWeight-2*(1-rightWeight))*phase),yaw=(float)(.2+.6*phase*rightWeight);
                    for(int v=0;v<4;v++){var position=model.Meshes[0].Vertices[v].Position;var expected=position*.7f+Vector3.Transform(position,Matrix4x4.CreateRotationY(yaw-.2f))*.3f+new Vector3(root?0:expectedX,0,0);double error=Vector3.Distance(expected,new(actual[v*12],actual[v*12+1],actual[v*12+2]));maxError=Math.Max(maxError,error);Check(error<1e-4,"Independent affine TRS / GPU / desired mixed-pose root strip oracle: "+error);}
                    Check(scene.Costs.GeometryDraws==1&&scene.Costs.ShadowDraws==1,"Shared native skin palette and shadow");
                }
                // Presentation binds the exact World identity; retire it before frozen-startup Reload.
                scene.Dispose();ulong tick=play.Tick;Guid instance=runtime.Animators!.ReadFrame(id).InstanceId;play.Reload(_=>throw new Exception("No behaviours"));Check(play.Tick==tick&&runtime.Animators.ReadFrame(id).InstanceId!=instance,"Reload resets same frozen startup phase without chronology migration");play.Pause();Check(play.Step().State==PlayState.Paused,"Reload first committed quantum: "+play.Fault);Check(runtime.Animators.ReadDebug(id).Instructions.Where(r=>r.Operation==AnimationPoseOperation.Clip).All(r=>r.Previous==0),"Reload source phase restarts");
            }finally{play.Stop();}
            Check(kernel.Statistics.Rigs==0&&kernel.Statistics.Clips==0&&renderer.SkinStats.Meshes==0&&physics.Inspect().Worlds==0,"Space close baseline");
        }
        foreach(int count in new[]{0,1,8,32}){var d=Document(count,true);using var assets=SceneAssetPreparation.Prepare(f.Root,f.Project,d.CaptureSnapshot(),true);using var play=new PlaySession(d,FrameTimePolicy.Strict,fixedDeltaSeconds:.125,advanceMode:PlayAdvanceMode.FixedSteps);using var runtime=ScenePlayRuntime.Compose(play,physics,assets);play.Start(_=>throw new Exception("No behaviours"));try{for(int step=0;step<4;step++)Check(play.AdvanceFixedStep().State==PlayState.Running,"Bounded Headless space actors");foreach(var actor in d.World.GetObjects().Where(o=>o.Has<AnimatorData>()))Check(runtime.Animators!.ReadFrame(actor.PersistentId).Context.Tick==4&&Math.Abs(runtime.Characters!.InspectRootMotion(actor.PersistentId).DesiredDisplacement.X-.125)<1e-5,"Shared group metadata never leaks instance phase/root");Check(kernel.Statistics.Rigs==0,"Headless creates no native pose");}finally{play.Stop();}}
        var packedDocument=Document(1,false);byte[] package=SceneAssetPreparation.CreateRuntimePackage(f.Root,f.Project,packedDocument.CaptureSnapshot(),[]);
        string moved=Path.Combine(output,"blendspace-moved",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(moved,"assets"));File.WriteAllBytes(Path.Combine(moved,"assets/game.ncpak"),package);
        using(var packed=SceneAssetPreparation.Prepare(moved,f.Project,packedDocument.CaptureSnapshot(),true,"assets/game.ncpak"))Check(((RuntimeAnimationGraphAsset)packed.Assets.Require(graph.AssetId,AssetKind.AnimationGraph)).CopyData().SequenceEqual(AnimationGraphCodec.Encode(graph)),"NCP1 explicit route retains exact strict v3 space and sample UUIDs");
        SceneDocumentFiles.Save(packedDocument,Path.Combine(moved,"start.ncmascene"));string repository=Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(native)))!;File.Copy(Path.Combine(repository,"out/managed/Ncma.Gameplay.Sample.dll"),Path.Combine(moved,"gameplay.dll"));
        var config=new Ncma.Application.ProjectConfiguration(1,f.Project,"Packed BlendSpace","start.ncmascene","gameplay.dll","Direct3D11",[],AssetPackage:"assets/game.ncpak");string project=Path.Combine(moved,"game.ncmaproject");File.WriteAllBytes(project,JsonSerializer.SerializeToUtf8Bytes(config,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        var report=Ncma.Player.App.PlayerRunner.Run(Ncma.Player.App.PlayerOptions.Parse(["--project",project,"--headless","--ticks","8","--report",Path.Combine(moved,"result.json")]),pluginRoot:Path.Combine(moved,"no-native-plugins"),visible:false);
        Check(report.ExitCode==0&&report.Tick==8&&report.Modules.Length==0&&report.ShutdownErrors.Length==0,"Formal source-free strict v3 BlendSpace Headless Player: "+report.Reason);
        TestBlendSpaceInterruptionSource(f,graph,node,end);
        Check(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"Space API validation");File.WriteAllText(Path.Combine(output,"blendspace-scene-results.json"),JsonSerializer.Serialize(new{schema=1,realNca=true,unequalDurations=true,phaseCycles=16,primaryEvents=true,rootPrimary=true,mixedPoseRootStrip=true,uniqueJolt=true,gpuMaxError=maxError,headlessActors=new[]{0,1,8,32},reload=true,manualAccepted=false}));
        Console.WriteLine("PASS M6.6-B actual NCA unequal-duration space/primary events/root/unique Jolt/shared skin-shadow/TRS GPU oracle/128 quanta/Headless32/reload");
    }

    private static void TestBlendSpaceInterruptionSource(SkinFixture f,AnimationGraphDefinition graph,AnimationGraphNode space,AnimationGraphNode output)
    {
        var flag=new AnimationParameter(Guid.NewGuid(),"Switch",AnimationParameterKind.Bool,0,0,false);
        var clip=AnimationGraphNode.Create(Guid.NewGuid(),"Right",AnimationNodeKind.Clip) with{ClipId=f.Manifest.Clips[0],Loop=true,Speed=1};
        var machine=AnimationGraphNode.Create(Guid.NewGuid(),"Machine",AnimationNodeKind.StateMachine);
        var a=new AnimationGraphState(Guid.NewGuid(),"Right",clip.Id);var b=new AnimationGraphState(Guid.NewGuid(),"Space",space.Id);
        graph=graph with{InterruptTransitions=true,EntryState=a.Id,Parameters=[..graph.Parameters,flag],Nodes=[clip,space,machine,output],Links=[new(Guid.NewGuid(),machine.Id,"pose",output.Id,"pose")],States=[a,b],Transitions=[new(Guid.NewGuid(),a.Id,b.Id,0,1,null,[new(flag.Id,AnimationComparison.Equal,0,0,true)]),new(Guid.NewGuid(),b.Id,a.Id,0,1,null,[new(flag.Id,AnimationComparison.Equal,0,0,false)])]};
        File.WriteAllBytes(Path.Combine(f.Root,"assets/space.ncmaanim"),AnimationGraphCodec.Encode(graph));
        var d=new SceneDocument("Prepared source",CharacterComponents.Register(RenderComponentRegistry.CreateRegistry()),CharacterComponents.RequireComposition);
        var o=d.World.CreateObject("Actor");o.Set(TransformData.Identity);o.Set(new SkinnedMeshData(f.Model,f.Manifest.Meshes[0].Mesh,graph.SkeletonId,f.Manifest.Meshes[0].Materials,true,true,uint.MaxValue));o.Set(new AnimatorData(graph.AssetId,graph.SkeletonId));
        using var assets=SceneAssetPreparation.Prepare(f.Root,f.Project,d.CaptureSnapshot(),true);
        var program=((RuntimeAnimationGraphAsset)assets.Assets.Require(graph.AssetId,AssetKind.AnimationGraph)).PrepareProgram(assets.Assets);
        var source=GraphPoseSnapshotPreparation.Prepare(program,assets.Assets);var instance=new AnimationGraphInstance(program,new(Guid.NewGuid(),Guid.NewGuid(),0),source);
        var frozen=new AnimationLocalTransform[2];float expectedX=0,expectedYaw=.2f;
        for(int step=0;step<128;step++){
            instance.SetBool(flag.Id,step%2==0);float previousX=expectedX,previousYaw=expectedYaw;
            var token=instance.Prepare(instance.Frame.Context,.125);
            if(step==2){instance.Abort(token);Check(instance.Frame.FrozenPoseGeneration==1&&instance.Frame.Context.Tick==2,"Aborted space cache does not consume committed generation/phase");token=instance.Prepare(instance.Frame.Context,.125);}
            instance.Commit(token,instance.Frame.Context with{Tick=instance.Frame.Context.Tick+1});
            if(step==0){expectedX=.125f*(.875f+.4f*.125f);expectedYaw=.2f+.075f*(.875f+.8f*.125f);}else{expectedX=previousX+((step%2==0?.05f:.125f)-previousX)*.125f;expectedYaw=previousYaw+((step%2==0?.26f:.275f)-previousYaw)*.125f;}
            if(step>0){instance.CopyFrozenPose((ulong)step,frozen);Check(Math.Abs(frozen[0].Position.X-previousX)<1e-5&&Math.Abs(2*Math.Atan2(frozen[1].Rotation.Y,frozen[1].Rotation.W)-previousYaw)<1e-5,"Actual unequal-duration space freezes complete committed mixed pose, not primary root pose");}
            var debug=instance.ReadDebug(instance.Frame.Context);Check(debug.Instructions.Count<=8&&debug.Events.Count==1,"Space interruption bounded depth and target-primary event only");
        }
        var plan=instance.ReadDebug(instance.Frame.Context).Instructions.ToArray();var untouched=new[]{new AnimationLocalTransform(new(7,8,9),Quaternion.Identity,1),new AnimationLocalTransform(new(7,8,9),Quaternion.Identity,1)};
        var bad=(AnimationPoseInstruction[])plan.Clone();bad[^1]=bad[^1] with{Weight=float.NaN};Reject(()=>source.Evaluate(bad,instance.Frame.Output,frozen,instance.Frame.FrozenPoseGeneration,untouched));Check(untouched.All(v=>v.Position==new Vector3(7,8,9)),"Invalid whole pose leaves copied destination unchanged");
    }
}
