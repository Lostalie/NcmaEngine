using System.Runtime.InteropServices;
using Ncma.Interop;
static void Check(bool value) { if (!value) throw new Exception("Assertion failed."); }
static void Reject(Action action)
{
    try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or BadImageFormatException or AggregateException or DllNotFoundException) { return; }
    throw new Exception("Expected rejection.");
}
string root = Path.GetFullPath(args[0]);
static PluginSpecification Spec(string id, string file, string[]? dependencies = null) => new(id, ModuleKind.Fixture, file, file, 1, 0, dependencies ?? []);
var good = Spec("good", "NcmaFixtureGood.dll");
var cases = new (string, Action)[]
{
    ("C and CLR x64 layouts", () => {
        Check(IntPtr.Size == 8 && Marshal.SizeOf<ModuleApi>() == 56 && Marshal.SizeOf<PluginError>() == 528 && Marshal.SizeOf<ModuleStatus>() == 32);
        Check((int)Marshal.OffsetOf<ModuleApi>(nameof(ModuleApi.Initialize)) == 24 && (int)Marshal.OffsetOf<ModuleStatus>(nameof(ModuleStatus.Sequence)) == 24);
    }),
    ("Module leases guard DLL lifetime and repeated Dispose", () => {
        using var loader = new PluginLoader(); loader.Load(root, [good]);
        var module = loader.Modules.Single(); var lease = module.AcquireLease();
        Check(module.Status.State == 1); Reject(module.Dispose); Reject(loader.Dispose);
        Check(module.Status.State == 1); lease.Dispose(); lease.Dispose();
        loader.Dispose(); loader.Dispose(); Reject(() => _ = module.Status);
    }),
    ("Wrong major short table missing export and failed init", () => {
        foreach (string file in new[] { "NcmaFixtureWrongMajor.dll", "NcmaFixtureShort.dll", "NcmaFixtureMissing.dll", "NcmaFixtureFail.dll" }) {
            using var loader = new PluginLoader(); Reject(() => loader.Load(root, [Spec("bad", file)]));
            Check(loader.Modules.Count == 0); loader.Load(root, [good]); Check(loader.Modules.Count == 1);
        }
    }),
    ("Owner-thread checks and stale context after release", () => {
        using var loader = new PluginLoader(); loader.Load(root, [good]); var module = loader.Modules.Single();
        Check(Task.Run(() => { try { _ = module.Status; return false; } catch (PluginException e) { return e.Result == PluginResult.WrongThread; } }).Result);
        Check(Task.Run(() => { try { loader.Dispose(); return false; } catch (InvalidOperationException) { return true; } }).Result);
        Reject(() => module.ReadFunction<Action>(24)); // Common API pointers cannot escape the typed adapter path.
        Reject(() => module.ReadFunction<Action>(4096));
    }),
    ("Dependency topological load and reverse release", () => {
        using var loader = new PluginLoader();
        loader.Load(root, [Spec("consumer", "NcmaFixtureTimeout.dll", ["good"]), good]);
        Check(loader.Modules[0].Id == "good" && loader.Modules[0].OutstandingLeases == 1);
        Reject(loader.Modules[0].Dispose); Reject(loader.Dispose); // Timeout retains both dependency and failing module.
        Check(loader.Modules.Count == 2 && loader.Modules[0].Status.State == 1);
        loader.Dispose(); loader.Dispose(); // Fixture's deterministic timeout clears on the second controlled shutdown.
    }),
    ("Failed consumer initializes no instance and releases dependency", () => {
        using var loader = new PluginLoader();
        Reject(() => loader.Load(root, [good, Spec("fail", "NcmaFixtureFail.dll", ["good"])]));
        Check(loader.Modules.Count == 0);
    }),
    ("Bounded owner release queue retains lease until drained", () => {
        using var loader = new PluginLoader(); loader.Load(root, [good]); var module = loader.Modules.Single();
        var leases = Enumerable.Range(0, 256).Select(_ => module.AcquireLease()).ToArray();
        Task.Run(() => { foreach (var lease in leases) lease.ScheduleRelease(() => { }); }).Wait();
        var overflow = module.AcquireLease(); Reject(() => overflow.ScheduleRelease(() => { })); overflow.Dispose();
        Reject(leases[0].Dispose);
        Check(module.DrainReleases() == 256 && module.OutstandingLeases == 0);
    }),
    ("Release failure retains lease and does not unload", () => {
        using var loader = new PluginLoader(); loader.Load(root, [good]); var module = loader.Modules.Single();
        var lease = module.AcquireLease(); bool fail = true;
        lease.ScheduleRelease(() => { if (fail) throw new InvalidOperationException("release"); });
        Reject(module.Dispose); Check(module.OutstandingLeases == 1);
        fail = false; Check(module.DrainReleases() == 1); module.Dispose();
    }),
    ("Duplicate IDs paths dependencies cycle and missing dependency", () => {
        foreach (var specs in new[] {
            new[] { good, good },
            new[] { good, good with { Id = "other" } },
            new[] { good with { Dependencies = ["unknown"] } },
            new[] { good with { Dependencies = ["good"] } },
            new[] { good with { Dependencies = ["other"] }, Spec("other", "NcmaFixtureTimeout.dll", ["good"]) },
            new[] { good with { Dependencies = ["other", "other"] }, Spec("other", "NcmaFixtureTimeout.dll") }
        }) { using var loader = new PluginLoader(); Reject(() => loader.Load(root, specs)); Check(loader.Modules.Count == 0); }
    }),
    ("Exact allowlist path filename ABI kind and hash checks", () => {
        foreach (var spec in new[] {
            good with { Path = "../NcmaFixtureGood.dll" }, good with { Path = Path.Combine(root, good.Path) },
            good with { FileName = "Other.dll" }, good with { Major = 2 }, good with { Minor = 1 },
            good with { Kind = ModuleKind.Renderer }, good with { Sha256 = new string('0', 64) },
            good with { Path = "Missing.dll", FileName = "Missing.dll" }
        }) { using var loader = new PluginLoader(); Reject(() => loader.Load(root, [spec])); Check(loader.Modules.Count == 0); }
    }),
    ("Thirty-two independent module contexts are released", () => {
        for (int cycle = 0; cycle < 32; cycle++) {
            using var a = new PluginLoader(); using var b = new PluginLoader();
            a.Load(root, [good]); b.Load(root, [good]);
            Check(a.Modules[0].Status.Sequence != b.Modules[0].Status.Sequence);
            a.Dispose(); Check(b.Modules[0].Status.State == 1);
        }
    }),
};
foreach (var (name, run) in cases) {
    try { run(); Console.WriteLine("PASS " + name); }
    catch (Exception e) { Console.Error.WriteLine("FAIL " + name + ": " + e); return 1; }
}
Console.WriteLine($"M2.2 Interop: {cases.Length} cases passed.");
return 0;
