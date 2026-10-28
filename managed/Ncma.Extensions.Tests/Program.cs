using Ncma.Extensions;

static void Check(bool value) { if (!value) throw new Exception("Extension contract assertion failed."); }
static void Reject(string code, Action action)
{
    try { action(); } catch (ExtensionRejectedException error) when (error.Code == code) { return; }
    throw new Exception("Expected rejection: " + code);
}
static ExtensionVersion V(int minor = 0, int patch = 0) => new(1, minor, patch);
static ExtensionDependency D(string id) => new(id, V(), V(99));
static ExtensionDescriptor E(string id, params ExtensionDependency[] dependencies) => new(id, V(), V(), dependencies);

int metadata = CaseHarness.Run("Managed extensions", [
    ("empty and deterministic dependency order", () => {
        Check(ExtensionPlan.Compile(V()).StartupOrder.Count == 0);
        var a = E("a", D("z")); var b = E("b", D("z")); var z = E("z");
        var first = ExtensionPlan.Compile(V(), a, b, z); var second = ExtensionPlan.Compile(V(), z, b, a);
        Check(first.StartupOrder.Select(e => e.Id).SequenceEqual(new[] { "z", "a", "b" }));
        Check(first.StartupOrder.SequenceEqual(second.StartupOrder) && first.DependencyCount == 2);
    }),
    ("identity and stable version rejection", () => {
        foreach (string id in new[] { "", "Upper", "x/y", "x\\y", "a..b", "a.", "a-", "a\n", "汉字", new string('a', 97) })
            Reject("extension_id_invalid", () => E(id));
        Reject("extension_version_invalid", () => new ExtensionVersion(0, 0, 0));
        Reject("extension_version_invalid", () => new ExtensionVersion(1, -1, 0));
        Reject("extension_version_invalid", () => new ExtensionVersion(1, 0, 65536));
        Reject("extension_version_invalid", () => new ExtensionDescriptor("a", default, V()));
        Reject("extension_version_invalid", () => ExtensionPlan.Compile(default));
        Reject("extension_dependency_range_invalid", () => new ExtensionDependency("a", V(2), V(1)));
        Reject("extension_dependency_range_invalid", () => new ExtensionDependency("a", V(), new(2, 0, 0)));
    }),
    ("exact major and minimum host API", () => {
        var needs = new ExtensionDescriptor("a", V(), V(2, 3));
        Reject("extension_host_api_incompatible", () => ExtensionPlan.Compile(V(2, 2), needs));
        Reject("extension_host_api_incompatible", () => ExtensionPlan.Compile(new(2, 0, 0), needs));
        Check(ExtensionPlan.Compile(V(2, 3), needs).StartupOrder.Count == 1);
        Check(ExtensionPlan.Compile(V(3), needs).StartupOrder.Count == 1);
    }),
    ("duplicate null and missing registrations", () => {
        var e = E("a");
        Reject("extension_registration_duplicate", () => ExtensionPlan.Compile(V(), e, e));
        Reject("extension_registration_null", () => ExtensionPlan.Compile(V(), new ExtensionDescriptor[] { null! }));
        Reject("extension_dependency_null", () => E("a", new ExtensionDependency[] { null! }));
        Reject("extension_dependency_duplicate", () => E("a", D("b"), D("b")));
        Reject("extension_dependency_missing", () => ExtensionPlan.Compile(V(), E("a", D("b"))));
    }),
    ("inclusive dependency version bounds", () => {
        var consumer = E("c", new ExtensionDependency("p", V(1, 2), V(1, 4)));
        foreach (int patch in new[] { 2, 3, 4 }) Check(ExtensionPlan.Compile(V(), consumer, new("p", V(1, patch), V())).StartupOrder.Count == 2);
        foreach (var version in new[] { V(1, 1), V(1, 5), new ExtensionVersion(2, 0, 0) })
            Reject("extension_dependency_version", () => ExtensionPlan.Compile(V(), consumer, new("p", version, V())));
    }),
    ("cycles and self dependency", () => {
        Reject("extension_dependency_cycle", () => ExtensionPlan.Compile(V(), E("a", D("a"))));
        Reject("extension_dependency_cycle", () => ExtensionPlan.Compile(V(), E("a", D("b")), E("b", D("c")), E("c", D("a"))));
    }),
    ("all budgets and maximum chain", () => {
        var chain = Enumerable.Range(0, 64).Select(i => E("e" + i.ToString("D2"), i == 0 ? [] : [D("e" + (i - 1).ToString("D2"))])).ToArray();
        Check(ExtensionPlan.Compile(V(), chain).StartupOrder.Count == 64);
        Reject("extension_count_budget", () => ExtensionPlan.Compile(V(), [.. chain, E("extra")]));
        Reject("extension_dependency_budget", () => E("a", Enumerable.Range(0, 17).Select(i => D("d" + i)).ToArray()));
        var providers = Enumerable.Range(0, 16).Select(i => E("p" + i)).ToArray();
        var consumers = Enumerable.Range(0, 16).Select(i => E("c" + i, providers.Select(p => D(p.Id)).ToArray())).ToArray();
        Check(ExtensionPlan.Compile(V(), [.. providers, .. consumers]).DependencyCount == 256);
        Reject("extension_edge_budget", () => ExtensionPlan.Compile(V(), [.. providers, .. consumers, E("z", D("p0"))]));
    }),
    ("copied inputs immutable plan failure isolation", () => {
        var dependencies = new[] { D("p") }; var c = E("c", dependencies); dependencies[0] = D("missing");
        var input = new[] { c, E("p") }; var plan = ExtensionPlan.Compile(V(), input); input[0] = E("other");
        Check(plan.StartupOrder.Select(e => e.Id).SequenceEqual(new[] { "p", "c" }));
        bool immutable = false; try { ((IList<ExtensionDescriptor>)plan.StartupOrder)[0] = E("bad"); } catch (NotSupportedException) { immutable = true; }
        Check(immutable);
        Reject("extension_dependency_missing", () => ExtensionPlan.Compile(V(), E("bad", D("missing"))));
        Check(plan.StartupOrder[0].Id == "p" && c.Dependencies[0].Id == "p");
    }),
    ("no engine or model runtime dependency", () => {
        var references = typeof(ExtensionPlan).Assembly.GetReferencedAssemblies();
        Check(!references.Any(r => r.Name!.StartsWith("Ncma.", StringComparison.Ordinal) || r.Name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)));
    })
]);
int lifecycle = ExtensionLifecycleTests.Run();
int composition = ExtensionCompositionTests.Run();
int sourcePatch = ExtensionSourcePatchTests.Run();
int sourceCapture = ExtensionSourceCaptureTests.Run();
return metadata == 0 && lifecycle == 0 && composition == 0 && sourcePatch == 0 && sourceCapture == 0 ? 0 : 1;
