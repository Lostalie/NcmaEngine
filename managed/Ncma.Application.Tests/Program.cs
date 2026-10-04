using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Ncma.Application;
using Ncma.Editor.Services;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scripting;

static void Check(bool value) { if (!value) throw new Exception("Assertion failed."); }
static void Reject(Action action)
{
    try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or JsonException or AggregateException) { return; }
    throw new Exception("Expected rejection.");
}
string root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
string configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name;
string assembly = Path.Combine(root, "managed/Ncma.Gameplay.Sample/bin", configuration, "net8.0/Ncma.Gameplay.Sample.dll");
var cases = new (string, Action)[]
{
    ("Catalogs are independent and descriptors are copied", () => {
        using var a = new ScriptCatalogService(); using var b = new ScriptCatalogService();
        using var candidate = a.LoadCandidate(assembly); a.CommitCandidate(candidate);
        Check(a.Snapshot.Types.Length == 1 && b.Snapshot.Types.Length == 0);
        var snapshot = a.Snapshot; snapshot.Types[0].Exports[0] = new("Bad", 0, "", "", 0);
        Check(a.Snapshot.Types[0].Exports[0].Name != "Bad");
        Check(a.Snapshot.Generation != Guid.Empty);
    }),
    ("Candidate owner, disposed and failed load checks", () => {
        using var a = new ScriptCatalogService(); using var b = new ScriptCatalogService();
        using var c = a.LoadCandidate(assembly); Reject(() => b.CommitCandidate(c));
        a.CommitCandidate(c); var generation = a.Snapshot.Generation;
        Reject(() => a.LoadCandidate(assembly + ".missing")); Check(a.Snapshot.Generation == generation);
        Reject(() => a.CommitCandidate(c));
        using var bad = a.LoadCandidate(assembly); bad.Dispose(); Reject(() => _ = bad.Count);
    }),
    ("Candidate factory remains usable after commit and Dispose", () => {
        using var service = new ScriptCatalogService();
        using var candidate = service.LoadCandidate(assembly);
        var binding = new BehaviourBindingData(Guid.NewGuid(), "Ncma.Gameplay.Sample.RotatorBehaviour", true, []);
        var factory = candidate.Instantiate; service.CommitCandidate(candidate); candidate.Dispose();
        Check(factory(binding).GetType().FullName == binding.TypeName);
        Reject(() => factory(binding with { Exports = [new("Multiplier", ExportKind.Integer, 0.5)] }));
        Reject(() => factory(binding with { TypeName = "Missing" }));
    }),
    ("Catalog owner-thread and idempotent disposal", () => {
        var catalog = new ScriptCatalogService();
        Check(Task.Run(() => { try { _ = catalog.Snapshot; return false; } catch (InvalidOperationException) { return true; } }).Result);
        catalog.Dispose(); catalog.Dispose(); Reject(() => _ = catalog.Snapshot);
    }),
    ("Twelve collectible candidate cycles", () => {
        for (int i = 0; i < 12; i++) {
            var weak = Probe(assembly);
            for (int pass = 0; pass < 10 && weak.IsAlive; pass++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            Check(!weak.IsAlive);
        }
    }),
    ("Editor Play is isolated and stopping restores editing", () => {
        using var owner = new EditorSessionOwner("Edit");
        owner.LoadGameplay(assembly);
        var obj = owner.Document.World.CreateObject("Character"); obj.Set(TransformData.Identity);
        owner.Document.SetBindings(obj.PersistentId, [new(Guid.NewGuid(), "Ncma.Gameplay.Sample.RotatorBehaviour", true, [])]);
        owner.Edit!.Resynchronize();
        byte[] before = owner.Document.CaptureBytes();
        var play = owner.StartPlay();
        Check(play.Document.World.Identity != owner.Document.World.Identity && owner.Edit.State.Frozen);
        play.AdvanceFrame(1.0 / 60);
        Check(before.SequenceEqual(owner.Document.CaptureBytes()));
        Reject(owner.Catalog.Clear);
        owner.StopPlay(); Check(owner.Play is null && !owner.Edit.State.Frozen);
        owner.StartPlay(); owner.StopPlay();
    }),
    ("Reload preserves edit generation on failed preflight", () => {
        using var owner = new EditorSessionOwner("Edit");
        owner.LoadGameplay(assembly);
        Guid generation = owner.Catalog.Snapshot.Generation;
        var obj = owner.Document.World.CreateObject("Bad");
        owner.Document.SetBindings(obj.PersistentId, [new(Guid.NewGuid(), "Missing.Type", true, [])]);
        Reject(() => owner.LoadGameplay(assembly));
        Check(owner.Catalog.Snapshot.Generation == generation);
        Reject(() => owner.StartPlay()); Check(owner.Play is null && !owner.Edit!.State.Frozen);
    }),
    ("Endpoint default is off; session disposal is guarded", () => {
        var owner = new EditorSessionOwner("Edit"); Check(owner.Endpoint is null);
        Check(Task.Run(() => { try { owner.Dispose(); return false; } catch (InvalidOperationException) { return true; } }).Result);
        owner.Dispose(); owner.Dispose(); Reject(() => _ = owner.Document);
    }),
    ("Lifetime reverse release and repeated dispose", () => {
        var calls = new List<string>(); var app = new ApplicationLifetime();
        app.Start([new Service("a", calls), new Service("b", calls)]);
        Check(app.State == ApplicationState.Running); app.Dispose(); app.Dispose();
        Check(string.Join(",", calls) == "a.start,b.start,b.stop,a.stop");
    }),
    ("Every initialization failure releases partial service", () => {
        for (int fail = 0; fail < 3; fail++) {
            var calls = new List<string>(); using var app = new ApplicationLifetime();
            var services = Enumerable.Range(0, 3).Select(i => (IApplicationService)new Service(i.ToString(), calls, i == fail)).ToArray();
            Reject(() => app.Start(services)); Check(app.State == ApplicationState.Failed);
            Check(calls.Count == 2 * (fail + 1));
            Check(calls.Last() == "0.stop");
        }
    }),
    ("Shutdown failure still releases all services", () => {
        var calls = new List<string>(); var app = new ApplicationLifetime();
        app.Start([new Service("a", calls), new Service("b", calls, stopFail: true)]);
        Reject(app.Dispose); Check(calls.Last() == "a.stop" && app.State == ApplicationState.Failed); app.Dispose();
    }),
    ("Callbacks cannot reenter lifetime", () => {
        var app = new ApplicationLifetime();
        app.Start([new Service("a", [], callback: () => Reject(app.Dispose))]);
        Check(Task.Run(() => { try { app.Dispose(); return false; } catch (InvalidOperationException) { return true; } }).Result);
        app.Dispose();
    }),
    ("One delta and deterministic safe-boundary frame order", () => {
        using var app = new ApplicationLifetime(); app.Start([]);
        var pipe = new Pipeline();
        Check(app.Run(new Clock([0, 0, 1.0 / 60, 9.0 / 60]), pipe, 3) == 3);
        Check(pipe.Frames.Select(f => f.DeltaSeconds).SequenceEqual(new[] { 0d, 1.0 / 60, 8.0 / 60 }));
        Check(string.Join(",", pipe.Calls.Take(7)) == "pump,edit,agent,gui,input,play,present");
        Check(pipe.Frames.Select(f => f.FrameId).SequenceEqual(new ulong[] { 1, 2, 3 }));
    }),
    ("Invalid monotonic clock does not Tick", () => {
        foreach (double delta in new[] { double.NaN, double.PositiveInfinity, -1d }) {
            using var app = new ApplicationLifetime(); app.Start([]); var pipe = new Pipeline();
            Reject(() => app.Run(new Clock([0, delta]), pipe, 1));
            Check(pipe.Frames.Count == 0 && app.State == ApplicationState.Failed);
        }
    }),
    ("Fake loop has no native Python or editor dependency", () => {
        var names = typeof(ApplicationLifetime).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();
        Check(!names.Any(n => n.Contains("Editor") || n.Contains("Python") || n.Contains("Interop")));
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Ncma.Managed.Host"));
        Check(!typeof(Ncma.Gameplay.PlaySession).Assembly.GetReferencedAssemblies().Any(a => a.Name!.Contains("Editor")));
    }),
    ("Strict project config and explicit atomic save", () => {
        string dir = Path.Combine(root, "out/verification/m2/projects/项目 with spaces"); Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "Main.ncmascene"), new SceneDocument().CaptureBytes());
        File.Copy(assembly, Path.Combine(dir, "Gameplay.dll"), true);
        string path = Path.Combine(dir, "Project.ncmaproject");
        string json = JsonSerializer.Serialize(new ProjectConfiguration(1, Guid.NewGuid(), "项目", "Main.ncmascene", "Gameplay.dll", "Direct3D11", []),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(path, json);
        var project = ProjectContext.Load(path); project.Save("Saved.ncmaproject");
        Check(ProjectContext.Load(Path.Combine(dir, "Saved.ncmaproject")).Configuration.ProjectId == project.Configuration.ProjectId);
        Check(!project.Configuration.PhysicsEnabled);
        // Missing flag remains disabled for existing projects; explicit opt-in round-trips.
        File.WriteAllText(path, json.Replace(",\"physicsEnabled\":false", ""));
        Check(!ProjectContext.Load(path).Configuration.PhysicsEnabled);
        File.WriteAllText(path, json.Replace("\"physicsEnabled\":false", "\"physicsEnabled\":true"));
        var physicsProject = ProjectContext.Load(path); physicsProject.Save("Physics.ncmaproject");
        Check(ProjectContext.Load(Path.Combine(dir, "Physics.ncmaproject")).Configuration.PhysicsEnabled);
        Reject(() => project.Resolve("../Gameplay.dll", ".dll"));
        Reject(() => project.Resolve("C:/Gameplay.dll", ".dll"));
        Reject(() => project.Save("../Bad.ncmaproject"));
        foreach (string bad in new[] {
            json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"other\":0"),
            json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"),
            json.Replace("Main.ncmascene", "Main.ncscene"),
            json.Replace("Direct3D11", "Bad"),
            json.Replace("\"plugins\":[]", "\"plugins\":[{\"id\":\"p\",\"path\":\"Gameplay.dll\"},{\"id\":\"p\",\"path\":\"Gameplay.dll\"}]"),
            json.Replace("\"name\":", "\"missingName\":")
        }) {
            File.WriteAllText(path, bad); Reject(() => ProjectContext.Load(path));
        }
    }),
    ("Bounded copied log and rotation", () => {
        string path = Path.Combine(root, "out/verification/m2/logs", Guid.NewGuid() + ".jsonl");
        using var log = new ApplicationLog(path, 2, 65536);
        for (int i = 0; i < 20; i++) log.Write("info", "test", new string('x', 8000), Guid.Empty);
        Check(log.Snapshot.Length == 2 && log.Snapshot[1].Sequence == 20);
        Check(File.Exists(path + ".1") && new FileInfo(path).Length <= 65536 && new FileInfo(path + ".1").Length <= 65536);
        Reject(() => log.Write("Bad", "test", "", Guid.Empty));
    })
};
foreach (var (name, run) in cases) { run(); Console.WriteLine("PASS " + name); }
Console.WriteLine($"M2.1 application/services: {cases.Length} cases passed.");

[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference Probe(string path)
{
    using var service = new ScriptCatalogService(); using var candidate = service.LoadCandidate(path);
    return candidate.ContextReference;
}
sealed class Service(string id, List<string> calls, bool startFail = false, bool stopFail = false, Action? callback = null) : IApplicationService
{
    public void Start() { calls.Add(id + ".start"); callback?.Invoke(); if (startFail) throw new InvalidOperationException("start"); }
    public void Dispose() { calls.Add(id + ".stop"); if (stopFail) throw new InvalidOperationException("stop"); }
}
sealed class Clock(double[] times) : IFrameClock { private int _index; public double Seconds => times[_index++]; }
sealed class Pipeline : IFramePipeline
{
    public List<string> Calls { get; } = [];
    public List<FrameContext> Frames { get; } = [];
    public bool PumpPlatform() { Calls.Add("pump"); return true; }
    public void ApplyEditorIntents(FrameContext f) { Calls.Add("edit"); }
    public void PumpAgent(FrameContext f) { Calls.Add("agent"); }
    public void BeginPresentation(FrameContext f) { Calls.Add("gui"); }
    public void SubmitInput(FrameContext f) { Calls.Add("input"); }
    public void AdvancePlay(FrameContext f) { Calls.Add("play"); Frames.Add(f); }
    public void Present(FrameContext f) { Calls.Add("present"); }
}
