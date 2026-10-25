using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Ncma.CodeGen;

internal static class ExportGeneratorTests
{
    private const string Source = """
        using Ncma;
        namespace Fixture;
        [GenerateExports] public sealed partial class Probe : Behaviour {
            [Export(DisplayName="速率", Category="Motion")] public float Speed {get;set;} = 3.5f;
            [Export] public double Duration = 2;
            [Export] public int Count {get;set;} = 4;
            [Export] public bool Enabled {get;set;} = true;
            private int Secret = 9;
            public int ReadSecret() => Secret;
        }
        """;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    internal static IEnumerable<(string Name, Action Run)> Cases(MetadataReference[] references, CSharpParseOptions parse)
    {
        CSharpCompilation Compile(string source) => CSharpCompilation.Create("ExportFixture", [CSharpSyntaxTree.ParseText(source, parse)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver Driver() => CSharpGeneratorDriver.Create([new ExportGenerator().AsSourceGenerator()], parseOptions: parse);
        string[] Texts(GeneratorDriver driver) => driver.GetRunResult().Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()).ToArray();
        void Valid(GeneratorDriver driver, Compilation output) {
            var errors = driver.GetRunResult().Diagnostics.Concat(output.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Check(errors.Length == 0, string.Join("\n", errors.Select(e => e.ToString())));
        }
        yield return ("B3-C four numeric kinds generate direct access only for declared public exports", () => {
            var driver = Driver().RunGeneratorsAndUpdateCompilation(Compile(Source), out var output, out _); Valid(driver, output);
            var text = Texts(driver).Single();
            Check(!text.Contains("Secret") && !text.Contains("Reflection") && text.Contains("ExportNumbers.Require(number, 4u)"), "Private access or unchecked setter");
            using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Check(emitted.Success, string.Join("\n", emitted.Diagnostics));
            var context = new System.Runtime.Loader.AssemblyLoadContext("generated-export-test", isCollectible: true);
            try {
                bytes.Position = 0; var type = context.LoadFromStream(bytes).GetType("Fixture.Probe")!;
                var instance = (Ncma.Behaviour)Activator.CreateInstance(type)!;
                var fields = ((Ncma.IGeneratedExportSource)instance).DescribeGeneratedExports();
                Check(fields.Select(f => f.Name).SequenceEqual(new[] { "Count", "Duration", "Enabled", "Speed" }), "Non deterministic order");
                foreach (var f in fields) { double value = f.Kind == 4 ? 0 : 12; f.Write(instance, value); Check(f.Read(instance) == value, "Typed setter mismatch"); }
                foreach (var bad in new[] { ("Count", 1.5), ("Enabled", 2d), ("Speed", double.MaxValue), ("Duration", double.NaN) }) {
                    var field = fields.Single(f => f.Name == bad.Item1); double before = field.Read(instance); bool rejected = false;
                    try { field.Write(instance, bad.Item2); } catch (ArgumentException) { rejected = true; }
                    Check(rejected && field.Read(instance) == before, "Invalid numeric mutation");
                }
            } finally { context.Unload(); }
        });
        yield return ("B3-C unsupported shapes and members fail closed without generated fallback", () => {
            foreach (var bad in new[] {
                Source.Replace("sealed partial", "sealed"), Source.Replace("sealed partial", "partial"),
                Source.Replace("class Probe", "class Probe<T>"), Source.Replace("float Speed", "string Speed").Replace("3.5f", "\"bad\""),
                Source.Replace("get;set;", "get;init;"), Source.Replace("double Duration", "readonly double Duration"),
                Source.Replace("double Duration", "static double Duration"), Source.Replace("double Duration", "private double Duration").Replace("public private", "private"),
                Source.Replace("速率", new string('界', 43)), Source.Replace("public float Speed", "public required float Speed") }) {
                var driver = Driver().RunGenerators(Compile(bad));
                Check(driver.GetRunResult().Diagnostics.Any(d => d.Id == "NCMA101") && Texts(driver).Length == 0, "Invalid Export accepted");
            }
        });
        yield return ("B3-C same driver metadata changes removal and invalid edits discard stale source", () => {
            var driver = Driver().RunGenerators(Compile(Source));
            var changed = Source.Replace("Speed", "Velocity").Replace("Motion", "Movement");
            driver = driver.RunGeneratorsAndUpdateCompilation(Compile(changed), out var output, out _); Valid(driver, output);
            Check(Texts(driver).Single().Contains("Velocity") && !Texts(driver).Single().Contains("Speed"), "Stale Export name");
            driver = driver.RunGenerators(Compile(changed.Replace("sealed partial", "sealed"))); Check(Texts(driver).Length == 0, "Invalid edit retained output");
            driver = driver.RunGenerators(Compile(Source.Replace("[GenerateExports]", ""))); Check(Texts(driver).Length == 0, "Removed marker retained output");
        });
        yield return ("B3-C emitted contract is deterministic and empty declaration is valid", () => {
            var first = Driver().RunGenerators(Compile(Source)); var second = Driver().RunGenerators(Compile(Source));
            Check(Texts(first).SequenceEqual(Texts(second)), "Nondeterministic generation");
            var empty = Driver().RunGeneratorsAndUpdateCompilation(Compile("[Ncma.GenerateExports] public sealed partial class Empty : Ncma.Behaviour {}"), out var output, out _);
            Valid(empty, output); Check(Texts(empty).Length == 1, "Empty Export class missing");
        });
    }
}
