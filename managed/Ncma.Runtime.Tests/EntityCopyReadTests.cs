using System.Runtime.CompilerServices;
using Ncma.Runtime;

internal static class EntityCopyReadTests
{
    private static void Check(bool condition) { if (!condition) throw new Exception("Entity copy read assertion failed."); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return; }
        throw new Exception("Invalid copied read operation accepted.");
    }
    private static void Same(World world, params Entity[] expected) => Check(world.GetEntities().SequenceEqual(expected));
    internal static int Run()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("copies are detached and retain creation order across slot reuse", () => {
                var world = new World(); Check(world.GetEntities().Count == 0);
                var a = world.CreateEntity("A"); var b = world.CreateEntity("B"); var c = world.CreateEntity("C");
                var first = (Entity[])world.GetEntities(); var second = (Entity[])world.GetEntities();
                Check(!ReferenceEquals(first, second)); first[0] = default; Array.Reverse(second); Same(world, a, b, c);
                world.Destroy(b); var d = world.CreateEntity("D"); Check(d.Index == b.Index && d != b);
                Same(world, a, c, d); Check(second.SequenceEqual(new[] { c, b, a })); Reject(() => world.Resolve(b));
            }),
            ("direct topology and value changes cannot produce stale or reordered copies", () => {
                var world = new World(); var expected = new List<Entity>();
                for (int i = 0; i < 64; i++) {
                    var entity = world.CreateEntity("item"); expected.Add(entity); Same(world, expected.ToArray());
                    world.Set(entity, TransformData.Identity); world.Set(entity, TransformData.Identity with { Position = new(i, 1, 2) });
                    world.Rename(entity, "renamed"); Same(world, expected.ToArray());
                    Check(world.Remove<TransformData>(entity)); Same(world, expected.ToArray());
                    if (i % 3 == 0) { world.Destroy(expected[0]); expected.RemoveAt(0); Same(world, expected.ToArray()); }
                }
            }),
            ("warm and initially cold reads observe committed membership during abort", () => {
                foreach (bool warm in new[] { false, true }) {
                    var world = new World(); var a = world.CreateEntity("A"); var b = world.CreateEntity("B");
                    if (warm) Same(world, a, b);
                    world.BeginStep(); world.StageDestroy(world.GetUuid(a)); Guid id = Guid.NewGuid(); world.StageCreate(id, "pending");
                    Entity pending = world.CandidateEntity(id); Same(world, a, b); world.AbortStep(); Same(world, a, b);
                    Reject(() => world.Resolve(pending));
                }
            }),
            ("prepared structural commit publishes new order only on install", () => {
                var world = new World(); var a = world.CreateEntity("A"); var b = world.CreateEntity("B"); Same(world, a, b);
                world.BeginStep(); world.StageDestroy(world.GetUuid(a)); Guid id = Guid.NewGuid(); world.StageCreate(id, "C");
                var pending = world.CandidateEntity(id);
                Action install = world.PrepareStep(_ => Same(world, a, b)); Same(world, a, b); install();
                Same(world, b, pending); Check(world.FindEntity(id) == pending); Reject(() => world.Resolve(a));
            }),
            ("failed preparation preserves committed copies and subsequent successful reads", () => {
                var world = new World(); var a = world.CreateEntity("A"); Same(world, a);
                world.BeginStep(); world.StageCreate(Guid.NewGuid(), "candidate");
                Reject(() => world.PrepareStep(_ => { Same(world, a); throw new InvalidOperationException("reject"); }));
                Same(world, a); world.AbortStep(); Same(world, a);
                var b = world.CreateEntity("B"); Same(world, a, b);
            }),
            ("restore replaces identity and order while failed restore retains old copy", () => {
                var world = new World(); var a = world.CreateEntity("A"); var b = world.CreateEntity("B");
                Guid aid = world.GetUuid(a), bid = world.GetUuid(b); var old = world.GetEntities();
                var snapshot = world.CaptureSnapshot();
                Reject(() => world.RestoreSnapshot(snapshot with { Objects = [snapshot.Objects[0], snapshot.Objects[0]] }));
                Same(world, a, b);
                world.RestoreSnapshot(snapshot with { Objects = [snapshot.Objects[1], snapshot.Objects[0]] });
                var newB = world.FindEntity(bid); var newA = world.FindEntity(aid); Same(world, newB, newA);
                Check(old.SequenceEqual(new[] { a, b })); Reject(() => world.Resolve(a)); Reject(() => world.Resolve(b));
            }),
            ("read-only copies remain detached and foreign-thread access rejects", () => {
                var world = new World(); var a = world.CreateEntity("A");
                using (world.ReadOnly()) { var result = (Entity[])world.GetEntities(); result[0] = default; Same(world, a); }
                Exception? failure = null; var thread = new Thread(() => { try { _ = world.GetEntities(); } catch (Exception error) { failure = error; } });
                thread.Start(); thread.Join(); Check(failure is InvalidOperationException); Same(world, a);
                var other = new World(); var otherA = other.CreateEntity("A"); Same(other, otherA); Reject(() => other.Resolve(a));
            }),
            ("value-only fixed steps preserve copied membership", () => {
                var world = new World(); var a = world.CreateEntity("A"); world.Set(a, TransformData.Identity); Same(world, a);
                ulong membership = world.MembershipVersion;
                for (int i = 0; i < 8; i++) {
                    world.BeginStep(); world.Set(a, TransformData.Identity with { Position = new(i, 0, 0) }); Same(world, a);
                    world.CommitStep(); Same(world, a); Check(world.MembershipVersion == membership && world.Get<TransformData>(a).Position.X == i);
                }
            }),
            ("warm copied reads allocate output only at 100 1000 and 4096", () => {
                foreach (int count in new[] { 100, 1000, World.MaxObjects }) {
                    var world = new World(); for (int i = 0; i < count; i++) world.CreateEntity("item");
                    for (int i = 0; i < 8; i++) Check(world.GetEntities().Count == count);
                    const int iterations = 16; int sum = 0;
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 0; i < iterations; i++) sum += world.GetEntities().Count;
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    Console.WriteLine($"E5 copied entities: count={count}, bytes/call={allocated / iterations}; output allocation, not latency acceptance.");
                    Check(sum == count * iterations && allocated > 0 && allocated <= iterations * (count * (long)Unsafe.SizeOf<Entity>() + 64));
                }
            })
        };
        int failed = 0;
        foreach (var test in tests) {
            try { test.Run(); Console.WriteLine("PASS E5-C " + test.Name); }
            catch (Exception error) { failed++; Console.Error.WriteLine("FAIL E5-C " + test.Name + ": " + error); }
        }
        Console.WriteLine($"E5-C copied entity reads: {tests.Length - failed}/{tests.Length} passed.");
        return failed;
    }
}
