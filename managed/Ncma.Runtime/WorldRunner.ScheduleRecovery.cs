using System.Diagnostics;

namespace Ncma.Runtime;

public sealed class ScheduleCleanupPendingException(string message) : InvalidOperationException(message);
public sealed record ScheduleCleanupStatus(int RetiredSchedules,int UnfinishedWorkers,int Capacity);

public sealed partial class WorldRunner
{
    // Reserve a retirement slot at attachment time; recovery itself never needs a new slot.
    public const int MaxRetainedSchedules=4;
    private readonly List<ScheduleExecutor> _retiredSchedules=new(MaxRetainedSchedules);
    private bool _scheduleFault;
    private Guid _faultWorld;
    private ulong _faultTick,_faultRevision;
    public bool CanRecoverScheduleFault { get { World.VerifyAccess(); return IsFaulted && _scheduleFault && World.Identity==_faultWorld && World.Tick==_faultTick && World.Revision==_faultRevision; } }
    public ScheduleCleanupStatus ScheduleCleanup
    {
        get { World.VerifyAccess(); int workers=0;foreach(var schedule in _retiredSchedules)workers+=schedule.PendingWorkerCount;return new(_retiredSchedules.Count,workers,MaxRetainedSchedules); }
    }
    private void RetireActiveSchedule()
    {
        if(_schedule is null)return;
        _schedule.Retire(); _retiredSchedules.Add(_schedule); _schedule=null; _submitStructure=null;
    }
    // Nonblocking maintenance: a pending return does not fault or pause simulation.
    public bool CollectScheduleResources()
    {
        World.VerifyAccess();if(_advancing || World.IsUpdating)throw new InvalidOperationException("Collect at an idle boundary.");
        for(int i=_retiredSchedules.Count-1;i>=0;i--)
            if(_retiredSchedules[i].TryReclaim(TimeSpan.Zero))_retiredSchedules.RemoveAt(i);
        return _retiredSchedules.Count==0;
    }
    // Explicit unload/shutdown gate only. A common deadline bounds all retired graphs together.
    public void WaitForScheduleResources(TimeSpan budget)
    {
        World.VerifyAccess();if(_advancing || World.IsUpdating || budget<TimeSpan.Zero || budget>TimeSpan.FromSeconds(5))throw new InvalidOperationException("Invalid resource cleanup boundary/budget.");
        var watch=Stopwatch.StartNew();
        for(int i=_retiredSchedules.Count-1;i>=0;i--) {
            var left=budget-watch.Elapsed;if(left<TimeSpan.Zero)left=TimeSpan.Zero;
            if(_retiredSchedules[i].TryReclaim(left))_retiredSchedules.RemoveAt(i);
        }
        if(_retiredSchedules.Count!=0)throw new ScheduleCleanupPendingException("Retired schedule resources remain owned; unloading is blocked, not execution recovery.");
    }
}
