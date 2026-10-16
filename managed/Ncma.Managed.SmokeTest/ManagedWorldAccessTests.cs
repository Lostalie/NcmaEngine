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
        Reject(() => world.ReadTransforms([a.Entity, default], output));
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
        Reject(() => world.ReadTransforms(new Ncma.Runtime.Entity[4097], new Transform[4097]));
        Reject(() => world.WriteTransforms(new TransformWrite[4097]));
        for (int i = 0; i < 4096; i++) world.SendSignal(a, b, 1, i);
        Reject(() => world.SendSignal(a, b, 1));
        var signals = new GameplaySignal[4096];
        Check(world.ReceiveSignals(b, signals) == 4096 && signals[^1].Value == 4095, "Bounded FIFO signal queue failed.");
        world.SendSignal(a, b, 2);
        a.Destroy();
        Check(world.ReceiveSignals(b, signals) == 0, "Deleted-source signal survived.");
        var old = b.Entity; var uuid = b.PersistentId;
        world.Runtime.RestoreSnapshot(world.Runtime.CaptureSnapshot()); world.Restored();
        var restored = world.FindObject(uuid);
        Check(restored.Entity != b.Entity && restored.Entity.WorldIdentity != b.Entity.WorldIdentity, "Restored World identity reused.");
        Reject(() => _ = b.LocalTransform);
        Reject(() => world.ReadTransforms([old], output.AsSpan(0, 1)));
        var equivalent = world.FindObject(uuid);
        Check(restored == equivalent && restored.Equals(equivalent) && restored.GetHashCode() == equivalent.GetHashCode(),
            "Lazy facades for the same Entity must be equal.");
        Check(!ReferenceEquals(restored, equivalent) && restored != b, "Facade equality rebound a stale Entity.");
        var priorEntity = restored.Entity;
        world.SendSignal(restored, restored, 101);
        restored.Destroy();
        var reused = world.CreateObject("reused"); reused.LocalTransform = Transform.Identity;
        Check(reused.Entity.Index == priorEntity.Index && reused.Entity.Generation > priorEntity.Generation,
            "Expected same slot with a new generation.");
        Check(world.ReceiveSignals(reused, signals) == 0 && reused != restored, "Stale signal/facade matched a reused slot.");
        Reject(() => world.SendSignal(restored, reused, 102));
        var staleWrite = new TransformWrite(reused, one) { Object = priorEntity };
        Reject(() => world.WriteTransforms([new(reused, two), staleWrite]));
        Check(reused.LocalTransform.Position.X == 0, "Same-slot stale batch allowed prefix publication.");
        Reject(() => world.ReadTransforms([reused.Entity, priorEntity], output));
        Check(output[0].Position.X == 11 && output[1].Position.X == 22, "Same-slot stale read changed caller output.");
        using var otherWorld = new SceneWorld("foreign identity");
        var otherObject = otherWorld.CreateObject("same slot"); otherObject.LocalTransform = Transform.Identity;
        Check(otherObject.Entity.Index == reused.Entity.Index && otherObject != reused, "Different World facades compared equal.");
        Reject(() => world.WriteTransforms([new(reused, two), new(otherObject, one)]));
        Check(reused.LocalTransform.Position.X == 0, "Foreign full identity allowed prefix publication.");
        Check(typeof(SceneWorld).Assembly.GetType("Ncma.ObjectReference") is null &&
            typeof(Ncma.Runtime.World).Assembly.GetType("Ncma.Runtime.ObjectReference") is null &&
            typeof(Ncma.Runtime.World).Assembly.GetType("Ncma.Runtime.GameObject") is null &&
            typeof(GameObject).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .All(f => f.FieldType == typeof(Ncma.Runtime.Entity) || f.FieldType == typeof(SceneWorld)), "Removed references or double wrapper survived.");
        Check(!restored.Equals(null), "A valid facade compared equal to null.");
        var entityOnly = world.Runtime.CreateEntity("Entity first");
        var lazyFacade = world.GetObjects().Single(o => o.Entity == entityOnly);
        Check(!lazyFacade.HasTransform, "Entity creation unexpectedly installed a component.");
        world.Runtime.Set(entityOnly, Ncma.Runtime.TransformData.Identity);
        Check(lazyFacade.HasTransform, "Facade did not observe Entity component storage.");
        lazyFacade.LocalTransform = one;
        Check(world.Runtime.Get<Ncma.Runtime.TransformData>(entityOnly).Position.X == 11, "Facade wrote a second store.");
        world.Runtime.Remove<Ncma.Runtime.TransformData>(entityOnly);
        Check(!lazyFacade.HasTransform, "Entity removal did not reach the facade.");
        world.Runtime.Destroy(entityOnly); Reject(() => _ = lazyFacade.Name);
        Console.WriteLine("Managed World access: atomic batches, limits, abort/commit, FIFO and stale references passed.");
    }
}
