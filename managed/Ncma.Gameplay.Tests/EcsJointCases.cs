using System.Reflection;
using Ncma.Gameplay;
using Ncma.Runtime;
using Ncma.Scene;

internal static class EcsJointCases
{
    private readonly record struct Marker(int Value) : IComponent;
    private sealed class StepSystem(Action<World> action) : IWorldSystem
    { public void FixedUpdate(World world, double delta) => action(world); }
    private static long Count(object value, string field) => (long)value.GetType().GetField(field,
        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static void Check(bool condition) { if (!condition) throw new Exception("E3 joint step assertion failed."); }
    private static Guid Id(int index) => new(index + 1, 0, 0, new byte[8]);
    internal static void Run()
    {
        int passed = 0;
        foreach (int count in new[] { 100, 1000, 4096 })
        foreach (string workload in new[] { "none", "sparse", "full", "membership", "replace" })
        {
            var registry = ComponentRegistry.CreateDefault();
            registry.Register<Marker>("joint.marker", 1,
                """{"type":"object","required":["value"],"properties":{"value":{"type":"integer"}}}""", v => v);
            var document = new SceneDocument("E3 joint fixture", registry); var world = document.World;
            var ids = Enumerable.Range(0, count).Select(Id).ToArray();
            for (int i = 0; i < count; i++)
            {
                var entity = world.CreateEntity("Object " + i, ids[i]);
                world.Set(entity, TransformData.Identity with { Position = new(i, 0, 0) });
                if (i % 100 == 0) world.Set(entity, new Marker(i));
            }
            var query = world.Query<TransformData>(); int steps = 0, nextId = count; bool markerPresent = true;
            using var play = new PlaySession(document, FrameTimePolicy.Strict);
            play.AddSystem(new StepSystem(w => {
                Check(query.Count == count && w.Count == count);
                if (workload is "sparse" or "full")
                    for (int i = 0; i < count; i += workload == "full" ? 1 : 100)
                    {
                        var entity = w.FindEntity(ids[i]); var old = w.Get<TransformData>(entity);
                        w.Set(entity, old with { Position = old.Position + new System.Numerics.Vector3(1, 0, 0) });
                        Check(w.Get<TransformData>(entity) == old);
                    }
                if (workload == "membership")
                    for (int i = 0; i < count; i += 100)
                    {
                        if (markerPresent) play.Commands.RemoveComponent(ids[i], "joint.marker");
                        else play.Commands.AddComponent(ids[i], new("joint.marker", 1, registry.Encode(new Marker(i))));
                        Check(w.Has<Marker>(w.FindEntity(ids[i])) == markerPresent);
                    }
                if (workload == "replace")
                    for (int i = 0; i < count; i += 100)
                    {
                        var old = w.FindEntity(ids[i]); play.Commands.Destroy(ids[i]);
                        ids[i] = Id(nextId++); play.Commands.SpawnEmpty("Object " + i, ids[i]);
                        play.Commands.AddComponent(ids[i], new("ncma.transform", 1,
                            registry.Encode(TransformData.Identity with { Position = new(i, 0, 0) })));
                        Check(w.Resolve(old) == old); // All public reads still see the committed world.
                    }
                markerPresent = !markerPresent; steps++;
            }));
            play.Start(_ => throw new Exception("No bindings expected."));
            Check(play.AdvanceFrame(1d / 60).State == PlayState.Running);
            var captures = (Count(world, "_capturedSnapshotCount"), Count(world, "_copiedValidationSnapshotCount"), Count(document, "_capturedDocumentCount"));
            long encodes = Count(world, "_encodedComponentCount");
            for (int step = 0; step < 4; step++) Check(play.AdvanceFrame(1d / 60).State == PlayState.Running);
            Check(captures == (Count(world, "_capturedSnapshotCount"), Count(world, "_copiedValidationSnapshotCount"), Count(document, "_capturedDocumentCount")));
            int expectedWrites = workload == "full" ? count : workload == "sparse" ? (count + 99) / 100 : 0;
            if (workload is "none" or "sparse" or "full") Check(Count(world, "_encodedComponentCount") == encodes + 4L * expectedWrites);
            Check(world.Tick == (ulong)steps && query.Count == count && world.Count == count);
            play.Pause(); var rendered = play.RenderView.Objects.ToDictionary(o => o.ObjectId);
            for (int i = 0; i < count; i++)
            {
                float expected = i + (workload == "full" || workload == "sparse" && i % 100 == 0 ? steps : 0);
                Check(world.Get<TransformData>(world.FindEntity(ids[i])).Position.X == expected && rendered[ids[i]].Transform.Position.X == expected);
                if (workload == "membership" && i % 100 == 0) Check(world.Has<Marker>(world.FindEntity(ids[i])) == markerPresent);
            }
            // Complete document comparison is an independent cold oracle, outside the checked step window.
            var copy = new SceneDocument("copy", registry); copy.RestoreBytes(document.CaptureBytes());
            Check(copy.CaptureBytes().SequenceEqual(document.CaptureBytes()));
            passed++; Console.WriteLine($"PASS E3 joint {workload} objects={count}: no full World/Scene snapshots; values/queries/render/codec agree.");
        }
        Console.WriteLine($"E3 joint fixed-step tests: {passed}/15 passed.");
    }
}
