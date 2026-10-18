using System.Numerics;
using System.Reflection;
using Ncma.Runtime;

internal static class EcsStorageTests
{
    private readonly record struct Label(string Text, int Value) : IComponent;
    private static ComponentRegistry Registry()
    {
        var registry = ComponentRegistry.CreateDefault();
        registry.Register<Label>("test.label", 1, """
            {"type":"object","required":["text","value"],"properties":{"text":{"type":"string"},"value":{"type":"integer"}}}
            """, v => v.Value >= 0 ? v : throw new ArgumentException("Negative label value."));
        return registry;
    }
    private static void Check(bool value) { if (!value) throw new Exception("E1 storage assertion failed."); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; }
        throw new Exception("Expected storage rejection.");
    }

    internal static int Run()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("dense swap-delete and string clearing", () => {
                var pool = new ComponentPool<Label>();
                pool.Set(0, new("zero", 0)); pool.Set(9, new("nine", 9)); pool.Set(4095, new("last", 4095));
                pool.Remove(9);
                Check(!pool.Has(9) && pool.Get(4095).Value == 4095 && pool.Get(0).Text == "zero");
                pool.Remove(4095); pool.Remove(0); pool.Remove(0);
                Check(!pool.Has(0) && !pool.Has(4095));
                var values = (PagedValues<Label>)typeof(ComponentPool<Label>).GetField("_values", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pool)!;
                Check(Enumerable.Range(0, World.MaxObjects).All(i => values.Read(i).Text is null));
                pool.Set(9, new("reused", 42)); Check(pool.Get(9).Value == 42);
                Reject(() => pool.Get(4096)); Reject(() => pool.Set(-1, default)); Reject(() => pool.Set(4096, default));
            }),
            ("sparse/dense randomized differential", () => {
                var random = new Random(671023);
                var pool = new ComponentPool<Label>(); var expected = new Dictionary<int, Label>();
                for (int i = 0; i < 30000; i++)
                {
                    int slot = random.Next(World.MaxObjects);
                    if (random.Next(3) == 0) { pool.Remove(slot); expected.Remove(slot); }
                    else { var value = new Label("v" + i, i); pool.Set(slot, value); expected[slot] = value; }
                    if (i % 127 == 0)
                        for (int s = 0; s < World.MaxObjects; s++)
                        { Check(pool.Has(s) == expected.ContainsKey(s)); if (expected.TryGetValue(s, out var v)) Check(pool.Get(s) == v); }
                }
            }),
            ("typed pool warmed reads/writes allocate zero", () => {
                var pool = new ComponentPool<Label>(); pool.Set(1, new("constant", 1));
                for (int i = 0; i < 20000; i++) pool.Set(1, pool.Get(1));
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 20000; i++) pool.Set(1, pool.Get(1));
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Check(allocated == 0); // Storage only: public validation/serialization is deliberately excluded.
            }),
            ("fork is isolated in both directions", () => {
                var source = new ComponentStorage(); source.Set(1, new Label("source", 1)); source.Set(1, TransformData.Identity);
                var candidate = source.Fork();
                source.Set(1, new Label("parent changed", 2));
                Check(candidate.Get<Label>(1).Value == 1);
                candidate.Set(1, TransformData.Identity with { Position = Vector3.One });
                candidate.Remove(1, typeof(Label));
                Check(source.Get<Label>(1).Value == 2 && source.Get<TransformData>(1).Position == Vector3.Zero);
                var nested = candidate.Fork(); candidate.RemoveAll(1);
                Check(nested.Get<TransformData>(1).Position == Vector3.One && candidate.Count(1) == 0);
            }),
            ("public committed reads allocate zero after warmup", () => {
                var world = new World(components: Registry()); global::Ncma.Runtime.Entity obj = world.CreateEntity("A"); world.Set(obj, new Label("text", 7));
                int sum = 0;
                for (int i = 0; i < 20000; i++) sum += world.Get<Label>(obj).Value;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 20000; i++) { if (world.Has<Label>(obj)) sum += world.Get<Label>(obj).Value; }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Check(allocated == 0 && sum == 280000);
            }),
            ("pool order does not change authoring order", () => {
                var world = new World(components: Registry()); global::Ncma.Runtime.Entity a = world.CreateEntity("A"); global::Ncma.Runtime.Entity b = world.CreateEntity("B"); global::Ncma.Runtime.Entity c = world.CreateEntity("C");
                world.Set(a, new Label("a", 1)); world.Set(b, new Label("b", 2)); world.Set(c, new Label("c", 3));
                world.Remove<Label>(a); world.Destroy(b); global::Ncma.Runtime.Entity d = world.CreateEntity("D"); world.Set(d, new Label("d", 4));
                Check(world.GetEntities().Select(o => world.GetName(o)).SequenceEqual(new[] { "A", "C", "D" }));
                Check(world.Get<Label>(c).Value == 3 && world.Get<Label>(d).Value == 4 && !world.Has<Label>(a));
                Check(world.CaptureSnapshot().Objects.Select(o => o.Name).SequenceEqual(new[] { "A", "C", "D" }));
                Reject(() => world.Get<Label>(b));
            }),
            ("bounded slots can churn past lifetime capacity", () => {
                var world = new World(components: Registry());
                for (int round = 0; round < 2; round++)
                {
                    var objects = Enumerable.Range(0, World.MaxObjects).Select(i => world.CreateEntity("A" + i)).ToArray();
                    for (int i = 0; i < objects.Length; i += 2) world.Set(objects[i], new Label("value", i));
                    Reject(() => world.CreateEntity("overflow"));
                    for (int i = 0; i < objects.Length; i++) { Check(world.Has<Label>(objects[i]) == (i % 2 == 0)); world.Destroy(objects[i]); }
                    Check(world.Count == 0);
                    Reject(() => world.Get<Label>(objects[0]));
                }
                global::Ncma.Runtime.Entity empty = world.CreateEntity("empty"); Check(!world.Has<Label>(empty) && !world.Has<TransformData>(empty));
            }),
            ("candidate writes are invisible until commit", () => {
                var world = new World(components: Registry()); global::Ncma.Runtime.Entity a = world.CreateEntity("A"); global::Ncma.Runtime.Entity b = world.CreateEntity("B");
                world.Set(a, new Label("before", 1)); world.Set(b, new Label("other", 3));
                world.BeginStep();
                world.Set(a, new Label("first", 2)); world.Set(a, new Label("last", 4));
                Check(world.Get<Label>(a).Value == 1 && world.Get<Label>(b).Value == 3);
                world.CommitStep(); Check(world.Get<Label>(a).Value == 4 && world.Get<Label>(b).Value == 3 && world.Tick == 1);
                world.BeginStep(); world.Set(b, new Label("abort", 5)); world.AbortStep();
                Check(world.Get<Label>(a).Value == 4 && world.Get<Label>(b).Value == 3 && world.Tick == 1);
            }),
            ("candidate delete and slot reuse never leak", () => {
                var world = new World(components: Registry()); global::Ncma.Runtime.Entity a = world.CreateEntity("A"); world.Set(a, new Label("old", 1));
                Guid fresh = Guid.NewGuid(); string before = world.SerializeSnapshot();
                world.BeginStep(); world.StageDestroy(world.GetUuid(a)); world.StageCreate(fresh, "new");
                world.StageAdd(fresh, new("test.label", 1, world.Components.Encode(new Label("new", 2))));
                Check(world.Get<Label>(a).Value == 1); Reject(() => world.FindEntity(fresh));
                world.AbortStep(); Check(world.SerializeSnapshot() == before && world.Get<Label>(a).Value == 1);
                world.BeginStep(); world.StageDestroy(world.GetUuid(a)); world.StageCreate(fresh, "new");
                world.CommitStep(); Reject(() => world.Get<Label>(a)); Check(!world.Has<Label>(world.FindEntity(fresh)));
            }),
            ("removed candidate component rejects caught write and poisons step", () => {
                var world = new World(components: Registry()); global::Ncma.Runtime.Entity a = world.CreateEntity("A"); world.Set(a, new Label("old", 1));
                ulong revision = world.Revision;
                world.BeginStep(); world.StageRemove(world.GetUuid(a), "test.label");
                Check(world.Has<Label>(a)); Reject(() => world.Set(a, new Label("lost", 2))); Reject(() => world.CommitStep());
                world.AbortStep(); Check(world.Get<Label>(a).Value == 1 && world.Revision == revision && world.Tick == 0);
            }),
            ("restore strings and failed preparation preserve identity and data", () => {
                var world = new World(components: Registry()); global::Ncma.Runtime.Entity a = world.CreateEntity("A"); world.Set(a, new Label("中文 🌍", 17));
                var snapshot = world.CaptureSnapshot(); Guid identity = world.Identity; ulong revision = world.Revision;
                var bad = snapshot with { Objects = [snapshot.Objects[0] with { Components = [snapshot.Objects[0].Components[0], snapshot.Objects[0].Components[0]] }] };
                Reject(() => world.RestoreSnapshot(bad));
                Check(world.Identity == identity && world.Revision == revision && world.Get<Label>(a).Value == 17);
                Guid uuid = world.GetUuid(a); string text = world.SerializeSnapshot();
                world.LoadSnapshot(text); Reject(() => world.Get<Label>(a));
                Check(world.Get<Label>(world.FindEntity(uuid)).Text == "中文 🌍" && world.SerializeSnapshot() == text);
                Check(!text.Contains("slot", StringComparison.OrdinalIgnoreCase));
            }),
            ("late candidate validator failure leaves every pool unchanged", () => {
                var world = new World(components: Registry()); global::Ncma.Runtime.Entity a = world.CreateEntity("A"); world.Set(a, new Label("old", 1)); world.Set(a, TransformData.Identity);
                string before = world.SerializeSnapshot(); ulong revision = world.Revision;
                world.BeginStep(); world.Set(a, new Label("changed", 2)); world.Set(a, TransformData.Identity with { Position = Vector3.One });
                Reject(() => world.PrepareStep(_ => throw new ArgumentException("late validator")));
                world.AbortStep(); Check(world.SerializeSnapshot() == before && world.Revision == revision && world.Tick == 0);
            }),
            ("write authority still guards typed storage", () => {
                var world = new World(components: Registry()); global::Ncma.Runtime.Entity a = world.CreateEntity("A"); world.Set(a, new Label("owned", 1));
                using var authority = world.ClaimComponents<Label>([world.GetUuid(a)], freezeMembership: true);
                Reject(() => world.Set(a, new Label("outside", 2))); Reject(() => world.Remove<Label>(a)); Reject(() => world.Destroy(a));
                world.BeginStep(); Check(world.WriteOwned(authority, world.GetUuid(a), new Label("owned update", 3)).Value == 3);
                Check(world.Get<Label>(a).Value == 1); world.CommitStep(); Check(world.Get<Label>(a).Value == 3);
                world.BeginStep(); Reject(() => world.CommitStep()); world.AbortStep(); Check(world.Tick == 1);
            }),
            ("thread and read-only guards precede typed access", () => {
                var world = new World(components: Registry()); global::Ncma.Runtime.Entity a = world.CreateEntity("A"); world.Set(a, new Label("old", 1));
                Check(Task.Run(() => { try { world.Get<Label>(a); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult());
                using (world.ReadOnly()) { Reject(() => world.Set(a, new Label("new", 2))); Reject(() => world.Remove<Label>(a)); }
                Check(world.Get<Label>(a).Value == 1);
            }),
        };
        int failures = 0;
        foreach (var test in cases)
        {
            try { test.Run(); Console.WriteLine("PASS E1-A " + test.Name); }
            catch (Exception e) { failures++; Console.Error.WriteLine("FAIL E1-A " + test.Name + ": " + e); }
        }
        Console.WriteLine($"E1-A typed storage: {cases.Length - failures}/{cases.Length} passed.");
        return failures;
    }
}
