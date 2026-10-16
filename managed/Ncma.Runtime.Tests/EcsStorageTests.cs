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
                var values = (Label[])typeof(ComponentPool<Label>).GetField("_values", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pool)!;
                Check(values.All(v => v.Text is null));
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
                var world = new World(components: Registry()); var obj = world.CreateObject("A"); obj.Set(new Label("text", 7));
                int sum = 0;
                for (int i = 0; i < 20000; i++) sum += obj.Get<Label>().Value;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 20000; i++) { if (obj.Has<Label>()) sum += obj.Get<Label>().Value; }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Check(allocated == 0 && sum == 280000);
            }),
            ("pool order does not change authoring order", () => {
                var world = new World(components: Registry()); var a = world.CreateObject("A"); var b = world.CreateObject("B"); var c = world.CreateObject("C");
                a.Set(new Label("a", 1)); b.Set(new Label("b", 2)); c.Set(new Label("c", 3));
                a.Remove<Label>(); b.Destroy(); var d = world.CreateObject("D"); d.Set(new Label("d", 4));
                Check(world.GetObjects().Select(o => o.Name).SequenceEqual(new[] { "A", "C", "D" }));
                Check(c.Get<Label>().Value == 3 && d.Get<Label>().Value == 4 && !a.Has<Label>());
                Check(world.CaptureSnapshot().Objects.Select(o => o.Name).SequenceEqual(new[] { "A", "C", "D" }));
                Reject(() => b.Get<Label>());
            }),
            ("bounded slots can churn past lifetime capacity", () => {
                var world = new World(components: Registry());
                for (int round = 0; round < 2; round++)
                {
                    var objects = Enumerable.Range(0, World.MaxObjects).Select(i => world.CreateObject("A" + i)).ToArray();
                    for (int i = 0; i < objects.Length; i += 2) objects[i].Set(new Label("value", i));
                    Reject(() => world.CreateObject("overflow"));
                    for (int i = 0; i < objects.Length; i++) { Check(objects[i].Has<Label>() == (i % 2 == 0)); objects[i].Destroy(); }
                    Check(world.Count == 0);
                    Reject(() => objects[0].Get<Label>());
                }
                var empty = world.CreateObject("empty"); Check(!empty.Has<Label>() && !empty.Has<TransformData>());
            }),
            ("candidate writes are invisible until commit", () => {
                var world = new World(components: Registry()); var a = world.CreateObject("A"); var b = world.CreateObject("B");
                a.Set(new Label("before", 1)); b.Set(new Label("other", 3));
                world.BeginStep();
                a.Set(new Label("first", 2)); a.Set(new Label("last", 4));
                Check(a.Get<Label>().Value == 1 && b.Get<Label>().Value == 3);
                world.CommitStep(); Check(a.Get<Label>().Value == 4 && b.Get<Label>().Value == 3 && world.Tick == 1);
                world.BeginStep(); b.Set(new Label("abort", 5)); world.AbortStep();
                Check(a.Get<Label>().Value == 4 && b.Get<Label>().Value == 3 && world.Tick == 1);
            }),
            ("candidate delete and slot reuse never leak", () => {
                var world = new World(components: Registry()); var a = world.CreateObject("A"); a.Set(new Label("old", 1));
                Guid fresh = Guid.NewGuid(); string before = world.SerializeSnapshot();
                world.BeginStep(); world.StageDestroy(a.PersistentId); world.StageCreate(fresh, "new");
                world.StageAdd(fresh, new("test.label", 1, world.Components.Encode(new Label("new", 2))));
                Check(a.Get<Label>().Value == 1); Reject(() => world.FindObject(fresh));
                world.AbortStep(); Check(world.SerializeSnapshot() == before && a.Get<Label>().Value == 1);
                world.BeginStep(); world.StageDestroy(a.PersistentId); world.StageCreate(fresh, "new");
                world.CommitStep(); Reject(() => a.Get<Label>()); Check(!world.FindObject(fresh).Has<Label>());
            }),
            ("removed candidate component rejects caught write and poisons step", () => {
                var world = new World(components: Registry()); var a = world.CreateObject("A"); a.Set(new Label("old", 1));
                ulong revision = world.Revision;
                world.BeginStep(); world.StageRemove(a.PersistentId, "test.label");
                Check(a.Has<Label>()); Reject(() => a.Set(new Label("lost", 2))); Reject(() => world.CommitStep());
                world.AbortStep(); Check(a.Get<Label>().Value == 1 && world.Revision == revision && world.Tick == 0);
            }),
            ("restore strings and failed preparation preserve identity and data", () => {
                var world = new World(components: Registry()); var a = world.CreateObject("A"); a.Set(new Label("中文 🌍", 17));
                var snapshot = world.CaptureSnapshot(); Guid identity = world.Identity; ulong revision = world.Revision;
                var bad = snapshot with { Objects = [snapshot.Objects[0] with { Components = [snapshot.Objects[0].Components[0], snapshot.Objects[0].Components[0]] }] };
                Reject(() => world.RestoreSnapshot(bad));
                Check(world.Identity == identity && world.Revision == revision && a.Get<Label>().Value == 17);
                Guid uuid = a.PersistentId; string text = world.SerializeSnapshot();
                world.LoadSnapshot(text); Reject(() => a.Get<Label>());
                Check(world.FindObject(uuid).Get<Label>().Text == "中文 🌍" && world.SerializeSnapshot() == text);
                Check(!text.Contains("slot", StringComparison.OrdinalIgnoreCase));
            }),
            ("late candidate validator failure leaves every pool unchanged", () => {
                var world = new World(components: Registry()); var a = world.CreateObject("A"); a.Set(new Label("old", 1)); a.Set(TransformData.Identity);
                string before = world.SerializeSnapshot(); ulong revision = world.Revision;
                world.BeginStep(); a.Set(new Label("changed", 2)); a.Set(TransformData.Identity with { Position = Vector3.One });
                Reject(() => world.PrepareStep(_ => throw new ArgumentException("late validator")));
                world.AbortStep(); Check(world.SerializeSnapshot() == before && world.Revision == revision && world.Tick == 0);
            }),
            ("write authority still guards typed storage", () => {
                var world = new World(components: Registry()); var a = world.CreateObject("A"); a.Set(new Label("owned", 1));
                using var authority = world.ClaimComponents<Label>([a.PersistentId], freezeMembership: true);
                Reject(() => a.Set(new Label("outside", 2))); Reject(() => a.Remove<Label>()); Reject(a.Destroy);
                world.BeginStep(); Check(world.WriteOwned(authority, a.PersistentId, new Label("owned update", 3)).Value == 3);
                Check(a.Get<Label>().Value == 1); world.CommitStep(); Check(a.Get<Label>().Value == 3);
                world.BeginStep(); Reject(() => world.CommitStep()); world.AbortStep(); Check(world.Tick == 1);
            }),
            ("thread and read-only guards precede typed access", () => {
                var world = new World(components: Registry()); var a = world.CreateObject("A"); a.Set(new Label("old", 1));
                Check(Task.Run(() => { try { a.Get<Label>(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult());
                using (world.ReadOnly()) { Reject(() => a.Set(new Label("new", 2))); Reject(() => a.Remove<Label>()); }
                Check(a.Get<Label>().Value == 1);
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
