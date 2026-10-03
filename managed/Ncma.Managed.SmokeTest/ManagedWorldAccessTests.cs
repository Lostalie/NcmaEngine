using Ncma;
internal static class ManagedWorldAccessTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (ArgumentException) { return; } catch (InvalidOperationException) { return; }
        throw new Exception("Invalid access was accepted.");
    }
    public static void Run()
    {
        using SceneWorld world = new("Pure managed access");
        var a = world.CreateObject("A"); var b = world.CreateObject("B");
        Reject(() => _ = a.LocalTransform);
        a.LocalTransform = b.LocalTransform = Transform.Identity;
        var one = Transform.Identity; one.Position.X = 11;
        var two = Transform.Identity; two.Position.X = 22;
        Transform[] output = [one, two];
        Reject(() => world.ReadTransforms([a.Reference, default], output));
        Check(output[0].Position.X == 11 && output[1].Position.X == 22, "Failed batch changed caller output.");
        Reject(() => world.WriteTransforms([new(a, one), new(a, two)]));
        Check(a.LocalTransform.Position.X == 0, "Duplicate batch partially applied.");
        var invalid = one; invalid.RotationW = 0;
        Reject(() => world.WriteTransforms([new(a, one), new(b, invalid)]));
        Check(a.LocalTransform.Position.X == 0, "Invalid later record partially applied.");
        world.BeginPhase();
        Reject(() => world.BeginPhase());
        Reject(() => world.CreateObject("Illegal structural mutation"));
        world.WriteTransforms([new(a, one)]); world.WriteTransforms([new(a, two)]);
        world.SendSignal(a, b, 10);
        Check(a.LocalTransform.Position.X == 0, "Pending transform leaked.");
        world.AbortPhase();
        Check(a.LocalTransform.Position.X == 0 && world.ReceiveSignals(b, new GameplaySignal[1]) == 0, "Abort did not discard writes/signals.");
        world.BeginPhase(); world.WriteTransforms([new(a, one)]); world.WriteTransforms([new(a, two)]); world.CommitPhase();
        Check(a.LocalTransform.Position.X == 22, "Last-submission-wins lost.");
        Reject(() => world.CommitPhase());
        Reject(() => world.ReadTransforms(new ObjectReference[4097], new Transform[4097]));
        Reject(() => world.WriteTransforms(new TransformWrite[4097]));
        for (int i = 0; i < 4096; i++) world.SendSignal(a, b, 1, i);
        Reject(() => world.SendSignal(a, b, 1));
        var signals = new GameplaySignal[4096];
        Check(world.ReceiveSignals(b, signals) == 4096 && signals[^1].Value == 4095, "Bounded FIFO signal queue failed.");
        world.SendSignal(a, b, 2);
        a.Destroy();
        Check(world.ReceiveSignals(b, signals) == 0, "Deleted-source signal survived.");
        var old = b.Reference; var uuid = b.PersistentId;
        world.Runtime.RestoreSnapshot(world.Runtime.CaptureSnapshot()); world.Restored();
        var restored = world.FindObject(uuid);
        Check(restored.Id != b.Id, "Restored numeric object ID reused.");
        Reject(() => _ = b.LocalTransform);
        Reject(() => world.ReadTransforms([old], output.AsSpan(0, 1)));
        Console.WriteLine("Managed World access: atomic batches, limits, abort/commit, FIFO and stale references passed.");
    }
}
