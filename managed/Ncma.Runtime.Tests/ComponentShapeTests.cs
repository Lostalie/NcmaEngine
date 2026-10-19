using System.Reflection;
using System.Text.Json;
using Ncma.Runtime;

internal static class ComponentShapeTests
{
    private static readonly Action<JsonElement, JsonElement, JsonElement, int> Original =
        typeof(ComponentRegistry).GetMethod("CheckShape", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Action<JsonElement, JsonElement, JsonElement, int>>();
    private static void Check(bool value) { if (!value) throw new Exception("Compiled schema assertion failed."); }
    private static bool Accepts(Action check)
    { try { check(); return true; } catch (ArgumentException) { return false; } }
    private static void Compare(string schema, IEnumerable<string> inputs)
    {
        using var definition = JsonDocument.Parse(schema);
        var root = definition.RootElement;
        var compiled = ComponentShape.TryCreate(root);
        Check(compiled is not null);
        foreach (string input in inputs)
        {
            using var document = JsonDocument.Parse(input);
            var value = document.RootElement;
            Check(Accepts(() => Original(value, root, root, 0)) == Accepts(() => compiled!.Check(value)));
        }
    }
    internal static int Run()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("closed object required optional duplicate unknown and escaped fields", () => Compare(
                """{"type":"object","required":["a","a"],"properties":{"a":{"type":"integer"},"中文😀":{"type":"string"},"q\"":{"type":"boolean"}}}""",
                ["{}", "{\"a\":1}", "{\"a\":1,\"a\":2}", "{\"a\":1,\"\\u0061\":2}", "{\"a\":1,\"extra\":0}",
                 "{\"a\":1,\"中文😀\":\"value\",\"q\\\"\":true}", "{\"a\":1,\"中文😀\":null}", "[]", "null", "1"])),
            ("scalar boundary agreement including finite numbers and int64", () => {
                foreach (string kind in new[] { "number", "integer", "string", "boolean" })
                    Compare("{\"type\":\"" + kind + "\"}",
                        ["0", "-0", "1.5", "1e2", "1e400", "9223372036854775807", "9223372036854775808", "-9223372036854775808",
                         "-9223372036854775809", "true", "false", "null", "\"1\"", "{}", "[]"]);
            }),
            ("local refs and nested duplicate rejection", () => Compare(
                """{"type":"object","required":["inner"],"properties":{"inner":{"$ref":"#/$defs/point"}},"$defs":{"point":{"type":"object","required":["x"],"properties":{"x":{"type":"number"},"y":{"type":"number"}}}}}""",
                ["{}", "{\"inner\":{}}", "{\"inner\":{\"x\":2}}", "{\"inner\":{\"x\":2,\"y\":3}}",
                 "{\"inner\":{\"x\":2,\"x\":3}}", "{\"inner\":{\"x\":2,\"y\":null}}", "{\"inner\":{\"z\":1}}"])),
            ("all 32 field bits including last required bit", () => {
                string schema = "{\"type\":\"object\",\"required\":[\"p31\"],\"properties\":{" +
                    string.Join(',', Enumerable.Range(0,32).Select(i => $"\"p{i}\":{{\"type\":\"integer\"}}")) + "}}";
                Compare(schema, ["{}", "{\"p31\":1}", "{\"p0\":1}", "{\"p31\":1,\"p31\":2}",
                    "{"+string.Join(',', Enumerable.Range(0,32).Select(i=>$"\"p{i}\":{i}"))+"}"]);
            }),
            ("random closed payloads agree with unmodified recursive oracle", () => {
                var random = new Random(310219); var inputs = new List<string>();
                string[] values = ["0", "1e400", "1.25", "true", "\"中文😀\"", "null", "{}", "[]"];
                for (int i=0; i<1000; i++) inputs.Add("{"+string.Join(',', Enumerable.Range(0,random.Next(6))
                    .Select(_=>$"\"p{random.Next(4)}\":{values[random.Next(values.Length)]}"))+"}");
                Compare("""{"type":"object","required":["p0"],"properties":{"p0":{"type":"number"},"p1":{"type":"string"},"p2":{"type":"boolean"}}}""", inputs);
            }),
            ("unsupported malformed cyclic deep and oversized plans fall back", () => {
                string large = "{\"type\":\"object\",\"properties\":{" +
                    string.Join(',', Enumerable.Range(0,33).Select(i => $"\"p{i}\":{{\"type\":\"integer\"}}")) + "}}";
                string deep = "{\"$ref\":\"#/$defs/p0\",\"$defs\":{" +
                    string.Join(',', Enumerable.Range(0,34).Select(i => $"\"p{i}\":{{\"$ref\":\"#/$defs/p{i+1}\"}}")) + ",\"p34\":{\"type\":\"integer\"}}}";
                foreach (string input in new[] { large, deep, "{}", "{\"type\":\"array\"}",
                    "{\"type\":\"object\",\"properties\":{\"x\":{},\"x\":{}}}",
                    "{\"type\":\"object\",\"properties\":{},\"required\":[\"missing\"]}",
                    "{\"$ref\":\"#/$defs/a\",\"$defs\":{\"a\":{\"$ref\":\"#/$defs/a\"}}}" }) {
                    using var document=JsonDocument.Parse(input); Check(ComponentShape.TryCreate(document.RootElement) is null);
                }
            }),
        };
        int failures=0;
        foreach(var test in cases) {try {test.Run(); Console.WriteLine("PASS schema plan "+test.Name);}
            catch(Exception error) {failures++; Console.Error.WriteLine("FAIL schema plan "+test.Name+": "+error);} }
        Console.WriteLine($"Component schema plan tests: {cases.Length-failures}/{cases.Length} passed.");
        return failures;
    }
}
