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
    Guid item = doc.CreateObject("Character", Guid.Parse("11111111-1111-1111-1111-111111111111")); doc.SetComponent(item, TransformData.Identity);
    doc.SetBindings(item, [new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "PlayerProbe", true,
        [new("FailAt", ExportKind.Integer, fail), new("UpdateFails", ExportKind.Boolean, updateFails ? 1 : 0),
         new("CreateFails", ExportKind.Boolean, createFails ? 1 : 0), new("CloseFails", ExportKind.Boolean, closeFails ? 1 : 0)])]);
    return doc;
}
static RuntimeSceneInstance RuntimeFixture(int fail = 0, bool updateFails = false, bool createFails = false, bool closeFails = false)
{
    var source = Fixture(fail, updateFails, createFails, closeFails);
    return source.BakeRuntime(source.Revision).Image.Instantiate(source.Components);
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
// Player lifecycle tests consume immutable scenario assets, not another case's mutable file.
// Atomic replacement/locked-save preservation belongs to SceneDocumentTests and stays unchanged.
void SaveScene(SceneDocument document)
{
    string candidate = Path.Combine(projectRoot, "start-" + Guid.NewGuid().ToString("N") + ".ncmascene");
    SceneDocumentFiles.Save(document, candidate);
    scenePath = candidate; config = config with { StartupScene = Path.GetFileName(candidate) }; Project();
}
void Scene(int fail = 0, bool updatesFail = false) => SaveScene(Fixture(fail, updatesFail));
PlayerOptions Options(ulong ticks = 120) => PlayerOptions.Parse(["--project", projectPath, "--headless", "--ticks", ticks.ToString(),
    "--report", Path.Combine(output, Guid.NewGuid().ToString("N") + ".json")]);
Project(); Scene();
if(args.Length==4 && args[3]=="--root-motion-only") {
    try {foreach(string test in RootMotionChecks.Run(root,output,Path.GetFullPath(args[2])))Console.WriteLine("PASS "+test);return 0;}
    catch(Exception e){Console.Error.WriteLine("FAIL Root motion: "+e);return 1;}
}
if(args.Length==4 && args[3]=="--action-only") {
    try {foreach(string test in ActionCombatChecks.Run(output,Path.GetFullPath(args[2])))Console.WriteLine("PASS "+test);return 0;}
    catch(Exception e){Console.Error.WriteLine("FAIL Actions: "+e);return 1;}
}
if(args.Length==4 && args[3]=="--action-joint-only") {
    try {foreach(string test in ActionJointChecks.Run(root,output,Path.GetFullPath(args[2])))Console.WriteLine("PASS "+test);return 0;}
    catch(Exception e){Console.Error.WriteLine("FAIL Joint actions: "+e);return 1;}
}
var cases = new (string Name, Action Run)[] {
    ("Memory checkpoints are opt-in lifecycle observations and preserve gameplay", () => {
        var plainOptions=Options(3);Check(!plainOptions.MemoryCheckpoints);
        var parsed=PlayerOptions.Parse(["--project",projectPath,"--headless","--ticks","3","--memory-checkpoints"]);
        Check(parsed.MemoryCheckpoints);
        Reject(()=>PlayerOptions.Parse(["--project",projectPath,"--memory-checkpoints","--memory-checkpoints"]));
        using var runner=new PlayerRunner();
        var plain=runner.Run(plainOptions,visible:false);
        var observedOptions=Options(3) with{MemoryCheckpoints=true};
        var observed=runner.Run(observedOptions,visible:false);
        Check(plain.ExitCode==0&&observed.ExitCode==0&&plain.StateSha256==observed.StateSha256&&plain.MemoryCheckpoints is null);
        Check(plain.ResourceMemory is null&&observed.ResourceMemory is {TotalObservations:4,OmittedObservations:0});
        Check(observed.ResourceMemory!.Samples.All(s=>s.State==Ncma.Rendering.RendererMemoryState.NoRenderer&&s.Ledger is null),"Null never fabricates zero GPU counters");
        Check(observed.MemoryCheckpoints is {Version:1,MemoryAccepted:false,CompleteLifetimeCoverage:false});
        Check(observed.MemoryCheckpoints!.Samples.Select(s=>s.Checkpoint).SequenceEqual(Enum.GetValues<MemoryCheckpoint>()));
        Check(observed.MemoryCheckpoints.Samples.All(s=>s.ProcessId==Environment.ProcessId&&s.ProcessCountersAvailable));
        using var jsonReport=JsonDocument.Parse(File.ReadAllBytes(observedOptions.Report));
        Check(jsonReport.RootElement.GetProperty("memoryCheckpoints").GetProperty("samples").GetArrayLength()==4);
        using var plainJson=JsonDocument.Parse(File.ReadAllBytes(plainOptions.Report));
        Check(!plainJson.RootElement.TryGetProperty("memoryCheckpoints",out _));
        Check(!plainJson.RootElement.TryGetProperty("resourceMemory",out _));
        var invalid=runner.Run(Options(1) with{MemoryCheckpoints=true,FixedDelta=0},visible:false);
        Check(invalid.ExitCode==2&&invalid.MemoryCheckpoints is null);
    }),
    ("Memory failure paths retain exact missing lifecycle checkpoints without retry", () => {
        using var startupOwner=new PlayerRunner();
        SaveScene(Fixture(createFails:true));
        var startup=startupOwner.Run(Options(1) with{MemoryCheckpoints=true},visible:false);
        Check(startup.ExitCode==5&&startup.MemoryCheckpoints is not null);
        Check(startup.MemoryCheckpoints!.Samples.Select(s=>s.Checkpoint).SequenceEqual(new[]{MemoryCheckpoint.BeforeStartup,MemoryCheckpoint.BeforeShutdown,MemoryCheckpoint.AfterShutdown}));
        using var closeOwner=new PlayerRunner();
        SaveScene(Fixture(closeFails:true));
        var close=closeOwner.Run(Options(1) with{MemoryCheckpoints=true},visible:false);
        Check(close.ExitCode==6&&closeOwner.Status.Phase==PlayerRunnerPhase.Retained&&closeOwner.Status.ShutdownAttempts==1);
        Check(close.MemoryCheckpoints!.Samples.Select(s=>s.Checkpoint).SequenceEqual(new[]{MemoryCheckpoint.BeforeStartup,MemoryCheckpoint.RuntimeReady,MemoryCheckpoint.BeforeShutdown}));
        Check(close.ResourceMemory!.Samples.All(s=>s.Checkpoint!=Ncma.Rendering.RendererMemoryCheckpoint.AfterShutdown));
        closeOwner.RetryShutdown();
        Check(closeOwner.Status.Phase==PlayerRunnerPhase.Idle&&closeOwner.LastReport==close&&close.MemoryCheckpoints.Samples.Length==3);
        Scene();
    }),
    ("Window mode CLI and SDK require explicit graphical semantics", () => {
        var standard=PlayerOptions.Parse(["--project",projectPath]);
        Check(standard.WindowMode is null);
        foreach(var name in new[]{"windowed","borderless-pixels"}) {
            var mode=PlayerOptions.Parse(["--project",projectPath,"--window-mode",name]);
            mode.ValidateRun();
            Check(mode.WindowMode==(name=="windowed"?Ncma.Platform.WindowMode.Windowed:Ncma.Platform.WindowMode.BorderlessPixels));
            Reject(()=>PlayerOptions.Parse(["--project",projectPath,"--headless","--ticks","1","--window-mode",name]));
            Reject(()=>(mode with{Headless=true,Ticks=1}).ValidateRun());
        }
        foreach(var value in new[]{"","borderless","fullscreen","BorderlessPixels","1","-1"})
            Reject(()=>PlayerOptions.Parse(["--project",projectPath,"--window-mode",value]));
        Reject(()=>PlayerOptions.Parse(["--project",projectPath,"--window-mode"]));
        Reject(()=>PlayerOptions.Parse(["--project",projectPath,"--window-mode","windowed","--window-mode","borderless-pixels"]));
        Reject(()=>(standard with{WindowMode=(Ncma.Platform.WindowMode)2}).ValidateRun());
        using var runner=new PlayerRunner();
        var invalid=runner.Run(Options(1) with{WindowMode=Ncma.Platform.WindowMode.BorderlessPixels},visible:false);
        Check(invalid.ExitCode==2&&invalid.Output is null&&invalid.Tick==0&&invalid.Modules.Length==0);
    }),
    ("Window size CLI is paired bounded invariant and unavailable to Null", () => {
        var standard=PlayerOptions.Parse(["--project",projectPath]);
        Check(standard.WindowSize is null && PlayerWindowSize.Default==new PlayerWindowSize(1280,720));
        var sized=PlayerOptions.Parse(["--project",projectPath,"--window-width","1920","--window-height","1080"]);
        Check(sized.WindowSize==new PlayerWindowSize(1920,1080));sized.ValidateRun();
        foreach(string dimension in new[]{"0","8193","-1","+1","1.5","1e3","NaN","4294967296"," 12","１２"}) {
            Reject(()=>PlayerOptions.Parse(["--project",projectPath,"--window-width",dimension,"--window-height","1080"]));
            Reject(()=>PlayerOptions.Parse(["--project",projectPath,"--window-height",dimension,"--window-width","1920"]));
        }
        foreach(string axis in new[]{"--window-width","--window-height"}) {
            Reject(()=>PlayerOptions.Parse(["--project",projectPath,axis,"1920"]));
            Reject(()=>PlayerOptions.Parse(["--project",projectPath,axis]));
            Reject(()=>PlayerOptions.Parse(["--project",projectPath,axis,"1",axis,"2"]));
        }
        Check(PlayerOptions.Parse(["--project",projectPath,"--window-height","8192","--window-width","1"]).WindowSize==new PlayerWindowSize(1,8192));
        Reject(()=>PlayerOptions.Parse(["--project",projectPath,"--headless","--ticks","1","--window-width","1920","--window-height","1080"]));
        Reject(()=>(sized with{Headless=true,Ticks=1}).ValidateRun());
        Reject(()=>(standard with{WindowSize=new(0,1080)}).ValidateRun());
        Reject(()=>(standard with{WindowSize=new(1920,8193)}).ValidateRun());
        using var runner=new PlayerRunner();
        var nullReport=runner.Run(Options(1),visible:false);
        Check(nullReport.ExitCode==0&&nullReport.Output is null&&nullReport.Modules.Length==0);
        var invalid=runner.Run(Options(1) with{WindowSize=new(1920,1080)},visible:false);
        Check(invalid.ExitCode==2&&invalid.Output is null&&invalid.Tick==0&&invalid.Modules.Length==0,
            "SDK rejects graphical sizing before Null startup");
    }),
    ("Presentation stages are opt-in and Null never fabricates graphics", () => {
        Scene();var baseline=Options(3);
        Check(!baseline.PresentationStages,"stages default off");
        var options=PlayerOptions.Parse(["--project",projectPath,"--headless","--ticks","3","--presentation-stages","--report",Path.Combine(output,Guid.NewGuid().ToString("N")+".json")]);
        Reject(()=>PlayerOptions.Parse(["--project",projectPath,"--presentation-stages","--presentation-stages"]));
        using var runner=new PlayerRunner();
        var plain=runner.Run(baseline,visible:false);var observed=runner.Run(options,visible:false);
        Check(plain.ExitCode==0&&observed.ExitCode==0&&plain.StateSha256==observed.StateSha256,"same simulation state");
        Check(plain.PresentationStages is null&&observed.PresentationStages is{Version:1,TotalCalls:0,OmittedCalls:0,PerformanceAccepted:false},"Null has no presentation calls");
        using var saved=JsonDocument.Parse(File.ReadAllBytes(options.Report));
        Check(saved.RootElement.GetProperty("presentationStages").GetProperty("calls").GetArrayLength()==0,"durable empty evidence");
        using var original=JsonDocument.Parse(File.ReadAllBytes(baseline.Report));
        Check(!original.RootElement.TryGetProperty("presentationStages",out _),"no default field");
    }),
    ("Full Player loop stream preserves suffix and explicit host failures", () => {
        Scene();
        string target=Path.Combine(output,"player-full.ncloop");
        var options=PlayerOptions.Parse(["--project",projectPath,"--headless","--ticks","800",
            "--loop-stream",target,"--report",Path.Combine(output,"player-full.json")]);
        Check(options.LoopCosts&&options.LoopStream==Path.GetFullPath(target));
        using var runner=new PlayerRunner();
        var report=runner.Run(options,visible:false);
        Check(report.ExitCode==0&&report.Tick==800&&report.LoopCosts is {
            OmittedIterations:288,CompleteRawCoverage:false,Stream:{Produced:800,Written:800,CompleteRawCoverage:true}});
        string[] lines=File.ReadAllLines(target);
        Check(lines.Length==802);
        using(var last=JsonDocument.Parse(lines[^2]))
            Check(last.RootElement.GetProperty("sample").GetProperty("Sequence").GetUInt64()==800);
        byte[] original=File.ReadAllBytes(target);
        var rejected=runner.Run(options with {Report=Path.Combine(output,"player-full-exists.json")},visible:false);
        Check(rejected.ExitCode==2&&original.SequenceEqual(File.ReadAllBytes(target)),"no overwrite or startup");
        Reject(()=>PlayerOptions.Parse(["--project",projectPath,"--loop-stream",target]));
        Reject(()=>PlayerOptions.Parse(["--project",projectPath,"--loop-stream",Path.Combine(output,"wrong.json")]));
        Scene(2);
        var fault=runner.Run(Options(10) with {LoopCosts=true,LoopStream=Path.Combine(output,"player-full-fault.ncloop")},visible:false);
        Check(fault.ExitCode==5&&fault.LoopCosts is {TotalIterations:2,Stream:{Written:2,CompleteRawCoverage:true}},
            "gameplay failure remains failure even with complete diagnostic stream");
        Scene();
        var stopped=runner.Run(Options(10) with {LoopCosts=true,LoopStream=Path.Combine(output,"player-full-cancel.ncloop")},()=>true,visible:false);
        Check(stopped.ExitCode==130&&stopped.LoopCosts is {TotalIterations:1,Stream:{Written:1,CompleteRawCoverage:true}});
    }),
    ("Opt-in actual Player loop prefix is bounded, durable, and not synthetic FPS", () => {
        Scene();var plainOptions=Options(3);
        Check(!plainOptions.LoopCosts,"default loop instrumentation disabled");
        var parsed=PlayerOptions.Parse(["--project",projectPath,"--headless","--ticks","3","--loop-costs","--report",Path.Combine(output,Guid.NewGuid().ToString("N")+".json")]);
        Check(parsed.LoopCosts,"explicit loop CLI");
        Reject(()=>PlayerOptions.Parse(["--project",projectPath,"--loop-costs","--loop-costs"]));
        using var runner=new PlayerRunner();
        var plain=runner.Run(plainOptions,visible:false);
        var observed=runner.Run(parsed,visible:false);
        Check(plain.ExitCode==0&&observed.ExitCode==0&&plain.StateSha256==observed.StateSha256&&plain.LoopCosts is null,"observation preserves gameplay");
        Check(observed.LoopCosts is {Version:2,TotalIterations:3,OmittedIterations:0,PresentReturns:0,PresentIntervals:0,TargetPerformanceAccepted:false,FrameRateLimit:null},"Null loop never fabricates presents");
        Check(observed.LoopCosts!.Samples.All(r=>r.Outcome==Ncma.Application.LoopCostOutcome.Completed&&r.OwnerAllocatedBytes>=0&&r.PresentReturnTimestamp is null),"real fixed step loop samples");
        using var saved=JsonDocument.Parse(File.ReadAllBytes(parsed.Report));
        Check(saved.RootElement.GetProperty("loopCosts").GetProperty("samples").GetArrayLength()==3,"durable raw prefix");
        using var ordinary=JsonDocument.Parse(File.ReadAllBytes(plainOptions.Report));
        Check(!ordinary.RootElement.TryGetProperty("loopCosts",out _),"default absence");
        Check(!ordinary.RootElement.TryGetProperty("frameRateLimit",out _),"default cap metadata absent");
        var boundedOptions=Options(514) with {LoopCosts=true};
        var bounded=runner.Run(boundedOptions,visible:false);
        Check(bounded.ExitCode==0&&bounded.Tick==514&&bounded.LoopCosts is {TotalIterations:514,OmittedIterations:2,CompleteRawCoverage:false},"explicit omissions without slowing gameplay");
        Check(new FileInfo(boundedOptions.Report).Length<PlayerReports.MaxBytes,"maximum prefix fits existing report budget");
        var cancelled=runner.Run(Options(3) with {LoopCosts=true},()=>true,visible:false);
        Check(cancelled.ExitCode==130&&cancelled.LoopCosts!.Samples.Single().Outcome==Ncma.Application.LoopCostOutcome.Stopped,"terminal iteration preserved");
        Scene(2);
        var fault=runner.Run(Options(3) with {LoopCosts=true},visible:false);
        Check(fault.ExitCode==5&&fault.LoopCosts!.Samples.Last().Outcome==Ncma.Application.LoopCostOutcome.Faulted,"gameplay failure prefix preserved");
        Scene();
    }),
    ("Opt-in actual Null Player simulation costs preserve gameplay and omit graphics", () => {
        Scene(); var original=Options(3);
        Check(!original.SimulationCosts,"default simulation instrumentation disabled");
        var parsed=PlayerOptions.Parse(["--project",projectPath,"--headless","--ticks","3","--simulation-costs","--report",Path.Combine(output,Guid.NewGuid().ToString("N")+".json")]);
        Check(parsed.SimulationCosts,"explicit opt-in CLI");
        Reject(()=>PlayerOptions.Parse(["--project",projectPath,"--simulation-costs","--simulation-costs"]));
        using var runner=new PlayerRunner();
        var plain=runner.Run(original,pluginRoot:args.Length>2?args[2]:null,visible:false);
        var observed=runner.Run(parsed,pluginRoot:args.Length>2?args[2]:null,visible:false);
        Check(plain.ExitCode==0&&observed.ExitCode==0&&plain.StateSha256==observed.StateSha256&&plain.SimulationCosts is null,"unchanged actual game state");
        Check(observed.SimulationCosts is { LastCharacterStep:null,Advance:{ Completed:true,Kind:SimulationAdvanceKind.FixedStep,TickAfter:3,CommittedSteps:1,IncludesUpdate:false }}&&
            observed.Modules.Length==0&&observed.RenderedFrames==0&&observed.PresentationCosts is null,"Null costs require no graphics/physics resources");
        using var saved=JsonDocument.Parse(File.ReadAllBytes(parsed.Report));
        Check(saved.RootElement.GetProperty("simulationCosts").GetProperty("advance").GetProperty("tickAfter").GetUInt64()==3,"durable copied simulation report");
        using var ordinary=JsonDocument.Parse(File.ReadAllBytes(original.Report));
        Check(!ordinary.RootElement.TryGetProperty("simulationCosts",out _),"disabled report remains absent");
    }),
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
    ("Frame cap is explicit, bounded and rejected for deterministic Headless", () => {
        Check(PlayerOptions.Parse(["--project",projectPath]).FrameRateLimit is null,"default uncapped");
        Check(PlayerOptions.Parse(["--project",projectPath,"--max-fps","30"]).FrameRateLimit==30,"explicit 30fps cap");
        foreach(string value in new[]{"0","241","-1","30.5","NaN","+30"," 30","2147483648"})
            Reject(()=>PlayerOptions.Parse(["--project",projectPath,"--max-fps",value]));
        Reject(()=>PlayerOptions.Parse(["--project",projectPath,"--max-fps"]));
        Reject(()=>PlayerOptions.Parse(["--project",projectPath,"--max-fps","30","--max-fps","30"]));
        Reject(()=>PlayerOptions.Parse(["--project",projectPath,"--headless","--ticks","3","--max-fps","30"]));
        Reject(()=>(Options(3) with {FrameRateLimit=30}).ValidateRun());
        Reject(()=>(Options(3) with {Headless=false,FrameRateLimit=241}).ValidateRun());
    }),
    ("Pure fixed step has exact ticks, no Update/debt and rejects mixed mode", () => {
        var doc = RuntimeFixture(); var probe = new PlayerProbe(); using var play = new PlaySession(doc, FrameTimePolicy.Strict, advanceMode: PlayAdvanceMode.FixedSteps);
        play.Start(_ => probe); for (int i = 0; i < 600; i++) Check(play.AdvanceFixedStep().StepsExecuted == 1);
        Check(play.Tick == 600 && probe.Updates == 0 && probe.Fixed == 600 && play.Status.FrameCount == 0 && play.Status.Accumulator == 0 && play.Status.TotalDroppedSeconds == 0);
        var before = play.Status; Reject(() => play.AdvanceFrame(.01)); Check(before == play.Status);
        play.Pause(); Reject(() => play.AdvanceFixedStep()); play.Step(); Check(play.Tick == 601); play.Resume(); play.AdvanceFixedStep();
        using var framePlay = new PlaySession(RuntimeFixture()); framePlay.Start(_ => new PlayerProbe()); Reject(() => framePlay.AdvanceFixedStep());
    }),
    ("Fixed step faults abort staged components/input, preserve successful ticks", () => {
        var doc = RuntimeFixture(); var probe = new PlayerProbe { FailAt = 3 };
        using var play = new PlaySession(doc, FrameTimePolicy.Strict, advanceMode: PlayAdvanceMode.FixedSteps); play.Start(_ => probe);
        play.AdvanceFixedStep(); play.AdvanceFixedStep(); var before = doc.CaptureCheckpoint().CopyBytes();
        var fault = play.AdvanceFixedStep(); Check(fault.State == PlayState.Faulted && fault.Tick == 2 && fault.Fault!.AttemptTick == 3);
        Check(before.SequenceEqual(doc.CaptureCheckpoint().CopyBytes())); Reject(() => play.AdvanceFixedStep()); play.Stop();
    }),
    ("Fixed input consumes transient once and preserves focus/reset semantics", () => {
        var probe = new PlayerProbe(); using var play = new PlaySession(RuntimeFixture(), FrameTimePolicy.Strict, advanceMode: PlayAdvanceMode.FixedSteps); play.Start(_ => probe);
        play.SubmitInput(new(play.SessionId, 1, true, new ulong[8], new ulong[8], new ulong[8]));
        var pressed = new ulong[8]; pressed[0] = 1;
        play.SubmitInput(new(play.SessionId, 2, true, pressed, pressed, new ulong[8])); play.AdvanceFixedStep(); play.AdvanceFixedStep();
        Check(probe.Presses == 1); Reject(() => play.SubmitInput(new(play.SessionId, 2, true, pressed, pressed, new ulong[8])));
    }),
    ("Frame rates 30/60/144 match fixed-step serialized state", () => {
        string? expected = null;
        foreach (int fps in new[] { 30, 60, 144 }) {
            var doc = RuntimeFixture(); using var play = new PlaySession(doc, FrameTimePolicy.Strict); play.Start(_ => new PlayerProbe());
            for (int i = 0; i < fps * 2; i++) play.AdvanceFrame(1d / fps);
            Check(play.Tick == 120); string hash = Convert.ToHexString(SHA256.HashData(doc.CaptureCheckpoint().CopyBytes())); expected ??= hash; Check(hash == expected);
        }
        var direct = RuntimeFixture(); using var headless = new PlaySession(direct, FrameTimePolicy.Strict, advanceMode: PlayAdvanceMode.FixedSteps); headless.Start(_ => new PlayerProbe());
        for (int i = 0; i < 120; i++) headless.AdvanceFixedStep(); Check(Convert.ToHexString(SHA256.HashData(direct.CaptureCheckpoint().CopyBytes())) == expected);
    }),
    ("Shared service borrows explicit runtime scene and owns catalog and close lease", () => {
        var doc = RuntimeFixture(); using var owner = new RuntimeSessionOwner(); owner.LoadGameplay(Path.Combine(projectRoot, "gameplay.dll"), doc);
        var play = owner.StartPlay(doc, factory: d => new PlaySession(d, FrameTimePolicy.Strict, advanceMode: PlayAdvanceMode.FixedSteps));
        Check(ReferenceEquals(play.Scene, doc)); play.AdvanceFixedStep(); Reject(owner.Catalog.Clear);
        bool rejected = false; var thread = new Thread(() => { try { owner.StopPlay(); } catch (InvalidOperationException) { rejected = true; } }); thread.Start(); thread.Join(); Check(rejected);
        owner.StopPlay(); owner.StopPlay(); owner.Catalog.Clear();
    }),
    ("Headless runs without native/Python/Gui and writes bounded reproducible reports", () => {

        using var playerRunOwner0 = new Ncma.Player.App.PlayerRunner();
        using var playerRunOwner1 = new Ncma.Player.App.PlayerRunner();
        Scene(updatesFail: true); var one = playerRunOwner0.Run(Options(), pluginRoot: Path.Combine(output, "no-native")); var two = playerRunOwner1.Run(Options(), pluginRoot: Path.Combine(output, "no-native"));
        Check(one.ExitCode == 0 && one.Tick == 120 && one.Modules.Length == 0 && one.StateSha256 == two.StateSha256);
        var oracleScene = RuntimeFixture(updateFails: true);
        using var oracle = new PlaySession(oracleScene, FrameTimePolicy.Strict, advanceMode: PlayAdvanceMode.FixedSteps);
        oracle.Start(_ => new PlayerProbe());
        for (int i = 0; i < 120; i++) oracle.AdvanceFixedStep();
        Check(one.SchemaVersion == 2 && two.SchemaVersion == 2 &&
            one.StateSha256 == Convert.ToHexString(SHA256.HashData(oracleScene.CaptureCheckpoint().CopyBytes())),
            "Player report schema 2 hashes the actual runtime model, not the authoring document");
        Check(two.ExitCode == 0 && two.RenderedFrames == 0); Scene();
    }),
    ("Shared runtime rejects factory reentry and World mutation before lifecycle", () => {
        var doc = RuntimeFixture(); using var owner = new RuntimeSessionOwner(); owner.LoadGameplay(Path.Combine(projectRoot, "gameplay.dll"), doc);
        byte[] before = doc.CaptureCheckpoint().CopyBytes();
        Reject(() => owner.StartPlay(doc, factory: d => { owner.StartPlay(doc); return new PlaySession(d); }));
        Check(owner.Play is null && before.SequenceEqual(doc.CaptureCheckpoint().CopyBytes()));
        Reject(() => owner.StartPlay(doc, factory: d => { d.World.CreateEntity("Forbidden"); return new PlaySession(d); }));
        Check(owner.Play is null && before.SequenceEqual(doc.CaptureCheckpoint().CopyBytes()));
        owner.StartPlay(doc); owner.StopPlay();
    }),
    ("Runtime composition rejects startup writes and retains failed close ownership for retry",()=>{
        using(var owner=new RuntimeSessionOwner()) {
            var runtimeScene=RuntimeFixture();
            owner.LoadGameplay(Path.Combine(projectRoot,"gameplay.dll"),runtimeScene);byte[] before=runtimeScene.CaptureCheckpoint().CopyBytes();
            Reject(()=>owner.StartPlay(runtimeScene,compose:p=>{p.Scene.World.CreateEntity("Forbidden composition mutation");return null;}));
            Check(owner.Play is null && before.SequenceEqual(runtimeScene.CaptureCheckpoint().CopyBytes()));
            var close=new RetryComposition();owner.StartPlay(runtimeScene,compose:_=>close);
            Reject(owner.StopPlay);Check(owner.Play is {State:PlayState.Stopped} && close.Attempts==1);Reject(owner.Catalog.Clear);
            owner.StopPlay();Check(owner.Play is null && close.Attempts==2);owner.Catalog.Clear();
        }
        using(var owner=new RuntimeSessionOwner()) {
            var runtimeScene=RuntimeFixture(createFails:true);
            owner.LoadGameplay(Path.Combine(projectRoot,"gameplay.dll"),runtimeScene);var close=new RetryComposition();
            bool failed=false;try{owner.StartPlay(runtimeScene,compose:_=>close);}catch(AggregateException){failed=true;}
            Check(failed && owner.Play is not null && close.Attempts==1 && owner.LastStartFailure?.State==PlayState.Faulted);
            Reject(owner.Catalog.Clear);owner.StopPlay();Check(owner.Play is null && close.Attempts==2);
        }
    }),
    ("Fault evidence captured before Stop and private exception message redacted", () => {

        using var playerRunOwner2 = new Ncma.Player.App.PlayerRunner();
        Scene(3); var options = Options(); var report = playerRunOwner2.Run(options);
        Check(report.ExitCode == 5 && report.Tick == 2 && report.Fault!.AttemptTick == 3 && report.State == PlayState.Faulted);
        Check(!File.ReadAllText(options.Report).Contains("private-secret")); Scene();
    }),
    ("Startup lifecycle fault evidence and shutdown failure have explicit exits", () => {

        using var playerRunOwner3 = new Ncma.Player.App.PlayerRunner();
        using var playerRunOwner4 = new Ncma.Player.App.PlayerRunner();
        SaveScene(Fixture(createFails: true)); var startup = playerRunOwner3.Run(Options());
        Check(startup.ExitCode == 5 && startup.Tick == 0 && startup.Fault is not null && startup.State == PlayState.Faulted);
        SaveScene(Fixture(closeFails: true)); var close = playerRunOwner4.Run(Options(1));
        Check(close.ExitCode == 6 && close.Tick == 1 && close.ShutdownErrors.SequenceEqual(new[] { "runtime_shutdown_failed" })); Scene();
    }),
    ("Wall-clock budget stops at safe boundary, not by changing simulated delta", () => {

        using var playerRunOwner5 = new Ncma.Player.App.PlayerRunner();
        var report = playerRunOwner5.Run(Options(2) with { MaxRuntimeSeconds = 1 }, trustedInput: (_, _) => Thread.Sleep(1100));
        Check(report.ExitCode == 8 && report.Tick == 1 && report.FixedDeltaSeconds == 1d / 60);
    }),
    ("Cancellation and exact 32 lifecycle cycles release catalog/resources", () => {

        using var playerRunOwner6 = new Ncma.Player.App.PlayerRunner();
        var cancel = playerRunOwner6.Run(Options(), () => true); Check(cancel.ExitCode == 130 && cancel.Tick == 0);
        for (int i = 0; i < 32; i++) {
            using var playerRunOwner7 = new Ncma.Player.App.PlayerRunner();
            var report = playerRunOwner7.Run(Options(1)); Check(report.ExitCode == 0 && report.Tick == 1 && report.ShutdownErrors.Length == 0); }
    }),
    ("Missing native dependency is explicit, disabled physics succeeds", () => {

        using var playerRunOwner8 = new Ncma.Player.App.PlayerRunner();
        using var playerRunOwner9 = new Ncma.Player.App.PlayerRunner();
        Project(true); var enabled = playerRunOwner8.Run(Options(), pluginRoot: Path.Combine(output, "missing-plugins")); Check(enabled.ExitCode == 3);
        Project(); Check(playerRunOwner9.Run(Options()).ExitCode == 0);
    }),
    ("Unknown bindings/schema reject without losing source assets", () => {

        using var playerRunOwner10 = new Ncma.Player.App.PlayerRunner();
        var doc = Fixture(); Guid item = doc.GetObjectIds()[0]; doc.SetBindings(item, [new(Guid.NewGuid(), "Missing.Behaviour", true, [])]);
        SaveScene(doc); byte[] source = File.ReadAllBytes(scenePath);
        Check(playerRunOwner10.Run(Options()).ExitCode != 0 && source.SequenceEqual(File.ReadAllBytes(scenePath))); Scene();
        Reject(() => { using var owner = new RuntimeSessionOwner(); owner.LoadGameplay(Path.Combine(output, "missing.dll"), RuntimeFixture()); });
    }),
    ("Vulkan refuses instead of silently falling back", () => {

        using var playerRunOwner11 = new Ncma.Player.App.PlayerRunner();
        var options = PlayerOptions.Parse(["--project", projectPath, "--renderer", "vulkan", "--ticks", "1", "--report", Path.Combine(output, "vulkan.json")]);
        var report = playerRunOwner11.Run(options); Check(report.ExitCode == 7 && report.Modules.Length == 0 && report.Tick == 0);
    }),
    ("Headless scene accepts render metadata without resolving or loading 3D assets", () => {

        using var playerRunOwner12 = new Ncma.Player.App.PlayerRunner();
        var doc = new SceneDocument("Null scene", global::Ncma.Runtime.CompositionRegistry.Build(global::Ncma.Scene.Rendering.Generated.ComponentModule.Module), Ncma.Scene.Rendering.SceneRenderValidation.RequireComposition);
        Guid obj = doc.CreateObject("Missing mesh"); doc.SetComponent(obj, TransformData.Identity);
        doc.SetComponent(obj, new Ncma.Scene.Rendering.StaticMeshData(Guid.NewGuid(), Guid.NewGuid(), true, true, uint.MaxValue));
        SaveScene(doc); var report = playerRunOwner12.Run(Options(1), pluginRoot: Path.Combine(output, "no-native"));
        Check(report.ExitCode == 0 && report.Tick == 1 && report.Modules.Length == 0 && report.RenderedFrames == 0);
        // The host must decide demand before preparing an explicitly configured visual package.
        Directory.CreateDirectory(Path.Combine(projectRoot, "assets"));
        File.WriteAllText(Path.Combine(projectRoot, "assets/unused-visual.ncpak"), "intentionally not a runtime package");
        var original = config; config = config with { AssetPackage = "assets/unused-visual.ncpak" }; Project();
        try {
            using var noVisualPackage = new Ncma.Player.App.PlayerRunner();
            var result = noVisualPackage.Run(Options(1), pluginRoot: Path.Combine(output, "no-native"));
            Check(result.ExitCode == 0 && result.Tick == 1 && result.Modules.Length == 0 && result.RenderedFrames == 0);
        } finally { config = original; Project(); }
        Scene();
    }),
    ("Existing report never overwritten and report failure is non-success", () => {

        using var playerRunOwner13 = new Ncma.Player.App.PlayerRunner();
        using var playerRunOwner14 = new Ncma.Player.App.PlayerRunner();
        var options = Options(); File.WriteAllText(options.Report, "keep"); var report = playerRunOwner13.Run(options);
        Check(report.ExitCode != 0 && File.ReadAllText(options.Report) == "keep" && report.ReportError is not null);
        var blocked = Path.Combine(output, "not-a-directory"); File.WriteAllText(blocked, "keep");
        Check(playerRunOwner14.Run(Options(1) with { Report = Path.Combine(blocked, "report.json") }).ExitCode == 9);
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
var harness = new CaseHarness("Player");
foreach (var test in cases) harness.Case(test.Name, () => { test.Run(); passed.Add(test.Name); });
if (args.Length > 2) {
    harness.Case("Simulation composition", () => {SimulationCompositionChecks.Run(root,output,Path.GetFullPath(args[2]));passed.Add("Simulation composition 0/1/8/32, default-off/state oracle/reload/native identity");});
    harness.Case("Action sample", () => {ActionSampleChecks.Run(root,output,Path.GetFullPath(args[2]));passed.Add("K6 generated procedural sample: 0/1 actor packed Headless/DX11, disabled unused physics/GPU and authoring preserved");});
    harness.Case("Joint actions", () => passed.AddRange(ActionJointChecks.Run(root,output,Path.GetFullPath(args[2]))));
    harness.Case("Actions", () => passed.AddRange(ActionCombatChecks.Run(output,Path.GetFullPath(args[2]))));
    harness.Case("Root motion", () => passed.AddRange(RootMotionChecks.Run(root,output,Path.GetFullPath(args[2]))));
    harness.Case("Character host", () => passed.AddRange(CharacterHostChecks.Run(output,Path.GetFullPath(args[2]))));
    harness.Case("Package", () => passed.AddRange(PackageChecks.Run(root,args[1],output,Path.GetFullPath(args[2]))));
    harness.Case("Scene Player", () => passed.AddRange(SceneHostChecks.Run(output,Path.GetFullPath(args[2]))));
    harness.Case("FBX Player", () => passed.AddRange(SkinnedHostChecks.Run(root,output,Path.GetFullPath(args[2]))));
}
int exitCode = harness.Finish();
File.WriteAllBytes(Path.Combine(output, "results.json"), JsonSerializer.SerializeToUtf8Bytes(new { passed = passed.Count, tests = passed,
    caseBoundaries = harness.Total, failed = harness.Failed, exitCode }, json));
return exitCode;

internal sealed class RetryComposition : IDisposable
{
    public int Attempts;
    public void Dispose() { if(++Attempts==1)throw new IOException("Injected close failure"); }
}
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
