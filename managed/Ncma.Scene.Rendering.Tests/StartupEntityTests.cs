using Ncma.Animation;
using Ncma.Characters;
using Ncma.Gameplay;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static partial class Program
{
    private readonly record struct Marker(int Value) : IComponent;
    private static ComponentRegistry StartupRegistry(bool marker = true)
    {
        var registry = ComponentRegistry.CreateDefault();
        if (marker) registry.Register<Marker>("test.marker", 1, """{"type":"object","required":["value"],"properties":{"value":{"type":"integer"}}}""", value => value);
        return registry;
    }
    private static void RegisterStartupEntityTests(List<(string Name, Action Run)> tests)
    {
        tests.Add(("E4-B optional types retain absent semantics", () => {
            var w = new World(components: StartupRegistry(false)); var e = w.CreateEntity("A"); w.Set(e, TransformData.Identity);
            Check(SceneStartupEntities.With<Marker>(w).Length == 0);
            Check(SceneStartupEntities.WithBoth<TransformData, Marker>(w).Length == 0);
            Check(SceneStartupEntities.Without<TransformData, Marker>(w).SequenceEqual(new[] { e }));
            Check(SceneStartupEntities.Without<Marker, TransformData>(w).Length == 0);
            Check(SceneStartupEntities.EitherByUuid<Marker, TransformData>(w).SequenceEqual(new[] { e }));
            Check(SceneStartupEntities.EitherByUuid<Marker, Marker>(w).Length == 0);
        }));
        tests.Add(("E4-B UUID union differs from creation order and deduplicates", () => {
            var w = new World(components: StartupRegistry());
            var a = w.CreateEntity("A", Guid.Parse("00000003-0000-0000-0000-000000000000"));
            var b = w.CreateEntity("B", Guid.Parse("00000001-0000-0000-0000-000000000000"));
            var c = w.CreateEntity("C", Guid.Parse("00000002-0000-0000-0000-000000000000"));
            w.Set(a, TransformData.Identity); w.Set(a, new Marker(1)); w.Set(b, TransformData.Identity); w.Set(c, new Marker(2));
            Check(SceneStartupEntities.With<TransformData>(w).SequenceEqual(new[] { a, b }));
            Check(SceneStartupEntities.WithByUuid<TransformData>(w).SequenceEqual(new[] { b, a }));
            Check(SceneStartupEntities.EitherByUuid<TransformData, Marker>(w).SequenceEqual(new[] { b, c, a }));
            Check(SceneStartupEntities.WithBoth<TransformData, Marker>(w).SequenceEqual(new[] { a }));
            Check(SceneStartupEntities.Without<TransformData, Marker>(w).SequenceEqual(new[] { b }));
        }));
        tests.Add(("E4-B selection rebuilds after restore and output is owned", () => {
            var w = new World(components: StartupRegistry()); var e = w.CreateEntity("A"); w.Set(e, new Marker(1));
            var copy = SceneStartupEntities.With<Marker>(w); copy[0] = default;
            Check(SceneStartupEntities.With<Marker>(w)[0] == e);
            var snapshot = w.CaptureSnapshot(); w.RestoreSnapshot(snapshot);
            var fresh = SceneStartupEntities.With<Marker>(w)[0]; Check(fresh != e && w.GetUuid(fresh) == snapshot.Objects[0].PersistentId);
            Reject(() => w.Resolve(e)); w.Destroy(fresh); Check(SceneStartupEntities.With<Marker>(w).Length == 0);
        }));
        tests.Add(("E4-B fixed seed selection oracle across structural changes", () => {
            var random = new Random(41019); var w = new World(components: StartupRegistry());
            for (int step = 0; step < 300; step++) {
                var all = w.GetEntities();
                if (all.Count == 0 || all.Count < 100 && random.Next(3) == 0) w.CreateEntity("object");
                else { var e = all[random.Next(all.Count)]; switch (random.Next(5)) {
                    case 0: w.Destroy(e); break;
                    case 1: w.Set(e, TransformData.Identity); break;
                    case 2: w.Set(e, new Marker(step)); break;
                    case 3: if (w.Has<Marker>(e)) w.Remove<Marker>(e); break;
                    case 4: if (w.Has<TransformData>(e)) w.Remove<TransformData>(e); break;
                }}
                all = w.GetEntities();
                Check(SceneStartupEntities.With<TransformData>(w).SequenceEqual(all.Where(e => w.Has<TransformData>(e))));
                Check(SceneStartupEntities.WithBoth<TransformData, Marker>(w).SequenceEqual(all.Where(e => w.Has<TransformData>(e) && w.Has<Marker>(e))));
                Check(SceneStartupEntities.Without<TransformData, Marker>(w).SequenceEqual(all.Where(e => w.Has<TransformData>(e) && !w.Has<Marker>(e))));
                Check(SceneStartupEntities.EitherByUuid<TransformData, Marker>(w).SequenceEqual(all.Where(e => w.Has<TransformData>(e) || w.Has<Marker>(e)).OrderBy(e => w.GetUuid(e))));
            }
        }));
        tests.Add(("E4-B 4096 union has no truncation or duplicate", () => {
            var w = new World(components: StartupRegistry());
            for (int i = 0; i < World.MaxObjects; i++) { var e = w.CreateEntity("object"); w.Set(e, new Marker(i)); if (i % 2 == 0) w.Set(e, TransformData.Identity); }
            var result = SceneStartupEntities.EitherByUuid<TransformData, Marker>(w);
            Check(result.Length == World.MaxObjects && result.Distinct().Count() == World.MaxObjects);
            Check(result.SequenceEqual(w.GetEntities().OrderBy(e => w.GetUuid(e))));
        }));
        tests.Add(("E4-B optional empty selection still rejects wrong thread", () => {
            var w = new World(); Task.Run(() => {
                Reject(() => SceneStartupEntities.With<Marker>(w));
                Reject(() => SceneStartupEntities.WithBoth<TransformData, Marker>(w));
                Reject(() => SceneStartupEntities.Without<Marker, TransformData>(w));
                Reject(() => SceneStartupEntities.EitherByUuid<Marker, Marker>(w));
            }).GetAwaiter().GetResult();
        }));
        tests.Add(("E4-B invalid root and animator remain selected for validation", () => {
            var d = Document(); var root = d.World.CreateEntity("missing dependencies"); d.World.Set(root, new RootMotionData(0));
            Check(SceneStartupEntities.Without<RootMotionData, AnimatorData>(d.World).Single() == root);
            var animator = d.World.CreateEntity("missing skin"); d.World.Set(animator, new AnimatorData(Guid.NewGuid(), Guid.NewGuid()));
            Check(SceneStartupEntities.WithByUuid<AnimatorData>(d.World).Single() == animator);
            Reject(d.ValidateAuthoring); // Selection must not hide malformed composition.
        }));
        tests.Add(("E4-B follow camera keeps absent and recreated UUID behavior", () => {
            var d = new SceneDocument("follow", CharacterComponents.Register(RenderComponentRegistry.CreateRegistry()));
            var target = d.World.CreateEntity("target"); d.World.Set(target, TransformData.Identity);
            var camera = d.World.CreateEntity("camera"); Guid id = d.World.GetUuid(camera);
            d.World.Set(camera, CameraData.Default); d.World.Set(camera, new FollowCameraData(d.World.GetUuid(target), 0, 2, 4, 1));
            using var play = new PlaySession(d); play.Start(_ => throw new Exception("No scripts expected")); play.Pause();
            var view = CharacterPlayRuntime.FollowView(play, id, 800, 600); Check(view is not null);
            Check(CharacterPlayRuntime.FollowView(play, Guid.Empty, 800, 600) is null);
            Check(CharacterPlayRuntime.FollowView(play, Guid.NewGuid(), 800, 600) is null);
            play.Stop(); d.World.Destroy(camera);
            Check(CharacterPlayRuntime.FollowView(play, id, 800, 600) is null);
            var fresh = d.World.CreateEntity("replacement", id); Check(fresh != camera);
            d.World.Set(fresh, CameraData.Default); d.World.Set(fresh, new FollowCameraData(d.World.GetUuid(target), 0, 2, 4, 1));
            play.Start(_ => throw new Exception("No scripts expected")); play.Pause();
            Check(CharacterPlayRuntime.FollowView(play, id, 800, 600) == view);
            Task.Run(() => Reject(() => CharacterPlayRuntime.FollowView(play, id, 800, 600))).GetAwaiter().GetResult();
        }));
    }
}
