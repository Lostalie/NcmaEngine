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
using Ncma.Application;
using Ncma.Player.App;

internal static class ActionCombatChecks
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Reject(Action action){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or IOException or JsonException){return;}throw new Exception("Expected action rejection");}
    private sealed class StepAction(Action<World> action):IWorldSystem {public void FixedUpdate(World world,double h)=>action(world);}
    private sealed class BadObserver:ICommittedStepObserver {public void StepCommitted(World world,double h)=>throw new IOException("Injected post-commit observer failure");}
    private sealed class Observer(Action observe):ICommittedStepObserver {public void StepCommitted(World world,double h)=>observe();}
    internal static string[] Run(string output,string plugins)
    {
        var passed=new List<string>();
        string root=Path.Combine(output,"Action combat");Directory.CreateDirectory(Path.Combine(root,"assets"));
        Guid project=Guid.NewGuid(),model=Guid.NewGuid(),mesh=Guid.NewGuid(),rig=Guid.NewGuid(),slots=Guid.NewGuid();
        Guid[] clips=Enumerable.Range(0,4).Select(_=>Guid.NewGuid()).ToArray();
        var identity=new ImportTransform(Vector3.Zero,Quaternion.Identity,Vector3.One);
        var skeleton=new SkeletonPayload([new("root",-1,identity),new("hand",0,identity with{Position=Vector3.UnitY})]);
        // Four separate numerical clips: unique root/hand tracks, not four aliases of sausage.
        ClipPayload Clip(int i)=>new(2,new(new[]{"Idle","Run","Attack","Dodge"}[i],.2,
            [new(0,[new(0,identity),new(.2,identity with{Position=i==2?new(0,0,-.04f):i==3?new(.01f,0,0):Vector3.Zero})]),
             new(1,[new(0,identity with{Position=Vector3.UnitY}),new(.2,identity with{Position=Vector3.UnitY,Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,(i+1)*.25f)})])]));
        ImportVertex Vertex(float x,float y)=>new(new(x,y,0),Vector3.UnitZ,default,default,new(1,0,0,0));
        var payload=new MeshPayload(true,2,[Vertex(-.3f,0),Vertex(.3f,0),Vertex(-.3f,1.8f)],[],[0,1,2],[0],[new(0,AssetMatrices.EncodeColumnMajor(Matrix4x4.Identity))],1);
        var settings=new ImportSettings(1,30,true);string sourceHash=new('C',64);
        var manifest=new ModelAssetManifest(1,model,false,sourceHash,settings,rig,[new(mesh,slots)],clips);
        var blocks=new List<DerivedAssetBlock>{new(model,AssetKind.Character,ModelAssetManifestCodec.Encode(manifest)),new(rig,AssetKind.Skeleton,ModelPayloadCodec.Encode(skeleton)),
            new(mesh,AssetKind.SkinnedMesh,ModelPayloadCodec.Encode(payload)),new(slots,AssetKind.MaterialSet,ModelPayloadCodec.Encode(new MaterialSlotsPayload(["Default"])))};
        blocks.AddRange(clips.Select((id,i)=>new DerivedAssetBlock(id,AssetKind.Clip,ModelPayloadCodec.Encode(Clip(i)))));
        byte[] bytes=DerivedAssetCodec.Encode(blocks);string hash=Convert.ToHexString(SHA256.HashData(bytes)),relative=$"out/assets/{project:N}/{model:N}/1-{hash}.nca";
        string generation=Path.Combine(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(generation)!);File.WriteAllBytes(generation,bytes);
        var sub=new List<SubassetRecord>{new(mesh,AssetKind.SkinnedMesh,"mesh/0","Character",false),new(rig,AssetKind.Skeleton,"rig/0","Rig",false),new(slots,AssetKind.MaterialSet,"materials/0","Slots",false)};
        sub.AddRange(clips.Select((id,i)=>new SubassetRecord(id,AssetKind.Clip,$"clip/{i}",new[]{"Idle","Run","Attack","Dodge"}[i],false)));
        var record=new AssetRecord(1,model,AssetKind.Character,"assets/model.fbx",sourceHash,"ufbx",1,settings,sub.ToArray(),[],new(1,hash,relative));
        File.WriteAllBytes(Path.Combine(root,"assets/model.fbx.ncmeta"),AssetRecordCodec.Encode(record));
        var definition=new ActionDefinitionData(Guid.NewGuid(),clips[0],clips[1],clips[2],clips[3],0,.5f,.25f,.75f,.5f,1,0,1,25,3,2,2);
        SceneDocument Fixture(bool opponent=false)
        {
            var d=CharacterHostChecks.Fixture();Guid actor=d.GetObjectIds().Single(o=>d.HasComponent<CharacterData>(o));
            Equip(d,actor);Guid target=d.CreateObject("Combat target",Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
            d.SetComponent(target, TransformData.Identity with{Position=new(0,opponent?.1f:1,-1.5f)});
            if(opponent){d.SetComponent(target, CharacterData.Default with{Controlled=false});Equip(d,target);}else{d.SetComponent(target, new BoxColliderData(.35f,.9f,.35f,1000,2,false));d.SetComponent(target, new HealthData(100,100));}
            Guid light=d.CreateObject("Light");d.SetComponent(light, TransformData.Identity);d.SetComponent(light, DirectionalLightData.Default);return d;
        }
        void Equip(SceneDocument document,Guid actor){document.SetComponent(actor, new SkinnedMeshData(model,mesh,rig,slots,true,true,uint.MaxValue));document.SetComponent(actor, new ClipPlaybackData(clips[0],true,true,1,0));document.SetComponent(actor, new RootMotionData(0));document.SetComponent(actor, new HealthData(100,100));document.SetComponent(actor, definition);}
        var startup=Fixture();Guid actorId=startup.GetObjectIds().Single(o=>startup.HasComponent<CharacterData>(o)),targetId=Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var registry=startup.Components;
        Check(registry.Decode<ActionDefinitionData>(new(ActionDefinitionData.TypeId,1,registry.Encode(definition)))==definition,"definition closed schema round trip");
        Reject(()=>ActionDefinitionData.Validate(definition with{DodgeClip=clips[2]}));Reject(()=>ActionDefinitionData.Validate(definition with{HitEnd=0}));Reject(()=>HealthData.Validate(new(-1,100)));
        string json=registry.Encode(definition).GetRawText();Reject(()=>registry.Decode<ActionDefinitionData>(new(ActionDefinitionData.TypeId,1,JsonDocument.Parse(json[..^1]+",\"extra\":1}").RootElement)));
        var missing=Fixture();missing.SetComponent(actorId, definition with{AttackClip=Guid.NewGuid()});
        Reject(()=>{using var invalid=SceneAssetPreparation.Prepare(root,project,missing.CaptureSnapshot(),true);});
        using var assets=SceneAssetPreparation.Prepare(root,project,startup.CaptureSnapshot(),true);
        var references=SceneAssetPreparation.References(startup.CaptureSnapshot());Check(clips.All(id=>references.Any(r=>r.Id.Value==id&&r.ExpectedKind==AssetKind.Clip)),"four typed action references");
        Check(assets.Metadata.TryFind(clips[3],out _),"all four clips prepared");
        byte[] package=SceneAssetPreparation.CreateRuntimePackage(root,project,startup.CaptureSnapshot(),[]);File.WriteAllBytes(Path.Combine(root,"assets/game.ncpak"),package);
        passed.Add("K5 strict action/health schema, distinct typed clip references and packed generation closure");

        using var physics=new PhysicsService(plugins,characterSupport:true);
        void Session(SceneDocument document,Action<PlaySession,CharacterPlayRuntime> test,Action<PlaySession,CharacterPlayRuntime>? setup=null,bool frames=false)
        {
            var runtimeScene=document.BakeRuntime(document.Revision).Image.Instantiate(document.Components);
            byte[] authoring=document.CaptureBytes();
            using var play=new PlaySession(runtimeScene,FrameTimePolicy.Strict,advanceMode:frames?PlayAdvanceMode.Frames:PlayAdvanceMode.FixedSteps);
            using var runtime=CharacterPlayRuntime.Compose(play,physics,assets)!;setup?.Invoke(play,runtime);
            try{play.Start(_=>throw new Exception("Injected missing behaviour"));test(play,runtime);}finally{play.Stop();}
            Check(physics.Inspect().Worlds==0,"combat numerical worlds drained");
            Check(authoring.SequenceEqual(document.CaptureBytes()),"combat never writes the authoring document");
        }
        static void Step(PlaySession play){var s=play.AdvanceFixedStep();Check(s.State==PlayState.Running,"Combat step failed: "+s.Fault);}
        Session(Fixture(),(play,runtime)=>{
            Step(play);Check(runtime.InspectAction(actorId).State==CharacterAction.Idle,"initial idle");
            CharacterHostChecks.Input(play,1,key:87);Step(play);Check(runtime.InspectAction(actorId).State==CharacterAction.Run,"input run");
            CharacterHostChecks.Input(play,2,key:-1);Step(play);Check(runtime.InspectAction(actorId).State==CharacterAction.Idle,"input idle");
            CharacterHostChecks.Input(play,3,key:74,pressed:true);Step(play);var first=runtime.InspectAction(actorId);
            Check(first.State==CharacterAction.Attack&&play.Scene.World.Get<HealthData>(play.Scene.World.FindEntity(targetId)).Current==75,"actual Jolt ray hit staged damage");
            var events=runtime.ReadCombatEvents();Check(events.Count(e=>e.Kind==CombatEventKind.Hit)==1&&events.Any(e=>e.Kind==CombatEventKind.HitOpen),"start notify and hit");
            Check(events.All(e=>e.Tick==play.Tick&&e.SessionId==play.SessionId&&e.WorldId==play.Scene.World.Identity&&e.NotifyId!=Guid.Empty&&e.Instance==(e.Kind==CombatEventKind.Exit?first.Instance-1:first.Instance)),"committed event identities");
            events[0]=default;Check(runtime.ReadCombatEvents()[0].NotifyId!=Guid.Empty,"copied event snapshot");
            for(int i=0;i<30;i++){Step(play);Check(play.Scene.World.Get<HealthData>(play.Scene.World.FindEntity(targetId)).Current==75,"held attack cannot repeat; instance target dedupe");}
            Check(runtime.InspectAction(actorId).State==CharacterAction.Idle,"attack ended without repeat");
            Reject(()=>runtime.SetRootPlayback(actorId,new(clips[2],true,false,1,0)));Reject(()=>runtime.RequestAction(Guid.NewGuid(),ActionRequest.Attack));
            Reject(()=>Task.Run(()=>runtime.RequestAction(actorId,ActionRequest.Attack)).GetAwaiter().GetResult());
        });
        passed.Add("K5 Idle/Run/Attack transitions, input edge once, actual Jolt hit/damage dedupe and copied stamped events");
        Session(Fixture(),(play,runtime)=>{
            runtime.RequestAction(actorId,ActionRequest.Attack);Step(play);ulong first=runtime.InspectAction(actorId).Instance;
            while(runtime.InspectAction(actorId).Time<.11)Step(play);
            runtime.RequestAction(actorId,ActionRequest.Attack);Step(play);Check(runtime.InspectAction(actorId).ComboQueued,"combo request consumed inside window");
            for(int i=0;i<20&&runtime.InspectAction(actorId).Instance==first;i++)Step(play);
            Check(runtime.InspectAction(actorId).State==CharacterAction.Attack&&runtime.InspectAction(actorId).Instance==first+1&&runtime.InspectAction(actorId).Time<.02,"same clip re-entry resets sole clock");
            Check(play.Scene.World.Get<HealthData>(play.Scene.World.FindEntity(targetId)).Current==50,"new attack instance may hit same target");
        });
        Session(Fixture(),(play,runtime)=>{
            runtime.RequestAction(actorId,ActionRequest.Attack);Step(play);ulong attack=runtime.InspectAction(actorId).Instance;
            while(runtime.InspectAction(actorId).Time<.06)Step(play);
            runtime.RequestAction(actorId,ActionRequest.Dodge);Step(play);
            var e=runtime.ReadCombatEvents();Check(runtime.InspectAction(actorId).State==CharacterAction.Dodge&&e.Any(x=>x.Kind==CombatEventKind.HitClose&&x.Instance==attack)&&e.Any(x=>x.Kind==CombatEventKind.Exit&&x.Instance==attack)&&e.Any(x=>x.Kind==CombatEventKind.InvulnerableOpen),"cancel closes interrupted hit window before dodge enters");
            int closes=0;for(int i=0;i<18;i++){Step(play);closes+=runtime.ReadCombatEvents().Count(x=>x.Kind==CombatEventKind.InvulnerableClose);}
            Check(closes==1,"exact nonloop terminal notify emitted once");
        });
        Session(Fixture(),(play,runtime)=>{
            runtime.RequestAction(actorId,ActionRequest.Dodge);Step(play);runtime.RequestAction(actorId,ActionRequest.Attack);Step(play);
            for(int i=0;i<30;i++)Step(play);
            Check(play.Scene.World.Get<HealthData>(play.Scene.World.FindEntity(targetId)).Current==100&&runtime.InspectAction(actorId).State==CharacterAction.Idle,"expired input buffer does not trigger later action");
        });
        passed.Add("K5 combo same-clip restart, interruption closes windows, terminal Notify once and bounded buffer expiry");
        Session(Fixture(true),(play,runtime)=>{
            runtime.RequestAction(actorId,ActionRequest.Attack);runtime.RequestAction(targetId,ActionRequest.Dodge);Step(play);
            Check(play.Scene.World.Get<HealthData>(play.Scene.World.FindEntity(targetId)).Current==100&&runtime.ReadCombatEvents().Any(e=>e.Kind==CombatEventKind.Blocked&&e.Target==targetId),"candidate same-quantum invulnerability blocks actual query");
            for(int i=0;i<20;i++)Step(play);
            runtime.RequestAction(actorId,ActionRequest.Attack);Step(play);Check(play.Scene.World.Get<HealthData>(play.Scene.World.FindEntity(targetId)).Current==75,"new instance hits after invulnerability");
        });
        var dead=Fixture();dead.SetComponent(actorId, new HealthData(0,100));
        Session(dead,(play,runtime)=>{CharacterHostChecks.Input(play,1,key:68);runtime.RequestAction(actorId,ActionRequest.Attack);for(int i=0;i<20;i++)Step(play);Check(Math.Abs(play.Scene.World.Get<TransformData>(play.Scene.World.FindEntity(actorId)).Position.X)<.001f&&runtime.InspectAction(actorId).State==CharacterAction.Idle,"dead actor cannot move or attack");});
        passed.Add("K5 simultaneous invulnerability/attack, later damage and dead input/root authority");

        foreach(int fault in new[]{0,1,2,3,4}) {
            var doc=Fixture();
            Session(doc,(play,runtime)=>{
                byte[] before=play.Scene.CaptureCheckpoint().CopyBytes();
                runtime.RequestAction(actorId,ActionRequest.Attack);var result=play.AdvanceFixedStep();
                Check(result.State==PlayState.Faulted&&play.Tick==(fault==4?1UL:0UL),"fault preserves exact managed commit boundary");
                Check(runtime.Status.NumericalExecutionStarted==(fault==3)&&runtime.Status.CommittedSequence==(fault==4?1UL:0UL),"before/after irreversible solver fault classification");
                if(fault!=4)Check(before.SequenceEqual(play.Scene.CaptureCheckpoint().CopyBytes()),"failed quantum never installs damage/transform");
                else Check(play.Scene.World.Get<HealthData>(play.Scene.World.FindEntity(targetId)).Current==75,"post-commit fault retains already committed damage");
                Reject(()=>runtime.InspectAction(actorId));Reject(()=>runtime.ReadCombatEvents());Reject(()=>runtime.InspectRootMotion(actorId));
            },(play,runtime)=>{
                if(fault==4)play.AddCommittedObserver(new BadObserver());
                else play.AddSystem(new StepAction(w=>{
                    if(fault==3){var pending=play.Commands.SpawnEmpty("late");play.Commands.AttachBehaviour(pending.ObjectId,new(Guid.NewGuid(),"Missing",true,[]));}
                    else try{if(fault==0)w.Set(w.FindEntity(targetId), new HealthData(1,100));else if(fault==1)w.Set(w.FindEntity(actorId), definition with{Damage=100});else runtime.RequestAction(actorId,ActionRequest.Dodge);}catch(InvalidOperationException){}
                }));
            });
        }
        passed.Add("K5 caught health/definition/control violations poison before solver, late rollback and post-commit fail-stop preserve truthful boundaries");

        Session(Fixture(),(play,runtime)=>{
            runtime.RequestAction(actorId,ActionRequest.Attack);Step(play);var state=runtime.InspectAction(actorId);Guid session=play.SessionId;
            play.Pause();Reject(()=>play.AdvanceFrame(.1));Check(runtime.InspectAction(actorId)==state,"paused action clock/events unchanged");
            play.Step();Check(play.Tick==2&&runtime.InspectAction(actorId).Time>state.Time,"paused single step");
            play.Reload(_=>throw new Exception("no behaviours"));Check(play.State==PlayState.Paused&&play.SessionId!=session&&runtime.InspectAction(actorId).Instance==0&&runtime.ReadCombatEvents().Length==0&&play.Scene.World.Get<HealthData>(play.Scene.World.FindEntity(targetId)).Current==100,"reload frozen startup clears private actions/damage/events with fresh identity");
            play.Step();Check(runtime.InspectAction(actorId).State==CharacterAction.Idle,"reload starts idle");
        });
        var end=new List<(Vector3 Position,HealthData Health)>();
        string? eventOracle=null;var collected=new List<CombatEvent>();
        foreach(int hz in new[]{30,60,144,0})Session(Fixture(),(play,runtime)=>{
            collected.Clear();
            CharacterHostChecks.Input(play,1,key:-1);CharacterHostChecks.Input(play,2,key:74,pressed:true);
            for(int i=0;i<(hz==0?60:hz);i++){if(hz==0)Step(play);else Check(play.AdvanceFrame(1d/hz).State==PlayState.Running,"catch-up action step");}
            Check(play.Tick==60&&play.Scene.World.Get<HealthData>(play.Scene.World.FindEntity(targetId)).Current==75&&runtime.InspectAction(actorId).State==CharacterAction.Idle,$"{hz}Hz committed action parity: tick={play.Tick}, health={play.Scene.World.Get<HealthData>(play.Scene.World.FindEntity(targetId)).Current}, state={runtime.InspectAction(actorId).State}");
            end.Add((play.Scene.World.Get<TransformData>(play.Scene.World.FindEntity(actorId)).Position,play.Scene.World.Get<HealthData>(play.Scene.World.FindEntity(targetId))));
            string trace=string.Join(";",collected.Select(e=>$"{e.Tick}:{e.Kind}:{e.Instance}:{e.NotifyId}:{e.Target}:{e.Damage}"));
            eventOracle??=trace;Check(trace==eventOracle&&collected.Count(e=>e.Kind==CombatEventKind.Hit)==1&&collected.Count(e=>e.Kind==CombatEventKind.HitOpen)==1&&collected.Count(e=>e.Kind==CombatEventKind.HitClose)==1,"per-commit event oracle, not last-render-frame polling");
        },(play,runtime)=>play.AddCommittedObserver(new Observer(()=>collected.AddRange(runtime.ReadCombatEvents()))),frames:hz!=0);
        Check(end.All(e=>Vector3.Distance(e.Position,end[0].Position)<1e-5f&&e.Health==end[0].Health),"motion and health independent of rendering frequency");
        passed.Add("K5 Pause/Step/reload and 30/60/144/headless transient/catch-up health/root parity");

        physics.Dispose();
        string assembly=Assembly.GetExecutingAssembly().Location;File.Copy(assembly,Path.Combine(root,"gameplay.dll"));File.Copy(Path.ChangeExtension(assembly,".deps.json"),Path.Combine(root,"gameplay.deps.json"));SceneDocumentFiles.Save(startup,Path.Combine(root,"start.ncmascene"));
        var config=new ProjectConfiguration(1,project,"Action Player","start.ncmascene","gameplay.dll","Direct3D11",[],true,startup.GetObjectIds().Single(o=>startup.HasComponent<CameraData>(o)));
        foreach(bool packed in new[]{false,true})foreach(bool headless in new[]{false,true}) {

            using var playerRunOwner0 = new Ncma.Player.App.PlayerRunner();
            string file=Path.Combine(root,"action.ncmaproject");File.WriteAllBytes(file,JsonSerializer.SerializeToUtf8Bytes(config with{AssetPackage=packed?"assets/game.ncpak":null},new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
            var flags=new List<string>{"--project",file,"--ticks","20","--report",Path.Combine(root,$"{packed}-{headless}.json")};if(headless)flags.Add("--headless");
            var report=playerRunOwner0.Run(PlayerOptions.Parse(flags.ToArray()),pluginRoot:plugins,visible:false,trustedInput:headless?(p,_)=>{if(p.Tick==0){CharacterHostChecks.Input(p,1,key:-1);CharacterHostChecks.Input(p,2,key:74,pressed:true);}}:null);
            Check(report.ExitCode==0&&report.Tick>=20&&report.ShutdownErrors.Length==0&&report.ValidationErrors==0&&report.ValidationWarnings==0,"formal action Player: "+report.Reason);
            Check(!headless||report.Modules.All(m=>m.Id is not ("ncma.renderer" or "ncma.platform")),"Headless action path no graphics");
        }
        passed.Add("K5 formal DX11/Headless and packed/unpacked Player four-clip actions, GPU validation 0/0");
        return passed.ToArray();
    }
}
