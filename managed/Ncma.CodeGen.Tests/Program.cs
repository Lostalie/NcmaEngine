using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Ncma.CodeGen;
using Ncma.Runtime;

if (args.Length != 0) return await BuildContractTests.Run(args);

// Registered for whole-E6 validation; building this executable does not run it.
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
    .Append(typeof(IComponent).Assembly.Location).Append(typeof(Ncma.Behaviour).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase)
    .Select(path => MetadataReference.CreateFromFile(path)).ToArray();
var parse = new CSharpParseOptions(LanguageVersion.CSharp12);
CSharpCompilation Compile(params string[] sources) => CSharpCompilation.Create("GeneratedFixture",
    sources.Select((s, i) => CSharpSyntaxTree.ParseText(s, parse, "Fixture" + i + ".cs")), references,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
GeneratorDriver Driver() => CSharpGeneratorDriver.Create([new ComponentGenerator().AsSourceGenerator()], parseOptions: parse);
static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static string Source(string name = "Position", string id = "fixture.position") => $$$$"""
    using Ncma.Runtime;
    namespace Fixture;
    [GenerateComponent("{{{{id}}}}", 1, "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"x\"],\"properties\":{\"x\":{\"type\":\"number\"}}}", nameof(Validate), nameof(Default))]
    [ComponentField("value.x", "X", "x")]
    public readonly partial record struct {{{{name}}}}(float X) : IComponent
    {
        public static {{{{name}}}} Validate({{{{name}}}} value) => value;
        public static {{{{name}}}} Default => new(0);
    }
    """;
static string[] Generated(GeneratorDriver driver) => driver.GetRunResult().Results.SelectMany(r => r.GeneratedSources)
    .Where(s => s.HintName.EndsWith(".Component.g.cs", StringComparison.Ordinal))
    .OrderBy(s => s.HintName, StringComparer.Ordinal).Select(s => s.HintName + "\n" + s.SourceText).ToArray();
void Valid(GeneratorDriver driver, Compilation output)
{
    var errors = output.GetDiagnostics().Concat(driver.GetRunResult().Diagnostics).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
    Check(errors.Length == 0, string.Join("\n", errors.Select(e => e.ToString())));
}
void Diagnostic(string source, string expected)
{
    var driver = Driver().RunGenerators(Compile(source));
    Check(driver.GetRunResult().Diagnostics.Any(d => d.Id == expected), "Missing " + expected);
    Check(Generated(driver).Length == 0, "Rejected declaration emitted usable source");
}
var cases = new (string Name, Action Run)[] {
    ("B2 explicit field IDs paths flags and coverage fail closed", () => {
        const string field = "[ComponentField(\"value.x\", \"X\", \"x\")]";
        Diagnostic(Source().Replace(field, ""), "NCMA006");
        Diagnostic(Source().Replace(field, field + "\n" + field), "NCMA006");
        Diagnostic(Source().Replace("\"value.x\", \"X\"", "\"value.x\", \"Missing\""), "NCMA006");
        Diagnostic(Source().Replace("\"X\", \"x\")", "\"X\", \"x\", Flags=(ComponentFieldFlags)9)"), "NCMA006");
        Diagnostic(Source().Replace("\"X\", \"x\")", "\"X\", \"x\", Flags=(ComponentFieldFlags)0)"), "NCMA006");
        Diagnostic(Source().Replace("value.x", new string('x', 129)), "NCMA006");
    }),
    ("B2 compile schema checks include type closed-shape required and custom serializer policy", () => {
        Diagnostic(Source().Replace("\\\"number\\\"", "\\\"integer\\\""), "NCMA006");
        Diagnostic(Source().Replace("\\\"additionalProperties\\\":false", "\\\"additionalProperties\\\":true"), "NCMA006");
        Diagnostic(Source().Replace("\\\"required\\\":[\\\"x\\\"]", "\\\"required\\\":[]"), "NCMA006");
        Diagnostic(Source().Replace("float X", "[property: System.Text.Json.Serialization.JsonPropertyName(\"y\")] float X"), "NCMA006");
    }),
    ("B2 rename and policy edits retain explicit FieldId and invalidate generated metadata", () => {
        var driver = Driver().RunGenerators(Compile(Source()));
        var renamed = Source().Replace("float X", "float Y").Replace("\"X\", \"x\"", "\"Y\", \"y\"").Replace("\\\"x\\\"", "\\\"y\\\"");
        driver = driver.RunGeneratorsAndUpdateCompilation(Compile(renamed), out var output, out _); Valid(driver, output);
        string generated = Generated(driver).Single();
        Check(generated.Contains("\"value.x\", \"Y\", \"y\"") && !generated.Contains("\"value.x\", \"X\""), "ID renamed implicitly or stale member mapping");
        driver = driver.RunGeneratorsAndUpdateCompilation(Compile(renamed.Replace("\"Y\", \"y\")", "\"Y\", \"y\", Flags=(ComponentFieldFlags)15)")), out output, out _); Valid(driver, output);
        Check(Generated(driver).Single().Contains("ComponentFieldFlags)15"), "Stale usage policy");
    }),
    ("B2 implicit converter types and readonly storage have no silent metadata fallback", () => {
        Diagnostic(Source().Replace("float X", "System.DateTime X").Replace("new(0)", "default"), "NCMA006");
        Diagnostic(Source().Replace("record struct Position(float X)", "struct Position").Replace("public static Position Validate", "public readonly float X; public static Position Validate").Replace("new(0)", "default"), "NCMA006");
    }),
    ("positive generation compiles and remains deterministic under source reordering", () => {
        var one = Driver().RunGeneratorsAndUpdateCompilation(Compile(Source(), Source("Scale", "fixture.scale")), out var output, out _); Valid(one, output);
        var two = Driver().RunGeneratorsAndUpdateCompilation(Compile(Source("Scale", "fixture.scale"), Source()), out output, out _); Valid(two, output);
        Check(Generated(one).SequenceEqual(Generated(two)), "Non-deterministic output");
        Check(Generated(one).Length == 2 && Generated(one).All(s => s.Contains("context.Write(row, value)") && s.Contains("world.Set(entity, value)")), "Missing guarded adapters");
    }),
    ("same driver edits and removals cannot retain stale generated declarations", () => {
        var driver = Driver().RunGenerators(Compile(Source()));
        driver = driver.RunGeneratorsAndUpdateCompilation(Compile(Source(id: "fixture.changed")), out var output, out _); Valid(driver, output);
        Check(Generated(driver).Single().Contains("fixture.changed") && !Generated(driver).Single().Contains("fixture.position"), "Old ID retained");
        driver = driver.RunGenerators(Compile(Source(id: ""))); Check(Generated(driver).Length == 0 && driver.GetRunResult().Diagnostics.Any(d => d.Id == "NCMA001"), "Invalid declaration kept old output");
        driver = driver.RunGenerators(Compile("namespace Empty; public class Empty {}")); Check(Generated(driver).Length == 0, "Removed declaration retained");
    }),
    ("duplicate IDs diagnose every declaration without output", () => {
        var driver = Driver().RunGenerators(Compile(Source(), Source("Other")));
        Check(driver.GetRunResult().Diagnostics.Count(d => d.Id == "NCMA002") == 2 && Generated(driver).Length == 0, "Duplicate ID accepted");
    }),
    ("invalid explicit identity and schema fail closed", () => {
        Diagnostic(Source(id: ""), "NCMA001");
        Diagnostic(Source().Replace("\\\"object\\\"", "\\\"array\\\""), "NCMA005");
    }),
    ("unsupported managed entity pointer and nonpartial declarations rejected", () => {
        Diagnostic(Source().Replace("float X", "Entity X").Replace("new(0)", "default"), "NCMA003");
        Diagnostic(Source().Replace("float X", "string X").Replace("new(0)", "default"), "NCMA003");
        Diagnostic(Source().Replace("partial record", "record"), "NCMA003");
        Diagnostic(Source().Replace("float X", "nint X"), "NCMA003");
        Diagnostic(Source().Replace("float X", "Nested X").Replace("new(0)", "default") + "\npublic readonly record struct Nested(Entity Handle);", "NCMA003");
    }),
    ("validator signature default property and reserved generated name guarded", () => {
        Diagnostic(Source().Replace("Validate(Position value)", "Validate(int value)").Replace("=> value;", "=> default;"), "NCMA004");
        Diagnostic(Source().Replace("Default => new(0)", "Default() => new(0)"), "NCMA004");
        Diagnostic(Source().Replace("public static Position Default", "public static int Generated => 0; public static Position Default"), "NCMA004");
    }),
};
cases = cases.Concat(ExportGeneratorTests.Cases(references, parse))
    .Concat(ComponentIdentityTests.Cases(references, parse))
    .Concat(FieldContractTests.Cases(references, parse))
    .Concat(CompositionTests.Cases(references, parse)).ToArray();
int failures = 0;
foreach (var test in cases) {
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + error); }
}
Console.WriteLine($"CodeGen: {cases.Length - failures}/{cases.Length}");
return failures == 0 ? 0 : 1;
