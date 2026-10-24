using Ncma.Characters;
using Ncma.Gameplay;
using Ncma.Physics;
using Ncma.Samples;
using Ncma.Scene;
using Ncma.Scene.Rendering;
using System.Text.Json;

internal static class SimulationCompositionChecks
{
    internal static void Run(string repository, string output, string plugins)
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
        void Reject(Action action) { try { action(); } catch (InvalidOperationException) { checks++; return; } throw new Exception("Expected owner-thread rejection"); }
        foreach (int count in new[] { 0, 1, 8, 32 }) {
            var sample = ActionSample.Create(Path.Combine(output,"simulation-composition"), Path.Combine(repository,"out/managed/Ncma.Gameplay.Sample.dll"),count);
            byte[]? baseline = null;
            foreach (bool enabled in new[] { false, true }) {
                var document = new SceneDocument("Simulation costs",CharacterComponents.Register(RenderComponentRegistry.CreateRegistry()),CharacterComponents.RequireComposition);
                SceneDocumentFiles.Load(document,Path.Combine(sample.Root,"start.ncmascene"));
                using var assets = SceneAssetPreparation.Prepare(sample.Root,sample.ProjectId,document.CaptureSnapshot(),true,"assets/action.ncpak");
                using var physics = new PhysicsService(plugins,enabled:count!=0,characterSupport:count!=0);
                var runtimeScene = document.BakeRuntime(document.Revision).Image.Instantiate(document.Components);
                byte[] authoring = document.CaptureBytes();
                using var play = new PlaySession(runtimeScene,FrameTimePolicy.Strict);
                using var runtime = ScenePlayRuntime.Compose(play,physics,assets,profile:enabled);
                Check(runtime.ReadSimulationCosts() is null,"startup is not a simulation observation");
                play.Start(_=>throw new Exception("No behaviours expected"));
                foreach (var actor in sample.Actors) runtime.Characters!.RequestAction(actor,ActionRequest.Attack);
                // Four actual advances, twelve committed quanta: character profile is last step, not the sum.
                for (int i=0;i<4;i++) Check(play.AdvanceFrame(3d/60).State==PlayState.Running,"actual coupled advance");
                var report = runtime.ReadSimulationCosts();
                Check(physics.Inspect().Worlds==(count==0?0:1),"only nonempty character scenes own a solver");
                if (!enabled) { Check(report is null,"disabled composition has no report"); baseline=runtimeScene.CaptureCheckpoint().CopyBytes(); }
                else {
                    Check(report is { Advance:{Completed:true,IncludesUpdate:true,AdvanceSequence:4,TickAfter:12,CommittedSteps:3} },"advance scope covers all three committed steps");
                    Check(baseline!.SequenceEqual(runtimeScene.CaptureCheckpoint().CopyBytes()),"instrumentation preserves complete scene state");
                    Check(!report!.Value.FullPerformanceAccepted,"functional observation is not performance acceptance");
                    if(count==0) Check(report.Value.LastCharacterStep is null,"zero actors do not fabricate character costs");
                    else {
                        var last=report.Value.LastCharacterStep!.Value;
                        Check(last.PlaySessionId==play.SessionId&&last.WorldId==runtimeScene.World.Identity&&last.Tick==12&&
                            last.Native.Sequence==runtime.Characters!.Status.CommittedSequence&&last.Native.LiveJobs==0,
                            "actual native profile belongs to final committed step only");
                        var native=JsonSerializer.SerializeToElement(report.Value).GetProperty("LastCharacterStep").GetProperty("Native");
                        Check(native.EnumerateObject().Count()==10&&native.GetProperty("Sequence").GetUInt64()==last.Native.Sequence&&
                            native.GetProperty("LiveBodies").GetUInt64()==last.Native.LiveBodies&&native.GetProperty("LiveJobs").GetUInt64()==0&&
                            native.GetProperty("LastStepMilliseconds").GetDouble()==last.Native.LastStepMilliseconds&&!native.TryGetProperty("StructSize",out _),
                            "default JSON retains actual copied counters without interop layout fields");
                    }
                    Reject(()=>Task.Run(()=>runtime.ReadSimulationCosts()).GetAwaiter().GetResult());
                    play.Pause(); play.AdvanceFrame(0);
                    Check(runtime.ReadSimulationCosts() is { LastCharacterStep:null,Advance:{IncludesUpdate:false,CommittedSteps:0} },"paused no-op cannot reuse last native work");
                    play.Step(); var retained=runtime.ReadSimulationCosts()!.Value;
                    Check(retained.Advance is { Kind:SimulationAdvanceKind.PausedStep,TickAfter:13,CommittedSteps:1 },"paused real step observed");
                    Guid session=play.SessionId;
                    play.Reload(_=>throw new Exception("No behaviours expected"));
                    Check(play.SessionId!=session&&runtime.ReadSimulationCosts() is null,"reload invalidates old-session sample");
                    play.Step(); var restarted=runtime.ReadSimulationCosts()!.Value;
                    Check(restarted.Advance.AdvanceSequence==1&&restarted.Advance.SessionId==play.SessionId&&
                        (count==0||restarted.LastCharacterStep!.Value.PlaySessionId==play.SessionId),"reloaded coupled domain stamps fresh identity");
                    Check(retained.Advance.SessionId==session&&retained.Advance.TickAfter==13,"copied old report stays immutable");
                }
                play.Stop();
                Check(authoring.SequenceEqual(document.CaptureBytes()),"simulation never writes the authoring document");
                Check(runtime.ReadSimulationCosts() is null&&physics.Inspect().Worlds==0,"Stop clears diagnostics and releases numerical world");
            }
        }
        Console.WriteLine($"Simulation composition: {checks} checks passed; actual 0/1/8/32 actors, not target performance.");
    }
}
