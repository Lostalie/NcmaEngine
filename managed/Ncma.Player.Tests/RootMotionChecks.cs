using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Animation;
using Ncma.Assets;
using Ncma.Characters;
using Ncma.Gameplay;
using Ncma.Physics;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;
using Ncma.Player.App;
using Ncma.Application;
using Ncma.Animation.Native;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Platform;
using Ncma.Interop;
using Ncma.Asset.Import;
using Ncma.Assets.Authoring;
internal static class RootMotionChecks
{
    private static void Check(bool v,string m){if(!v)throw new Exception(m);}
    private static void Reject(Action a){try{a();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or IOException){return;}throw new Exception("Expected root rejection");}
    private sealed class StepAction(Action<World> action):IWorldSystem { public void FixedUpdate(World w,double h)=>action(w); }
    internal static string[] Run(string repository,string output,string plugins)
    {
        var passed=new List<string>();
        var identity=new ImportTransform(Vector3.Zero,Quaternion.Identity,Vector3.One);
        var skeleton=new SkeletonPayload([new("root",-1,identity),new("child",0,identity with{Position=Vector3.UnitY})]);
        ClipPayload Clip(string name,Vector3 end,float yaw=0,double duration=1)=>new(2,new(name,duration,[new(0,[new(0,identity),new(duration,identity with{Position=end,Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw)})])]));
        var track=new RootMotionTrack(skeleton,Clip("translation",new(3,5,2)),0);
        Check(Vector3.Distance(track.Extract(new(.9,1.1),true).Translation,new(.6f,0,.4f))<1e-5f,"loop boundary translation oracle");
        Check(Vector3.Distance(track.Extract(new(.2,3.2),true).Translation,new(9,0,6))<1e-5f,"three loops oracle");
        Check(track.Extract(new(1,1),false).Translation==Vector3.Zero,"exact endpoint no duplicate");
        Reject(()=>track.Extract(new(0,33),true));Reject(()=>track.Extract(new(1,0),true));
        Reject(()=>new RootMotionTrack(skeleton,Clip("bad scale",Vector3.One) with{Clip=new("bad",1,[new(0,[new(0,identity with{Scale=new(2)}),new(1,identity)])])},0));
        Reject(()=>new RootMotionTrack(skeleton,Clip("bad index",Vector3.One),1));
        var turn=new RootMotionTrack(skeleton,Clip("turn",new(2,0,0),MathF.PI/2),0);
        var one=turn.Extract(new(0,1),true);var two=turn.Extract(new(1,2),true);
        Matrix4x4 a=Matrix4x4.CreateRotationY(one.Yaw)*Matrix4x4.CreateTranslation(one.Translation),b=Matrix4x4.CreateRotationY(two.Yaw)*Matrix4x4.CreateTranslation(two.Translation);
        var expected=Matrix4x4.CreateRotationY(MathF.PI/2)*Matrix4x4.CreateTranslation(2,0,0);
        Check(Vector3.Distance((b*a).Translation,(expected*expected).Translation)<1e-5f,"noncommuting loop yaw/translation oracle");
        Matrix4x4[] models=[Matrix4x4.CreateRotationY(.3f)*Matrix4x4.CreateTranslation(1,2,3),Matrix4x4.CreateTranslation(0,1,0)*Matrix4x4.CreateRotationY(.3f)*Matrix4x4.CreateTranslation(1,2,3)];
        turn.RemoveRoot(models);Check(Vector3.Distance(models[0].Translation,new(0,2,0))<1e-5f && Vector3.Distance(models[1].Translation,new(0,3,0))<1e-5f,"remove desired XZ/yaw, retain Y and descendant pose");
        passed.Add("K4 immutable root interval oracle: boundary/multiple loops/end/yaw and visual channel removal");

        string root=Path.Combine(output,"Root motion");Directory.CreateDirectory(Path.Combine(root,"assets"));
        Guid project=Guid.NewGuid(),model=Guid.NewGuid(),mesh=Guid.NewGuid(),rig=Guid.NewGuid(),slots=Guid.NewGuid(),clip=Guid.NewGuid(),other=Guid.NewGuid();
        var settings=new ImportSettings(1,30,true);string sourceHash=new('B',64);
        ImportVertex Vertex(float x,float y)=>new(new(x,y,0),Vector3.UnitZ,default,default,new(1,0,0,0));
        var payload=new MeshPayload(true,2,[Vertex(-.3f,0),Vertex(.3f,0),Vertex(-.3f,1.8f)],[],[0,1,2],[0],[new(0,AssetMatrices.EncodeColumnMajor(Matrix4x4.Identity))],1);
        var manifest=new ModelAssetManifest(1,model,false,sourceHash,settings,rig,[new(mesh,slots)],[clip,other]);
        byte[] bytes=DerivedAssetCodec.Encode([new(model,AssetKind.Character,ModelAssetManifestCodec.Encode(manifest)),new(rig,AssetKind.Skeleton,ModelPayloadCodec.Encode(skeleton)),
            new(mesh,AssetKind.SkinnedMesh,ModelPayloadCodec.Encode(payload)),new(slots,AssetKind.MaterialSet,ModelPayloadCodec.Encode(new MaterialSlotsPayload(["Default"]))),
            new(clip,AssetKind.Clip,ModelPayloadCodec.Encode(Clip("run root",new(3,0,1)))),new(other,AssetKind.Clip,ModelPayloadCodec.Encode(Clip("turn",Vector3.Zero,.5f)))]);
        string hash=Convert.ToHexString(SHA256.HashData(bytes)),relative=$"out/assets/{project:N}/{model:N}/1-{hash}.nca",generation=Path.Combine(root,relative);
        Directory.CreateDirectory(Path.GetDirectoryName(generation)!);File.WriteAllBytes(generation,bytes);
        var record=new AssetRecord(1,model,AssetKind.Character,"assets/model.fbx",sourceHash,"ufbx",1,settings,
            [new(mesh,AssetKind.SkinnedMesh,"mesh/0","Character",false),new(rig,AssetKind.Skeleton,"rig/0","Rig",false),new(slots,AssetKind.MaterialSet,"materials/0","Slots",false),new(clip,AssetKind.Clip,"clip/0","Run",false),new(other,AssetKind.Clip,"clip/1","Turn",false)],[],new(1,hash,relative));
        string metadata=Path.Combine(root,"assets/model.fbx.ncmeta");File.WriteAllBytes(metadata,AssetRecordCodec.Encode(record));
        var doc=CharacterHostChecks.Fixture();var actor=doc.World.GetObjects().Single(o=>o.Has<CharacterData>());Guid actorId=actor.PersistentId;
        actor.Set(new SkinnedMeshData(model,mesh,rig,slots,true,true,uint.MaxValue));actor.Set(new ClipPlaybackData(clip,true,true,1,0));actor.Set(new RootMotionData(0));
        var light=doc.World.CreateObject("Light");light.Set(TransformData.Identity);light.Set(DirectionalLightData.Default);
        using var assets=SceneAssetPreparation.Prepare(root,project,doc.CaptureSnapshot(),true);
        using var physics=new PhysicsService(plugins,characterSupport:true);
        var outcomes=new List<TransformData>();
        foreach(int hz in new[]{30,60,144,0}) {
            var copy=doc.CreateIsolatedCopy();using var play=new PlaySession(copy,FrameTimePolicy.Strict,advanceMode:hz==0?PlayAdvanceMode.FixedSteps:PlayAdvanceMode.Frames);
            using var runtime=CharacterPlayRuntime.Compose(play,physics,assets)!;play.Start(_=>throw new Exception("no behaviours"));
            // D input MUST NOT add locomotion on top of authored diagonal root motion.
            CharacterHostChecks.Input(play,1);
            for(int i=0;i<(hz==0?120:hz*2);i++)Check((hz==0?play.AdvanceFixedStep():play.AdvanceFrame(1d/hz)).State==PlayState.Running,"actual root collision step");
            var result=copy.World.FindObject(actorId).Get<TransformData>();var status=runtime.InspectRootMotion(actorId);
            Check(play.Tick==120 && status.Tick==120 && Math.Abs(status.Time-2)<1e-10,"sole clock matches committed ticks");
            Check(result.Position.X>1.45f&&result.Position.X<1.51f&&result.Position.Z>1.9f&&result.Position.Z<2.1f,"wall blocks X while desired Z slides");
            Check(status.DesiredDisplacement.X>.04f&&status.AcceptedDisplacement.X<.001f,"separate desired and blocked actual displacement");
            outcomes.Add(result);play.Pause();var stopped=runtime.InspectRootMotion(actorId);Reject(()=>runtime.SetRootPlayback(actorId,new(Guid.NewGuid(),true,true,1,0)));
            runtime.SetRootPlayback(actorId,new(clip,false,true,2,0));play.Step();Check(runtime.InspectRootMotion(actorId).Time==stopped.Time,"paused clip does not consume root interval");
            runtime.SetRootPlayback(actorId,new(other,true,false,2,0));play.Step();Check(Math.Abs(runtime.InspectRootMotion(actorId).Time-2d/60)<1e-10,"clip switch resets then consumes one interval");
            for(int i=0;i<60;i++)play.Step();Check(runtime.InspectRootMotion(actorId).Time==1,"nonloop end clamps");
            play.Stop();
        }
        Check(outcomes.All(t=>Vector3.Distance(t.Position,outcomes[0].Position)<1e-5f),"render rates/headless parity");
        passed.Add("K4 actual Jolt wall/sliding/root replaces input, 30/60/144/headless shared clock, speed/pause/switch/end");
        foreach(bool late in new[]{false,true}) {
            var copy=doc.CreateIsolatedCopy();using var play=new PlaySession(copy,FrameTimePolicy.Strict,advanceMode:PlayAdvanceMode.FixedSteps);using var runtime=CharacterPlayRuntime.Compose(play,physics,assets)!;
            play.AddSystem(new StepAction(w=>{if(late){var pending=play.Commands.SpawnEmpty("late");play.Commands.AttachBehaviour(pending.ObjectId,new(Guid.NewGuid(),"Missing",true,[]));}else {try{w.FindObject(actorId).Set(new ClipPlaybackData(clip,true,true,8,0));}catch(InvalidOperationException){}}}));
            byte[] before=copy.CaptureBytes();play.Start(_=>throw new Exception("injected factory"));Check(play.AdvanceFixedStep().State==PlayState.Faulted&&play.Tick==0,"root failed step not committed");
            Check(runtime.Status.NumericalExecutionStarted==late && before.SequenceEqual(copy.CaptureBytes()),"before/after solver fault boundary");Reject(()=>runtime.InspectRootMotion(actorId));play.Stop();
        }
        passed.Add("K4 caught clock writes poison before solver; managed post-solver failure aborts tick and invalidates root presentation");
        // Publish a newer immutable generation while existing sessions retain the old pin.
        string secondRelative=$"out/assets/{project:N}/{model:N}/2-{hash}.nca";File.WriteAllBytes(Path.Combine(root,secondRelative),bytes);
        File.WriteAllBytes(metadata,AssetRecordCodec.Encode(record with{Generation=new(2,hash,secondRelative)}));
        using(var refreshed=SceneAssetPreparation.Prepare(root,project,doc.CaptureSnapshot(),true))Check(refreshed.Metadata.TryFind(clip,out var info)&&info!.Generation==2,"new preparation selects new committed generation");
        Reject(()=>{using var write=new FileStream(generation,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite|FileShare.Delete);});
        for(int cycle=0;cycle<8;cycle++){
            var copy=doc.CreateIsolatedCopy();using var play=new PlaySession(copy,FrameTimePolicy.Strict,advanceMode:PlayAdvanceMode.FixedSteps);using var runtime=CharacterPlayRuntime.Compose(play,physics,assets)!;
            play.Start(_=>throw new Exception("no behaviours"));play.AdvanceFixedStep();Guid old=play.SessionId,epoch=runtime.NumericalEpoch;
            play.Reload(_=>throw new Exception("no behaviours"));Check(play.State==PlayState.Paused&&play.SessionId!=old&&runtime.NumericalEpoch!=epoch&&runtime.InspectRootMotion(actorId).Time==0,"reload frozen startup resets clock with fresh identity");
            play.Step();Check(Math.Abs(runtime.InspectRootMotion(actorId).Time-1d/60)<1e-10,"reloaded clock consumes only new success");play.Stop();Check(physics.Inspect().Worlds==0,"root domains drained");
        }
        passed.Add("K4 pinned old generation survives reimport; frozen-startup Reload and repeated close/reset");
        // Native compute skin evidence: remove DESIRED motion even when collision rejects the move.
        using(var loader=new PluginLoader()) {
            loader.Load(plugins,[new("platform",ModuleKind.Platform,"NcmaPlatform.dll","NcmaPlatform.dll",1,0,[]),new("renderer",ModuleKind.Renderer,"NcmaRenderer.dll","NcmaRenderer.dll",1,2,["platform"])]);
            using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"K4 skin",320,240,false);
            using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,320,240);using var cache=new RenderResourceCache(renderer);
            string kernelPath=Path.Combine(plugins,"NcmaAnimationKernel.dll");using var kernel=new PoseKernel(kernelPath,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(kernelPath))));
            var copy=doc.CreateIsolatedCopy();using var play=new PlaySession(copy,FrameTimePolicy.Strict,advanceMode:PlayAdvanceMode.FixedSteps);using var runtime=CharacterPlayRuntime.Compose(play,physics,assets)!;play.Start(_=>throw new Exception("no behaviours"));
            try { using(var scene=new SceneRenderSession(renderer,cache,copy.World,assets,copy.CaptureSnapshot(),poseKernel:kernel,play:play,interpolateTransforms:true,rootMotion:runtime)) {
                var captured=new byte[3*48];Guid camera=copy.World.GetObjects().Single(o=>o.Has<CameraData>()).PersistentId;
                for(ulong frame=1;frame<=6;frame++) {
                    for(int i=0;i<20;i++)play.AdvanceFixedStep();scene.Submit(frame,320,240,camera);renderer.Present();scene.CaptureCharacterVertices(actorId,captured);
                    var floats=System.Runtime.InteropServices.MemoryMarshal.Cast<byte,float>(captured);Check(Math.Abs(floats[0]+.3f)<1e-4f&&Math.Abs(floats[2])<1e-4f,"GPU visual root remains in-place through wall/loops");
                    ulong tick=play.Tick;scene.Animation!.Evaluate(.2f,false);scene.Animation.Evaluate(.8f,false);Check(play.Tick==tick,"render interpolation never repeats movement");
                }
                Check(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"GPU validation 0/0");
            }}finally{play.Stop();}Check(kernel.Statistics.Rigs==0&&kernel.Statistics.Clips==0,"derived pose leases drained");
        }
        passed.Add("K4 native GPU skin root de-duplication under wall collision, shared main/shadow palette, render interpolation read-only");
        physics.Dispose();
        // Formal Player entries and NCP1 use the same policy, with no pose/GPU initialization in Headless.
        string assembly=Assembly.GetExecutingAssembly().Location;File.Copy(assembly,Path.Combine(root,"gameplay.dll"));File.Copy(Path.ChangeExtension(assembly,".deps.json"),Path.Combine(root,"gameplay.deps.json"));
        SceneDocumentFiles.Save(doc,Path.Combine(root,"start.ncmascene"));
        var config=new ProjectConfiguration(1,project,"Root Player","start.ncmascene","gameplay.dll","Direct3D11",[],true,doc.World.GetObjects().Single(o=>o.Has<CameraData>()).PersistentId);
        File.WriteAllBytes(Path.Combine(root,"assets/game.ncpak"),SceneAssetPreparation.CreateRuntimePackage(root,project,doc.CaptureSnapshot(),[]));
        foreach(bool packed in new[]{false,true})foreach(bool headless in new[]{false,true}) {
            string file=Path.Combine(root,"root.ncmaproject");File.WriteAllBytes(file,JsonSerializer.SerializeToUtf8Bytes(config with{AssetPackage=packed?"assets/game.ncpak":null},new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
            var flags=new List<string>{"--project",file,"--ticks","30","--report",Path.Combine(root,$"{packed}-{headless}.json")};if(headless)flags.Add("--headless");
            var report=PlayerRunner.Run(PlayerOptions.Parse(flags.ToArray()),pluginRoot:plugins,visible:false);
            Check(report.ExitCode==0&&report.Tick>=30&&report.ShutdownErrors.Length==0&&report.ValidationErrors==0&&report.ValidationWarnings==0,"formal root Player: "+report.Reason);
            Check(!headless||report.Modules.All(m=>m.Id is not ("ncma.renderer" or "ncma.platform")),"Headless root path never initializes graphics");
        }
        passed.Add("K4 formal Headless/DX11 and packed/unpacked Player root-motion path, validation 0/0");
        // Genuine imported FBX rig/clip comparison against the existing pose numerical kernel.
        string importPath=Path.Combine(Directory.GetParent(Directory.GetParent(plugins)!.FullName)!.FullName,"NcmaImportKernel.dll");
        using(var importer=new ImportKernel(importPath,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(importPath))))) {
            var imported=importer.LoadAndCopy(Path.Combine(repository,"tests/assets/fbx/blender_279_sausage_7400_binary.fbx"),30);
            var realRig=new SkeletonPayload(imported.Bones);var realClip=new ClipPayload(imported.Bones.Length,imported.Clips[0]);
            var realTrack=new RootMotionTrack(realRig,realClip,0);var delta=realTrack.Extract(new(0,.1),false);
            Check(float.IsFinite(delta.Translation.LengthSquared()),"real imported root interval is finite");
        }
        passed.Add("K4 genuine FBX rig/clip root sampling (not user attack/dodge material acceptance)");
        return passed.ToArray();
    }
}
