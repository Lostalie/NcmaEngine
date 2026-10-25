using System.Reflection;
using Ncma.Runtime;

internal static class StructuralCandidateTests
{
    private readonly record struct TextData(string Text) : IComponent;
    private static void Check(bool value) { if (!value) throw new Exception("Structural candidate assertion failed."); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; }
        throw new Exception("Invalid candidate accepted.");
    }
    private static ComponentSnapshot Transform(World world) => new("ncma.transform", 1, world.Components.Encode(TransformData.Identity));
    internal static int Run()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("failed fork restores preceding candidate roots and burns generation", () => {
                var world = new World(); var a = world.CreateEntity("A"); world.Set(a, TransformData.Identity);
                var uuid = world.GetUuid(a); var created = Guid.NewGuid(); Entity failed = default;
                var query = world.Query<TransformData>(); var iterator = query.GetEnumerator(); Check(iterator.MoveNext());
                string before = world.SerializeSnapshot(); ulong revision = world.Revision;
                world.BeginStep(); world.Set(a, TransformData.Identity with { Position = new(3, 0, 0) });
                object storage = typeof(World).GetField("_stepStorage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
                Reject(() => world.PrepareStructuralBatch(() => {
                    world.StageRemove(uuid, "ncma.transform"); world.StageCreate(created, "Prefix"); failed = world.CandidateEntity(created);
                    world.StageAdd(created, Transform(world)); throw new ArgumentException("Late failure");
                }, _ => { }));
                Check(ReferenceEquals(storage, typeof(World).GetField("_stepStorage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)));
                Check(((ComponentStorage)storage).Get<TransformData>(a.Index).Position.X == 3);
                Check(world.CandidateEntity(uuid) == a); Reject(() => world.CandidateEntity(created)); Reject(world.CommitStep);
                Check(query.Count == 1 && iterator.Current == a && !iterator.MoveNext()); world.AbortStep();
                Check(world.SerializeSnapshot() == before && world.Revision == revision);
                world.BeginStep(); world.StageCreate(created, "Retry"); var accepted = world.CandidateEntity(created);
                Check(accepted.Index == failed.Index && accepted.Generation > failed.Generation); world.CommitStep();
                Reject(() => world.Resolve(failed)); Check(world.Resolve(accepted) == accepted);
            }),
            ("successful preparation is invisible until install and enclosing commit", () => {
                var world = new World(); var a = world.CreateEntity("A"); var id = Guid.NewGuid();
                var query = world.Query<TransformData>(); Check(query.Count == 0); world.BeginStep();
                var install = world.PrepareStructuralBatch(() => { world.StageCreate(id, "B"); world.StageAdd(id, Transform(world)); },
                    snapshot => Check(snapshot.Objects.Count() == 2));
                Reject(() => world.CandidateEntity(id)); Check(world.Count == 1 && query.Count == 0);
                install(); var entity = world.CandidateEntity(id); Reject(() => world.Resolve(entity)); Check(query.Count == 0);
                world.CommitStep(); Check(query.Count == 1 && world.Resolve(entity) == entity && world.Resolve(a) == a);
                Reject(install);
            }),
            ("validation failure leaves prior candidate and committed query intact", () => {
                var world = new World(); var a = world.CreateEntity("A"); var id = Guid.NewGuid(); world.BeginStep();
                Reject(() => world.PrepareStructuralBatch(() => world.StageCreate(id, "Prefix"), _ => throw new ArgumentException("Metadata")));
                Reject(() => world.CandidateEntity(id)); Check(world.CandidateEntity(world.GetUuid(a)) == a); Reject(world.CommitStep); world.AbortStep();
            }),
            ("authority and frozen membership are not bypassed", () => {
                foreach (bool remove in new[] { false, true }) {
                    var world = new World(); var a = world.CreateEntity("A"); world.Set(a, TransformData.Identity); var uuid = world.GetUuid(a);
                    using var proof = world.ClaimComponents<TransformData>([uuid], publishRequired: false, freezeMembership: true);
                    world.BeginStep(); Reject(() => world.PrepareStructuralBatch(() => {
                        if (remove) world.StageRemove(uuid, "ncma.transform"); else world.StageDestroy(uuid);
                    }, _ => { })); Reject(world.CommitStep); world.AbortStep(); Check(world.Get<TransformData>(a) == TransformData.Identity);
                }
                var empty = new World(); var b = empty.CreateEntity("B");
                using var frozen = empty.ClaimComponents<TransformData>([], publishRequired: false, freezeMembership: true);
                empty.BeginStep(); Reject(() => empty.PrepareStructuralBatch(() => empty.StageAdd(empty.GetUuid(b), Transform(empty)), _ => { }));
                Reject(empty.CommitStep); empty.AbortStep(); Check(!empty.Has<TransformData>(b));
            }),
            ("component copy on write isolates multiple candidate forks", () => {
                var world = new World(); var a = world.CreateEntity("A"); world.Set(a, TransformData.Identity); var uuid = world.GetUuid(a);
                world.BeginStep(); world.Set(a, TransformData.Identity with { Position = new(5, 0, 0) });
                world.PrepareStructuralBatch(() => world.StageRename(uuid, "Renamed"), _ => { })();
                world.Set(a, TransformData.Identity with { Position = new(8, 0, 0) });
                world.PrepareStructuralBatch(() => world.StageCreate(Guid.NewGuid(), "Other"), _ => { })();
                Check(world.Get<TransformData>(a).Position.X == 0 && world.GetName(a) == "A");
                world.CommitStep(); Check(world.Get<TransformData>(a).Position.X == 8 && world.GetName(a) == "Renamed");
            }),
            ("caught validation mutation poisons candidate", () => {
                var world = new World(); var a = world.CreateEntity("A"); world.Set(a, TransformData.Identity);
                world.BeginStep(); Reject(() => world.PrepareStructuralBatch(() => world.StageRename(world.GetUuid(a), "Prefix"), _ => {
                    Reject(() => world.Set(a, TransformData.Identity with { Position = new(9, 0, 0) }));
                })); Reject(world.CommitStep); world.AbortStep(); Check(world.GetName(a) == "A");
            }),
            ("full world snapshot budget is retained", () => {
                var registry = global::Ncma.Runtime.CompositionRegistry.Build();
                registry.Register<TextData>("test.text", 1, """{"type":"object","required":["text"],"properties":{"text":{"type":"string"}}}""", value => value);
                var world = new World(components: registry); var text = new TextData(new string('X', 63000));
                for (int i = 0; i < 64; ++i) world.Set(world.CreateEntity("Existing"), text);
                string before = world.SerializeSnapshot(); var component = new ComponentSnapshot("test.text", 1, registry.Encode(text));
                world.BeginStep(); Reject(() => world.PrepareStructuralBatch(() => {
                    for (int i = 0; i < 3; ++i) { var id = Guid.NewGuid(); world.StageCreate(id, "Overflow"); world.StageAdd(id, component); }
                }, _ => { })); Reject(world.CommitStep); world.AbortStep(); Check(world.SerializeSnapshot() == before);
            }),
            ("prepared candidate cannot survive abort or a replaced candidate", () => {
                var world = new World(); var a = world.CreateEntity("A"); var uuid = world.GetUuid(a);
                world.BeginStep(); var stale = world.PrepareStructuralBatch(() => world.StageRename(uuid, "Stale"), _ => { });
                world.PrepareStructuralBatch(() => world.StageRename(uuid, "Current"), _ => { })(); Reject(stale);
                world.AbortStep(); world.BeginStep(); Reject(stale); world.AbortStep(); Check(world.GetName(a) == "A");
            }),
        };
        int failures = 0;
        foreach (var test in cases) {
            try { test.Run(); Console.WriteLine("PASS structural candidate " + test.Name); }
            catch (Exception error) { failures++; Console.Error.WriteLine("FAIL structural candidate " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Structural candidate tests: {cases.Length - failures}/{cases.Length} passed."); return failures;
    }
}
