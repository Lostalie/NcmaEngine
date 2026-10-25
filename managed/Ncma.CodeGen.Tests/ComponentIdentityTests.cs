using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Ncma.CodeGen;

internal static class ComponentIdentityTests
{
    // Whole-E6 test entry only. These dynamically compiled fixtures are NOT run by dotnet build.
    internal static IEnumerable<(string Name, Action Run)> Cases(MetadataReference[] references, CSharpParseOptions parse)
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        CSharpCompilation Compile(string name, string source, params MetadataReference[] extra) => CSharpCompilation.Create(name,
            [CSharpSyntaxTree.ParseText(source, parse)], references.Concat(extra),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver Driver() => CSharpGeneratorDriver.Create([new ComponentGenerator().AsSourceGenerator()], parseOptions: parse);
        void Valid(GeneratorDriver driver, Compilation output) {
            var errors = driver.GetRunResult().Diagnostics.Concat(output.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Check(errors.Length == 0, string.Join("\n", errors.Select(d => d.ToString())));
        }
        PortableExecutableReference Library(string name, string source, bool generated = true, params MetadataReference[] extra) {
            Compilation output = Compile(name, source, extra);
            if (generated) { var driver = Driver().RunGeneratorsAndUpdateCompilation(output, out output, out _); Valid(driver, output); }
            using var bytes = new MemoryStream(); var result = output.Emit(bytes);
            Check(result.Success, string.Join("\n", result.Diagnostics));
            return MetadataReference.CreateFromImage(bytes.ToArray());
        }
        static string[] Texts(GeneratorDriver driver) => driver.GetRunResult().Results.SelectMany(r => r.GeneratedSources)
            .OrderBy(s => s.HintName, StringComparer.Ordinal).Select(s => s.HintName + "\n" + s.SourceText).ToArray();

        yield return ("B4 local component cannot reuse a referenced persistent ID across versions", () => {
            var lib = Library("IdentityLocalDependency", Source("First", "fixture.shared"));
            var driver = Driver().RunGenerators(Compile("IdentityLocal", Source("Second", "fixture.shared", 2), lib));
            Check(driver.GetRunResult().Diagnostics.Any(d => d.Id == "NCMA007" && d.GetMessage().Contains("fixture.shared")), "Local/reference collision not rejected");
            Check(Texts(driver).Length == 0, "Conflicting local component emitted usable source");
        });
        yield return ("B4 composition-only consumer rejects reference/reference conflict deterministically", () => {
            var first = Library("IdentityLeft", Source("First", "fixture.shared"));
            var second = Library("IdentityRight", Source("Second", "fixture.shared", 2));
            string[] Diagnostics(params MetadataReference[] refs) => Driver().RunGenerators(Compile("Composition", "", refs))
                .GetRunResult().Diagnostics.Where(d => d.Id == "NCMA007").Select(d => d.ToString()).ToArray();
            var forward = Diagnostics(first, second); var reverse = Diagnostics(second, first);
            Check(forward.Length == 1 && forward.SequenceEqual(reverse), "Missing/unstable composition conflict");
        });
        yield return ("B4 diamond references reuse one identity and distinct IDs with equal leaf names coexist", () => {
            var leaf = Library("IdentityLeaf", Source("SameName", "fixture.left"));
            var other = Library("IdentityOther", Source("SameName", "fixture.right").Replace("namespace Fixture;", "namespace Other;"));
            var facade = Library("IdentityFacade", "public class Facade { public Fixture.SameName Value; }", true, leaf);
            var driver = Driver().RunGeneratorsAndUpdateCompilation(Compile("IdentityDiamond", "public class Consumer { public Facade F = new(); }", facade, leaf, other), out var output, out _);
            Valid(driver, output);
            Check(Texts(driver).Length == 1 && Texts(driver)[0].StartsWith("Assembly.ComponentModule.g.cs", StringComparison.Ordinal),
                "Composition-only consumer must emit one dependency aggregate, not duplicate per-component registrations");
        });
        yield return ("B4 malformed manifests fail closed without generator crashes", () => {
            var sources = new[] {
                "[assembly: Ncma.Runtime.GeneratedComponentIdentity(\"bad\", 1, typeof(int))]",
                "[assembly: Ncma.Runtime.GeneratedComponentIdentity(\"bad\", 1, typeof(Plain))] public struct Plain {}",
                "[assembly: Ncma.Runtime.GeneratedComponentIdentity(\"wrong.id\", 1, typeof(Fixture.First))]\n" + Source("First", "fixture.first"),
                "[assembly: Ncma.Runtime.GeneratedComponentIdentity(\"fixture.first\", 2, typeof(Fixture.First))]\n" + Source("First", "fixture.first"),
                "[assembly: Ncma.Runtime.GeneratedComponentIdentity(\"fixture.first\", 1, typeof(Fixture.First))]\n" +
                "[assembly: Ncma.Runtime.GeneratedComponentIdentity(\"fixture.second\", 1, typeof(Fixture.First))]\n" + Source("First", "fixture.first")
            };
            // Different attribute blobs survive C# emit; byte-identical assembly attributes are coalesced.
            string[] names = ["foreign-type", "missing-declaration", "id-mismatch", "version-mismatch", "duplicate-owned-type"];
            for (int i = 0; i < sources.Length; ++i) {
                Console.WriteLine($"MANIFEST case={names[i]} construction={System.Text.Json.JsonSerializer.Serialize(sources[i])}");
                var lib = Library("Malformed" + i, sources[i], false);
                var compilation = Compile("InvalidConsumer", "", lib);
                var assembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(lib)!;
                int entries = assembly.GetAttributes().Count(a => a.AttributeClass?.ToDisplayString() == "Ncma.Runtime.GeneratedComponentIdentityAttribute");
                Console.WriteLine($"MANIFEST case={names[i]} emittedIdentityEntries={entries}");
                Check(entries == (i == 4 ? 2 : 1), "Malformed identity construction lost an attribute");
                var driver = Driver().RunGenerators(compilation);
                Console.WriteLine($"MANIFEST case={names[i]} diagnostics={string.Join(" | ", driver.GetRunResult().Diagnostics)}");
                Check(driver.GetRunResult().Diagnostics.Any(d => d.Id == "NCMA008"), "Malformed manifest accepted: " + i);
                Check(driver.GetRunResult().Results.All(r => r.Exception is null), "Generator crashed on malformed input");
            }
        });
        yield return ("B4 module manifest arguments ownership and provider fail closed", () => {
            const string property = "public static Ncma.Runtime.ComponentModule Module => new(\"fixture\", [], _ => {});";
            string Manifest(string expression, string body = "") => "[assembly: Ncma.Runtime.GeneratedComponentModule(" + expression + ")]\n" + body;
            var samples = new (string Name, string Source, string Reason)[] {
                ("null-argument", Manifest("null"), "arguments"),
                ("array-argument", Manifest("typeof(int[])"), "arguments"),
                ("foreign-provider", Manifest("typeof(Ncma.Runtime.Generated.ComponentModule)"), "ownership"),
                ("internal-provider", Manifest("typeof(Provider)", "internal static class Provider { " + property + " }"), "provider"),
                ("missing-property", Manifest("typeof(Provider)", "public static class Provider {}"), "provider"),
                ("instance-property", Manifest("typeof(Provider)", "public class Provider { " + property.Replace(" static", "") + " }"), "provider"),
                ("private-getter", Manifest("typeof(Provider)", "public class Provider { public static Ncma.Runtime.ComponentModule Module { private get; set; } }"), "provider"),
                ("wrong-return", Manifest("typeof(Provider)", "public static class Provider { public static int Module => 0; }"), "provider"),
                ("open-generic-provider", Manifest("typeof(Provider<>)", "public static class Provider<T> { " + property + " }"), "provider"),
                ("nested-internal-container", Manifest("typeof(Container.Provider)", "internal class Container { public static class Provider { " + property + " } }"), "provider"),
                ("nested-generic-container", Manifest("typeof(Container<>.Provider)", "public class Container<T> { public static class Provider { " + property + " } }"), "provider"),
                // Malformed metadata shapes need a synthetic declaration: the real Type constructor
                // cannot emit a zero/two/string-argument attribute through valid C# source.
                ("zero-arguments", "[assembly: Ncma.Runtime.GeneratedComponentModule] namespace Ncma.Runtime { public sealed class GeneratedComponentModuleAttribute : System.Attribute {} }", "arguments"),
                ("two-arguments", "[assembly: Ncma.Runtime.GeneratedComponentModule(1, 2)] namespace Ncma.Runtime { public sealed class GeneratedComponentModuleAttribute(int a, int b) : System.Attribute {} }", "arguments"),
                ("string-argument", "[assembly: Ncma.Runtime.GeneratedComponentModule(\"Provider\")] namespace Ncma.Runtime { public sealed class GeneratedComponentModuleAttribute(string name) : System.Attribute {} }", "arguments")
            };
            foreach (var sample in samples) {
                Console.WriteLine($"MODULE case={sample.Name} construction={System.Text.Json.JsonSerializer.Serialize(sample.Source)}");
                var lib = Library("Module_" + sample.Name.Replace('-', '_'), sample.Source, false);
                var driver = Driver().RunGenerators(Compile("ModuleConsumer", "", lib));
                var result = driver.GetRunResult();
                Console.WriteLine($"MODULE case={sample.Name} diagnostics={string.Join(" | ", result.Diagnostics)}");
                Check(result.Results.All(r => r.Exception is null), "Module manifest crashed generator: " + sample.Name);
                Check(result.Diagnostics.Any(d => d.Id == "NCMA009" &&
                    d.GetMessage().StartsWith("Invalid component module manifest " + sample.Reason + ": ", StringComparison.Ordinal)),
                    "Module validation branch missed: " + sample.Name);
                Check(Texts(driver).Length == 0, "Invalid module emitted aggregate: " + sample.Name);
            }
            foreach (var (name, body) in new[] {
                ("PublicProvider", "public static class Provider { " + property + " }"),
                ("NestedProvider", "public class Container { public static class Provider { " + property + " } }")
            }) {
                string type = name == "PublicProvider" ? "Provider" : "Container.Provider";
                var lib = Library(name, Manifest("typeof(" + type + ")", body), false);
                var driver = Driver().RunGeneratorsAndUpdateCompilation(Compile("ValidModuleConsumer", "", lib), out var output, out _);
                Valid(driver, output);
                Check(Texts(driver).Single().Contains("global::" + type + ".Module"), "Valid provider missing from aggregate");
            }
        });
        yield return ("B4 reference replacement removal and restored clean generation invalidate persistent driver state", () => {
            var safe = Library("IdentityChanging", Source("Dependency", "fixture.other"));
            var collision = Library("IdentityChanging", Source("Dependency", "fixture.local"));
            var baseline = Compile("IdentityChangingConsumer", Source("Local", "fixture.local"), safe);
            var driver = Driver().RunGeneratorsAndUpdateCompilation(baseline, out var output, out _); Valid(driver, output);
            var clean = Texts(driver);
            driver = driver.RunGenerators(Compile("IdentityChangingConsumer", Source("Local", "fixture.local"), collision));
            Check(driver.GetRunResult().Diagnostics.Any(d => d.Id == "NCMA007") && Texts(driver).Length == 0, "Changed reference retained stale source");
            var removed = Compile("IdentityChangingConsumer", Source("Local", "fixture.local"));
            driver = driver.RunGeneratorsAndUpdateCompilation(removed, out output, out _); Valid(driver, output);
            var rebuilt = Driver().RunGeneratorsAndUpdateCompilation(removed, out var rebuiltOutput, out _); Valid(rebuilt, rebuiltOutput);
            Check(Texts(driver).SequenceEqual(Texts(rebuilt)), "Reference removal differs from a fresh compilation/driver");
            Check(!Texts(driver).SequenceEqual(clean) && Texts(driver).All(t => !t.Contains("IdentityChanging.Generated.ComponentModule.Module")),
                "Removed dependency still appears in generated aggregate");
            Check(clean.Where(t => !t.StartsWith("Assembly.ComponentModule.g.cs", StringComparison.Ordinal)).SequenceEqual(
                Texts(driver).Where(t => !t.StartsWith("Assembly.ComponentModule.g.cs", StringComparison.Ordinal))), "Reference removal changed unrelated component output");
            driver = driver.RunGeneratorsAndUpdateCompilation(baseline, out output, out _); Valid(driver, output);
            var fresh = Driver().RunGenerators(baseline);
            Check(Texts(driver).SequenceEqual(Texts(fresh)), "Clean and reused driver differ");
        });
    }

    private static string Source(string name, string id, int version = 1) => $$$$"""
        namespace Fixture;
        [Ncma.Runtime.GenerateComponent("{{{{id}}}}", {{{{version}}}}, "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"x\"],\"properties\":{\"x\":{\"type\":\"number\"}}}", nameof(Validate), nameof(Default))]
        [Ncma.Runtime.ComponentField("value.x", "X", "x")]
        public readonly partial record struct {{{{name}}}}(float X) : Ncma.Runtime.IComponent
        {
            public static {{{{name}}}} Validate({{{{name}}}} value) => value;
            public static {{{{name}}}} Default => new(0);
        }
        """;
}
