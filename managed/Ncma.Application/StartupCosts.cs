using System.Diagnostics;

namespace Ncma.Application;

public enum StartupCall { MainPreparation, PresentationStart }
public enum StartupStage {
    Control, PackageValidation, Arguments, Project, HostConstruction,
    ScenePreflight, ShaderPreflight, LogAndPlugins, PacingAndPhysics, Window,
    Preferences, GuiCreate, Renderer, ToolbarIcon, EditorOwner, Document,
    Authoring, RenderAssets, Environment, SceneHud, Gameplay, Presenter, SceneSynchronization, Diagnostics
}
public readonly record struct StartupStageCost(StartupStage Stage, int Visits, long WallTicks, long OwnerAllocatedBytes);
public sealed record StartupCallCost(StartupCall Call, bool Completed, long StartedTimestamp,
    long EndedTimestamp, long OwnerAllocatedBytes, StartupStageCost[] Stages);
public sealed record StartupCostReport(int Version, long TimestampFrequency, StartupCallCost[] Calls)
{
    public bool PerformanceAccepted => false;
    public string Scope => "opt-in sequential MainPreparation/PresentationStart wall and raw owner allocation; includes nested JIT/IO/observation; excludes process/CLR-before-entry, entry JIT, observer construction, report copying/serialization, gaps, loop, rollback and shutdown; GuiCreate is not exclusive font time";
}

// One-shot owner-thread startup observation. Never a clock, authority, callback or resource owner.
// Two fixed call slots; reject repeated calls rather than overwrite a failed first attempt.
public sealed class StartupCostCapture
{
    private const int StageCount = (int)StartupStage.Diagnostics + 1;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly StartupCallCost?[] _calls = new StartupCallCost?[2];
    private readonly StartupStageCost[] _working = new StartupStageCost[StageCount];
    private StartupCall? _active;
    private int _seen;
    private StartupStage _phase;
    private long _start, _allocation, _phaseTime, _phaseAllocation;
    private void Verify()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Startup observation requires owner thread.");
    }
    public StartupCall? ActiveCall { get { Verify(); return _active; } }
    public void Begin(StartupCall call)
    {
        Verify();
        if (!Enum.IsDefined(call) || _active is not null || (_seen & (1 << (int)call)) != 0 ||
            call == StartupCall.MainPreparation && (_seen & 2) != 0)
            throw new InvalidOperationException("Invalid or repeated startup call.");
        _seen |= 1 << (int)call; _active = call; _phase = StartupStage.Control;
        Array.Clear(_working); _working[0] = new(StartupStage.Control, 1, 0, 0);
        _allocation = _phaseAllocation = GC.GetAllocatedBytesForCurrentThread();
        _start = _phaseTime = Stopwatch.GetTimestamp();
    }
    private void Flush(long now, long allocated)
    {
        int index = (int)_phase; var previous = _working[index];
        _working[index] = previous with { WallTicks = checked(previous.WallTicks + now - _phaseTime),
            OwnerAllocatedBytes = checked(previous.OwnerAllocatedBytes + allocated - _phaseAllocation) };
        _phaseTime = now; _phaseAllocation = allocated;
    }
    public void Phase(StartupStage stage)
    {
        Verify();
        if (_active is null || !Enum.IsDefined(stage) ||
            _active == StartupCall.MainPreparation && stage > StartupStage.HostConstruction ||
            _active == StartupCall.PresentationStart && stage is > StartupStage.Control and < StartupStage.ScenePreflight)
            throw new InvalidOperationException("Invalid startup phase.");
        Flush(Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread());
        _phase = stage; int index = (int)stage;
        _working[index] = _working[index] with { Stage = stage, Visits = checked(_working[index].Visits + 1) };
    }
    public void End(bool completed)
    {
        Verify();
        if (_active is not { } call) throw new InvalidOperationException("No active startup call.");
        long now = Stopwatch.GetTimestamp(), allocated = GC.GetAllocatedBytesForCurrentThread();
        Flush(now, allocated);
        var phases = new StartupStageCost[StageCount];
        for (int i = 0; i < StageCount; i++) phases[i] = _working[i] with { Stage = (StartupStage)i };
        _calls[(int)call] = new(call, completed, _start, now, allocated - _allocation, phases);
        _active = null;
    }
    public StartupCostReport Capture()
    {
        Verify();
        if (_active is not null) throw new InvalidOperationException("Cannot capture active startup.");
        return new(1, Stopwatch.Frequency, _calls.Where(c => c is not null)
            .Select(c => c! with { Stages = (StartupStageCost[])c!.Stages.Clone() }).ToArray());
    }
}
