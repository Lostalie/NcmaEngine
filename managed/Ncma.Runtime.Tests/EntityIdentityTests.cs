using System.Reflection;
using System.Text.Json;
using Ncma.Runtime;

internal static class EntityIdentityTests
{
    private readonly record struct Direct(Entity Target) : IComponent;
    private readonly record struct Inner(Entity Target);
    private readonly record struct Nested(Inner Value) : IComponent;
    private readonly record struct NullableEntity(Entity? Target) : IComponent;
    private static void Check(bool condition) { if (!condition) throw new Exception("Entity assertion failed."); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or JsonException) { return; }
        throw new Exception("Invalid Entity operation accepted.");
    }
    private static ulong[] Reservations(World world)
    {
        var allocator = typeof(World).GetField("_generations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
        return (ulong[])typeof(EntityGenerations).GetField("_reserved", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(allocator)!;
    }
    internal static int Run()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("single value identity API has no obsolete wrapper or signatures", () => {
                Check(typeof(Entity).IsValueType && typeof(World).Assembly.GetType("Ncma.Runtime.GameObject") is null);
                Check(new[] { "CreateObject", "FindObject", "GetObjects" }.All(name => typeof(World).GetMethod(name) is null));
                Check(typeof(World).GetMethod("CreateEntity")!.ReturnType == typeof(Entity));
                Check(typeof(World).GetMethod("FindEntity")!.ReturnType == typeof(Entity));
                Check(typeof(World).GetMethod("GetEntities")!.ReturnType == typeof(IReadOnlyList<Entity>));
                var world = new World(); var a = world.CreateEntity("A"); Guid uuid = world.GetUuid(a);
                for (int i = 0; i < 512; i++) _ = world.FindEntity(uuid);
                long before = GC.GetAllocatedBytesForCurrentThread(); Entity found = default;
                for (int i = 0; i < 512; i++) found = world.FindEntity(uuid);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Check(found == a && allocated == 0); // Lookup itself allocates no per-entity facade.
            }),
            ("enumeration is a detached committed identity copy", () => {
                var world = new World(); var a = world.CreateEntity("A"); var b = world.CreateEntity("B");
                var copy = (Entity[])world.GetEntities(); copy[0] = default;
                Check(world.GetEntities().SequenceEqual(new[] { a, b }));
                world.BeginStep(); world.StageDestroy(world.GetUuid(a)); var uuid = Guid.NewGuid();
                world.StageCreate(uuid, "C"); var candidate = world.CandidateEntity(uuid);
                Check(world.GetEntities().SequenceEqual(new[] { a, b })); Reject(() => world.FindEntity(uuid));
                world.CommitStep(); Check(world.GetEntities().SequenceEqual(new[] { b, candidate }));
                Reject(() => world.GetName(a)); Check(world.FindEntity(uuid) == candidate);
            }),
            ("entity entry points enforce owner thread and read-only creation", () => {
                var world = new World(); var a = world.CreateEntity("A"); Guid uuid = world.GetUuid(a);
                foreach (Action action in new Action[] { () => world.CreateEntity("bad"), () => world.FindEntity(uuid), () => world.GetEntities() })
                    Check(Task.Run(() => { try { action(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult());
                using (world.ReadOnly()) { Reject(() => world.CreateEntity("bad")); Check(world.FindEntity(uuid) == a); }
                Check(world.Count == 1 && world.Revision == 1);
            }),
            ("default/foreign/range/zero/wrong generation rejected", () => {
                var world = new World(); var a = world.CreateEntity("A");
                var foreign = new World().CreateEntity("foreign");
                Entity[] invalid = [default, foreign, a with { Index = -1 }, a with { Index = World.MaxObjects },
                    a with { Generation = 0 }, a with { Generation = a.Generation + 1 }, a with { WorldIdentity = Guid.Empty }];
                foreach (var entity in invalid)
                {
                    Reject(() => world.Resolve(entity)); Reject(() => world.GetUuid(entity));
                    Reject(() => world.Has<TransformData>(entity)); Reject(() => world.Set(entity, TransformData.Identity));
                    Reject(() => world.Rename(entity, "bad")); Reject(() => world.Destroy(entity));
                }
                Check(world.Count == 1 && world.GetName(a) == "A" && !world.Has<TransformData>(a));
            }),
            ("slot reuse increments generation without changing creation order", () => {
                var world = new World(); global::Ncma.Runtime.Entity a = world.CreateEntity("A"); global::Ncma.Runtime.Entity b = world.CreateEntity("B");
                world.Destroy(a); global::Ncma.Runtime.Entity c = world.CreateEntity("C");
                Check(c.Index == a.Index && c.Generation > a.Generation);
                Reject(() => world.Resolve(a));
                Check(world.GetEntities().Select(o => world.GetName(o)).SequenceEqual(new[] { "B", "C" }));
                Check(world.CaptureSnapshot().Objects.Select(o => o.PersistentId).SequenceEqual(new[] { world.GetUuid(b), world.GetUuid(c) }));
            }),
            ("aborted candidate reservation never revives", () => {
                var world = new World(); Guid uuid = Guid.NewGuid();
                world.BeginStep(); world.StageCreate(uuid, "candidate"); var aborted = world.CandidateEntity(uuid);
                Reject(() => world.Resolve(aborted)); world.AbortStep();
                world.BeginStep(); world.StageCreate(uuid, "accepted"); var accepted = world.CandidateEntity(uuid);
                Check(accepted.Index == aborted.Index && accepted.Generation > aborted.Generation);
                world.CommitStep(); Check(world.Resolve(accepted) == accepted); Reject(() => world.Resolve(aborted));
            }),
            ("candidate reuse preserves committed handle through abort", () => {
                var world = new World(); global::Ncma.Runtime.Entity a = world.CreateEntity("A"); world.Set(a, TransformData.Identity);
                string before = world.SerializeSnapshot(); ulong revision = world.Revision;
                world.BeginStep(); world.StageDestroy(world.GetUuid(a)); Guid uuid = Guid.NewGuid(); world.StageCreate(uuid, "B");
                var b = world.CandidateEntity(uuid);
                Check(a.Index == b.Index && world.Get<TransformData>(a) == TransformData.Identity);
                Reject(() => world.Resolve(b)); world.AbortStep();
                Check(world.SerializeSnapshot() == before && world.Revision == revision && world.Resolve(a) == a);
                world.Destroy(a); global::Ncma.Runtime.Entity c = world.CreateEntity("C"); Check(c.Generation > b.Generation);
                Reject(() => world.Resolve(b)); Reject(() => world.Resolve(a));
            }),
            ("destroyed candidate handle never revives within a step", () => {
                var world = new World(); var first = Guid.NewGuid(); var second = Guid.NewGuid();
                world.BeginStep(); world.StageCreate(first, "first"); var a = world.CandidateEntity(first);
                world.StageDestroy(first); world.StageCreate(second, "second"); var b = world.CandidateEntity(second);
                world.CommitStep(); Check(a.Index == b.Index && b.Generation > a.Generation);
                Reject(() => world.Resolve(a)); Check(world.Resolve(b) == b);
            }),
            ("late candidate failure burns identity but not committed revision", () => {
                var world = new World(); Guid uuid = Guid.NewGuid();
                world.BeginStep(); world.StageCreate(uuid, "bad"); var bad = world.CandidateEntity(uuid);
                Reject(() => world.PrepareStep(_ => throw new ArgumentException("late validation"))); world.AbortStep();
                var good = world.CreateEntity("good", uuid);
                Check(good.Index == bad.Index && good.Generation > bad.Generation && world.Tick == 0 && world.Revision == 1);
                Reject(() => world.Resolve(bad));
            }),
            ("last generation can live then slot retires without wrapping", () => {
                var world = new World(); Reservations(world)[0] = ulong.MaxValue - 1;
                global::Ncma.Runtime.Entity last = world.CreateEntity("last"); Check(last.Index == 0 && last.Generation == ulong.MaxValue);
                world.Destroy(last); global::Ncma.Runtime.Entity next = world.CreateEntity("next"); Check(next.Index == 1 && next.Generation == 1);
                Reject(() => world.Resolve(last));
                Array.Fill(Reservations(world), ulong.MaxValue);
                var before = world.SerializeSnapshot(); ulong revision = world.Revision;
                Reject(() => world.CreateEntity("exhausted"));
                Check(world.SerializeSnapshot() == before && world.Revision == revision && world.GetName(next) == "next");
            }),
            ("aborting final generation retires slot but preserves old live entity", () => {
                var world = new World(); global::Ncma.Runtime.Entity a = world.CreateEntity("old"); Reservations(world)[0] = ulong.MaxValue - 1;
                world.BeginStep(); world.StageDestroy(world.GetUuid(a)); var uuid = Guid.NewGuid(); world.StageCreate(uuid, "last");
                var candidate = world.CandidateEntity(uuid); Check(candidate.Generation == ulong.MaxValue);
                world.AbortStep(); Check(world.GetName(a) == "old"); world.Destroy(a);
                global::Ncma.Runtime.Entity fresh = world.CreateEntity("new"); Check(fresh.Index == 1); Reject(() => world.Resolve(candidate));
            }),
            ("creation ordinal exhaustion fails without publishing", () => {
                var world = new World(); global::Ncma.Runtime.Entity a = world.CreateEntity("old");
                typeof(World).GetField("_nextOrdinal", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(world, ulong.MaxValue);
                Reject(() => world.CreateEntity("overflow")); world.BeginStep();
                Reject(() => world.StageCreate(Guid.NewGuid(), "overflow")); world.AbortStep();
                Check(world.Count == 1 && world.Revision == 1 && world.GetName(a) == "old");
            }),
            ("failed restore preserves identities; successful restore invalidates all", () => {
                var world = new World(); global::Ncma.Runtime.Entity a = world.CreateEntity("old"); var snapshot = world.CaptureSnapshot();
                Reject(() => world.PrepareRestore(snapshot, _ => throw new ArgumentException("late restore failure")));
                Check(world.Resolve(a) == a);
                string json = world.SerializeSnapshot(); Guid uuid = world.GetUuid(a); world.RestoreSnapshot(snapshot); global::Ncma.Runtime.Entity fresh = world.FindEntity(uuid);
                Check(fresh.WorldIdentity != a.WorldIdentity && world.SerializeSnapshot() == json);
                Reject(() => world.Resolve(a)); Check(world.Resolve(fresh) == fresh);
            }),
            ("runtime identity cannot be serialized directly or nested", () => {
                var world = new World(); var a = world.CreateEntity("A");
                Reject(() => JsonSerializer.Serialize(a)); Reject(() => JsonSerializer.Serialize(new { Nested = a }));
                Reject(() => JsonSerializer.Deserialize<Entity>("{}"));
                var registry = new ComponentRegistry();
                Reject(() => registry.Register<Direct>("test.direct", 1, "{\"type\":\"object\"}", v => v));
                Reject(() => registry.Register<Nested>("test.nested", 1, "{\"type\":\"object\"}", v => v));
                Reject(() => registry.Register<NullableEntity>("test.nullable", 1, "{\"type\":\"object\"}", v => v));
                var json = world.SerializeSnapshot();
                Check(!json.Contains("generation", StringComparison.OrdinalIgnoreCase) && !json.Contains("worldIdentity", StringComparison.OrdinalIgnoreCase));
            }),
            ("owner thread/read-only and stale writes keep guards", () => {
                var world = new World(); var entity = world.CreateEntity("A"); world.Set(entity, TransformData.Identity);
                Check(Task.Run(() => { try { world.Resolve(entity); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult());
                using (world.ReadOnly()) { Reject(() => world.Set(entity, TransformData.Identity)); Reject(() => world.Destroy(entity)); }
                world.BeginStep(); Reject(() => world.Set(entity with { Generation = 0 }, TransformData.Identity));
                Reject(world.CommitStep); world.AbortStep(); Check(world.Tick == 0 && world.Get<TransformData>(entity) == TransformData.Identity);
            }),
        };
        int failures = 0;
        foreach (var test in cases)
        {
            try { test.Run(); Console.WriteLine("PASS E1-B identity " + test.Name); }
            catch (Exception e) { failures++; Console.Error.WriteLine("FAIL E1-B identity " + test.Name + ": " + e); }
        }
        Console.WriteLine($"E1-B identity: {cases.Length - failures}/{cases.Length} passed.");
        return failures;
    }
}
