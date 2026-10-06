using V3 = System.Numerics.Vector3;
using Ncma.Characters;
using Ncma.Gameplay;
using Ncma.Physics;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;
using Ncma.Application;
using Ncma.Player.App;
using System.Reflection;
using System.Text.Json;
internal static class CharacterHostChecks
{
    private static readonly Guid Character = Guid.Parse("11111111-1111-1111-1111-111111111111"), Camera = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static void Check(bool v, string m) { if (!v) throw new Exception(m); }
    private sealed class SystemAction(Action<World> action) : IWorldSystem { public void FixedUpdate(World world, double h) => action(world); }
    private sealed class BadObserver : ICommittedStepObserver { public void StepCommitted(World world, double h) => throw new Exception("Injected post-commit fault"); }
    internal static SceneDocument Fixture(int dense = 0)
    {
        var doc = new SceneDocument("K3 actual character", CharacterComponents.Register(RenderComponentRegistry.CreateRegistry()), CharacterComponents.RequireComposition);
        var actor = doc.World.CreateObject("Character", Character); actor.Set(TransformData.Identity with { Position = new(0, .1f, 0) }); actor.Set(CharacterData.Default);
        for (int i = 0; i < Math.Max(1, dense); ++i) { var floor = doc.World.CreateObject("Floor" + i); floor.Set(TransformData.Identity with { Position = new(0, -.5f, 0) }); floor.Set(new BoxColliderData(20, .5f, 20, 1000, 1, false)); }
        var wall = doc.World.CreateObject("Wall"); wall.Set(TransformData.Identity with { Position = new(2, 2, 0) }); wall.Set(new BoxColliderData(.2f, 2, 20, 1000, 1, false));
        var camera = doc.World.CreateObject("Follow camera", Camera); camera.Set(TransformData.Identity with { Position = new(0, 3, 6) }); camera.Set(CameraData.Default); camera.Set(new FollowCameraData(Character, 0, 3, 6, 1));
        return doc;
    }
    internal static void Input(PlaySession play, ulong sequence, bool focused = true, int key = 68, bool pressed = false)
    {
        var held = new ulong[8]; var edges = new ulong[8]; if (key >= 0) { held[key / 64] |= 1UL << (key % 64); if (pressed) edges[key / 64] |= 1UL << (key % 64); }
        play.SubmitInput(new(play.SessionId, sequence, focused, held, edges, new ulong[8]));
    }
    internal static string[] Run(string output, string plugins)
    {
        var passed = new List<string>();
        static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected invalid composition"); }
        {
            var doc = Fixture(); var actor = doc.World.FindObject(Character); actor.Set(TransformData.Identity with { Scale = new(2, 1, 1) }); Reject(doc.ValidateAuthoring);
            actor.Set(TransformData.Identity); var camera = doc.World.FindObject(Camera); camera.Set(new FollowCameraData(Guid.NewGuid(), 0, 3, 6, 1)); Reject(doc.ValidateAuthoring);
            camera.Set(new FollowCameraData(Character, 0, 3, 6, 1)); actor.Set(new BoxColliderData(1, 1, 1, 1000, 1, false)); Reject(doc.ValidateAuthoring);
            Reject(() => CharacterData.Validate(CharacterData.Default with { Radius = float.NaN })); Reject(() => FollowCameraData.Validate(new(Character, 0, 3, 0, 1)));
        }
        passed.Add("Strict value schemas/composition: unit scale, exact follow UUID, exclusive shape and finite nondegenerate values");
        using (var disabled = new PhysicsService("unused-no-native-directory", enabled: false))
        foreach (int type in new[] { 0, 1, 2 }) {
            var doc = new SceneDocument("No physics startup", CharacterComponents.Register(RenderComponentRegistry.CreateRegistry()), CharacterComponents.RequireComposition);
            var ordinary = doc.World.CreateObject("Ordinary"); ordinary.Set(TransformData.Identity); var registry = doc.World.Components;
            ComponentSnapshot addition = type switch {
                0 => new(CharacterData.TypeId, 1, registry.Encode(CharacterData.Default)),
                1 => new(BoxColliderData.TypeId, 1, registry.Encode(new BoxColliderData(1, 1, 1, 1000, 1, false))),
                _ => new(FollowCameraData.TypeId, 1, registry.Encode(new FollowCameraData(Character, 0, 3, 6, 1))) };
            byte[] before = doc.CaptureBytes(); using var play = new PlaySession(doc, advanceMode: PlayAdvanceMode.FixedSteps, policy: FrameTimePolicy.Strict);
            Check(CharacterPlayRuntime.Compose(play, disabled) is null, "No physics creates no numerical composition");
            play.AddSystem(new SystemAction(_ => { try { play.Commands.AddComponent(ordinary.PersistentId, addition); } catch (InvalidOperationException) { } }));
            play.Start(_ => throw new Exception("No behaviours")); play.AdvanceFixedStep();
            Check(play.State == PlayState.Faulted && play.Tick == 0 && disabled.Inspect().Worlds == 0 && before.SequenceEqual(doc.CaptureBytes()), "Disabled/no-binding Play refuses unsupported runtime physics/follow attachment without loading a solver");play.Stop();
        }
        passed.Add("No-binding disabled Play rejects startup-only component attachment with zero numerical worlds");
        using (var service = new PhysicsService(plugins, characterSupport: true))
        {
            V3? reference = null;
            foreach (int hz in new[] { 0, 30, 60, 144 })
            {
                var doc = Fixture(); byte[] startup = doc.CaptureBytes(); using var play = new PlaySession(doc, FrameTimePolicy.Strict, advanceMode: hz == 0 ? PlayAdvanceMode.FixedSteps : PlayAdvanceMode.Frames);
                using var runtime = CharacterPlayRuntime.Compose(play, service)!; play.Start(_ => throw new Exception("No behaviours")); Input(play, 1);
                if (hz == 0) for (int i = 0; i < 120; ++i) Check(play.AdvanceFixedStep().State == PlayState.Running, "headless character step");
                else for (int i = 0; i < hz * 2; ++i) Check(play.AdvanceFrame(1.0 / hz).State == PlayState.Running, "framerate character step");
                var transform = doc.World.FindObject(Character).Get<TransformData>();
                Check(play.Tick == 120 && runtime.Status.CommittedSequence == 120 && runtime.Status.SnapshotValid && transform.Position.X < 1.51f && transform.Position.X > 1.45f, "actual wall/ground fixed-step movement");
                Check(MathF.Abs(transform.Rotation.Y) > .7f, "character turns toward movement separately from camera");
                if (reference is null) reference = transform.Position; else Check(V3.Distance(reference.Value, transform.Position) < 1e-5f, "30/60/144/headless same fixed-step sequence");
                ulong tick = play.Tick; byte[] committed = doc.CaptureBytes(); var follow = CharacterPlayRuntime.FollowView(play, Camera, 640, 480);
                Check(follow is not null && follow.Value.Position.X > 1.4f && committed.SequenceEqual(doc.CaptureBytes()) && play.Tick == tick, "follow camera readonly");
                play.Pause(); Input(play, 2, key: 32, pressed: true); play.AdvanceFrameIfFrames(hz); Check(play.Tick == tick, "pause no simulation");
                play.Step(); Check(play.Tick == tick + 1, "single step quantum"); play.Resume(); Input(play, 3, focused: false);
                if (hz == 0) play.AdvanceFixedStep(); else play.AdvanceFrame(1.0 / 60);
                Check(MathF.Abs(doc.World.FindObject(Character).Get<TransformData>().Position.X - transform.Position.X) < .01f, "focus loss clears horizontal movement");
                Guid oldSession = play.SessionId, oldWorld = doc.World.Identity, oldEpoch = runtime.NumericalEpoch;
                play.Reload(_ => throw new Exception("No behaviours")); Check(play.State == PlayState.Paused && play.SessionId != oldSession && doc.World.Identity != oldWorld && runtime.NumericalEpoch != oldEpoch && runtime.Status.CommittedSequence == 0, "reload fresh solver/world/session");
                Check(doc.World.FindObject(Character).Get<TransformData>().Position == new V3(0, .1f, 0), "reload from frozen startup");
                play.Stop(); Check(service.Inspect().Worlds == 0, "world drained after stop");
                Check(!startup.SequenceEqual(committed), "movement changed live runtime only");
            }
            passed.Add("Real Jolt 30/60/144Hz/headless parity, wall/ground, readonly follow, Pause/Step/focus and fresh reload");
            {
                var doc = Fixture(); using var play = new PlaySession(doc, FrameTimePolicy.Strict, advanceMode: PlayAdvanceMode.FixedSteps); using var runtime = CharacterPlayRuntime.Compose(play, service)!;
                play.Start(_ => throw new Exception("No behaviours")); Input(play, 1, key: -1); for (int i = 0; i < 12; ++i) play.AdvanceFixedStep();
                float floor = doc.World.FindObject(Character).Get<TransformData>().Position.Y; Input(play, 2, key: 32, pressed: true); play.AdvanceFixedStep();
                Check(doc.World.FindObject(Character).Get<TransformData>().Position.Y > floor + .04f, "jump from grounded copied state");
                Input(play, 3, key: 32); float peak = floor; for (int i = 0; i < 90; ++i) { play.AdvanceFixedStep(); peak = Math.Max(peak, doc.World.FindObject(Character).Get<TransformData>().Position.Y); }
                Check(peak > floor + .8f && MathF.Abs(doc.World.FindObject(Character).Get<TransformData>().Position.Y - floor) < .04f, "gravity lands / held jump not repeated"); play.Stop();
            }
            passed.Add("Grounded jump, gravity, landing and transient single consumption");
            {
                var doc = Fixture(); byte[] before = doc.CaptureBytes(); using var play = new PlaySession(doc, advanceMode: PlayAdvanceMode.FixedSteps, policy: FrameTimePolicy.Strict); using var runtime = CharacterPlayRuntime.Compose(play, service)!;
                play.AddSystem(new SystemAction(_ => { var pending = play.Commands.SpawnEmpty("Late factory fault"); play.Commands.AttachBehaviour(pending.ObjectId, new(Guid.NewGuid(), "Missing.Type", true, [])); }));
                play.Start(_ => throw new Exception("Injected late factory failure")); Input(play, 1, pressed: true); play.AdvanceFixedStep();
                Check(play.State == PlayState.Faulted && play.Tick == 0 && !runtime.Status.SnapshotValid && runtime.Status.NumericalExecutionStarted && before.SequenceEqual(doc.CaptureBytes()), "real solver success followed by managed failure invalidates coupled snapshot without solver rollback"); play.Stop();
            }
            foreach (bool withCharacter in new[] { true, false })
            {
                var doc = Fixture(); if (!withCharacter) { doc.World.FindObject(Character).Destroy(); doc.World.FindObject(Camera).Remove<FollowCameraData>(); }
                var box = doc.World.CreateObject("Dynamic box"); box.Set(TransformData.Identity with { Position = new(-4, 3, 0) }); box.Set(new BoxColliderData(.2f, .2f, .2f, 1000, 4, true));
                using var play = new PlaySession(doc, advanceMode: PlayAdvanceMode.FixedSteps, policy: FrameTimePolicy.Strict); using var runtime = CharacterPlayRuntime.Compose(play, service)!; play.Start(_ => throw new Exception("No behaviours"));
                for (int i = 0; i < 30; ++i) Check(play.AdvanceFixedStep().State == PlayState.Running, "dynamic body coupled step");
                Check(doc.World.FindObject(box.PersistentId).Get<TransformData>().Position.Y < 2, "C# publishes real dynamic body results"); play.Stop();
            }
            passed.Add("Real post-solver managed failure fail-stop and dynamic box publication");
            {
                var doc = Fixture(); doc.World.GetObjects().Single(o => o.Name == "Wall").Destroy();
                doc.World.GetObjects().Single(o => o.Name == "Floor0").Set(TransformData.Identity with { Position = new(0, -.5f, 0), Rotation = System.Numerics.Quaternion.CreateFromAxisAngle(V3.UnitZ, .3f) });
                using var play = new PlaySession(doc, advanceMode: PlayAdvanceMode.FixedSteps, policy: FrameTimePolicy.Strict); using var runtime = CharacterPlayRuntime.Compose(play, service)!; play.Start(_ => throw new Exception("No behaviours")); Input(play, 1);
                for (int i = 0; i < 60; ++i) Check(play.AdvanceFixedStep().State == PlayState.Running, "actual slope step"); var position = doc.World.FindObject(Character).Get<TransformData>().Position;
                Check(position.X > 2 && position.Y > .6f && runtime.Status.SnapshotValid, "actual C# movement follows permitted slope"); play.Stop();
            }
            passed.Add("Real Jolt slope grounding under C# locomotion policy");
            foreach (int mode in new[] { 0, 1, 2 })
            {
                var doc = Fixture(); using var play = new PlaySession(doc, advanceMode: PlayAdvanceMode.FixedSteps, policy: FrameTimePolicy.Strict); using var runtime = CharacterPlayRuntime.Compose(play, service)!;
                play.AddSystem(new SystemAction(world =>
                {
                    try
                    {
                        if (mode == 0) world.FindObject(Character).Set(TransformData.Identity);
                        else if (mode == 1) world.FindObject(Character).Set(CharacterData.Default with { Speed = 20 });
                        else world.FindObject(Camera).Set(new FollowCameraData(Character, 1, 3, 6, 1));
                    }
                    catch (InvalidOperationException) { }
                })); play.Start(_ => throw new Exception("No behaviours"));
                var status = play.AdvanceFixedStep(); Check(status.State == PlayState.Faulted && play.Tick == 0 && !runtime.Status.NumericalExecutionStarted && runtime.Status.CommittedSequence == 0, "caught transform/config mutation poisons BEFORE numerics"); play.Stop();
            }
            passed.Add("Caught Transform/capsule/follow-config writes cannot bypass authority");
            foreach (int type in new[] { 0, 1, 2 })
            {
                var doc = Fixture(); if (type == 0) { doc.World.FindObject(Character).Destroy(); doc.World.FindObject(Camera).Remove<FollowCameraData>(); }
                if (type == 2) doc.World.FindObject(Camera).Remove<FollowCameraData>();
                var ordinary = doc.World.CreateObject("Unbound ordinary"); ordinary.Set(TransformData.Identity);
                var registry = doc.World.Components;
                ComponentSnapshot addition = type switch {
                    0 => new(CharacterData.TypeId, 1, registry.Encode(CharacterData.Default)),
                    1 => new(BoxColliderData.TypeId, 1, registry.Encode(new BoxColliderData(1, 1, 1, 1000, 1, false))),
                    _ => new(FollowCameraData.TypeId, 1, registry.Encode(new FollowCameraData(Character, 0, 3, 6, 1))) };
                byte[] before = doc.CaptureBytes(); using var play = new PlaySession(doc, advanceMode: PlayAdvanceMode.FixedSteps, policy: FrameTimePolicy.Strict); using var runtime = CharacterPlayRuntime.Compose(play, service)!;
                play.AddSystem(new SystemAction(_ => { try { play.Commands.AddComponent(ordinary.PersistentId, addition); } catch (InvalidOperationException) { } }));
                play.Start(_ => throw new Exception("No behaviours")); play.AdvanceFixedStep();
                Check(play.State == PlayState.Faulted && play.Tick == 0 && !runtime.Status.NumericalExecutionStarted && before.SequenceEqual(doc.CaptureBytes()), "Runtime attachment to unbound objects, including empty membership, poisons before solver");
                play.Stop(); ordinary.Set(CharacterData.Default with { Controlled = false });
            }
            {
                var doc = Fixture(); using var play = new PlaySession(doc, advanceMode: PlayAdvanceMode.FixedSteps, policy: FrameTimePolicy.Strict); using var runtime = CharacterPlayRuntime.Compose(play, service)!;
                play.AddSystem(new SystemAction(_ => { var ordinary = play.Commands.SpawnEmpty("Legal unbound"); play.Commands.AddComponent(ordinary.ObjectId, new("ncma.transform", 1, doc.World.Components.Encode(TransformData.Identity))); }));
                play.Start(_ => throw new Exception("No behaviours")); Check(play.AdvanceFixedStep().State == PlayState.Running && doc.World.Count == 5, "Unbound ordinary structural commands remain legal"); play.Stop();
            }
            passed.Add("Whole-type topology freeze (including absent types), caught new bindings rejected before numerics and ordinary commands preserved");
            {
                var doc = Fixture(80); using var play = new PlaySession(doc, advanceMode: PlayAdvanceMode.FixedSteps, policy: FrameTimePolicy.Strict); using var runtime = CharacterPlayRuntime.Compose(play, service)!;
                play.Start(_ => throw new Exception("No behaviours")); var status = play.AdvanceFixedStep();
                Check(status.State == PlayState.Faulted && play.Tick == 0 && !runtime.Status.SnapshotValid && runtime.Status.NumericalExecutionStarted, "real dense native overflow invalidates coupled world snapshot");
                play.Stop(); Check(service.Inspect().Worlds == 0, "faulted domain released");
            }
            {
                var doc = Fixture(); using var play = new PlaySession(doc, advanceMode: PlayAdvanceMode.FixedSteps, policy: FrameTimePolicy.Strict); using var runtime = CharacterPlayRuntime.Compose(play, service)!;
                play.AddCommittedObserver(new BadObserver()); play.Start(_ => throw new Exception("No behaviours")); var status = play.AdvanceFixedStep();
                Check(status.State == PlayState.Faulted && play.Tick == 1 && runtime.Status.SnapshotValid && runtime.Status.CommittedSequence == 1, "postcommit fault retains actual solved tick"); play.Stop();
            }
            for (int cycle = 0; cycle < 32; ++cycle)
            {
                var doc = Fixture(); using var play = new PlaySession(doc, advanceMode: PlayAdvanceMode.FixedSteps, policy: FrameTimePolicy.Strict); using var runtime = CharacterPlayRuntime.Compose(play, service)!;
                play.Start(_ => throw new Exception("No behaviours")); Check(play.AdvanceFixedStep().State == PlayState.Running, "cycle actual step"); play.Stop(); Check(service.Inspect().Worlds == 0, "32 actual domains drained");
            }
            passed.Add("Actual solver failure/committed observer fault, no rollback claim and 32 start/step/stop lifecycles");
        }
        // Actual Player entry and authoring file isolation; no GUI/reference scene fallback.
        string projectRoot = Path.Combine(output, "Character Player"); Directory.CreateDirectory(projectRoot); var fixture = Fixture();
        string scene = Path.Combine(projectRoot, "start.ncmascene"); SceneDocumentFiles.Save(fixture, scene); byte[] saved = File.ReadAllBytes(scene);
        string assembly = Assembly.GetExecutingAssembly().Location; File.Copy(assembly, Path.Combine(projectRoot, "gameplay.dll")); File.Copy(Path.ChangeExtension(assembly, ".deps.json"), Path.Combine(projectRoot, "gameplay.deps.json"));
        var config = new ProjectConfiguration(1, Guid.NewGuid(), "Character Player", "start.ncmascene", "gameplay.dll", "Direct3D11", [], true, Camera);
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }; string projectFile = Path.Combine(projectRoot, "character.ncmaproject"); File.WriteAllBytes(projectFile, JsonSerializer.SerializeToUtf8Bytes(config, json));
        foreach (bool headless in new[] { true, false })
        {
            var flags = new List<string> { "--project", projectFile, "--ticks", "30", "--report", Path.Combine(projectRoot, headless ? "headless.json" : "graphical.json") }; if (headless) flags.Add("--headless");
            var report = PlayerRunner.Run(PlayerOptions.Parse(flags.ToArray()), pluginRoot: plugins, visible: false, trustedInput: (p, t) => { if (t > p.Input.FrameSequence) Input(p, t); });
            Check(report.ExitCode == 0 && report.Tick >= 30 && report.ShutdownErrors.Length == 0 && report.Modules.Any(m => m.Id == "ncma.physics" && m.AbiMinor == 2), "actual Player coupled entry: " + report.Reason);
            Check(saved.SequenceEqual(File.ReadAllBytes(scene)), "Player did not write scene");
        }
        File.WriteAllBytes(projectFile, JsonSerializer.SerializeToUtf8Bytes(config with { PhysicsEnabled = false }, json));
        var rejected = PlayerRunner.Run(PlayerOptions.Parse(["--project", projectFile, "--headless", "--ticks", "1", "--report", Path.Combine(projectRoot, "disabled.json")]), pluginRoot: plugins);
        Check(rejected.ExitCode == 2 && rejected.Modules.Length == 0 && rejected.Tick == 0, "disabled physics binding preflight before native initialization");
        passed.Add("Actual headless/graphical Player Physics1.2 composition, scene isolation and disabled binding rejection"); return passed.ToArray();
    }
    private static void AdvanceFrameIfFrames(this PlaySession play, int hz) { if (hz != 0) play.AdvanceFrame(.1); }
}
