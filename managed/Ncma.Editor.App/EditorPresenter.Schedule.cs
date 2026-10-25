using Ncma.Runtime;
using Ncma.Gui;
using Ncma.Editor.Core;

namespace Ncma.Editor.App;

// Local, explicitly refreshed, read-only diagnostics. No remote grant or schedule mutation.
internal sealed unsafe partial class EditorPresenter
{
    private const int SchedulePageSize = 8;
    private bool _showSchedule;
    private int _scheduleTaskOffset, _scheduleEdgeOffset, _scheduleSelected;
    private SchedulePanel? _schedulePanel;
    private sealed record SchedulePanel(Guid Play,Guid Scene,Guid World,ulong Tick,ulong Revision,Ncma.Gameplay.PlayState State,ScheduleGraph? Graph,ScheduleReport? Report,ScheduleCleanupStatus Cleanup,bool CanRecover)
    {
        // Constructed once by explicit refresh, then discarded with the panel snapshot.
        public Dictionary<string,ScheduleTaskReport> TaskReports { get; } =
            Report?.Tasks.ToDictionary(r=>r.TaskId,StringComparer.Ordinal) ?? new(StringComparer.Ordinal);
    }
    private bool CurrentSchedulePanel(SchedulePanel panel) => workspace.Owner.Play is {} play &&
        play.SessionId==panel.Play && play.Scene.SessionId==panel.Scene && play.Scene.World.Identity==panel.World &&
        play.Scene.World.Tick==panel.Tick && play.Scene.World.Revision==panel.Revision && play.State==panel.State && ReferenceEquals(play.ScheduleGraph,panel.Graph);
    private bool ApplySchedule(ActionView action)
    {
        if(!action.Kind.StartsWith("schedule_",StringComparison.Ordinal)) return false;
        if(action.Operation is SchedulePanel scope && !CurrentSchedulePanel(scope)) throw new EditRejectedException("schedule_view_stale");
        switch(action.Kind) {
            case "schedule_toggle":_showSchedule=!_showSchedule;_schedulePanel=null;break;
            case "schedule_close":_showSchedule=false;_schedulePanel=null;break;
            case "schedule_refresh":
                var play=workspace.Owner.Play??throw new EditRejectedException("schedule_play_missing");
                _schedulePanel=new(play.SessionId,play.Scene.SessionId,play.Scene.World.Identity,play.Scene.World.Tick,play.Scene.World.Revision,play.State,play.ScheduleGraph,play.ScheduleStatus,play.ScheduleCleanup,play.CanRecoverScheduleFault);
                _scheduleTaskOffset=_scheduleEdgeOffset=_scheduleSelected=0;break;
            case "schedule_task_previous":_scheduleTaskOffset=Math.Max(0,_scheduleTaskOffset-SchedulePageSize);break;
            case "schedule_task_next":_scheduleTaskOffset=Math.Min(ScheduleGraph.MaxTasks-SchedulePageSize,_scheduleTaskOffset+SchedulePageSize);break;
            case "schedule_edge_previous":_scheduleEdgeOffset=Math.Max(0,_scheduleEdgeOffset-SchedulePageSize);break;
            case "schedule_edge_next":_scheduleEdgeOffset=Math.Min(ScheduleGraph.MaxEdges-SchedulePageSize,_scheduleEdgeOffset+SchedulePageSize);break;
            case "schedule_select":
                if(!int.TryParse(action.Field,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out int selected) || selected<0 || selected>=ScheduleGraph.MaxTasks)throw new EditRejectedException("schedule_selection_invalid");
                _scheduleSelected=selected;break;
            default:throw new EditRejectedException("schedule_intent_invalid");
        }
        return true;
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void BuildSchedule(float width,float height,ref ulong label)
    {
        Panel(422,"任务调度 / 本地只读诊断",width*.08f,height*.08f,width*.84f,height*.84f);
        Add(GuiItemKind.Button,67,2,"关闭",new("schedule_close"));
        Add(GuiItemKind.Button,67,3,"显式刷新已提交边界",new("schedule_refresh"),enabled:workspace.Owner.Play is not null);
        Add(GuiItemKind.Label,3,label++,"默认串行；Behaviour/原生插件保留 owner-thread 边界。诊断不授予 AI 权限，不修改任务图。");
        Add(GuiItemKind.Label,3,label++,"输入/生命周期 → Behaviour/旧 System → 受检任务图 → 原提交预检 → 物理执行 → 唯一提交 → 提交后观察");
        if(_schedulePanel is {} panel && !CurrentSchedulePanel(panel)) _schedulePanel=null;
        if(_schedulePanel is not {} current) {Add(GuiItemKind.Label,3,label++,"未读取或数据已失效；暂停 Play 后可显式刷新。");End();return;}
        Add(GuiItemKind.Label,3,label++,$"Play {current.Play:D} | Tick {current.Tick} | Revision {current.Revision}");
        Add(GuiItemKind.Label,3,label++,$"执行状态 {current.State} | 可显式恢复 {current.CanRecover} | 待回收任务图 {current.Cleanup.RetiredSchedules}/{current.Cleanup.Capacity} | 未退出 worker {current.Cleanup.UnfinishedWorkers}（刷新时快照）");
        Add(GuiItemKind.Label,3,label++,"资源待回收不等于执行故障；Stop/Reload 仍须等待退出。恢复 API 隔离旧图并暂停，不自动重试。此面板只读。");
        if(current.Graph is not {} graph) {Add(GuiItemKind.Label,3,label++,"当前 Play 未注册受检任务图；现有逻辑仍按原串行边界执行。");End();return;}
        var report=current.Report!;
        Add(GuiItemKind.Label,3,label++,$"合同 {graph.ContentHash} | {report.State} | parallel={report.ParallelEnabled}");
        if(report.Failure is {} failure) Add(GuiItemKind.Label,3,label++,failure);
        Add(GuiItemKind.Label,3,label++,"Bytes = 已接受的任务输出/资源复制配额；不是堆分配或总读写带宽。");
        Add(GuiItemKind.Label,3,label++,"AllocatedBytes = 同步回调及其检查在执行线程上的托管分配；不含捕获、Context构造、合并/报告、原生内存或子线程。");
        Add(GuiItemKind.Label,3,label++,"N/A = 未执行/未完成或测量不可用；回调0B不代表整个调度器0B（未做整帧验收）。");
        var tasks=graph.CopyTasks();var edges=graph.CopyEdges();
        foreach(var anchor in graph.CopyPhaseAnchors())
            Add(GuiItemKind.Label,3,label++,$"阶段完成锚点 {anchor.Id} | {anchor.CompletedPhase} → {anchor.NextPhase}（非执行任务）");
        Add(GuiItemKind.Button,67,4,"上一页任务",new("schedule_task_previous",Operation:current),enabled:_scheduleTaskOffset>0);
        Add(GuiItemKind.Button,67,5,"下一页任务",new("schedule_task_next",Operation:current),enabled:_scheduleTaskOffset+SchedulePageSize<tasks.Length);
        for(int i=_scheduleTaskOffset;i<Math.Min(tasks.Length,_scheduleTaskOffset+SchedulePageSize);i++) {
            var task=tasks[i];current.TaskReports.TryGetValue(task.Id,out var timing);
            string elapsed=timing?.Bytes is null?"N/A":timing.ElapsedTimestampTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string bytes=timing?.Bytes?.ToString(System.Globalization.CultureInfo.InvariantCulture)??"N/A";
            string allocated=timing?.AllocatedBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture)??"N/A";
            Add(GuiItemKind.Button,67,(ulong)(100+i),$"{task.Id} | phase={task.Phase} | {task.Affinity} | {timing?.State??"未执行"} | timestampTicks={elapsed} | Bytes={bytes} | AllocatedBytes={allocated}",new("schedule_select",Field:i.ToString(System.Globalization.CultureInfo.InvariantCulture),Operation:current));
        }
        if(tasks.Length>0) {
            var task=tasks[Math.Clamp(_scheduleSelected,0,tasks.Length-1)];
            Add(GuiItemKind.Label,3,label++,"访问声明："+task.Id);
            foreach(var access in task.Access)Add(GuiItemKind.Label,3,label++,$"{access.Kind} {access.Mode} {access.Id} v{access.Version}");
        }
        Add(GuiItemKind.Button,67,6,"上一页依赖边",new("schedule_edge_previous",Operation:current),enabled:_scheduleEdgeOffset>0);
        Add(GuiItemKind.Button,67,7,"下一页依赖边",new("schedule_edge_next",Operation:current),enabled:_scheduleEdgeOffset+SchedulePageSize<edges.Length);
        foreach(var edge in edges.Skip(_scheduleEdgeOffset).Take(SchedulePageSize))Add(GuiItemKind.Label,3,label++,$"{edge.From} → {edge.To} | {edge.Reason}");
        End();
    }
}
