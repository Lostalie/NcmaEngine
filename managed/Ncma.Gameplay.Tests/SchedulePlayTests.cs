using Ncma;
using Ncma.Gameplay;
using Ncma.Runtime;
using Ncma.Scene;

internal static class SchedulePlayTests
{
    private static void Check(bool condition) { if(!condition) throw new Exception("Schedule Play assertion"); }
    private static (SceneDocument Author,RuntimeSceneInstance Runtime) Fixture()
    { var doc=new SceneDocument("Schedule"); var id=doc.CreateObject("Object"); doc.SetComponent(id,TransformData.Identity); return(doc,doc.BakeRuntime(doc.Revision).Image.Instantiate(doc.Components)); }
    private static ScheduleExecutor Schedule(RuntimeSceneInstance scene,Action<ScheduleContext> callback,bool structure=false,int workers=0)
    {
        var access=new List<ScheduleAccess>{ScheduleAccess.Write<TransformData>(scene.World.Components),ScheduleAccess.Resource("result",ScheduleAccessMode.Write)};
        if(structure) access.Add(ScheduleAccess.StructuralWrite());
        var graph=ScheduleGraph.Compile(scene.World.Components,[new("game.compute",0,workers>0?ScheduleAffinity.Worker:ScheduleAffinity.OwnerThread,access.ToArray(),[],[])],[new("result",true)]);
        return new(scene.World,graph,[new("game.compute",callback)],[ScheduleComponent.Of<TransformData>()],new Dictionary<string,byte[]>{{"result",[1]}},workers);
    }
    private static void Move(ScheduleContext c) {var value=c.Read<TransformData>(0);c.Write(0,value with{Position=value.Position+System.Numerics.Vector3.UnitX});c.WriteResource("result",new byte[]{7});}
    internal static IEnumerable<(string Name,Action Run)> Cases()
    {
        yield return("E6-C real Play schedules runtime only; author unchanged and render sees committed value",()=>{
            var (author,scene)=Fixture();byte[] before=author.CaptureBytes();using var execution=Schedule(scene,Move,workers:2);using var play=new PlaySession(scene,FrameTimePolicy.Strict);
            play.ConfigureSchedule(execution);play.Start(_=>throw new Exception("No binding expected"));play.AdvanceFrame(1d/60);
            Check(play.State==PlayState.Running&&scene.World.Tick==1&&scene.World.Get<TransformData>(scene.World.GetEntities()[0]).Position.X==1&&author.CaptureBytes().SequenceEqual(before)&&play.ScheduleStatus!.State=="committed");play.Stop();Check(play.ScheduleStatus is null);execution.Dispose();
        });
        yield return("E6-C Play structural creation/add use original pending handles and commit receipts",()=>{
            var (_,scene)=Fixture();Guid id=Guid.NewGuid();using var execution=Schedule(scene,c=>{c.Create(id,"Scheduled");c.Add(id,TransformData.Identity);},structure:true);using var play=new PlaySession(scene,FrameTimePolicy.Strict);
            play.ConfigureSchedule(execution);play.Start(_=>throw new Exception());play.AdvanceFrame(1d/60);
            var submission=play.LastScheduleSubmission!;Check(play.State==PlayState.Running&&submission.Count==2&&scene.World.Count==2&&scene.World.Get<TransformData>(scene.World.FindEntity(id))==TransformData.Identity);
            Check(play.Commands.GetReceipt(submission[0]).Tick==1&&play.Commands.GetReceipt(submission[1]).Tick==1&&submission.Created(0).ObjectId==id);play.Stop();Check(play.LastScheduleSubmission is null);
        });
        yield return("E6-C coupled preflight failure aborts scheduled values and resource publication",()=>{
            var (_,scene)=Fixture();using var execution=Schedule(scene,Move);using var play=new PlaySession(scene,FrameTimePolicy.Strict);var coupled=new Participant{FailPreflight=true};play.AttachCoupledParticipant(coupled);play.ConfigureSchedule(execution);play.Start(_=>throw new Exception());play.AdvanceFrame(1d/60);
            Check(play.State==PlayState.Faulted&&scene.World.Tick==0&&scene.World.Get<TransformData>(scene.World.GetEntities()[0]).Position.X==0&&execution.ReadResource("result")[0]==1&&execution.Inspect().State=="aborted"&&coupled.Executed==0);play.Stop();
        });
        yield return("E6-C post-commit external failure preserves matching original publication and fail-stop",()=>{
            var (_,scene)=Fixture();using var execution=Schedule(scene,Move);using var play=new PlaySession(scene,FrameTimePolicy.Strict);var coupled=new Participant{FailCommitted=true};play.AttachCoupledParticipant(coupled);play.ConfigureSchedule(execution);play.Start(_=>throw new Exception());play.AdvanceFrame(1d/60);
            Check(play.State==PlayState.Faulted&&scene.World.Tick==1&&scene.World.Get<TransformData>(scene.World.GetEntities()[0]).Position.X==1&&execution.ReadResource("result")[0]==7&&execution.Inspect().State=="committed"&&coupled.Executed==1);play.Stop();
        });
        yield return("E6-C Reload drains and detaches old callbacks before new script lifetime",()=>{
            var (_,scene)=Fixture();int calls=0;using var execution=Schedule(scene,c=>{calls++;Move(c);},workers:1);using var play=new PlaySession(scene,FrameTimePolicy.Strict);play.ConfigureSchedule(execution);play.Start(_=>throw new Exception());play.AdvanceFrame(1d/60);play.Reload(_=>throw new Exception());
            Check(play.State==PlayState.Paused&&play.ScheduleStatus is null);execution.Dispose();play.Step();Check(calls==1&&scene.World.Tick==2);play.Stop();
        });
        yield return("E6-C stopped configured Play disposes borrowed binding without starting workers",()=>{
            var (_,scene)=Fixture();using var execution=Schedule(scene,_=>throw new Exception());var play=new PlaySession(scene);play.ConfigureSchedule(execution);play.Dispose();execution.Dispose();
        });
    }
    private sealed class Participant:ICoupledStepParticipant
    {
        internal bool FailPreflight,FailCommitted;internal int Executed;
        public void Start(){} public void BeginStep(){} public void Preflight(){if(FailPreflight)throw new InvalidOperationException("preflight");}
        public void ExecuteAndStage(){Executed++;} public void Committed(){if(FailCommitted)throw new InvalidOperationException("post-commit");}
        public void Faulted(){}public void Stop(){}public void RebuildStartup(){}
    }
}
