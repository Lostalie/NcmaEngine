using System.Security.Cryptography;
using System.Numerics;
using System.Text.Json;
using Ncma.Animation;
using Ncma.Animation.Native;
using Ncma.Assets.Runtime;
using Ncma.Characters;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Gameplay;
using Ncma.Physics;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    private static void TestAnimatorJointAcceptance(RendererSession renderer,ref ulong frame,string native,string output,
        SkinFixture fixture,AnimationGraphDefinition source,Func<int,bool,SceneDocument> create,PhysicsService physics,PoseKernel kernel,string evidencePrefix="m6-5-d",string stage="M6.5-D")
    {
        // Autonomous closed conditions exercise the production Player without a test-only Animator control entry.
        bool montage=source.Montage is not null;
        var graph=source with{AssetId=Guid.NewGuid(),Name="Joint persistent animation",Events=montage?source.Events:source.Events.Select(e=>e with{Time=.001}).ToArray(),
            Transitions=source.Transitions.Select(t=>t with{Conditions=t.Conditions.Select(c=>c with{BoolValue=false}).ToArray()}).ToArray()};
        File.WriteAllBytes(Path.Combine(fixture.Root,"assets/joint.ncmaanim"),AnimationGraphCodec.Encode(graph));
        SceneDocument Document(int count,bool root){var d=create(count,root);int index=0;foreach(var o in d.World.GetObjects().Where(o=>o.Has<AnimatorData>())){o.Set(new AnimatorData(graph.AssetId,graph.SkeletonId));o.Set(TransformData.Identity with{Position=new((index%8-3.5f)*2,0,-(index/8)*2)});index++;}foreach(var camera in d.World.GetObjects().Where(o=>o.Has<CameraData>()))camera.Set(camera.Get<CameraData>() with{Projection=CameraProjection.Orthographic,OrthographicHeight=40});return d;}
        var counts=new[]{0,1,8,32};
        foreach(int count in counts) {
            ScenePlayRuntime? runtime=null;SceneRenderSession? scene=null;bool failClose=false;
            using var cache=new RenderResourceCache(renderer);using var target=renderer.CreateViewTarget(256,256);
            using var editor=new EditorSessionOwner(stage+" joint Editor",components:CharacterComponents.Register(RenderComponentRegistry.Register(RenderConfiguration.CreateRegistry())),validateComposition:CharacterComponents.RequireComposition,
                composePreparedPlay:(p,a)=>runtime=ScenePlayRuntime.Compose(p,physics,a),beforePlayStop:()=>{if(failClose)throw new InvalidOperationException("Joint close retention");scene?.Dispose();scene=null;});
            editor.Document.RestoreSnapshot(Document(count,true).CaptureSnapshot());editor.PrepareRenderAssets(fixture.Root,fixture.Project);
            byte[] unchanged=editor.Document.CaptureBytes();Guid editWorld=editor.Document.World.Identity;
            for(int cycle=0;cycle<2;cycle++) {
                var play=editor.StartPlay();play.Pause();var actors=play.Document.World.GetObjects().Where(o=>o.Has<AnimatorData>()).Select(o=>o.PersistentId).ToArray();Guid camera=play.Document.World.GetObjects().Single(o=>o.Has<CameraData>()).PersistentId;
                scene=new(renderer,cache,play.Document.World,editor.PlayRenderAssets!,play.Document.CaptureSnapshot(),poseKernel:kernel,play:play,animators:runtime!.Animators,rootMotion:runtime.Characters);
                for(int step=0;step<8;step++) {
                    Check(play.Step().State==PlayState.Paused,"Joint Editor quantum: "+play.Fault?.Code);
                    var instances=new HashSet<Guid>();
                    foreach(Guid id in actors){var debug=runtime.Animators!.ReadDebug(id);instances.Add(debug.Frame.InstanceId);Check(debug.SnapshotValid&&(montage?runtime.Animators.ReadMontageFrame(id).Context==debug.Frame.Context:debug.Frame.FrozenPoseGeneration==(ulong)step&&debug.Events.Count==1),"Persistent policy and SAME playback owner");
                        foreach(var receipt in debug.Events)Check(receipt.Context.Tick==play.Tick&&receipt.StateId==debug.Frame.StateId,"Committed event identity");Check(runtime.Characters!.InspectRootMotion(id).Tick==play.Tick,"Unique movement and graph share committed tick");}
                    // Synchronous numerical/draw acceptance, not throughput acceptance. Present does
                    // not promise GPU completion; a fast host may legitimately fill bounded skin slots.
                    // Use the existing bounded (2s) diagnostic drain, never wait in production ticks.
                    if(count>0)renderer.WaitIdle();
                    ulong backpressure=scene.SkinBackpressureFrames,tick=play.Tick;
                    bool submitted=scene.Submit(frame++,256,256,camera,target:target);Check(instances.Count==count&&submitted==(count>0),"Joint actor isolation/native scene submission; count="+count+" cycle="+cycle+" step="+step+" instances="+instances.Count+" submitted="+submitted+" skinBackpressure="+scene.SkinBackpressureFrames+" diagnostics="+JsonSerializer.Serialize(scene.Diagnostics));if(submitted)renderer.Present();
                    Check(scene.SkinBackpressureFrames==backpressure&&play.Tick==tick,"Completed GPU drain must allow exact same committed tick presentation without skin backpressure or simulation changes");
                    if(submitted)Check(scene.Costs.GeometryDraws==count&&scene.Costs.ShadowDraws==count,"Every joint actor contributes actual geometry and shadow, not merely a prepared palette");
                    Check(count==0?scene.Animation is null&&scene.Plan is null:scene.Animation?.Costs.Characters==count,"All prepared actor palettes share one scene clock; zero actors do not initialize 3D");
                }
                if(count==32&&cycle==0){failClose=true;Reject(editor.StopPlay);Check(editor.Play==play&&kernel.Statistics.Rigs==1&&editor.PlayRenderAssets is not null,"Failed close retains joint ownership");failClose=false;}
                editor.StopPlay();Check(unchanged.SequenceEqual(editor.Document.CaptureBytes())&&editWorld==editor.Document.World.Identity&&editor.Document.World.Tick==0,"Joint Editor leaves Edit/history unchanged");
                Check(kernel.Statistics.Rigs==0&&kernel.Statistics.Clips==0&&renderer.SkinStats.Meshes==0&&physics.Inspect().Worlds==0,"Joint Editor complete drain");
            }
        }
        // The native physics plugin has one process owner; formal Players must not borrow the Editor service.
        physics.Dispose();
        // A copied/cooked graph retains exact CURRENT graph bytes; relocated packages contain no authoring files.
        string repository=Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(native)))!;string plugins=Path.Combine(native,"m2/plugins");
        var playerEvidence=new List<object>();
        foreach(int count in counts)foreach(bool root in new[]{false,true}) {
            var document=Document(count,root);string moved=Path.Combine(output,evidencePrefix+"-player",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(moved,"assets"));
            byte[] package=SceneAssetPreparation.CreateRuntimePackage(fixture.Root,fixture.Project,document.CaptureSnapshot(),[]);File.WriteAllBytes(Path.Combine(moved,"assets/game.ncpak"),package);
            if(count>0){using var packed=SceneAssetPreparation.Prepare(moved,fixture.Project,document.CaptureSnapshot(),true,"assets/game.ncpak");var restored=((RuntimeAnimationGraphAsset)packed.Assets.Require(graph.AssetId,Ncma.Assets.AssetKind.AnimationGraph)).CopyDefinition();var semantic=graph with{Nodes=graph.Nodes.Select(n=>n with{X=0,Y=0}).ToArray()};Check((montage?restored.Montage is not null&&restored.Nodes.Any(n=>n.PlayOnStart):restored.InterruptTransitions)&&restored.Nodes.All(n=>n.X==0&&n.Y==0)&&AnimationGraphCodec.Encode(restored).SequenceEqual(AnimationGraphCodec.Encode(semantic)),"Cook strips layout only and preserves every current semantic field/UUID/policy/event/Montage");}
            SceneDocumentFiles.Save(document,Path.Combine(moved,"start.ncmascene"));File.Copy(Path.Combine(repository,"out/managed/Ncma.Gameplay.Sample.dll"),Path.Combine(moved,"gameplay.dll"));
            Guid camera=document.World.GetObjects().Single(o=>o.Has<CameraData>()).PersistentId;
            var configuration=new Ncma.Application.ProjectConfiguration(1,fixture.Project,"Joint Player","start.ncmascene","gameplay.dll","Direct3D11",[],root,camera,AssetPackage:"assets/game.ncpak");
            string project=Path.Combine(moved,"joint.ncmaproject");File.WriteAllBytes(project,JsonSerializer.SerializeToUtf8Bytes(configuration,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
            foreach(bool headless in new[]{true,false}) {
                string reportPath=Path.Combine(moved,headless?"headless.json":"dx11.json");Ncma.Player.App.PlayerReport report;
                if(headless)report=Ncma.Player.App.PlayerRunner.Run(Ncma.Player.App.PlayerOptions.Parse(["--project",project,"--headless","--ticks","8","--report",reportPath]),pluginRoot:root?plugins:Path.Combine(moved,"no-native"),visible:false);
                else {var start=new System.Diagnostics.ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(string arg in new[]{typeof(Program).Assembly.Location,"--graph-root-player-child",plugins,project,reportPath})start.ArgumentList.Add(arg);
                    using var child=System.Diagnostics.Process.Start(start)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();Check(child.WaitForExit(30000),"Joint Player bounded process timeout");Check(child.ExitCode==0,"Joint Player: "+stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult());
                    var json=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase};json.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());report=JsonSerializer.Deserialize<Ncma.Player.App.PlayerReport>(File.ReadAllBytes(reportPath),json)!;}
                Check(report.ExitCode==0&&report.Tick>=8&&report.ShutdownErrors.Length==0&&report.ValidationErrors==0&&report.ValidationWarnings==0,"Formal packed joint Player: "+report.Reason);
                Check(headless||report.RenderedFrames>0,"Formal DX11 Player actually renders");
                Check(!headless||report.RenderedFrames==0&&report.Modules.All(m=>m.Id is not ("ncma.renderer" or "ncma.platform")),"Headless has no GPU/platform");Check(!headless||root||report.Modules.Length==0,"Non-physics Headless has no native modules");
                playerEvidence.Add(new{count,root,headless,tick=report.Tick,nativeModules=report.Modules.Length,validationErrors=report.ValidationErrors,validationWarnings=report.ValidationWarnings});
            }
            Check(!Directory.Exists(Path.Combine(moved,"out"))&&Directory.GetFiles(Path.Combine(moved,"assets")).Length==1,"Moved Player does not use source or import cache");
        }
        File.WriteAllText(Path.Combine(output,evidencePrefix+"-joint-results.json"),JsonSerializer.Serialize(new{schema=2,graphVersion=graph.Version,montage,blendSpace=graph.Nodes.Any(n=>n.BlendSpace is not null),layers=graph.Nodes.Any(n=>n.Layer is not null),cache=graph.Nodes.Any(n=>n.Kind==AnimationNodeKind.CachePose),editorActors=counts,editorCyclesPerCount=2,ticksPerCycle=8,committedTargetEvents=true,interruptions=graph.InterruptTransitions,uniqueJolt=true,editUnchanged=true,closeFailureRetention=true,players=playerEvidence,manualAccepted=false}));
        Console.WriteLine("PASS "+stage+" current graph events/persisted policy/unique Jolt/skin-shadow Editor 0-1-8-32, moved source-free Headless/DX11 Player16 cases");
    }
}
