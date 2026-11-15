using System.Text;
using Ncma.Editor.Core;
using Ncma.Editor.Services;

// Real directory/log IO with a deliberately SYNTHETIC execution lease. These
// cases do not prove SDK/restore/process identity or real child/job drain.
internal static class WorkflowBuildOutputTests
{
    private static readonly List<Lease> retained = [];
    private static string H => new('A', 64);
    private static void Check(bool value) { if (!value) throw new Exception("M8.6-C2b2 output assertion"); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException) { return; }
        throw new Exception("Expected output rejection");
    }
    private sealed class Clock : TimeProvider
    {
        internal long Ticks;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Ticks;
    }
    private sealed class Lease : IWorkflowBuildLease
    {
        public Guid LeaseId { get; } = Guid.NewGuid();
        public WorkflowVerificationTask Task { get; }
        public WorkflowBuildEnvironment Environment { get; }
        internal WorkflowBuildOutput Output { get; }
        internal WorkflowBuildObservation Observation = new(false, true, null);
        internal WorkflowBuildStartPermit? SavedPermit;
        internal Action<WorkflowBuildStartPermit>? OnStart;
        internal bool ObserveFails;
        internal Lease(string root, string configuration = "Debug", string variant = "default", Guid? job = null, string recipe = "editor-services-build-v1")
        {
            Task = new(new(1, job ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), H, new(Guid.NewGuid(), 1, 2),
                Guid.NewGuid(), H, H, H, null, recipe, configuration, variant, "unverified-fixture-sdk", H),
                [new("tool.exe", 1, H)], recipe == "editor-services-build-v1" ? [] : ["case"]);
            Environment = new(root, @"C:\fixture\dotnet.exe", @"C:\fixture\powershell.exe", H,
                [new("scripts/WorkflowBuildInputs.ps1", 1, H)], TimeSpan.FromSeconds(90));
            Output = new(this);
        }
        public void RequireCurrent() { }
        public void Start(WorkflowBuildStartPermit permit)
        {
            SavedPermit = permit;
            if (OnStart is not null) { OnStart(permit); return; }
            Output.Reserve(permit); Observation = new(true, false, null);
        }
        public void RequestStop() { } // never pretend a signal drains a borrower
        public WorkflowBuildObservation Observe() => ObserveFails ? throw new IOException("fixture-observation") : Observation;
        public void Close() => Output.Close();
    }
    private static Lease New(string parent)
    {
        string root = Path.Combine(parent, "workflow-output-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var lease = new Lease(root); retained.Add(lease); return lease;
    }
    private static WorkflowBuildJobs Queue(Lease lease, TimeProvider? clock = null)
    {
        var jobs = new WorkflowBuildJobs(_ => true, clock); var review = jobs.Review(lease);
        Check(review.CommandFingerprint == lease.Output.Command.Fingerprint);
        jobs.Approve(review, review.Fingerprint, true); return jobs;
    }
    private static void End(WorkflowBuildJobs jobs, Lease lease)
    {
        lease.Observation = lease.Observation with { ExecutionDrained = true, ExitCode = lease.Observation.BuildStarted ? 0 : null };
        jobs.Poll(lease.Task.Binding.JobId); Check(jobs.Close(lease.Task.Binding.JobId)); retained.Remove(lease);
    }
    internal static IEnumerable<(string, Action)> Cases(string output)
    {
        yield return ("M8.6 log sealing requires drain, preserves exact bytes and prevents further append", () => {
            var lease = New(output); var jobs = Queue(lease); Check(jobs.StartNext());
            lease.Output.Append(false, [1, 2, 3]); Reject(() => lease.Output.SealLogs());
            lease.Observation = new(true, true, 0); var logs = lease.Output.SealLogs();
            Check(logs.Length == 2 && logs.Single(l => l.Path == "stdout.log").Bytes == 3 && logs.Single(l => l.Path == "stderr.log").Bytes == 0);
            Check(logs.Single(l => l.Path == "stdout.log").Sha256 == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 1, 2, 3 })));
            Reject(() => lease.Output.Append(false, [4]));
            Reject(() => File.WriteAllBytes(Path.Combine(lease.Output.Command.OutputRoot, "logs", "stdout.log"), [5]));
            logs[0] = new("forged.log", 0, H); Check(lease.Output.SealLogs().All(l => l.Path != "forged.log")); End(jobs, lease);
        });
        yield return ("M8.6-C2b2 fixed compile vector covers both configurations and four variants", () => {
            foreach (string configuration in new[] { "Debug", "Release" }) foreach (string variant in new[] { "default", "ai", "extensions", "ai-extensions" }) {
                var lease = new Lease(@"F:\NcmaEngineRelease\source", configuration, variant); var command = lease.Output.Command;
                string[] args = command.CopyArguments(); Check(args.Length == 18 && args[0] == "build");
                Check(args.Contains("-p:CustomAfterDirectoryBuildProps=" + command.RestoreMappingPath));
                Check(args[1] == Path.Combine(command.CaptureRoot, "source", "managed", "Ncma.Editor.Services.Tests", "Ncma.Editor.Services.Tests.csproj"));
                Check(args[2] == "--no-restore" && args.Contains("-noAutoResponse") && args.Contains("--disable-build-servers") && args.Contains("-warnaserror"));
                Check(args[11] == configuration && args.Contains("-p:ArtifactsPath=" + command.ArtifactsRoot));
                Check(args.Contains("-p:NcmaEnableAI=" + (variant is "ai" or "ai-extensions" ? "true" : "false")));
                Check(args.Contains("-p:NcmaEnableExtensions=" + (variant is "extensions" or "ai-extensions" ? "true" : "false")));
                Check(!args.Contains("restore") && !args.Any(a => a.StartsWith("-bl:", StringComparison.Ordinal)));
                args[0] = "publish"; Check(command.CopyArguments()[0] == "build"); lease.Output.Close();
            }
        });
        yield return ("M8.6-C2b2 command rejects MSBuild path grammar and noncompile recipe", () => {
            foreach (string path in new[] { @"F:\work;Restore=true", @"F:\work%3B", @"F:\work$(X)", @"F:\work@x", @"F:\work=path", @"F:\work\..\elsewhere", @"\\server\share" })
                Reject(() => new Lease(path));
            Reject(() => new Lease(@"F:\NcmaEngineRelease\source", recipe: "workflow-host-v1"));
            Check(WorkflowBuildCommand.CommandPath(@"F:\space allowed\work") == @"F:\space allowed\work");
        });
        yield return ("Workflow environment rejects raw aliases before normalization for every path role", () => {
            foreach (string alias in new[] { @"F:\fixture\..\work", @"F:\fixture\.\work", @"F:\fixture.\work", @"F:\fixture \work" })
            foreach (int role in new[] { 0, 1, 2 }) {
                string[] paths = [@"F:\fixture\workspace", @"C:\fixture\dotnet.exe", @"C:\fixture\powershell.exe"];
                paths[role] = alias;
                Reject(() => new WorkflowBuildEnvironment(paths[0], paths[1], paths[2], H, [new("scripts/WorkflowBuildInputs.ps1", 1, H)], TimeSpan.FromSeconds(90)));
            }
        });
        yield return ("M8.6-C2b2 approval alone does not reserve and explicit start retains logs until drain", () => {
            var lease = New(output); var jobs = Queue(lease); string root = lease.Output.Command.OutputRoot;
            Check(!Directory.Exists(root) && !lease.Output.Reserved); Check(jobs.StartNext()); Check(lease.Output.Reserved);
            byte[] text = Encoding.UTF8.GetBytes("build diagnostic\n"); lease.Output.Append(false, text); lease.Output.Append(true, text);
            Reject(lease.Output.Close); Check(!lease.Output.Closed);
            Reject(() => { using var denied = new FileStream(Path.Combine(root, "logs", "stdout.log"), FileMode.Open, FileAccess.Write, FileShare.ReadWrite); });
            End(jobs, lease); Check(lease.Output.Closed && Directory.Exists(root));
            Check(File.ReadAllBytes(Path.Combine(root, "logs", "stdout.log")).SequenceEqual(text));
            Check(File.ReadAllBytes(Path.Combine(root, "logs", "stderr.log")).SequenceEqual(text)); Check(!jobs.StartNext());
        });
        yield return ("M8.6-C2b2 existing output collision never overwrites or retries", () => {
            var lease = New(output); string root = lease.Output.Command.OutputRoot;
            Directory.CreateDirectory(root); string sentinel = Path.Combine(root, "sentinel.txt"); File.WriteAllText(sentinel, "original");
            var jobs = Queue(lease); Check(jobs.StartNext()); var status = jobs.Poll(lease.Task.Binding.JobId);
            Check(status.Outcome == WorkflowBuildOutcome.LaunchFailed && !status.BuildStarted && !lease.Output.Reserved);
            Check(File.ReadAllText(sentinel) == "original" && !Directory.Exists(Path.Combine(root, "logs")));
            Check(jobs.Close(lease.Task.Binding.JobId)); retained.Remove(lease);
            Reject(() => lease.Output.Reserve(lease.SavedPermit!)); Check(File.ReadAllText(sentinel) == "original");
        });
        yield return ("M8.6-C2b2 foreign expired and escaped start permits cannot create output", () => {
            for (int mode = 0; mode < 3; mode++) {
                var lease = New(output); var other = New(output); var clock = new Clock();
                lease.OnStart = permit => {
                    if (mode == 0) { Reject(() => other.Output.Reserve(permit)); Reject(() => lease.Output.Reserve(permit)); }
                    if (mode == 1) { clock.Ticks += 60000; Reject(() => lease.Output.Reserve(permit)); }
                    // mode 2 deliberately saves the permit without consuming it.
                };
                var jobs = Queue(lease, clock); Check(jobs.StartNext());
                Reject(() => lease.Output.Reserve(lease.SavedPermit!));
                Check(!Directory.Exists(lease.Output.Command.OutputRoot) && !Directory.Exists(other.Output.Command.OutputRoot));
                jobs.Cancel(lease.Task.Binding.JobId); End(jobs, lease); other.Output.Close(); retained.Remove(other);
            }
        });
        yield return ("M8.6-C2b2 log budget is exact per stream and overflow is terminal without truncation", () => {
            var lease = New(output); var jobs = Queue(lease); Check(jobs.StartNext()); byte[] chunk = new byte[65536];
            for (int i = 0; i < WorkflowBuildOutput.MaximumLogBytes / chunk.Length; i++) lease.Output.Append(false, chunk);
            lease.Output.Append(true, [1, 2, 3]); Reject(() => lease.Output.Append(false, [4]));
            Check(lease.Output.LogFailed && lease.Output.StandardOutputBytes == WorkflowBuildOutput.MaximumLogBytes && lease.Output.StandardErrorBytes == 3);
            Reject(() => lease.Output.Append(true, [5])); End(jobs, lease);
            Check(new FileInfo(Path.Combine(lease.Output.Command.OutputRoot, "logs", "stdout.log")).Length == WorkflowBuildOutput.MaximumLogBytes);
            Check(File.ReadAllBytes(Path.Combine(lease.Output.Command.OutputRoot, "logs", "stderr.log")).SequenceEqual(new byte[] { 1, 2, 3 }));
        });
        yield return ("M8.6-C2b2 lost observation retains ownership and owner thread guard remains", () => {
            var lease = New(output); var jobs = Queue(lease); Check(jobs.StartNext());
            System.Threading.Tasks.Task.Run(() => Reject(() => lease.Output.Append(false, [1]))).GetAwaiter().GetResult();
            lease.ObserveFails = true; Reject(lease.Output.Close); Check(!lease.Output.Closed);
            var status = jobs.Poll(lease.Task.Binding.JobId); Check(status.Outcome == WorkflowBuildOutcome.ObservationFailed && status.LeaseRetained);
            lease.ObserveFails = false; End(jobs, lease); Check(Directory.Exists(lease.Output.Command.OutputRoot));
        });
        yield return ("M8.6-C2b2 absent permit and duplicate consumption create no second reservation", () => {
            var noPermit = New(output); Reject(() => noPermit.Output.Reserve(null!)); Check(!Directory.Exists(noPermit.Output.Command.OutputRoot));
            noPermit.Output.Close(); retained.Remove(noPermit);
            var lease = New(output); var second = new WorkflowBuildOutput(lease);
            lease.OnStart = permit => { lease.Output.Reserve(permit); Reject(() => second.Reserve(permit)); lease.Observation = new(true, false, null); };
            var jobs = Queue(lease); Check(jobs.StartNext()); Check(lease.Output.Reserved && !second.Reserved);
            End(jobs, lease); second.Close();
        });
    }
}
