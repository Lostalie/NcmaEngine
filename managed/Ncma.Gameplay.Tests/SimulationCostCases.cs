using Ncma;
using Ncma.Gameplay;
using Ncma.Runtime;
using Ncma.Scene;

internal static class SimulationCostCases
{
    private static RuntimeSceneInstance CreateScene(string name)
    {
        var source = new SceneDocument(name);
        return source.BakeRuntime(source.Revision).Image.Instantiate(source.Components);
    }
    private sealed class FailSecondStep : IWorldSystem
    {
        private int _steps;
        public void FixedUpdate(World world, double delta) { if (++_steps == 2) throw new InvalidOperationException("cost fixture fault"); }
    }
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception("Simulation costs: " + label); checks++; }
        void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { checks++; return; } throw new Exception("Expected cost boundary rejection"); }
        PlaySession Create(bool costs, bool fixedSteps = false) {
            var play = new PlaySession(CreateScene("Cost fixture"), FrameTimePolicy.Strict,
                advanceMode: fixedSteps ? PlayAdvanceMode.FixedSteps : PlayAdvanceMode.Frames);
            if (costs) play.EnableSimulationCosts();
            play.Start(_ => throw new Exception("No behaviours")); return play;
        }
        using (var disabled = Create(false)) {
            disabled.AdvanceFrame(1d/60); Check(disabled.LastSimulationCosts is null, "default does not capture");
            Reject(disabled.EnableSimulationCosts); Check(disabled.LastSimulationCosts is null, "cannot enable during active Play");
        }
        SimulationCostReport retained;
        using (var play = Create(true)) {
            Check(play.LastSimulationCosts is null, "startup is not simulated time");
            play.AdvanceFrame(3d/60); var first = play.LastSimulationCosts!.Value;
            Check(first is { Completed:true, Kind:SimulationAdvanceKind.Frame, IncludesUpdate:true, AdvanceSequence:1, SourceFrameCount:1, TickBefore:0, TickAfter:3, CommittedSteps:3 }, "all fixed quanta in one frame scope");
            Check(first.SessionId == play.SessionId && first.WorldId == play.Scene.World.Identity && first.CpuWallMilliseconds >= 0 &&
                double.IsFinite(first.CpuWallMilliseconds) && first.OwnerThreadAllocatedBytes >= 0 && !first.FullPerformanceAccepted, "identity/finite scoped observation");
            Reject(() => play.AdvanceFrame(double.NaN)); Reject(() => play.AdvanceFrame(1));
            Check(play.LastSimulationCosts == first, "rejected preflight does not change counters or diagnostics");
            Reject(() => Task.Run(() => _ = play.LastSimulationCosts).GetAwaiter().GetResult());
            play.AdvanceFrame(0); Check(play.LastSimulationCosts is { CommittedSteps:0, IncludesUpdate:true, AdvanceSequence:2 }, "zero-quantum running frame");
            play.Pause(); play.AdvanceFrame(0); Check(play.LastSimulationCosts is { CommittedSteps:0, IncludesUpdate:false, State:PlayState.Paused, AdvanceSequence:3 }, "paused no-op does not claim Update");
            play.Step(); retained = play.LastSimulationCosts!.Value;
            Check(retained is { Completed:true, Kind:SimulationAdvanceKind.PausedStep, CommittedSteps:1, IncludesUpdate:false, State:PlayState.Paused, AdvanceSequence:4 }, "UI paused Step is captured at authoritative operation");
            play.Reload(_ => throw new Exception("No behaviours"));
            Check(play.LastSimulationCosts is null && play.SessionId != retained.SessionId, "reload clears old-session observation");
            play.Step(); Check(play.LastSimulationCosts is { AdvanceSequence:1 } && play.LastSimulationCosts.Value.SessionId == play.SessionId, "new session sequence");
            play.Stop(); Check(play.LastSimulationCosts is null, "Stop clears active costs");
            play.Start(_ => throw new Exception("No behaviours")); Check(play.LastSimulationCosts is null, "restart is not old measured work");
        }
        Check(retained.TickAfter == 4, "copied report survives Play disposal");
        using (var play = Create(true, true)) {
            play.AdvanceFixedStep(); Check(play.LastSimulationCosts is { Kind:SimulationAdvanceKind.FixedStep, SourceFrameCount:0, TickAfter:1, IncludesUpdate:false }, "headless no Update/frame claim");
            Reject(() => play.AdvanceFrame(0));
        }
        using (var play = new PlaySession(CreateScene("Fault costs"), FrameTimePolicy.Strict)) {
            play.EnableSimulationCosts(); play.AddSystem(new FailSecondStep()); play.Start(_ => throw new Exception("No behaviours"));
            play.AdvanceFrame(3d/60);
            Check(play.State == PlayState.Faulted && play.LastSimulationCosts is { Completed:false, IncludesUpdate:false, TickBefore:0, TickAfter:1, CommittedSteps:1 }, "partial fixed-step fault never enters Update or claims successful whole advance");
        }
        using (var off = Create(false)) using (var on = Create(true)) {
            long Allocations(PlaySession play) { long before = GC.GetAllocatedBytesForCurrentThread(); for (int i=0;i<128;i++) play.AdvanceFrame(0); return GC.GetAllocatedBytesForCurrentThread()-before; }
            _ = Allocations(off); _ = Allocations(on);
            long baseline = Allocations(off), observed = Allocations(on);
            Check(observed == baseline, $"value observation adds no warm empty-advance allocation ({baseline}/{observed}); not whole-engine zero allocation");
        }
        Console.WriteLine($"Simulation cost scopes: {checks} checks passed.");
    }
}
