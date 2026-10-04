using System.Numerics;
using System.Reflection;
using System.Text;
using System.Diagnostics;
using System.Text.Json;
using Ncma.Physics;

internal static class PhysicsServiceTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or AggregateException) { return; }
        throw new Exception("Expected physics service rejection.");
    }
    private static PhysicsWorld Native(PhysicsSimulation world) =>
        (PhysicsWorld)typeof(PhysicsSimulation).GetField("_native", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(world)!;
    private static Box2D Dynamic2(float x = 0) => new() { Position = new(x, 4), HalfExtents = new(.5f, .5f), Density = 1, Dynamic = 1 };
    private static Box3D Dynamic3(float x = 0) => new() { Position = new(x, 4, 0), HalfExtents = new(.5f, .5f, .5f), Density = 1, Dynamic = 1 };

    public static void Run(string root, string report)
    {
        using (var disabled = new PhysicsService(Path.Combine(root, "missing"), false))
        {
            Check(!disabled.Inspect().Enabled && disabled.Inspect().Worlds == 0, "Disabled managed service without DLL.");
            Reject(() => disabled.CreateWorld(PhysicsSimulationSettings.TwoD));
        }
        // ABI 1.0 remains a real, independently negotiated low-level contract.
        using (var loader = new Ncma.Interop.PluginLoader())
        {
            loader.Load(root, [new("physics-old", Ncma.Interop.ModuleKind.Physics, "NcmaPhysics.dll", "NcmaPhysics.dll", 1, 0, [])]);
            var module = loader.Modules.Single();
            Check(module.AbiMinor == 0, "1.0 negotiated.");
            using var legacy = PhysicsWorld.Create2D(module);
            legacy.Step(1f / 60);
            Check(legacy.Stats.Sequence == 1 && legacy.Stats.StepP95Milliseconds >= legacy.Stats.StepMedianMilliseconds, "1.0 stats preserved.");
            Reject(() => { _ = legacy.NativeCounters; });
        }
        using (var service = new PhysicsService(root))
        {
            Check(service.Inspect().AbiMinor == 1, "Service requires raw ABI 1.1.");
            Reject(() => service.CreateWorld(PhysicsSimulationSettings.TwoD with { FixedSeconds = float.NaN }));
            Reject(() => service.CreateWorld(PhysicsSimulationSettings.ThreeD with { SubSteps = 4 }));
            using var two = service.CreateWorld(PhysicsSimulationSettings.TwoD);
            using var three = service.CreateWorld(PhysicsSimulationSettings.ThreeD);
            var refs2 = new PhysicsBodyReference[2]; var refs3 = new PhysicsBodyReference[2];
            two.CreateBoxes2D([new() { Position = new(0, -.5f), HalfExtents = new(8, .5f), Density = 1 }, Dynamic2()], refs2);
            three.CreateBoxes3D([new() { Position = new(0, -.5f, 0), HalfExtents = new(8, .5f, 8), Density = 1 }, Dynamic3()], refs3);
            Reject(() => two.Step()); Check(two.Inspect().PublishedSequence == 0, "Paused step rejected.");
            for (int i = 0; i < 180; i++) { two.StepOnce(); three.StepOnce(); }
            var states2 = new BodyState2D[2]; var states3 = new BodyState3D[2];
            Check(two.CopyStates2D(states2) == 2 && three.CopyStates3D(states3) == 2, "Copied snapshots.");
            Check(Math.Abs(states2[1].Position.Y - .5f) < .08f && Math.Abs(states3[1].Position.Y - .5f) < .08f, "Managed reference settling.");
            var view = two.Inspect();
            Check(view.SnapshotValid && view.NativeAvailable && view.Native.LiveWorlds == 2 && view.Profile.Samples == 180 &&
                view.Profile.WindowSamples == 180 && view.Profile.P95Milliseconds >= view.Profile.MedianMilliseconds, "Managed profile and raw counters.");
            states2[1].Position = new(999, 999); two.CopyStates2D(states2);
            Check(states2[1].Position.X != 999, "Output snapshot does not alias service.");
            Check(two.CopyStates2D(states2.AsSpan(0, 1), 1) == 1 && states2[0].Body == refs2[1].Handle, "Paged snapshot.");
            two.Resume(); Reject(() => two.StepOnce()); Check(two.Step() == 181, "Running step.");
            two.Pause(); Check(two.State == PhysicsSimulationState.Paused, "Managed pause.");
            Exception? failure = null; var thread = new Thread(() => { try { two.StepOnce(); } catch (Exception e) { failure = e; } });
            thread.Start(); thread.Join(); Check(failure is InvalidOperationException, "Service owner thread.");
        }
        using (var service = new PhysicsService(root))
        {
            using var world = service.CreateWorld(PhysicsSimulationSettings.TwoD with { Gravity = Vector3.Zero, MaximumBodies = 4 });
            using var other = service.CreateWorld(PhysicsSimulationSettings.TwoD);
            var bodies = new PhysicsBodyReference[3]; var foreign = new PhysicsBodyReference[1];
            world.CreateBoxes2D([Dynamic2(), Dynamic2(2), Dynamic2(4) with { Dynamic = 0 }], bodies);
            other.CreateBoxes2D([Dynamic2()], foreign);
            world.StageVelocities2D([new(bodies[0], new(1, 0))]);
            Reject(() => world.StageVelocities2D([new(bodies[1], new(3, 0)), new(foreign[0], new(3, 0))]));
            Reject(() => world.StageVelocities2D([new(bodies[1], new(3, 0)), new(bodies[1], new(3, 0))]));
            Reject(() => world.StageVelocities2D([new(bodies[2], new(3, 0))]));
            Reject(() => world.StageVelocities2D([new(bodies[1], new(float.NaN, 0))]));
            Reject(() => world.StageVelocities2D([new(bodies[0] with { ServiceId = Guid.NewGuid() }, new(3, 0))]));
            Check(world.Inspect().PendingVelocities == 1, "Rejected stages do not partially enqueue.");
            world.StageVelocities2D([new(bodies[0], new(4, 0))]); world.StepOnce();
            var states = new BodyState2D[3]; world.CopyStates2D(states);
            Check(states[0].Velocity.X == 4 && states[1].Velocity.X == 0 && world.Inspect().PendingVelocities == 0, "Last submitted batch wins, one native flush.");
            world.StageVelocities2D([new(bodies[1], new(8, 0))]);
            Reject(() => world.DestroyBodies([bodies[0], foreign[0]])); Reject(() => world.DestroyBodies([bodies[0], bodies[0]]));
            Check(world.BodyCount == 3, "Destroy preflight.");
            world.DestroyBodies([bodies[0]]);
            Check(world.Inspect().PendingVelocities == 1, "Dense swap preserves another body's queue.");
            world.DestroyBodies([bodies[1]]); Check(world.Inspect().PendingVelocities == 0, "Destroyed queued target removed.");
            var replacement = new PhysicsBodyReference[1]; world.CreateBoxes2D([Dynamic2()], replacement);
            Check(replacement[0].Handle != bodies[0].Handle, "No stale handle reuse.");
            Reject(() => world.StageVelocities2D([new(bodies[0], new(3, 0))]));
            var copied = new PhysicsBodyReference[4]; Check(world.CopyBodies(copied) == 2, "Copied dense resource references.");
            world.StageVelocities2D([new(replacement[0], new(9, 0))]); world.CancelPendingVelocities();
            world.StepOnce(); world.CopyStates2D(states); Check(states[1].Velocity.X == 0, "Cancel never reaches solver.");
            // Errors are copied, bounded and scoped; rejected create batch preserves resources.
            for (int i = 0; i < 40; i++) Reject(() => world.CreateBoxes2D([Dynamic2() with { Density = 0 }], replacement));
            var diagnostics = service.CopyDiagnostics();
            Check(diagnostics.Length == 32 && service.Inspect().DroppedDiagnostics == 8 &&
                diagnostics.All(d => d.ServiceId == service.ServiceId && d.WorldId == world.WorldId && Encoding.UTF8.GetByteCount(d.Message) <= 512), "Bounded correlated diagnostics.");
        }
        using (var service = new PhysicsService(root))
        {
            using var world = service.CreateWorld(PhysicsSimulationSettings.TwoD);
            world.StepOnce();
            // White-box lifecycle invalidation, no production debug API or simulated actual solver failure.
            Native(world).Dispose();
            Reject(() => world.StepOnce());
            var view = world.Inspect();
            Check(view.State == PhysicsSimulationState.Faulted && !view.SnapshotValid && !view.NativeAvailable && view.PublishedSequence == 1, "Fail-stop publication.");
            Reject(() => world.Resume()); Reject(() => world.CopyStates2D(new BodyState2D[1]));
        }
        // Failed native close retains ownership and module lease; explicit corrected retry can close.
        using (var service = new PhysicsService(root))
        {
            var world = service.CreateWorld(PhysicsSimulationSettings.ThreeD);
            var native = Native(world); var handle = typeof(PhysicsWorld).GetField("_handle", BindingFlags.NonPublic | BindingFlags.Instance)!;
            ulong valid = (ulong)handle.GetValue(native)!; handle.SetValue(native, ulong.MaxValue);
            Reject(service.Dispose); Check(service.Inspect().Closing && service.Inspect().Worlds == 1, "Failed close retained.");
            Reject(() => service.CreateWorld(PhysicsSimulationSettings.TwoD));
            Reject(() => world.StepOnce());
            handle.SetValue(native, valid); service.Dispose(); service.Dispose();
        }
        var measurements = new List<object>();
        foreach (var dimension in new[] { PhysicsDimension.Two, PhysicsDimension.Three })
            foreach (int count in new[] { 0, 64, 1024, 4096 }) measurements.Add(Measure(root, dimension, count));
        File.WriteAllText(Path.Combine(report, "physics-service-results.json"), JsonSerializer.Serialize(measurements, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
        for (int cycle = 0; cycle < 32; cycle++)
        {
            using var service = new PhysicsService(root);
            var world = service.CreateWorld(PhysicsSimulationSettings.ThreeD);
            var reference = new PhysicsBodyReference[1]; world.CreateBoxes3D([Dynamic3()], reference);
            world.StepOnce(); world.Dispose(); world.Dispose();
            Check(service.Inspect().Worlds == 0, "32 managed service lifecycles drained.");
        }
        using (var service = new PhysicsService(root))
        {
            var worlds = new PhysicsSimulation[16];
            for (int i = 0; i < worlds.Length; i++) worlds[i] = service.CreateWorld(PhysicsSimulationSettings.TwoD with { MaximumBodies = 1 });
            Reject(() => service.CreateWorld(PhysicsSimulationSettings.TwoD));
            foreach (var world in worlds) world.Dispose();
            Check(service.Inspect().Worlds == 0, "Managed world budget.");
        }
        Console.WriteLine("Physics service: policy, batches, copied pages, profile, failure/close retention, 8 workloads and 32 lifecycles passed.");
    }
    private static object Measure(string root, PhysicsDimension dimension, int count)
    {
        using var service = new PhysicsService(root);
        using var world = service.CreateWorld((dimension == PhysicsDimension.Two ? PhysicsSimulationSettings.TwoD : PhysicsSimulationSettings.ThreeD) with { Gravity = Vector3.Zero });
        var references = new PhysicsBodyReference[count];
        var boxes2 = new Box2D[count]; var boxes3 = new Box3D[count];
        var velocity2 = new PhysicsVelocity2D[count]; var velocity3 = new PhysicsVelocity3D[count];
        var states2 = new BodyState2D[count]; var states3 = new BodyState3D[count];
        for (int i = 0; i < count; i++)
        {
            boxes2[i] = Dynamic2() with { Position = new(i % 64 * 2, i / 64 * 2), HalfExtents = new(.25f, .25f) };
            boxes3[i] = Dynamic3() with { Position = new(i % 64 * 2, i / 64 * 2, 0), HalfExtents = new(.25f, .25f, .25f) };
        }
        if (dimension == PhysicsDimension.Two) world.CreateBoxes2D(boxes2, references); else world.CreateBoxes3D(boxes3, references);
        for (int i = 0; i < count; i++) { velocity2[i] = new(references[i], new(.1f, 0)); velocity3[i] = new(references[i], new(.1f, 0, 0)); }
        // Warm the entire measured path, not Step alone: staging/sort/flush/copy
        // can initialize runtime helpers on their first call. The zero-allocation
        // assertion below remains exact, and the 4 + 32 sample count is unchanged.
        for (int i = 0; i < 4; i++)
        {
            if (dimension == PhysicsDimension.Two) world.StageVelocities2D(velocity2); else world.StageVelocities3D(velocity3);
            world.StepOnce();
            if (dimension == PhysicsDimension.Two) world.CopyStates2D(states2); else world.CopyStates3D(states3);
        }
        long[] stageAllocations = new long[32], stepAllocations = new long[32], copyAllocations = new long[32];
        long started = Stopwatch.GetTimestamp(), bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 32; i++)
        {
            long phaseStart = GC.GetAllocatedBytesForCurrentThread();
            if (dimension == PhysicsDimension.Two) world.StageVelocities2D(velocity2); else world.StageVelocities3D(velocity3);
            long afterStage = GC.GetAllocatedBytesForCurrentThread(); stageAllocations[i] = afterStage - phaseStart;
            world.StepOnce();
            long afterStep = GC.GetAllocatedBytesForCurrentThread(); stepAllocations[i] = afterStep - afterStage;
            if (dimension == PhysicsDimension.Two) world.CopyStates2D(states2); else world.CopyStates3D(states3);
            copyAllocations[i] = GC.GetAllocatedBytesForCurrentThread() - afterStep;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
        double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var inspection = world.Inspect();
        Check(allocated == 0 && inspection.Native.LiveBodies == (ulong)count && inspection.Profile.Samples == 36,
            $"Service hot path: dimension={dimension}, bodies={count}, allocatedBytes={allocated}, liveBodies={inspection.Native.LiveBodies}, samples={inspection.Profile.Samples} (expected 0/count/36); stage={stageAllocations.Sum()}, step={stepAllocations.Sum()}, copy={copyAllocations.Sum()}.");
        return new { dimension, count, elapsedMilliseconds = elapsed, managedThreadBytes = allocated, inspection };
    }
}
