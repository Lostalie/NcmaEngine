using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private sealed record M6Cost(int Characters,int Cycle,ulong Tick,Guid Session,Guid World,Guid Instance,ulong NumericalSequence,
        double SimulationMs,long SimulationBytes,double DiagnosticDrainMs,double DrawMs,long DrawBytes,
        SceneRenderCosts Render,double PoseMs,double PaletteMs,double SkinAbiMs,ulong PaletteBytes,ulong VertexBytes,
        ulong SampleCalls,double? LastValidGpuMs,int[] Gc);
    private sealed class M6Observer(Action action):ICommittedStepObserver {public void StepCommitted(World world,double delta)=>action();}
    private sealed record M6Step(ulong Tick,AnimationPoseInstruction[] Recipe,AnimationLocalTransform[] Pose,
        MontageSlotFrame Slot,Vector3 DesiredRoot,Guid[] Markers);
    private static byte[] RewriteM6Package(byte[] source,Guid? drop=null,Guid? target=null,
        Func<byte[],byte[]>? payload=null,Action<JsonObject>? metadata=null)
    {
        int length=BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(8));
        var table=JsonNode.Parse(source.AsSpan(16,length))!.AsObject();var entries=table["assets"]!.AsArray();
        var resultEntries=new JsonArray();var blocks=new List<byte[]>();int offset=0;
        foreach(var item in entries){var entry=item!.DeepClone().AsObject();Guid id=Guid.Parse(entry["assetId"]!.GetValue<string>());
            if(id==drop)continue;
            byte[] bytes=source.AsSpan(16+length+entry["offset"]!.GetValue<int>(),entry["length"]!.GetValue<int>()).ToArray();
            if(id==target){
                if(payload is not null){bytes=payload(bytes);byte[] hash=SHA256.HashData(bytes);entry["hash"]=Convert.ToHexString(hash);
                    if(entry["encoding"]!.GetValue<string>()=="animgraph"){ulong token=BinaryPrimitives.ReadUInt64LittleEndian(hash);entry["generation"]=token==0?1:token;}}
                metadata?.Invoke(entry);
            }
            entry["offset"]=offset;entry["length"]=bytes.Length;offset+=bytes.Length;blocks.Add(bytes);resultEntries.Add(entry);
        }
        table["assets"]=resultEntries;byte[] index=JsonSerializer.SerializeToUtf8Bytes(table);
        byte[] result=new byte[16+index.Length+offset];source.AsSpan(0,16).CopyTo(result);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8),index.Length);BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(12),offset);
        index.CopyTo(result,16);int at=16+index.Length;foreach(byte[] block in blocks){block.CopyTo(result,at);at+=block.Length;}return result;
    }
    private static void TestM6FinalAcceptance(RendererSession renderer,ref ulong frame,string native,string output,
        SkinFixture fixture,AnimationGraphDefinition original,Func<int,bool,SceneDocument> create,PoseKernel kernel)
    {
        string evidence=Path.Combine(output,"m6-10",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(evidence);
        string plugins=Path.Combine(native,"m2/plugins");using var physics=new PhysicsService(plugins,characterSupport:true);
        using var raw=RuntimeAssetLoader.Prepare(fixture.Root,fixture.Project,[new(new(original.SkeletonId),AssetKind.Skeleton)]);
        using var rigLease=raw.AcquireLease();var rig=(RuntimeDataAsset)rigLease.Require(original.SkeletonId,AssetKind.Skeleton);
        var layout=RuntimeAnimationGraphAsset.DescribeSkeleton(rig);
        var x=new AnimationParameter(Guid.NewGuid(),"Speed",AnimationParameterKind.Float,.25,0,false);
        var space=AnimationGraphNode.Create(Guid.NewGuid(),"Locomotion",AnimationNodeKind.BlendSpace) with{Speed=1,Loop=true,
            BlendSpace=new(Guid.NewGuid(),1,new(x.Id,"Speed","normalized",0,1),null,1,Guid.Empty,
                [new(Guid.NewGuid(),fixture.Manifest.Clips[0],0,0),new(Guid.NewGuid(),fixture.Manifest.Clips[1],1,0)])};
        var cacheNode=AnimationGraphNode.Create(Guid.NewGuid(),"Shared base",AnimationNodeKind.CachePose);
        var upper=AnimationGraphNode.Create(Guid.NewGuid(),"Upper clip",AnimationNodeKind.Clip) with{ClipId=fixture.Manifest.Clips[0],Speed=1,Loop=true};
        var layer=AnimationGraphNode.Create(Guid.NewGuid(),"Masked override",AnimationNodeKind.LayerOverride) with{Weight=.5,
            Layer=new(new(Guid.NewGuid(),rig.Id,rig.ContentHash,[new(layout.Bones[1].Path,1)]),Guid.Empty,0)};
        var mixed=AnimationGraphNode.Create(Guid.NewGuid(),"Mix cached base",AnimationNodeKind.Blend) with{Weight=.5};
        var end=AnimationGraphNode.Create(Guid.NewGuid(),"Output",AnimationNodeKind.Output);
        AnimationGraphLink Link(Guid a,string pin,Guid b,string input)=>new(Guid.NewGuid(),a,pin,b,input);
        var graph=new AnimationGraphDefinition(AnimationGraphCodec.CurrentVersion,Guid.NewGuid(),"M6 combined runtime",rig.Id,Guid.Empty,[x],
            [space,cacheNode,upper,layer,mixed,end],[Link(space.Id,"pose",cacheNode.Id,"pose"),Link(cacheNode.Id,"pose",layer.Id,"a"),
                Link(upper.Id,"pose",layer.Id,"b"),Link(cacheNode.Id,"pose",mixed.Id,"a"),Link(layer.Id,"pose",mixed.Id,"b"),Link(mixed.Id,"pose",end.Id,"pose")],[],[])
            {Events=original.Events};
        graph=AddMontage(graph,original.Montage!);
        graph=graph with{Nodes=graph.Nodes.Select((n,i)=>n with{X=100+i*20,Y=70-i*10}).ToArray()};
        File.WriteAllBytes(Path.Combine(fixture.Root,"assets/m6-final.ncmaanim"),AnimationGraphCodec.Encode(graph));
        SceneDocument Document(int count,bool root){var d=create(count,root);int n=0;foreach(var o in d.World.GetObjects().Where(o=>o.Has<AnimatorData>())){
                o.Set(new AnimatorData(graph.AssetId,graph.SkeletonId));o.Set(TransformData.Identity with{Position=new((n%8-3.5f)*2,0,-(n/8)*2)});n++;}
            return d;}
        var startup=Document(1,true);Guid actor=startup.World.GetObjects().Single(o=>o.Has<AnimatorData>()).PersistentId;
        Guid camera=startup.World.GetObjects().Single(o=>o.Has<CameraData>()).PersistentId;
        byte[] package=SceneAssetPreparation.CreateRuntimePackage(fixture.Root,fixture.Project,startup.CaptureSnapshot(),[]);
        Check(package.SequenceEqual(SceneAssetPreparation.CreateRuntimePackage(fixture.Root,fixture.Project,startup.CaptureSnapshot(),[])),"M6 final package deterministic");
        var semantic=graph with{Nodes=graph.Nodes.Select(n=>n with{X=0,Y=0}).ToArray()};
        File.WriteAllBytes(Path.Combine(fixture.Root,"assets/m6-final.ncmaanim"),AnimationGraphCodec.Encode(graph with{Nodes=graph.Nodes.Select(n=>n with{X=n.X+77,Y=n.Y-93}).ToArray()}));
        Check(package.SequenceEqual(SceneAssetPreparation.CreateRuntimePackage(fixture.Root,fixture.Project,startup.CaptureSnapshot(),[])),"Author layout-only edits do not alter runtime bytes/hash/generation");
        File.WriteAllBytes(Path.Combine(fixture.Root,"assets/m6-final.ncmaanim"),AnimationGraphCodec.Encode(graph));
        string moved=Path.Combine(evidence,"moved");Directory.CreateDirectory(Path.Combine(moved,"assets"));string packPath=Path.Combine(moved,"assets/game.ncpak");
        File.WriteAllBytes(packPath,package);File.WriteAllBytes(Path.Combine(evidence,"valid.ncpak"),package);
        var negatives=new List<(string Name,byte[] Bytes)>{
            ("truncated",package[..^1]),("package_version",(byte[])package.Clone()),("checksum",(byte[])package.Clone()),
            ("graph_generation",RewriteM6Package(package,target:graph.AssetId,metadata:e=>e["generation"]=e["generation"]!.GetValue<ulong>()+1)),
            ("skeleton_generation",RewriteM6Package(package,target:rig.Id,metadata:e=>e["generation"]=e["generation"]!.GetValue<ulong>()+1)),
            ("clip_generation",RewriteM6Package(package,target:fixture.Manifest.Clips[0],metadata:e=>e["generation"]=e["generation"]!.GetValue<ulong>()+1)),
            ("missing_skeleton",RewriteM6Package(package,drop:rig.Id)),("missing_clip",RewriteM6Package(package,drop:fixture.Manifest.Clips[1])),
            ("graph_version",RewriteM6Package(package,target:graph.AssetId,payload:b=>{var node=JsonNode.Parse(b)!.AsObject();node["version"]=4;return JsonSerializer.SerializeToUtf8Bytes(node);})),
            ("actual_duration",RewriteM6Package(package,target:graph.AssetId,payload:_=>AnimationGraphCodec.Encode(semantic with{Montage=graph.Montage! with{
                Sections=graph.Montage.Sections.Select((s,i)=>i==0?s with{End=2}:s).ToArray()}}))),
            ("mask_content",RewriteM6Package(package,target:graph.AssetId,payload:_=>AnimationGraphCodec.Encode(semantic with{Nodes=semantic.Nodes.Select(n=>n.Layer is{} l?
                n with{Layer=l with{Mask=l.Mask with{SkeletonHash=new('F',64)}}}:n).ToArray()}))),
            ("editor_coordinates",RewriteM6Package(package,target:graph.AssetId,payload:_=>AnimationGraphCodec.Encode(graph)))
        };
        negatives[1].Bytes[4]=2;negatives[2].Bytes[^1]^=1;
        string repository=Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(native)))!;
        SceneDocumentFiles.Save(startup,Path.Combine(moved,"start.ncmascene"));File.Copy(Path.Combine(repository,"out/managed/Ncma.Gameplay.Sample.dll"),Path.Combine(moved,"gameplay.dll"));
        var configuration=new Ncma.Application.ProjectConfiguration(1,fixture.Project,"M6 final package","start.ncmascene","gameplay.dll","Direct3D11",[],true,camera,AssetPackage:"assets/game.ncpak");
        string project=Path.Combine(moved,"game.ncmaproject");File.WriteAllBytes(project,JsonSerializer.SerializeToUtf8Bytes(configuration,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        foreach(var negative in negatives){File.WriteAllBytes(Path.Combine(evidence,negative.Name+".ncpak"),negative.Bytes);
            Reject(()=>RuntimeAssetPackage.Inspect(negative.Bytes,fixture.Project));File.WriteAllBytes(packPath,negative.Bytes);
            foreach(bool headless in new[]{true,false}){var args=new List<string>{"--project",project,"--ticks","2","--report",Path.Combine(evidence,negative.Name+(headless?"-null":"-dx11")+".json")};if(headless)args.Add("--headless");
                var rejected=Ncma.Player.App.PlayerRunner.Run(Ncma.Player.App.PlayerOptions.Parse(args.ToArray()),pluginRoot:plugins,visible:false);
                Check(rejected.ExitCode==3&&rejected.Tick==0&&rejected.Modules.Length==0&&rejected.RenderedFrames==0&&rejected.ShutdownErrors.Length==0,"M6 package rejects before gameplay/native without fallback: "+negative.Name+" "+rejected.Reason);}
            using var unlocked=new FileStream(packPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        }
        File.WriteAllBytes(packPath,package);
        var costs=new List<M6Cost>();var schedules=new List<object>();var workloads=new List<object>();var identities=new HashSet<Guid>();
        using(var assets=SceneAssetPreparation.Prepare(moved,fixture.Project,startup.CaptureSnapshot(),true,"assets/game.ncpak")){
            var retained=(RuntimeAnimationGraphAsset)assets.Assets.Require(graph.AssetId,AssetKind.AnimationGraph);
            Check(retained.CopyDefinition().Nodes.All(n=>n.X==0&&n.Y==0)&&retained.CopyData().SequenceEqual(AnimationGraphCodec.Encode(semantic)),"Runtime graph contains all semantic data, no editor layout");
            var compiled=retained.PrepareProgram(assets.Assets);using(var lease=assets.Assets.AcquireLease()){
                long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1024;i++)Check(ReferenceEquals(compiled,retained.PrepareProgram(lease)),"Same exact publication uses immutable precompiled graph");
                Check(GC.GetAllocatedBytesForCurrentThread()-before==0,"Warm program reuse has no owner-thread allocation");
                Check(Task.Run(()=>{try{retained.PrepareProgram(lease);return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult(),"Program cache cannot bypass owner thread");
            }
            var expired=assets.Assets.AcquireLease();expired.Dispose();Reject(()=>retained.PrepareProgram(expired));
            var layers=compiled.CopyLayers();layers[0].Weights[0]=99;
            var slots=compiled.Montage!.CopyDefinition();slots.Slots[0]=slots.Slots[0] with{Name="changed"};
            Check(compiled.CopyLayers()[0].Weights[0]==0&&compiled.Montage.CopyDefinition().Slots[0].Name!="changed","Shared program returns owned nested data");
            using(var other=SceneAssetPreparation.Prepare(moved,fixture.Project,startup.CaptureSnapshot(),true,"assets/game.ncpak")){
                Reject(()=>retained.PrepareProgram(other.Assets));var second=(RuntimeAnimationGraphAsset)other.Assets.Require(graph.AssetId,AssetKind.AnimationGraph);
                Check(!ReferenceEquals(compiled,second.PrepareProgram(other.Assets)),"Program cache remains publication local");}
            Guid slot=graph.Montage!.Slots[0].Id;Vector3? expectedPosition=null;AnimationPoseInstruction[]? expectedPlan=null;MontageSlotFrame? expectedSlot=null;string? expectedTrace=null;
            foreach(int hz in new[]{30,60,144,0}){
                var d=startup.CreateIsolatedCopy();using var play=new PlaySession(d,FrameTimePolicy.Strict,advanceMode:hz==0?PlayAdvanceMode.FixedSteps:PlayAdvanceMode.Frames);
                using var runtime=ScenePlayRuntime.Compose(play,physics,assets);
                var trace=new List<M6Step>(120);var numeric=GraphPoseSnapshotPreparation.Prepare(compiled,assets.Assets);
                play.AddCommittedObserver(new M6Observer(()=>{var committed=runtime.Animators!.ReadDebug(actor);var pose=new AnimationLocalTransform[layout.Bones.Length];
                    numeric.Evaluate(committed.Instructions.ToArray(),committed.Frame.Output,[],0,pose);
                    trace.Add(new(play.Tick,committed.Instructions.ToArray(),pose,runtime.Animators.ReadMontageSlot(actor,slot),runtime.Characters!.InspectRootMotion(actor).DesiredDisplacement,
                        committed.Events.Select(e=>e.MarkerId).ToArray()));}));
                play.Start(_=>throw new Exception("No behaviours"));
                try{for(int i=0;i<(hz==0?120:hz*2);i++){
                        Check((hz==0?play.AdvanceFixedStep():play.AdvanceFrame(1d/hz)).State==PlayState.Running,"M6 combined schedule "+hz+": "+play.Fault?.Code);}
                    var debug=runtime.Animators!.ReadDebug(actor);var position=d.World.FindObject(actor).Get<TransformData>().Position;var currentSlot=runtime.Animators.ReadMontageSlot(actor,slot);
                    Check(play.Tick==120&&debug.Frame.Context.Tick==120&&runtime.Animators.ReadMontageFrame(actor).Context==debug.Frame.Context&&runtime.Characters!.Status.CommittedSequence==120,"All combined fixed-step owners share tick120");
                    expectedPosition??=position;expectedPlan??=debug.Instructions.ToArray();expectedSlot??=currentSlot;
                    Check(Vector3.Distance(expectedPosition.Value,position)<1e-5&&expectedPlan.SequenceEqual(debug.Instructions)&&expectedSlot==currentSlot,"M6 30/60/144/fixed combined root/pose/Slot parity");
                    string hash=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(trace,new JsonSerializerOptions{IncludeFields=true})));expectedTrace??=hash;
                    Check(trace.Count==120&&hash==expectedTrace&&trace.Any(t=>t.Markers.Length!=0),"Every successful quantum's TRS/root/Slot/Notify trace matches, including multi-step frames");
                    Check(kernel.Statistics.Rigs==0,"Headless schedules never prepare native pose/GPU");
                    schedules.Add(new{hz,tick=play.Tick,position=new{x=position.X,y=position.Y,z=position.Z},currentSlot,traceHash=hash,successfulQuanta=trace.Count,notifyCount=trace.Sum(t=>t.Markers.Length)});
                }finally{play.Stop();}
                Check(physics.Inspect().Worlds==0,"M6 schedule numerical drain");
            }
            using var cache=new RenderResourceCache(renderer);var skinBaseline=renderer.SkinStats.Meshes;var meshBaseline=renderer.SceneStats.LiveMeshes;
            var pipelineBaseline=renderer.PipelineStats.Pipelines;var resourceBaseline=renderer.ResourceStats;
            byte[] frozen=startup.CaptureBytes();
            for(int cycle=0;cycle<128;cycle++){
                var d=startup.CreateIsolatedCopy();using var play=new PlaySession(d,FrameTimePolicy.Strict,advanceMode:PlayAdvanceMode.FixedSteps);
                using var runtime=ScenePlayRuntime.Compose(play,physics,assets);bool fail=false;play.AddSystem(new GraphFailure(_=>{if(fail){var pending=play.Commands.SpawnEmpty("post-solver fault");play.Commands.AttachBehaviour(pending.ObjectId,new(Guid.NewGuid(),"Unavailable",true,[]));}}));
                play.Start(_=>throw new Exception("Injected unavailable behaviour"));Check(identities.Add(play.SessionId)&&identities.Add(d.World.Identity)&&identities.Add(runtime.Animators!.ReadFrame(actor).InstanceId),"Fresh M6 coupled identities");
                SceneRenderSession? scene=null;
                try{
                    scene=new(renderer,cache,d.World,assets,d.CaptureSnapshot(),poseKernel:kernel,play:play,animators:runtime.Animators,rootMotion:runtime.Characters);
                    for(int step=0;step<8;step++){
                        int[] gc=[GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)];long bytes=GC.GetAllocatedBytesForCurrentThread(),started=Stopwatch.GetTimestamp();
                        var status=play.AdvanceFixedStep();
                        double simulation=Stopwatch.GetElapsedTime(started).TotalMilliseconds;long simulationBytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
                        Check(status.State==PlayState.Running,"M6 cycle quantum "+cycle+": "+play.Fault?.Code);
                        var debug=runtime.Animators!.ReadFrame(actor);Check(debug.Context.Tick==play.Tick&&runtime.Animators.ReadMontageFrame(actor).Context==debug.Context&&runtime.Characters!.Status.CommittedSequence==play.Tick,"M6 draw exact committed identity");
                        started=Stopwatch.GetTimestamp();renderer.WaitIdle();double drain=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                        ulong samples=kernel.Statistics.SampleCalls,palette=renderer.SkinStats.PaletteBytes,vertices=renderer.SceneStats.UploadedBytes;
                        bytes=GC.GetAllocatedBytesForCurrentThread();started=Stopwatch.GetTimestamp();ulong backpressure=scene.SkinBackpressureFrames,tick=play.Tick;
                        Check(scene.Submit(frame++,256,256,camera),"M6 cycle actual scene submit");renderer.Present();double draw=Stopwatch.GetElapsedTime(started).TotalMilliseconds;long drawBytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
                        Check(scene.SkinBackpressureFrames==backpressure&&play.Tick==tick&&scene.Costs.GeometryDraws==1&&scene.Costs.ShadowDraws==1,"M6 no backpressure/same-tick geometry and shadow");
                        Check(renderer.SceneStats.UploadedBytes==vertices&&physics.Inspect().Worlds==1,"M6 resident geometry not uploaded each frame; one solver");
                        costs.Add(new(1,cycle,tick,play.SessionId,d.World.Identity,debug.InstanceId,runtime.Characters!.Status.CommittedSequence,simulation,simulationBytes,drain,draw,drawBytes,scene.Costs,
                            scene.Animation!.Costs.SampleMilliseconds,scene.Animation.Costs.PaletteMilliseconds,scene.SkinAbiMilliseconds,renderer.SkinStats.PaletteBytes-palette,renderer.SceneStats.UploadedBytes-vertices,
                            kernel.Statistics.SampleCalls-samples,renderer.Stats.GpuSampleValid==1?renderer.Stats.GpuMilliseconds:null,Enumerable.Range(0,3).Select(g=>GC.CollectionCount(g)-gc[g]).ToArray()));
                    }
                    scene.Dispose();scene=null;
                    if(cycle%4==0){Guid old=runtime.Animators!.ReadFrame(actor).InstanceId,world=d.World.Identity;ulong tick=play.Tick;play.Reload(_=>throw new Exception("No behaviours"));
                        Check(play.State==PlayState.Paused&&play.Tick==tick&&d.World.Identity!=world&&runtime.Animators.ReadFrame(actor).InstanceId!=old,"M6 reload rotates identities and retains chronology");
                        Check(play.Step().State==PlayState.Paused&&runtime.Animators.ReadMontageSlot(actor,slot).Active,"M6 reload starts same persistent Montage without external binding");}
                    if(cycle%4==1){byte[] before=d.CaptureBytes();ulong tick=play.Tick;fail=true;
                        Check(play.AdvanceFixedStep().State==PlayState.Faulted&&play.Tick==tick&&before.SequenceEqual(d.CaptureBytes()),"M6 failed quantum retains committed World");
                        Check(runtime.Characters!.Status.NumericalExecutionStarted,"M6 post-solver failure is fail-stop, not rollback");Reject(()=>runtime.Animators!.ReadFrame(actor));Reject(()=>runtime.Characters.InspectRootMotion(actor));}
                }finally{scene?.Dispose();play.Stop();}
                Check(physics.Inspect().Worlds==0&&kernel.Statistics.Rigs==0&&kernel.Statistics.Clips==0&&cache.Count==0&&renderer.SkinStats.Meshes==skinBaseline&&renderer.SceneStats.LiveMeshes==meshBaseline&&renderer.PipelineStats.Pipelines==pipelineBaseline&&renderer.ResourceStats.Textures==resourceBaseline.Textures&&renderer.ResourceStats.Materials==resourceBaseline.Materials&&renderer.ResourceStats.Targets==resourceBaseline.Targets,"M6 shared services return to original resource baselines: "+cycle);
                Check(frozen.SequenceEqual(startup.CaptureBytes()),"M6 cycles leave frozen startup unchanged");
            }
        }
        using(var unlocked=new FileStream(packPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(unlocked.Length==package.Length,"M6 final runtime package pin released");
        Check(!Directory.Exists(Path.Combine(moved,"out"))&&Directory.GetFiles(Path.Combine(moved,"assets")).Length==1,"M6 relocated fixture uses only runtime package");
        foreach(int count in new[]{0,1,8,32}){
            var d=Document(count,count>0);var ids=d.World.GetObjects().Where(o=>o.Has<AnimatorData>()).Select(o=>o.PersistentId).ToArray();Guid view=d.World.GetObjects().Single(o=>o.Has<CameraData>()).PersistentId;
            byte[] bytes=SceneAssetPreparation.CreateRuntimePackage(fixture.Root,fixture.Project,d.CaptureSnapshot(),[]);
            string root=Path.Combine(evidence,"workload-"+count);Directory.CreateDirectory(Path.Combine(root,"assets"));File.WriteAllBytes(Path.Combine(root,"assets/game.ncpak"),bytes);
            using var assets=SceneAssetPreparation.Prepare(root,fixture.Project,d.CaptureSnapshot(),true,"assets/game.ncpak");
            Check(count!=0||assets.Assets.List().Count==0,"Empty workload packages no unused animation closure");
            using var play=new PlaySession(d,FrameTimePolicy.Strict,advanceMode:PlayAdvanceMode.FixedSteps);using var runtime=ScenePlayRuntime.Compose(play,physics,assets);using var cache=new RenderResourceCache(renderer);
            play.Start(_=>throw new Exception("No behaviours"));var measured=new List<M6Cost>();
            try{using var scene=new SceneRenderSession(renderer,cache,d.World,assets,d.CaptureSnapshot(),poseKernel:count==0?null:kernel,play:play,animators:runtime.Animators,rootMotion:runtime.Characters);
                for(int step=0;step<24;step++){
                    int[] gc=[GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)];long before=GC.GetAllocatedBytesForCurrentThread(),started=Stopwatch.GetTimestamp();
                    var status=play.AdvanceFixedStep();double simulation=Stopwatch.GetElapsedTime(started).TotalMilliseconds;long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
                    Check(status.State==PlayState.Running,"M6 workload quantum "+count+": "+play.Fault?.Code);
                    foreach(Guid id in ids){var stamp=runtime.Animators!.ReadFrame(id);Check(stamp.Context.SessionId==play.SessionId&&stamp.Context.WorldId==d.World.Identity&&stamp.Context.Tick==play.Tick&&runtime.Animators.ReadMontageFrame(id).Context==stamp.Context,"Every workload actor has exact graph/Montage/tick identity");}
                    started=Stopwatch.GetTimestamp();if(count>0)renderer.WaitIdle();double drain=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    ulong samples=kernel.Statistics.SampleCalls,palette=renderer.SkinStats.PaletteBytes,vertices=renderer.SceneStats.UploadedBytes,backpressure=scene.SkinBackpressureFrames;
                    before=GC.GetAllocatedBytesForCurrentThread();started=Stopwatch.GetTimestamp();bool submitted=scene.Submit(frame++,256,256,view);if(submitted)renderer.Present();
                    double draw=Stopwatch.GetElapsedTime(started).TotalMilliseconds;long drawBytes=GC.GetAllocatedBytesForCurrentThread()-before;
                    Check(submitted==(count>0)&&scene.SkinBackpressureFrames==backpressure,"M6 workload every actor drawn without backpressure");
                    Check(count==0?scene.Animation is null&&scene.Plan is null&&physics.Inspect().Worlds==0&&kernel.Statistics.Rigs==0&&cache.Count==0:
                        scene.Costs.GeometryDraws==count&&scene.Costs.ShadowDraws==count&&scene.Animation!.Costs.Characters==count&&physics.Inspect().Worlds==1,"M6 workload exact active resources and draws");
                    Check(renderer.SceneStats.UploadedBytes==vertices,"M6 workload no per-frame geometry upload");
                    if(step>=8)measured.Add(new(count,-1,play.Tick,play.SessionId,d.World.Identity,count==0?Guid.Empty:runtime.Animators!.ReadFrame(ids[0]).InstanceId,runtime.Characters?.Status.CommittedSequence??0,
                        simulation,allocated,drain,draw,drawBytes,scene.Costs,scene.Animation?.Costs.SampleMilliseconds??0,scene.Animation?.Costs.PaletteMilliseconds??0,scene.SkinAbiMilliseconds,
                        renderer.SkinStats.PaletteBytes-palette,renderer.SceneStats.UploadedBytes-vertices,kernel.Statistics.SampleCalls-samples,count==0?null:renderer.Stats.GpuSampleValid==1?renderer.Stats.GpuMilliseconds:null,
                        Enumerable.Range(0,3).Select(g=>GC.CollectionCount(g)-gc[g]).ToArray()));
                }
            }finally{play.Stop();}
            Check(physics.Inspect().Worlds==0&&kernel.Statistics.Rigs==0&&kernel.Statistics.Clips==0&&cache.Count==0&&renderer.SkinStats.Meshes==0,"M6 workload drain");
            workloads.Add(new{characters=count,warmup=8,samples=16,packageHash=Convert.ToHexString(SHA256.HashData(bytes)),measurements=measured});
        }
        TestAnimatorJointAcceptance(renderer,ref frame,native,output,fixture,graph,Document,physics,kernel,"m6-10","M6.10");
        // Run the actual relocated production apphost, not the test executable with resident Editor/import dependencies.
        string build=native.Contains("windows-ninja-release",StringComparison.OrdinalIgnoreCase)?"Release":"Debug";
        string binaries=Path.Combine(repository,"managed/Ncma.Player.App/bin",build,"net8.0");
        foreach(string file in Directory.GetFiles(binaries).Where(p=>Path.GetExtension(p) is ".dll" or ".json" or ".exe" or ".ico"))File.Copy(file,Path.Combine(moved,Path.GetFileName(file)));
        Directory.CreateDirectory(Path.Combine(moved,"plugins"));foreach(string name in new[]{"NcmaPlatform.dll","NcmaRenderer.dll","NcmaPhysics.dll","NcmaAnimationKernel.dll","glfw3.dll"})File.Copy(Path.Combine(plugins,name),Path.Combine(moved,"plugins",name));
        string[] forbidden=["Ncma.Editor","Ncma.Gui","Ncma.Mcp","Ncma.Asset.Import","Ncma.Assets.Authoring","NcmaNative",".fbx",".ncmeta",".ncmaanim",".nca",".py"];
        var files=Directory.GetFiles(moved,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(p=>new{path=Path.GetRelativePath(moved,p).Replace('\\','/'),size=new FileInfo(p).Length,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))}).ToArray();
        foreach(var file in files)foreach(string marker in forbidden)Check(!file.path.Contains(marker,StringComparison.OrdinalIgnoreCase),"M6 apphost authoring dependency: "+file.path);
        string dependencies=File.ReadAllText(Path.Combine(moved,"NcmaPlayer.deps.json"));foreach(string marker in forbidden.Where(m=>m.StartsWith("Ncma",StringComparison.Ordinal)))Check(!dependencies.Contains(marker,StringComparison.Ordinal),"M6 apphost dependency graph: "+marker);
        File.WriteAllBytes(Path.Combine(moved,Ncma.Application.DeploymentManifest.FileName),JsonSerializer.SerializeToUtf8Bytes(new{schemaVersion=1,configuration=build,rid="win-x64",tfm="net8.0",publishMode="framework-dependent",production=false,manualAcceptance=false,selfContainedVerified=false,files}));
        Ncma.Application.DeploymentManifest.Validate(moved);var apphosts=new List<object>();
        foreach(bool headless in new[]{true,false}){
            string reportPath=Path.Combine(evidence,headless?"apphost-headless.json":"apphost-dx11.json");
            var start=new ProcessStartInfo(Path.Combine(moved,"NcmaPlayer.exe")){WorkingDirectory=evidence,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(string arg in new[]{"--project",project,"--ticks","8","--max-runtime-seconds","20","--report",reportPath})start.ArgumentList.Add(arg);if(headless)start.ArgumentList.Add("--headless");
            using(var child=Process.Start(start)!){var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
                if(!child.WaitForExit(30000)){child.Kill(true);child.WaitForExit();throw new Exception("M6 relocated apphost bounded timeout");}
                Check(child.ExitCode==0,"M6 relocated apphost: "+stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult());}
            var json=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase};json.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            var result=JsonSerializer.Deserialize<Ncma.Player.App.PlayerReport>(File.ReadAllBytes(reportPath),json)!;
            Check(result.ExitCode==0&&result.Tick>=8&&result.ValidationErrors==0&&result.ValidationWarnings==0&&result.ShutdownErrors.Length==0,"M6 actual apphost completes and drains");
            Check(headless?result.Tick==8&&result.RenderedFrames==0&&result.Modules.All(m=>m.Id is not ("ncma.renderer" or "ncma.platform")):result.RenderedFrames>0,"M6 actual apphost exact Headless or real DX11");
            apphosts.Add(new{headless,result.Tick,result.RenderedFrames,result.Modules,result.ValidationErrors,result.ValidationWarnings});
            Ncma.Application.DeploymentManifest.Validate(moved);using var unlocked=new FileStream(packPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        }
        Check(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"M6 final API0/0");
        using var report=new FileStream(Path.Combine(evidence,"acceptance.json"),FileMode.CreateNew,FileAccess.Write,FileShare.None);
        JsonSerializer.Serialize(report,new{schema=1,configuration=native.Contains("windows-ninja-release",StringComparison.OrdinalIgnoreCase)?"Release":"Debug",graphVersion=graph.Version,
            authorGraphHash=Convert.ToHexString(SHA256.HashData(AnimationGraphCodec.Encode(graph))),runtimeGraphHash=Convert.ToHexString(SHA256.HashData(AnimationGraphCodec.Encode(semantic))),layoutIndependent=true,packageHash=Convert.ToHexString(SHA256.HashData(package)),runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os=Environment.OSVersion.ToString(),width=256,height=256,fixedSeconds=1d/60,cycles=128,reloads=32,postSolverFailures=32,draws=costs.Count,
            negativePackages=negatives.Select(n=>n.Name),schedules,workloads,costs,apphosts,plugins=Directory.GetFiles(plugins,"*.dll").Order().Select(p=>new{name=Path.GetFileName(p),sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))}),
            gpuSemantics="Asynchronous last-valid timestamp, no sample-frame ID; observations may repeat. Diagnostic drain is separate and test-only, NOT production tick/throughput acceptance.",
            synthetic=true,manualAccepted=false,userMaterialAccepted=false,targetAccepted=false,budgetAccepted=false,longRunAccepted=false},new JsonSerializerOptions{WriteIndented=true});
        Console.WriteLine("PASS M6.10 layout-independent combined NCP1/12 negative packages/24 fail-closed Players/1024 zero-allocation program cache/30-60-144-fixed parity/128 shared-service cycles/32 Reload/32 post-solver fail-stop/API0/0: "+evidence);
    }
}
