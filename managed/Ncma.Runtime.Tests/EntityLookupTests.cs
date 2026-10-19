using Ncma.Runtime;

internal static class EntityLookupTests
{
    private readonly record struct Unknown(int Value) : IComponent;
    private static void Check(bool value) { if (!value) throw new Exception("Lookup assertion failed."); }
    private static void Reject(Action action)
    { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; } throw new Exception("Owner/identity rejection missing."); }
    internal static int Run()
    {
        var cases = new (string Name, Action Run)[] {
            ("absent UUID returns false/default, known returns full identity", () => {
                var w = new World(); var e = w.CreateEntity("A"); Guid id = w.GetUuid(e);
                Check(w.TryFindEntity(id, out var found) && found == e);
                Check(!w.TryFindEntity(Guid.NewGuid(), out found) && found == default);
                Check(!w.TryFindEntity(Guid.Empty, out found) && found == default);
                Check(w.Revision == 1 && w.Count == 1);
            }),
            ("deletion and same UUID reuse never revive Entity", () => {
                var w = new World(); var old = w.CreateEntity("A"); Guid id = w.GetUuid(old);
                w.Destroy(old); Check(!w.TryFindEntity(id, out var missing) && missing == default);
                var fresh = w.CreateEntity("B", id); Check(w.TryFindEntity(id, out var e) && e == fresh && e != old);
                Reject(() => w.Resolve(old));
            }),
            ("restore and foreign worlds resolve their own identities", () => {
                var w = new World(); var old = w.CreateEntity("A"); Guid id = w.GetUuid(old);
                var snapshot = w.CaptureSnapshot(); w.RestoreSnapshot(snapshot);
                Check(w.TryFindEntity(id, out var fresh) && fresh.WorldIdentity != old.WorldIdentity);
                Reject(() => w.Resolve(old));
                var other = new World(); other.RestoreSnapshot(snapshot);
                Check(other.TryFindEntity(id, out var foreign) && foreign != fresh);
            }),
            ("staged create invisible, abort and commit keep exact identity", () => {
                var w = new World(); Guid id = Guid.NewGuid();
                w.BeginStep(); w.StageCreate(id, "pending"); var abandoned = w.CandidateEntity(id);
                Check(!w.TryFindEntity(id, out var e) && e == default); w.AbortStep();
                Check(!w.TryFindEntity(id, out e));
                w.BeginStep(); w.StageCreate(id, "accepted"); var accepted = w.CandidateEntity(id); w.CommitStep();
                Check(w.TryFindEntity(id, out e) && e == accepted && e != abandoned);
            }),
            ("staged destroy and replacement still read committed Entity", () => {
                var w = new World(); var old = w.CreateEntity("A"); Guid id = w.GetUuid(old);
                Guid replacement = Guid.NewGuid();
                w.BeginStep(); w.StageDestroy(id); w.StageCreate(replacement, "replacement"); var candidate = w.CandidateEntity(replacement);
                Check(w.TryFindEntity(id, out var e) && e == old && !w.TryFindEntity(replacement, out _)); w.AbortStep();
                Check(w.TryFindEntity(id, out e) && e == old);
                w.BeginStep(); w.StageDestroy(id); w.CommitStep();
                Check(!w.TryFindEntity(id, out e) && e == default); Reject(() => w.Resolve(candidate));
            }),
            ("read-only works and foreign thread never becomes not-found", () => {
                var w = new World(); var e = w.CreateEntity("A"); Guid id = w.GetUuid(e);
                using (w.ReadOnly()) Check(w.TryFindEntity(id, out var found) && found == e);
                Task.Run(() => { Reject(() => w.TryFindEntity(id, out _)); Reject(() => w.TryFindEntity(Guid.Empty, out _)); }).GetAwaiter().GetResult();
            }),
            ("registration is not membership or write permission", () => {
                var r = ComponentRegistry.CreateDefault(); var w = new World(components: r);
                Check(r.IsRegistered<TransformData>() && !r.IsRegistered<Unknown>() && w.Query<TransformData>().Count == 0);
                var e = w.CreateEntity("empty"); Check(!w.Has<TransformData>(e) && r.IsRegistered<TransformData>());
            }),
            ("warm UUID lookups allocate no per-object list", () => {
                var w = new World(); var e = w.CreateEntity("A"); Guid id = w.GetUuid(e), absent = Guid.NewGuid();
                for (int i = 0; i < 512; i++) { w.TryFindEntity(id, out _); w.TryFindEntity(absent, out _); }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 4096; i++) { Check(w.TryFindEntity(id, out var found) && found == e); Check(!w.TryFindEntity(absent, out _)); }
                Check(GC.GetAllocatedBytesForCurrentThread() == before); // Lookup-only, not whole frame/performance acceptance.
            })
        };
        int failed = 0;
        foreach (var test in cases) try { test.Run(); Console.WriteLine("PASS E4-B lookup " + test.Name); }
            catch (Exception e) { failed++; Console.Error.WriteLine("FAIL E4-B lookup " + test.Name + ": " + e); }
        Console.WriteLine($"E4-B lookup: {cases.Length - failed}/{cases.Length} passed."); return failed;
    }
}
