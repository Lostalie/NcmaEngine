using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Ncma.Runtime;

if (args.Contains("--ecs-world-read")) { WorldReadMeasurements.Run(); return 0; }

static void Check(bool valid, string message = "Assertion failed") { if (!valid) throw new Exception(message); }
static void Reject(Action action)
{
    bool failed = false;
    try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or JsonException) { failed = true; }
    Check(failed, "Invalid operation was accepted");
}
static JsonElement Json(string text) { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }
static ComponentRegistry Extensions()
{
    var registry = ComponentRegistry.CreateDefault();
    registry.Register<Health>("game.health", 1, """
        {"type":"object","additionalProperties":false,"required":["points"],"properties":{"points":{"type":"integer"}}}
        """, value => value.Points >= 0 ? value : throw new ArgumentException("Health must be nonnegative."));
    return registry;
}

var cases = new (string Name, Action Run)[]
{
    ("No native/Python dependency", () => {
        var assembly = typeof(World).Assembly;
        Check(!assembly.GetReferencedAssemblies().Any(a => a.Name!.StartsWith("Ncma.Managed") || a.Name.Contains("Python")));
        Check(!assembly.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            .Any(m => m.GetCustomAttribute<DllImportAttribute>() is not null));
    }),
    ("Empty flat objects and optional Transform", () => {
        var world = new World(); global::Ncma.Runtime.Entity a = world.CreateEntity("Logic"); global::Ncma.Runtime.Entity b = world.CreateEntity("Other");
        Check(!world.Has<TransformData>(a) && world.Count == 2);
        world.Set(a, TransformData.Identity); Check(world.Get<TransformData>(a) == TransformData.Identity);
        Check(world.Remove<TransformData>(a) && !world.Has<TransformData>(a));
        world.Destroy(a); Check(world.Count == 1 && world.GetName(b) == "Other"); Reject(() => _ = world.GetName(a));
        Check(typeof(World).Assembly.GetType("Ncma.Runtime.GameObject") is null);
        Check(typeof(Entity).GetProperty("Parent") is null && typeof(Entity).GetProperty("LogicLanguage") is null);
        Check(new[] { "CreateObject", "FindObject", "GetObjects" }.All(name => typeof(World).GetMethod(name) is null));
    }),
    ("UUIDs and duplicate rejection", () => {
        var world = new World(); Guid id = Guid.NewGuid(); global::Ncma.Runtime.Entity a = world.CreateEntity("A", id);
        Check(world.FindEntity(id) == a);
        Reject(() => world.CreateEntity("Duplicate", id)); Reject(() => world.CreateEntity("Empty", Guid.Empty));
        Check(world.Count == 1);
    }),
    ("Cross-world and owner-thread safety", () => {
        var world = new World(); var other = new World(); global::Ncma.Runtime.Entity a = world.CreateEntity("A");
        Reject(() => other.FindEntity(world.GetUuid(a)));
        Check(Task.Run(() => { try { _ = world.Count; return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult());
    }),
    ("Transform validation is atomic", () => {
        var world = new World(); global::Ncma.Runtime.Entity a = world.CreateEntity("A"); world.Set(a, TransformData.Identity); ulong revision = world.Revision;
        Reject(() => world.Set(a, TransformData.Identity with { Position = new(float.NaN, 0, 0) }));
        Reject(() => world.Set(a, TransformData.Identity with { Rotation = new(0, 0, 0, 0) }));
        Check(world.Revision == revision && world.Get<TransformData>(a) == TransformData.Identity);
        world.Set(a, TransformData.Identity with { Rotation = new(0, 0, 0, 2) }); Check(world.Get<TransformData>(a).Rotation == Quaternion.Identity);
    }),
    ("Registered extensions and frozen metadata", () => {
        var registry = Extensions(); var world = new World(components: registry); global::Ncma.Runtime.Entity a = world.CreateEntity("A");
        world.Set(a, new Health(100)); Check(world.Get<Health>(a).Points == 100);
        Reject(() => world.Set(a, new Health(-1))); Check(world.Get<Health>(a).Points == 100);
        Reject(() => registry.Register<Health>("new.health", 1, "{}", v => v));

    }),
    ("Mutable reference component rejected", () => {
        var registry = ComponentRegistry.CreateDefault();
        Reject(() => registry.Register<BadComponent>("bad", 1, "{}", value => value));
        Reject(() => registry.Register<BadHandle>("bad.handle", 1, "{}", value => value));
    }),
    ("Managed snapshot round-trip and stale handles", () => {
        var world = new World(components: Extensions()); global::Ncma.Runtime.Entity a = world.CreateEntity("A"); Guid id = world.GetUuid(a);
        world.Set(a, new Health(25)); world.Set(a, TransformData.Identity); string text = world.SerializeSnapshot();
        Check(!text.Contains("worldId") && !text.Contains("reference"));
        world.LoadSnapshot(text); Reject(() => _ = world.GetName(a));
        Check(world.Get<Health>(world.FindEntity(id)).Points == 25 && world.SerializeSnapshot() == text);
    }),
    ("Unknown/malformed snapshots preserve World", () => {
        var world = new World(); world.CreateEntity("A"); string before = world.SerializeSnapshot(); ulong revision = world.Revision;
        Reject(() => world.LoadSnapshot(before.Replace("\"version\":1", "\"version\":99")));
        Reject(() => world.LoadSnapshot(before.Replace("\"objects\":", "\"python\":true,\"objects\":")));
        Reject(() => world.LoadSnapshot(before.Replace("\"version\":1", "\"version\":99,\"version\":1")));
        var snapshot = world.CaptureSnapshot();
        var malformed = snapshot with { Objects = [snapshot.Objects[0] with { Components = [new("python.behaviour", 1, Json("{}"))] }] };
        Reject(() => world.RestoreSnapshot(malformed));
        Check(world.Revision == revision && world.SerializeSnapshot() == before);
    }),
    ("Fixed-step scheduling and committed reads", () => {
        var world = new World(); Guid id = world.GetUuid(world.CreateEntity("A")); world.Set(world.FindEntity(id), TransformData.Identity);
        var runner = new WorldRunner(world, 0.1); int observed = 0;
        runner.AddSystem(new TestSystem((w, dt) => { Check(dt == 0.1); global::Ncma.Runtime.Entity a = w.FindEntity(id); w.Set(a, w.Get<TransformData>(a) with { Position = new(w.Get<TransformData>(a).Position.X + 1, 0, 0) }); }));
        runner.AddSystem(new TestSystem((w, _) => { Check(w.Get<TransformData>(w.FindEntity(id)).Position.X == observed++); }));
        Check(runner.Advance(0.05) == 0 && world.Tick == 0);
        Check(runner.Advance(0.15) == 2 && world.Tick == 2 && world.Get<TransformData>(world.FindEntity(id)).Position.X == 2);
        Reject(() => runner.Advance(double.NaN)); Reject(() => runner.Advance(2));
        Check(world.Tick == 2 && !runner.IsFaulted);
    }),
    ("Fixed-step failure aborts writes and requires recovery", () => {
        var world = new World(); global::Ncma.Runtime.Entity a = world.CreateEntity("A"); world.Set(a, TransformData.Identity); ulong revision = world.Revision;
        var runner = new WorldRunner(world, 0.1);
        runner.AddSystem(new TestSystem((_, _) => { world.Set(a, TransformData.Identity with { Position = Vector3.One }); world.Rename(a, "Forbidden"); }));
        Reject(() => runner.Advance(0.1));
        Check(runner.IsFaulted && world.Tick == 0 && world.Revision == revision && world.Get<TransformData>(a) == TransformData.Identity && world.GetName(a) == "A");
        Reject(() => runner.Advance(0)); runner.ResetFault(); Check(!runner.IsFaulted);
    }),
    ("Fixed-step budget/reentrancy/structure cannot bypass guards", () => {
        var world = new World(); var runner = new WorldRunner(world, 0.1, 2); Guid id = world.GetUuid(world.CreateEntity("A"));
        world.Set(world.FindEntity(id), TransformData.Identity);
        runner.AddSystem(new TestSystem((w, _) => {
            Reject(() => w.CreateEntity("Forbidden")); Reject(() => w.Destroy(w.FindEntity(id)));
            Reject(() => w.Remove<TransformData>(w.FindEntity(id)));
            Reject(() => runner.Advance(0)); Reject(() => runner.AddSystem(new TestSystem((_, _) => { })));
            w.Set(w.FindEntity(id), TransformData.Identity with { Position = new(1, 0, 0) });
            w.Set(w.FindEntity(id), TransformData.Identity with { Position = new(2, 0, 0) });
        }));
        Reject(() => runner.Advance(0.3)); Check(world.Tick == 0 && !runner.IsFaulted);
        Check(runner.Advance(0.1) == 1 && world.Get<TransformData>(world.FindEntity(id)).Position.X == 2);
    }),
    ("Impure trusted validator cannot overwrite outside mutations", () => {
        World? live = null; bool mutate = false;
        var registry = new ComponentRegistry();
        registry.Register<Health>("health", 1, """{"type":"object","required":["points"],"properties":{"points":{"type":"integer"}}}""", value => {
            if (mutate) { mutate = false; live!.CreateEntity("Outside"); }
            return value;
        });
        live = new World(components: registry); global::Ncma.Runtime.Entity a = live.CreateEntity("A"); live.Set(a, new Health(1));
        var snapshot = live.CaptureSnapshot(); mutate = true;
        Reject(() => live.RestoreSnapshot(snapshot));
        Check(live.Count == 1 && live.Get<Health>(a).Points == 1); // Preparation now blocks the mutation before it happens.

    }),
};

int failures = 0;
foreach (var test in cases)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { ++failures; Console.Error.WriteLine("FAIL " + test.Name + ": " + error); }
}
Console.WriteLine($"Ncma managed headless: {cases.Length - failures}/{cases.Length} passed (no native DLL/Python).");
failures += EcsStorageTests.Run();
failures += EntityIdentityTests.Run();
failures += EntityQueryTests.Run();
failures += StructuralCandidateTests.Run();
failures += PagedStorageTests.Run();
failures += IncrementalValidationTests.Run();
failures += ComponentShapeTests.Run();
failures += EntityLookupTests.Run();
if (failures == 0 && args.Contains("--ecs-pool-benchmark")) PoolMeasurements.Run();
return failures == 0 ? 0 : 1;

readonly record struct Health(int Points) : IComponent;
readonly record struct BadComponent(List<int> Values) : IComponent;
readonly record struct BadHandle(nint Address) : IComponent;
sealed class TestSystem(Action<World, double> update) : IWorldSystem
{
    public void FixedUpdate(World world, double fixedDeltaSeconds) => update(world, fixedDeltaSeconds);
}
