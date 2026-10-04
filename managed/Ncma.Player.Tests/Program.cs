using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma;
using Ncma.Application;
using Ncma.Application.Runtime;
using Ncma.Gameplay;
using Ncma.Player.App;
using Ncma.Runtime;
using Ncma.Scene;

static void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
static void Reject(Action action)
{ try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException) { return; } throw new Exception("Expected rejection."); }
static SceneDocument Fixture(int fail = 0, bool updateFails = false, bool createFails = false, bool closeFails = false)
{
    var doc = new SceneDocument("Player fixture");
    var item = doc.World.CreateObject("Character", Guid.Parse("11111111-1111-1111-1111-111111111111")); item.Set(TransformData.Identity);
    doc.SetBindings(item.PersistentId, [new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "PlayerProbe", true,
        [new("FailAt", ExportKind.Integer, fail), new("UpdateFails", ExportKind.Boolean, updateFails ? 1 : 0),
         new("CreateFails", ExportKind.Boolean, createFails ? 1 : 0), new("CloseFails", ExportKind.Boolean, closeFails ? 1 : 0)])]);
    return doc;
}
string root = Path.GetFullPath(args[0]);
string output = Path.Combine(root, "out/verification/m2-7", args[1], "tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(output);
string projectRoot = Path.Combine(output, "中文 Player Project"); Directory.CreateDirectory(projectRoot);
string assemblyPath = Assembly.GetExecutingAssembly().Location;
File.Copy(assemblyPath, Path.Combine(projectRoot, "gameplay.dll"));
File.Copy(Path.ChangeExtension(assemblyPath, ".deps.json"), Path.Combine(projectRoot, "gameplay.deps.json"));
string projectPath = Path.Combine(projectRoot, "sample.ncmaproject"), scenePath = Path.Combine(projectRoot, "start.ncmascene");
var config = new ProjectConfiguration(1, Guid.Parse("33333333-3333-3333-3333-333333333333"), "Player fixture", "start.ncmascene", "gameplay.dll", "Direct3D11", []);
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
void Project(bool enabled = false) => File.WriteAllBytes(projectPath, JsonSerializer.SerializeToUtf8Bytes(config with { PhysicsEnabled = enabled }, json));
void Scene(int fail = 0, bool updatesFail = false) => SceneDocumentFiles.Save(Fixture(fail, updatesFail), scenePath);
PlayerOptions Options(ulong ticks = 120) => PlayerOptions.Parse(["--project", projectPath, "--headless", "--ticks", ticks.ToString(),
    "--report", Path.Combine(output, Guid.NewGuid().ToString("N") + ".json")]);
Project(); Scene();
var cases = new (string Name, Action Run)[] {
    ("CLI preflight rejects malformed/conflicting/bounded arguments", () => {
        foreach (string[] a in new[] { new[] { "--headless" }, new[] { "--help", "--version" },
            new[] { "--project" }, new[] { "--project", projectPath, "--headless", "--ticks", "0" },
            new[] { "--project", projectPath, "--headless", "--ticks", "1000001" },
            new[] { "--project", projectPath, "--headless", "--ticks", "1", "--renderer", "d3d11" },
            new[] { "--project", projectPath, "--ticks", "1", "--fixed-delta", "NaN" },
            new[] { "--project", projectPath, "--max-runtime-seconds", "Infinity" },
            new[] { "--project", projectPath, "--ticks", "1", "--ticks", "2" },
            new[] { "--project", projectPath, "--renderer", "vulkan-extra" } }) Reject(() => PlayerOptions.Parse(a));
        Check(PlayerOptions.Parse(["--help"]).Help && PlayerOptions.Parse(["--version"]).Version);
    }),
    ("Pure fixed step has exact ticks, no Update/debt and rejects mixed mode", () => {
        var doc = Fixture(); var probe = new PlayerProbe(); using var play = new PlaySession(doc, FrameTimePolicy.Strict, advanceMode: PlayAdvanceMode.FixedSteps);
        play.Start(_ => probe); for (int i = 0; i < 600; i++) Check(play.AdvanceFixedStep().StepsExecuted == 1);
        Check(play.Tick == 600 && probe.Updates == 0 && probe.Fixed == 600 && play.Status.FrameCount == 0 && play.Status.Accumulator == 0 && play.Status.TotalDroppedSeconds == 0);
        var before = play.Status; Reject(() => play.AdvanceFrame(.01)); Check(before == play.Status);
        play.Pause(); Reject(() => play.AdvanceFixedStep()); play.Step(); Check(play.Tick == 601); play.Resume(); play.AdvanceFixedStep();
        using var framePlay = new PlaySession(Fixture()); framePlay.Start(_ => new PlayerProbe()); Reject(() => framePlay.AdvanceFixedStep());
    }),
    ("Fixed step faults abort staged components/input, preserve successful ticks", () => {
        var doc = Fixture(); var probe = new PlayerProbe { FailAt = 3 };
        using var play = new PlaySession(doc, FrameTimePolicy.Strict, advanceMode: PlayAdvanceMode.FixedSteps); play.Start(_ => probe);
        play.AdvanceFixedStep(); play.AdvanceFixedStep(); var before = doc.CaptureBytes();
        var fault = play.AdvanceFixedStep(); Check(fault.State == PlayState.Faulted && fault.Tick == 2 && fault.Fault!.AttemptTick == 3);
        Check(before.SequenceEqual(doc.CaptureBytes())); Reject(() => play.AdvanceFixedStep()); play.Stop();
    }),
    ("Fixed input consumes transient once and preserves focus/reset semantics", () => {
        var probe = new PlayerProbe(); using var play = new PlaySession(Fixture(), FrameTimePolicy.Strict, advanceMode: PlayAdvanceMode.FixedSteps); play.Start(_ => probe);
        play.SubmitInput(new(play.SessionId, 1, true, new ulong[8], new ulong[8], new ulong[8]));
        var pressed = new ulong[8]; pressed[0] = 1;
        play.SubmitInput(new(play.SessionId, 2, true, pressed, pressed, new ulong[8])); play.AdvanceFixedStep(); play.AdvanceFixedStep();
        Check(probe.Presses == 1); Reject(() => play.SubmitInput(new(play.SessionId, 2, true, pressed, pressed, new ulong[8])));
    }),
    ("Frame rates 30/60/144 match fixed-step serialized state", () => {
        string? expected = null;
        foreach (int fps in new[] { 30, 60, 144 }) {
            var doc = Fixture(); using var play = new PlaySession(doc, FrameTimePolicy.Strict); play.Start(_ => new PlayerProbe());
            for (int i = 0; i < fps * 2; i++) play.AdvanceFrame(1d / fps);
            Check(play.Tick == 120); string hash = Convert.ToHexString(SHA256.HashData(doc.CaptureBytes())); expected ??= hash; Check(hash == expected);
        }
        var direct = Fixture(); using var headless = new PlaySession(direct, FrameTimePolicy.Strict, advanceMode: PlayAdvanceMode.FixedSteps); headless.Start(_ => new PlayerProbe());
        for (int i = 0; i < 120; i++) headless.AdvanceFixedStep(); Check(Convert.ToHexString(SHA256.HashData(direct.CaptureBytes())) == expected);
    }),
    ("Shared service owns direct runtime document, borrowed catalog and close lease", () => {
        var doc = Fixture(); using var owner = new RuntimeSessionOwner(doc); owner.LoadGameplay(Path.Combine(projectRoot, "gameplay.dll"));
        var play = owner.StartPlay(factory: d => new PlaySession(d, FrameTimePolicy.Strict, advanceMode: PlayAdvanceMode.FixedSteps));
        Check(ReferenceEquals(play.Document, doc)); play.AdvanceFixedStep(); Reject(owner.Catalog.Clear);
        bool rejected = false; var thread = new Thread(() => { try { owner.StopPlay(); } catch (InvalidOperationException) { rejected = true; } }); thread.Start(); thread.Join(); Check(rejected);
        owner.StopPlay(); owner.StopPlay(); owner.Catalog.Clear();
    }),
    ("Headless runs without native/Python/Gui and writes bounded reproducible reports", () => {
        Scene(updatesFail: true); var one = PlayerRunner.Run(Options(), pluginRoot: Path.Combine(output, "no-native")); var two = PlayerRunner.Run(Options(), pluginRoot: Path.Combine(output, "no-native"));
        Check(one.ExitCode == 0 && one.Tick == 120 && one.Modules.Length == 0 && one.StateSha256 == two.StateSha256);
        Check(two.ExitCode == 0 && two.RenderedFrames == 0); Scene();
    }),
    ("Shared runtime rejects factory reentry and World mutation before lifecycle", () => {
        var doc = Fixture(); using var owner = new RuntimeSessionOwner(doc); owner.LoadGameplay(Path.Combine(projectRoot, "gameplay.dll"));
        byte[] before = doc.CaptureBytes();
        Reject(() => owner.StartPlay(factory: d => { owner.StartPlay(); return new PlaySession(d); }));
        Check(owner.Play is null && before.SequenceEqual(doc.CaptureBytes()));
        Reject(() => owner.StartPlay(factory: d => { d.World.CreateObject("Forbidden"); return new PlaySession(d); }));
        Check(owner.Play is null && before.SequenceEqual(doc.CaptureBytes()));
        owner.StartPlay(); owner.StopPlay();
    }),
    ("Fault evidence captured before Stop and private exception message redacted", () => {
        Scene(3); var options = Options(); var report = PlayerRunner.Run(options);
        Check(report.ExitCode == 5 && report.Tick == 2 && report.Fault!.AttemptTick == 3 && report.State == PlayState.Faulted);
        Check(!File.ReadAllText(options.Report).Contains("private-secret")); Scene();
    }),
    ("Startup lifecycle fault evidence and shutdown failure have explicit exits", () => {
        SceneDocumentFiles.Save(Fixture(createFails: true), scenePath); var startup = PlayerRunner.Run(Options());
        Check(startup.ExitCode == 5 && startup.Tick == 0 && startup.Fault is not null && startup.State == PlayState.Faulted);
        SceneDocumentFiles.Save(Fixture(closeFails: true), scenePath); var close = PlayerRunner.Run(Options(1));
        Check(close.ExitCode == 6 && close.Tick == 1 && close.ShutdownErrors.SequenceEqual(new[] { "runtime_shutdown_failed" })); Scene();
    }),
    ("Wall-clock budget stops at safe boundary, not by changing simulated delta", () => {
        var report = PlayerRunner.Run(Options(2) with { MaxRuntimeSeconds = 1 }, trustedInput: (_, _) => Thread.Sleep(1100));
        Check(report.ExitCode == 8 && report.Tick == 1 && report.FixedDeltaSeconds == 1d / 60);
    }),
    ("Cancellation and exact 32 lifecycle cycles release catalog/resources", () => {
        var cancel = PlayerRunner.Run(Options(), () => true); Check(cancel.ExitCode == 130 && cancel.Tick == 0);
        for (int i = 0; i < 32; i++) { var report = PlayerRunner.Run(Options(1)); Check(report.ExitCode == 0 && report.Tick == 1 && report.ShutdownErrors.Length == 0); }
    }),
    ("Missing native dependency is explicit, disabled physics succeeds", () => {
        Project(true); var enabled = PlayerRunner.Run(Options(), pluginRoot: Path.Combine(output, "missing-plugins")); Check(enabled.ExitCode == 3);
        Project(); Check(PlayerRunner.Run(Options()).ExitCode == 0);
    }),
    ("Unknown bindings/schema reject without losing source assets", () => {
        var doc = Fixture(); var item = doc.World.GetObjects()[0]; doc.SetBindings(item.PersistentId, [new(Guid.NewGuid(), "Missing.Behaviour", true, [])]);
        SceneDocumentFiles.Save(doc, scenePath); byte[] source = File.ReadAllBytes(scenePath);
        Check(PlayerRunner.Run(Options()).ExitCode != 0 && source.SequenceEqual(File.ReadAllBytes(scenePath))); Scene();
        Reject(() => { using var owner = new RuntimeSessionOwner(Fixture()); owner.LoadGameplay(Path.Combine(output, "missing.dll")); });
    }),
    ("Vulkan refuses instead of silently falling back", () => {
        var options = PlayerOptions.Parse(["--project", projectPath, "--renderer", "vulkan", "--ticks", "1", "--report", Path.Combine(output, "vulkan.json")]);
        var report = PlayerRunner.Run(options); Check(report.ExitCode == 7 && report.Modules.Length == 0 && report.Tick == 0);
    }),
    ("Existing report never overwritten and report failure is non-success", () => {
        var options = Options(); File.WriteAllText(options.Report, "keep"); var report = PlayerRunner.Run(options);
        Check(report.ExitCode != 0 && File.ReadAllText(options.Report) == "keep" && report.ReportError is not null);
        var blocked = Path.Combine(output, "not-a-directory"); File.WriteAllText(blocked, "keep");
        Check(PlayerRunner.Run(Options(1) with { Report = Path.Combine(blocked, "report.json") }).ExitCode == 9);
    }),
    ("Actual apphost runs from unrelated cwd and has no editor/legacy bridge dependency", () => {
        var options = Options(2); string app = Path.Combine(root, "managed/Ncma.Player.App/bin", args[1], "net8.0/NcmaPlayer.exe");
        var info = new ProcessStartInfo(app) { WorkingDirectory = output, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string a in new[] { "--project", projectPath, "--headless", "--ticks", "2", "--report", options.Report }) info.ArgumentList.Add(a);
        using var process = Process.Start(info)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(20000)) { process.Kill(true); throw new Exception("Apphost watchdog"); }
        Check(process.ExitCode == 0, stdout.Result + stderr.Result); Check(File.Exists(options.Report));
        string deps = File.ReadAllText(Path.ChangeExtension(app, ".deps.json"));
        foreach (string forbidden in new[] { "Ncma.Editor", "Ncma.Gui", "Ncma.Managed.Host", "NcmaNative" }) Check(!deps.Contains(forbidden));
    }),
};
var passed = new List<string>();
foreach (var test in cases) { try { test.Run(); passed.Add(test.Name); Console.WriteLine("PASS " + test.Name); }
    catch (Exception e) { Console.Error.WriteLine("FAIL " + test.Name + ": " + e); return 1; } }
if (args.Length > 2) {
    try { foreach (string test in PackageChecks.Run(root, args[1], output, Path.GetFullPath(args[2]))) { passed.Add(test); Console.WriteLine("PASS " + test); } }
    catch (Exception e) { Console.Error.WriteLine("FAIL Package: " + e); return 1; }
}
File.WriteAllBytes(Path.Combine(output, "results.json"), JsonSerializer.SerializeToUtf8Bytes(new { passed = passed.Count, tests = passed }, json));
Console.WriteLine($"Player tests: {passed.Count} passed."); return 0;

public sealed class PlayerProbe : Behaviour
{
    [Export] public int FailAt { get; set; }
    [Export] public bool UpdateFails { get; set; }
    [Export] public bool CreateFails { get; set; }
    [Export] public bool CloseFails { get; set; }
    public int Fixed, Updates, Presses;
    protected override void OnCreate() { if (CreateFails) throw new Exception("private-secret startup"); }
    protected override void OnDisable() { if (CloseFails) throw new Exception("private-secret shutdown"); }
    protected override void OnFixedUpdate(double delta)
    {
        Fixed++; if (Context.Input.Pressed(0)) Presses++;
        var transform = GameObject.LocalTransform; transform.Position.X += (float)delta; GameObject.LocalTransform = transform;
        if (FailAt != 0 && Fixed == FailAt) throw new Exception("private-secret");
    }
    protected override void OnUpdate(double delta) { Updates++; if (UpdateFails) throw new Exception("Headless invoked OnUpdate"); }
}
