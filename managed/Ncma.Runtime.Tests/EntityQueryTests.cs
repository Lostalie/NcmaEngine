using System.Reflection;
using System.Text.Json;
using Ncma.Runtime;

internal static class EntityQueryTests
{
    private readonly record struct Label(string Text) : IComponent;
    private readonly record struct Flag(bool Enabled) : IComponent;
    private readonly record struct Unknown(int Value) : IComponent;
    private static void Check(bool valid) { if (!valid) throw new Exception("Query assertion failed."); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return; }
        throw new Exception("Invalid query operation accepted.");
    }
    private static World NewWorld()
    {
        var registry = ComponentRegistry.CreateDefault();
        registry.Register<Label>("test.label", 1, """{"type":"object","required":["text"],"properties":{"text":{"type":"string"}}}""", value => value);
        registry.Register<Flag>("test.flag", 1, """{"type":"object","required":["enabled"],"properties":{"enabled":{"type":"boolean"}}}""", value => value);
        return new World(components: registry);
    }
    private static Entity[] Read(EntityQuery query)
    {
        var result = new Entity[query.Count];
        Check(query.CopyTo(result) == result.Length);
        return result;
    }
    private static ComponentSnapshot Transform(World world) => new("ncma.transform", 1, world.Components.Encode(TransformData.Identity));
    private static void Invalidated(ref EntityQuery.Enumerator iterator)
    {
        bool moveRejected = false, currentRejected = false;
        try { iterator.MoveNext(); } catch (InvalidOperationException) { moveRejected = true; }
        try { _ = iterator.Current; } catch (InvalidOperationException) { currentRejected = true; }
        Check(moveRejected && currentRejected);
    }
    internal static int Run()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("All/None and typed intersections include only committed members", () => {
                var world = NewWorld(); var a = world.CreateEntity("empty"); var b = world.CreateEntity("transform");
                var c = world.CreateEntity("both"); var d = world.CreateEntity("three");
                world.Set(b, TransformData.Identity); world.Set(c, TransformData.Identity); world.Set(c, new Label("C"));
                world.Set(d, TransformData.Identity); world.Set(d, new Label("D")); world.Set(d, new Flag(true));
                Check(Read(world.CreateQuery([], [])).SequenceEqual(new[] { a, b, c, d }));
                Check(Read(world.Query<TransformData>()).SequenceEqual(new[] { b, c, d }));
                Check(Read(world.Query<TransformData, Label>()).SequenceEqual(new[] { c, d }));
                Check(Read(world.Query<TransformData, Label, Flag>()).SequenceEqual(new[] { d }));
                Check(Read(world.CreateQuery([typeof(TransformData)], [typeof(Label)])).SequenceEqual(new[] { b }));
                Check(Read(world.CreateQuery([], [typeof(TransformData)])).SequenceEqual(new[] { a }));
            }),
            ("missing pools and empty intersections return empty", () => {
                var world = NewWorld(); var a = world.CreateEntity("A");
                Check(world.Query<Label>().Count == 0);
                var query = world.Query<TransformData, Label>(); world.Set(a, new Label("A")); Check(query.Count == 0);
                var b = world.CreateEntity("B"); world.Set(b, TransformData.Identity); Check(query.Count == 0);
            }),
            ("swap delete and slot reuse preserve creation order", () => {
                var world = NewWorld(); var query = world.Query<Label>(); var a = world.CreateEntity("A");
                var b = world.CreateEntity("B"); var c = world.CreateEntity("C");
                world.Set(c, new Label("C")); world.Set(a, new Label("A")); world.Set(b, new Label("B"));
                Check(Read(query).SequenceEqual(new[] { a, b, c }));
                world.Remove<Label>(a); Check(Read(query).SequenceEqual(new[] { b, c }));
                world.Set(a, new Label("again")); Check(Read(query).SequenceEqual(new[] { a, b, c }));
                world.Destroy(a); var d = world.CreateEntity("D"); world.Set(d, new Label("D"));
                Check(d.Index == a.Index && Read(query).SequenceEqual(new[] { b, c, d }));
            }),
            ("filters are copied; duplicate/null/unregistered/overlap/budgets rejected", () => {
                var world = NewWorld(); var a = world.CreateEntity("A"); world.Set(a, new Label("A"));
                Type[] all = [typeof(Label)]; var query = world.CreateQuery(all, []); all[0] = typeof(Flag); Check(query.Count == 1);
                Reject(() => world.CreateQuery([null!], [])); Reject(() => world.Query<Unknown>());
                Reject(() => world.Query<Label, Label>()); Reject(() => world.CreateQuery([typeof(Label)], [typeof(Label)]));
                Reject(() => world.CreateQuery([], [typeof(Label), typeof(Label)]));
                Reject(() => world.CreateQuery(new Type[EntityQuery.MaxTerms + 1], []));
                Reject(() => world.CreateQuery([], [], 0)); Reject(() => world.CreateQuery([], [], World.MaxObjects + 1));
            }),
            ("bounded results and output have no partial publication", () => {
                var world = NewWorld(); var a = world.CreateEntity("A"); var query = world.CreateQuery([], [], 1);
                Check(query.Count == 1); var b = world.CreateEntity("B"); Entity[] output = [a, a];
                Reject(() => query.CopyTo(output)); Check(output.SequenceEqual(new[] { a, a })); Reject(() => _ = query.Count);
                var full = world.CreateQuery([], []); Entity[] shortOutput = [b];
                Reject(() => full.CopyTo(shortOutput)); Check(shortOutput[0] == b);
                world.Destroy(b); Check(Read(query).SequenceEqual(new[] { a }));
                output = [default, a]; Check(query.CopyTo(output) == 1 && output[0] == a && output[1] == a);
            }),
            ("versions separate topology from values/name/tick", () => {
                var world = NewWorld(); var a = world.CreateEntity("A"); world.Set(a, new Label("one"));
                ulong version = world.MembershipVersion, revision = world.Revision; var query = world.Query<Label>();
                var iterator = query.GetEnumerator(); Check(iterator.MoveNext());
                world.Set(a, new Label("two")); world.Rename(a, "renamed");
                world.BeginStep(); world.Set(a, new Label("three")); Check(world.Get<Label>(a).Text == "two");
                world.CommitStep(); Check(iterator.Current == a && world.Get<Label>(a).Text == "three");
                world.BeginStep(); world.CommitStep(); Check(world.MembershipVersion == version && world.Revision > revision);
                Check(!iterator.MoveNext());
            }),
            ("immediate add/remove/create/destroy invalidate iteration", () => {
                var world = NewWorld(); var a = world.CreateEntity("A"); var query = world.CreateQuery([], []);
                var iterator = query.GetEnumerator(); Check(iterator.MoveNext()); world.Set(a, new Label("one")); Invalidated(ref iterator);
                iterator = query.GetEnumerator(); world.Remove<Label>(a); Invalidated(ref iterator);
                iterator = query.GetEnumerator(); var b = world.CreateEntity("B"); Invalidated(ref iterator);
                iterator = query.GetEnumerator(); world.Destroy(b); Invalidated(ref iterator);
                Check(Read(query).SequenceEqual(new[] { a }));
            }),
            ("pending create/delete visible only after successful commit", () => {
                var world = NewWorld(); var a = world.CreateEntity("A"); var query = world.CreateQuery([], []);
                ulong version = world.MembershipVersion; var iterator = query.GetEnumerator(); Guid uuid = Guid.NewGuid();
                world.BeginStep(); world.StageDestroy(world.GetUuid(a)); world.StageCreate(uuid, "B"); var pending = world.CandidateEntity(uuid);
                Check(Read(query).SequenceEqual(new[] { a }) && world.MembershipVersion == version);
                Check(iterator.MoveNext() && iterator.Current == a); Reject(() => world.Resolve(pending));
                world.CommitStep(); Invalidated(ref iterator); Check(Read(query).SequenceEqual(new[] { pending }));
            }),
            ("pending component add/remove abort leaves cache and iterator valid", () => {
                var world = NewWorld(); var a = world.CreateEntity("A"); world.Set(a, TransformData.Identity);
                var b = world.CreateEntity("B"); var query = world.Query<TransformData>(); var iterator = query.GetEnumerator();
                ulong version = world.MembershipVersion;
                world.BeginStep(); world.StageRemove(world.GetUuid(a), "ncma.transform"); world.StageAdd(world.GetUuid(b), Transform(world));
                Check(Read(query).SequenceEqual(new[] { a })); world.AbortStep();
                Check(world.MembershipVersion == version && iterator.MoveNext() && iterator.Current == a);
                world.BeginStep(); world.StageRemove(world.GetUuid(a), "ncma.transform"); world.StageAdd(world.GetUuid(b), Transform(world));
                world.CommitStep(); Invalidated(ref iterator); Check(Read(query).SequenceEqual(new[] { b }));
            }),
            ("failed prepared commit does not publish membership", () => {
                var world = NewWorld(); var a = world.CreateEntity("A"); var query = world.CreateQuery([], []);
                var iterator = query.GetEnumerator(); ulong version = world.MembershipVersion;
                world.BeginStep(); world.StageCreate(Guid.NewGuid(), "B");
                Reject(() => world.PrepareStep(_ => throw new ArgumentException("reject candidate"))); world.AbortStep();
                Check(world.MembershipVersion == version && query.Count == 1 && iterator.MoveNext() && iterator.Current == a);
            }),
            ("restore invalidates plans and iterators; failed restore does not", () => {
                var world = NewWorld(); var a = world.CreateEntity("A"); var query = world.CreateQuery([], []);
                var iterator = query.GetEnumerator(); var snapshot = world.CaptureSnapshot();
                Reject(() => world.RestoreSnapshot(snapshot with { Version = 42 })); Check(query.Count == 1);
                world.RestoreSnapshot(snapshot); Reject(() => _ = query.Count); Invalidated(ref iterator);
                Check(Read(world.CreateQuery([], [])).Single() != a);
            }),
            ("owner thread checks on plan creation/count/copy/enumeration and versions", () => {
                var world = NewWorld(); world.CreateEntity("A"); var query = world.CreateQuery([], []); Check(query.Count == 1);
                foreach (Action action in new Action[] { () => world.Query<Label>(), () => _ = query.Count,
                    () => query.CopyTo(new Entity[1]), () => { var e = query.GetEnumerator(); e.MoveNext(); }, () => _ = world.MembershipVersion })
                    Check(Task.Run(() => { try { action(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult());
            }),
            ("read-only query cannot bypass write authority or membership freeze", () => {
                var world = NewWorld(); var a = world.CreateEntity("A"); world.Set(a, TransformData.Identity);
                var query = world.Query<TransformData>(); using var authority = world.ClaimComponents<TransformData>([world.GetUuid(a)], false, true);
                using (world.ReadOnly()) { foreach (var entity in query) Reject(() => world.Set(entity, TransformData.Identity)); }
                foreach (var entity in query) { Reject(() => world.Set(entity, TransformData.Identity)); Reject(() => world.Remove<TransformData>(entity)); }
                Check(query.Count == 1);
            }),
            ("default/before-first/after-end Current rejects; nested iteration independent", () => {
                var world = NewWorld(); world.CreateEntity("A"); world.CreateEntity("B"); var query = world.CreateQuery([], []);
                EntityQuery.Enumerator empty = default; Invalidated(ref empty);
                var iterator = query.GetEnumerator(); bool rejected = false;
                try { _ = iterator.Current; } catch (InvalidOperationException) { rejected = true; } Check(rejected);
                int pairs = 0; foreach (var a in query) foreach (var b in query) { Check(a != default && b != default); ++pairs; } Check(pairs == 4);
                while (iterator.MoveNext()) { } Check(!iterator.MoveNext()); rejected = false;
                try { _ = iterator.Current; } catch (InvalidOperationException) { rejected = true; } Check(rejected);
            }),
            ("capacity 4096 warm enumeration/get/copy allocate zero bytes", () => {
                var world = NewWorld(); for (int i = 0; i < World.MaxObjects; ++i) world.Set(world.CreateEntity("item"), new Label("value"));
                var query = world.Query<Label>(); var copy = new Entity[World.MaxObjects]; int count = 0;
                for (int round = 0; round < 20; ++round) { foreach (var entity in query) count += world.Get<Label>(entity).Text.Length; query.CopyTo(copy); }
                long before = GC.GetAllocatedBytesForCurrentThread(); count = 0;
                for (int round = 0; round < 20; ++round) { foreach (var entity in query) count += world.Get<Label>(entity).Text.Length; query.CopyTo(copy); }
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Check(count == 20 * World.MaxObjects * 5 && bytes == 0);
                Console.WriteLine($"Query warm loop: entities={World.MaxObjects}, rounds=20, allocated={bytes} bytes (not full-frame performance).");
            }),
            ("deterministic mutation sequence matches independent creation-list oracle", () => {
                var world = NewWorld(); var query = world.CreateQuery([typeof(Label)], [typeof(Flag)]);
                var order = new List<Entity>(); var labels = new HashSet<Entity>(); var flags = new HashSet<Entity>(); var random = new Random(23017);
                for (int step = 0; step < 600; ++step)
                {
                    if (order.Count == 0 || random.Next(5) == 0) order.Add(world.CreateEntity("item"));
                    var entity = order[random.Next(order.Count)];
                    switch (random.Next(5))
                    {
                        case 0: world.Set(entity, new Label("value")); labels.Add(entity); break;
                        case 1: world.Remove<Label>(entity); labels.Remove(entity); break;
                        case 2: world.Set(entity, new Flag(true)); flags.Add(entity); break;
                        case 3: world.Remove<Flag>(entity); flags.Remove(entity); break;
                        case 4: world.Destroy(entity); order.Remove(entity); labels.Remove(entity); flags.Remove(entity); break;
                    }
                    Check(Read(query).SequenceEqual(order.Where(e => labels.Contains(e) && !flags.Contains(e))));
                }
            }),
            ("revision overflow cannot change membership or invalidate iterator", () => {
                var world = NewWorld(); var a = world.CreateEntity("A"); var query = world.CreateQuery([], []);
                var iterator = query.GetEnumerator(); ulong version = world.MembershipVersion;
                typeof(World).GetField("_revision", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(world, ulong.MaxValue);
                bool overflow = false; try { world.Destroy(a); } catch (OverflowException) { overflow = true; }
                Check(overflow && world.MembershipVersion == version && iterator.MoveNext() && iterator.Current == a);
            }),
        };
        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Run(); Console.WriteLine("PASS Query " + test.Name); }
            catch (Exception error) { ++failed; Console.Error.WriteLine("FAIL Query " + test.Name + ": " + error); }
        }
        Console.WriteLine($"E2-A queries: {cases.Length - failed}/{cases.Length} passed.");
        return failed;
    }
}
