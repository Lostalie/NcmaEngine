using Ncma.Runtime;

internal static class ScheduleRecoveryTests
{
    private static void Check(bool value) { if(!value)throw new Exception("Schedule recovery assertion"); }
    private static void Reject(Action action)
    { try {action();}catch(Exception e) when(e is InvalidOperationException or TimeoutException or OperationCanceledException){return;}throw new Exception("Expected rejection"); }
    private static ScheduleExecutor Make(World world,Action<ScheduleContext> callback,int workers=1)
    {
        var graph=ScheduleGraph.Compile(world.Components,[new("work",0,ScheduleAffinity.Worker,
            [ScheduleAccess.Write<TransformData>(world.Components),ScheduleAccess.Resource("r",ScheduleAccessMode.Write)],[],[])],[new("r",true)]);
        return new(world,graph,[new("work",callback)],[ScheduleComponent.Of<TransformData>()],new Dictionary<string,byte[]>{{"r",[1]}},workers,TimeSpan.FromMilliseconds(500));
    }
    internal static IEnumerable<(string Name,Action Run)> Cases()
    {
        yield return("E6-C ResetFault preserves healthy graph across system precommit and postcommit observer failures",()=>{
            foreach(int workers in new[]{0,2}) foreach(int faultKind in new[]{0,1,2}) {
                var world=new World();var entity=world.CreateEntity("row");world.Set(entity,TransformData.Identity);
                bool armed=false;int calls=0;
                void FailOnce(){if(armed){armed=false;throw new InvalidOperationException("non-schedule fault");}}
                var runner=faultKind==1
                    ?new WorldRunner(world,1d/60,8,step=>world.FixedStep(()=>{step();FailOnce();}))
                    :new WorldRunner(world);
                if(faultKind==0)runner.AddSystem(new FaultSystem(FailOnce));
                if(faultKind==2)runner.AddCommittedObserver(new FaultObserver(FailOnce));
                using var execution=Make(world,c=>{
                    var value=c.Read<TransformData>(0);c.Write(0,value with{Position=value.Position+System.Numerics.Vector3.UnitX});
                    c.WriteResource("r",new[]{(byte)++calls});
                },workers);
                runner.ConfigureSchedule(execution);var graph=runner.ScheduleGraph;
                try {
                    runner.ResetFault();Check(ReferenceEquals(graph,runner.ScheduleGraph));
                    runner.Advance(1d/60);Check(world.Tick==1 && calls==1 && execution.ReadResource("r")[0]==1);
                    armed=true;Reject(()=>runner.Advance(1d/60));
                    ulong committed=faultKind==2?2ul:1ul;int attempts=faultKind==0?1:2;
                    Check(runner.IsFaulted && !runner.CanRecoverScheduleFault && world.Tick==committed && calls==attempts);
                    Check(runner.ScheduleStatus!.State==(faultKind==1?"aborted":"committed") && execution.ReadResource("r")[0]==committed);
                    runner.ResetFault();
                    Check(!runner.IsFaulted && runner.LastError is null && runner.Accumulator==0 && ReferenceEquals(graph,runner.ScheduleGraph));
                    Check(runner.ScheduleCleanup is {RetiredSchedules:0,UnfinishedWorkers:0});
                    runner.Advance(1d/60);
                    Check(world.Tick==committed+1 && world.Get<TransformData>(entity).Position.X==committed+1 && calls==attempts+1);
                    Check(execution.ReadResource("r")[0]==calls && runner.ScheduleStatus!.State=="committed");
                }finally{runner.ClearSchedule();runner.WaitForScheduleResources(TimeSpan.FromSeconds(4));}
            }
        });
        yield return("E6-C ResetFault still retires failed schedule and never replays its callback",()=>{
            foreach(int workers in new[]{0,2}) {
                var world=new World();var runner=new WorldRunner(world);int calls=0;
                using var execution=Make(world,_=>{calls++;throw new InvalidOperationException("schedule fault");},workers);
                runner.ConfigureSchedule(execution);
                try {
                    Reject(()=>runner.Advance(1d/60));Check(runner.IsFaulted && runner.CanRecoverScheduleFault && calls==1 && world.Tick==0);
                    runner.ResetFault();Check(!runner.IsFaulted && runner.ScheduleGraph is null && !runner.CanRecoverScheduleFault);
                    runner.WaitForScheduleResources(TimeSpan.FromSeconds(4));Reject(()=>runner.ConfigureSchedule(execution));
                    runner.Advance(1d/60);Check(world.Tick==1 && calls==1);
                }finally{runner.ClearSchedule();runner.WaitForScheduleResources(TimeSpan.FromSeconds(4));}
            }
        });
        yield return("E6-C recovery isolates late worker and permits a fresh graph before reclamation",()=>{
            var world=new World();var entity=world.CreateEntity("row");world.Set(entity,TransformData.Identity);var runner=new WorldRunner(world);
            using var gate=new ManualResetEventSlim();using var entered=new ManualResetEventSlim();int rejected=0;
            using var old=Make(world,c=>{
                entered.Set();gate.Wait(TimeSpan.FromSeconds(15));
                try{c.Write(0,TransformData.Identity with{Position=System.Numerics.Vector3.One});}catch(InvalidOperationException){Interlocked.Increment(ref rejected);}catch(OperationCanceledException){Interlocked.Increment(ref rejected);}
                try{c.WriteResource("r",new byte[]{99});}catch(InvalidOperationException){Interlocked.Increment(ref rejected);}catch(OperationCanceledException){Interlocked.Increment(ref rejected);}
            });
            runner.ConfigureSchedule(old);
            try {
                Reject(()=>runner.Advance(1d/60));Check(entered.IsSet && world.Tick==0 && runner.CanRecoverScheduleFault);
                runner.ResetFault();Check(!runner.IsFaulted && runner.ScheduleGraph is null && !runner.CollectScheduleResources());
                Check(runner.ScheduleCleanup is {RetiredSchedules:1,UnfinishedWorkers:1});
                try{runner.WaitForScheduleResources(TimeSpan.Zero);throw new Exception("Missing cleanup debt");}catch(ScheduleCleanupPendingException){}
                Reject(()=>runner.ConfigureSchedule(old));Reject(()=>old.UpdateResource("r",new byte[]{2}));
                using var next=Make(world,c=>{c.Write(0,TransformData.Identity with{Position=System.Numerics.Vector3.UnitX});c.WriteResource("r",new byte[]{7});},0);
                runner.ConfigureSchedule(next);
                try{runner.Advance(1d/60);Check(world.Tick==1 && next.ReadResource("r")[0]==7);}
                finally{runner.ClearSchedule();}
                gate.Set();runner.WaitForScheduleResources(TimeSpan.FromSeconds(4));
                Check(rejected==2 && old.ReadResource("r")[0]==1 && world.Get<TransformData>(entity).Position.X==1 && world.Tick==1 && !runner.IsFaulted);
                Check(runner.ScheduleCleanup.RetiredSchedules==0);Reject(()=>runner.ConfigureSchedule(old));
            }finally{gate.Set();runner.ClearSchedule();runner.WaitForScheduleResources(TimeSpan.FromSeconds(4));}
        });
        yield return("E6-C bounded retirement debt never blocks ResetFault or graphless steps",()=>{
            var world=new World();var runner=new WorldRunner(world);using var gate=new ManualResetEventSlim();
            var executions=new List<ScheduleExecutor>();var entries=new List<ManualResetEventSlim>();
            try{
                for(int i=0;i<WorldRunner.MaxRetainedSchedules;i++){
                    var entered=new ManualResetEventSlim();entries.Add(entered);
                    var execution=Make(world,c=>{entered.Set();gate.Wait(TimeSpan.FromSeconds(15));c.CheckCancellation();});executions.Add(execution);
                    runner.ConfigureSchedule(execution);Reject(()=>runner.Advance(1d/60));Check(entered.IsSet);runner.ResetFault();
                    Check(!runner.IsFaulted && runner.ScheduleCleanup.RetiredSchedules==i+1);
                }
                using var denied=Make(world,_=>{},0);Reject(()=>runner.ConfigureSchedule(denied));
                runner.ResetFault();runner.Advance(1d/60);Check(world.Tick==1 && !runner.IsFaulted);
                gate.Set();runner.WaitForScheduleResources(TimeSpan.FromSeconds(4));Check(runner.CollectScheduleResources());
                runner.ConfigureSchedule(denied);runner.Advance(1d/60);runner.ClearSchedule();Check(world.Tick==2);
            }finally{gate.Set();runner.ClearSchedule();runner.WaitForScheduleResources(TimeSpan.FromSeconds(4));foreach(var execution in executions)execution.Dispose();foreach(var entry in entries)entry.Dispose();}
        });
        yield return("E6-C recovery stamp rejects changed committed state but still permits old-resource cleanup",()=>{
            var world=new World();var runner=new WorldRunner(world);using var execution=Make(world,_=>throw new InvalidOperationException("task"),0);
            runner.ConfigureSchedule(execution);Reject(()=>runner.Advance(1d/60));Check(runner.CanRecoverScheduleFault);
            world.RestoreSnapshot(world.CaptureSnapshot());Check(!runner.CanRecoverScheduleFault);
            runner.ClearSchedule();runner.WaitForScheduleResources(TimeSpan.Zero);runner.ResetFault();Check(!runner.IsFaulted);
        });
    }
    private sealed class FaultSystem(Action fail):IWorldSystem { public void FixedUpdate(World world,double delta)=>fail(); }
    private sealed class FaultObserver(Action fail):ICommittedStepObserver { public void StepCommitted(World world,double delta)=>fail(); }
}
