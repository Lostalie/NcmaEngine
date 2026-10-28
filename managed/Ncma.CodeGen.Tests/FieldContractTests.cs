using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Ncma.CodeGen;
using Ncma.Runtime;

internal static class FieldContractTests
{
    private const string Marker = "Ncma.Runtime.GenerateComponentAttribute";
    private static readonly JsonSerializerOptions Json = new() {
        IncludeFields = true, IgnoreReadOnlyProperties = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    internal static IEnumerable<(string Name, Action Run)> Cases(MetadataReference[] references, CSharpParseOptions parse)
    {
        yield return ("field contract: every production GenerateComponent source agrees with runtime Prepare", () => Production(references, parse));
        yield return ("field contract: scalar rules nested members flags and ordinal ordering differential", () => Positive(references, parse));
        yield return ("field contract: ignored members and custom JSON rejection differential", () => Negative(references, parse));
    }

    private static CSharpCompilation Compile(string name, IEnumerable<SyntaxTree> trees, IEnumerable<MetadataReference> references) =>
        CSharpCompilation.Create(name, trees.Append(CSharpSyntaxTree.ParseText("global using System; global using System.Collections.Generic; global using System.Linq;")),
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, metadataImportOptions: MetadataImportOptions.All));

    private static void Production(MetadataReference[] references, CSharpParseOptions parse)
    {
        // Load the product closure explicitly. No plugin discovery or production registration.
        var assemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        void Visit(Assembly assembly)
        {
            string name = assembly.GetName().Name!;
            if (!name.StartsWith("Ncma.", StringComparison.Ordinal) || !assemblies.TryAdd(name, assembly)) return;
            foreach (var reference in assembly.GetReferencedAssemblies())
                if (reference.Name!.StartsWith("Ncma.", StringComparison.Ordinal)) Visit(Assembly.Load(reference));
        }
        Visit(typeof(Ncma.Characters.CharacterData).Assembly);
        Visit(typeof(TransformData).Assembly);
        // Optional production components must obey the same differential contract.
        // This is test enrollment only; default product composition stays unchanged.
        Visit(typeof(Ncma.Extensions.Hosting.ExtensionSampleTag).Assembly);
        var actual = assemblies.Values.SelectMany(a => a.GetTypes()).Where(t => t.IsDefined(typeof(GenerateComponentAttribute), false))
            .ToDictionary(t => t.Assembly.GetName().Name + ":" + t.FullName, StringComparer.Ordinal);
        Check(actual.Count != 0, "No generated components discovered.");
        var allReferences = references.Concat(assemblies.Values.Select(a => MetadataReference.CreateFromFile(a.Location)))
            .DistinctBy(r => r.Display, StringComparer.OrdinalIgnoreCase).ToArray();
        var sourceInventory = new HashSet<string>(StringComparer.Ordinal);
        string root = Repository();
        // Source inventory is intentionally wider than the referenced product closure: a new
        // component in an unreferenced module FAILS enrollment instead of silently escaping.
        foreach (string project in Directory.EnumerateDirectories(Path.Combine(root, "managed"), "Ncma.*")) {
            string projectName = Path.GetFileName(project);
            if (projectName.EndsWith(".Tests", StringComparison.Ordinal) || !File.Exists(Path.Combine(project, projectName + ".csproj"))) continue;
            var trees = Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
                .Where(p => !Path.GetRelativePath(project, p).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(s => s is "obj" or "bin"))
                .Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p), parse, p)).ToArray();
            // Resolve attributes semantically (including aliases/qualified names), not regex.
            var compilation = Compile(projectName, trees, allReferences.Where(r =>
                !string.Equals(Path.GetFileNameWithoutExtension(r.Display), projectName, StringComparison.Ordinal)));
            foreach (var tree in trees) {
                var model = compilation.GetSemanticModel(tree);
                foreach (var syntax in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>().Where(s => s.AttributeLists.Count != 0)) {
                    if (model.GetDeclaredSymbol(syntax) is not INamedTypeSymbol symbol || !symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == Marker)) continue;
                    string key = projectName + ":" + symbol.ToDisplayString();
                    if (!sourceInventory.Add(key)) continue; // Partial declarations share one model.
                    if (!actual.TryGetValue(key, out var type)) throw new Exception("Generated component missing from differential test project closure: " + key);
                    var declaration = type.GetCustomAttribute<GenerateComponentAttribute>()!;
                    var sourceDeclaration = symbol.GetAttributes().Single(a => a.AttributeClass?.ToDisplayString() == Marker);
                    Check((string?)sourceDeclaration.ConstructorArguments[2].Value == declaration.Schema, "Stale assembly schema: " + key);
                    Compare(ComponentFields.Read(symbol, declaration.Schema), Prepare(type, declaration.Schema), key);
                }
            }
        }
        Check(sourceInventory.SetEquals(actual.Keys), "Source/assembly generated component inventory differs; rebuild current sources.");
    }

    private static ComponentMetadata PrepareTyped<T>(string schema) where T : struct, IComponent
    {
        // Independent reflection declarations, NOT the generator's DescribeFields output.
        var declarations = typeof(T).GetCustomAttributes<ComponentFieldAttribute>().Select(a =>
            new ComponentFieldDefinition(a.FieldId, a.MemberPath, a.JsonPath, a.Flags)).Reverse().ToArray();
        using var document = JsonDocument.Parse(schema);
        return ComponentMetadata.Prepare<T>(declarations, document.RootElement).Capture(JsonSerializer.SerializeToElement(default(T), Json));
    }
    private static ComponentMetadata Prepare(Type type, string schema)
    {
        try { return (ComponentMetadata)typeof(FieldContractTests).GetMethod(nameof(PrepareTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type).Invoke(null, [schema])!; }
        catch (TargetInvocationException error) when (error.InnerException is not null) {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw;
        }
    }
    private static void Compare(ComponentFields.Field[] generated, ComponentMetadata runtime, string label)
    {
        Check(generated.Length == runtime.Fields.Count, label + ": field count");
        for (int i = 0; i < generated.Length; i++) {
            var a = generated[i]; var b = runtime.Fields[i];
            Check((a.Id, a.MemberPath, a.JsonPath, a.Kind, a.Flags) ==
                (b.FieldId, b.MemberPath, b.JsonPath, b.ValueKind, (int)b.Flags), label + ": field[" + i + "]");
            if (i != 0) Check(StringComparer.Ordinal.Compare(generated[i - 1].Id, a.Id) < 0, label + ": not ordinal order");
        }
    }

    private static string Schema(params (string Name, string Kind)[] fields) => JsonSerializer.Serialize(new {
        type = "object", additionalProperties = false, required = fields.Select(f => f.Name).ToArray(),
        properties = fields.ToDictionary(f => f.Name, f => new { type = f.Kind })
    });
    private static string Declaration(string id, string member, string path, int flags = 5) =>
        $"[ComponentField(\"{id}\",\"{member}\",\"{path}\",Flags=(ComponentFieldFlags){flags})]";

    private static void WithFixture(string source, MetadataReference[] references, CSharpParseOptions parse,
        Action<INamedTypeSymbol, Type> action)
    {
        string name = "FieldParity" + Guid.NewGuid().ToString("N");
        var compilation = Compile(name, [CSharpSyntaxTree.ParseText(source, parse)], references);
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        Check(emit.Success, string.Join("\n", emit.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext(name, isCollectible: true);
        try {
            var assembly = context.LoadFromStream(image);
            action(compilation.GetTypeByMetadataName("Fixture.Value")!, assembly.GetType("Fixture.Value", throwOnError: true)!);
        } finally { context.Unload(); }
    }

    private static void Positive(MetadataReference[] references, CSharpParseOptions parse)
    {
        var scalars = new (string Type, string Kind)[] {
            ("bool", "boolean"), ("char", "string"), ("System.Guid", "string"), ("float", "number"),
            ("double", "number"), ("decimal", "number"), ("sbyte", "integer"), ("byte", "integer"),
            ("short", "integer"), ("ushort", "integer"), ("int", "integer"), ("uint", "integer"),
            ("long", "integer"), ("ulong", "integer"), ("Choice", "integer")
        };
        foreach (var scalar in scalars) {
            string schema = Schema(("x", scalar.Kind));
            string source = "using Ncma.Runtime; namespace Fixture; public enum Choice:ulong { Zero } " + Declaration("z.x", "X", "x", 15) +
                "public struct Value:IComponent { public " + scalar.Type + " X {get;init;} public int ReadOnly => 1; public static int Static {get;set;} public int this[int i] {get=>i;set{}} }";
            WithFixture(source, references, parse, (symbol, type) => {
                var generated = ComponentFields.Read(symbol, schema);
                Compare(generated, Prepare(type, schema), scalar.Type);
                Check(generated.Length == 1 && generated[0].Kind == scalar.Kind && generated[0].Flags == 15, "Independent scalar oracle: " + scalar.Type);
            });
        }
        const string nestedSchema = """{"type":"object","additionalProperties":false,"required":["part"],"properties":{"part":{"type":"object","additionalProperties":false,"required":["x"],"properties":{"x":{"type":"integer"}}}}} """;
        string nested = "using Ncma.Runtime; namespace Fixture; public struct Part { public int X; } " +
            Declaration("z.parent", "Part", "part", 7) + Declaration("a.child", "Part.X", "part.x", 5) +
            "public struct Value:IComponent { public Part Part {get;init;} }";
        WithFixture(nested, references, parse, (symbol, type) => {
            var fields = ComponentFields.Read(symbol, nestedSchema); Compare(fields, Prepare(type, nestedSchema), "nested");
            Check(fields.Length == 2 && fields[0].Id == "a.child" && fields[0].Kind == "integer" && fields[0].Flags == 5 &&
                fields[1].Id == "z.parent" && fields[1].Kind == "object" && fields[1].Flags == 7, "Independent nested/order/flags oracle");
        });
    }

    private static void Negative(MetadataReference[] references, CSharpParseOptions parse)
    {
        var cases = new (string Prefix, string Member, string Kind, string Message)[] {
            ("", "[System.Text.Json.Serialization.JsonIgnore] private int Hidden {get;set;} public float X {get;init;}", "number", "Custom JSON attributes"),
            ("", "[System.Text.Json.Serialization.JsonPropertyName(\"other\")] public float X {get;init;}", "number", "Custom JSON attributes"),
            ("[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]", "public float X {get;init;}", "number", "Custom JSON attributes"),
            ("", "public readonly float X;", "number", "Readonly fields"),
            ("", "public float X {get;private set;}", "number", "Nonpublic serialized setter"),
            ("", "public nint X {get;init;}", "integer", "Unsupported metadata value type"),
            ("", "public nuint X {get;init;}", "integer", "Unsupported metadata value type"),
            // Entity's JsonConverter is rejected before the unsupported-type rule on both adapters.
            ("", "public Entity X {get;init;}", "object", "Custom JSON attributes"),
            ("", "public string X {get;init;}", "string", "Unsupported metadata value type"),
            ("", "public System.DateTime X {get;init;}", "string", "Unsupported built-in converter/collection")
        };
        var failures = new List<string>();
        for (int index = 0; index < cases.Length; ++index) {
            var item = cases[index];
            // Object leaves need a valid closed schema to reach the member-rule rejection.
            string schema = item.Kind == "object" ?
                """{"type":"object","additionalProperties":false,"required":["x"],"properties":{"x":{"type":"object","additionalProperties":false,"required":[],"properties":{}}}}""" : Schema(("x", item.Kind));
            string source = "using Ncma.Runtime; namespace Fixture; " + item.Prefix + Declaration("x", "X", "x") +
                "public struct Value:IComponent { " + item.Member + " }";
            WithFixture(source, references, parse, (symbol, type) => {
                var compiler = Rejection(() => ComponentFields.Read(symbol, schema));
                var runtime = Rejection(() => Prepare(type, schema));
                // The path identifies the controlled fixture target, not an inferred exception location.
                string memberPath = index == 0 ? "Hidden" : index == 2 ? "<type>" : "X";
                foreach (var side in new[] { (Name: "generator", Error: compiler), (Name: "runtime", Error: runtime) })
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {
                        side = side.Name, @case = $"negative-{index}:{item.Member}", memberPath,
                        exceptionType = side.Error.Type, message = side.Error.Message, expected = item.Message
                    }));
                if (compiler != runtime || compiler.Type != typeof(ArgumentException).FullName ||
                    !compiler.Message.StartsWith(item.Message, StringComparison.Ordinal))
                    failures.Add($"case={index}, memberPath={memberPath}, Rejection drift: {compiler} / {runtime}; expected={item.Message}");
            });
        }
        Check(failures.Count == 0, string.Join("\n", failures));
    }
    private static (string Type, string Message) Rejection(Action action)
    {
        try { action(); } catch (Exception error) { return (error.GetType().FullName!, error.Message); }
        return ("<none>", "Expected field contract rejection.");
    }
    private static string Repository()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "managed/Ncma.CodeGen/Ncma.CodeGen.csproj"))) return dir.FullName;
        throw new Exception("Field contract inventory requires the current repository source tree.");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
