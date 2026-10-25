using Ncma.Gameplay;
using Ncma.Runtime;
using Ncma.Scene;

internal static class ScheduleRecoveryPlayTests
{
    private static void Check(bool value){if(!value)throw new Exception("Play schedule recovery assertion");}
    private static void Reject(Action action){try{action();}catch(InvalidOperationException){return;}throw new Exception("Expected rejection");}
    internal static IEnumerable<(string Name,Action Run)> Cases()
    {
        yield return("E6-C Play recovers paused with new epoch; pending cleanup gates Stop/Reload, not stepping",()=>{
            var author=new SceneDocument("Recovery");var id=author.CreateObject("Object");author.SetComponent(id,TransformData.Identity);var before=author.CaptureBytes();
            var scene=author.BakeRuntime(author.Revision).Image.Instantiate(author.Components);
            using var gate=new ManualResetEventSlim();using var entered=new ManualResetEventSlim();
            var graph=ScheduleGraph.Compile(scene.World.Components,[new("worker",0,ScheduleAffinity.Worker,[],[],[])]);
            using var execution=new ScheduleExecutor(scene.World,graph,[new("worker",c=>{entered.Set();gate.Wait(TimeSpan.FromSeconds(15));c.CheckCancellation();})],[],workerCount:1,timeout:TimeSpan.FromMilliseconds(500));
            using var play=new PlaySession(scene,FrameTimePolicy.Strict);play.ConfigureSchedule(execution);play.Start(_=>throw new Exception("No binding"));Guid oldSession=play.SessionId;
            try{
                play.AdvanceFrame(1d/60);Check(entered.IsSet && play.State==PlayState.Faulted && play.CanRecoverScheduleFault && scene.World.Tick==0);
                play.RecoverScheduleFault();Check(play.State==PlayState.Paused && play.SessionId!=oldSession && play.Fault is null && play.ScheduleGraph is null && !play.CollectScheduleResources());
                play.Step();Check(play.State==PlayState.Paused && scene.World.Tick==1);Guid recovered=play.SessionId;
                try{play.Stop();throw new Exception("Unloaded pending worker");}catch(ScheduleCleanupPendingException){}
                Check(play.State==PlayState.Paused && play.Fault is null && play.SessionId==recovered);
                try{play.Reload(_=>throw new Exception("Factory must not run"));throw new Exception("Reloaded pending worker");}catch(ScheduleCleanupPendingException){}
                Check(play.State==PlayState.Paused && play.Fault is null && play.SessionId==recovered);
                play.Step();Check(scene.World.Tick==2 && author.CaptureBytes().SequenceEqual(before));
                gate.Set();play.Stop();Check(play.State==PlayState.Stopped && play.ScheduleCleanup.RetiredSchedules==0);
            }finally{gate.Set();play.Stop();}
        });
        yield return("E6-C coupled schedule failure remains fail-stop even when task failed before native execution",()=>{
            var author=new SceneDocument("Coupled");var scene=author.BakeRuntime(author.Revision).Image.Instantiate(author.Components);
            var graph=ScheduleGraph.Compile(scene.World.Components,[new("task",0,ScheduleAffinity.OwnerThread,[],[],[])]);
            using var execution=new ScheduleExecutor(scene.World,graph,[new("task",_=>throw new InvalidOperationException("task"))],[]);
            using var play=new PlaySession(scene,FrameTimePolicy.Strict);var participant=new Participant();play.AttachCoupledParticipant(participant);play.ConfigureSchedule(execution);play.Start(_=>throw new Exception());
            play.AdvanceFrame(1d/60);Check(play.State==PlayState.Faulted && participant.Failed && participant.Executed==0 && !play.CanRecoverScheduleFault);
            Reject(()=>play.RecoverScheduleFault());Check(play.State==PlayState.Faulted);play.Stop();
        });
    }
    private sealed class Participant:ICoupledStepParticipant
    {
        internal bool Failed;internal int Executed;
        public void Start(){}public void BeginStep(){}public void Preflight(){}public void ExecuteAndStage(){Executed++;}public void Committed(){}
        public void Faulted(){Failed=true;}public void Stop(){}public void RebuildStartup(){}
    }
}
