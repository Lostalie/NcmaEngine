using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Animation;
using Ncma.Animation.Native;
using Ncma.Assets;
using Ncma.Assets.Runtime;
using Ncma.Application.Runtime;
using Ncma.Editor.Services;
using Ncma.Gameplay;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Scene.Rendering;
using Ncma.Runtime;

internal static unsafe partial class Program
{
    private static AnimationGraphDefinition AnimatorGraph(SkinFixture f)
    {
        var a=AnimationGraphNode.Create(Guid.NewGuid(),"Right",AnimationNodeKind.Clip) with{ClipId=f.Manifest.Clips[0],Speed=1,Loop=true};
        var b=AnimationGraphNode.Create(Guid.NewGuid(),"Left",AnimationNodeKind.Clip) with{ClipId=f.Manifest.Clips[1],Speed=1,Loop=true};
        var blend=AnimationGraphNode.Create(Guid.NewGuid(),"Mix",AnimationNodeKind.Blend);
        var parameter=new AnimationParameter(Guid.NewGuid(),"Weight",AnimationParameterKind.Float,.25,0,false);
        var value=AnimationGraphNode.Create(Guid.NewGuid(),"Weight",AnimationNodeKind.Parameter) with{ParameterId=parameter.Id};
        var output=AnimationGraphNode.Create(Guid.NewGuid(),"Output",AnimationNodeKind.Output);
        AnimationGraphLink Link(Guid from,string pin,Guid to,string input)=>new(Guid.NewGuid(),from,pin,to,input);
        return new(1,Guid.NewGuid(),"Pinned graph",f.Manifest.Skeleton!.Value,Guid.Empty,[parameter],[a,b,blend,value,output],
            [Link(a.Id,"pose",blend.Id,"a"),Link(b.Id,"pose",blend.Id,"b"),Link(value.Id,"value",blend.Id,"weight"),Link(blend.Id,"pose",output.Id,"pose")],[],[]);
    }
    private sealed class GraphFailure(Action<World> action):IWorldSystem {public void FixedUpdate(World world,double delta)=>action(world);}
    private static void TestAnimatorScenes(RendererSession renderer,ref ulong frame,string native,string output)
    {
        native=Path.GetDirectoryName(native)!;
        string path=Path.Combine(native,"NcmaAnimationKernel.dll");using var kernel=new PoseKernel(path,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),blendSupport:true);
        var f=new SkinFixture(output);var graph=AnimatorGraph(f);string graphPath=Path.Combine(f.Root,"assets/character.ncmaanim");
        File.WriteAllBytes(graphPath,AnimationGraphCodec.Encode(graph));var (document,camera,ids)=f.Scene();
        var obj=document.World.FindObject(ids[0]);obj.Remove<ClipPlaybackData>();obj.Set(new AnimatorData(graph.AssetId,graph.SkeletonId));document.ValidateAuthoring();
        obj.Set(new ClipPlaybackData(f.Manifest.Clips[0],true,true,1,0));Reject(document.ValidateAuthoring);obj.Remove<ClipPlaybackData>();
        using var assets=SceneAssetPreparation.Prepare(f.Root,f.Project,document.CaptureSnapshot(),true);
        var retained=(RuntimeAnimationGraphAsset)assets.Assets.Require(graph.AssetId,AssetKind.AnimationGraph);
        var copy=retained.CopyDefinition();copy.Nodes[0]=copy.Nodes[0] with{Name="mutated"};Check(retained.CopyDefinition().Nodes.All(n=>n.Name!="mutated"),"Graph publication leaked authoring data");
        Check(assets.Assets.PinnedGenerations==1 && retained.PrepareProgram(assets.Assets).ResourceGeneration==f.Plan.Record.Generation!.Number,"Real NCA graph closure");
        var priorIdentity=assets.Assets.Identity;
        var missing=graph with{Nodes=graph.Nodes.Select(n=>n.Kind==AnimationNodeKind.Clip?n with{ClipId=Guid.NewGuid()}:n).ToArray()};
        File.WriteAllBytes(graphPath,AnimationGraphCodec.Encode(missing));Reject(()=>SceneAssetPreparation.Prepare(f.Root,f.Project,document.CaptureSnapshot(),false));
        Check(assets.Assets.Identity==priorIdentity && PinLocked(f.GenerationPath),"Failed graph candidate released previous pins");File.WriteAllBytes(graphPath,AnimationGraphCodec.Encode(graph));
        var mismatch=graph with{SkeletonId=Guid.NewGuid()};File.WriteAllBytes(graphPath,AnimationGraphCodec.Encode(mismatch));Reject(()=>SceneAssetPreparation.Prepare(f.Root,f.Project,document.CaptureSnapshot(),true));File.WriteAllBytes(graphPath,AnimationGraphCodec.Encode(graph));
        byte[] package=SceneAssetPreparation.CreateRuntimePackage(f.Root,f.Project,document.CaptureSnapshot(),[]);
        var index=RuntimeAssetPackage.Inspect(package,f.Project);Check(index.Assets.Count(e=>e.Encoding=="animgraph")==1,"Graph runtime package route missing");
        var forged=ChangeGraphIndex(package,graph.AssetId);Reject(()=>RuntimeAssetPackage.Inspect(forged,f.Project));
        Reject(()=>RuntimeAssetPackage.Inspect(ChangeGraphIndex(package,f.Manifest.Clips[0],generation:true),f.Project));
        string moved=Path.Combine(output,"animator-moved",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(moved,"assets"));File.WriteAllBytes(Path.Combine(moved,"assets/game.ncpak"),package);
        using var packed=SceneAssetPreparation.Prepare(moved,f.Project,document.CaptureSnapshot(),true,"assets/game.ncpak");
        Check(!Directory.Exists(Path.Combine(moved,"out")) && Directory.GetFiles(Path.Combine(moved,"assets")).Length==1,"Packed graph reentered authoring sources");
        string repository=Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(native)))!;
        Ncma.Scene.SceneDocumentFiles.Save(document,Path.Combine(moved,"start.ncmascene"));
        File.Copy(Path.Combine(repository,"out/managed/Ncma.Gameplay.Sample.dll"),Path.Combine(moved,"gameplay.dll"));
        var configuration=new Ncma.Application.ProjectConfiguration(1,f.Project,"Packed Animator","start.ncmascene","gameplay.dll","Direct3D11",[],AssetPackage:"assets/game.ncpak");
        string project=Path.Combine(moved,"game.ncmaproject");File.WriteAllBytes(project,JsonSerializer.SerializeToUtf8Bytes(configuration,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        var playerOptions=Ncma.Player.App.PlayerOptions.Parse(["--project",project,"--headless","--ticks","8","--report",Path.Combine(moved,"player-result.json")]);
        var report=Ncma.Player.App.PlayerRunner.Run(playerOptions,pluginRoot:Path.Combine(moved,"no-native-plugins"),visible:false);
        Check(report.ExitCode==0 && report.Tick==8 && report.Modules.Length==0 && report.RenderedFrames==0 && report.ShutdownErrors.Length==0,"Formal source-free Headless Player graph: "+report.Reason);
        using(var owner=new RuntimeSessionOwner(document)) {
            SceneAnimatorRuntime? animator=null;
            var play=owner.StartPlay(factory:d=>new PlaySession(d,FrameTimePolicy.Strict,advanceMode:PlayAdvanceMode.FixedSteps),compose:p=>animator=SceneAnimatorRuntime.Compose(p,packed));
            for(int i=0;i<60;i++)Check(play.AdvanceFixedStep().State==PlayState.Running,"Headless graph step failed");
            Check(animator!.ReadFrame(ids[0]).Context.Tick==60 && kernel.Statistics.Rigs==0,"Headless created native pose/GPU or clock missed commit");
            var session=play.SessionId;var world=play.Document.World.Identity;play.Reload(_=>throw new Exception("no behaviours"));
            Check(play.State==PlayState.Paused && play.Tick==60 && play.SessionId!=session && play.Document.World.Identity!=world && animator.ReadFrame(ids[0]).Context.Tick==60,"Graph reload identity/chronology");
            owner.StopPlay();Reject(()=>animator.ReadFrame(ids[0]));
        }
        foreach(int count in new[]{32,33}) {
            var (many,_,actors)=f.Scene(count);
            foreach(Guid id in actors){var actor=many.World.FindObject(id);actor.Remove<ClipPlaybackData>();actor.Set(new AnimatorData(graph.AssetId,graph.SkeletonId));}
            using var host=new RuntimeSessionOwner(many);SceneAnimatorRuntime? animator=null;
            if(count==33){Reject(()=>host.StartPlay(compose:p=>SceneAnimatorRuntime.Compose(p,packed)));continue;}
            var play=host.StartPlay(compose:p=>animator=SceneAnimatorRuntime.Compose(p,packed));
            animator!.SetFloat(actors[0],graph.Parameters[0].Id,1);Check(play.AdvanceFrame(1.0/60).State==PlayState.Running,"32 Animator quantum");
            var identities=new HashSet<Guid>();var recipe=new AnimationPoseInstruction[retained.PrepareProgram(assets.Assets).MaximumPlanInstructions];
            foreach(Guid id in actors) {
                var snapshot=animator.ReadFrame(id);identities.Add(snapshot.InstanceId);int n=animator.CopyCommittedPlan(id,recipe);
                Check(snapshot.Context.Tick==1 && recipe[n-1].Weight==(id==actors[0]?1:.25f),"Shared program leaked object parameters/clock");
            }
            Check(identities.Count==32 && kernel.Statistics.Rigs==0,"32 Animator identity/Headless native isolation");host.StopPlay();
        }
        // Snapshot restore invalidates object references; restore source startup for the Editor clone.
        SceneAnimatorRuntime? live=null; SceneRenderSession? scene=null;bool closeFail=false;
        using var editor=new EditorSessionOwner("Animator",components:RenderComponentRegistry.Register(RenderConfiguration.CreateRegistry()),validateComposition:SceneRenderValidation.RequireComposition,
            composePreparedPlay:(p,a)=>live=SceneAnimatorRuntime.Compose(p,a),beforePlayStop:()=>{if(closeFail)throw new InvalidOperationException("injected derived close retention");scene?.Dispose();scene=null;});
        editor.Document.RestoreSnapshot(document.CaptureSnapshot());editor.PrepareRenderAssets(f.Root,f.Project);
        using var cache=new RenderResourceCache(renderer);using var target=renderer.CreateViewTarget(256,256);
        ulong editTick=editor.Document.World.Tick;
        using(var preview=new SceneRenderSession(renderer,cache,editor.Document.World,editor.RenderAssets!,editor.Document.CaptureSnapshot(),poseKernel:kernel)) {
            preview.Animation!.AdvancePreview(.25);preview.Submit(frame++,256,256,camera,target:target);renderer.Present();
            Check(Math.Abs(preview.Animation.RootDisplacement(ids[0]).X-.125)<1e-5 && editor.Document.World.Tick==editTick,"Independent graph preview oracle: displacement="+preview.Animation.RootDisplacement(ids[0]).X+" tick="+editor.Document.World.Tick);
        }
        for(int cycle=0;cycle<32;cycle++) {
            var play=editor.StartPlay();play.Pause();live!.SetFloat(ids[0],graph.Parameters[0].Id,.75);
            var before=live.ReadFrame(ids[0]);scene=new(renderer,cache,play.Document.World,editor.PlayRenderAssets!,play.Document.CaptureSnapshot(),poseKernel:kernel,play:play,animators:live);
            if(cycle==0) {
                ulong rigs=kernel.Statistics.Rigs;
                Reject(()=>new SceneAnimationSession(play.Document.World,assets,play.Document.CaptureSnapshot(),kernel,animators:live));
                Check(kernel.Statistics.Rigs==rigs,"Foreign prepared asset publication acquired numerical resources");
            }
            scene.Submit(frame++,256,256,camera,target:target);renderer.Present();
            Check(Math.Abs(scene.Animation!.RootDisplacement(ids[0]).X)<1e-5,"Requested parameter advanced uncommitted time");
            play.Step();var after=live.ReadFrame(ids[0]);Check(after.Context.Tick==before.Context.Tick+1,"Committed graph tick");
            scene.Submit(frame++,256,256,camera,target:target);renderer.Present();
            Check(Math.Abs(scene.Animation.RootDisplacement(ids[0]).X+1f/120)<1e-5,"Two-clip native graph blend oracle");
            Check(scene.Costs.GeometryDraws==1 && scene.Costs.ShadowDraws==1,"Graph skin/shadow production pipeline");
            if(cycle==0) {
                closeFail=true;Reject(editor.StopPlay);Check(editor.Play==play && editor.PlayRenderAssets is not null && kernel.Statistics.Rigs==1,"Failed derived close discarded ownership");closeFail=false;
                Check(Task.Run(()=>{try{live.ReadFrame(ids[0]);return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult(),"Graph wrong thread accepted");
            }
            editor.StopPlay();Check(kernel.Statistics.Rigs==0 && kernel.Statistics.Clips==0 && renderer.SkinStats.Meshes==0,"Graph stop resource baseline");
        }
        foreach(int fault in new[]{0,1,2}) {
            // Register fault injection after graph preparation, before starting the host.
            var failureDocument=editor.Document.CreateIsolatedCopy();using var host=new RuntimeSessionOwner(failureDocument);
            SceneAnimatorRuntime? failed=null;
            var failedPlay=host.StartPlay(compose:p=>{
                failed=SceneAnimatorRuntime.Compose(p,assets);
                p.AddSystem(new GraphFailure(w=>{if(fault!=0){try{if(fault==1)w.FindObject(ids[0]).Set(new AnimatorData(graph.AssetId,graph.SkeletonId));else failed!.SetFloat(ids[0],graph.Parameters[0].Id,.5);}catch(InvalidOperationException){}}else throw new InvalidOperationException("prepared failure");}));return failed;
            });
            var old=failed!.ReadFrame(ids[0]);failedPlay.Pause();Check(failedPlay.Step().State==PlayState.Faulted && failedPlay.Tick==old.Context.Tick,"Failed graph quantum published time or caught write was not poisoned");
            Reject(()=>failed.ReadFrame(ids[0]));host.StopPlay();
        }
        Check(kernel.Statistics.Rigs==0 && kernel.Statistics.Clips==0 && renderer.Stats.ValidationErrors==0 && renderer.Stats.ValidationWarnings==0,"Graph final native/API baseline");
        File.WriteAllText(Path.Combine(output,"animator-scene-results.json"),JsonSerializer.Serialize(new{schema=1,realNca=true,sourceFreeGraphPackage=true,formalPlayerTicks=8,formalPlayerNativeModules=0,headlessTicks=60,headlessActors=32,actor33Rejected=true,playStopCycles=32,nativeGraphBlendOracle=true,closeRetention=true,failedStepAndCaughtWriteAndControl=true,foreignAssetPublicationRejected=true,manualAcceptance=false}));
        Console.WriteLine("PASS M6.3-B real NCA graph/pinned closure/package/headless/reload/preview/native blend/32 Editor cycles/fault/close retention");
    }
    private static byte[] ChangeGraphIndex(byte[] bytes,Guid graph,bool generation=false)
    {
        int size=System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));var index=System.Text.Json.Nodes.JsonNode.Parse(bytes.AsSpan(16,size))!;
        var row=index["assets"]!.AsArray().Single(a=>a!["assetId"]!.GetValue<Guid>()==graph)!;if(generation)row["generation"]=3;else row["modelId"]=Guid.NewGuid();
        byte[] table=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(index),result=new byte[bytes.Length+table.Length-size];bytes.AsSpan(0,16).CopyTo(result);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8),table.Length);table.CopyTo(result,16);bytes.AsSpan(16+size).CopyTo(result.AsSpan(16+table.Length));return result;
    }
}
