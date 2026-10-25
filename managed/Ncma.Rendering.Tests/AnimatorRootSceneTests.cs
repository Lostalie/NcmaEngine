using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using InputState = Ncma.InputState;
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
using Ncma.Editor.Services;
using Ncma.Editor.App;

internal static unsafe partial class Program
{
    private static void TestAnimatorRootScenes(RendererSession renderer,ref ulong frame,string native,string output)
    {
        var f=new SkinFixture(output);var graph=AnimatorGraph(f);File.WriteAllBytes(Path.Combine(f.Root,"assets/root.ncmaanim"),AnimationGraphCodec.Encode(graph));
        SceneDocument Document(int count=1,bool wall=true) {
            var d=new SceneDocument("Graph root",global::Ncma.Runtime.CompositionRegistry.Build(global::Ncma.Characters.Generated.ComponentModule.Module, global::Ncma.Rendering.Generated.ComponentModule.Module),CharacterComponents.RequireComposition);
            for(int i=0;i<count;i++) {
                Guid obj=d.CreateObject("Graph root actor");d.SetComponent(obj, TransformData.Identity with{Position=new(0,0,i*3)});
                d.SetComponent(obj, new SkinnedMeshData(f.Model,f.Manifest.Meshes[0].Mesh,graph.SkeletonId,f.Manifest.Meshes[0].Materials,true,true,uint.MaxValue));
                d.SetComponent(obj, new AnimatorData(graph.AssetId,graph.SkeletonId));d.SetComponent(obj, new RootMotionData(0));d.SetComponent(obj, CharacterData.Default with{Controlled=i==0});
            }
            Guid floor=d.CreateObject("Ground");d.SetComponent(floor, TransformData.Identity with{Position=new(0,-.2f,0)});d.SetComponent(floor, new BoxColliderData(100,.2f,100,1000,1,false));
            if(wall){Guid block=d.CreateObject("Wall");d.SetComponent(block, TransformData.Identity with{Position=new(.8f,1,0)});d.SetComponent(block, new BoxColliderData(.1f,1,100,1000,1,false));}
            Guid camera=d.CreateObject("Camera");d.SetComponent(camera, TransformData.Identity with{Position=new(0,0,5)});d.SetComponent(camera, CameraData.Default with{Projection=CameraProjection.Orthographic,OrthographicHeight=4});
            Guid light=d.CreateObject("Light");d.SetComponent(light, TransformData.Identity with{Rotation=Quaternion.CreateFromYawPitchRoll(.3f,-.5f,0)});d.SetComponent(light, DirectionalLightData.Default);
            return d;
        }
        var document=Document();using var assets=SceneAssetPreparation.Prepare(f.Root,f.Project,document.CaptureSnapshot(),true);
        string plugins=Path.Combine(native,"m2/plugins");using var physics=new PhysicsService(plugins,characterSupport:true);
        var outcomes=new List<Vector3>();
        foreach(int hz in new[]{30,60,144,0}) {
            var d=document.CreateIsolatedCopy();Guid id=d.GetObjectIds().Single(o=>d.HasComponent<AnimatorData>(o));
            using var play=new PlaySession(d.BakeRuntime(d.Revision).Image.Instantiate(d.Components),FrameTimePolicy.Strict,advanceMode:hz==0?PlayAdvanceMode.FixedSteps:PlayAdvanceMode.Frames);
            using var runtime=ScenePlayRuntime.Compose(play,physics,assets);play.Start(_=>throw new Exception("No behaviours"));
            var held=new ulong[InputState.WordCount];held[65/64]=1UL<<(65%64);
            play.SubmitInput(new(play.SessionId,1,true,held,new ulong[InputState.WordCount],new ulong[InputState.WordCount])); // A is held; graph root moves right.
            for(int i=0;i<(hz==0?120:hz*2);i++)Check((hz==0?play.AdvanceFixedStep():play.AdvanceFrame(1d/hz)).State==PlayState.Running,"Graph/Jolt quantum failed: "+play.Fault?.Code);
            var position=play.Scene.World.Get<TransformData>(play.Scene.World.FindEntity(id)).Position;var root=runtime.Characters!.InspectRootMotion(id);
            Check(play.Tick==120&&position.X>.3f&&position.X<.41f&&Math.Abs(position.Z)<.001f,"Root input replacement/wall collision: "+position);
            Check(root.Tick==play.Tick&&root.DesiredDisplacement.X>.008f&&root.AcceptedDisplacement.X<.001f,"Graph desired and collided movement separated");
            var current=runtime.Animators!.ReadFrame(id);Check(current.Context.Tick==120&&runtime.Characters.Status.CommittedSequence==120,"One graph clock and one numerical step");
            outcomes.Add(position);play.Pause();var session=play.SessionId;var world=play.Scene.World.Identity;var instance=current.InstanceId;
            runtime.Animators.SetFloat(id,graph.Parameters[0].Id,1);Check(play.Step().State==PlayState.Paused,"Safe-boundary graph control");
            Check(runtime.Characters.InspectRootMotion(id).DesiredDisplacement.X<-.016f,"Prepared parameter affects current root quantum, not previous committed recipe");
            play.Reload(_=>throw new Exception("No behaviours"));Check(play.State==PlayState.Paused&&play.SessionId!=session&&play.Scene.World.Identity!=world&&runtime.Animators.ReadFrame(id).InstanceId!=instance&&play.Tick==121,"Graph/Character frozen-startup reload");
            Check(runtime.Characters.InspectRootMotion(id).DesiredDisplacement==Vector3.Zero,"Reload root observations reset");
            Check(play.Step().State==PlayState.Paused&&runtime.Animators.ReadFrame(id).Context.Tick==122,"Reload advances only new successful quantum");play.Stop();
            Reject(()=>runtime.Animators.ReadFrame(id));Check(physics.Inspect().Worlds==0,"Graph root numerical close baseline");
        }
        Check(outcomes.All(p=>Vector3.Distance(p,outcomes[0])<1e-5f),"Graph root render schedules/headless parity");
        foreach(int mutation in new[]{0,1,2}) {
            var d=document.CreateIsolatedCopy();var id=d.GetObjectIds().Single(o=>d.HasComponent<AnimatorData>(o));
            using var play=new PlaySession(d.BakeRuntime(d.Revision).Image.Instantiate(d.Components),FrameTimePolicy.Strict,advanceMode:PlayAdvanceMode.FixedSteps);using var runtime=ScenePlayRuntime.Compose(play,physics,assets);
            play.AddSystem(new GraphFailure(w=>{try {global::Ncma.Runtime.Entity actor=w.FindEntity(id);if(mutation==0)w.Set(actor, new RootMotionData(1));else if(mutation==1)w.Remove<RootMotionData>(actor);else w.Set(actor, w.Get<SkinnedMeshData>(actor) with{CastShadow=false});}catch(InvalidOperationException){}}));
            byte[] before=play.Scene.CaptureCheckpoint().CopyBytes();play.Start(_=>throw new Exception("No behaviours"));try {
                Check(play.AdvanceFixedStep().State==PlayState.Faulted&&play.Tick==0&&runtime.Characters!.Status.CommittedSequence==0,"Caught graph root/binding mutation must poison before numerical execution");
                Check(before.SequenceEqual(play.Scene.CaptureCheckpoint().CopyBytes()),"Graph root membership/binding rejection preserves committed World");
            }finally{play.Stop();}
        }
        foreach(bool late in new[]{false,true}) {
            var d=document.CreateIsolatedCopy();Guid id=d.GetObjectIds().Single(o=>d.HasComponent<AnimatorData>(o));
            using var play=new PlaySession(d.BakeRuntime(d.Revision).Image.Instantiate(d.Components),FrameTimePolicy.Strict,advanceMode:PlayAdvanceMode.FixedSteps);using var runtime=ScenePlayRuntime.Compose(play,physics,assets);
            play.AddSystem(new GraphFailure(w=>{if(late){var created=play.Commands.SpawnEmpty("injected late");play.Commands.AttachBehaviour(created.ObjectId,new(Guid.NewGuid(),"Missing",true,[]));}
                else {try{runtime.Animators!.SetFloat(id,graph.Parameters[0].Id,1);}catch(InvalidOperationException){}}}));
            var before=play.Scene.CaptureCheckpoint().CopyBytes();play.Start(_=>throw new Exception("Injected factory"));Check(play.AdvanceFixedStep().State==PlayState.Faulted&&play.Tick==0,"Graph root failed step must not commit");
            Check(runtime.Characters!.Status.NumericalExecutionStarted==late&&before.SequenceEqual(play.Scene.CaptureCheckpoint().CopyBytes()),"Graph root pre/post solver fail-stop classification");
            Reject(()=>runtime.Animators!.ReadFrame(id));Reject(()=>runtime.Characters.InspectRootMotion(id));play.Stop();
        }
        foreach(int count in new[]{0,8,32,33}) {
            var d=Document(count,wall:false);if(count==33){Reject(d.ValidateAuthoring);continue;}
            using var prepared=SceneAssetPreparation.Prepare(f.Root,f.Project,d.CaptureSnapshot(),true);using var play=new PlaySession(d.BakeRuntime(d.Revision).Image.Instantiate(d.Components),FrameTimePolicy.Strict,advanceMode:PlayAdvanceMode.FixedSteps);
            using var runtime=ScenePlayRuntime.Compose(play,physics,prepared);play.Start(_=>throw new Exception("No behaviours"));Check(play.AdvanceFixedStep().State==PlayState.Running,"Bounded graph root actors");
            foreach(global::Ncma.Runtime.Entity actor in play.Scene.World.GetEntities().Where(o=>play.Scene.World.Has<AnimatorData>(o)))Check(runtime.Animators!.ReadFrame(play.Scene.World.GetUuid(actor)).Context.Tick==1&&Math.Abs(runtime.Characters!.InspectRootMotion(play.Scene.World.GetUuid(actor)).DesiredDisplacement.X-1f/120)<1e-5f,"Shared graph instance root isolation");
            play.Stop();Check(physics.Inspect().Worlds==0,"Many graph root close baseline");
        }
        // State transition blends feed THIS pending output, including target reentry; no extra root clock.
        var switchParameter=new AnimationParameter(Guid.NewGuid(),"Switch",AnimationParameterKind.Bool,0,0,false);
        var machine=AnimationGraphNode.Create(Guid.NewGuid(),"Machine",AnimationNodeKind.StateMachine);
        var stateA=new AnimationGraphState(Guid.NewGuid(),"Right",graph.Nodes[0].Id);var stateB=new AnimationGraphState(Guid.NewGuid(),"Left",graph.Nodes[1].Id);
        var stateGraph=graph with{AssetId=Guid.NewGuid(),EntryState=stateA.Id,Parameters=[switchParameter],Nodes=[graph.Nodes[0],graph.Nodes[1],machine,graph.Nodes[4]],
            Links=[new(Guid.NewGuid(),machine.Id,"pose",graph.Nodes[4].Id,"pose")],States=[stateA,stateB],
            Transitions=[new(Guid.NewGuid(),stateA.Id,stateB.Id,0,.2,null,[new(switchParameter.Id,AnimationComparison.Equal,0,0,true)]),new(Guid.NewGuid(),stateB.Id,stateA.Id,0,.2,null,[new(switchParameter.Id,AnimationComparison.Equal,0,0,false)])]};
        File.WriteAllBytes(Path.Combine(f.Root,"assets/state-root.ncmaanim"),AnimationGraphCodec.Encode(stateGraph));
        var transitionDocument=Document(wall:false);var transitionActor=transitionDocument.GetObjectIds().Single(o=>transitionDocument.HasComponent<AnimatorData>(o));
        transitionDocument.SetComponent(transitionActor, new AnimatorData(stateGraph.AssetId,stateGraph.SkeletonId));
        using(var prepared=SceneAssetPreparation.Prepare(f.Root,f.Project,transitionDocument.CaptureSnapshot(),true))using(var play=new PlaySession(transitionDocument.BakeRuntime(transitionDocument.Revision).Image.Instantiate(transitionDocument.Components),FrameTimePolicy.Strict,fixedDeltaSeconds:.1,advanceMode:PlayAdvanceMode.FixedSteps))using(var runtime=ScenePlayRuntime.Compose(play,physics,prepared)) {
            play.Start(_=>throw new Exception("No behaviours"));try {
                runtime.Animators!.SetBool(transitionActor,switchParameter.Id,true);Check(play.AdvanceFixedStep().State==PlayState.Running,"First transition root quantum");
                Check(Math.Abs(runtime.Characters!.InspectRootMotion(transitionActor).DesiredDisplacement.X)<1e-5f,"Transition .5 root delta oracle");
                Check(play.AdvanceFixedStep().State==PlayState.Running&&Math.Abs(runtime.Characters.InspectRootMotion(transitionActor).DesiredDisplacement.X+.1f)<1e-5f,"Transition endpoint root delta oracle");
                runtime.Animators.SetBool(transitionActor,switchParameter.Id,false);Check(play.AdvanceFixedStep().State==PlayState.Running&&Math.Abs(runtime.Characters.InspectRootMotion(transitionActor).DesiredDisplacement.X)<1e-5f,"State reentry root interval restarts");
                Check(play.AdvanceFixedStep().State==PlayState.Running&&Math.Abs(runtime.Characters.InspectRootMotion(transitionActor).DesiredDisplacement.X-.1f)<1e-5f,"Reentry endpoint root oracle");
            }finally{play.Stop();}
        }
        string path=Path.Combine(native,"NcmaAnimationKernel.dll");using var kernel=new PoseKernel(path,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),blendSupport:true);
        double maxError=0;
        var oracleRig=ModelPayloadCodec.DecodeSkeleton(((RuntimeDataAsset)assets.Assets.Require(graph.SkeletonId,AssetKind.Skeleton)).CopyData());
        var oracleA=ModelPayloadCodec.DecodeClip(((RuntimeDataAsset)assets.Assets.Require(graph.Nodes[0].ClipId,AssetKind.Clip)).CopyData());
        var oracleB=ModelPayloadCodec.DecodeClip(((RuntimeDataAsset)assets.Assets.Require(graph.Nodes[1].ClipId,AssetKind.Clip)).CopyData());
        var oracleMesh=assets.Assets.RequireMesh(f.Manifest.Meshes[0].Mesh,AssetKind.SkinnedMesh).CopyPayload();
        Vector3[] Oracle(double time) {
            ImportTransform At(ClipPayload c,int bone) {
                var track=c.Clip.Tracks.SingleOrDefault(t=>t.Bone==bone);if(track is null)return oracleRig.Bones[bone].BindLocal;
                var keys=track.Keys;int hi=1;while(hi<keys.Length-1&&keys[hi].Time<time)hi++;var a=keys[hi-1];var b=keys[hi];float weight=(float)((time-a.Time)/(b.Time-a.Time));
                return new(Vector3.Lerp(a.Value.Position,b.Value.Position,weight),Quaternion.Slerp(a.Value.Rotation,b.Value.Rotation,weight),Vector3.Lerp(a.Value.Scale,b.Value.Scale,weight));
            }
            var models=new Matrix4x4[oracleRig.Bones.Length];
            for(int b=0;b<models.Length;b++){var a=At(oracleA,b);var other=At(oracleB,b);var local=new ImportTransform(Vector3.Lerp(a.Position,other.Position,.25f),Quaternion.Slerp(a.Rotation,other.Rotation,.25f),Vector3.Lerp(a.Scale,other.Scale,.25f));models[b]=Matrix4x4.CreateScale(local.Scale)*Matrix4x4.CreateFromQuaternion(local.Rotation)*Matrix4x4.CreateTranslation(local.Position);if(oracleRig.Bones[b].Parent>=0)models[b]*=models[oracleRig.Bones[b].Parent];}
            Check(Matrix4x4.Decompose(models[0],out _,out var yaw,out var p),"Independent root decomposition");
            Check(Matrix4x4.Invert(Matrix4x4.CreateFromQuaternion(yaw)*Matrix4x4.CreateTranslation(p.X,0,p.Z),out var correction),"Independent root correction");
            Matrix4x4 Binding(float[] v)=>new(v[0],v[1],v[2],v[3],v[4],v[5],v[6],v[7],v[8],v[9],v[10],v[11],v[12],v[13],v[14],v[15]);
            var palette=oracleMesh.Bindings.Select(b=>Binding(b.GeometryToBone)*models[b.Bone]*correction).ToArray();
            return oracleMesh.Vertices.Select(v=>Vector3.Transform(v.Position,palette[v.Joints.X])*v.Weights.X+Vector3.Transform(v.Position,palette[v.Joints.Y])*v.Weights.Y).ToArray();
        }
        for(int cycle=0;cycle<8;cycle++) {
            var d=document.CreateIsolatedCopy();Guid id=d.GetObjectIds().Single(o=>d.HasComponent<AnimatorData>(o)),camera=d.GetObjectIds().Single(o=>d.HasComponent<CameraData>(o));
            using var play=new PlaySession(d.BakeRuntime(d.Revision).Image.Instantiate(d.Components),FrameTimePolicy.Strict,advanceMode:PlayAdvanceMode.FixedSteps);using var runtime=ScenePlayRuntime.Compose(play,physics,assets);play.Start(_=>throw new Exception("No behaviours"));play.Pause();
            try{using(var cache=new RenderResourceCache(renderer))using(var scene=new SceneRenderSession(renderer,cache,play.Scene.World))using(var target=renderer.CreateViewTarget(256,256)) {scene.Prepare(assets,play.Scene.CaptureSnapshot(),poseKernel:kernel,play:play,rootMotion:runtime.Characters,animators:runtime.Animators);
                var captured=new byte[4*48];
                for(int step=0;step<120;step++) {
                    Check(play.Step().State==PlayState.Paused,"Actual graph/skin root quantum");
                    if(step%20!=19)continue;
                    Check(scene.Submit(frame++,256,256,camera,target:target),"Actual graph geometry submit");renderer.Present();scene.CaptureCharacterVertices(id,captured);
                    // Use the committed graph interval, not tick/60 rounded to an exact discontinuous loop endpoint.
                    var interval=runtime.Animators!.ReadDebug(id).Instructions.First(r=>r.ClipId==graph.Nodes[0].ClipId);
                    double oracleTime=interval.Current%interval.Duration;var expectedVertices=Oracle(oracleTime);
                    var floats=System.Runtime.InteropServices.MemoryMarshal.Cast<byte,float>(captured);
                    for(int v=0;v<4;v++) {
                        var expected=expectedVertices[v];
                        var actual=new Vector3(floats[v*12],floats[v*12+1],floats[v*12+2]);double error=Vector3.Distance(actual,expected);maxError=Math.Max(maxError,error);
                        Check(error<1e-4,$"Independent graph root stripped GPU skin oracle: error={error} tick={play.Tick} vertex={v} actual={actual} expected={expected} time={oracleTime}");
                    }
                    Check(scene.Costs.GeometryDraws==1&&scene.Costs.ShadowDraws==1,"Shared exact-frame geometry/shadow skin");
                    ulong tick=play.Tick;scene.Animation!.Evaluate(.2f,false);scene.Animation.Evaluate(.8f,false);Check(play.Tick==tick,"Presentation cannot move graph root actor");
                }
            }}finally{play.Stop();}
            Check(kernel.Statistics.Rigs==0&&kernel.Statistics.Clips==0&&renderer.SkinStats.Meshes==0&&physics.Inspect().Worlds==0,"Graph root GPU/pose/physics baseline");
        }
        Check(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"Graph root DX11 validation");
        foreach(int count in new[]{0,8,32}) {
            var d=Document(count,wall:false);int index=0;foreach(Guid actor in d.GetObjectIds().Where(o=>d.HasComponent<AnimatorData>(o))){d.SetComponent(actor, TransformData.Identity with{Position=new((index%8-3.5f)*2,0,-(index/8)*2)});index++;}
            Guid camera=d.GetObjectIds().Single(o=>d.HasComponent<CameraData>(o));d.SetComponent(camera, CameraData.Default with{Projection=CameraProjection.Orthographic,OrthographicHeight=24});
            using var prepared=SceneAssetPreparation.Prepare(f.Root,f.Project,d.CaptureSnapshot(),true);using var play=new PlaySession(d.BakeRuntime(d.Revision).Image.Instantiate(d.Components),FrameTimePolicy.Strict,advanceMode:PlayAdvanceMode.FixedSteps);using var runtime=ScenePlayRuntime.Compose(play,physics,prepared);
            play.Start(_=>throw new Exception("No behaviours"));try {
                Check(play.AdvanceFixedStep().State==PlayState.Running,"0/8/32 graph GPU quantum");
                using var cache=new RenderResourceCache(renderer);using var scene=new SceneRenderSession(renderer,cache,play.Scene.World);scene.Prepare(prepared,play.Scene.CaptureSnapshot(),poseKernel:kernel,play:play,rootMotion:runtime.Characters,animators:runtime.Animators);using var target=renderer.CreateViewTarget(256,256);
                bool submitted=scene.Submit(frame++,256,256,camera,target:target);if(submitted)renderer.Present();
                Check(submitted==(count>0)&&scene.Costs.GeometryDraws==count&&scene.Costs.ShadowDraws==count,"Actual 0/8/32 graph skin geometry/shadow count");
                var stamp=scene.ReadAnimationPresentation();Check(count==0?stamp is null:stamp is {} p&&p.Tick==play.Tick&&p.PublicationId==prepared.Assets.Identity&&p.PoseGeneration>0,"Copied exact submission identity");
                if(count>0){Check(play.AdvanceFixedStep().State==PlayState.Running,"Advance invalidates submitted pose tick");Check(scene.ReadAnimationPresentation() is null,"Cannot label last tick skin submission current");}
            }finally{play.Stop();}
            Check(kernel.Statistics.Rigs==0&&kernel.Statistics.Clips==0&&renderer.SkinStats.Meshes==0&&physics.Inspect().Worlds==0,"Joint count resources drain");
        }
        var slopeDocument=Document(wall:false);var slopeActor=slopeDocument.GetObjectIds().Single(o=>slopeDocument.HasComponent<AnimatorData>(o));
        Guid floorObject=slopeDocument.GetObjectIds().Single(o=>slopeDocument.GetObjectName(o)=="Ground");slopeDocument.SetComponent(floorObject, slopeDocument.GetComponent<TransformData>(floorObject) with{Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,.1f)});
        using(var prepared=SceneAssetPreparation.Prepare(f.Root,f.Project,slopeDocument.CaptureSnapshot(),true))using(var play=new PlaySession(slopeDocument.BakeRuntime(slopeDocument.Revision).Image.Instantiate(slopeDocument.Components),FrameTimePolicy.Strict,advanceMode:PlayAdvanceMode.FixedSteps))using(var runtime=ScenePlayRuntime.Compose(play,physics,prepared)) {
            play.Start(_=>throw new Exception("No behaviours"));try {
                play.SubmitInput(new(play.SessionId,1,true,new ulong[InputState.WordCount],new ulong[InputState.WordCount],new ulong[InputState.WordCount]));
                for(int i=0;i<30;i++)Check(play.AdvanceFixedStep().State==PlayState.Running,"Graph slope root quantum");
                Check(runtime.Characters!.ReadDebugFrame().Characters.Single().Ground=="Ground","Graph root ground support on slope");
                var pressed=new ulong[InputState.WordCount];pressed[0]=1UL<<32;play.SubmitInput(new(play.SessionId,2,true,new ulong[InputState.WordCount],pressed,new ulong[InputState.WordCount]));
                float startHeight=play.Scene.World.Get<TransformData>(play.Scene.World.FindEntity(slopeActor)).Position.Y,maximum=0,firstHeight=0;
                for(int i=0;i<60;i++){Check(play.AdvanceFixedStep().State==PlayState.Running,"Graph root jumping quantum");float height=play.Scene.World.Get<TransformData>(play.Scene.World.FindEntity(slopeActor)).Position.Y;if(i==0)firstHeight=height;maximum=Math.Max(maximum,height);}
                Check(firstHeight>startHeight+.04f&&maximum>startHeight+.8f,$"Graph horizontal root must preserve Character jump/gravity authority: start={startHeight} first={firstHeight} max={maximum}");
            }finally{play.Stop();}
        }
        // Exact current numerical desired yaw reaches the SAME character solver; incompatible anchors reject off-frame.
        foreach(bool invalidAnchor in new[]{false,true}) {
            var source=SyntheticModel();var changed=source with{Clips=source.Clips.Select((c,index)=>c with{Tracks=c.Tracks.Select(t=>t.Bone==0?t with{Keys=t.Keys.Select((k,key)=>k with{Value=k.Value with{Position=invalidAnchor&&index==1&&key==0?new(.1f,0,0):k.Value.Position,Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,key==0?0:(index==0?.6f:-.6f))}}).ToArray()}:t).ToArray()}).ToArray()};
            var twist=new SkinFixture(output,changed);var twistGraph=AnimatorGraph(twist);File.WriteAllBytes(Path.Combine(twist.Root,"assets/twist.ncmaanim"),AnimationGraphCodec.Encode(twistGraph));
            var d=Document(wall:false);Guid actor=d.GetObjectIds().Single(o=>d.HasComponent<AnimatorData>(o));d.SetComponent(actor, new SkinnedMeshData(twist.Model,twist.Manifest.Meshes[0].Mesh,twistGraph.SkeletonId,twist.Manifest.Meshes[0].Materials,true,true,uint.MaxValue));d.SetComponent(actor, new AnimatorData(twistGraph.AssetId,twistGraph.SkeletonId));
            using var prepared=SceneAssetPreparation.Prepare(twist.Root,twist.Project,d.CaptureSnapshot(),true);using var play=new PlaySession(d.BakeRuntime(d.Revision).Image.Instantiate(d.Components),FrameTimePolicy.Strict,advanceMode:PlayAdvanceMode.FixedSteps);
            if(invalidAnchor){Reject(()=>ScenePlayRuntime.Compose(play,physics,prepared));Check(physics.Inspect().Worlds==0,"Rejected anchor must not start solver");continue;}
            using var runtime=ScenePlayRuntime.Compose(play,physics,prepared);play.Start(_=>throw new Exception("No behaviours"));try {
                for(int i=0;i<120;i++){Check(play.AdvanceFixedStep().State==PlayState.Running,"Graph yaw quantum");Check(Math.Abs(runtime.Characters!.InspectRootMotion(actor).DesiredYaw-.005f)<1e-5f,"Independent shortest yaw blend interval oracle");}
                var rotation=play.Scene.World.Get<TransformData>(play.Scene.World.FindEntity(actor)).Rotation;Check(Math.Abs(2*Math.Atan2(rotation.Y,rotation.W)-.6)<1e-4,"Graph yaw reaches unique numerical movement result");
            }finally{play.Stop();}
        }
        ScenePlayRuntime? editorRuntime=null;SceneRenderSession? editorScene=null;bool failClose=false;
        using(var editor=new EditorSessionOwner("Graph root Editor",components:global::Ncma.Runtime.CompositionRegistry.Build(global::Ncma.Characters.Generated.ComponentModule.Module, global::Ncma.Rendering.Generated.ComponentModule.Module),validateComposition:CharacterComponents.RequireComposition,
            composePreparedPlay:(p,a)=>editorRuntime=ScenePlayRuntime.Compose(p,physics,a),beforePlayStop:()=>{if(failClose)throw new InvalidOperationException("Injected graph root derived close");editorScene?.Dispose();editorScene=null;})) {
            editor.Document.RestoreSnapshot(document.CaptureSnapshot());editor.PrepareRenderAssets(f.Root,f.Project);byte[] edit=editor.Document.CaptureBytes();var editSource=EditorSceneSourceStamp.Read(editor);
            using var cache=new RenderResourceCache(renderer);using var target=renderer.CreateViewTarget(256,256);
            for(int cycle=0;cycle<8;cycle++) {
                var play=editor.StartPlay();play.Pause();var camera=play.Scene.World.GetUuid(play.Scene.World.GetEntities().Single(o=>play.Scene.World.Has<CameraData>(o)));
                editorScene=new(renderer,cache,play.Scene.World);editorScene.Prepare(editor.PlayRenderAssets!,play.Scene.CaptureSnapshot(),poseKernel:kernel,play:play,rootMotion:editorRuntime!.Characters,animators:editorRuntime.Animators);
                Check(play.Step().State==PlayState.Paused&&editorScene.Submit(frame++,256,256,camera,target:target),"Formal Editor graph root scene");renderer.Present();
                if(cycle==0){failClose=true;Reject(editor.StopPlay);Check(editor.Play==play&&editor.PlayRenderAssets is not null&&kernel.Statistics.Rigs==1,"Close failure retains graph/pose/asset ownership");failClose=false;}
                editor.StopPlay();Check(edit.SequenceEqual(editor.Document.CaptureBytes())&&editSource.Matches(editor),"Editor graph roots do not mutate Edit");
                Check(kernel.Statistics.Rigs==0&&kernel.Statistics.Clips==0&&physics.Inspect().Worlds==0,"Editor graph close retry baseline");
            }
        }
        physics.Dispose();
        string repository=Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(native)))!;
        string icon=Path.Combine(AppContext.BaseDirectory,"NcmaEngine.ico");if(!File.Exists(icon))File.Copy(Path.Combine(repository,"engine/build/resources/NcmaEngine.ico"),icon);
        string packageDirectory=Path.Combine(f.Root,"packed-player");Directory.CreateDirectory(Path.Combine(packageDirectory,"assets"));
        File.WriteAllBytes(Path.Combine(packageDirectory,"assets/game.ncpak"),SceneAssetPreparation.CreateRuntimePackage(f.Root,f.Project,document.CaptureSnapshot(),[]));
        SceneDocumentFiles.Save(document,Path.Combine(packageDirectory,"start.ncmascene"));File.Copy(Path.Combine(repository,"out/managed/Ncma.Gameplay.Sample.dll"),Path.Combine(packageDirectory,"gameplay.dll"));
        Guid cameraId=document.GetObjectIds().Single(o=>document.HasComponent<CameraData>(o));
        var configuration=new Ncma.Application.ProjectConfiguration(1,f.Project,"Graph root Player","start.ncmascene","gameplay.dll","Direct3D11",[],true,cameraId,AssetPackage:"assets/game.ncpak");
        string project=Path.Combine(packageDirectory,"root.ncmaproject");File.WriteAllBytes(project,JsonSerializer.SerializeToUtf8Bytes(configuration,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        foreach(bool headless in new[]{true,false}) {

            using var playerRunOwner0 = new Ncma.Player.App.PlayerRunner();
            var flags=new List<string>{"--project",project,"--ticks","8","--report",Path.Combine(packageDirectory,headless?"headless.json":"dx11.json")};if(headless)flags.Add("--headless");
            Ncma.Player.App.PlayerReport report;
            if(headless)report=playerRunOwner0.Run(Ncma.Player.App.PlayerOptions.Parse(flags.ToArray()),pluginRoot:plugins,visible:false);
            else {
                // Platform/Renderer own one context per process. A formal Player must not borrow this test's live device.
                var start=new System.Diagnostics.ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                foreach(string arg in new[]{typeof(Program).Assembly.Location,"--graph-root-player-child",plugins,project,flags[5]})start.ArgumentList.Add(arg);
                using var child=System.Diagnostics.Process.Start(start)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
                Check(child.WaitForExit(30000),"Graph root Player process timeout");Check(child.ExitCode==0,"Graph root Player child: "+stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult());
                var json=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase};json.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                report=JsonSerializer.Deserialize<Ncma.Player.App.PlayerReport>(File.ReadAllBytes(flags[5]),json)!;
            }
            Check(report.ExitCode==0&&report.Tick>=8&&report.ShutdownErrors.Length==0&&report.ValidationErrors==0&&report.ValidationWarnings==0,"Formal source-free graph root Player: "+report.Reason);
            Check(!headless||report.Modules.All(m=>m.Id is not ("ncma.renderer" or "ncma.platform")),"Headless graph root initializes physics, never GPU/pose");
        }
        File.WriteAllText(Path.Combine(output,"animator-root-scene-results.json"),JsonSerializer.Serialize(new{realNca=true,graphRoot=true,oneClock=true,oneSolver=true,renderRates=new[]{30,60,144},headless=true,wall=true,replaceInput=true,actors=new[]{0,1,8,32},actor33Rejected=true,transitionsAndReentry=true,postSolverFailStop=true,reload=true,gpuCycles=8,formalPackedPlayer=true,independentSkinMaxError=maxError,manualAccepted=false}));
        Console.WriteLine("PASS M6.3-C actual pinned NCA graph root/Jolt/wall/input replacement/30-60-144/Headless/32 actors/reload/fail-stop/shared GPU root-strip oracle");
    }
}
