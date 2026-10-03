using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Ncma.Runtime;

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
        var world = new World(); var a = world.CreateObject("Logic"); var b = world.CreateObject("Other");
        Check(!a.Has<TransformData>() && world.Count == 2);
        a.Set(TransformData.Identity); Check(a.Get<TransformData>() == TransformData.Identity);
        Check(a.Remove<TransformData>() && !a.Has<TransformData>());
        a.Destroy(); Check(world.Count == 1 && b.Name == "Other"); Reject(() => _ = a.Name);
        Check(typeof(GameObject).GetProperty("Parent") is null && typeof(GameObject).GetProperty("LogicLanguage") is null);
    }),
    ("UUIDs and duplicate rejection", () => {
        var world = new World(); Guid id = Guid.NewGuid(); var a = world.CreateObject("A", id);
        Check(world.FindObject(id).Reference == a.Reference);
        Reject(() => world.CreateObject("Duplicate", id)); Reject(() => world.CreateObject("Empty", Guid.Empty));
        Check(world.Count == 1);
    }),
    ("Cross-world and owner-thread safety", () => {
        var world = new World(); var other = new World(); var a = world.CreateObject("A");
        Reject(() => other.FindObject(a.PersistentId));
        Check(Task.Run(() => { try { _ = world.Count; return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult());
    }),
    ("Transform validation is atomic", () => {
        var world = new World(); var a = world.CreateObject("A"); a.Set(TransformData.Identity); ulong revision = world.Revision;
        Reject(() => a.Set(TransformData.Identity with { Position = new(float.NaN, 0, 0) }));
        Reject(() => a.Set(TransformData.Identity with { Rotation = new(0, 0, 0, 0) }));
        Check(world.Revision == revision && a.Get<TransformData>() == TransformData.Identity);
        a.Set(TransformData.Identity with { Rotation = new(0, 0, 0, 2) }); Check(a.Get<TransformData>().Rotation == Quaternion.Identity);
    }),
    ("Registered extensions and frozen metadata", () => {
        var registry = Extensions(); var world = new World(components: registry); var a = world.CreateObject("A");
        a.Set(new Health(100)); Check(a.Get<Health>().Points == 100);
        Reject(() => a.Set(new Health(-1))); Check(a.Get<Health>().Points == 100);
        Reject(() => registry.Register<Health>("new.health", 1, "{}", v => v));

    }),
    ("Mutable reference component rejected", () => {
        var registry = ComponentRegistry.CreateDefault();
        Reject(() => registry.Register<BadComponent>("bad", 1, "{}", value => value));
        Reject(() => registry.Register<BadHandle>("bad.handle", 1, "{}", value => value));
    }),
    ("Managed snapshot round-trip and stale handles", () => {
        var world = new World(components: Extensions()); var a = world.CreateObject("A"); Guid id = a.PersistentId;
        a.Set(new Health(25)); a.Set(TransformData.Identity); string text = world.SerializeSnapshot();
        Check(!text.Contains("worldId") && !text.Contains("reference"));
        world.LoadSnapshot(text); Reject(() => _ = a.Name);
        Check(world.FindObject(id).Get<Health>().Points == 25 && world.SerializeSnapshot() == text);
    }),
    ("Unknown/malformed snapshots preserve World", () => {
        var world = new World(); world.CreateObject("A"); string before = world.SerializeSnapshot(); ulong revision = world.Revision;
        Reject(() => world.LoadSnapshot(before.Replace("\"version\":1", "\"version\":99")));
        Reject(() => world.LoadSnapshot(before.Replace("\"objects\":", "\"python\":true,\"objects\":")));
        Reject(() => world.LoadSnapshot(before.Replace("\"version\":1", "\"version\":99,\"version\":1")));
        var snapshot = world.CaptureSnapshot();
        var malformed = snapshot with { Objects = [snapshot.Objects[0] with { Components = [new("python.behaviour", 1, Json("{}"))] }] };
        Reject(() => world.RestoreSnapshot(malformed));
        Check(world.Revision == revision && world.SerializeSnapshot() == before);
    }),
    ("Fixed-step scheduling and committed reads", () => {
        var world = new World(); Guid id = world.CreateObject("A").PersistentId; world.FindObject(id).Set(TransformData.Identity);
        var runner = new WorldRunner(world, 0.1); int observed = 0;
        runner.AddSystem(new TestSystem((w, dt) => { Check(dt == 0.1); var a = w.FindObject(id); a.Set(a.Get<TransformData>() with { Position = new(a.Get<TransformData>().Position.X + 1, 0, 0) }); }));
        runner.AddSystem(new TestSystem((w, _) => { Check(w.FindObject(id).Get<TransformData>().Position.X == observed++); }));
        Check(runner.Advance(0.05) == 0 && world.Tick == 0);
        Check(runner.Advance(0.15) == 2 && world.Tick == 2 && world.FindObject(id).Get<TransformData>().Position.X == 2);
        Reject(() => runner.Advance(double.NaN)); Reject(() => runner.Advance(2));
        Check(world.Tick == 2 && !runner.IsFaulted);
    }),
    ("Fixed-step failure aborts writes and requires recovery", () => {
        var world = new World(); var a = world.CreateObject("A"); a.Set(TransformData.Identity); ulong revision = world.Revision;
        var runner = new WorldRunner(world, 0.1);
        runner.AddSystem(new TestSystem((_, _) => { a.Set(TransformData.Identity with { Position = Vector3.One }); a.Name = "Forbidden"; }));
        Reject(() => runner.Advance(0.1));
        Check(runner.IsFaulted && world.Tick == 0 && world.Revision == revision && a.Get<TransformData>() == TransformData.Identity && a.Name == "A");
        Reject(() => runner.Advance(0)); runner.ResetFault(); Check(!runner.IsFaulted);
    }),
    ("Fixed-step budget/reentrancy/structure cannot bypass guards", () => {
        var world = new World(); var runner = new WorldRunner(world, 0.1, 2); Guid id = world.CreateObject("A").PersistentId;
        world.FindObject(id).Set(TransformData.Identity);
        runner.AddSystem(new TestSystem((w, _) => {
            Reject(() => w.CreateObject("Forbidden")); Reject(() => w.FindObject(id).Destroy());
            Reject(() => w.FindObject(id).Remove<TransformData>());
            Reject(() => runner.Advance(0)); Reject(() => runner.AddSystem(new TestSystem((_, _) => { })));
            w.FindObject(id).Set(TransformData.Identity with { Position = new(1, 0, 0) });
            w.FindObject(id).Set(TransformData.Identity with { Position = new(2, 0, 0) });
        }));
        Reject(() => runner.Advance(0.3)); Check(world.Tick == 0 && !runner.IsFaulted);
        Check(runner.Advance(0.1) == 1 && world.FindObject(id).Get<TransformData>().Position.X == 2);
    }),
    ("Impure trusted validator cannot overwrite outside mutations", () => {
        World? live = null; bool mutate = false;
        var registry = new ComponentRegistry();
        registry.Register<Health>("health", 1, """{"type":"object","required":["points"],"properties":{"points":{"type":"integer"}}}""", value => {
            if (mutate) { mutate = false; live!.CreateObject("Outside"); }
            return value;
        });
        live = new World(components: registry); var a = live.CreateObject("A"); a.Set(new Health(1));
        var snapshot = live.CaptureSnapshot(); mutate = true;
        Reject(() => live.RestoreSnapshot(snapshot));
        Check(live.Count == 1 && a.Get<Health>().Points == 1); // Preparation now blocks the mutation before it happens.

    }),
};

int failures = 0;
foreach (var test in cases)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { ++failures; Console.Error.WriteLine("FAIL " + test.Name + ": " + error); }
}
Console.WriteLine($"Ncma managed headless: {cases.Length - failures}/{cases.Length} passed (no native DLL/Python).");
return failures == 0 ? 0 : 1;

readonly record struct Health(int Points) : IComponent;
readonly record struct BadComponent(List<int> Values) : IComponent;
readonly record struct BadHandle(nint Address) : IComponent;
sealed class TestSystem(Action<World, double> update) : IWorldSystem
{
    public void FixedUpdate(World world, double fixedDeltaSeconds) => update(world, fixedDeltaSeconds);
}
