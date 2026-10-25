using Ncma.Runtime;

internal static class CompositionRegistryTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("composition roots are copied deduplicated deterministic and isolated", () => {
            var calls = new List<string>();
            var leaf = new ComponentModule("leaf", [], _ => calls.Add("leaf"));
            var dependencies = new[] { leaf };
            var left = new ComponentModule("left", dependencies, _ => calls.Add("left"));
            dependencies[0] = new("unused", [], _ => throw new Exception("Caller mutated dependencies"));
            var right = new ComponentModule("right", [leaf], _ => calls.Add("right"));
            var first = CompositionRegistry.Build(right, left, leaf, left);
            Check(calls.SequenceEqual(new[] { "leaf", "left", "right" }));
            Check(first.Describe().Single().TypeId == "ncma.transform");
            calls.Clear(); var second = CompositionRegistry.Build(left, right);
            Check(calls.SequenceEqual(new[] { "leaf", "left", "right" }) && !ReferenceEquals(first, second));
            _ = new World(components: first);
            Reject(() => first.Register<Value>("test.extra", 1, Schema, v => v));
            second.Register<Value>("test.extra", 1, Schema, v => v); // Existing cold extension semantics retained.
            Check(!first.IsRegistered<Value>() && second.IsRegistered<Value>());
        });
        yield return ("composition rejects conflicting identities and budgets before callbacks", () => {
            int calls = 0;
            var a = new ComponentModule("same", [], _ => calls++);
            var b = new ComponentModule("same", [], _ => calls++);
            Reject(() => CompositionRegistry.Build(a, b)); Check(calls == 0);
            var roots = Enumerable.Range(0, 129).Select(i => new ComponentModule("m" + i, [], _ => calls++)).ToArray();
            Reject(() => CompositionRegistry.Build(roots)); Check(calls == 0);
            Reject(() => CompositionRegistry.Build([null!])); Check(calls == 0);
        });
        yield return ("composition preserves original duplicate registration and failed-build isolation", () => {
            var first = new ComponentModule("a", [], r => r.Register<Value>("test.value", 1, Schema, v => v));
            var duplicate = new ComponentModule("b", [], r => r.Register<Value>("test.value", 1, Schema, v => v));
            Reject(() => CompositionRegistry.Build(first, duplicate));
            var good = CompositionRegistry.Build(first);
            Check(good.Describe().Count == 2 && good.IsRegistered<Value>());
            var failing = new ComponentModule("z", [], _ => throw new InvalidOperationException("expected registration failure"));
            Reject(() => CompositionRegistry.Build(first, failing));
            Check(good.Describe().Count == 2 && CompositionRegistry.Build().Describe().Count == 1);
        });
    }
    private const string Schema = """{"type":"object","additionalProperties":false,"required":["x"],"properties":{"x":{"type":"integer"}}}""";
    private readonly record struct Value(int X) : IComponent;
    private static void Check(bool value) { if (!value) throw new Exception("Composition contract failed"); }
    private static void Reject(Action action) {
        try { action(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return; }
        throw new Exception("Expected composition rejection");
    }
}
