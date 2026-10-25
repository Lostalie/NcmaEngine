using Ncma.Runtime;

internal static class ScheduleExecutionTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Schedule execution assertion"); }
    private static void Reject(Action action)
    { try { action(); } catch (Exception e) when (e is InvalidOperationException or ArgumentException or TimeoutException or OperationCanceledException) { return; } throw new Exception("Rejected schedule accepted"); }
    private static ScheduleTask Node(string id, ScheduleAffinity affinity, params ScheduleAccess[] access) => new(id, 0, affinity, access, [], []);
    private static World Scene()
    { var world = new World(); world.Set(world.CreateEntity("row", Guid.Parse("11000000-0000-0000-0000-000000000001")), TransformData.Identity); return world; }
    private static ScheduleExecutor Executor(World world, ScheduleTask[] tasks, ScheduleKernel[] kernels, int workers = 0,
        ScheduleResource[]? resources = null, Dictionary<string, byte[]>? input = null, TimeSpan? timeout = null)
    {
        var channels = tasks.Any(t => t.Access.Any(a => a.Kind == ScheduleAccessKind.Component)) ? new[] { ScheduleComponent.Of<TransformData>() } : [];
        return new(world, ScheduleGraph.Compile(world.Components, tasks, resources), kernels, channels, input, workers, timeout);
    }
    private static ScheduleAccess Write(World w) => ScheduleAccess.Write<TransformData>(w.Components);
    private static ScheduleAccess Read(World w) => ScheduleAccess.Read<TransformData>(w.Components);
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("E6-C explicit resource ReadOutput carries component-produced values across phases", () => {
            foreach(int workers in new[]{0,2}) {
                var world=Scene();var runner=new WorldRunner(world);var entity=world.GetEntities()[0];int observed=0;
                var output=ScheduleAccess.Resource("next.value",ScheduleAccessMode.Write);
                using var execution=Executor(world,
                    [Node("produce",ScheduleAffinity.Worker,Write(world),output),Node("consume",ScheduleAffinity.Worker,output with{Mode=ScheduleAccessMode.Read})with{Phase=7}],
                    [new("produce",c=>{var value=c.Read<TransformData>(0);c.Write(0,value with{Position=System.Numerics.Vector3.UnitX*7});c.WriteResource("next.value",new byte[]{7});}),
                     new("consume",c=>{Check(c.ReadResource("next.value")[0]==1);observed=c.ReadOutput("produce","next.value")[0];})],workers,[new("next.value",true)],new(){{"next.value",[1]}});
                runner.ConfigureSchedule(execution);
                try{runner.Advance(1d/60);Check(observed==7 && world.Get<TransformData>(entity).Position.X==7 && world.Tick==1 && execution.Inspect().State=="committed");}
                finally{runner.ClearSchedule();runner.WaitForScheduleResources(TimeSpan.FromSeconds(4));}
            }
        });
        yield return ("E6-C created-object typed outputs reject before any staging regardless of task order", () => {
            foreach (int workers in new[] { 0, 2 }) foreach (bool writerFirst in new[] { false, true }) {
                var world=Scene();var runner=new WorldRunner(world);Guid id=Guid.NewGuid();Guid existing=world.GetUuid(world.GetEntities()[0]);
                string type=world.Components.Describe(typeof(TransformData)).TypeId;
                string writer=writerFirst?"b.writer":"z.writer", creator=writerFirst?"z.creator":"b.creator";
                var sentinel=new PreflightWrites(existing,type);var invalid=new PreflightWrites(id,type);
                // White-box malformed output injection: public Write(index) cannot address
                // a fresh UUID absent from the captured column. Do not widen that API.
                using var execution=Executor(world,
                    [Node("a.sentinel",ScheduleAffinity.Worker,Write(world)),Node(writer,ScheduleAffinity.Worker,Write(world)),Node(creator,ScheduleAffinity.OwnerThread,ScheduleAccess.StructuralWrite())],
                    [new("a.sentinel",c=>c.Writes[typeof(TransformData)]=sentinel),new(writer,c=>c.Writes[typeof(TransformData)]=invalid),new(creator,c=>c.Create(id,"new"))],workers);
                byte[] before=SceneJson.EncodeBounded(world.CaptureSnapshot(),4*1024*1024);ulong revision=world.Revision;
                runner.ConfigureSchedule(execution);
                try {
                    InvalidOperationException? failure=null;
                    try{runner.Advance(1d/60);}catch(InvalidOperationException e){failure=e;}
                    Check(failure is not null && failure.Message.Contains("scheduled_write_to_created_object") && failure.Message.Contains("Add<T>") && failure.Message.Contains(id.ToString("D")) && failure.Message.Contains(writer));
                    Check(sentinel.StageCalls==0 && invalid.StageCalls==0 && sentinel.Prepared && invalid.Prepared);
                    Check(world.Tick==0 && world.Revision==revision && before.SequenceEqual(SceneJson.EncodeBounded(world.CaptureSnapshot(),4*1024*1024)));
                    Check(execution.Inspect().Failure!.Contains("scheduled_write_to_created_object") && execution.Inspect().Failure!.Contains("Add<T>"));
                }finally{runner.ClearSchedule();runner.WaitForScheduleResources(TimeSpan.FromSeconds(4));}
            }
        });
        yield return ("E6-C public Create plus existing-row Write has explicit preflight diagnostic", () => {
            var world=Scene();var runner=new WorldRunner(world);Guid id=world.GetUuid(world.GetEntities()[0]);
            using var execution=Executor(world,[Node("create",ScheduleAffinity.OwnerThread,ScheduleAccess.StructuralWrite()),Node("write",ScheduleAffinity.OwnerThread,Write(world))],
                [new("create",c=>c.Create(id,"duplicate")),new("write",c=>c.Write(c.Find<TransformData>(id),TransformData.Identity))]);
            runner.ConfigureSchedule(execution);
            try {
                InvalidOperationException? failure=null;try{runner.Advance(1d/60);}catch(InvalidOperationException e){failure=e;}
                Check(failure?.Message.Contains("scheduled_write_to_created_object")==true && failure.Message.Contains("Add<T>") && world.Tick==0 && world.Count==1);
            }finally{runner.ClearSchedule();}
        });
        yield return ("E6-C separate Create and Add tasks still initialize new objects through structural operations", () => {
            var world=Scene();var runner=new WorldRunner(world);Guid id=Guid.NewGuid();
            using var execution=Executor(world,[Node("create",ScheduleAffinity.OwnerThread,ScheduleAccess.StructuralWrite()),Node("add",ScheduleAffinity.OwnerThread,ScheduleAccess.StructuralWrite(),Write(world))with{After=["create"]}],
                [new("create",c=>c.Create(id,"new")),new("add",c=>{Check(c.Find<TransformData>(id)==-1);c.Add(id,TransformData.Identity with{Position=System.Numerics.Vector3.One});})]);
            runner.ConfigureSchedule(execution);
            try{runner.Advance(1d/60);Check(world.Tick==1 && world.Count==2 && world.Get<TransformData>(world.FindEntity(id)).Position==System.Numerics.Vector3.One && execution.Inspect().State=="committed");}
            finally{runner.ClearSchedule();}
        });
        yield return ("E6-C phase anchors join all producers, preserve within-phase parallelism and output ancestry", () => {
            foreach(int workers in new[]{0,2}) {
                var world=Scene();var runner=new WorldRunner(world);using var barrier=new Barrier(2);int done=0;
                var wa=ScheduleAccess.Resource("a",ScheduleAccessMode.Write);var wb=ScheduleAccess.Resource("b",ScheduleAccessMode.Write);
                var wc=ScheduleAccess.Resource("c",ScheduleAccessMode.Write);
                void Producer(ScheduleContext c,string id,byte value){if(workers>0 && !barrier.SignalAndWait(TimeSpan.FromSeconds(2)))throw new TimeoutException("Same-phase parallelism lost");c.WriteResource(id,new[]{value});Interlocked.Increment(ref done);}
                using var execution=Executor(world,
                    [Node("z.a",ScheduleAffinity.Worker,wa),Node("z.b",ScheduleAffinity.Worker,wb),
                     Node("a.read",ScheduleAffinity.OwnerThread,wa with{Mode=ScheduleAccessMode.Read},wb with{Mode=ScheduleAccessMode.Read},wc) with{Phase=7},
                     Node("b.last",ScheduleAffinity.Worker,wc with{Mode=ScheduleAccessMode.Read}) with{Phase=15}],
                    [new("z.a",c=>Producer(c,"a",7)),new("z.b",c=>Producer(c,"b",8)),
                     new("a.read",c=>{Check(Volatile.Read(ref done)==2 && c.ReadOutput("z.a","a")[0]==7 && c.ReadOutput("z.b","b")[0]==8 && c.ReadResource("a")[0]==1);c.WriteResource("c",new byte[]{9});}),
                     new("b.last",c=>Check(c.ReadOutput("a.read","c")[0]==9))],workers,
                    [new("a",true),new("b",true),new("c",true)],new(){{"a",[1]},{"b",[1]},{"c",[1]}},TimeSpan.FromSeconds(5));
                runner.ConfigureSchedule(execution);
                try{runner.Advance(1d/60);Check(world.Tick==1 && execution.Inspect().Tasks.Length==4 && execution.ReadResource("c")[0]==9 && execution.Graph.CopySerialOrder().Length==4);}
                finally{runner.ClearSchedule();runner.WaitForScheduleResources(TimeSpan.FromSeconds(4));}
            }
        });
        yield return ("E6-C failed earlier phase never releases its anchor or executes later kernels", () => {
            var world=Scene();var runner=new WorldRunner(world);int later=0;
            using var execution=Executor(world,[Node("z.fail",ScheduleAffinity.Worker),Node("a.later",ScheduleAffinity.OwnerThread)with{Phase=15}],
                [new("z.fail",_=>throw new InvalidOperationException("phase failure")),new("a.later",_=>later++)],1);
            runner.ConfigureSchedule(execution);
            try{Reject(()=>runner.Advance(1d/60));Check(world.Tick==0 && later==0 && runner.IsFaulted);}
            finally{runner.ClearSchedule();runner.WaitForScheduleResources(TimeSpan.FromSeconds(4));}
        });
        yield return ("E6-C serial/worker fixed steps use immutable input and one publication", () => {
            foreach (int workers in new[] { 0, 2 }) {
                var world = Scene(); var runner = new WorldRunner(world); var e = world.GetEntities()[0];
                var observed = new List<float>();
                using var execution = Executor(world,
                    [Node("a", ScheduleAffinity.Worker, Write(world)), Node("b", ScheduleAffinity.OwnerThread, Read(world))],
                    [new("a", c => { var t = c.Read<TransformData>(0); t = t with { Position = t.Position + System.Numerics.Vector3.UnitX }; c.Write(0,t); }), new("b", c => observed.Add(c.Read<TransformData>(0).Position.X))], workers);
                runner.ConfigureSchedule(execution);
                try { runner.Advance(1d/60); runner.Advance(1d/60); Check(world.Tick == 2 && world.Get<TransformData>(e).Position.X == 2 && observed.SequenceEqual(new float[] {0,1}) && execution.Inspect().State == "committed"); }
                finally { runner.ClearSchedule(); }
            }
        });
        yield return ("E6-C caught undeclared write poisons step and leaves value/tick unchanged", () => {
            var world=Scene(); var runner=new WorldRunner(world); var e=world.GetEntities()[0];
            using var execution=Executor(world,[Node("a",ScheduleAffinity.OwnerThread,Read(world))],[new("a",c=>Reject(()=>c.Write(0,TransformData.Identity)))]);
            runner.ConfigureSchedule(execution);
            try { Reject(()=>runner.Advance(1d/60)); Check(world.Tick==0 && world.Get<TransformData>(e)==TransformData.Identity && runner.IsFaulted); }
            finally { runner.ClearSchedule(); }
        });
        yield return ("E6-C captured World and query cannot bypass declaration even when caught", () => {
            foreach (bool queryAccess in new[]{false,true}) {
                var world=Scene(); var runner=new WorldRunner(world); var query=world.Query<TransformData>();
                using var execution=Executor(world,[Node("a",ScheduleAffinity.OwnerThread,Read(world))],[new("a",c=>Reject(()=> { if(queryAccess) _=query.Count; else _=world.Count; }))]);
                runner.ConfigureSchedule(execution);
                try { Reject(()=>runner.Advance(1d/60)); Check(world.Tick==0); } finally {runner.ClearSchedule();}
            }
        });
        yield return ("E6-C context expires; foreign-thread access poisons original task", () => {
            var world=Scene(); var runner=new WorldRunner(world); ScheduleContext? held=null;
            using var execution=Executor(world,[Node("a",ScheduleAffinity.OwnerThread,Read(world))],[new("a",c=> {held=c; Task.Run(()=>Reject(()=>c.Count<TransformData>())).GetAwaiter().GetResult();})]);
            runner.ConfigureSchedule(execution);
            try { Reject(()=>runner.Advance(1d/60)); Check(world.Tick==0); Reject(()=>held!.Count<TransformData>()); } finally {runner.ClearSchedule();}
        });
        yield return ("E6-C resource outputs require predecessor and commit only with original step", () => {
            var world=Scene(); var runner=new WorldRunner(world);
            var write=ScheduleAccess.Resource("data",ScheduleAccessMode.Write); var read=ScheduleAccess.Resource("data",ScheduleAccessMode.Read);
            using var execution=Executor(world,[Node("a",ScheduleAffinity.Worker,write),Node("b",ScheduleAffinity.OwnerThread,read)],[new("a",c=>c.WriteResource("data",new byte[]{7})),new("b",c=> {Check(c.ReadResource("data")[0]==1); var output=c.ReadOutput("a","data"); Check(output[0]==7); output[0]=9;})],2,[new("data",true)],new(){{"data",[1]}});
            runner.ConfigureSchedule(execution);
            try {runner.Advance(1d/60); var copy=execution.ReadResource("data"); Check(copy[0]==7); copy[0]=99; Check(execution.ReadResource("data")[0]==7);}
            finally {runner.ClearSchedule();}
        });
        yield return ("E6-C duplicate output and legacy producer conflict roll back", () => {
            foreach(bool legacy in new[]{false,true}) {
                var world=Scene();var runner=new WorldRunner(world);var e=world.GetEntities()[0];
                var tasks=legacy ? new[]{Node("a",ScheduleAffinity.OwnerThread,Write(world))} : new[]{Node("a",ScheduleAffinity.OwnerThread,Write(world)),Node("b",ScheduleAffinity.OwnerThread,Write(world))};
                using var execution=Executor(world,tasks,tasks.Select(t=>new ScheduleKernel(t.Id,c=>c.Write(0,TransformData.Identity))).ToArray());
                if(legacy) runner.AddSystem(new CallbackSystem(w=>w.Set(e,TransformData.Identity)));
                runner.ConfigureSchedule(execution);
                try {Reject(()=>runner.Advance(1d/60));Check(world.Tick==0);}finally{runner.ClearSchedule();}
            }
        });
        yield return ("E6-C owner structural output uses original staged create/add; duplicate destroy aborts", () => {
            foreach(bool conflict in new[]{false,true}) {
                var world=Scene();var runner=new WorldRunner(world);Guid created=Guid.NewGuid();Guid existing=world.GetUuid(world.GetEntities()[0]);
                using var execution=Executor(world,[Node("a",ScheduleAffinity.OwnerThread,Write(world),ScheduleAccess.StructuralWrite())],[new("a",c=> {if(conflict){c.Rename(existing,"changed");c.Destroy(existing);}else{c.Create(created,"created");c.Add(created,TransformData.Identity);}})]);
                runner.ConfigureSchedule(execution);
                try {if(conflict){Reject(()=>runner.Advance(1d/60));Check(world.Tick==0&&world.Count==1);}else{runner.Advance(1d/60);Check(world.Tick==1&&world.Get<TransformData>(world.FindEntity(created))==TransformData.Identity);}}finally{runner.ClearSchedule();}
            }
        });
        yield return ("E6-C worker parallel overlap and owner affinity", () => {
            var world=Scene();var runner=new WorldRunner(world);int owner=Environment.CurrentManagedThreadId;using var barrier=new Barrier(2);int workers=0;
            void Worker(ScheduleContext c){Check(Environment.CurrentManagedThreadId!=owner);if(!barrier.SignalAndWait(TimeSpan.FromSeconds(2)))throw new TimeoutException();Interlocked.Increment(ref workers);c.CheckCancellation();}
            using var execution=Executor(world,[Node("a",ScheduleAffinity.Worker),Node("b",ScheduleAffinity.Worker),Node("c",ScheduleAffinity.OwnerThread)],[new("a",Worker),new("b",Worker),new("c",_=>Check(Environment.CurrentManagedThreadId==owner))],2,timeout:TimeSpan.FromSeconds(5));
            runner.ConfigureSchedule(execution);try{runner.Advance(1d/60);Check(workers==2&&world.Tick==1);}finally{runner.ClearSchedule();}
        });
        yield return ("E6-C timed-out workers retain ownership until explicit drain; no reuse", () => {
            var world=Scene();var runner=new WorldRunner(world);using var gate=new ManualResetEventSlim();using var exited=new ManualResetEventSlim();
            using var execution=Executor(world,[Node("a",ScheduleAffinity.Worker)],[new("a",c=>{try{gate.Wait(TimeSpan.FromSeconds(3));c.CheckCancellation();}finally{exited.Set();}})],1,timeout:TimeSpan.FromMilliseconds(40));
            runner.ConfigureSchedule(execution);
            try {
                Reject(()=>runner.Advance(1d/60));Check(world.Tick==0);Reject(()=>runner.Advance(1d/60));
                var timeoutReport=execution.Inspect();Check(timeoutReport.Tasks.Length==1 && timeoutReport.Tasks[0].State=="unfinished" && timeoutReport.Tasks[0].Bytes is null && timeoutReport.Tasks[0].AllocatedBytes is null);
                runner.ClearSchedule();runner.ResetFault();Check(!runner.IsFaulted && runner.ScheduleGraph is null);
                runner.Advance(1d/60);Check(world.Tick==1);Reject(()=>runner.ConfigureSchedule(execution));
            }
            finally {gate.Set();Check(exited.Wait(TimeSpan.FromSeconds(4)));runner.ClearSchedule();runner.WaitForScheduleResources(TimeSpan.FromSeconds(4));}
        });
        yield return ("E6-C failed task discards resources and diagnostic copies cannot mutate state", () => {
            var world=Scene();var runner=new WorldRunner(world);var access=ScheduleAccess.Resource("data",ScheduleAccessMode.Write);
            using var execution=Executor(world,[Node("a",ScheduleAffinity.OwnerThread,access),Node("z",ScheduleAffinity.OwnerThread)with{After=["a"]}],[new("a",c=>c.WriteResource("data",new byte[]{8})),new("z",_=>throw new InvalidOperationException("failure"))],resources:[new("data")],input:new(){{"data",[1]}});
            runner.ConfigureSchedule(execution);
            try{Reject(()=>runner.Advance(1d/60));Check(execution.ReadResource("data")[0]==1&&world.Tick==0);var report=execution.Inspect();report.Tasks[0]=new("fake",0,"fake",null,null);Check(execution.Inspect().Tasks[0].TaskId=="a");}finally{runner.ClearSchedule();}
        });
        yield return ("E6-C single attachment and replaced World reject; stale executor still detachable", () => {
            var world=Scene();var runner=new WorldRunner(world);var other=new WorldRunner(world);
            using var execution=Executor(world,[],[]);runner.ConfigureSchedule(execution);
            Reject(()=>other.ConfigureSchedule(execution));var snapshot=world.CaptureSnapshot();world.RestoreSnapshot(snapshot);
            Reject(()=>runner.Advance(1d/60));runner.ClearSchedule();
        });
        yield return ("E6-C resource input copied and strict output budget caught failure remains failure", () => {
            var world=Scene();var runner=new WorldRunner(world);byte[] bytes=[3];
            using var execution=Executor(world,[Node("a",ScheduleAffinity.OwnerThread,ScheduleAccess.Resource("r",ScheduleAccessMode.Write))],[new("a",c=>Reject(()=>c.WriteResource("r",new byte[ScheduleExecutor.MaxResourceBytes+1])))],resources:[new("r")],input:new(){{"r",bytes}});
            bytes[0]=9;runner.ConfigureSchedule(execution);
            try{Reject(()=>runner.Advance(1d/60));Check(execution.ReadResource("r")[0]==3&&world.Tick==0);}finally{runner.ClearSchedule();}
        });
        yield return ("E6-C default serial follows exact compiled order and empty graph commits", () => {
            var world=Scene();var runner=new WorldRunner(world);var order=new List<string>();
            using var execution=Executor(world,[Node("a",ScheduleAffinity.Worker),Node("b",ScheduleAffinity.Worker)with{After=["a"]},Node("c",ScheduleAffinity.OwnerThread)],[new("a",_=>order.Add("a")),new("b",_=>order.Add("b")),new("c",_=>order.Add("c"))]);
            runner.ConfigureSchedule(execution);try{runner.Advance(1d/60);Check(order.SequenceEqual(execution.Graph.CopySerialOrder()));}finally{runner.ClearSchedule();}
            using var empty=Executor(world,[],[]);runner.ConfigureSchedule(empty);try{runner.Advance(1d/60);Check(empty.Inspect().State=="committed"&&world.Tick==2);}finally{runner.ClearSchedule();}
        });
        yield return ("E6-C declarations do not grant reserved native component authority", () => {
            var world=Scene();var runner=new WorldRunner(world);var entity=world.GetEntities()[0];
            using var authority=world.ClaimComponents<TransformData>([world.GetUuid(entity)],publishRequired:false);
            using var execution=Executor(world,[Node("a",ScheduleAffinity.OwnerThread,Write(world))],[new("a",c=>c.Write(0,TransformData.Identity))]);
            runner.ConfigureSchedule(execution);try{Reject(()=>runner.Advance(1d/60));Check(world.Tick==0&&world.Get<TransformData>(entity)==TransformData.Identity);}finally{runner.ClearSchedule();}
        });
        yield return ("E6-C combined structural budget rejects before any partial publication", () => {
            var world=Scene();var runner=new WorldRunner(world);
            using var execution=Executor(world,[Node("a",ScheduleAffinity.OwnerThread,ScheduleAccess.StructuralWrite()),Node("b",ScheduleAffinity.OwnerThread,ScheduleAccess.StructuralWrite())],
                [new("a",c=>{for(int i=0;i<65;i++)c.Create(Guid.NewGuid(),"a");}),new("b",c=>{for(int i=0;i<65;i++)c.Create(Guid.NewGuid(),"b");})]);
            runner.ConfigureSchedule(execution);try{Reject(()=>runner.Advance(1d/60));Check(world.Tick==0&&world.Count==1);}finally{runner.ClearSchedule();}
        });
        yield return ("E6-C 4096 row diagnostic matrix serial/worker without complete snapshots", () => {
            foreach(int workers in new[]{0,2}) {
                var world=new World();for(int i=0;i<4096;i++)world.Set(world.CreateEntity("row"),TransformData.Identity);
                var runner=new WorldRunner(world);var counter=typeof(World).GetField("_capturedSnapshotCount",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
                long captures=(long)counter.GetValue(world)!;
                using var execution=Executor(world,[Node("a",ScheduleAffinity.Worker,Write(world))],[new("a",c=>{for(int i=0;i<c.Count<TransformData>();i++){var t=c.Read<TransformData>(i);c.Write(i,t with{Position=t.Position+System.Numerics.Vector3.UnitX});}})],workers,timeout:TimeSpan.FromSeconds(5));
                runner.ConfigureSchedule(execution);
                try {
                    long allocations=GC.GetTotalAllocatedBytes(true);var watch=System.Diagnostics.Stopwatch.StartNew();
                    for(int i=0;i<8;i++)runner.Advance(1d/60);
                    watch.Stop();long allocated=GC.GetTotalAllocatedBytes(true)-allocations;
                    Check(world.Tick==8 && (long)counter.GetValue(world)! == captures && world.GetEntities().All(e=>world.Get<TransformData>(e).Position.X==8));
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{contract="ncma.schedule-diagnostic.v1",workers,objects=4096,steps=8,watch.ElapsedTicks,allocated,historicalPerformanceAcceptance=false}));
                }finally{runner.ClearSchedule();}
            }
        });
    }
    private sealed class CallbackSystem(Action<World> callback):IWorldSystem { public void FixedUpdate(World world,double delta)=>callback(world); }
    private sealed class PreflightWrites(Guid id,string type):IScheduleWrites
    {
        public int Count=>1;
        internal int StageCalls;
        internal bool Prepared;
        public void Reset(){Prepared=false;StageCalls=0;}
        public void Prepare()=>Prepared=true;
        public (Guid Object,string Type) TargetAt(int index){Check(index==0 && Prepared);return(id,type);}
        public void Stage(World world)=>StageCalls++;
    }
}
