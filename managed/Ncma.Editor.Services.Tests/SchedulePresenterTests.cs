using Ncma.Runtime;

internal static partial class RuntimeChangeReviewTests
{
    private sealed class ScheduleNoopBehaviour:Ncma.Behaviour { }
    private static Dictionary<string,ScheduleTaskReport> ScheduleReportIndex(KeepUi ui)
    {
        var panel=ui.Presenter.GetType().GetField("_schedulePanel",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(ui.Presenter)!;
        return (Dictionary<string,ScheduleTaskReport>)panel.GetType().GetProperty("TaskReports")!.GetValue(panel)!;
    }
    internal static IEnumerable<(string,Action)> ScheduleCases(string repository)
    {
        yield return("E6-C local schedule panel has explicit refresh and honest unregistered status",()=>{
            using var f=new Fixture(repository);var ui=new KeepUi(f.Owner);byte[] before=f.Owner.Document.CaptureBytes();var state=f.Owner.Edit!.State;
            ui.Click(67,1);Check(ui.Text().Contains("未读取或数据已失效"));ui.Click(67,3);Check(ui.Text().Contains("当前 Play 未注册受检任务图"));
            Check(before.SequenceEqual(f.Owner.Document.CaptureBytes())&&state==f.Owner.Edit.State);ui.Click(67,2);Check(!ui.Text().Contains("当前 Play 未注册受检任务图"));
        });
        yield return("E6-C schedule report index is refresh-scoped and full task paging follows graph bounds",()=>{
            using var f=new Fixture(repository);f.Play.Stop();var world=f.Play.Scene.World;
            var tasks=Enumerable.Range(0,ScheduleGraph.MaxTasks).Select(i=>new ScheduleTask($"task{i:D3}",0,ScheduleAffinity.OwnerThread,[],[],[])).ToArray();
            var graph=ScheduleGraph.Compile(world.Components,tasks);
            using var execution=new ScheduleExecutor(world,graph,tasks.Select(t=>new ScheduleKernel(t.Id,_=>{})),[],timeout:TimeSpan.FromSeconds(5));
            f.Play.ConfigureSchedule(execution);f.Play.Start(_=>new ScheduleNoopBehaviour());f.Play.Pause();
            var ui=new KeepUi(f.Owner);ui.Click(67,1);ui.Click(67,3);var idle=ScheduleReportIndex(ui);
            Check(idle.Count==0 && ui.Text().Contains("Bytes=N/A | AllocatedBytes=N/A"));
            f.Play.Step();Check(ui.Text().Contains("未读取或数据已失效"));ui.Click(67,3);var index=ScheduleReportIndex(ui);
            Check(!ReferenceEquals(idle,index) && index.Count==ScheduleGraph.MaxTasks && !index.Comparer.Equals("task000","TASK000"));
            foreach(var row in f.Play.ScheduleStatus!.Tasks)Check(index[row.TaskId]==row);
            byte[] before=f.Owner.Document.CaptureBytes();var state=f.Owner.Edit!.State;
            for(int page=0;page<ScheduleGraph.MaxTasks/8;page++) {
                ui.Build();Check(ReferenceEquals(index,ScheduleReportIndex(ui)));
                var rows=ui.Presenter.Items.ToArray().Where(i=>i.WidgetHigh==67 && i.WidgetLow>=100 && i.WidgetLow<100+(ulong)ScheduleGraph.MaxTasks).ToArray();
                Check(rows.Length==8 && rows.Min(i=>i.WidgetLow)==100+(ulong)(page*8));
                if(page+1<ScheduleGraph.MaxTasks/8){Check(ui.Item(67,5).Enabled==1);ui.Click(67,5);}
            }
            Check(ui.Item(67,5).Enabled==0 && ui.Item(67,4).Enabled==1);
            for(int page=1;page<ScheduleGraph.MaxTasks/8;page++)ui.Click(67,4);
            Check(ui.Item(67,4).Enabled==0 && ReferenceEquals(index,ScheduleReportIndex(ui)));
            ui.Click(67,3);Check(!ReferenceEquals(index,ScheduleReportIndex(ui)) && ScheduleReportIndex(ui).Count==ScheduleGraph.MaxTasks);
            Check(before.SequenceEqual(f.Owner.Document.CaptureBytes()) && state==f.Owner.Edit.State);
            ui.Click(67,2);ui.Click(67,1);Check(ui.Text().Contains("未读取或数据已失效"));f.Play.Stop();
        });
        yield return("E6-C actual presenter shows declared dependencies and hides stale tick graph",()=>{
            using var f=new Fixture(repository);f.Play.Stop();var world=f.Play.Scene.World;var write=ScheduleAccess.Resource("r",ScheduleAccessMode.Write);var read=ScheduleAccess.Resource("r",ScheduleAccessMode.Read);
            var graph=ScheduleGraph.Compile(world.Components,[new("produce",0,ScheduleAffinity.OwnerThread,[write],[],[]),new("consume",0,ScheduleAffinity.OwnerThread,[read],[],["produce"])],[new("r")]);
            using var execution=new ScheduleExecutor(world,graph,[new("produce",c=>c.WriteResource("r",new byte[]{7})),new("consume",c=>Check(c.ReadOutput("produce","r")[0]==7))],[],new Dictionary<string,byte[]>{{"r",[1]}});
            f.Play.ConfigureSchedule(execution);f.Play.Start(_=>new ScheduleNoopBehaviour());f.Play.Pause();var ui=new KeepUi(f.Owner);ui.Click(67,1);ui.Click(67,3);
            Check(ui.Text().Contains("Bytes=N/A | AllocatedBytes=N/A"));
            f.Play.Step();ui.Click(67,3);
            Check(ui.Text().Contains(graph.ContentHash)&&ui.Text().Contains("produce → consume")&&ui.Text().Contains("committed"));
            var metrics=f.Play.ScheduleStatus!;foreach(var row in metrics.Tasks)Check(ui.Text().Contains($"timestampTicks={row.ElapsedTimestampTicks} | Bytes={row.Bytes} | AllocatedBytes={row.AllocatedBytes}"));
            Check(metrics.Tasks.All(r=>r.Bytes==1 && r.AllocatedBytes>0) && ui.Text().Contains("回调0B不代表整个调度器0B"));
            byte[] before=f.Owner.Document.CaptureBytes();var state=f.Owner.Edit!.State;ui.Click(67,100);Check(before.SequenceEqual(f.Owner.Document.CaptureBytes())&&state==f.Owner.Edit.State);
            f.Play.Step();Check(ui.Text().Contains("未读取或数据已失效")&&!ui.Text().Contains(graph.ContentHash));ui.Click(67,3);Check(ui.Text().Contains(graph.ContentHash));f.Play.Stop();Check(!ui.Text().Contains(graph.ContentHash));
        });
    }
}
