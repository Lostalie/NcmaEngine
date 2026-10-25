using Ncma;
using Ncma.Scene;
using Ncma.Scripting;

internal static class GeneratedExportCatalogTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Generated Export catalog assertion"); }
    private static void Reject(Action action) {
        try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; }
        throw new Exception("Invalid generated Export accepted");
    }
    internal static IEnumerable<(string Name, Action Run)> Cases(string root)
    {
        string path = Path.Combine(root, "out/managed/Ncma.Gameplay.Sample.dll");
        yield return ("E6-B3-C actual generated sample uses original catalog and persisted names", () => {
            using var catalog = new ScriptCatalogService(); using var candidate = catalog.LoadCandidate(path);
            Check(catalog.Snapshot.Types.Length == 0 && candidate.Count == 1); catalog.CommitCandidate(candidate);
            var type = catalog.Snapshot.Types.Single();
            Check(type.Exports.Select(e => e.Name).SequenceEqual(new[] { "Clockwise", "DegreesPerSecond", "Multiplier" }));
            Check(type.Exports.Select(e => e.DefaultValue).SequenceEqual(new[] { 1d, 45d, 1d }));
            var binding = new BehaviourBindingData(Guid.NewGuid(), type.TypeName, true, [new("DegreesPerSecond", ExportKind.Float, 90), new("Multiplier", ExportKind.Integer, 2), new("Clockwise", ExportKind.Boolean, 0)]);
            var instance = catalog.Instantiate(binding); Check(instance is IGeneratedExportSource);
            var access = ((IGeneratedExportSource)instance).DescribeGeneratedExports();
            Check(access.Single(a => a.Name == "DegreesPerSecond").Read(instance) == 90 && access.Single(a => a.Name == "Multiplier").Read(instance) == 2);
            Check(access.All(a => a.Name != "ElapsedSeconds") && type.Exports.All(e => e.Name != "ElapsedSeconds"));
            access[0] = access[0] with { Name = "changed" }; Check(catalog.Snapshot.Types.Single().Exports[0].Name == "Clockwise");
            using (catalog.AcquireLease()) Reject(catalog.Clear);
        });
        yield return ("E6-B3-C original binding validation rejects wrong kind range and hidden script state", () => {
            using var catalog = new ScriptCatalogService(); using var candidate = catalog.LoadCandidate(path); catalog.CommitCandidate(candidate);
            var type = catalog.Snapshot.Types.Single(); var binding = new BehaviourBindingData(Guid.NewGuid(), type.TypeName, true, []);
            var generation = catalog.Snapshot.Generation;
            foreach (var bad in new[] { new ExportData("Clockwise", ExportKind.Boolean, 2), new("Multiplier", ExportKind.Integer, 1.5),
                new("Multiplier", ExportKind.Integer, (double)int.MaxValue + 1), new("DegreesPerSecond", ExportKind.Float, double.MaxValue),
                new("DegreesPerSecond", ExportKind.Float, double.NaN), new("DegreesPerSecond", ExportKind.Double, 1), new("ElapsedSeconds", ExportKind.Double, 1) })
                Reject(() => catalog.Instantiate(binding with { Exports = [bad] }));
            Check(catalog.Snapshot.Generation == generation);
            var instance = catalog.Instantiate(binding); var access = ((IGeneratedExportSource)instance).DescribeGeneratedExports();
            Check(access.Single(a => a.Name == "DegreesPerSecond").Read(instance) == 45);
            Check(Task.Run(() => { try { catalog.Instantiate(binding); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult());
        });
        yield return ("E6-B3-C generated candidates preserve original foreign disposal and reload boundaries", () => {
            using var catalog = new ScriptCatalogService(); using var other = new ScriptCatalogService(); using var first = catalog.LoadCandidate(path);
            Reject(() => other.CommitCandidate(first)); catalog.CommitCandidate(first); var generation = catalog.Snapshot.Generation;
            using (var unused = catalog.LoadCandidate(path)) { Check(unused.Count == 1); }
            Check(catalog.Snapshot.Generation == generation);
            using var next = catalog.LoadCandidate(path); catalog.CommitCandidate(next); Check(catalog.Snapshot.Generation != generation);
            Reject(() => _ = next.Count); catalog.Clear(); Check(catalog.Snapshot.Types.Length == 0);
        });
    }
}
