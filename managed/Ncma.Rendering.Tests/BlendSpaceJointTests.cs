using System.Numerics;
using System.Text.Json;
using Ncma.Animation;
using Ncma.Animation.Native;
using Ncma.Characters;
using Ncma.Gameplay;
using Ncma.Physics;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    private static void TestBlendSpaceSchedulesAndFaults(SkinFixture f,AnimationGraphDefinition graph,
        Func<int,bool,SceneDocument> create,PhysicsService physics,PoseKernel kernel,string output)
    {
        var results=new List<object>();var outcomes=new List<Vector3>();
        foreach(int hz in new[]{30,60,144,0}) {
            var d=create(1,true);Guid id=d.World.GetObjects().Single(o=>o.Has<AnimatorData>()).PersistentId;
            using var assets=SceneAssetPreparation.Prepare(f.Root,f.Project,d.CaptureSnapshot(),true);
            using var play=new PlaySession(d,FrameTimePolicy.Strict,advanceMode:hz==0?PlayAdvanceMode.FixedSteps:PlayAdvanceMode.Frames);
            using var runtime=ScenePlayRuntime.Compose(play,physics,assets);play.Start(_=>throw new Exception("No behaviours"));
            try {
                for(int i=0;i<(hz==0?120:hz*2);i++)Check((hz==0?play.AdvanceFixedStep():play.AdvanceFrame(1d/hz)).State==PlayState.Running,"Space schedule: "+play.Fault?.Code);
                var position=d.World.FindObject(id).Get<TransformData>().Position;outcomes.Add(position);
                var debug=runtime.Animators!.ReadDebug(id);var rows=debug.Instructions.Where(r=>r.Operation==AnimationPoseOperation.Clip).ToArray();
                Check(play.Tick==120&&debug.Frame.Context.Tick==120&&runtime.Characters!.Status.CommittedSequence==120,"Space phase and unique solver share fixed tick");
                Check(rows.All(r=>Math.Abs(r.Current/r.Duration-2)<1e-10)&&Math.Abs(position.X-2)<1e-4,"Independent2s phase/root oracle: "+position);
                Check(kernel.Statistics.Rigs==0,"Headless schedule does not allocate native pose");
                results.Add(new{frameRate=hz,tick=play.Tick,position=new{x=position.X,y=position.Y,z=position.Z},phase=rows[0].Current/rows[0].Duration});
                play.Pause();runtime.Animators.SetFloat(id,graph.Parameters[0].Id,.8);runtime.Animators.SetFloat(id,graph.Parameters[1].Id,.1);
                Check(play.Step().State==PlayState.Paused&&Math.Abs(runtime.Characters!.InspectRootMotion(id).DesiredDisplacement.X+1f/30)<1e-5,"Safe controls select current unequal-duration primary root");
                Guid instance=runtime.Animators.ReadFrame(id).InstanceId,world=d.World.Identity;play.Reload(_=>throw new Exception("No behaviours"));
                Check(play.Tick==121&&play.State==PlayState.Paused&&d.World.Identity!=world&&runtime.Animators.ReadFrame(id).InstanceId!=instance,"Space reload resets identity, keeps chronology");
                Check(play.Step().State==PlayState.Paused&&runtime.Animators.ReadDebug(id).Instructions.Where(r=>r.Operation==AnimationPoseOperation.Clip).All(r=>r.Previous==0),"New space phases start0 after frozen-startup Reload");
            }finally{play.Stop();}
            Check(physics.Inspect().Worlds==0,"Space schedule native drain");
        }
        Check(outcomes.All(p=>Vector3.Distance(p,outcomes[0])<1e-5),"30/60/144 frame accumulation and Headless fixed-step parity");
        foreach(bool late in new[]{false,true}) {
            var d=create(1,true);Guid id=d.World.GetObjects().Single(o=>o.Has<AnimatorData>()).PersistentId;
            using var assets=SceneAssetPreparation.Prepare(f.Root,f.Project,d.CaptureSnapshot(),true);
            using var play=new PlaySession(d,FrameTimePolicy.Strict,advanceMode:PlayAdvanceMode.FixedSteps);using var runtime=ScenePlayRuntime.Compose(play,physics,assets);
            bool fail=false;play.AddSystem(new GraphFailure(_=>{
                if(!fail)return;
                if(late){var created=play.Commands.SpawnEmpty("late failed space");play.Commands.AttachBehaviour(created.ObjectId,new(Guid.NewGuid(),"Missing",true,[]));}
                else {try{runtime.Animators!.SetFloat(id,graph.Parameters[0].Id,.9);}catch(InvalidOperationException){}}
            }));
            play.Start(_=>throw new Exception("Injected unavailable behaviour"));
            try {
                Check(play.AdvanceFixedStep().State==PlayState.Running,"Space failure baseline");byte[] before=d.CaptureBytes();fail=true;
                Check(play.AdvanceFixedStep().State==PlayState.Faulted&&play.Tick==1&&before.SequenceEqual(d.CaptureBytes()),"Space fault cannot publish partial World/clock/event/cache");
                Check(runtime.Characters!.Status.NumericalExecutionStarted==late,"Pre/post numerical space fail-stop, never solver rollback");
                Reject(()=>runtime.Animators!.ReadFrame(id));Reject(()=>runtime.Characters.InspectRootMotion(id));
            }finally{play.Stop();}
            Check(physics.Inspect().Worlds==0,"Failed space explicitStop drains native resources");
        }
        Reject(()=>create(33,true).ValidateAuthoring());
        File.WriteAllText(Path.Combine(output,"m6-6-d-schedules.json"),JsonSerializer.Serialize(new{schema=1,results,parity=true,safeBoundaryPrimary=true,reload=true,caughtControlPoisons=true,postSolverFailStop=true,noSolverRollback=true,actor33Rejected=true,manualAccepted=false}));
        Console.WriteLine("PASS M6.6-D actual space 30/60/144/Headless phase-root parity, controls/reload/pre-post solver fail-stop/33 rejection");
    }
}
