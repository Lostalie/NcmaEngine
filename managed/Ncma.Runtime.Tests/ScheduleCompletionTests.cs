using System.Collections.Frozen;
using Ncma.Runtime;

internal static class ScheduleCompletionTests
{
    private static void Check(bool value){if(!value)throw new Exception("Schedule completion assertion");}
    private static void Reject(Action action){try{action();}catch(Exception e)when(e is InvalidOperationException or ArgumentException){return;}throw new Exception("Expected completion rejection");}
    private static ScheduleCompletionBuffer Buffer(params string[] ids) => new(ids.Select((id,index)=>(id,index)).ToFrozenDictionary(x=>x.id,x=>x.index,StringComparer.Ordinal));
    private static ScheduleContext Context(ScheduleCompletionBuffer buffer,string id,ScheduleAccessMode mode=ScheduleAccessMode.Read,string[]? predecessors=null,int? cutoff=null) =>
        new(new(id,0,ScheduleAffinity.Worker,[ScheduleAccess.Resource("r",mode)],[],[]),new(),new(){{"r",[1]}},buffer.View,cutoff??buffer.View.Sequence,
            new(predecessors??[],StringComparer.Ordinal),1d/60,1,CancellationToken.None,new(0),new());
    internal static IEnumerable<(string Name,Action Run)> Cases()
    {
        yield return("E6-C shared completion view retains a fixed visibility cutoff and copied output semantics",()=>{
            var buffer=Buffer("producer","early","reader","unrelated");
            var producer=Context(buffer,"producer",ScheduleAccessMode.Write);
            var early=Context(buffer,"early",predecessors:["producer"]);
            producer.Invoke(c=>c.WriteResource("r",new byte[]{7}));buffer.Publish("producer",producer);
            bool hidden=false;
            early.Invoke(c=>{try{c.ReadOutput("producer","r");}catch(InvalidOperationException){hidden=true;}});Check(hidden && early.Error is InvalidOperationException);
            var reader=Context(buffer,"reader",predecessors:["producer"]);Check(ReferenceEquals(early.CompletionView,reader.CompletionView));
            reader.Invoke(c=>{var result=c.ReadOutput("producer","r");Check(result[0]==7);result[0]=99;Check(c.ReadOutput("producer","r")[0]==7 && c.ReadResource("r")[0]==1);});
            Check(reader.Error is null);Reject(()=>reader.ReadOutput("producer","r"));
            bool undeclared=false;var unrelated=Context(buffer,"unrelated");
            unrelated.Invoke(c=>{try{c.ReadOutput("producer","r");}catch(InvalidOperationException){undeclared=true;}});Check(undeclared && unrelated.Error is InvalidOperationException);
            Reject(()=>Context(buffer,"reader",cutoff:buffer.View.Sequence+1));
        });
        yield return("E6-C active worker reads stable outputs while owner appends; later result is invisible",()=>{
            var buffer=Buffer("first","later","reader");var first=Context(buffer,"first",ScheduleAccessMode.Write);
            first.Invoke(c=>c.WriteResource("r",new byte[]{7}));buffer.Publish("first",first);
            var later=Context(buffer,"later",ScheduleAccessMode.Write);later.Invoke(c=>c.WriteResource("r",new byte[]{8}));
            var reader=Context(buffer,"reader",predecessors:["first","later"]);
            using var entered=new ManualResetEventSlim();using var published=new ManualResetEventSlim();int readCount=0;bool laterRejected=false;
            var worker=Task.Run(()=>reader.Invoke(c=>{
                Check(c.ReadOutput("first","r")[0]==7);entered.Set();
                for(int i=0;i<1024;i++){Check(c.ReadOutput("first","r")[0]==7);Interlocked.Increment(ref readCount);}
                if(!published.Wait(TimeSpan.FromSeconds(4)))throw new TimeoutException("Completion publication stalled");
                try{c.ReadOutput("later","r");}catch(InvalidOperationException){laterRejected=true;}
            }));
            try{Check(entered.Wait(TimeSpan.FromSeconds(4)));buffer.Publish("later",later);published.Set();Check(worker.Wait(TimeSpan.FromSeconds(4)));Check(readCount==1024 && laterRejected && reader.Error is InvalidOperationException && buffer.View.Sequence==2);}
            finally{published.Set();if(!worker.Wait(TimeSpan.FromSeconds(4)))throw new TimeoutException("Reader did not exit");}
        });
        yield return("E6-C completion publication rejects unfinished failed duplicate foreign-attempt and foreign-thread contexts",()=>{
            var buffer=Buffer("task","failed");var context=Context(buffer,"task",ScheduleAccessMode.Write);
            Reject(()=>buffer.Publish("task",context));context.Invoke(c=>c.WriteResource("r",new byte[]{7}));
            Task.Run(()=>Reject(()=>buffer.Publish("task",context))).GetAwaiter().GetResult();Check(buffer.View.Sequence==0);
            var other=Buffer("task");Reject(()=>other.Publish("task",context));
            buffer.Publish("task",context);Reject(()=>buffer.Publish("task",context));Check(buffer.View.Sequence==1);
            var failed=Context(buffer,"failed",ScheduleAccessMode.Write);failed.Invoke(_=>throw new InvalidOperationException("failed"));Reject(()=>buffer.Publish("failed",failed));
            Check(buffer.View.Sequence==1);Reject(()=>context.Invoke(_=>throw new Exception("Must not execute again")));
        });
        yield return("E6-C 128-task chained outputs share one view per attempt across phases and refresh next tick",()=>{
            foreach(int workers in new[]{0,2}) {
                var world=new World();var runner=new WorldRunner(world);var tasks=new ScheduleTask[128];var kernels=new ScheduleKernel[128];var resources=new ScheduleResource[128];var input=new Dictionary<string,byte[]>();
                var views=new List<ScheduleCompletionBuffer.ReadView>();
                for(int i=0;i<128;i++){
                    int index=i;string id="t"+i.ToString("D3"),resource="r"+i.ToString("D3"),previous="t"+(i-1).ToString("D3"),previousResource="r"+(i-1).ToString("D3");
                    var access=new List<ScheduleAccess>{ScheduleAccess.Resource(resource,ScheduleAccessMode.Write)};
                    if(i>0)access.Add(ScheduleAccess.Resource(previousResource,ScheduleAccessMode.Read));
                    tasks[i]=new(id,i/8,ScheduleAffinity.Worker,access.ToArray(),[],i==0?[]:[previous]);resources[i]=new(resource,true);input.Add(resource,[0]);
                    kernels[i]=new(id,c=>{
                        views.Add(c.CompletionView);
                        Check(c.ReadResource(resource)[0]==(c.Tick==1?0:index+1));
                        byte value=index==0?(byte)c.Tick:(byte)(c.ReadOutput(previous,previousResource)[0]+1);
                        c.WriteResource(resource,new[]{value});
                    });
                }
                using var executor=new ScheduleExecutor(world,ScheduleGraph.Compile(world.Components,tasks,resources),kernels,[],input,workers,TimeSpan.FromSeconds(5));
                runner.ConfigureSchedule(executor);
                try{
                    runner.Advance(1d/60);Check(world.Tick==1 && views.Count==128 && views.All(v=>ReferenceEquals(v,views[0])) && views[0].Sequence==128 && executor.ReadResource("r127")[0]==128);
                    var old=views[0];views.Clear();runner.Advance(1d/60);
                    Check(world.Tick==2 && views.Count==128 && views.All(v=>ReferenceEquals(v,views[0])) && !ReferenceEquals(old,views[0]) && old.Sequence==128 && executor.ReadResource("r127")[0]==129);
                }finally{runner.ClearSchedule();runner.WaitForScheduleResources(TimeSpan.FromSeconds(4));}
            }
        });
    }
}
