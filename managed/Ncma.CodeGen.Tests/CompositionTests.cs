using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Ncma.CodeGen;
using Ncma.Runtime;

internal static class CompositionTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases(MetadataReference[] references, CSharpParseOptions parse)
    {
        CSharpCompilation Compile(string source) => CSharpCompilation.Create("AggregationFixture",
            [CSharpSyntaxTree.ParseText(source, parse)], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver Driver() => CSharpGeneratorDriver.Create([new ComponentGenerator().AsSourceGenerator()], parseOptions: parse);
        static string Aggregate(GeneratorDriver driver) => driver.GetRunResult().Results.SelectMany(r => r.GeneratedSources)
            .Single(s => s.HintName == "Assembly.ComponentModule.g.cs").SourceText.ToString();
        static void Valid(GeneratorDriver driver, Compilation output) {
            var errors = driver.GetRunResult().Diagnostics.Concat(output.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Check(errors.Length == 0, string.Join("\n", errors.Select(d => d.ToString())));
        }
        yield return ("assembly aggregate includes generated and explicit handwritten registrations and invalidates removal", () => {
            var driver = Driver().RunGeneratorsAndUpdateCompilation(Compile(Source + Manual), out var output, out _); Valid(driver, output);
            string initial = Aggregate(driver);
            Check(initial.Contains("global::Fixture.Value.Generated.Register(registry);") && initial.Contains("global::Fixture.Manual.@Register(registry);"), "Missing local registration");
            driver = driver.RunGeneratorsAndUpdateCompilation(Compile(Manual + Source), out output, out _); Valid(driver, output);
            Check(initial == Aggregate(driver), "Order depends on declaration layout");
            driver = driver.RunGeneratorsAndUpdateCompilation(Compile(Source), out output, out _); Valid(driver, output);
            Check(!Aggregate(driver).Contains("Fixture.Manual"), "Removed manual registration survived");
            driver = driver.RunGeneratorsAndUpdateCompilation(Compile(""), out output, out _); Valid(driver, output);
            Check(!Aggregate(driver).Contains("Fixture.Value") && Aggregate(driver).Contains(".Module"), "Empty composition must retain only referenced module roots");
        });
        yield return ("assembly aggregate rejects malformed explicit registration without emitting a partial module", () => {
            foreach (string source in new[] { Manual.Replace("static void Register", "void Register"),
                Manual.Replace("static void Register", "static void Register<T>"), Manual.Replace("Ncma.Runtime.ComponentRegistry registry", "int registry").Replace("registry.Register<ManualValue>(\"fixture.manual\",1,\"{}\",v=>v);", ""),
                Manual.Replace("public static void Register", "private static void Register") }) {
                var driver = Driver().RunGenerators(Compile(source));
                Check(driver.GetRunResult().Diagnostics.Any(d => d.Id == "NCMA009"), "Missing aggregate diagnostic");
                Check(!driver.GetRunResult().Results.SelectMany(r => r.GeneratedSources).Any(s => s.HintName == "Assembly.ComponentModule.g.cs"), "Malformed aggregate emitted");
            }
        });
        yield return ("full engine composition owns all 19 declarations with original metadata and attachment policy", () => {
            var registry = CompositionRegistry.Build(Ncma.Characters.Generated.ComponentModule.Module, Ncma.Rendering.Generated.ComponentModule.Module);
            string[] ids = ["ncma.transform", "ncma.animation.clip_playback", "ncma.animation.root_motion", "ncma.action.definition", "ncma.animation.animator",
                "ncma.render.post_process", "ncma.scene.ui", "ncma.render.shadow", "ncma.render.environment", "ncma.render.static_mesh", "ncma.render.skinned_mesh",
                "ncma.render.camera", "ncma.render.directional_light", "ncma.render.material_override", "ncma.character.capsule", "ncma.physics.box", "ncma.camera.follow",
                "ncma.combat.health", "ncma.render.configuration"];
            Check(registry.Describe().Select(d => d.TypeId).SequenceEqual(ids.Order(StringComparer.Ordinal)), "Composition has missing/extra/duplicate types");
            Check(registry.Describe().All(d => d.Version == 1), "Version changed");
            Check(registry.Describe().Count(d => d.Metadata is not null) == 4, "Handwritten schemas must not acquire invented defaults/metadata");
            Check(registry.Describe("ncma.transform").Metadata!.Fields.Count == 13 && registry.Describe("ncma.character.capsule").Metadata!.Fields.Count == 13 &&
                registry.Describe("ncma.render.camera").Metadata!.Fields.Count == 10 && registry.Describe("ncma.render.directional_light").Metadata!.Fields.Count == 7, "Generated metadata changed");
            foreach (var type in new[] { typeof(Ncma.Characters.CharacterData), typeof(Ncma.Characters.BoxColliderData), typeof(Ncma.Characters.FollowCameraData), typeof(Ncma.Characters.HealthData),
                typeof(Ncma.Scene.Rendering.SceneUiData), typeof(Ncma.Scene.Rendering.SceneShadowData), typeof(Ncma.Animation.AnimatorData), typeof(Ncma.Animation.ActionDefinitionData) }) {
                bool rejected = false;
                try { registry.VerifyRuntimeAttachment(type); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "Lost runtime attachment boundary: " + type);
            }
            Check(CompositionRegistry.Build().Describe().Count == 1, "Core-only composition unexpectedly references higher layers");
        });
    }

    private const string Source = """
        namespace Fixture {
        [Ncma.Runtime.GenerateComponent("fixture.generated",1,"{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"x\"],\"properties\":{\"x\":{\"type\":\"number\"}}}",nameof(Validate),nameof(Default))]
        [Ncma.Runtime.ComponentField("x","X","x")]
        public readonly partial record struct Value(float X):Ncma.Runtime.IComponent {
            public static Value Validate(Value value)=>value;
            public static Value Default=>default;
        }}
        """;
    private const string Manual = """
        namespace Fixture {
        public readonly record struct ManualValue(int X):Ncma.Runtime.IComponent;
        public class Manual {
            [Ncma.Runtime.ComponentRegistration]
            public static void Register(Ncma.Runtime.ComponentRegistry registry) {
                registry.Register<ManualValue>("fixture.manual",1,"{}",v=>v);
            }
        }}
        """;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
}
