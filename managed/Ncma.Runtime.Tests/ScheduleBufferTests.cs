using Ncma.Runtime;

internal static class ScheduleBufferTests
{
    private static void Check(bool value) { if(!value)throw new Exception("Schedule buffer assertion"); }
    private static void Reject(Action action) { try{action();}catch(Exception e) when(e is InvalidOperationException or TimeoutException){return;}throw new Exception("Expected buffer rejection"); }
    private static World Scene(int count)
    { var world=new World();for(int i=0;i<count;i++)world.Set(world.CreateEntity("row"),TransformData.Identity);return world; }
    private static void Refresh(IScheduleCapture capture,ScheduleColumn<TransformData>.Writes[] writers,int rounds,bool dense)
    {
        for(int i=0;i<rounds;i++) {
            int bank=i&1;capture.Capture(bank);var writer=writers[bank];writer.Reset();
            if(dense)for(int row=4095;row>=0;row--)writer.Set(row,TransformData.Identity);
            else {writer.Set(4095,TransformData.Identity);writer.Set(0,TransformData.Identity);writer.Set(0,TransformData.Identity);}
            writer.Prepare();
        }
    }
    internal static IEnumerable<(string Name,Action Run)> Cases()
    {
        yield return("E6-C indexed Find over 4096 rows preserves exact rows and warmed 0B in serial/worker callbacks",()=>{
            foreach(int workers in new[]{0,2}) {
                var world=Scene(4096);var runner=new WorldRunner(world);var ids=new Guid[4096];Guid missing=Guid.NewGuid();int calls=0;
                var graph=ScheduleGraph.Compile(world.Components,[new("find",0,ScheduleAffinity.Worker,[ScheduleAccess.Read<TransformData>(world.Components)],[],[])]);
                using var executor=new ScheduleExecutor(world,graph,[new("find",c=>{
                    for(int i=0;i<ids.Length;i++)ids[i]=c.ObjectAt<TransformData>(i);
                    for(int i=0;i<4096;i++)Check(c.Find<TransformData>(ids[i])==i);
                    long start=GC.GetAllocatedBytesForCurrentThread();int sum=0;bool exact=true;
                    for(int i=4095;i>=0;i--){int row=c.Find<TransformData>(ids[i]);sum+=row;exact&=row==i;}
                    long allocated=GC.GetAllocatedBytesForCurrentThread()-start;
                    Check(exact && sum==8386560 && allocated==0 && c.Find<TransformData>(missing)==-1 && c.Find<TransformData>(Guid.Empty)==-1);calls++;
                })],[ScheduleComponent.Of<TransformData>()],workerCount:workers,timeout:TimeSpan.FromSeconds(5));
                runner.ConfigureSchedule(executor);
                try{for(int i=0;i<3;i++)runner.Advance(1d/60);Check(calls==3 && world.Tick==3);}
                finally{runner.ClearSchedule();runner.WaitForScheduleResources(TimeSpan.FromSeconds(4));}
            }
        });
        yield return("E6-C UUID index follows captured membership while banks stay independent and bounded",()=>{
            var world=Scene(4);var entities=world.GetEntities();var ids=entities.Select(world.GetUuid).ToArray();
            var budget=new ScheduleBufferBudget();var capture=ScheduleComponent.Of<TransformData>().Bind(world,budget);capture.Capture(0);capture.Capture(1);
            var a=(ScheduleColumn<TransformData>)capture.Buffer(0);var b=(ScheduleColumn<TransformData>)capture.Buffer(1);
            int oldRow=a.Find(ids[0]);Check(oldRow>=0);long retained=budget.Bytes;
            world.Destroy(entities[0]);world.Remove<TransformData>(entities[1]);capture.Capture(1);
            Check(b.Count==2 && b.Find(ids[0])==-1 && b.Find(ids[1])==-1 && a.Find(ids[0])==oldRow && a.Count==4);
            for(int i=0;i<b.Count;i++)Check(b.Find(b.Ids[i])==i);
            var replacement=world.CreateEntity("replacement",ids[0]);world.Set(replacement,TransformData.Identity);
            world.Set(entities[1],TransformData.Identity);capture.Capture(0);
            Check(a.Count==4 && a.Entities[a.Find(ids[0])]==replacement && replacement!=entities[0] && a.Find(ids[1])>=0 && b.Find(ids[0])==-1);
            foreach(var entity in world.GetEntities())world.Destroy(entity);
            capture.Capture(0);capture.Capture(1);Check(a.Count==0 && b.Count==0 && budget.Bytes==retained);
            foreach(var id in ids)Check(a.Find(id)==-1 && b.Find(id)==-1);
            var fullWorld=Scene(1);var fullBudget=new ScheduleBufferBudget();var fullCapture=ScheduleComponent.Of<TransformData>().Bind(fullWorld,fullBudget);
            fullBudget.Reserve(ScheduleExecutor.MaxFrameBytes-fullBudget.Bytes);Reject(()=>fullCapture.Capture(0));
            var empty=(ScheduleColumn<TransformData>)fullCapture.Buffer(0);
            Check(empty.Count==0 && empty.Values.Length==0 && empty.Find(fullWorld.GetUuid(fullWorld.GetEntities()[0]))==-1);
        });
        yield return("E6-C indexed Find retains undeclared foreign-thread and expired context rejection",()=>{
            var world=Scene(1);var capture=ScheduleComponent.Of<TransformData>().Bind(world,new());capture.Capture(0);
            var column=(ScheduleColumn<TransformData>)capture.Buffer(0);Guid id=column.Ids[0];
            ScheduleContext Context(bool declared) {
                var task=new ScheduleTask("find",0,ScheduleAffinity.OwnerThread,declared?[ScheduleAccess.Read<TransformData>(world.Components)]:[],[],[]);
                var buffer=new ScheduleCompletionBuffer(System.Collections.Frozen.FrozenDictionary<string,int>.Empty);
                return new(task,new(){{typeof(TransformData),column}},new(),buffer.View,0,new(),1d/60,1,CancellationToken.None,new(0),new());
            }
            var denied=Context(false);bool rejected=false;
            denied.Invoke(c=>{try{c.Find<TransformData>(id);}catch(InvalidOperationException){rejected=true;}});
            Check(rejected && denied.Error is InvalidOperationException);
            var foreign=Context(true);bool foreignRejected=false;
            foreign.Invoke(c=>{var thread=new Thread(()=>{try{c.Find<TransformData>(id);}catch(InvalidOperationException){foreignRejected=true;}});thread.Start();thread.Join();});
            Check(foreignRejected && foreign.Error is InvalidOperationException);
            var expired=Context(true);expired.Invoke(c=>Check(c.Find<TransformData>(id)==0));Check(expired.Error is null);
            Reject(()=>expired.Find<TransformData>(id));Check(expired.Error is InvalidOperationException);
        });
        yield return("E6-C warmed 4096-row capture and sparse/dense writer buffer paths allocate exactly 0B",()=>{
            foreach(bool dense in new[]{false,true}) {
                var world=Scene(4096);var budget=new ScheduleBufferBudget();var capture=ScheduleComponent.Of<TransformData>().Bind(world,budget);
                var a=(ScheduleColumn<TransformData>)capture.Buffer(0);var b=(ScheduleColumn<TransformData>)capture.Buffer(1);
                var writers=new[]{(ScheduleColumn<TransformData>.Writes)a.CreateWrites(),(ScheduleColumn<TransformData>.Writes)b.CreateWrites()};
                Refresh(capture,writers,8,dense);var av=a.Values;var bv=b.Values;var ae=a.Entities;var be=b.Entities;var ai=a.Ids;var bi=b.Ids;long retained=budget.Bytes;
                _=GC.GetAllocatedBytesForCurrentThread();long start=GC.GetAllocatedBytesForCurrentThread();
                Refresh(capture,writers,8,dense);
                long allocated=GC.GetAllocatedBytesForCurrentThread()-start;
                Check(allocated==0 && budget.Bytes==retained && !ReferenceEquals(av,bv) && ReferenceEquals(av,a.Values) && ReferenceEquals(bv,b.Values));
                Check(ReferenceEquals(ae,a.Entities) && ReferenceEquals(be,b.Entities) && ReferenceEquals(ai,a.Ids) && ReferenceEquals(bi,b.Ids));
                Check(a.Count==4096 && b.Count==4096 && writers[0].Count==(dense?4096:2) && writers[0].Capacity==(dense?4096:4));
                Check(writers[0].TargetAt(0).Object==a.Ids[0] && writers[0].TargetAt(writers[0].Count-1).Object==a.Ids[4095]);
            }
        });
        yield return("E6-C capture tracks growth shrink and UUID generation replacement without stale rows",()=>{
            var world=Scene(1);var old=world.GetEntities()[0];Guid uuid=world.GetUuid(old);
            var capture=ScheduleComponent.Of<TransformData>().Bind(world,new());capture.Capture(0);var a=(ScheduleColumn<TransformData>)capture.Buffer(0);
            var oldEntities=a.Entities;for(int i=0;i<8;i++)world.Set(world.CreateEntity("new"),TransformData.Identity);
            capture.Capture(1);Check(capture.Buffer(1).Count==9 && a.Count==1);capture.Capture(0);Check(a.Count==9 && !ReferenceEquals(oldEntities,a.Entities));
            foreach(var entity in world.GetEntities())world.Destroy(entity);
            world.Set(world.CreateEntity("replacement",uuid),TransformData.Identity with{Position=System.Numerics.Vector3.One});
            capture.Capture(0);capture.Capture(1);Check(a.Count==1 && a.Ids[0]==uuid && a.Entities[0]!=old && a.Values[0].Position.X==1 && capture.Buffer(1).Count==1);
            world.RestoreSnapshot(world.CaptureSnapshot());Reject(()=>capture.Capture(0));
        });
        yield return("E6-C 64 sparse writers over 4096 rows no longer reserve 64 full writable columns",()=>{
            foreach(int workers in new[]{0,2}) {
                var world=Scene(4096);var entities=world.GetEntities();var runner=new WorldRunner(world);var write=ScheduleAccess.Write<TransformData>(world.Components);
                var tasks=Enumerable.Range(0,64).Select(i=>new ScheduleTask("w"+i.ToString("D2"),0,ScheduleAffinity.Worker,[write],[],[])).ToArray();
                var kernels=tasks.Select((task,row)=>new ScheduleKernel(task.Id,c=>{
                    var before=c.Read<TransformData>(row);c.Write(row,before with{Position=before.Position+System.Numerics.Vector3.One});
                    c.Write(row,before with{Position=before.Position+System.Numerics.Vector3.UnitX});
                    var output=(ScheduleColumn<TransformData>.Writes)c.Writes[typeof(TransformData)];Check(output.Count==1 && output.Capacity==4 && c.Read<TransformData>(row)==before);
                })).ToArray();
                using var executor=new ScheduleExecutor(world,ScheduleGraph.Compile(world.Components,tasks),kernels,[ScheduleComponent.Of<TransformData>()],workerCount:workers,timeout:TimeSpan.FromSeconds(5));
                runner.ConfigureSchedule(executor);
                try{for(int i=0;i<4;i++)runner.Advance(1d/60);Check(world.Tick==4);for(int i=0;i<4096;i++)Check(world.Get<TransformData>(entities[i]).Position.X==(i<64?4:0));}
                finally{runner.ClearSchedule();runner.WaitForScheduleResources(TimeSpan.FromSeconds(4));}
            }
        });
        yield return("E6-C sparse expansion is bounded and caught allocation rejection poisons its task",()=>{
            var world=Scene(4);var retained=new ScheduleBufferBudget();var capture=ScheduleComponent.Of<TransformData>().Bind(world,retained);capture.Capture(0);
            var column=(ScheduleColumn<TransformData>)capture.Buffer(0);var writer=(ScheduleColumn<TransformData>.Writes)column.CreateWrites();
            retained.Reserve(ScheduleExecutor.MaxFrameBytes-retained.Bytes);
            var task=new ScheduleTask("writer",0,ScheduleAffinity.OwnerThread,[ScheduleAccess.Write<TransformData>(world.Components)],[],[]);
            var buffer=new ScheduleCompletionBuffer(System.Collections.Frozen.FrozenDictionary<string,int>.Empty);
            var context=new ScheduleContext(task,new(){{typeof(TransformData),column}},new(),buffer.View,0,new(),1d/60,1,CancellationToken.None,new(0),new(){{typeof(TransformData),writer}});
            bool rejected=false;context.Invoke(c=>{try{c.Write(0,TransformData.Identity);}catch(InvalidOperationException){rejected=true;}});
            Check(rejected && context.Error is InvalidOperationException && writer.Count==0 && writer.Capacity==0 && retained.Bytes==ScheduleExecutor.MaxFrameBytes && world.Tick==0);
        });
        yield return("E6-C Span resource/output reads allocate 0B after warmup and reject short destinations atomically",()=>{
            foreach(bool shortBuffer in new[]{false,true}) {
                var world=new World();var runner=new WorldRunner(world);long allocated=-1;bool rejected=false;byte[] sentinel=[99];
                var resource=new ScheduleResource("r",true);var write=ScheduleAccess.Resource("r",ScheduleAccessMode.Write);var read=write with{Mode=ScheduleAccessMode.Read};
                var graph=ScheduleGraph.Compile(world.Components,[new("produce",0,ScheduleAffinity.Worker,[write],[],[]),new("consume",1,ScheduleAffinity.Worker,[read],[],[])],[resource]);
                using var executor=new ScheduleExecutor(world,graph,[new("produce",c=>c.WriteResource("r",new byte[]{7,8})),new("consume",c=>{
                    if(shortBuffer){try{c.ReadOutput("produce","r",sentinel.AsSpan());}catch(InvalidOperationException){rejected=true;}return;}
                    Span<byte> destination=stackalloc byte[2];for(int i=0;i<1024;i++){c.ReadResource("r",destination);c.ReadOutput("produce","r",destination);}
                    long start=GC.GetAllocatedBytesForCurrentThread();int sum=0;
                    for(int i=0;i<4096;i++){sum+=c.ReadResource("r",destination);sum+=c.ReadOutput("produce","r",destination);}
                    allocated=GC.GetAllocatedBytesForCurrentThread()-start;Check(sum==16384 && destination[0]==7 && destination[1]==8);destination[0]=99;Check(c.ReadOutput("produce","r",destination)==2 && destination[0]==7);
                })],[],new Dictionary<string,byte[]>{{"r",[1,2]}},2,TimeSpan.FromSeconds(5));
                runner.ConfigureSchedule(executor);
                try{if(shortBuffer){Reject(()=>runner.Advance(1d/60));Check(rejected && sentinel[0]==99 && world.Tick==0);}else{runner.Advance(1d/60);Check(allocated==0 && world.Tick==1);}}
                finally{runner.ClearSchedule();runner.WaitForScheduleResources(TimeSpan.FromSeconds(4));}
            }
        });
        yield return("E6-C timed-out worker keeps its typed buffers while a fresh executor uses independent banks",()=>{
            var world=Scene(1);var runner=new WorldRunner(world);var entity=world.GetEntities()[0];using var gate=new ManualResetEventSlim();using var entered=new ManualResetEventSlim();
            var write=ScheduleAccess.Write<TransformData>(world.Components);var graph=ScheduleGraph.Compile(world.Components,[new("task",0,ScheduleAffinity.Worker,[write],[],[])]);
            IScheduleWrites? oldWriter=null;bool lateRejected=false;
            using var old=new ScheduleExecutor(world,graph,[new("task",c=>{c.Write(0,TransformData.Identity with{Position=System.Numerics.Vector3.One});oldWriter=c.Writes[typeof(TransformData)];entered.Set();gate.Wait(TimeSpan.FromSeconds(15));try{c.Write(0,TransformData.Identity);}catch(InvalidOperationException){lateRejected=true;}})],[ScheduleComponent.Of<TransformData>()],workerCount:1,timeout:TimeSpan.FromMilliseconds(500));
            runner.ConfigureSchedule(old);
            try{
                Reject(()=>runner.Advance(1d/60));Check(entered.IsSet && oldWriter!.Count==1 && world.Tick==0);runner.ResetFault();
                using var next=new ScheduleExecutor(world,graph,[new("task",c=>{Check(!ReferenceEquals(oldWriter,c.Writes[typeof(TransformData)]));var value=c.Read<TransformData>(0);c.Write(0,value with{Position=value.Position+System.Numerics.Vector3.UnitX});})],[ScheduleComponent.Of<TransformData>()]);
                runner.ConfigureSchedule(next);try{for(int i=0;i<4;i++)runner.Advance(1d/60);Check(oldWriter!.Count==1 && world.Get<TransformData>(entity).Position.X==4);}finally{runner.ClearSchedule();}
                gate.Set();runner.WaitForScheduleResources(TimeSpan.FromSeconds(4));Check(lateRejected && world.Get<TransformData>(entity).Position.X==4);
            }finally{gate.Set();runner.ClearSchedule();runner.WaitForScheduleResources(TimeSpan.FromSeconds(4));}
        });
    }
}
