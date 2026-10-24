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
            var document = new SceneDocument("E3 joint fixture", registry);
            var ids = Enumerable.Range(0, count).Select(Id).ToArray();
            for (int i = 0; i < count; i++)
            {
                Guid entity = document.CreateObject("Object " + i, ids[i]);
                document.SetComponent(entity, TransformData.Identity with { Position = new(i, 0, 0) });
                if (i % 100 == 0) document.SetComponent(entity, new Marker(i));
            }
            byte[] authoringBytes = document.CaptureBytes();
            var scene = document.BakeRuntime(document.Revision).Image.Instantiate(registry);
            var world = scene.World;
            var query = world.Query<TransformData>(); int steps = 0, nextId = count; bool markerPresent = true;
            using var play = new PlaySession(scene, FrameTimePolicy.Strict);
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
            var captures = (Count(world, "_capturedSnapshotCount"), Count(world, "_copiedValidationSnapshotCount"), Count(scene, "_capturedSnapshots"));
            long encodes = Count(world, "_encodedComponentCount");
            for (int step = 0; step < 4; step++) Check(play.AdvanceFrame(1d / 60).State == PlayState.Running);
            Check(captures == (Count(world, "_capturedSnapshotCount"), Count(world, "_copiedValidationSnapshotCount"), Count(scene, "_capturedSnapshots")));
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
            // Complete runtime state roundtrip is cold, outside the checked step window.
            // Runtime inspection never creates an EditSession or an authoring document wrapper.
            VerifyInspection(scene, query, rendered.Keys.ToHashSet());
            play.Stop(); var checkpoint = scene.CaptureCheckpoint(); var oldWorld = world.Identity;
            scene.RestoreCheckpoint(checkpoint, scene.Revision);
            Check(checkpoint.CopyBytes().SequenceEqual(scene.CaptureCheckpoint().CopyBytes()) && world.Identity != oldWorld);
            Check(authoringBytes.SequenceEqual(document.CaptureBytes()));
            passed++; Console.WriteLine($"PASS E3 joint {workload} objects={count}: no full World/Scene snapshots; values/queries/render/codec agree.");
        }
        Console.WriteLine($"E3 joint fixed-step tests: {passed}/15 passed.");
        Console.WriteLine($"E4-E committed Play/query/render/inspection joint cases: {passed}/15 passed.");
    }

    private static void VerifyInspection(RuntimeSceneInstance scene, EntityQuery query, HashSet<Guid> rendered)
    {
        // The independent cold oracle is outside the simulation window. Paginated inspection
        // must not capture that whole snapshot again, even on its first request.
        var snapshot = scene.CaptureSnapshot(); var stamp = scene.GetInspectionStamp();
        long before = Count(scene, "_capturedSnapshots"); var inspected = new List<Guid>();
        for (int offset = 0; offset < snapshot.Objects.Length; offset += 128)
        {
            var result = scene.InspectObjects(stamp, offset, 128);
            Check(result.TotalCount == snapshot.Objects.Length && result.Stamp == stamp && result.Offset == offset);
            Check(result.Stamp.WorldId == scene.World.Identity && result.Stamp.Tick == scene.World.Tick);
            for (int i = 0; i < result.Items.Length; i++)
            {
                var expected = snapshot.Objects[offset + i]; Guid id = result.Items[i].Id; inspected.Add(id);
                Check(id == expected.Id && result.Items[i].Name == expected.Name && rendered.Contains(id));
            }
        }
        foreach (var entity in query) Check(inspected.Contains(scene.World.GetUuid(entity)));
        foreach (var expected in new[] { snapshot.Objects[0], snapshot.Objects[^1] })
        {
            var result = scene.InspectComponents(stamp, expected.Id, limit: 64);
            Check(result.Stamp == stamp && result.ObjectId == expected.Id && result.TotalCount == expected.Components.Length);
            Check(result.Items.Length == expected.Components.Length);
            foreach (var component in expected.Components)
                Check(result.Items.Single(c => c.TypeId == component.TypeId).Data.GetRawText() == component.Data.GetRawText());
        }
        Check(Count(scene, "_capturedSnapshots") == before && scene.GetInspectionStamp() == stamp && scene.World.Tick > 0);
    }
}
